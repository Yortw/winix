#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Yort.ShellKit;

namespace Winix.Peep.Tests;

/// <summary>
/// End-to-end tests for <see cref="Cli.RunAsync"/> — parse→validate→once-mode with threaded
/// writers and real child processes (no fakes — phase-1 ADR D4 discipline). The interactive
/// path is deliberately out of seam scope (console-bound; enters the alternate buffer) and is
/// never invoked here: every test uses --once or an error path. Colour is also out of seam
/// scope (interactive-only — peep ADR P3).
/// </summary>
public class CliRunAsyncTests
{
    private const string NoSuchCommand = "winix-test-no-such-command-zz9";

    private static async Task<(int Exit, string Stdout, string Stderr)> RunCliAsync(
        string[] args, CancellationToken? token = null)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = await Cli.RunAsync(args, stdout, stderr, token ?? CancellationToken.None);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Child that prints a marker to stdout and exits 0.</summary>
    private static string[] EchoChild(string marker) =>
        OperatingSystem.IsWindows()
            ? new[] { "--once", "--", "cmd.exe", "/c", $"echo {marker}" }
            : new[] { "--once", "--", "/bin/sh", "-c", $"echo {marker}" };

    /// <summary>Child that exits with the given code, no output.</summary>
    private static string[] ExitChild(int code, params string[] flagsBeforeOnce)
    {
        string[] cmd = OperatingSystem.IsWindows()
            ? new[] { "--once", "--", "cmd.exe", "/c", $"exit {code}" }
            : new[] { "--once", "--", "/bin/sh", "-c", $"exit {code}" };
        var all = new string[flagsBeforeOnce.Length + cmd.Length];
        flagsBeforeOnce.CopyTo(all, 0);
        cmd.CopyTo(all, flagsBeforeOnce.Length);
        return all;
    }

    /// <summary>Child that sleeps ~30s (cancellation tests — long child is load-bearing;
    /// 30s rather than 10s so the killed-vs-ran-to-completion gap is unambiguous even on a
    /// saturated CI runner — adversarial-review F2).</summary>
    private static string[] SleepChild(params string[] flagsBeforeOnce)
    {
        string[] cmd = OperatingSystem.IsWindows()
            ? new[] { "--once", "--", "cmd.exe", "/c", "ping -n 30 127.0.0.1 > NUL" }
            : new[] { "--once", "--", "/bin/sh", "-c", "sleep 30" };
        var all = new string[flagsBeforeOnce.Length + cmd.Length];
        flagsBeforeOnce.CopyTo(all, 0);
        cmd.CopyTo(all, flagsBeforeOnce.Length);
        return all;
    }

    // --- Validation → 125 ---

    [Fact]
    public async Task NoCommand_Returns125_PlainErrorOnStderr()
    {
        var r = await RunCliAsync(Array.Empty<string>());
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("no command specified", r.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Theory]
    [InlineData("--interval", "0")]
    [InlineData("--interval", "-1")]
    [InlineData("--interval", "abc")]
    [InlineData("--debounce", "-5")]
    [InlineData("--history", "-1")]
    public async Task InvalidOptionValue_Returns125(string flag, string value)
    {
        var r = await RunCliAsync(new[] { flag, value, "--", "cmd" });
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.NotEqual(string.Empty, r.Stderr);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public async Task BadExitOnMatchRegex_Returns125_PlainError()
    {
        var r = await RunCliAsync(new[] { "--exit-on-match", "[", "--", "cmd" });
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("invalid regex pattern", r.Stderr, StringComparison.Ordinal);
    }

    // --- The --json-output bridging trio (R5 SFH I1 — envelope without --json) ---

    [Fact]
    public async Task JsonOutputBridging_NoCommand_EnvelopeOnStderr()
    {
        var r = await RunCliAsync(new[] { "--json-output" });
        Assert.Equal(ExitCode.UsageError, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public async Task JsonOutputBridging_ParseError_EnvelopeOnStderr()
    {
        var r = await RunCliAsync(new[] { "--json-output", "--interval", "abc", "--", "cmd" });
        Assert.Equal(ExitCode.UsageError, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task JsonOutputBridging_BadRegex_EnvelopeOnStderr()
    {
        var r = await RunCliAsync(new[] { "--json-output", "--exit-on-match", "[", "--", "cmd" });
        Assert.Equal(ExitCode.UsageError, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task JsonProper_NoCommand_EnvelopeViaWriteErrors()
    {
        // Non-bridged counterpart: with --json proper, ShellKit's WriteError emits the envelope.
        var r = await RunCliAsync(new[] { "--json" });
        Assert.Equal(ExitCode.UsageError, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal(ExitCode.UsageError, doc.RootElement.GetProperty("exit_code").GetInt32());
    }

    // --- Once-mode end-to-end ---

    [Fact]
    public async Task Once_ChildOutput_VerbatimOnStdout_ExitZero()
    {
        var r = await RunCliAsync(EchoChild("PEEP-SEAM-MARKER"));
        Assert.Equal(0, r.Exit);
        Assert.Contains("PEEP-SEAM-MARKER", r.Stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stderr);
    }

    [Fact]
    public async Task Once_ChildNonZero_ExitPassthrough()
    {
        var r = await RunCliAsync(ExitChild(7));
        Assert.Equal(7, r.Exit);
    }

    [Fact]
    public async Task Once_Json_EnvelopeOnStderr()
    {
        var r = await RunCliAsync(ExitChild(0, "--json"));
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("once", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("runs").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("history_retained").GetInt32());
    }

    [Fact]
    public async Task Once_JsonOutput_IncludesLastOutput()
    {
        var r = await RunCliAsync(
            OperatingSystem.IsWindows()
                ? new[] { "--json-output", "--once", "--", "cmd.exe", "/c", "echo CAPTURED" }
                : new[] { "--json-output", "--once", "--", "/bin/sh", "-c", "echo CAPTURED" });
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Contains("CAPTURED", doc.RootElement.GetProperty("last_output").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Once_NotFound_Plain_127()
    {
        var r = await RunCliAsync(new[] { "--once", "--", NoSuchCommand });
        Assert.Equal(ExitCode.NotFound, r.Exit);
        Assert.StartsWith("peep: ", r.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public async Task Once_NotFound_Json_Envelope127()
    {
        var r = await RunCliAsync(new[] { "--json", "--once", "--", NoSuchCommand });
        Assert.Equal(ExitCode.NotFound, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("command_not_found", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task Once_EmptyCommandToken_126_NotExecutable()
    {
        // PROBED 2026-06-06 pre-refactor: empty token routes through the typed
        // CommandNotExecutableException arm (NOT the catch-all — retry differs here):
        // plain → `peep: permission denied: `, exit 126.
        var r = await RunCliAsync(new[] { "--once", "--", "" });
        Assert.Equal(ExitCode.NotExecutable, r.Exit);
        Assert.Contains("permission denied", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Once_EmptyCommandToken_Json_Envelope126()
    {
        // PROBED 2026-06-06: {"…","exit_code":126,"exit_reason":"command_not_executable"}.
        var r = await RunCliAsync(new[] { "--json", "--once", "--", "" });
        Assert.Equal(ExitCode.NotExecutable, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("command_not_executable", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    // --- Cancellation (once-mode OperationCanceledException arm → 130) ---
    // JSON-parse safety: in --json once-mode the envelope is the SOLE stderr content —
    // child output (stdout AND stderr, line-merged) goes to the captured Output written to
    // the stdout writer; the cancelled arm writes only the envelope.

    [Fact]
    public async Task Once_PreCancelledToken_130_CancelledEnvelope()
    {
        // Long child is load-bearing (phase-1 lesson): a fast child can finish before the
        // kill-on-cancel fires and the run exits 0 — a real race, not hypothetical.
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int exit = await Cli.RunAsync(SleepChild("--json"), stdout, stderr, cts.Token);
        Assert.Equal(130, exit);
        using var doc = JsonDocument.Parse(stderr.ToString());
        Assert.Equal("cancelled", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task Once_MidWaitCancel_130_ChildKilledPromptly()
    {
        // Exercises kill-on-cancel through the seam token (previously only reachable via
        // real Ctrl+C). Child sleeps ~10s; cancel at 300ms; a working kill path returns
        // far sooner — taking ~10s IS the failure signal.
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(300);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int exit = await Cli.RunAsync(SleepChild("--json"), stdout, stderr, cts.Token);
        sw.Stop();
        Assert.Equal(130, exit);
        using var doc = JsonDocument.Parse(stderr.ToString());
        Assert.Equal("cancelled", doc.RootElement.GetProperty("exit_reason").GetString());
        // Coarse LIVENESS bound, not a perf assertion (adversarial-review F2): it only needs
        // to sit well under the child's 30s sleep so killed-vs-ran-to-completion is
        // unambiguous, with generous headroom for a saturated CI runner.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15),
            $"cancel→kill took {sw.Elapsed} — child was not killed promptly");
    }
}
