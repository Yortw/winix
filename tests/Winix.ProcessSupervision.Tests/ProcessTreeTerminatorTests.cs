using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;

namespace Winix.ProcessSupervision.Tests;

public class ProcessTreeTerminatorTests
{
    // Unix-gated: SendSignal(pid, SIGTERM) to a real child must terminate it. CANNOT run on
    // Windows (no libc kill) — Skipped there, VERIFIED BY CI on ubuntu/macos.
    [SkippableFact]
    public void SendSignal_Sigterm_TerminatesChild_Unix()
    {
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix-only libc kill.");
        if (OperatingSystem.IsWindows()) { return; } // deliberate CA1416 redundancy with Skip.IfNot

        var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("sleep 120");
        using Process child = Process.Start(psi)!;
        try
        {
            int rc = NativeProcess.SendSignal(child.Id, NativeProcess.SigTerm);
            Assert.Equal(0, rc); // kill succeeded (child is ours, alive)
            bool exited = child.WaitForExit(10_000);
            Assert.True(exited, "child did not exit after SIGTERM");
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); }
        }
    }

    [Fact]
    public void KillTree_AlreadyExitedChild_DoesNotThrow()
    {
        (string cmd, string[] args) = ChildHelpers.ExitWith(0);
        var psi = new ProcessStartInfo(cmd) { UseShellExecute = false };
        foreach (string a in args) { psi.ArgumentList.Add(a); }
        using Process child = Process.Start(psi)!;
        child.WaitForExit(); // child has fully exited before we kill it

        // Must hit the HasExited guard / InvalidOperationException arm without throwing.
        ProcessTreeTerminator.KillTree(child);
    }

    [Fact]
    public void KillTree_DisposedProcess_DoesNotThrow()
    {
        (string cmd, string[] args) = ChildHelpers.ExitWith(0);
        var psi = new ProcessStartInfo(cmd) { UseShellExecute = false };
        foreach (string a in args) { psi.ArgumentList.Add(a); }
        Process child = Process.Start(psi)!;
        child.WaitForExit();
        child.Dispose(); // exercise the ObjectDisposedException arm

        // Must swallow ObjectDisposedException (the catch ordering puts it before InvalidOperationException).
        ProcessTreeTerminator.KillTree(child);
    }

    [SkippableFact]
    public void TerminateGracefully_OnWindows_KillsImmediately()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows immediate-kill path.");
        if (!OperatingSystem.IsWindows()) { return; } // deliberate CA1416 redundancy

        (string cmd, string[] args) = ChildHelpers.SleepSeconds(120);
        var psi = new ProcessStartInfo(cmd) { UseShellExecute = false };
        foreach (string a in args) { psi.ArgumentList.Add(a); }
        using Process child = Process.Start(psi)!;

        var sw = Stopwatch.StartNew();
        bool terminated = ProcessTreeTerminator.TerminateGracefully(child, NativeProcess.SigTerm, TimeSpan.FromSeconds(30));
        sw.Stop();

        Assert.True(terminated, "TerminateGracefully did not confirm the child exited on Windows");
        Assert.True(child.HasExited, "child not killed on Windows");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Windows kill waited {sw.Elapsed.TotalSeconds:F1}s (should be immediate)");
    }

    [SkippableFact]
    public void TerminateGracefully_Unix_ChildHandlesSignalWithinGrace_ExitsItself()
    {
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix graceful-signal path.");
        if (OperatingSystem.IsWindows()) { return; } // deliberate CA1416 redundancy

        (string cmd, string[] args) = ChildHelpers.TrapSignalThenSleepUnix("TERM", 42);
        var psi = new ProcessStartInfo(cmd) { UseShellExecute = false };
        foreach (string a in args) { psi.ArgumentList.Add(a); }
        using Process child = Process.Start(psi)!;
        Thread.Sleep(500); // sh needs a moment to install the trap

        bool terminated = ProcessTreeTerminator.TerminateGracefully(child, NativeProcess.SigTerm, TimeSpan.FromSeconds(10));

        Assert.True(terminated, "TerminateGracefully did not confirm exit");
        Assert.True(child.HasExited, "child did not exit");
        Assert.Equal(42, child.ExitCode); // exited ITSELF via the trap, not the SIGKILL backstop
    }

    [SkippableFact]
    public void TerminateGracefully_Unix_ChildIgnoresSignal_SigkillBackstopFires()
    {
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix backstop path.");
        if (OperatingSystem.IsWindows()) { return; } // deliberate CA1416 redundancy

        (string cmd, string[] args) = ChildHelpers.IgnoreSignalThenSleepUnix("TERM");
        var psi = new ProcessStartInfo(cmd) { UseShellExecute = false };
        foreach (string a in args) { psi.ArgumentList.Add(a); }
        using Process child = Process.Start(psi)!;
        Thread.Sleep(500);

        var sw = Stopwatch.StartNew();
        bool terminated = ProcessTreeTerminator.TerminateGracefully(child, NativeProcess.SigTerm, TimeSpan.FromSeconds(2));
        sw.Stop();

        Assert.True(terminated, "SIGKILL backstop did not terminate a SIGTERM-ignoring child");
        Assert.True(child.HasExited, "child still alive after backstop");
        Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(1), $"backstop fired too fast ({sw.Elapsed.TotalSeconds:F1}s) — grace window not honoured");
    }

    [SkippableFact]
    public void TerminateGracefully_Unix_GraceZero_BackstopFiresImmediately()
    {
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix zero-grace path.");
        if (OperatingSystem.IsWindows()) { return; } // deliberate CA1416 redundancy

        (string cmd, string[] args) = ChildHelpers.IgnoreSignalThenSleepUnix("TERM");
        var psi = new ProcessStartInfo(cmd) { UseShellExecute = false };
        foreach (string a in args) { psi.ArgumentList.Add(a); }
        using Process child = Process.Start(psi)!;
        Thread.Sleep(500);

        var sw = Stopwatch.StartNew();
        bool terminated = ProcessTreeTerminator.TerminateGracefully(child, NativeProcess.SigTerm, TimeSpan.Zero);
        sw.Stop();

        Assert.True(terminated, "zero-grace path did not kill the child");
        Assert.True(child.HasExited, "child still alive");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"zero-grace waited {sw.Elapsed.TotalSeconds:F1}s (should not block)");
    }

    [SkippableFact]
    public void TerminateGracefully_Unix_BackstopReapsGrandchild()
    {
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix tree-backstop path.");
        if (OperatingSystem.IsWindows()) { return; } // deliberate CA1416 redundancy

        string pidFile = Path.GetTempFileName();
        try
        {
            string script = $"sleep 120 & echo $! > '{pidFile}'; trap '' TERM; wait";
            var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(script);
            using Process child = Process.Start(psi)!;
            Thread.Sleep(500);

            bool terminated = ProcessTreeTerminator.TerminateGracefully(child, NativeProcess.SigTerm, TimeSpan.FromSeconds(2));
            Assert.True(terminated, "parent not terminated");

            string pidText = File.ReadAllText(pidFile).Trim();
            Assert.False(string.IsNullOrEmpty(pidText), "grandchild PID not recorded");
            int grandchildPid = int.Parse(pidText);

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
            Assert.True(dead, $"grandchild PID {grandchildPid} survived the tree backstop");
        }
        finally
        {
            File.Delete(pidFile);
        }
    }
}
