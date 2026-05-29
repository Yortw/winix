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
    public void Write_failure_surfaces_as_NotExecutable()
    {
        // A genuine output write failure (disk full, device removed, redirected-file error) throws
        // IOException from the writer, and the contract maps that to NotExecutable — it is NOT swallowed
        // as success. A *closed downstream pipe* does not throw in production (the runtime absorbs it at
        // the Console.Out layer on both Windows and Linux), so a throwing writer is the real-error path.
        var se = new StringWriter();
        int code = Cli.Run(new[] { "password", "--length", "8", "--quiet" },
            new ThrowingWriter(), se, new SequenceRandom(new byte[64]));
        Assert.Equal(ExitCode.NotExecutable, code);
        Assert.Contains("mksecret: error:", se.ToString());
    }

    [Fact]
    public void Help_lists_json_flag_exactly_once()
    {
        // Regression: CommonShell called StandardFlags() (which already registers --json) and then
        // re-added --json via .Flag(), so --help and --describe listed --json twice. ShellKit writes
        // --help to Console.Out (not the injected writer), so capture it there. No other test touches
        // Console, so the redirect is collision-safe under xUnit parallelism.
        var original = System.Console.Out;
        var sw = new StringWriter();
        try
        {
            System.Console.SetOut(sw);
            Cli.Run(new[] { "--help" }, sw, new StringWriter());
        }
        finally
        {
            System.Console.SetOut(original);
        }

        int jsonCount = sw.ToString().Split(new[] { "--json" }, System.StringSplitOptions.None).Length - 1;
        Assert.Equal(1, jsonCount);
    }

    [Fact]
    public void Json_streams_a_wellformed_envelope_for_multiple_values()
    {
        // Locks the streamed JSON bytes (Cli no longer buffers all values before emitting the envelope).
        // length 2, count 2, all-zero RNG -> alphanumeric[0]='A'; bits = 2*log2(62) = 11.9.
        var rng = new SequenceRandom(new byte[64]);
        var (code, outText, _) = Run(new[] { "password", "--length", "2", "--count", "2", "--json" }, rng);
        Assert.Equal(0, code);
        Assert.Equal("{\"mode\":\"password\",\"bits\":11.9,\"values\":[\"AA\",\"AA\"]}", outText.Trim());
    }
}

/// <summary>TextWriter that throws IOException on every write — simulates a genuine write failure
/// (disk full / device error). A closed downstream pipe does NOT throw in production.</summary>
internal sealed class ThrowingWriter : TextWriter
{
    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
    public override void Write(char value) => throw new IOException("broken pipe");
    public override void Write(string? value) => throw new IOException("broken pipe");
    public override void WriteLine(string? value) => throw new IOException("broken pipe");
}
