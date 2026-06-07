using System.Text;
using Winix.MkAuth;
using Xunit;

public class CliTests
{
    private static (int code, string outp, string err) Run(string[] args, string stdin = "", MkAuthDeps? deps = null)
    {
        var so = new StringWriter(); var se = new StringWriter();
        int code = Cli.Run(args, so, se, new StringReader(stdin), deps);
        return (code, so.ToString(), se.ToString());
    }

    [Fact]
    public void Bearer_from_stdin_prints_full_header()
    {
        var (code, outp, _) = Run(new[] { "bearer", "--token", "stdin" }, stdin: "tok123\n");
        Assert.Equal(0, code);
        Assert.Equal("Authorization: Bearer tok123", outp.Trim());
    }

    [Fact]
    public void Value_only_prints_bare_value()
    {
        var (_, outp, _) = Run(new[] { "bearer", "--token", "literal:t", "--value-only" });
        Assert.Equal("Bearer t", outp.Trim());
    }

    [Fact]
    public void Vault_SecretStoreException_SurfacesMessageVerbatim()
    {
        // A6: a vault: ref whose backend raises SecretStoreException must surface that project-authored
        // English verbatim. Under UseSystemResourceKeys a regression routing it through SafeError.Describe
        // would print "SecretStoreException" instead of the actionable backend text.
        var deps = new MkAuthDeps
        {
            SecretStore = new ThrowingSecretStore(
                new Winix.SecretStore.SecretStoreException("secret-tool store failed (exit 1): collection locked")),
        };
        var (code, outp, err) = Run(new[] { "bearer", "--token", "vault:ns/key" }, deps: deps);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        Assert.Contains("secret-tool store failed (exit 1): collection locked", err);
        Assert.DoesNotContain("SecretStoreException", err);   // verbatim message, not the type name
        Assert.Empty(outp);
    }

    [Fact]
    public void Vault_FrameworkException_RoutesThroughSafeErrorNoResourceKey()
    {
        // A6 INVARIANT: a genuine framework exception from the same vault seam must NOT print verbatim —
        // its .Message is a bare SR key under UseSystemResourceKeys. Routes through SafeError.Describe.
        var deps = new MkAuthDeps
        {
            SecretStore = new ThrowingSecretStore(
                new System.IO.FileNotFoundException("blocked", fileName: "/secret/store.db")),
        };
        var (code, _, err) = Run(new[] { "bearer", "--token", "vault:ns/key" }, deps: deps);

        Assert.Equal(Yort.ShellKit.ExitCode.NotExecutable, code);
        Assert.Contains("no such file", err);                  // SafeError-mapped English
        Assert.DoesNotContain("IO_FileNotFound", err);         // no leaked SR key
    }

    [Fact]
    public void Json_output_shape()
    {
        var (_, outp, _) = Run(new[] { "bearer", "--token", "literal:t", "--json" });
        Assert.Contains("\"scheme\":\"bearer\"", outp);
        Assert.Contains("\"header_value\":\"Bearer t\"", outp);
    }

    [Fact]
    public void Oauth1_is_deterministic_under_fixed_clock_and_nonce()
    {
        var deps = new MkAuthDeps { Clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(1318622958)), Nonce = new FixedNonce("NONCE") };
        var (code, outp, _) = Run(new[] { "oauth1", "--method", "GET", "--url", "https://x/y?a=1",
            "--consumer-key", "ck", "--consumer-secret", "literal:cs" }, deps: deps);
        Assert.Equal(0, code);
        Assert.Contains("oauth_nonce=\"NONCE\"", outp);
        Assert.Contains("oauth_timestamp=\"1318622958\"", outp);
    }

    [Fact]
    public void Literal_secret_warns_on_stderr()
    {
        var (_, _, err) = Run(new[] { "bearer", "--token", "literal:t" });
        Assert.Contains("literal", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_subcommand_is_usage_error_code()
    {
        var (code, _, _) = Run(new[] { "nope" });
        Assert.Equal(125, code); // ShellKit ExitCode.UsageError
    }

    [Fact]
    public void Show_base_string_goes_to_stderr_in_plain_mode()
    {
        var deps = new MkAuthDeps { Clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(1)), Nonce = new FixedNonce("N") };
        var (_, outp, err) = Run(new[] { "oauth1", "--method", "GET", "--url", "https://x/y",
            "--consumer-key", "k", "--consumer-secret", "literal:s", "--show-base-string" }, deps: deps);
        Assert.StartsWith("Authorization:", outp.Trim());
        Assert.Contains("GET&https%3A", err); // base string on stderr, not polluting stdout
    }

    [Theory]                                            // F1: header-injection guard
    [InlineData("tok\r\nX-Evil: 1")]
    [InlineData("tok\nX-Evil: 1")]
    public void Newline_in_computed_header_is_refused(string token)
    {
        var (code, outp, err) = Run(new[] { "bearer", "--token", "literal:" + token });
        Assert.NotEqual(0, code); // ExitCode.Error — pin the exact value against ShellKit.ExitCode at implementation
        Assert.DoesNotContain("X-Evil", outp); // nothing emitted to stdout
        Assert.Contains("newline", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]                                              // A4 (TA-I2): the CRLF guard applies in --json mode too
    public void Newline_in_computed_header_is_refused_in_json_mode()
    {
        var (code, outp, err) = Run(new[] { "bearer", "--token", "literal:tok\r\nX-Evil: 1", "--json" });
        Assert.NotEqual(0, code);
        Assert.True(string.IsNullOrEmpty(outp), "nothing must be emitted to stdout (no partial JSON envelope)");
        Assert.DoesNotContain("X-Evil", outp);
        Assert.Contains("newline", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]                                              // F3: stdin can't be both key and claims body
    public void Jwt_key_stdin_plus_claims_stdin_is_usage_error()
    {
        var (code, _, err) = Run(new[] { "jwt", "--alg", "HS256", "--key", "stdin", "--claims-stdin" }, stdin: "k");
        Assert.Equal(125, code); // ExitCode.UsageError
        Assert.Contains("stdin", err, StringComparison.OrdinalIgnoreCase);
    }

    // B1 integration backstops: the empty-secret rejection wires through Cli to exit 126, and the
    // documented invariants (empty literal still emits; 2-legged oauth1 with no token-secret still signs)
    // survive.

    [Fact]                                              // B1: env set-but-empty → exit 126 naming the source
    public void Bearer_empty_env_secret_exits_126_naming_source()
    {
        Environment.SetEnvironmentVariable("MKAUTH_CLI_EMPTY", "");
        var (code, outp, err) = Run(new[] { "bearer", "--token", "env:MKAUTH_CLI_EMPTY" });
        Assert.Equal(126, code); // ExitCode.NotExecutable
        Assert.True(string.IsNullOrWhiteSpace(outp), "no header should be emitted");
        Assert.Contains("env:MKAUTH_CLI_EMPTY", err, StringComparison.Ordinal);
        Assert.Contains("empty", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]                                              // B1: stdin newline-only → exit 126 (distinct from a value)
    public void Bearer_stdin_newline_only_exits_126()
    {
        var (code, outp, err) = Run(new[] { "bearer", "--token", "stdin" }, stdin: "\n");
        Assert.Equal(126, code);
        Assert.True(string.IsNullOrWhiteSpace(outp));
        Assert.Contains("stdin", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]                                              // B1 invariant: an explicit empty literal: still emits a header
    public void Bearer_empty_literal_still_emits_header()
    {
        var (code, outp, _) = Run(new[] { "bearer", "--token", "literal:", "--value-only" });
        Assert.Equal(0, code);
        Assert.Equal("Bearer", outp.Trim()); // "Bearer " with an empty token
    }

    [Fact]                                              // B1 invariant: 2-legged oauth1 (no --token-secret) still signs
    public void Oauth1_two_legged_without_token_secret_still_signs()
    {
        var deps = new MkAuthDeps { Clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(1)), Nonce = new FixedNonce("N") };
        var (code, outp, _) = Run(new[] { "oauth1", "--method", "GET", "--url", "https://x/y",
            "--consumer-key", "k", "--consumer-secret", "literal:s" }, deps: deps);
        Assert.Equal(0, code);
        Assert.StartsWith("Authorization:", outp.Trim());
    }

    [Fact]                                              // F8: PLAINTEXT over non-HTTPS warns but still emits
    public void Oauth1_plaintext_over_http_warns()
    {
        var deps = new MkAuthDeps { Clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(1)), Nonce = new FixedNonce("N") };
        var (code, outp, err) = Run(new[] { "oauth1", "--signature-method", "PLAINTEXT", "--method", "GET",
            "--url", "http://x/y", "--consumer-key", "k", "--consumer-secret", "literal:s" }, deps: deps);
        Assert.Equal(0, code);
        Assert.StartsWith("Authorization:", outp.Trim());
        Assert.Contains("https", err, StringComparison.OrdinalIgnoreCase); // PLAINTEXT-should-be-HTTPS warning
    }

    [Fact]                                              // F5: malformed --url is a clean error, not an SR-key leak
    public void Oauth1_malformed_url_is_clean_error()
    {
        var (code, _, err) = Run(new[] { "oauth1", "--method", "GET", "--url", "not-a-url",
            "--consumer-key", "k", "--consumer-secret", "literal:s" });
        Assert.NotEqual(0, code);
        Assert.False(string.IsNullOrWhiteSpace(err));
        // The specific leaking SR key under UseSystemResourceKeys was "net_uri_BadFormat".
        Assert.DoesNotContain("net_uri", err, StringComparison.OrdinalIgnoreCase);
        AssertNoSrResourceKey(err);
    }

    [Fact]                                              // M2: malformed --url gets a friendly MkAuthException
    public void Oauth1_malformed_url_gives_friendly_message()
    {
        var (code, _, err) = Run(new[] { "oauth1", "--method", "GET", "--url", "not-a-url",
            "--consumer-key", "k", "--consumer-secret", "literal:s" });
        Assert.NotEqual(0, code);
        Assert.Contains("--url is not a valid absolute URL", err, StringComparison.Ordinal);
        Assert.Contains("not-a-url", err, StringComparison.Ordinal);
        AssertNoSrResourceKey(err);
    }

    [Fact]                                              // M2: malformed azure --url gets the same friendly message
    public void Azure_malformed_url_gives_friendly_message()
    {
        var (code, _, err) = Run(new[] { "azure-storage", "--account", "a", "--key", "literal:bm90LWEtdXJs",
            "--method", "GET", "--url", "not-a-url", "--x-ms-version", "2021-08-06" });
        Assert.NotEqual(0, code);
        Assert.Contains("--url is not a valid absolute URL", err, StringComparison.Ordinal);
        AssertNoSrResourceKey(err);
    }

    [Fact]                                              // M2: azure --key not base64 gets a friendly MkAuthException
    public void Azure_bad_base64_key_gives_friendly_message()
    {
        var (code, _, err) = Run(new[] { "azure-storage", "--account", "a", "--key", "literal:not_base64!!",
            "--method", "GET", "--url", "https://a.blob.core.windows.net/c/b", "--x-ms-version", "2021-08-06" });
        Assert.NotEqual(0, code);
        Assert.Contains("azure-storage --key is not valid base64", err, StringComparison.Ordinal);
        AssertNoSrResourceKey(err);
    }

    [Fact]                                              // azure --key not base64 → framework FormatException
    public void Azure_bad_base64_key_is_clean_error()
    {
        var (code, _, err) = Run(new[] { "azure-storage", "--account", "a", "--key", "literal:not_base64!!",
            "--method", "GET", "--url", "https://a.blob.core.windows.net/c/b", "--x-ms-version", "2021-08-06" });
        Assert.NotEqual(0, code);
        Assert.False(string.IsNullOrWhiteSpace(err));
        // The leaking SR key under UseSystemResourceKeys was "Format_BadBase64Char".
        Assert.DoesNotContain("Format_BadBase64", err, StringComparison.OrdinalIgnoreCase);
        AssertNoSrResourceKey(err);
    }

    [Fact]                                              // RS256 with non-PEM key → framework ArgumentException
    public void Jwt_bad_pem_is_clean_error()
    {
        var (code, _, err) = Run(new[] { "jwt", "--alg", "RS256", "--key", "literal:not-a-pem", "--claim", "sub=x" });
        Assert.NotEqual(0, code);
        Assert.False(string.IsNullOrWhiteSpace(err));
        // The leaking SR keys under UseSystemResourceKeys were "Argument_PemImport_NoPemFound" + "Arg_ParamName_Name".
        Assert.DoesNotContain("Argument_PemImport", err, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Arg_ParamName_Name", err, StringComparison.OrdinalIgnoreCase);
        AssertNoSrResourceKey(err);
    }

    /// <summary>
    /// Asserts the error line after "mkauth: error:" is readable text, not a bare SR resource key.
    /// SR keys are single tokens shaped like <c>Xxx_Yyy_Zzz</c> (underscores, no spaces). SafeError.Describe
    /// yields either an English sentence (with spaces) or a CLR type name (e.g. UriFormatException, no
    /// underscores). Either is acceptable; an underscore-joined token with no spaces is not.
    /// </summary>
    private static void AssertNoSrResourceKey(string err)
    {
        foreach (string raw in err.Split('\n'))
        {
            string line = raw.Trim();
            const string prefix = "mkauth: error:";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string message = line.Substring(prefix.Length).Trim();
            Assert.False(string.IsNullOrWhiteSpace(message)); // must say something

            // An SR-key leak looks like "net_uri_BadFormat" — at least one underscore and no spaces.
            bool looksLikeSrKey = message.IndexOf(' ') < 0 && message.IndexOf('_') >= 0;
            Assert.False(looksLikeSrKey, $"error message looks like a bare SR resource key: '{message}'");
        }
    }

    [Fact]                                              // G2: typed --exp wins over a string --claim exp= AND stays numeric
    public void Explicit_exp_overrides_claim_and_stays_a_number()
    {
        var deps = new MkAuthDeps { Clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(1000)) };
        // --claim exp=123 (string) and --exp 60s (now+60 = 1060 as a number). Explicit wins, as a number.
        var (code, outp, _) = Run(new[] { "jwt", "--alg", "HS256", "--key", "literal:k",
            "--claim", "exp=123", "--exp", "60s", "--value-only" }, deps: deps);
        Assert.Equal(0, code);
        string payload = System.Text.Encoding.UTF8.GetString(B64UrlDecode(outp.Trim().Split('.')[1]));
        Assert.Contains("\"exp\":1060", payload);          // numeric, from --exp
        Assert.DoesNotContain("\"exp\":\"123\"", payload); // string --claim did not win and did not leak
    }

    [Fact]                                              // B4 (CR-M2): --header alg override → usage error 125
    public void Jwt_header_alg_override_is_usage_error_125()
    {
        var (code, _, err) = Run(new[] { "jwt", "--alg", "HS256", "--key", "literal:k", "--header", "alg=HS512" });
        Assert.Equal(125, code);
        Assert.Contains("--alg", err, StringComparison.Ordinal);
    }

    [Fact]                                              // B4: distinct-cased Alg is accepted and lands as a header
    public void Jwt_header_distinct_cased_alg_is_accepted_and_emits()
    {
        var (code, outp, _) = Run(new[] { "jwt", "--alg", "HS256", "--key", "literal:k",
            "--header", "Alg=HS512", "--value-only" });
        Assert.Equal(0, code);
        string jwt = outp.Trim().Split(' ')[1];
        string header = System.Text.Encoding.UTF8.GetString(B64UrlDecode(jwt.Split('.')[0]));
        Assert.Contains("\"Alg\":\"HS512\"", header); // distinct param present
        Assert.Contains("\"alg\":\"HS256\"", header); // the real alg untouched
    }

    [Fact] // regression (round 2): jwt without --alg defaults to HS256 (the docs now rely on this)
    public void Jwt_without_alg_defaults_to_hs256()
    {
        var (code, outp, _) = Run(new[] { "jwt", "--key", "literal:k", "--sub", "x", "--value-only" });
        Assert.Equal(0, code);
        string jwt = outp.Trim().Split(' ')[1]; // "Bearer <jwt>" -> the token
        string header = System.Text.Encoding.UTF8.GetString(B64UrlDecode(jwt.Split('.')[0]));
        Assert.Contains("\"alg\":\"HS256\"", header);
    }

    [Fact]                                              // B5 (DOCS-C1): basic --user with ':' → usage error 125, English, no leaked type
    public void Basic_user_with_colon_is_usage_error_125()
    {
        var (code, outp, err) = Run(new[] { "basic", "--user", "bad:user", "--password", "literal:p" });
        Assert.Equal(125, code);
        Assert.True(string.IsNullOrWhiteSpace(outp));
        Assert.Contains("colon", err, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ArgumentException", err, StringComparison.Ordinal);
        AssertNoSrResourceKey(err);
    }

    [Fact] // regression: -v is not a valid flag (guards against re-introducing the phantom short alias)
    public void Dash_v_is_usage_error()
    {
        var (code, _, _) = Run(new[] { "-v" });
        Assert.Equal(125, code);
    }

    private static byte[] B64UrlDecode(string s)
    {
        string b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }

    private static string MissingPath()
        => Path.Combine(Path.GetTempPath(), "mkauth-no-such-" + Guid.NewGuid().ToString("N") + ".txt");

    [Fact]                                              // FIX 1 (C1): missing file: secret must NOT exit 0 silently
    public void Bearer_missing_file_token_is_nonzero_with_message_and_no_header()
    {
        string missing = MissingPath();
        var (code, outp, err) = Run(new[] { "bearer", "--token", "file:" + missing });
        Assert.NotEqual(0, code);
        Assert.True(string.IsNullOrWhiteSpace(outp), "no header should be emitted to stdout");
        Assert.False(string.IsNullOrWhiteSpace(err), "an error must be reported on stderr");
        Assert.Contains(missing, err, StringComparison.Ordinal); // path named
        Assert.DoesNotContain("Authorization", outp, StringComparison.Ordinal);
        AssertNoSrResourceKey(err);
    }

    [Fact]                                              // FIX 1 (C1): missing --claims-file must NOT exit 0 / wrong JWT
    public void Jwt_missing_claims_file_is_nonzero_and_emits_no_token()
    {
        string missing = MissingPath();
        var (code, outp, err) = Run(new[] { "jwt", "--alg", "HS256", "--key", "literal:k",
            "--claims-file", missing, "--value-only" });
        Assert.NotEqual(0, code); // NOT 0, NOT a silently-wrong JWT
        Assert.True(string.IsNullOrWhiteSpace(outp), "no token should be emitted");
        Assert.Contains(missing, err, StringComparison.Ordinal);
        AssertNoSrResourceKey(err);
    }

    [Fact]                                              // FIX 2: bad --exp duration → friendly message, no SR leak
    public void Jwt_bad_exp_duration_gives_friendly_message()
    {
        var (code, _, err) = Run(new[] { "jwt", "--alg", "HS256", "--key", "literal:k",
            "--exp", "banana", "--value-only" });
        Assert.NotEqual(0, code);
        Assert.Contains("--exp 'banana' is not a valid duration", err, StringComparison.Ordinal);
        Assert.DoesNotContain("FormatException", err, StringComparison.Ordinal);
        AssertNoSrResourceKey(err);
    }
}
