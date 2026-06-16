#nullable enable

using System;
using System.IO;
using System.Threading;
using Winix.ProcessSupervision;
using Xunit;
using Yort.ShellKit;

namespace Winix.RunFor.Tests;

public class CliRunTests
{
    private static int Run(string[] args, out string outText, out string errText, IChildStarter? starter = null)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int code = Cli.Run(args, so, se, CancellationToken.None, starter);
        outText = so.ToString();
        errText = se.ToString();
        return code;
    }

    [Fact]
    public void Describe_HandledByParser_ZeroExit()
    {
        // ShellKit writes --describe to Console.Out (not the injected stdout TextWriter).
        // Redirect Console.Out to capture the JSON; verify tool name and exit-0 contract.
        var capture = new StringWriter();
        TextWriter original = Console.Out;
        int code;
        string json;
        try
        {
            Console.SetOut(capture);
            code = Run(new[] { "--describe" }, out _, out _);
            json = capture.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal(0, code);
        Assert.Contains("\"tool\":\"runfor\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void NoArgs_UsageError()
    {
        int code = Run(Array.Empty<string>(), out _, out string err);
        Assert.Equal(ExitCode.UsageError, code);
        Assert.Contains("DURATION", err, StringComparison.Ordinal);
    }

    [Fact]
    public void BadDuration_UsageError()
    {
        int code = Run(new[] { "notaduration", "--", "echo", "hi" }, out _, out string err);
        Assert.Equal(ExitCode.UsageError, code);
        Assert.Contains("duration", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DurationButNoCommand_UsageError()
    {
        int code = Run(new[] { "5s" }, out _, out string err);
        Assert.Equal(ExitCode.UsageError, code);
        Assert.Contains("command", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownSignal_UsageError()
    {
        int code = Run(new[] { "--signal", "BOGUS", "5s", "--", "echo", "hi" }, out _, out string err);
        Assert.Equal(ExitCode.UsageError, code);
        Assert.Contains("signal", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChildExitsInTime_ForwardsCode_ViaFakeStarter()
    {
        var child = new FakeChild { ExitsWithinDeadline = true, FakeExitCode = 3 };
        int code = Run(new[] { "10s", "--", "x" }, out _, out _, new FakeChildStarter(child));
        Assert.Equal(3, code);
    }

    [Fact]
    public void Deadline_Returns124_AndJsonEnvelope()
    {
        var child = new FakeChild { ExitsWithinDeadline = false };
        int code = Run(new[] { "10s", "--json", "--", "x" }, out _, out string err, new FakeChildStarter(child));
        Assert.Equal(SupervisionExitCode.Timeout, code);
        Assert.Contains("\"timed_out\":true", err, StringComparison.Ordinal); // --json envelope goes to stderr (stdout stays clean)
    }

    // F1: --kill-after 0 must ESCALATE (TimeSpan.Zero), NOT degrade to the signal-only default (null).
    [Fact]
    public void KillAfterZero_Escalates_NotSignalOnlyDefault()
    {
        var child = new FakeChild { ExitsWithinDeadline = false };
        Run(new[] { "--kill-after", "0s", "5s", "--", "x" }, out _, out _, new FakeChildStarter(child));
        Assert.Equal(TimeSpan.Zero, child.LastKillAfter); // a value, not null → escalation mode
    }

    // F2: "1000000h" parses to a valid TimeSpan (1,000,000 hours ≈ 41,666 days, well within
    // TimeSpan.MaxValue of ~10,675,199 days), so DurationParser returns true and Cli proceeds.
    // The FakeChild exits immediately, so the outcome is a clean completion → exit 0.
    [Fact]
    public void HugeDuration_DoesNotOverflow()
    {
        var child = new FakeChild { ExitsWithinDeadline = true, FakeExitCode = 0 };
        int code = Run(new[] { "1000000h", "--", "x" }, out _, out _, new FakeChildStarter(child));
        Assert.Equal(0, code); // tightened: DurationParser accepts it, FakeChild exits 0
    }

    // F6 (strengthen): a clean completion in non-JSON mode is SILENT on stderr.
    [Fact]
    public void ChildExitsInTime_NonJson_StderrIsSilent()
    {
        var child = new FakeChild { ExitsWithinDeadline = true, FakeExitCode = 0 };
        Run(new[] { "10s", "--", "x" }, out _, out string err, new FakeChildStarter(child));
        Assert.Equal(string.Empty, err.Trim());
    }

    // F7: --json on a clean completion → envelope on stderr, stdout STAYS EMPTY (child owns stdout).
    [Fact]
    public void CleanCompletion_Json_EnvelopeOnStderr_StdoutEmpty()
    {
        var child = new FakeChild { ExitsWithinDeadline = true, FakeExitCode = 0 };
        Run(new[] { "--json", "10s", "--", "x" }, out string outText, out string err, new FakeChildStarter(child));
        Assert.Equal(string.Empty, outText);
        Assert.Contains("\"outcome\":\"completed\"", err, StringComparison.Ordinal);
    }

    // F8: --color never suppresses ANSI in the timeout notice at the Cli seam.
    [Fact]
    public void TimeoutNotice_ColorNever_NoAnsi()
    {
        var child = new FakeChild { ExitsWithinDeadline = false };
        Run(new[] { "--color", "never", "5s", "--", "x" }, out _, out string err, new FakeChildStarter(child));
        Assert.DoesNotContain(((char)27).ToString(), err, StringComparison.Ordinal);
    }

    // Launch failure (plain): the classified reason text + forwarded 127 are produced at the Cli seam,
    // not just the runner — pins the user-facing message a failed launch prints.
    [Fact]
    public void CommandNotFound_Plain_Returns127_ReasonOnStderr()
    {
        var starter = new ThrowingChildStarter(new CommandNotFoundException("x"));
        int code = Run(new[] { "5s", "--", "x" }, out _, out string err, starter);
        Assert.Equal(ExitCode.NotFound, code);
        Assert.Contains("command not found", err, StringComparison.OrdinalIgnoreCase);
    }

    // Launch failure (--json): the envelope carries outcome launch_failed on stderr.
    [Fact]
    public void CommandNotFound_Json_LaunchFailedEnvelopeOnStderr()
    {
        var starter = new ThrowingChildStarter(new CommandNotFoundException("x"));
        int code = Run(new[] { "--json", "5s", "--", "x" }, out string outText, out string err, starter);
        Assert.Equal(ExitCode.NotFound, code);
        Assert.Equal(string.Empty, outText);
        Assert.Contains("\"outcome\":\"launch_failed\"", err, StringComparison.Ordinal);
    }

    // Broad catch: an unexpected orchestration exception surfaces as a readable one-liner INCLUDING the
    // type name (AOT-safe diagnostic, never a flattened resource key) and exits 126 — not a crash.
    [Fact]
    public void UnexpectedOrchestrationError_Returns126_TypeNameOnStderr()
    {
        var starter = new ThrowingChildStarter(new InvalidOperationException("boom"));
        int code = Run(new[] { "5s", "--", "x" }, out _, out string err, starter);
        Assert.Equal(ExitCode.NotExecutable, code);
        Assert.Contains("InvalidOperationException", err, StringComparison.Ordinal);
    }

    // Launch failure (plain): the not-EXECUTABLE branch of ExitReasonText (126) — the runner-level test
    // pins the code, this pins the user-facing Cli message text. (Fresh-eyes review TA-C4.)
    [Fact]
    public void CommandNotExecutable_Plain_Returns126_ReasonOnStderr()
    {
        var starter = new ThrowingChildStarter(new CommandNotExecutableException("x"));
        int code = Run(new[] { "5s", "--", "x" }, out _, out string err, starter);
        Assert.Equal(ExitCode.NotExecutable, code);
        Assert.Contains("not executable", err, StringComparison.OrdinalIgnoreCase);
    }

    // Ctrl+C at the Cli seam (plain): a pre-cancelled token → 130 + "interrupted" notice on stderr.
    // The runner/formatter cover the decision + the string; THIS pins the Cli wiring (the "unit-green
    // but caller-unwired" class). (Fresh-eyes review TA-C1.)
    [Fact]
    public void Interrupted_Plain_Returns130_NoticeOnStderr()
    {
        var child = new FakeChild { ExitsWithinDeadline = false };
        var so = new StringWriter();
        var se = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        int code = Cli.Run(new[] { "10s", "--", "x" }, so, se, cts.Token, new FakeChildStarter(child));

        Assert.Equal(SupervisionExitCode.Interrupted, code);
        Assert.Contains("interrupted", se.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // Ctrl+C at the Cli seam (--json): pre-cancelled token → 130 + interrupted envelope on STDERR,
    // stdout stays empty. (Fresh-eyes review TA-C1.)
    [Fact]
    public void Interrupted_Json_Returns130_EnvelopeOnStderr_StdoutEmpty()
    {
        var child = new FakeChild { ExitsWithinDeadline = false };
        var so = new StringWriter();
        var se = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        int code = Cli.Run(new[] { "--json", "10s", "--", "x" }, so, se, cts.Token, new FakeChildStarter(child));

        Assert.Equal(SupervisionExitCode.Interrupted, code);
        Assert.Equal(string.Empty, so.ToString());
        Assert.Contains("\"outcome\":\"interrupted\"", se.ToString(), StringComparison.Ordinal);
    }

    // A non-default --signal must be PARSED and THREADED through to the runner (a regression that
    // dropped the parsed value back to the default would be silent). (Fresh-eyes review TA-C2.)
    [Fact]
    public void NonDefaultSignal_ThreadedToRunner()
    {
        var child = new FakeChild { ExitsWithinDeadline = false };
        Run(new[] { "--signal", "INT", "5s", "--", "x" }, out _, out _, new FakeChildStarter(child));
        Assert.Equal(2, child.LastSignal); // SIGINT, not the default SIGTERM (15)
    }

    // ...and the parsed signal name is reflected in the --json envelope (not hardcoded TERM).
    // (Fresh-eyes review TA-I1.)
    [Fact]
    public void NonDefaultSignal_ReflectedInJsonEnvelope()
    {
        var child = new FakeChild { ExitsWithinDeadline = false };
        Run(new[] { "--signal", "INT", "--json", "5s", "--", "x" }, out _, out string err, new FakeChildStarter(child));
        Assert.Contains("\"signal\":\"INT\"", err, StringComparison.Ordinal);
    }

    // --kill-after with an unparseable duration → usage error 125 mentioning the flag. The --signal
    // reject path was covered; this completes the arg-validation matrix. (Fresh-eyes review TA-C3.)
    [Fact]
    public void BadKillAfter_UsageError()
    {
        int code = Run(new[] { "--kill-after", "notaduration", "5s", "--", "x" }, out _, out string err);
        Assert.Equal(ExitCode.UsageError, code);
        Assert.Contains("kill-after", err, StringComparison.OrdinalIgnoreCase);
    }
}
