#nullable enable
using System;
using System.Linq;
using Winix.MkAuth;
using Xunit;

public class ArgParserTests
{
    [Fact]
    public void Unknown_subcommand_is_usage_error()
    {
        var result = ArgParser.Parse(new[] { "frobnicate" });
        Assert.False(result.Ok);
        Assert.Equal(AuthScheme.Basic, default); // sanity: enum exists
    }

    [Fact]
    public void Missing_subcommand_is_usage_error()
    {
        var result = ArgParser.Parse(Array.Empty<string>());
        Assert.False(result.Ok);
        Assert.False(result.IsHandled);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Basic_requires_user_and_password()
    {
        Assert.False(ArgParser.Parse(new[] { "basic", "--user", "bob" }).Ok); // missing --password
        Assert.False(ArgParser.Parse(new[] { "basic", "--password", "env:P" }).Ok); // missing --user

        var r = ArgParser.Parse(new[] { "basic", "--user", "bob", "--password", "env:P" });
        Assert.True(r.Ok);
        Assert.Equal(AuthScheme.Basic, r.Scheme);
        Assert.NotNull(r.Basic);
        Assert.Equal("bob", r.Basic!.User);
        Assert.Equal("env:P", r.Basic!.PasswordRef);
    }

    [Fact] // B5 (DOCS-C1): a basic --user containing ':' is a parse-time usage error (docs say "usage error").
    public void Basic_user_with_colon_is_usage_error()
    {
        var r = ArgParser.Parse(new[] { "basic", "--user", "bad:user", "--password", "literal:p" });
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
        Assert.Contains("colon", r.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ArgumentException", r.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Bearer_requires_token()
    {
        Assert.False(ArgParser.Parse(new[] { "bearer" }).Ok);

        var r = ArgParser.Parse(new[] { "bearer", "--token", "stdin" });
        Assert.True(r.Ok);
        Assert.Equal(AuthScheme.Bearer, r.Scheme);
        Assert.Equal("stdin", r.Bearer!.TokenRef);
    }

    [Fact]
    public void Oauth1_requires_core_flags()
    {
        // Missing --consumer-secret
        Assert.False(ArgParser.Parse(new[] { "oauth1", "--method", "GET", "--url", "https://x/y",
            "--consumer-key", "k" }).Ok);

        var r = ArgParser.Parse(new[] { "oauth1", "--method", "GET", "--url", "https://x/y",
            "--consumer-key", "k", "--consumer-secret", "literal:s" });
        Assert.True(r.Ok);
        Assert.Equal(AuthScheme.OAuth1, r.Scheme);
        Assert.Equal("GET", r.OAuth1!.Method);
        Assert.Equal("https://x/y", r.OAuth1!.Url);
        Assert.Equal("k", r.OAuth1!.ConsumerKey);
        Assert.Equal("literal:s", r.OAuth1!.ConsumerSecretRef);
        Assert.Equal("HMAC-SHA1", r.OAuth1!.SignatureMethod); // default
    }

    [Fact]
    public void Oauth1_rejects_unknown_signature_method()
    {
        var r = ArgParser.Parse(new[] { "oauth1", "--method", "GET", "--url", "https://x/y",
            "--consumer-key", "k", "--consumer-secret", "literal:s", "--signature-method", "BOGUS" });
        Assert.False(r.Ok);
    }

    [Theory]
    [InlineData("HMAC-SHA1")]
    [InlineData("HMAC-SHA256")]
    [InlineData("PLAINTEXT")]
    public void Oauth1_accepts_known_signature_methods(string method)
    {
        var r = ArgParser.Parse(new[] { "oauth1", "--method", "GET", "--url", "https://x/y",
            "--consumer-key", "k", "--consumer-secret", "literal:s", "--signature-method", method });
        Assert.True(r.Ok);
        Assert.Equal(method, r.OAuth1!.SignatureMethod);
    }

    [Fact]
    public void Jwt_requires_key()
    {
        Assert.False(ArgParser.Parse(new[] { "jwt", "--alg", "HS256" }).Ok); // missing --key

        var r = ArgParser.Parse(new[] { "jwt", "--alg", "HS256", "--key", "literal:k" });
        Assert.True(r.Ok);
        Assert.Equal(AuthScheme.Jwt, r.Scheme);
        Assert.Equal("HS256", r.Jwt!.Alg);
        Assert.Equal("literal:k", r.Jwt!.KeyRef);
    }

    [Fact]
    public void Jwt_alg_defaults_to_hs256()
    {
        var r = ArgParser.Parse(new[] { "jwt", "--key", "literal:k" });
        Assert.True(r.Ok);
        Assert.Equal("HS256", r.Jwt!.Alg);
    }

    [Fact]
    public void Jwt_rejects_unknown_alg()
    {
        var r = ArgParser.Parse(new[] { "jwt", "--alg", "ZZ9", "--key", "literal:k" });
        Assert.False(r.Ok);
    }

    [Theory]
    [InlineData("HS256")]
    [InlineData("HS384")]
    [InlineData("HS512")]
    [InlineData("RS256")]
    [InlineData("RS384")]
    [InlineData("RS512")]
    [InlineData("ES256")]
    [InlineData("ES384")]
    [InlineData("ES512")]
    public void Jwt_accepts_all_nine_algs(string alg)
    {
        var r = ArgParser.Parse(new[] { "jwt", "--alg", alg, "--key", "literal:k" });
        Assert.True(r.Ok);
        Assert.Equal(alg, r.Jwt!.Alg);
    }

    [Fact]
    public void Azure_storage_requires_core_flags()
    {
        Assert.False(ArgParser.Parse(new[] { "azure-storage", "--account", "acct", "--method", "GET",
            "--url", "https://acct.blob.core.windows.net/c/b" }).Ok); // missing --key

        var r = ArgParser.Parse(new[] { "azure-storage", "--account", "acct", "--key", "literal:k",
            "--method", "GET", "--url", "https://acct.blob.core.windows.net/c/b" });
        Assert.True(r.Ok);
        Assert.Equal(AuthScheme.AzureStorage, r.Scheme);
        Assert.Equal("acct", r.AzureStorage!.Account);
        Assert.Equal("literal:k", r.AzureStorage!.KeyRef);
    }

    [Fact] // G1: k=v value may contain '=' (split on first only)
    public void Param_value_may_contain_equals()
    {
        var r = ArgParser.Parse(new[] { "oauth1", "--method", "GET", "--url", "https://x/y",
            "--consumer-key", "k", "--consumer-secret", "literal:s", "--param", "state=a=b" });
        Assert.True(r.Ok);
        var (key, value) = r.OAuth1!.ExtraParams.Single();
        Assert.Equal("state", key);
        Assert.Equal("a=b", value);
    }

    [Fact] // G1/F5: k=v with no '=' is a usage error
    public void Param_without_equals_is_usage_error()
    {
        var r = ArgParser.Parse(new[] { "oauth1", "--method", "GET", "--url", "https://x/y",
            "--consumer-key", "k", "--consumer-secret", "literal:s", "--param", "novalue" });
        Assert.False(r.Ok);
        Assert.Contains("novalue", r.Error);
    }

    [Fact] // F5/G1: jwt --header k=v with no '=' is a usage error
    public void Jwt_header_without_equals_is_usage_error()
    {
        var r = ArgParser.Parse(new[] { "jwt", "--alg", "HS256", "--key", "literal:k",
            "--header", "novalue" });
        Assert.False(r.Ok);
    }

    [Fact] // F5/G1: jwt --claim splits on first '=' only and is repeatable
    public void Jwt_claims_split_on_first_equals_and_repeat()
    {
        var r = ArgParser.Parse(new[] { "jwt", "--alg", "HS256", "--key", "literal:k",
            "--claim", "role=admin", "--claim", "next=a=b" });
        Assert.True(r.Ok);
        Assert.Equal(2, r.Jwt!.Claims.Count);
        Assert.Equal(("role", "admin"), r.Jwt!.Claims[0]);
        Assert.Equal(("next", "a=b"), r.Jwt!.Claims[1]);
    }

    [Fact] // F5/G1: azure --header splits on first '=' only (base64 padding can contain '=')
    public void Azure_header_value_may_contain_equals()
    {
        var r = ArgParser.Parse(new[] { "azure-storage", "--account", "a", "--key", "literal:k",
            "--method", "PUT", "--url", "https://a.blob.core.windows.net/c/b",
            "--header", "Content-MD5=q2gT+oN==" });
        Assert.True(r.Ok);
        var (key, value) = r.AzureStorage!.Headers.Single();
        Assert.Equal("Content-MD5", key);
        Assert.Equal("q2gT+oN==", value);
    }

    [Fact] // --header accepts HTTP-style 'name: value' (colon) and trims OWS
    public void Azure_header_accepts_colon_form_and_trims_ows()
    {
        var r = ArgParser.Parse(new[] { "azure-storage", "--account", "a", "--key", "literal:k",
            "--method", "GET", "--url", "https://a.blob.core.windows.net/c/b",
            "--header", "x-ms-meta-color: blue" });
        Assert.True(r.Ok);
        var (key, value) = r.AzureStorage!.Headers.Single();
        Assert.Equal("x-ms-meta-color", key);
        Assert.Equal("blue", value); // split on ':', leading OWS trimmed
    }

    [Fact] // '=' before ':' wins, so an '='-form value containing a colon stays intact
    public void Header_equals_before_colon_keeps_colon_in_value()
    {
        var r = ArgParser.Parse(new[] { "jwt", "--alg", "HS256", "--key", "literal:k",
            "--header", "x5u=https://example/jwks" });
        Assert.True(r.Ok);
        Assert.Equal(("x5u", "https://example/jwks"), r.Jwt!.Headers.Single());
    }

    // B4 (CR-M2): the JOSE header's alg/typ are owned by --alg; a --header override would make the header
    // lie about the signature. Reject EXACT-CASE alg/typ (RFC 7515 §4.1 — JOSE header param names are
    // case-sensitive) as a parse-time usage error. A distinct-cased name (e.g. Alg) is NOT reserved and
    // is accepted as its own header param.
    [Theory]
    [InlineData("alg=HS512")]
    [InlineData("typ=foo")]
    public void Jwt_header_alg_or_typ_is_usage_error(string header)
    {
        var r = ArgParser.Parse(new[] { "jwt", "--alg", "HS256", "--key", "literal:k", "--header", header });
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
        // The message must name the restriction (alg/typ owned by --alg), not leak a framework type.
        Assert.Contains("--alg", r.Error!, StringComparison.Ordinal);
    }

    [Fact] // case-sensitivity policy: Alg is a distinct, non-reserved JOSE param and is accepted.
    public void Jwt_header_distinct_cased_alg_is_accepted()
    {
        var r = ArgParser.Parse(new[] { "jwt", "--alg", "HS256", "--key", "literal:k", "--header", "Alg=HS512" });
        Assert.True(r.Ok);
        Assert.Equal(("Alg", "HS512"), r.Jwt!.Headers.Single());
    }

    [Fact] // invariant: an ordinary JOSE header param (kid) and the colon form still work.
    public void Jwt_header_kid_still_works()
    {
        var r = ArgParser.Parse(new[] { "jwt", "--alg", "HS256", "--key", "literal:k",
            "--header", "kid=abc", "--header", "x5t: thumb" });
        Assert.True(r.Ok);
        Assert.Equal(2, r.Jwt!.Headers.Count);
        Assert.Contains(("kid", "abc"), r.Jwt!.Headers);
        Assert.Contains(("x5t", "thumb"), r.Jwt!.Headers);
    }

    [Fact] // Global flags bind on every scheme.
    public void Global_output_flags_bind()
    {
        var r = ArgParser.Parse(new[] { "bearer", "--token", "literal:t", "--value-only", "--json" });
        Assert.True(r.Ok);
        Assert.True(r.ValueOnly);
        Assert.True(r.Json);
    }

    [Fact] // --show-base-string is a global flag, available on oauth1/azure.
    public void Show_base_string_binds()
    {
        var r = ArgParser.Parse(new[] { "oauth1", "--method", "GET", "--url", "https://x/y",
            "--consumer-key", "k", "--consumer-secret", "literal:s", "--show-base-string" });
        Assert.True(r.Ok);
        Assert.True(r.ShowBaseString);
    }

    [Fact]
    public void Help_is_handled_by_shellkit()
    {
        var r = ArgParser.Parse(new[] { "--help" });
        Assert.True(r.IsHandled);
        Assert.False(r.Ok);
    }

    // B6 (DOCS-I1): top-level --help and --describe must enumerate the five subcommands. ShellKit prints
    // help/describe to Console.Out during Parse, so capture it.
    private static string CaptureConsoleOut(Action action)
    {
        System.IO.TextWriter original = Console.Out;
        var sw = new System.IO.StringWriter();
        try
        {
            Console.SetOut(sw);
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return sw.ToString();
    }

    [Fact]
    public void Help_lists_all_five_subcommands()
    {
        string help = CaptureConsoleOut(() => ArgParser.Parse(new[] { "--help" }));
        Assert.Contains("Subcommands", help, StringComparison.Ordinal);
        foreach (string name in new[] { "basic", "bearer", "oauth1", "jwt", "azure-storage" })
        {
            Assert.Contains(name, help, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Describe_lists_all_five_subcommands()
    {
        string describe = CaptureConsoleOut(() => ArgParser.Parse(new[] { "--describe" }));
        foreach (string name in new[] { "basic", "bearer", "oauth1", "jwt", "azure-storage" })
        {
            Assert.Contains(name, describe, StringComparison.Ordinal);
        }
    }
}
