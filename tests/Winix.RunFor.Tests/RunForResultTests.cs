using System;
using Winix.ProcessSupervision;
using Xunit;
using Yort.ShellKit;

namespace Winix.RunFor.Tests;

public class RunForResultTests
{
    [Fact]
    public void Completed_ForwardsChildCode_AndIsNotTimeout()
    {
        RunForResult r = RunForResult.Completed(7, TimeSpan.FromSeconds(1));
        Assert.Equal(RunForOutcome.Completed, r.Outcome);
        Assert.Equal(7, r.ExitCode);
        Assert.Equal(7, r.ChildExitCode);
        Assert.NotEqual(SupervisionExitCode.Timeout, r.ExitCode); // negative invariant
    }

    [Fact]
    public void TimedOut_Is124_NullChildCode()
    {
        RunForResult r = RunForResult.TimedOut(TimeSpan.FromSeconds(5), killFailed: false);
        Assert.Equal(RunForOutcome.TimedOut, r.Outcome);
        Assert.Equal(SupervisionExitCode.Timeout, r.ExitCode);
        Assert.Null(r.ChildExitCode);
        Assert.False(r.KillFailed);
    }

    [Fact]
    public void Interrupted_Is130()
    {
        RunForResult r = RunForResult.Interrupted(TimeSpan.Zero, killFailed: true);
        Assert.Equal(RunForOutcome.Interrupted, r.Outcome);
        Assert.Equal(SupervisionExitCode.Interrupted, r.ExitCode);
        Assert.Null(r.ChildExitCode);
        Assert.True(r.KillFailed);
    }

    [Fact]
    public void LaunchFailed_CarriesClassifiedCode_NullChildCode()
    {
        RunForResult r = RunForResult.LaunchFailed(ExitCode.NotFound, TimeSpan.Zero);
        Assert.Equal(RunForOutcome.LaunchFailed, r.Outcome);
        Assert.Equal(ExitCode.NotFound, r.ExitCode);
        Assert.Null(r.ChildExitCode);
    }
}
