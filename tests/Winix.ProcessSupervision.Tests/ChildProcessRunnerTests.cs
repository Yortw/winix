using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;
using Yort.ShellKit;

namespace Winix.ProcessSupervision.Tests;

public class ChildProcessRunnerTests
{
    [Fact]
    public void Run_ChildExitsZero_ReturnsZero()
    {
        var runner = new ChildProcessRunner();
        (string cmd, string[] args) = ChildHelpers.ExitWith(0);

        int code = runner.Run(cmd, args, CancellationToken.None);

        Assert.Equal(0, code);
    }

    [Fact]
    public void Run_ChildExitsNonZero_ForwardsExitCode()
    {
        var runner = new ChildProcessRunner();
        (string cmd, string[] args) = ChildHelpers.ExitWith(7);

        int code = runner.Run(cmd, args, CancellationToken.None);

        Assert.Equal(7, code);
    }

    [Fact]
    public void Run_CommandNotFound_ThrowsCommandNotFound()
    {
        var runner = new ChildProcessRunner();

        Assert.Throws<CommandNotFoundException>(() =>
            runner.Run("this-command-does-not-exist-xyzzy", System.Array.Empty<string>(), CancellationToken.None));
    }

    [Fact]
    public void Run_TokenCancelledMidRun_KillsChildAndReturnsPromptly()
    {
        var runner = new ChildProcessRunner();
        (string cmd, string[] args) = ChildHelpers.SleepSeconds(120);

        using var cts = new CancellationTokenSource();
        // Cancel well after the child has started but far short of its 120s sleep. The bound below
        // (30s) is load-bearing slack — wide enough to swamp CI thread-pool jitter, far short of 120s
        // so a hung wait (kill failed) still fails the test instead of waiting out the sleep.
        cts.CancelAfter(TimeSpan.FromSeconds(2));

        var sw = Stopwatch.StartNew();
        int code = runner.Run(cmd, args, cts.Token);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30),
            $"Run did not return promptly after cancel (took {sw.Elapsed.TotalSeconds:F1}s — kill likely failed)");
        // The exact killed-child exit code is platform-specific (137-ish on Unix SIGKILL, non-zero on
        // Windows). We assert only that it is NOT the success code, since the child never finished.
        Assert.NotEqual(0, code);
    }

    [Fact]
    public void Run_TokenAlreadyCancelled_ReturnsPromptlyWithoutHanging()
    {
        var runner = new ChildProcessRunner();
        (string cmd, string[] args) = ChildHelpers.SleepSeconds(120);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancelled: the Register callback fires synchronously during Run

        var sw = Stopwatch.StartNew();
        int code = runner.Run(cmd, args, cts.Token); // must not hang or throw
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30),
            $"Run hung on a pre-cancelled token (took {sw.Elapsed.TotalSeconds:F1}s)");
        Assert.NotEqual(0, code);
    }

    [Fact]
    public void Run_LiveTokenNeverCancelled_ChildExitsNaturally_ForwardsRealCode()
    {
        // Invariant: with a real (never-cancelled) token, the kill registration is present but inert —
        // a naturally-exiting child's true exit code is forwarded, NOT a killed code. Unlike the
        // Task 4 tests, this uses a real CancellationToken (not None) so the registration path runs.
        var runner = new ChildProcessRunner();
        (string cmd, string[] args) = ChildHelpers.ExitWith(7);

        using var cts = new CancellationTokenSource();
        int code = runner.Run(cmd, args, cts.Token);

        Assert.Equal(7, code);
        Assert.False(cts.IsCancellationRequested, "token must not have been cancelled by the runner");
    }

    // Unix-gated: spawns sh → backgrounds a `sleep 120` grandchild → records the grandchild PID,
    // then waits. On cancel, the runner must kill the WHOLE tree, so the grandchild dies too.
    // Windows note: the same Kill(entireProcessTree:true) call handles the Windows tree, but the
    // single-child test only proves the CHILD dies — a Windows GRANDCHILD assertion is deferred to
    // the tool integration tests (runfor/lock/soak/attempt) against a real wrapped command. We do
    // not claim Windows grandchild coverage here (verification-oracle honesty).
    [SkippableFact]
    public void Run_TokenCancelled_KillsGrandchildToo_Unix()
    {
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix-only process-group kill assertion.");
        if (OperatingSystem.IsWindows()) { return; } // deliberate CA1416 redundancy with Skip.IfNot

        string pidFile = Path.GetTempFileName();
        try
        {
            var runner = new ChildProcessRunner();
            // Background a sleep, record its PID, then `wait` so the parent stays alive holding the tree.
            string script = $"sleep 120 & echo $! > '{pidFile}'; wait";
            var args = new[] { "-c", script };

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            runner.Run("/bin/sh", args, cts.Token);

            // Read the grandchild PID the script recorded.
            string pidText = File.ReadAllText(pidFile).Trim();
            Assert.False(string.IsNullOrEmpty(pidText), "grandchild PID was not recorded");
            int grandchildPid = int.Parse(pidText);

            // Poll up to 10s for the grandchild to disappear. GetProcessById throws ArgumentException
            // once the PID is no longer a live process.
            bool dead = false;
            for (int i = 0; i < 100 && !dead; i++)
            {
                try
                {
                    using Process gc = Process.GetProcessById(grandchildPid);
                    if (gc.HasExited) { dead = true; }
                }
                catch (ArgumentException) { dead = true; }
                if (!dead) { Thread.Sleep(100); }
            }

            Assert.True(dead, $"grandchild PID {grandchildPid} survived the tree kill");
        }
        finally
        {
            File.Delete(pidFile);
        }
    }
}
