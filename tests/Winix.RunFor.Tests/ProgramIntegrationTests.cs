using System;
using System.Diagnostics;
using System.Threading;
using Winix.ProcessSupervision;
using Xunit;

namespace Winix.RunFor.Tests;

public class ProgramIntegrationTests
{
    // A real child that exits BEFORE the deadline: code forwarded, NOT 124.
    [Fact]
    public void RealChild_ExitsBeforeDeadline_ForwardsCode()
    {
        (string cmd, string[] args) = ChildHelpers.ExitWith(4);
        var options = new RunForOptions(TimeSpan.FromSeconds(30), UnixSignal.DefaultSignal, killAfter: null);

        RunForResult r = RunForRunner.Execute(new ProcessChildStarter(), cmd, args, options, CancellationToken.None);

        Assert.Equal(RunForOutcome.Completed, r.Outcome);
        Assert.Equal(4, r.ExitCode);
    }

    // A real child that OUTLASTS the deadline: returns 124 promptly, child terminated.
    // --kill-after guarantees the SIGKILL backstop so this is deterministic on every platform.
    [Fact]
    public void RealChild_OutlastsDeadline_Returns124_Promptly()
    {
        (string cmd, string[] args) = ChildHelpers.SleepSeconds(120);
        var options = new RunForOptions(
            TimeSpan.FromMilliseconds(300), UnixSignal.DefaultSignal, killAfter: TimeSpan.FromMilliseconds(300));

        var sw = Stopwatch.StartNew();
        RunForResult r = RunForRunner.Execute(new ProcessChildStarter(), cmd, args, options, CancellationToken.None);
        sw.Stop();

        Assert.Equal(SupervisionExitCode.Timeout, r.ExitCode);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30),
            $"runfor did not return promptly after the deadline (took {sw.Elapsed.TotalSeconds:F1}s)");
    }

    // Unix default mode (no --kill-after) with a TERM-handling child: at the deadline the child is
    // terminated, runfor returns 124. Asserts the deadline path still returns 124 in signal-only mode
    // with a cooperating child. (Only runfor's exit code is asserted — not the child's response.)
    [SkippableFact]
    public void RealChild_Unix_DefaultSignalOnly_CooperatingChild_Returns124()
    {
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix signal semantics.");
        if (OperatingSystem.IsWindows()) { return; }

        (string cmd, string[] args) = ChildHelpers.TrapSignalThenSleepUnix("TERM", 42);
        var options = new RunForOptions(TimeSpan.FromMilliseconds(300), UnixSignal.DefaultSignal, killAfter: null);

        RunForResult r = RunForRunner.Execute(new ProcessChildStarter(), cmd, args, options, CancellationToken.None);

        // runfor's own exit code is the deadline code regardless of how the child responded.
        Assert.Equal(SupervisionExitCode.Timeout, r.ExitCode);
    }
}
