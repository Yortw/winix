#nullable enable
using System;
using System.IO;
using Winix.Url;
using Xunit;
using Yort.ShellKit;

namespace Winix.Url.Tests;

// Round-1 review TA-C1 — Program.cs's per-subcommand exit-code contract previously had
// zero coverage. Cli.Run now lives in the library so every dispatch path can be locked
// here. Round-1 review TA-C2 — also includes the parse|build round-trip property tests
// the test analyzer flagged as missing.
public class CliTests
{
    private static (int exit, string stdout, string stderr) RunCli(params string[] args)
    {
        var stdoutWriter = new StringWriter();
        var stderrWriter = new StringWriter();
        int exit = Cli.Run(args, stdoutWriter, stderrWriter);
        return (exit, stdoutWriter.ToString(), stderrWriter.ToString());
    }

    // ── Per-subcommand happy-path exit codes ──

    [Fact]
    public void Encode_HappyPath_ExitsZero()
    {
        var r = RunCli("encode", "hello world");
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Equal("hello%20world" + Environment.NewLine, r.stdout);
    }

    [Fact]
    public void Decode_HappyPath_ExitsZero()
    {
        var r = RunCli("decode", "hello%20world");
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Equal("hello world" + Environment.NewLine, r.stdout);
    }

    [Fact]
    public void Parse_HappyPath_ExitsZero()
    {
        var r = RunCli("parse", "https://x.io/a?b=1");
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Contains("scheme=https", r.stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HappyPath_ExitsZero()
    {
        var r = RunCli("build", "--host", "x.io", "--path", "/a");
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Equal("https://x.io/a" + Environment.NewLine, r.stdout);
    }

    [Fact]
    public void Join_HappyPath_ExitsZero()
    {
        var r = RunCli("join", "https://x.io/a/", "b");
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Equal("https://x.io/a/b" + Environment.NewLine, r.stdout);
    }

    [Fact]
    public void QueryGet_HappyPath_ExitsZero()
    {
        var r = RunCli("query", "get", "https://x.io/?a=1", "a");
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Equal("1" + Environment.NewLine, r.stdout);
    }

    // ── Per-subcommand failure-path exit codes ──

    [Fact]
    public void Parse_InvalidUrl_ExitsNotExecutable()
    {
        var r = RunCli("parse", "not a url");
        Assert.Equal(ExitCode.NotExecutable, r.exit);
        Assert.Contains("url:", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_FieldWithJson_ExitsUsageError()
    {
        var r = RunCli("parse", "https://x.io/", "--field", "host", "--json");
        Assert.Equal(ExitCode.UsageError, r.exit);
    }

    [Fact]
    public void Parse_UnknownField_ExitsUsageError()
    {
        // ArgumentException from Formatting.Field hits the inner catch in RunParse → UsageError.
        var r = RunCli("parse", "https://x.io/", "--field", "bogus");
        Assert.Equal(ExitCode.UsageError, r.exit);
    }

    [Fact]
    public void Build_MissingHost_ExitsUsageError()
    {
        var r = RunCli("build", "--path", "/a");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("host", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Join_NonAbsoluteBase_ExitsUsageError()
    {
        // "must be absolute" is a UsageError per the substring check in RunJoin — pin it.
        var r = RunCli("join", "/relative/path", "x");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("must be absolute", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Join_OpaqueScheme_ExitsUsageError()
    {
        // CR-I3 — javascript: scheme rejected. The substring "scheme not allowed" routes to UsageError.
        var r = RunCli("join", "javascript:foo", "bar");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("scheme not allowed", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void QueryGet_KeyNotFound_ExitsNotExecutable()
    {
        var r = RunCli("query", "get", "https://x.io/?a=1", "missing");
        Assert.Equal(ExitCode.NotExecutable, r.exit);
    }

    [Fact]
    public void UnknownSubcommand_ExitsUsageError()
    {
        var r = RunCli("encod", "x"); // typo
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("unknown subcommand", r.stderr, StringComparison.Ordinal);
        Assert.Contains("Run 'url --help' for usage.", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void NoSubcommand_ExitsUsageError()
    {
        var r = RunCli();
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("missing subcommand", r.stderr, StringComparison.Ordinal);
    }

    // ── TA-C2 — parse|build round-trip property ──

    [Theory]
    [InlineData("https://x.io/")]
    [InlineData("https://x.io/path/to/resource")]
    [InlineData("https://x.io:8080/api")]
    [InlineData("https://user@x.io/path")]
    [InlineData("https://x.io/?a=1&b=2")]
    [InlineData("https://x.io/path#section")]
    [InlineData("http://[::1]:8080/v6")]
    public void ParseBuild_RoundTripsCanonicalUrls(string input)
    {
        var parsed = UrlParser.Parse(input);
        Assert.True(parsed.Success, $"parse failed for {input}: {parsed.Error}");

        // Re-encode the parsed query (preserving key order + duplicates).
        var queryPairs = new System.Collections.Generic.List<(string, string)>();
        foreach (var (k, v) in parsed.Url!.QueryPairs)
        {
            queryPairs.Add((k, v));
        }

        var built = UrlBuilder.Build(
            parsed.Url.Scheme, parsed.Url.Host, parsed.Url.Port, parsed.Url.Path,
            queryPairs, parsed.Url.Fragment, raw: false, userInfo: parsed.Url.UserInfo);
        Assert.True(built.Success, $"build failed: {built.Error}");

        // Re-parse and assert structural equivalence (string-equality is fragile due to
        // Uri.AbsoluteUri normalisation differences — pin the parsed structure instead).
        var reparsed = UrlParser.Parse(built.Url!);
        Assert.True(reparsed.Success);
        Assert.Equal(parsed.Url.Scheme, reparsed.Url!.Scheme);
        Assert.Equal(parsed.Url.Host, reparsed.Url.Host);
        Assert.Equal(parsed.Url.Port, reparsed.Url.Port);
        Assert.Equal(parsed.Url.Path, reparsed.Url.Path);
        Assert.Equal(parsed.Url.Fragment, reparsed.Url.Fragment);
        Assert.Equal(parsed.Url.UserInfo, reparsed.Url.UserInfo);
    }

    // ── SFH-I3 — decode --strict pin ──

    [Fact]
    public void Decode_LenientByDefault_AcceptsMalformedPercent()
    {
        var r = RunCli("decode", "a%");
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Equal("a%" + Environment.NewLine, r.stdout);
    }

    [Fact]
    public void Decode_Strict_RejectsTrailingPercent()
    {
        var r = RunCli("decode", "a%", "--strict");
        Assert.Equal(ExitCode.NotExecutable, r.exit);
        Assert.Contains("malformed percent-escape", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_Strict_RejectsNonHexAfterPercent()
    {
        var r = RunCli("decode", "a%ZZ", "--strict");
        Assert.Equal(ExitCode.NotExecutable, r.exit);
        Assert.Contains("not valid hex", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_Strict_AcceptsValidPercent()
    {
        var r = RunCli("decode", "hello%20world", "--strict");
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Equal("hello world" + Environment.NewLine, r.stdout);
    }

    [Fact]
    public void Strict_OnEncode_ExitsUsageError()
    {
        var r = RunCli("encode", "x", "--strict");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("--strict only applies to decode", r.stderr, StringComparison.Ordinal);
    }

    // ── SFH-I1/I2 — query duplicate semantics ──

    [Fact]
    public void QueryGet_NoDuplicates_NoWarning()
    {
        var r = RunCli("query", "get", "https://x.io/?a=1", "a");
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Equal("1" + Environment.NewLine, r.stdout);
        Assert.Empty(r.stderr);
    }

    [Fact]
    public void QueryGet_Duplicates_DefaultFirstWinsWithWarning()
    {
        var r = RunCli("query", "get", "https://x.io/?a=1&a=2&a=3", "a");
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Equal("1" + Environment.NewLine, r.stdout);
        // SFH-I2 — warn that there are more values the user isn't seeing.
        Assert.Contains("3 values", r.stderr, StringComparison.Ordinal);
        Assert.Contains("--all", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void QueryGet_All_PrintsEveryValueOnePerLine()
    {
        var r = RunCli("query", "get", "https://x.io/?a=1&a=2&a=3", "a", "--all");
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Equal("1" + Environment.NewLine + "2" + Environment.NewLine + "3" + Environment.NewLine, r.stdout);
        // No first-only warning when --all is set.
        Assert.DoesNotContain("--all", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void QuerySet_NoDuplicates_NoWarning()
    {
        var r = RunCli("query", "set", "https://x.io/?a=1", "a", "9");
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Empty(r.stderr);
    }

    [Fact]
    public void QuerySet_DuplicatesCollapsed_WarnsOnStderr()
    {
        // SFH-I1 — set on duplicates collapses; user must be told.
        var r = RunCli("query", "set", "https://x.io/?a=1&a=2&a=3", "a", "9");
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Contains("collapsed 3 values", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void QueryDelete_DuplicatesRemoved_WarnsOnStderr()
    {
        var r = RunCli("query", "delete", "https://x.io/?a=1&a=2&a=3", "a");
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Contains("deleted 3 occurrences", r.stderr, StringComparison.Ordinal);
    }

    // ── CR-I1 — query key encoding symmetry ──

    [Fact]
    public void QueryGet_EncodedKeyMatchesDecodedKey()
    {
        // The URL contains key "a=b" (encoded as a%3Db on the wire). Both forms of the
        // user-supplied key should match — pre-fix only the decoded form worked.
        var r1 = RunCli("query", "get", "https://x.io/?a%3Db=1", "a=b");
        var r2 = RunCli("query", "get", "https://x.io/?a%3Db=1", "a%3Db");
        Assert.Equal(ExitCode.Success, r1.exit);
        Assert.Equal(ExitCode.Success, r2.exit);
        Assert.Equal("1" + Environment.NewLine, r1.stdout);
        Assert.Equal("1" + Environment.NewLine, r2.stdout);
    }

    // ── CR-I2 — host injection rejected ──

    [Fact]
    public void Build_HostWithSlashAt_RejectsAsUsageError()
    {
        var r = RunCli("build", "--host", "evil.com/@trusted.com");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("URL-component separator", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HostWithFragmentChar_RejectsAsUsageError()
    {
        var r = RunCli("build", "--host", "x.io#frag");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("URL-component separator", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HostIsValidIPv6_Accepted()
    {
        var r = RunCli("build", "--host", "[::1]", "--port", "8080");
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Contains("[::1]:8080", r.stdout, StringComparison.Ordinal);
    }
}
