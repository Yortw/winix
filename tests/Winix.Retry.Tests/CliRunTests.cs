#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Xunit;
using Yort.ShellKit;

namespace Winix.Retry.Tests;

/// <summary>
/// End-to-end tests for <see cref="Cli.Run"/> — full parse→validate→retry-loop→summary-routing
/// path. Per ADR D4 (seam-retrofit design) there is NO spawner fake: failure paths use a
/// nonexistent command (fully deterministic — the spawn never succeeds), happy paths spawn a
/// trivial real child. Child-passthrough and Ctrl+C remain covered by ProgramMainTests + smokes.
/// </summary>
public class CliRunTests
{
    private static readonly string Esc = ((char)27).ToString();
    private const string NoSuchCommand = "winix-test-no-such-command-zz9";

    private static (int Exit, string Stdout, string Stderr) RunCli(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Cli.Run(args, stdout, stderr, CancellationToken.None);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Platform-conditional argv for a child that exits with <paramref name="code"/> quickly.</summary>
    private static string[] ExitWith(int code) =>
        OperatingSystem.IsWindows()
            ? new[] { "cmd.exe", "/c", $"exit {code}" }
            : new[] { "/bin/sh", "-c", $"exit {code}" };

    private static string[] Concat(string[] head, string[] tail)
    {
        var all = new string[head.Length + tail.Length];
        head.CopyTo(all, 0);
        tail.CopyTo(all, head.Length);
        return all;
    }

    // --- Usage / validation errors (no child ever spawned) ---

    [Fact]
    public void NoCommand_Returns125()
    {
        var r = RunCli();
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("no command specified", r.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Theory]
    [InlineData("--times", "abc")]
    [InlineData("--times", "-1")]
    [InlineData("--delay", "fortnight")]
    [InlineData("--backoff", "sideways")]
    [InlineData("--on", "1,x")]
    [InlineData("--until", "")]
    public void InvalidOptionValue_Returns125_ErrorOnStderr(string flag, string value)
    {
        var r = RunCli(flag, value, NoSuchCommand);
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains($"invalid {flag} value", r.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public void OnAndUntilCombined_Returns125()
    {
        var r = RunCli("--on", "1", "--until", "0", NoSuchCommand);
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("cannot be combined", r.Stderr, StringComparison.Ordinal);
    }

    // --- Launch failure (deterministic, spawn never succeeds) ---

    [Fact]
    public void CommandNotFound_Plain_Returns127_ErrorOnStderr()
    {
        var r = RunCli(NoSuchCommand);
        Assert.Equal(ExitCode.NotFound, r.Exit);
        Assert.Contains(NoSuchCommand, r.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public void CommandNotFound_Json_EnvelopeOnStderrByDefault()
    {
        var r = RunCli("--json", NoSuchCommand);
        Assert.Equal(ExitCode.NotFound, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("launch_failed", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("child_exit_code").ValueKind);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public void CommandNotFound_JsonWithStdoutFlag_EnvelopeOnStdout()
    {
        var r = RunCli("--json", "--stdout", NoSuchCommand);
        Assert.Equal(ExitCode.NotFound, r.Exit);
        using var doc = JsonDocument.Parse(r.Stdout);
        Assert.Equal("launch_failed", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    // --- Real-child paths ---

    [Fact]
    public void ChildSucceeds_ExitZero_JsonReportsSingleAttempt()
    {
        var r = RunCli(Concat(new[] { "--json" }, ExitWith(0)));
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("succeeded", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("attempts").GetInt32());
    }

    [Fact]
    public void RetriesExhausted_PassesThroughChildExitCode()
    {
        var r = RunCli(Concat(new[] { "--times", "1", "--delay", "1ms" }, ExitWith(7)));
        Assert.Equal(7, r.Exit);
        Assert.Contains("no retries remaining", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void UntilMatch_PassesThroughChildExitCode_NotZero()
    {
        // Pins the empirically-verified contract (probe 2026-06-06): --until match passes the
        // child code through; only exit_reason says "succeeded". The man page previously
        // claimed exit 0 here — fixed in Task 1 of this plan.
        var r = RunCli(Concat(new[] { "--until", "7", "--times", "0" }, ExitWith(7)));
        Assert.Equal(7, r.Exit);
        Assert.Contains("matched target", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void StdoutFlag_ProgressLinesRouteToStdout_ChildFailure()
    {
        var r = RunCli(Concat(new[] { "--stdout", "--times", "0" }, ExitWith(7)));
        Assert.Equal(7, r.Exit);
        Assert.Contains("no retries remaining", r.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("no retries remaining", r.Stderr, StringComparison.Ordinal);
    }

    // --- Colour wiring ---

    [Fact]
    public void ColorAlways_ProgressLineCarriesAnsi()
    {
        var r = RunCli(Concat(new[] { "--color=always", "--times", "0" }, ExitWith(7)));
        Assert.Contains(Esc, r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ColorNever_NoAnsiAnywhere()
    {
        var r = RunCli(Concat(new[] { "--color=never", "--times", "0" }, ExitWith(7)));
        Assert.DoesNotContain(Esc, r.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(Esc, r.Stdout, StringComparison.Ordinal);
    }

    // --- Empty command token → top-level catch-all (adversarial-review F4 + F5) ---

    [Fact]
    public void EmptyCommandToken_HitsCatchAll_Returns126()
    {
        // Probed against the pre-refactor binary 2026-06-06: `retry ""` →
        //   stderr: "retry: unexpected error: InvalidOperationException: FileNameMissing"
        //   exit:   126
        // ProcessStartInfo.FileName="" throws InvalidOperationException, which is NOT a typed
        // launch failure (CommandNotFound/NotExecutable), so it escapes RunWithRetry into the
        // top-level catch-all. This is the only deterministic catch-all trigger reachable
        // through the public seam — it pins both the exit code and the error-on-stderr contract
        // after the catch-all's move into Cli.Run. (The bare "FileNameMissing" resource key is
        // the accepted broad-catch minimum — type name present — so the assertion stays loose.)
        var r = RunCli("");
        Assert.Equal(ExitCode.NotExecutable, r.Exit);
        Assert.Contains("unexpected error", r.Stderr, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", r.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stdout);
    }

    // --- Cancellation ---
    // JSON-parse-of-stderr safety (adversarial-review pass-2 G1, closed by code-read): the
    // progress callback is constructed ONLY when !jsonOutput (`if (!jsonOutput) { onAttempt = … }`
    // in RunWithRetry), so under --json the final envelope is the SOLE stderr content — no
    // progress line can precede it, and JsonDocument.Parse(stderr) is safe on every --json path
    // including cancellation. If a test fails with JsonException here, that gating regressed.

    /// <summary>Platform-conditional argv for a child that sleeps several seconds.</summary>
    private static string[] SleepChild() =>
        OperatingSystem.IsWindows()
            ? new[] { "cmd.exe", "/c", "ping -n 10 127.0.0.1 > NUL" }
            : new[] { "/bin/sh", "-c", "sleep 10" };

    [Fact]
    public void PreCancelledToken_KillsFirstAttempt_ReportsCancelled()
    {
        // Contract pinned by code-read 2026-06-06: RetryRunner "always runs at least once" —
        // a pre-cancelled token still spawns attempt 1; the kill registration fires at
        // Register time (token already signalled), the child is killed, and the outcome is
        // labelled cancelled. A LONG child is load-bearing here: with a fast child (exit 0)
        // the child can finish BEFORE the kill and the run exits 0 — a real race, not a
        // hypothetical. The sleep child guarantees the kill wins.
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int exit = Cli.Run(Concat(new[] { "--json" }, SleepChild()), stdout, stderr, cts.Token);
        using var doc = JsonDocument.Parse(stderr.ToString());
        Assert.Equal("cancelled", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.NotEqual(0, exit); // kill exit code is platform-dependent (-1 Windows, 137-class Unix) — assert nonzero only
    }

    [Fact]
    public void MidWaitCancel_KillsChild_ReturnsPromptly()
    {
        // Adversarial-review F2: exercises the kill-registration → WaitForExitAsync-cancel →
        // grace-window path that was previously untestable (needed real Ctrl+C; the seam's
        // CancellationToken makes it drivable). The child sleeps ~10s; cancel fires at 300ms;
        // a working kill path returns in well under the sleep duration. If this test takes
        // ~10s, the cancel→kill chain is broken — that slowness IS the failure signal.
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(300);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int exit = Cli.Run(Concat(new[] { "--json" }, SleepChild()), stdout, stderr, cts.Token);
        sw.Stop();
        using var doc = JsonDocument.Parse(stderr.ToString());
        Assert.Equal("cancelled", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.NotEqual(0, exit);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8),
            $"cancel→kill took {sw.Elapsed} — child was not killed promptly");
    }
}
