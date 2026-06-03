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

    [Fact]                                              // F3: stdin can't be both key and claims body
    public void Jwt_key_stdin_plus_claims_stdin_is_usage_error()
    {
        var (code, _, err) = Run(new[] { "jwt", "--alg", "HS256", "--key", "stdin", "--claims-stdin" }, stdin: "k");
        Assert.Equal(125, code); // ExitCode.UsageError
        Assert.Contains("stdin", err, StringComparison.OrdinalIgnoreCase);
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
        Assert.DoesNotContain("_Name", err); // no bare SR resource key (e.g. Arg_ParamName_Name)
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

    private static byte[] B64UrlDecode(string s)
    {
        string b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }
}
