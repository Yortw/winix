#nullable enable

using System;
using Winix.Retry;
using Xunit;

namespace Winix.Retry.Tests;

/// <summary>
/// Regression tests locking retry's --color emission path at the formatter layer.
/// Guards against a future regression where colour is silently unwired.
/// </summary>
/// <remarks>
/// Seam note: retry has no Cli.Run library seam — Program.cs drives the retry loop
/// directly via process spawning with no injected TextWriter seam.
/// Colour is wired at:
///   Program.Main: bool useColor = result.ResolveColor(checkStdErr: !useStdout)
///   Program.RunWithRetry: onAttempt = (info) => SafeWriteLine(summaryWriter, Formatting.FormatAttempt(info, useColor))
/// Output destination: summaryWriter = Console.Error by default (--stdout routes to Console.Out).
/// Each attempt progress line goes to stderr (or stdout under --stdout).
/// This test suite covers the formatter layer directly — confirming that FormatAttempt
/// emits ESC when useColor=true and suppresses it when false across all terminal stop-reason
/// branches (WillRetry, Succeeded, RetriesExhausted).
/// Production wiring is confirmed by code inspection: useColor is the single variable
/// resolved once in Program.Main then forwarded to every FormatAttempt call via the
/// onAttempt closure. A process-spawn colour regression test would require spawning a
/// real child process that fails, which introduces cross-platform command-availability
/// dependencies unsuitable for unit tests.
/// </remarks>
public sealed class ColorTests
{
    private static readonly string Esc = ((char)27).ToString();

    // ── WillRetry=true branch (attempt failed, more retries remain) ───────────────

    [Fact]
    public void FormatAttempt_WillRetry_ColorTrue_ContainsEscape()
    {
        // This branch emits AnsiColor.Dim (attempt counter), AnsiColor.Red ("failed"),
        // and AnsiColor.Yellow (retry delay).
        var info = new AttemptInfo(
            attempt: 1, maxAttempts: 4, exitCode: 1,
            nextDelay: TimeSpan.FromSeconds(2),
            willRetry: true, stopReason: null);

        string line = Formatting.FormatAttempt(info, useColor: true);

        Assert.Contains(Esc, line, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatAttempt_WillRetry_ColorFalse_NoEscape()
    {
        var info = new AttemptInfo(
            attempt: 1, maxAttempts: 4, exitCode: 1,
            nextDelay: TimeSpan.FromSeconds(2),
            willRetry: true, stopReason: null);

        string line = Formatting.FormatAttempt(info, useColor: false);

        Assert.DoesNotContain(Esc, line, StringComparison.Ordinal);
        // Key content must still be present when colour is off.
        Assert.Contains("attempt 1/4", line, StringComparison.Ordinal);
        Assert.Contains("failed", line, StringComparison.Ordinal);
    }

    // ── Succeeded branch (child exited 0, loop stops) ─────────────────────────────

    [Fact]
    public void FormatAttempt_Succeeded_ColorTrue_ContainsEscape()
    {
        // This branch emits AnsiColor.Green ("succeeded").
        var info = new AttemptInfo(
            attempt: 2, maxAttempts: 4, exitCode: 0,
            nextDelay: null,
            willRetry: false, stopReason: RetryOutcome.Succeeded);

        string line = Formatting.FormatAttempt(info, useColor: true);

        Assert.Contains(Esc, line, StringComparison.Ordinal);
    }

    // ── RetriesExhausted branch ────────────────────────────────────────────────────

    [Fact]
    public void FormatAttempt_RetriesExhausted_ColorTrue_ContainsEscape()
    {
        // This branch emits AnsiColor.Red ("failed").
        var info = new AttemptInfo(
            attempt: 4, maxAttempts: 4, exitCode: 1,
            nextDelay: null,
            willRetry: false, stopReason: RetryOutcome.RetriesExhausted);

        string line = Formatting.FormatAttempt(info, useColor: true);

        Assert.Contains(Esc, line, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatAttempt_RetriesExhausted_ColorFalse_NoEscape()
    {
        var info = new AttemptInfo(
            attempt: 4, maxAttempts: 4, exitCode: 1,
            nextDelay: null,
            willRetry: false, stopReason: RetryOutcome.RetriesExhausted);

        string line = Formatting.FormatAttempt(info, useColor: false);

        Assert.DoesNotContain(Esc, line, StringComparison.Ordinal);
        Assert.Contains("no retries remaining", line, StringComparison.Ordinal);
    }
}
