#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Winix.SecretStore;
using Yort.ShellKit;

namespace Winix.MkAuth;

/// <summary>
/// Library entry point. <c>Program.cs</c> is a thin shim around <see cref="Run"/> so the output
/// shape and error path are unit-testable. The pipeline is: parse args, resolve secret references,
/// dispatch to the scheme builder/signer, guard the computed header against CRLF injection, then
/// route output (header → stdout, warnings/debug → stderr).
/// </summary>
public static class Cli
{
    /// <summary>
    /// Runs the mkauth pipeline and returns a process exit code.
    /// </summary>
    /// <param name="args">The raw CLI argument vector.</param>
    /// <param name="stdout">Destination for the computed header / JSON.</param>
    /// <param name="stderr">Destination for warnings, debug, and errors.</param>
    /// <param name="stdin">Reader consulted for <c>stdin</c> secret refs and <c>--claims-stdin</c>.</param>
    /// <param name="deps">Injectable clock/nonce/secret-store seams; defaults to production seams.</param>
    /// <returns><see cref="ExitCode.Success"/> on success; <see cref="ExitCode.UsageError"/> for bad
    /// arguments; <see cref="ExitCode.NotExecutable"/> for runtime/signing/resolution errors.</returns>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, TextReader stdin, MkAuthDeps? deps = null)
    {
        deps ??= new MkAuthDeps();

        ArgParser.Result r = ArgParser.Parse(args);

        if (r.IsHandled) { return r.HandledExitCode; }
        if (!r.Ok)
        {
            stderr.WriteLine($"mkauth: {r.Error}");
            stderr.WriteLine("Run 'mkauth --help' for usage.");
            return ExitCode.UsageError;
        }

        try
        {
            // Lazily construct the OS-native store only if a vault: ref is actually resolved. Non-vault
            // paths never call Get, so the real keychain is never touched (and never fails on a host
            // without a backend). Tests inject deps.SecretStore directly.
            ISecretStore store = deps.SecretStore ?? new LazySecretStore();
            var resolver = new SecretResolver(store, stdin);
            void Warn(string msg) => stderr.WriteLine($"mkauth: warning: {msg}");
            string Resolve(string reference) => resolver.Resolve(SecretRef.Parse(reference), Warn);

            HeaderResult header;
            string schemeName;

            switch (r.Scheme)
            {
                case AuthScheme.Basic:
                {
                    ArgParser.BasicOptions o = r.Basic!;
                    header = BasicAuthBuilder.Build(o.User, Resolve(o.PasswordRef));
                    schemeName = "basic";
                    break;
                }

                case AuthScheme.Bearer:
                {
                    ArgParser.BearerOptions o = r.Bearer!;
                    header = BearerAuthBuilder.Build(Resolve(o.TokenRef));
                    schemeName = "bearer";
                    break;
                }

                case AuthScheme.OAuth1:
                {
                    ArgParser.OAuth1Options o = r.OAuth1!;
                    OAuth1SignatureMethod sigMethod = ParseSignatureMethod(o.SignatureMethod);

                    // F8: PLAINTEXT signs with the bare key; only safe over TLS. Warn (but still emit) on http://.
                    if (sigMethod == OAuth1SignatureMethod.Plaintext
                        && !o.Url.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
                    {
                        Warn("PLAINTEXT signature over a non-https URL exposes the signing key; use https.");
                    }

                    var req = new OAuth1Request
                    {
                        Method = o.Method,
                        Url = o.Url,
                        ConsumerKey = o.ConsumerKey,
                        ConsumerSecret = Resolve(o.ConsumerSecretRef),
                        Token = o.Token,
                        TokenSecret = o.TokenSecretRef is null ? "" : Resolve(o.TokenSecretRef),
                        SignatureMethod = sigMethod,
                        ExtraParams = ToKvp(o.ExtraParams),
                        Realm = o.Realm,
                        Timestamp = o.Timestamp is not null
                            ? ParseTimestamp(o.Timestamp)
                            : deps.Clock.UtcNow.ToUnixTimeSeconds(),
                        Nonce = o.Nonce ?? deps.Nonce.NextNonce(),
                    };
                    header = OAuth1Signer.Sign(req).Header;
                    schemeName = "oauth1";
                    break;
                }

                case AuthScheme.Jwt:
                {
                    ArgParser.JwtOptions o = r.Jwt!;

                    // F3: stdin can feed EITHER the key OR the claims body, never both — the resolver is
                    // single-use, so this would otherwise surface as a confusing "stdin already consumed".
                    bool keyFromStdin = o.KeyRef is "stdin" or "-";
                    if (keyFromStdin && o.ClaimsStdin)
                    {
                        stderr.WriteLine("mkauth: stdin can supply either the key or the claims body, not both");
                        stderr.WriteLine("Run 'mkauth --help' for usage.");
                        return ExitCode.UsageError;
                    }

                    var req = BuildJwtRequest(o, deps, Resolve, stdin);
                    header = JwtSigner.Sign(req).Header;
                    schemeName = "jwt";
                    break;
                }

                case AuthScheme.AzureStorage:
                {
                    ArgParser.AzureStorageOptions o = r.AzureStorage!;
                    var req = new AzureStorageRequest
                    {
                        Account = o.Account,
                        KeyBase64 = Resolve(o.KeyRef),
                        Method = o.Method,
                        Url = o.Url,
                        XmsDate = o.XMsDate ?? deps.Clock.UtcNow.ToString("R"),
                        XmsVersion = o.XMsVersion ?? "2021-08-06",
                        Headers = ToDictionary(o.Headers),
                    };
                    header = AzureStorageSigner.Sign(req).Header;
                    schemeName = "azure-storage";
                    break;
                }

                default:
                    // Unreachable — ArgParser only produces the schemes above.
                    stderr.WriteLine("mkauth: internal error: unhandled scheme");
                    return ExitCode.NotExecutable;
            }

            // F1: refuse to emit a header whose name or value contains a CR/LF. A resolved secret/token/
            // claim/realm carrying an embedded newline would otherwise let an attacker inject extra
            // headers via the dominant `curl -H "$(mkauth …)"` path. Applies to BOTH plain and --json.
            if (ContainsNewline(header.HeaderName) || ContainsNewline(header.HeaderValue))
            {
                stderr.WriteLine("mkauth: refusing to emit a header containing a newline (possible header injection)");
                return ExitCode.NotExecutable;
            }

            if (r.Json)
            {
                stdout.WriteLine(Formatting.Json(header, schemeName, includeBaseString: r.ShowBaseString));
            }
            else
            {
                stdout.WriteLine(Formatting.Plain(header, r.ValueOnly));
                if (r.ShowBaseString && header.BaseString is not null)
                {
                    stderr.WriteLine(header.BaseString);
                }
            }

            return ExitCode.Success;
        }
        catch (MkAuthException ex)
        {
            // Project-authored, human-readable English (e.g. "Environment variable 'X' is not set.",
            // "Algorithm RS256 needs a PEM private key…"). Safe to surface verbatim — this is the ONLY
            // exception type whose Message we print directly.
            stderr.WriteLine($"mkauth: error: {ex.Message}");
            return ExitCode.NotExecutable;
        }
        catch (IOException)
        {
            // Downstream pipe closed (e.g. `mkauth … | head`). The runtime absorbs most of these at the
            // Console.Out layer; a leaked one is not an error from our perspective.
            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            // Framework exceptions: bad --url (UriFormatException), malformed base64 azure --key
            // (FormatException), bad RS/ES PEM (ArgumentException from ImportFromPem), malformed JSON
            // claims body (JsonException), missing --claims-file (FileNotFoundException), etc. Under
            // UseSystemResourceKeys these .Message values are bare SR resource keys (net_uri_BadFormat,
            // Format_BadBase64Char, Argument_PemImport_NoPemFound…), so route through SafeError.Describe
            // for English (or a readable type name) — never the raw key.
            stderr.WriteLine($"mkauth: error: {SafeError.Describe(ex)}");
            return ExitCode.NotExecutable;
        }
    }

    /// <summary>
    /// Builds the <see cref="JwtRequest"/> from parsed JWT options, applying claim typing (F2) and the
    /// explicit-standard-claim-overrides-string-claim precedence (G2). Standard claims (<c>iss/sub/aud/
    /// exp/nbf/iat</c>) are applied LAST so they overwrite any same-named <c>--claim</c> — and keep their
    /// typed value (numeric for the date claims).
    /// </summary>
    private static JwtRequest BuildJwtRequest(
        ArgParser.JwtOptions o,
        MkAuthDeps deps,
        Func<string, string> resolve,
        TextReader stdin)
    {
        var claims = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Bare --claim → string.
        foreach (var (k, v) in o.Claims)
        {
            claims[k] = v;
        }

        // --claim-num → long (so NumericDate-style claims serialize as JSON numbers). Invalid → a
        // project-authored MkAuthException with readable English (surfaced verbatim by Cli's catch).
        foreach (var (k, v) in o.NumericClaims)
        {
            if (!long.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long n))
            {
                throw new MkAuthException($"--claim-num {k}={v} is not an integer.");
            }
            claims[k] = n;
        }

        // --claim-json → parsed JSON node (number/bool/object/array/string preserved).
        foreach (var (k, v) in o.JsonClaims)
        {
            claims[k] = JsonNode.Parse(v);
        }

        // --claims-file → merge a JSON object.
        if (o.ClaimsFile is not null)
        {
            MergeJsonObject(claims, File.ReadAllText(o.ClaimsFile), "--claims-file");
        }

        // --claims-stdin → merge a JSON object read from stdin (single-use; the F3 arbiter guarantees the
        // key is not also taken from stdin).
        if (o.ClaimsStdin)
        {
            MergeJsonObject(claims, stdin.ReadToEnd(), "--claims-stdin");
        }

        // Standard convenience claims — applied LAST so an explicit flag wins over a same-named --claim,
        // keeping the typed value (G2).
        if (o.Iss is not null) { claims["iss"] = o.Iss; }
        if (o.Sub is not null) { claims["sub"] = o.Sub; }
        if (o.Aud is not null) { claims["aud"] = o.Aud; }

        long now = deps.Clock.UtcNow.ToUnixTimeSeconds();
        if (o.Exp is not null) { claims["exp"] = now + (long)DurationParser.Parse(o.Exp).TotalSeconds; }
        if (o.Nbf is not null) { claims["nbf"] = now + (long)DurationParser.Parse(o.Nbf).TotalSeconds; }
        if (o.Iat) { claims["iat"] = now; }

        var headerParams = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in o.Headers)
        {
            headerParams[k] = v;
        }
        if (o.Kid is not null) { headerParams["kid"] = o.Kid; }

        bool hmac = o.Alg.StartsWith("HS", StringComparison.Ordinal);
        string keyValue = resolve(o.KeyRef);

        return new JwtRequest
        {
            Algorithm = o.Alg,
            Key = hmac ? Encoding.UTF8.GetBytes(keyValue) : null,
            KeyPem = hmac ? null : keyValue,
            Claims = claims,
            HeaderParams = headerParams,
        };
    }

    /// <summary>Parses a JSON object string and merges its top-level members into <paramref name="claims"/>,
    /// preserving JSON types. Throws <see cref="MkAuthException"/> if the content is not a JSON object.
    /// (A malformed JSON body throws a framework <see cref="System.Text.Json.JsonException"/>, which Cli
    /// routes through <see cref="SafeError.Describe"/>.)</summary>
    private static void MergeJsonObject(Dictionary<string, object?> claims, string json, string source)
    {
        JsonNode? node = JsonNode.Parse(json);
        if (node is not JsonObject obj)
        {
            throw new MkAuthException($"{source} must contain a JSON object.");
        }
        foreach (var kv in obj)
        {
            // Detach the node from its current parent before re-homing it into the claims dictionary;
            // JwtSigner re-wraps values, but a JsonNode cannot belong to two parents at once.
            claims[kv.Key] = kv.Value?.DeepClone();
        }
    }

    private static OAuth1SignatureMethod ParseSignatureMethod(string method)
    {
        return method switch
        {
            "HMAC-SHA1" => OAuth1SignatureMethod.HmacSha1,
            "HMAC-SHA256" => OAuth1SignatureMethod.HmacSha256,
            "PLAINTEXT" => OAuth1SignatureMethod.Plaintext,
            // ArgParser validates the set, so this is unreachable in practice.
            _ => throw new MkAuthException($"unknown --signature-method '{method}'."),
        };
    }

    private static long ParseTimestamp(string value)
    {
        if (!long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long ts))
        {
            throw new MkAuthException($"--timestamp '{value}' is not an integer (Unix seconds).");
        }
        return ts;
    }

    private static IReadOnlyList<KeyValuePair<string, string>> ToKvp(IReadOnlyList<(string Key, string Value)> pairs)
    {
        var list = new List<KeyValuePair<string, string>>(pairs.Count);
        foreach (var (k, v) in pairs)
        {
            list.Add(new KeyValuePair<string, string>(k, v));
        }
        return list;
    }

    private static Dictionary<string, string> ToDictionary(IReadOnlyList<(string Key, string Value)> pairs)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs)
        {
            dict[k] = v;
        }
        return dict;
    }

    private static bool ContainsNewline(string s) => s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0;

    /// <summary>
    /// An <see cref="ISecretStore"/> that constructs the real OS-native store on first access. Used so a
    /// non-vault mkauth invocation never touches the keychain (and never fails on a host without a
    /// backend); only a <c>vault:</c> ref calls <see cref="Get"/>, which triggers construction.
    /// </summary>
    private sealed class LazySecretStore : ISecretStore
    {
        private ISecretStore? _inner;

        private ISecretStore Inner => _inner ??= SecretStoreFactory.CreateUserStore();

        public byte[]? Get(string namespace_, string key) => Inner.Get(namespace_, key);

        public void Set(string namespace_, string key, byte[] value) => Inner.Set(namespace_, key, value);

        public bool TryAdd(string namespace_, string key, byte[] value) => Inner.TryAdd(namespace_, key, value);

        public bool Delete(string namespace_, string key) => Inner.Delete(namespace_, key);

        public IReadOnlyList<string> ListKeys(string namespace_) => Inner.ListKeys(namespace_);

        public IReadOnlyList<string> ListNamespaces(string toolPrefix) => Inner.ListNamespaces(toolPrefix);
    }
}
