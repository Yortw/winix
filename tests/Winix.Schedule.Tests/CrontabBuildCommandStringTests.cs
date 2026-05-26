#nullable enable

using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R3 regression pins for CrontabBackend.BuildCommandString — the Linux/macOS analogue of
/// SchtasksBackend.EscapeWindowsArg. R3 test-analyzer flagged this as zero-coverage
/// despite running on every Linux/macOS Add. Without quoting around shell metacharacters,
/// an argument like 'cmd1;rm' would be written into the user's crontab as an injection
/// vector — the cron daemon then runs both commands.
/// </summary>
public sealed class CrontabBuildCommandStringTests
{
    [Fact]
    public void NoArgs_ReturnsCommandUnchanged()
    {
        Assert.Equal("/usr/bin/run", CrontabBackend.BuildCommandString("/usr/bin/run", System.Array.Empty<string>()));
    }

    [Fact]
    public void PlainArgs_NotQuoted()
    {
        Assert.Equal("dotnet build --verbose",
            CrontabBackend.BuildCommandString("dotnet", new[] { "build", "--verbose" }));
    }

    [Fact]
    public void ArgWithSpace_SingleQuoted()
    {
        Assert.Equal("curl 'http://localhost/health check'",
            CrontabBackend.BuildCommandString("curl", new[] { "http://localhost/health check" }));
    }

    [Fact]
    public void ArgWithSingleQuote_UsesBashEscapePattern()
    {
        // Canonical bash apostrophe escape: terminate quote, escaped apostrophe, reopen quote.
        // Input: it's
        // Output: 'it'\''s'
        Assert.Equal(@"echo 'it'\''s'",
            CrontabBackend.BuildCommandString("echo", new[] { "it's" }));
    }

    [Fact]
    public void ArgWithDoubleQuote_SingleQuoted()
    {
        Assert.Equal(@"echo 'say ""hi""'",
            CrontabBackend.BuildCommandString("echo", new[] { @"say ""hi""" }));
    }

    [Fact]
    public void ArgWithDollarSign_SingleQuoted()
    {
        // Without single-quoting, '$HOME' would expand at cron-fire time.
        Assert.Equal("echo '$HOME'",
            CrontabBackend.BuildCommandString("echo", new[] { "$HOME" }));
    }

    [Fact]
    public void ArgWithBackslash_SingleQuoted()
    {
        Assert.Equal(@"echo 'line1\nline2'",
            CrontabBackend.BuildCommandString("echo", new[] { @"line1\nline2" }));
    }

    [Theory]
    [InlineData(";rm -rf /", "';rm -rf /'")]
    [InlineData("cmd1;cmd2", "'cmd1;cmd2'")]
    [InlineData("a&b", "'a&b'")]
    [InlineData("a|b", "'a|b'")]
    [InlineData("a<b", "'a<b'")]
    [InlineData("a>b", "'a>b'")]
    [InlineData("a`b`c", "'a`b`c'")]
    [InlineData("a*b", "'a*b'")]
    [InlineData("a?b", "'a?b'")]
    [InlineData("a~b", "'a~b'")]
    [InlineData("a!b", "'a!b'")]
    [InlineData("a#b", "'a#b'")]
    [InlineData("a(b)", "'a(b)'")]
    [InlineData("a[b]", "'a[b]'")]
    public void ArgWithShellMetachar_SingleQuoted(string input, string expectedQuoted)
    {
        // Each of these characters changes tokenisation, redirection, chaining, command
        // substitution, globbing, or comment behaviour if left unquoted. R1's check missed
        // ;/&/|/etc — R3 widened it. Pin one example per category.
        Assert.Equal($"cmd {expectedQuoted}",
            CrontabBackend.BuildCommandString("cmd", new[] { input }));
    }

    [Fact]
    public void ArgWithTab_SingleQuoted()
    {
        Assert.Equal("cmd 'a\tb'",
            CrontabBackend.BuildCommandString("cmd", new[] { "a\tb" }));
    }

    [Fact]
    public void MixOfQuotedAndUnquotedArgs_PreservedIndividually()
    {
        Assert.Equal("rsync -av '/path with space/' user@host:dest",
            CrontabBackend.BuildCommandString("rsync", new[] { "-av", "/path with space/", "user@host:dest" }));
    }

    // R4 finding: pre-fix the *command* token was concatenated raw — only the args got
    // shell-quoting. A path like '/opt/my app/bin/run' would write a crontab line that
    // cron tokenises as '/opt/my' with args 'app/bin/run', silently launching the wrong
    // (or missing) executable. Asymmetric with the schtasks side which already quotes
    // both — these pins close the asymmetry.

    [Fact]
    public void NoArgs_CommandWithSpace_SingleQuoted()
    {
        Assert.Equal("'/opt/my app/bin/run'",
            CrontabBackend.BuildCommandString("/opt/my app/bin/run", System.Array.Empty<string>()));
    }

    [Fact]
    public void NoArgs_CommandWithSemicolon_SingleQuoted()
    {
        // ;rm here would be a real injection: cron tokenises 'cmd1;rm' as 'cmd1' then '; rm'.
        Assert.Equal("'cmd1;rm'",
            CrontabBackend.BuildCommandString("cmd1;rm", System.Array.Empty<string>()));
    }

    [Fact]
    public void CommandWithSpace_AndArgs_BothQuotedIndependently()
    {
        Assert.Equal("'/opt/my app/bin/run' --once",
            CrontabBackend.BuildCommandString("/opt/my app/bin/run", new[] { "--once" }));
    }

    [Fact]
    public void CommandWithSingleQuote_UsesBashEscapePattern()
    {
        Assert.Equal(@"'bob'\''s-tool'",
            CrontabBackend.BuildCommandString("bob's-tool", System.Array.Empty<string>()));
    }

    [Fact]
    public void CommandWithDollar_SingleQuoted()
    {
        // Without quoting, $PATH would expand at cron-fire time on the user's shell.
        Assert.Equal("'$PATH/run'",
            CrontabBackend.BuildCommandString("$PATH/run", System.Array.Empty<string>()));
    }

    [Theory]
    [InlineData("cmd&", "'cmd&'")]
    [InlineData("cmd|other", "'cmd|other'")]
    [InlineData("cmd>out", "'cmd>out'")]
    [InlineData("`cmd`", "'`cmd`'")]
    public void CommandWithMetachar_SingleQuoted(string input, string expected)
    {
        Assert.Equal(expected,
            CrontabBackend.BuildCommandString(input, System.Array.Empty<string>()));
    }

    [Fact]
    public void PlainCommand_PlainArgs_NoQuotingApplied()
    {
        // Regression pin: the no-special-char path still produces the unquoted shape.
        Assert.Equal("/usr/bin/curl --silent --fail",
            CrontabBackend.BuildCommandString("/usr/bin/curl", new[] { "--silent", "--fail" }));
    }
}
