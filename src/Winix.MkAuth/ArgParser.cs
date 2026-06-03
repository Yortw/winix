#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.MkAuth;

/// <summary>
/// Parses <c>mkauth</c> CLI arguments. The auth scheme is dispatched via positional[0]
/// (the <c>url</c>/<c>schedule</c> subcommand pattern). Pure — no I/O and no secret resolution:
/// secret-bearing flags are kept as raw reference strings (e.g. <c>env:NAME</c>) and resolved later
/// by <c>Cli</c>, which owns stdin and the secret store. ShellKit prints
/// <c>--help/--version/--describe</c> automatically; signalled via <see cref="Result.IsHandled"/>.
/// </summary>
public static class ArgParser
{
    private static readonly string[] SignatureMethods = { "HMAC-SHA1", "HMAC-SHA256", "PLAINTEXT" };

    private static readonly string[] JwtAlgorithms =
    {
        "HS256", "HS384", "HS512",
        "RS256", "RS384", "RS512",
        "ES256", "ES384", "ES512",
    };

    /// <summary>Basic-scheme bound options. Secret reference is left unresolved.</summary>
    public sealed record BasicOptions(string User, string PasswordRef);

    /// <summary>Bearer-scheme bound options. Token reference is left unresolved.</summary>
    public sealed record BearerOptions(string TokenRef);

    /// <summary>OAuth 1.0a bound options. Consumer/token secret references are left unresolved.</summary>
    public sealed record OAuth1Options(
        string Method,
        string Url,
        string ConsumerKey,
        string ConsumerSecretRef,
        string? Token,
        string? TokenSecretRef,
        string SignatureMethod,
        IReadOnlyList<(string Key, string Value)> ExtraParams,
        string? Realm,
        string? Timestamp,
        string? Nonce);

    /// <summary>JWT bound options. Key reference is left unresolved.</summary>
    public sealed record JwtOptions(
        string Alg,
        string KeyRef,
        IReadOnlyList<(string Key, string Value)> Claims,
        IReadOnlyList<(string Key, string Value)> NumericClaims,
        IReadOnlyList<(string Key, string Value)> JsonClaims,
        string? ClaimsFile,
        bool ClaimsStdin,
        string? Iss,
        string? Sub,
        string? Aud,
        string? Exp,
        string? Nbf,
        bool Iat,
        string? Kid,
        IReadOnlyList<(string Key, string Value)> Headers);

    /// <summary>Azure Storage SharedKey bound options. Account-key reference is left unresolved.</summary>
    public sealed record AzureStorageOptions(
        string Account,
        string KeyRef,
        string Method,
        string Url,
        string? XMsDate,
        string? XMsVersion,
        IReadOnlyList<(string Key, string Value)> Headers);

    /// <summary>Parse outcome. <see cref="Ok"/> is true when a scheme's options are populated;
    /// otherwise either <see cref="Error"/> is set, or <see cref="IsHandled"/> is true
    /// (ShellKit already emitted --help/--version/--describe).</summary>
    public sealed record Result(
        bool Ok,
        string? Error,
        bool IsHandled,
        int HandledExitCode,
        bool UseColor,
        AuthScheme Scheme,
        bool ValueOnly,
        bool Json,
        bool ShowBaseString,
        BasicOptions? Basic,
        BearerOptions? Bearer,
        OAuth1Options? OAuth1,
        JwtOptions? Jwt,
        AzureStorageOptions? AzureStorage);

    /// <summary>Parses the argument vector.</summary>
    public static Result Parse(string[] argv)
    {
        var parser = BuildParser();
        var parsed = parser.Parse(argv);
        bool useColor = parsed.ResolveColor(checkStdErr: false);

        Result Fail(string error) => new(
            Ok: false, Error: error, IsHandled: false, HandledExitCode: 0, UseColor: useColor,
            Scheme: default, ValueOnly: false, Json: false, ShowBaseString: false,
            Basic: null, Bearer: null, OAuth1: null, Jwt: null, AzureStorage: null);

        if (parsed.IsHandled)
        {
            return new Result(
                Ok: false, Error: null, IsHandled: true, HandledExitCode: parsed.ExitCode, UseColor: useColor,
                Scheme: default, ValueOnly: false, Json: false, ShowBaseString: false,
                Basic: null, Bearer: null, OAuth1: null, Jwt: null, AzureStorage: null);
        }
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors[0]);
        }

        string[] positionals = parsed.Positionals;
        if (positionals.Length == 0)
        {
            return Fail("missing subcommand (expected: basic, bearer, oauth1, jwt, azure-storage)");
        }

        string subcommand = positionals[0];

        bool valueOnly = parsed.Has("--value-only");
        bool json = parsed.Has("--json");
        bool showBaseString = parsed.Has("--show-base-string");

        Result Success(
            AuthScheme scheme,
            BasicOptions? basic = null,
            BearerOptions? bearer = null,
            OAuth1Options? oauth1 = null,
            JwtOptions? jwt = null,
            AzureStorageOptions? azure = null)
            => new(
                Ok: true, Error: null, IsHandled: false, HandledExitCode: 0, UseColor: useColor,
                Scheme: scheme, ValueOnly: valueOnly, Json: json, ShowBaseString: showBaseString,
                Basic: basic, Bearer: bearer, OAuth1: oauth1, Jwt: jwt, AzureStorage: azure);

        switch (subcommand)
        {
            case "basic":
            {
                if (!parsed.Has("--user"))
                {
                    return Fail("basic requires --user");
                }
                if (!parsed.Has("--password"))
                {
                    return Fail("basic requires --password");
                }
                return Success(AuthScheme.Basic, basic: new BasicOptions(
                    User: parsed.GetString("--user"),
                    PasswordRef: parsed.GetString("--password")));
            }

            case "bearer":
            {
                if (!parsed.Has("--token"))
                {
                    return Fail("bearer requires --token");
                }
                return Success(AuthScheme.Bearer, bearer: new BearerOptions(
                    TokenRef: parsed.GetString("--token")));
            }

            case "oauth1":
            {
                if (!parsed.Has("--method"))
                {
                    return Fail("oauth1 requires --method");
                }
                if (!parsed.Has("--url"))
                {
                    return Fail("oauth1 requires --url");
                }
                if (!parsed.Has("--consumer-key"))
                {
                    return Fail("oauth1 requires --consumer-key");
                }
                if (!parsed.Has("--consumer-secret"))
                {
                    return Fail("oauth1 requires --consumer-secret");
                }

                string signatureMethod = parsed.Has("--signature-method")
                    ? parsed.GetString("--signature-method")
                    : "HMAC-SHA1";
                if (Array.IndexOf(SignatureMethods, signatureMethod) < 0)
                {
                    return Fail($"unknown --signature-method '{signatureMethod}' (expected: HMAC-SHA1, HMAC-SHA256, PLAINTEXT)");
                }

                if (!TrySplitPairs(parsed.GetList("--param"), "--param", out var extraParams, out string? paramError))
                {
                    return Fail(paramError);
                }

                return Success(AuthScheme.OAuth1, oauth1: new OAuth1Options(
                    Method: parsed.GetString("--method"),
                    Url: parsed.GetString("--url"),
                    ConsumerKey: parsed.GetString("--consumer-key"),
                    ConsumerSecretRef: parsed.GetString("--consumer-secret"),
                    Token: parsed.Has("--token") ? parsed.GetString("--token") : null,
                    TokenSecretRef: parsed.Has("--token-secret") ? parsed.GetString("--token-secret") : null,
                    SignatureMethod: signatureMethod,
                    ExtraParams: extraParams,
                    Realm: parsed.Has("--realm") ? parsed.GetString("--realm") : null,
                    Timestamp: parsed.Has("--timestamp") ? parsed.GetString("--timestamp") : null,
                    Nonce: parsed.Has("--nonce") ? parsed.GetString("--nonce") : null));
            }

            case "jwt":
            {
                if (!parsed.Has("--key"))
                {
                    return Fail("jwt requires --key");
                }

                string alg = parsed.Has("--alg") ? parsed.GetString("--alg") : "HS256";
                if (Array.IndexOf(JwtAlgorithms, alg) < 0)
                {
                    return Fail($"unknown --alg '{alg}' (expected: HS256, HS384, HS512, RS256, RS384, RS512, ES256, ES384, ES512)");
                }

                if (!TrySplitPairs(parsed.GetList("--claim"), "--claim", out var claims, out string? claimError))
                {
                    return Fail(claimError);
                }
                if (!TrySplitPairs(parsed.GetList("--claim-num"), "--claim-num", out var numericClaims, out string? numError))
                {
                    return Fail(numError);
                }
                if (!TrySplitPairs(parsed.GetList("--claim-json"), "--claim-json", out var jsonClaims, out string? jsonError))
                {
                    return Fail(jsonError);
                }
                if (!TrySplitPairs(parsed.GetList("--header"), "--header", out var headers, out string? headerError))
                {
                    return Fail(headerError);
                }

                return Success(AuthScheme.Jwt, jwt: new JwtOptions(
                    Alg: alg,
                    KeyRef: parsed.GetString("--key"),
                    Claims: claims,
                    NumericClaims: numericClaims,
                    JsonClaims: jsonClaims,
                    ClaimsFile: parsed.Has("--claims-file") ? parsed.GetString("--claims-file") : null,
                    ClaimsStdin: parsed.Has("--claims-stdin"),
                    Iss: parsed.Has("--iss") ? parsed.GetString("--iss") : null,
                    Sub: parsed.Has("--sub") ? parsed.GetString("--sub") : null,
                    Aud: parsed.Has("--aud") ? parsed.GetString("--aud") : null,
                    Exp: parsed.Has("--exp") ? parsed.GetString("--exp") : null,
                    Nbf: parsed.Has("--nbf") ? parsed.GetString("--nbf") : null,
                    Iat: parsed.Has("--iat"),
                    Kid: parsed.Has("--kid") ? parsed.GetString("--kid") : null,
                    Headers: headers));
            }

            case "azure-storage":
            {
                if (!parsed.Has("--account"))
                {
                    return Fail("azure-storage requires --account");
                }
                if (!parsed.Has("--key"))
                {
                    return Fail("azure-storage requires --key");
                }
                if (!parsed.Has("--method"))
                {
                    return Fail("azure-storage requires --method");
                }
                if (!parsed.Has("--url"))
                {
                    return Fail("azure-storage requires --url");
                }

                if (!TrySplitPairs(parsed.GetList("--header"), "--header", out var headers, out string? headerError))
                {
                    return Fail(headerError);
                }

                return Success(AuthScheme.AzureStorage, azure: new AzureStorageOptions(
                    Account: parsed.GetString("--account"),
                    KeyRef: parsed.GetString("--key"),
                    Method: parsed.GetString("--method"),
                    Url: parsed.GetString("--url"),
                    XMsDate: parsed.Has("--x-ms-date") ? parsed.GetString("--x-ms-date") : null,
                    XMsVersion: parsed.Has("--x-ms-version") ? parsed.GetString("--x-ms-version") : null,
                    Headers: headers));
            }

            default:
                return Fail($"unknown subcommand '{subcommand}' (expected: basic, bearer, oauth1, jwt, azure-storage)");
        }
    }

    /// <summary>
    /// Splits each repeatable <c>k=v</c> token on the FIRST <c>=</c> only — the value may itself
    /// contain further <c>=</c> (e.g. base64 padding, or <c>state=a=b</c>). A token with no
    /// <c>=</c> at all is a usage error (F5/G1). Returns false with <paramref name="error"/> set
    /// on the first malformed token.
    /// </summary>
    private static bool TrySplitPairs(
        string[] tokens,
        string flagName,
        out IReadOnlyList<(string Key, string Value)> pairs,
        out string error)
    {
        var result = new List<(string, string)>(tokens.Length);
        foreach (string token in tokens)
        {
            int eq = token.IndexOf('=');
            if (eq < 0)
            {
                pairs = Array.Empty<(string, string)>();
                error = $"{flagName} expected k=v, got '{token}'";
                return false;
            }
            result.Add((token.Substring(0, eq), token.Substring(eq + 1)));
        }
        pairs = result;
        error = "";
        return true;
    }

    private static CommandLineParser BuildParser()
    {
        return new CommandLineParser("mkauth", ResolveVersion())
            .Description("Compute HTTP Authorization headers (Basic, Bearer, OAuth 1.0a, JWT, Azure Storage SharedKey).")
            .StandardFlags()
            .Platform("cross-platform",
                replaces: new[] { "ad-hoc openssl/base64 signing scripts" },
                valueOnWindows: "No openssl/base64 toolchain assumptions — one self-contained binary computes the signature.",
                valueOnUnix: "Consistent header construction across shells without bespoke openssl pipelines per scheme.")
            .ExitCodes(
                (0, "Success"),
                (ExitCode.UsageError, "Usage error: bad flags, unknown subcommand, missing required flag"),
                (ExitCode.NotExecutable, "Runtime error: secret resolution failed, malformed URL, signing error"))
            .StdinDescription("Reads a secret/key or claims body when a flag uses 'stdin'")
            .StdoutDescription("The computed header line, bare value (--value-only), or JSON (--json)")
            .StderrDescription("Warnings (literal: secret exposure, PLAINTEXT-over-HTTP), --show-base-string debug, errors")
            // Global output flags.
            .Flag("--value-only", "Print the bare header value instead of the full 'Name: value' line")
            .Flag("--json", "Emit a JSON object: { scheme, header_name, header_value, [base_string] }")
            .Flag("--show-base-string", "(oauth1/azure-storage) emit the computed signature base string for debugging")
            // basic
            .Option("--user", null, "NAME", "(basic) username — required")
            .Option("--password", null, "REF", "(basic) password secret reference (env:/file:/vault:/literal:/stdin) — required")
            // bearer
            .Option("--token", null, "REF", "(bearer) token secret reference; (oauth1) oauth_token value")
            // oauth1
            .Option("--method", null, "VERB", "(oauth1/azure-storage) HTTP method — required")
            .Option("--url", null, "URL", "(oauth1/azure-storage) full request URL — required")
            .Option("--consumer-key", null, "K", "(oauth1) oauth_consumer_key — required")
            .Option("--consumer-secret", null, "REF", "(oauth1) consumer secret reference — required")
            .Option("--token-secret", null, "REF", "(oauth1) token secret reference (empty for 2-legged)")
            .Option("--signature-method", null, "M", "(oauth1) HMAC-SHA1 (default), HMAC-SHA256, or PLAINTEXT")
            .ListOption("--param", null, "K=V", "(oauth1) extra/body param folded into the signature base; repeatable")
            .Option("--realm", null, "R", "(oauth1) realm (not part of the signature base)")
            .Option("--timestamp", null, "N", "(oauth1) override oauth_timestamp (default: clock)")
            .Option("--nonce", null, "S", "(oauth1) override oauth_nonce (default: random)")
            // jwt
            .Option("--alg", null, "A", "(jwt) HS256 (default), HS384, HS512, RS256/384/512, ES256/384/512")
            .Option("--key", null, "REF", "(jwt) signing key reference; (azure-storage) account key reference — required")
            .ListOption("--claim", null, "K=V", "(jwt) string claim; repeatable")
            .ListOption("--claim-num", null, "K=V", "(jwt) numeric claim (value parsed as a number); repeatable")
            .ListOption("--claim-json", null, "K=V", "(jwt) raw-JSON claim (value parsed as JSON); repeatable")
            .Option("--claims-file", null, "PATH", "(jwt) JSON object merged into the claim set")
            .Flag("--claims-stdin", "(jwt) merge a JSON object from stdin (exclusive with --key stdin)")
            .Option("--iss", null, "S", "(jwt) iss claim")
            .Option("--sub", null, "S", "(jwt) sub claim")
            .Option("--aud", null, "S", "(jwt) aud claim")
            .Option("--exp", null, "DURATION", "(jwt) exp = now + DURATION")
            .Option("--nbf", null, "DURATION", "(jwt) nbf = now + DURATION")
            .Flag("--iat", "(jwt) set iat to now")
            .Option("--kid", null, "S", "(jwt) kid JOSE header parameter")
            .ListOption("--header", null, "K=V", "(jwt) JOSE header parameter; (azure-storage) extra header; repeatable")
            // azure-storage
            .Option("--account", null, "NAME", "(azure-storage) storage account name — required")
            .Option("--x-ms-date", null, "DATE", "(azure-storage) x-ms-date header value (default: clock, RFC1123 GMT)")
            .Option("--x-ms-version", null, "V", "(azure-storage) x-ms-version header value")
            .JsonField("scheme", "string", "Auth scheme name (basic, bearer, oauth1, jwt, azure-storage)")
            .JsonField("header_name", "string", "Header name (always Authorization)")
            .JsonField("header_value", "string", "The computed header value")
            .JsonField("base_string", "string", "Signature base string (only with --show-base-string)")
            .Example("mkauth basic --user alice --password env:PASSWORD", "Compute a Basic header from an env var")
            .Example("mkauth bearer --token vault:api/token", "Bearer header from a vaulted token")
            .Example("mkauth oauth1 --method GET --url https://api.x.com/1.1/x.json --consumer-key CK --consumer-secret env:CS --token T --token-secret env:TS", "Sign a 3-legged OAuth 1.0a request")
            .Example("mkauth jwt --alg HS256 --key env:JWT_KEY --iss me --exp 1h --value-only", "Mint a short-lived HS256 JWT")
            .Example("curl -H \"$(mkauth oauth1 --method GET --url https://api.x.com/x --consumer-key CK --consumer-secret env:CS)\" https://api.x.com/x", "Pipe the header straight into curl")
            .ComposesWith("curl", "curl -H \"$(mkauth bearer --token env:TOKEN)\" https://api.example.com/", "Use the computed header in a request")
            .ComposesWith("envvault", "envvault api mkauth oauth1 --method GET --url https://api.x.com/x --consumer-key CK --consumer-secret env:CONSUMER_SECRET", "Inject secrets from a vault namespace, then sign")
            .ComposesWith("digest", "printf '%s.%s' \"$ts\" \"$body\" | digest --hmac sha256 --key-stdin --base64", "Generic webhook HMAC (deliberately not a mkauth scheme — compose with digest)");
    }

    private static string ResolveVersion()
    {
        string raw = typeof(ArgParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
