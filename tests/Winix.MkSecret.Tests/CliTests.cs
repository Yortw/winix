using System.IO;
using Winix.MkSecret;
using Xunit;
using Yort.ShellKit;

namespace Winix.MkSecret.Tests;

public class CliTests
{
    private static (int code, string outText, string errText) Run(string[] args, Winix.Codec.ISecureRandom? rng = null)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int code = Cli.Run(args, so, se, rng);
        return (code, so.ToString(), se.ToString());
    }

    [Fact]
    public void Password_writes_secret_to_stdout_and_entropy_to_stderr()
    {
        var rng = new SequenceRandom(new byte[64]); // index 0 each time
        var (code, outText, errText) = Run(new[] { "password", "--length", "8" }, rng);
        Assert.Equal(0, code);
        Assert.Equal("AAAAAAAA", outText.Trim());      // alphanumeric[0]='A'
        Assert.Contains("bits", errText);
    }

    [Fact]
    public void Quiet_suppresses_entropy_note()
    {
        var rng = new SequenceRandom(new byte[64]);
        var (_, _, errText) = Run(new[] { "password", "--length", "4", "--quiet" }, rng);
        Assert.Equal("", errText.Trim());
    }

    [Fact]
    public void Json_emits_envelope_to_stdout_and_nothing_to_stderr()
    {
        var rng = new SequenceRandom(new byte[64]);
        var (code, outText, errText) = Run(new[] { "password", "--length", "4", "--json" }, rng);
        Assert.Equal(0, code);
        Assert.Contains("\"mode\":\"password\"", outText);
        Assert.Equal("", errText.Trim());
    }

    [Fact]
    public void Count_emits_multiple_lines()
    {
        var rng = new SequenceRandom(new byte[64]);
        var (_, outText, _) = Run(new[] { "password", "--length", "2", "--count", "3" }, rng);
        Assert.Equal(3, outText.Trim().Split('\n').Length);
    }

    [Fact]
    public void Usage_error_returns_usage_exit_code()
    {
        var (code, _, errText) = Run(new[] { "password", "--charset", "nope" });
        Assert.Equal(ExitCode.UsageError, code);
        Assert.Contains("mksecret:", errText);
    }

    [Fact]
    public void Csprng_failure_returns_NotExecutable_with_no_secret_or_stacktrace()
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int code = Cli.Run(new[] { "password", "--length", "8" }, so, se, new ThrowingRandom());
        Assert.Equal(ExitCode.NotExecutable, code);
        Assert.Contains("mksecret: error:", se.ToString());
        Assert.DoesNotContain("   at ", se.ToString());   // no leaked stack trace
        Assert.Equal("", so.ToString());                  // no partial secret on stdout
    }

    [Fact]
    public void Broken_pipe_is_swallowed_and_returns_success()
    {
        var se = new StringWriter();
        int code = Cli.Run(new[] { "password", "--length", "8", "--quiet" },
            new ThrowingWriter(), se, new SequenceRandom(new byte[64]));
        Assert.Equal(ExitCode.Success, code);
    }
}

/// <summary>TextWriter that throws IOException on every write — simulates a closed downstream pipe.</summary>
internal sealed class ThrowingWriter : TextWriter
{
    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
    public override void Write(char value) => throw new IOException("broken pipe");
    public override void Write(string? value) => throw new IOException("broken pipe");
    public override void WriteLine(string? value) => throw new IOException("broken pipe");
}
