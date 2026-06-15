using System;
using System.Diagnostics;
using System.Threading;
using Xunit;

namespace Winix.ProcessSupervision.Tests;

public class TerminateAtDeadlineTests
{
    // DEFAULT (no --kill-after) coreutils-faithful: signal-only, NO backstop. A child that IGNORES
    // the signal must SURVIVE — this is the negative invariant that pins "we did not kill it".
    [SkippableFact]
    public void TerminateAtDeadline_Unix_DefaultNoKillAfter_SignalIgnored_LeavesChildAlive()
    {
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix signal semantics.");
        if (OperatingSystem.IsWindows()) { return; } // deliberate CA1416 redundancy with Skip.IfNot

        (string cmd, string[] args) = ChildHelpers.IgnoreSignalThenSleepUnix("TERM");
        using Process p = ChildProcessLauncher.Launch(cmd, args);
        Thread.Sleep(500); // sh must install the `trap '' TERM` before the signal arrives, else the
                           // default TERM disposition kills the child and the survives-invariant flakes
        try
        {
            // killAfter == null → default mode: send SIGTERM once, no SIGKILL backstop.
            TerminationOutcome outcome =
                ProcessTreeTerminator.TerminateAtDeadline(p, NativeProcess.SigTerm, killAfter: null);

            Assert.Equal(TerminationOutcome.SignalSentNoGuarantee, outcome);
            // Negative invariant: the TERM-ignoring child is STILL ALIVE shortly after (we did NOT
            // backstop it). Give it a moment; it must not have exited.
            Assert.False(p.WaitForExit(500), "child that ignores TERM must survive the default deadline action");
        }
        finally
        {
            // Cleanup: this test deliberately leaves the child alive, so force-kill the tree now.
            ProcessTreeTerminator.KillTree(p);
        }
    }

    // DEFAULT mode, signal DELIVERED: a child that HANDLES TERM exits itself (proves the signal
    // actually reached it). `sleep & wait` so the trap fires promptly (Plan 2a CI lesson).
    [SkippableFact]
    public void TerminateAtDeadline_Unix_DefaultNoKillAfter_SignalHandled_ChildExitsItself()
    {
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix signal semantics.");
        if (OperatingSystem.IsWindows()) { return; }

        (string cmd, string[] args) = ChildHelpers.TrapSignalThenSleepUnix("TERM", 42);
        using Process p = ChildProcessLauncher.Launch(cmd, args);
        Thread.Sleep(500); // sh must install the trap before the signal arrives, else default TERM
                           // disposition exits 143 not 42 (same CI lesson as ProcessTreeTerminatorTests)

        TerminationOutcome outcome =
            ProcessTreeTerminator.TerminateAtDeadline(p, NativeProcess.SigTerm, killAfter: null);

        Assert.Equal(TerminationOutcome.SignalSentNoGuarantee, outcome);
        Assert.True(p.WaitForExit(5000), "child that handles TERM should exit within the bound");
        Assert.Equal(42, p.ExitCode);
    }

    // --kill-after with a signal-IGNORING child: the SIGKILL backstop reaps it → ConfirmedDead.
    [SkippableFact]
    public void TerminateAtDeadline_Unix_KillAfter_SignalIgnored_BackstopReaps_ConfirmedDead()
    {
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix signal semantics.");
        if (OperatingSystem.IsWindows()) { return; }

        (string cmd, string[] args) = ChildHelpers.IgnoreSignalThenSleepUnix("TERM");
        using Process p = ChildProcessLauncher.Launch(cmd, args);
        Thread.Sleep(500); // sh must install the trap before the signal arrives (CI trap-race; matches
                           // the established ProcessTreeTerminatorTests precedent for signal children)

        TerminationOutcome outcome = ProcessTreeTerminator.TerminateAtDeadline(
            p, NativeProcess.SigTerm, killAfter: TimeSpan.FromMilliseconds(300));

        Assert.Equal(TerminationOutcome.ConfirmedDead, outcome);
        Assert.True(p.HasExited);
    }

    // WINDOWS: no signal model → kill the tree immediately even in default mode → ConfirmedDead.
    [SkippableFact]
    public void TerminateAtDeadline_Windows_KillsImmediately_ConfirmedDead()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows immediate-kill semantics.");
        if (!OperatingSystem.IsWindows()) { return; }

        (string cmd, string[] args) = ChildHelpers.SleepSeconds(120);
        using Process p = ChildProcessLauncher.Launch(cmd, args);

        TerminationOutcome outcome =
            ProcessTreeTerminator.TerminateAtDeadline(p, NativeProcess.SigTerm, killAfter: null);

        Assert.Equal(TerminationOutcome.ConfirmedDead, outcome);
        Assert.True(p.HasExited);
    }
}
