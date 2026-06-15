using System;
using System.Threading;
using Winix.ProcessSupervision;
using Xunit;
using Yort.ShellKit;

namespace Winix.RunFor.Tests;

public class RunForRunnerTests
{
    private static RunForOptions Opts(TimeSpan? killAfter = null) =>
        new(TimeSpan.FromSeconds(5), UnixSignal.DefaultSignal, killAfter);

    [Fact]
    public void ChildExitsInTime_ForwardsCode_NotTimeout_NoTerminate()
    {
        var child = new FakeChild { ExitsWithinDeadline = true, FakeExitCode = 7 };
        var starter = new FakeChildStarter(child);

        RunForResult r = RunForRunner.Execute(starter, "x", Array.Empty<string>(), Opts(), CancellationToken.None);

        Assert.Equal(RunForOutcome.Completed, r.Outcome);
        Assert.Equal(7, r.ExitCode);
        Assert.NotEqual(SupervisionExitCode.Timeout, r.ExitCode); // negative invariant: NOT 124
        Assert.Equal(0, child.TerminateCallCount);                // invariant: no kill on clean exit
        Assert.True(child.Disposed);
    }

    [Fact]
    public void DeadlineFires_Returns124_TerminatesWithConfiguredSignalAndKillAfter()
    {
        var child = new FakeChild { ExitsWithinDeadline = false };
        var starter = new FakeChildStarter(child);
        TimeSpan grace = TimeSpan.FromSeconds(3);

        RunForResult r = RunForRunner.Execute(starter, "x", Array.Empty<string>(), Opts(grace), CancellationToken.None);

        Assert.Equal(RunForOutcome.TimedOut, r.Outcome);
        Assert.Equal(SupervisionExitCode.Timeout, r.ExitCode);
        Assert.Equal(1, child.TerminateCallCount);
        Assert.Equal(UnixSignal.DefaultSignal, child.LastSignal);
        Assert.Equal(grace, child.LastKillAfter);   // --kill-after threaded through
    }

    [Fact]
    public void DeadlineFires_KillFailed_SurfacesWarningFlag()
    {
        var child = new FakeChild { ExitsWithinDeadline = false, TerminateResult = TerminationOutcome.KillFailed };
        var starter = new FakeChildStarter(child);

        RunForResult r = RunForRunner.Execute(starter, "x", Array.Empty<string>(), Opts(), CancellationToken.None);

        Assert.Equal(SupervisionExitCode.Timeout, r.ExitCode);
        Assert.True(r.KillFailed);
    }

    [Fact]
    public void DeadlineFires_SignalOnlyDefault_NotAKillFailure()
    {
        var child = new FakeChild { ExitsWithinDeadline = false, TerminateResult = TerminationOutcome.SignalSentNoGuarantee };
        var starter = new FakeChildStarter(child);

        RunForResult r = RunForRunner.Execute(starter, "x", Array.Empty<string>(), Opts(killAfter: null), CancellationToken.None);

        Assert.Equal(SupervisionExitCode.Timeout, r.ExitCode);
        Assert.Null(child.LastKillAfter);   // default mode: no grace passed
        Assert.False(r.KillFailed);         // signal-only is NOT a kill failure
    }

    [Fact]
    public void TokenCancelled_Returns130_TerminatesPromptly()
    {
        var child = new FakeChild { ExitsWithinDeadline = false };
        var starter = new FakeChildStarter(child);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // models Ctrl+C: WaitForExit returns false, token is cancelled

        RunForResult r = RunForRunner.Execute(starter, "x", Array.Empty<string>(), Opts(), cts.Token);

        Assert.Equal(RunForOutcome.Interrupted, r.Outcome);
        Assert.Equal(SupervisionExitCode.Interrupted, r.ExitCode);
        Assert.Equal(1, child.TerminateCallCount);
        Assert.Equal(TimeSpan.Zero, child.LastKillAfter); // Ctrl+C ⇒ prompt ensure-dead (grace 0)
    }

    [Fact]
    public void CommandNotFound_Returns127_LaunchFailed()
    {
        var starter = new ThrowingChildStarter(new CommandNotFoundException("nope"));

        RunForResult r = RunForRunner.Execute(starter, "nope", Array.Empty<string>(), Opts(), CancellationToken.None);

        Assert.Equal(RunForOutcome.LaunchFailed, r.Outcome);
        Assert.Equal(ExitCode.NotFound, r.ExitCode);
    }

    [Fact]
    public void CommandNotExecutable_Returns126_LaunchFailed()
    {
        var starter = new ThrowingChildStarter(new CommandNotExecutableException("nope"));

        RunForResult r = RunForRunner.Execute(starter, "nope", Array.Empty<string>(), Opts(), CancellationToken.None);

        Assert.Equal(ExitCode.NotExecutable, r.ExitCode);
    }

    [Fact]
    public void TokenCancelled_ChildAlsoExitedCleanly_CtrlCWins_Returns130()
    {
        // Ctrl+C arriving with a coincident clean exit: runfor reports 130, NOT the child's 0.
        var child = new FakeChild { ExitsWithinDeadline = true, FakeExitCode = 0 };
        var starter = new FakeChildStarter(child);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        RunForResult r = RunForRunner.Execute(starter, "x", Array.Empty<string>(), Opts(), cts.Token);

        Assert.Equal(RunForOutcome.Interrupted, r.Outcome);
        Assert.Equal(SupervisionExitCode.Interrupted, r.ExitCode);
        // Pin the ensure-dead behaviour on the Ctrl+C-wins path: Terminate is called UNCONDITIONALLY on
        // cancel even though the child "exited" — a future "skip Terminate when exited" optimisation
        // would otherwise pass silently while leaving the (possibly still-live) tree unreaped.
        Assert.Equal(1, child.TerminateCallCount);
        Assert.Equal(TimeSpan.Zero, child.LastKillAfter);
    }
}
