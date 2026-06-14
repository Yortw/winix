using System;
using System.Diagnostics;
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
}
