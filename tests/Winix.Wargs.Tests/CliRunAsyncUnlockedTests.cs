#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Yort.ShellKit;

namespace Winix.Wargs.Tests;

/// <summary>
/// The previously-IMPOSSIBLE coverage the seam unlocks (ADR W4 stage 2 — added after the
/// move's behaviour-neutrality was validated by Task 3's gates): the cancelled envelope
/// (rounds 4–8's most-litigated contract), input_read_failed via fault injection, and the
/// round-7 cancel-vs-read-failure classification.
/// </summary>
public class CliRunAsyncUnlockedTests
{
    /// <summary>TextReader that yields nothing and throws IOException on every read —
    /// drives the input_read_failed path that previously needed a broken OS pipe.</summary>
    private sealed class ThrowingTextReader : TextReader
    {
        public override int Read() => throw new IOException("synthetic stdin fault");
        public override int Read(char[] buffer, int index, int count) => throw new IOException("synthetic stdin fault");
        public override string? ReadLine() => throw new IOException("synthetic stdin fault");
    }

    /// <summary>Path to a Windows sleep helper batch file, created once per test class in
    /// the temp dir. DECIDED AT PLANNING (adversarial-review F5 — no implementation-time
    /// fork): a generated .cmd beats cmd-parsing cleverness (`&rem` item-swallowing) because
    /// a batch file ignores extra arguments it never references — wargs's appended item is
    /// harmless by construction, no VERIFY needed. The batch sleeps ~30s via ping.</summary>
    private static readonly Lazy<string> WindowsSleepCmd = new(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), $"wargs-seam-sleep-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(path, "@ping -n 30 127.0.0.1 > NUL\r\n");
        return path;
    });

    /// <summary>Command argv whose appended item is ignored, so the child sleeps ~30s
    /// regardless of the item (long child is load-bearing — phase-1 lesson: a fast child
    /// can beat the kill and the 130 assert fails loudly, which is the designed protection;
    /// see the F5 analysis in the review-integration record).</summary>
    private static string[] SleepCommand() =>
        OperatingSystem.IsWindows()
            ? new[] { "--", WindowsSleepCmd.Value }
            : new[] { "--", "/bin/sh", "-c", "sleep 30" };

    private static string[] Concat(string[] head, string[] tail)
    {
        var all = new string[head.Length + tail.Length];
        head.CopyTo(all, 0);
        tail.CopyTo(all, head.Length);
        return all;
    }

    // --- Cancelled envelope (pre-cancelled token; "always at least the envelope") ---

    [Theory]
    [InlineData("--json")]
    [InlineData("--ndjson")]
    public async Task PreCancelledToken_EmitsCancelledEnvelope_130(string mode)
    {
        using var stdin = new StringReader("30\n");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int exit = await Cli.RunAsync(Concat(new[] { mode }, SleepCommand()), stdin, stdout, stderr, cts.Token);
        Assert.Equal(130, exit);
        // The cancelled envelope must be the LAST stderr line (NDJSON may have per-job
        // lines before it in mid-run scenarios; pre-cancelled typically yields just the
        // envelope — parse the last non-empty line to be robust to both).
        string[] lines = stderr.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        using var doc = JsonDocument.Parse(lines[lines.Length - 1]);
        Assert.Equal("cancelled", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(130, doc.RootElement.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public async Task PreCancelledToken_HumanMode_130_NoEnvelope()
    {
        using var stdin = new StringReader("30\n");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int exit = await Cli.RunAsync(SleepCommand(), stdin, stdout, stderr, cts.Token);
        Assert.Equal(130, exit);
        Assert.DoesNotContain("exit_reason", stderr.ToString(), StringComparison.Ordinal);
    }

    // --- Mid-run cancel: kill-on-cancel through JobRunner ---

    [Fact]
    public async Task MidRunCancel_Parallel_KillsAllInFlightChildren_130_Promptly()
    {
        // Adversarial-review F2: a single-job cancel can pass even if the parallel kill
        // fan-out is broken. 4 long children in flight under -P4; the cancel must kill
        // ALL of them for the run to return inside the liveness bound. Self-protecting
        // against a fast-exiting child (broken sleep helper): the run would then complete
        // BEFORE the 300ms cancel and exit 0/123, failing the 130 assert loudly.
        using var stdin = new StringReader("a\nb\nc\nd\n");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(300);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int exit = await Cli.RunAsync(
            Concat(new[] { "--json", "--parallel", "4" }, SleepCommand()), stdin, stdout, stderr, cts.Token);
        sw.Stop();
        Assert.Equal(130, exit);
        string[] lines = stderr.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        using var doc = JsonDocument.Parse(lines[lines.Length - 1]);
        Assert.Equal("cancelled", doc.RootElement.GetProperty("exit_reason").GetString());
        // Coarse LIVENESS bound (not perf): must sit well under the children's ~30s sleep.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15),
            $"cancel→kill took {sw.Elapsed} — in-flight children were not all killed promptly");
    }

    // --- input_read_failed via fault injection ---

    [Theory]
    [InlineData("--json")]
    [InlineData("--ndjson")]
    public async Task ThrowingStdin_InputReadFailed_126_Envelope(string mode)
    {
        using var stdin = new ThrowingTextReader();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = await Cli.RunAsync(new[] { mode, "--", "cmd-irrelevant" }, stdin, stdout, stderr, CancellationToken.None);
        Assert.Equal(ExitCode.NotExecutable, exit);
        using var doc = JsonDocument.Parse(stderr.ToString());
        Assert.Equal("input_read_failed", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task ThrowingStdin_HumanMode_126_DiagnosticWithExceptionType()
    {
        using var stdin = new ThrowingTextReader();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = await Cli.RunAsync(new[] { "--", "cmd-irrelevant" }, stdin, stdout, stderr, CancellationToken.None);
        Assert.Equal(ExitCode.NotExecutable, exit);
        Assert.Contains("failed to read input", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("IOException", stderr.ToString(), StringComparison.Ordinal);
    }

    // --- Round-7 classification: cancel during read is CANCELLED, not input_read_failed ---

    [Fact]
    public async Task ThrowingStdin_WithCancelledToken_ClassifiesAsCancelled_130()
    {
        // Pins the round-7 SFH fix: on Linux a SIGINT can surface as an IOException from a
        // blocked stdin read while the token is already signalled. The materialisation
        // catch must re-check the token and classify as cancelled (130), NOT
        // input_read_failed (126). The seam makes that race deterministically composable:
        // a reader that throws IOException + a token that is already cancelled.
        using var stdin = new ThrowingTextReader();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int exit = await Cli.RunAsync(new[] { "--json", "--", "cmd-irrelevant" }, stdin, stdout, stderr, cts.Token);
        Assert.Equal(130, exit);
        using var doc = JsonDocument.Parse(stderr.ToString());
        Assert.Equal("cancelled", doc.RootElement.GetProperty("exit_reason").GetString());
    }
}
