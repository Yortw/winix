# Process-Supervision: Graceful-Termination Extension (`ProcessTreeTerminator`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the `Winix.ProcessSupervision` shared library with the cross-platform process-tree termination primitives `runfor` needs — an immediate `KillTree` (extracted from `ChildProcessRunner`, made DRY) and a `TerminateGracefully` (Unix: signal the child, wait a grace window, then SIGKILL the tree; Windows: immediate tree kill) — backed by a libc `kill(2)` P/Invoke that mirrors the suite's proven `NativeMetrics` pattern.

**Architecture:** This is plan 2a of the runfor build (the runfor tool itself is plan 2b, written once this terminator API is verified). It adds a `NativeProcess` libc-`kill` P/Invoke (two platform partial files mirroring `src/Winix.TimeIt/NativeMetrics.{Linux,MacOS}.cs`), and a static `ProcessTreeTerminator` exposing `KillTree(Process)` and `TerminateGracefully(Process, int signal, TimeSpan grace)`. `ChildProcessRunner`'s existing inline kill is refactored to call `ProcessTreeTerminator.KillTree`, so the immediate-kill logic lives in exactly one place (DRY) and the existing runner tests act as the regression net.

**Tech Stack:** .NET 10, C#, NativeAOT-compatible (`[LibraryImport]`, `partial`), xUnit + `Xunit.SkippableFact`, `Yort.ShellKit`.

**Why split from runfor:** The Unix signal P/Invoke is the only part of runfor that (a) cannot be exercised on this Windows dev box and (b) carries real cross-platform risk. Isolating it into its own small, CI-verified unit de-risks the tool before any packaging investment — the same spike-first discipline the family design applies to `lock`.

**Verification-level honesty (load-bearing):** The Unix graceful-signal path CANNOT be run on the Windows dev machine. Its tests are `SkippableFact` (Skipped on Windows) and are **verified by CI on `ubuntu-latest` and `macos-latest`**. The plan marks every Unix-only assertion accordingly; "passes locally on Windows" is NOT evidence the Unix path works — only a green CI Linux/macOS leg is.

**Reference files (read before implementing):**
- `src/Winix.TimeIt/NativeMetrics.Linux.cs` + `src/Winix.TimeIt/NativeMetrics.MacOS.cs` — the EXACT proven P/Invoke pattern to mirror (`[LibraryImport]`, `partial`, platform dispatch, no `[SupportedOSPlatform]` — runtime-guarded). `NativeProcess` must follow this structure precisely.
- `src/Winix.ProcessSupervision/ChildProcessRunner.cs` — the current inline kill-on-cancel block (Task 2 extracts its `Kill(entireProcessTree:true)` + catch set into `ProcessTreeTerminator.KillTree`).
- `tests/Winix.ProcessSupervision.Tests/ChildProcessRunnerTests.cs` + `ChildHelpers.cs` — the existing runner tests (regression net) and the cross-platform child helpers (`ExitWith`, `SleepSeconds`).

**Scope boundary (deferred to plan 2b — runfor tool):** signal-NAME parsing (`--signal TERM`), the deadline orchestration, exit-code 124 mapping, `--kill-after` flag wiring, and all packaging. This plan ships only the library primitives + their tests.

---

## File Structure

**Library — `src/Winix.ProcessSupervision/`:**
- `NativeProcess.cs` — platform dispatcher: `static void SendSignal(int pid, int signal)` (runtime-guards Linux/macOS, throws `PlatformNotSupportedException` elsewhere) + the portable signal-number constants (`SigHup=1`, `SigInt=2`, `SigQuit=3`, `SigKill=9`, `SigTerm=15` — identical on Linux and macOS).
- `NativeProcess.Linux.cs` — `[LibraryImport("libc", EntryPoint = "kill")]` partial.
- `NativeProcess.MacOS.cs` — `[LibraryImport("libSystem", EntryPoint = "kill")]` partial.
- `ProcessTreeTerminator.cs` — `static void KillTree(Process)` + `static void TerminateGracefully(Process, int signal, TimeSpan grace)`.
- `ChildProcessRunner.cs` — MODIFIED: its cancel callback now calls `ProcessTreeTerminator.KillTree(process)`.

**Tests — `tests/Winix.ProcessSupervision.Tests/`:**
- `ProcessTreeTerminatorTests.cs` — Windows immediate-kill + Unix-gated graceful (CI-verified).
- `ChildHelpers.cs` — MODIFIED: add `TrapSignalThenExit(...)` / `IgnoreSignalThenSleep(...)` Unix child builders.

---

## Task 1: `NativeProcess` — libc `kill(2)` P/Invoke

**Files:**
- Create: `src/Winix.ProcessSupervision/NativeProcess.cs`
- Create: `src/Winix.ProcessSupervision/NativeProcess.Linux.cs`
- Create: `src/Winix.ProcessSupervision/NativeProcess.MacOS.cs`
- Test: `tests/Winix.ProcessSupervision.Tests/ProcessTreeTerminatorTests.cs` (created here, grows in Task 3)

- [ ] **Step 1: Read the pattern to mirror**

Read `src/Winix.TimeIt/NativeMetrics.Linux.cs` and `src/Winix.TimeIt/NativeMetrics.MacOS.cs` in full. Note exactly: the `[LibraryImport(...)]` attribute form, the `partial` class + `private static partial` method declaration, the library-name strings (`"libc"` vs `"libSystem"`), and how the public dispatcher decides which platform method to call (runtime `OperatingSystem.IsLinux()/IsMacOS()` — confirm there is no `[SupportedOSPlatform]` attribute and the build is still warning-clean). `NativeProcess` MUST mirror this structure; do not invent a different P/Invoke style.

- [ ] **Step 2: Write the failing test (Unix-gated)**

Create `tests/Winix.ProcessSupervision.Tests/ProcessTreeTerminatorTests.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Threading;
using Xunit;

namespace Winix.ProcessSupervision.Tests;

public class ProcessTreeTerminatorTests
{
    // Unix-gated: SendSignal(pid, SIGTERM) to a real child must terminate it. CANNOT run on
    // Windows (no libc kill) — Skipped there, VERIFIED BY CI on ubuntu/macos. "Passes on Windows"
    // is meaningless for this test; it only proves something when the CI Linux/macOS leg runs it.
    [SkippableFact]
    public void SendSignal_Sigterm_TerminatesChild_Unix()
    {
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix-only libc kill.");
        if (OperatingSystem.IsWindows()) { return; } // deliberate CA1416 redundancy with Skip.IfNot

        var psi = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
        };
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
}
```

- [ ] **Step 3: Run the test — verify it FAILS (Windows: it Skips, which is not the failure we want; build must fail because `NativeProcess` is absent)**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter ProcessTreeTerminatorTests
```

Expected on Windows: a COMPILE failure (`NativeProcess` does not exist). The Skip-on-Windows behaviour only matters once it compiles; right now the absence of `NativeProcess` is the red.

- [ ] **Step 4: Write the platform P/Invoke partials**

Create `src/Winix.ProcessSupervision/NativeProcess.Linux.cs` (mirror `NativeMetrics.Linux.cs`'s attribute form exactly):

```csharp
using System.Runtime.InteropServices;

namespace Winix.ProcessSupervision;

internal static partial class NativeProcess
{
    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int KillLinux(int pid, int sig);
}
```

Create `src/Winix.ProcessSupervision/NativeProcess.MacOS.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Winix.ProcessSupervision;

internal static partial class NativeProcess
{
    [LibraryImport("libSystem", EntryPoint = "kill", SetLastError = true)]
    private static partial int KillMacOS(int pid, int sig);
}
```

- [ ] **Step 5: Write the dispatcher + signal constants**

Create `src/Winix.ProcessSupervision/NativeProcess.cs`:

```csharp
using System;
using System.Runtime.InteropServices;

namespace Winix.ProcessSupervision;

/// <summary>
/// Thin libc <c>kill(2)</c> P/Invoke for sending a Unix signal to a process. Windows has no signal
/// model; <see cref="SendSignal"/> throws <see cref="PlatformNotSupportedException"/> there.
/// Mirrors the platform-split P/Invoke pattern of <c>Winix.TimeIt.NativeMetrics</c>.
/// </summary>
internal static partial class NativeProcess
{
    /// <summary>SIGHUP (1) — hangup. Identical on Linux and macOS.</summary>
    public const int SigHup = 1;

    /// <summary>SIGINT (2) — interrupt (Ctrl+C). Identical on Linux and macOS.</summary>
    public const int SigInt = 2;

    /// <summary>SIGQUIT (3) — quit. Identical on Linux and macOS.</summary>
    public const int SigQuit = 3;

    /// <summary>SIGKILL (9) — force kill, uncatchable. Identical on Linux and macOS.</summary>
    public const int SigKill = 9;

    /// <summary>SIGTERM (15) — polite termination request (default). Identical on Linux and macOS.</summary>
    public const int SigTerm = 15;

    /// <summary>ESRCH (3) — no such process. Benign: the target already exited.</summary>
    public const int ESRCH = 3;

    /// <summary>EPERM (1) — operation not permitted. The signal could NOT be delivered (e.g. the
    /// target runs as a different user). A real termination failure, NOT benign.</summary>
    public const int EPERM = 1;

    /// <summary>
    /// Sends <paramref name="signal"/> to the process with id <paramref name="pid"/> via libc
    /// <c>kill(2)</c>. Linux uses <c>libc</c>, macOS uses <c>libSystem</c> (both expose <c>kill</c>).
    /// </summary>
    /// <returns>
    /// 0 on success, otherwise the errno from the failed <c>kill</c> (<see cref="ESRCH"/> = target
    /// already gone — benign; <see cref="EPERM"/> = not permitted — a real failure the caller should
    /// surface). The caller decides how to treat each — this method does NOT swallow the result, so a
    /// genuinely-failed kill (EPERM) is distinguishable from a benign one (ESRCH) and from success.
    /// </returns>
    /// <remarks>
    /// SIGNALS BY RAW PID — RESIDUAL REUSE RACE: signalling by PID (the only mechanism the BCL exposes
    /// on Unix — there is no signal-by-handle) carries a narrow window where the target could have
    /// exited and its PID been recycled onto an unrelated process between the caller reading the PID
    /// and this call. Callers MUST re-check <c>Process.HasExited</c> immediately before calling to
    /// narrow the window; it cannot be eliminated. The handle-based SIGKILL backstop
    /// (<see cref="ProcessTreeTerminator.KillTree"/>, via <c>Process.Kill</c>) is reuse-safe.
    /// </remarks>
    /// <exception cref="PlatformNotSupportedException">Called on a non-Unix platform.</exception>
    public static int SendSignal(int pid, int signal)
    {
        int rc;
        if (OperatingSystem.IsLinux())
        {
            rc = KillLinux(pid, signal);
        }
        else if (OperatingSystem.IsMacOS())
        {
            rc = KillMacOS(pid, signal);
        }
        else
        {
            throw new PlatformNotSupportedException("Unix signals are not available on this platform.");
        }

        // kill returns 0 on success, -1 on failure with errno set. Surface the errno so callers can
        // distinguish ESRCH (benign) from EPERM (real failure) rather than treating all as success.
        return rc == 0 ? 0 : Marshal.GetLastPInvokeError();
    }
}
```

> NOTE: if Step 1 revealed that `NativeMetrics` guards its calls with `[SupportedOSPlatform]` or a different dispatch shape, adopt THAT shape here instead — the proven suite pattern wins over this snippet. Report any divergence.

- [ ] **Step 6: Run the test**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter ProcessTreeTerminatorTests
```

Expected: build succeeds (0 warnings); on Windows the test reports **Skipped** (1 skipped). The real PASS is recorded only when CI runs the Linux/macOS leg — do NOT claim the Unix path verified from a Windows run.

- [ ] **Step 7: Commit**

```bash
git add src/Winix.ProcessSupervision/NativeProcess.cs src/Winix.ProcessSupervision/NativeProcess.Linux.cs src/Winix.ProcessSupervision/NativeProcess.MacOS.cs tests/Winix.ProcessSupervision.Tests/ProcessTreeTerminatorTests.cs
git commit -m "feat(process-supervision): add NativeProcess libc kill(2) P/Invoke (Unix signals)"
```

---

## Task 2: `ProcessTreeTerminator.KillTree` — extract the immediate kill (DRY)

**Files:**
- Create: `src/Winix.ProcessSupervision/ProcessTreeTerminator.cs`
- Modify: `src/Winix.ProcessSupervision/ChildProcessRunner.cs`

This extracts the immediate `Process.Kill(entireProcessTree: true)` + best-effort catch set currently inline in `ChildProcessRunner`'s cancel callback, so it lives in one place. The existing `ChildProcessRunnerTests` (kill-on-cancel, pre-cancelled, never-cancelled, grandchild) are the regression net — they must stay green after the refactor with no test changes.

- [ ] **Step 1: Write the `KillTree` impl**

Create `src/Winix.ProcessSupervision/ProcessTreeTerminator.cs`:

```csharp
using System;
using System.ComponentModel;
using System.Diagnostics;

namespace Winix.ProcessSupervision;

/// <summary>
/// Cross-platform process-tree termination for the process-supervision family. <see cref="KillTree"/>
/// is the immediate (SIGKILL-equivalent) kill used by <c>lock</c>/<c>soak</c>/<c>attempt</c> on cancel;
/// <see cref="TerminateGracefully"/> adds the Unix SIGTERM→grace→SIGKILL escalation <c>runfor</c> uses.
/// </summary>
public static class ProcessTreeTerminator
{
    /// <summary>
    /// Immediately kills <paramref name="process"/> and its entire child tree. Best-effort: every
    /// failure mode (already-exited, disposed, access-denied, platform-can't-kill) is swallowed, so a
    /// kill that races process teardown never throws into the caller (a throwing kill called from a
    /// <see cref="System.Threading.CancellationToken"/> callback would propagate out of
    /// <c>Cancel()</c> into the supervising tool).
    /// </summary>
    public static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        // ObjectDisposedException FIRST — it derives from InvalidOperationException.
        catch (ObjectDisposedException) { /* disposed before kill fired */ }
        catch (InvalidOperationException) { /* already exited — benign */ }
        catch (Win32Exception) { /* access denied / signal-delivery error — best-effort */ }
        catch (NotSupportedException) { /* platform cannot kill the tree — best-effort */ }
    }
}
```

- [ ] **Step 2: Refactor `ChildProcessRunner` to call it**

In `src/Winix.ProcessSupervision/ChildProcessRunner.cs`, replace the inline callback body:

```csharp
            using CancellationTokenRegistration killReg = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                // ObjectDisposedException FIRST — it derives from InvalidOperationException.
                catch (ObjectDisposedException) { /* disposed before kill fired */ }
                catch (InvalidOperationException) { /* already exited — benign */ }
                catch (Win32Exception) { /* access denied / signal-delivery error — best-effort */ }
                catch (NotSupportedException) { /* platform cannot kill the tree — best-effort */ }
            });
```

with:

```csharp
            using CancellationTokenRegistration killReg =
                cancellationToken.Register(() => ProcessTreeTerminator.KillTree(process));
```

Leave the surrounding ORDERING-INVARIANT comment block intact (it still describes why `killReg` disposes before `process`). If removing the inline `catch (Win32Exception)` makes `using System.ComponentModel;` unused in `ChildProcessRunner.cs`, remove that using (it is still needed for the `catch (Win32Exception ex)` on the `Process.Start` path — CONFIRM by reading the file before deleting; the Start-path catch keeps the using needed, so most likely leave it).

- [ ] **Step 3: Pin `KillTree`'s exited/disposed path directly (T3 — don't just trust the regression net)**

The extraction's subtlest property is the catch set, especially `ObjectDisposedException` caught *before* `InvalidOperationException` (it derives from it). First CONFIRM by reading `ChildProcessRunnerTests.cs` whether any existing test actually drives a kill against an already-exited/disposed process. The kill-on-cancel tests kill a *live* child, so they likely do NOT exercise the exited/disposed catch arms — meaning the "regression net" does not cover the invariant being relied on. Add a direct test that does, in `ProcessTreeTerminatorTests.cs`:

```csharp
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
```

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter "KillTree_AlreadyExitedChild|KillTree_DisposedProcess"
```

Expected: PASS (2 tests) — both complete without an exception escaping `KillTree`.

- [ ] **Step 4: Run the existing runner tests — they must stay green (regression net)**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter ChildProcessRunnerTests
```

Expected: PASS — same counts as before the refactor (the kill-on-cancel, pre-cancelled, never-cancelled tests all still pass; grandchild Skipped on Windows). No test edits. If any fails, the extraction changed behaviour — revert and investigate.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.ProcessSupervision/ProcessTreeTerminator.cs src/Winix.ProcessSupervision/ChildProcessRunner.cs tests/Winix.ProcessSupervision.Tests/ProcessTreeTerminatorTests.cs
git commit -m "refactor(process-supervision): extract ProcessTreeTerminator.KillTree (DRY) + pin exited/disposed path"
```

---

## Task 3: `ProcessTreeTerminator.TerminateGracefully`

**Files:**
- Modify: `src/Winix.ProcessSupervision/ProcessTreeTerminator.cs`
- Modify: `tests/Winix.ProcessSupervision.Tests/ChildHelpers.cs`
- Modify: `tests/Winix.ProcessSupervision.Tests/ProcessTreeTerminatorTests.cs`

- [ ] **Step 1: Add the Unix child helpers**

Add to `tests/Winix.ProcessSupervision.Tests/ChildHelpers.cs` (inside the `ChildHelpers` class):

```csharp
    /// <summary>A Unix child that traps the given signal, exits with <paramref name="exitCode"/> on it,
    /// and otherwise sleeps. Used to prove graceful termination (the child exits ITSELF on SIGTERM,
    /// before the SIGKILL backstop). Unix-only — the caller must platform-gate.</summary>
    public static (string Command, string[] Args) TrapSignalThenSleepUnix(string signalName, int exitCode)
    {
        return ("/bin/sh", new[] { "-c", $"trap 'exit {exitCode}' {signalName}; sleep 120" });
    }

    /// <summary>A Unix child that IGNORES the given signal and keeps sleeping. Used to prove the
    /// SIGKILL backstop fires after the grace window. Unix-only — the caller must platform-gate.</summary>
    public static (string Command, string[] Args) IgnoreSignalThenSleepUnix(string signalName)
    {
        return ("/bin/sh", new[] { "-c", $"trap '' {signalName}; sleep 120" });
    }
```

- [ ] **Step 2: Write the failing tests (Windows immediate + Unix graceful, CI-verified)**

Add to `tests/Winix.ProcessSupervision.Tests/ProcessTreeTerminatorTests.cs` (ensure `using System.IO;` and `using System.Runtime.InteropServices;` are present at the top — add any missing):

```csharp
    // Windows: TerminateGracefully ignores signal/grace and kills the tree immediately (no signal
    // model). Runs on the Windows dev box AND CI Windows leg. grace=30s so "didn't honour grace" is
    // unambiguous (immediate kill + bounded confirm returns in ms).
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
        // Immediate: must not have waited out the 30s grace (Windows ignores it).
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Windows kill waited {sw.Elapsed.TotalSeconds:F1}s (should be immediate)");
    }

    // Unix graceful: a child that TRAPS SIGTERM and exits 42 must exit ITSELF (code 42) — proving the
    // signal was delivered and the grace window let it handle it, BEFORE the SIGKILL backstop.
    // CANNOT run on Windows; VERIFIED BY CI on ubuntu/macos.
    [SkippableFact]
    public void TerminateGracefully_Unix_ChildHandlesSignalWithinGrace_ExitsItself()
    {
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix graceful-signal path.");
        if (OperatingSystem.IsWindows()) { return; } // deliberate CA1416 redundancy

        (string cmd, string[] args) = ChildHelpers.TrapSignalThenSleepUnix("TERM", 42);
        var psi = new ProcessStartInfo(cmd) { UseShellExecute = false };
        foreach (string a in args) { psi.ArgumentList.Add(a); }
        using Process child = Process.Start(psi)!;
        // sh needs a moment to install the trap before the signal lands.
        Thread.Sleep(500);

        bool terminated = ProcessTreeTerminator.TerminateGracefully(child, NativeProcess.SigTerm, TimeSpan.FromSeconds(10));

        Assert.True(terminated, "TerminateGracefully did not confirm exit");
        Assert.True(child.HasExited, "child did not exit");
        Assert.Equal(42, child.ExitCode); // exited ITSELF via the trap, not the SIGKILL backstop
    }

    // Unix backstop: a child that IGNORES SIGTERM must still die — the SIGKILL backstop fires after
    // the grace window. CANNOT run on Windows; VERIFIED BY CI on ubuntu/macos.
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
        // 2s grace: the SIGTERM is ignored, so the backstop SIGKILL must fire ~2s later.
        bool terminated = ProcessTreeTerminator.TerminateGracefully(child, NativeProcess.SigTerm, TimeSpan.FromSeconds(2));
        sw.Stop();

        Assert.True(terminated, "SIGKILL backstop did not terminate a SIGTERM-ignoring child");
        Assert.True(child.HasExited, "child still alive after backstop");
        // Must have waited roughly the grace window (signal ignored) — proves grace was honoured,
        // not an immediate kill. Lower bound generous to avoid CI jitter flake.
        Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(1), $"backstop fired too fast ({sw.Elapsed.TotalSeconds:F1}s) — grace window not honoured");
    }

    // T1 — grace == Zero: no grace wait. Signal a SIGTERM-ignoring child with zero grace; the backstop
    // SIGKILL must fire essentially immediately (no grace window honoured). CI-verified on Unix.
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
        // grace==0 ⇒ WaitForExit(0) does not block ⇒ near-immediate backstop. Well under the 2s grace
        // any other test uses, proving the zero path did not wait.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"zero-grace waited {sw.Elapsed.TotalSeconds:F1}s (should not block)");
    }

    // T2 — backstop reaps the TREE: a child that spawns a sleeping grandchild and IGNORES SIGTERM.
    // After the grace, the SIGKILL backstop kills the whole tree, so the grandchild dies too. Pins the
    // tree-scope of the backstop (the documented v1 scope: tree reaped only when the parent ignores
    // the signal). CI-verified on Unix.
    [SkippableFact]
    public void TerminateGracefully_Unix_BackstopReapsGrandchild()
    {
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix tree-backstop path.");
        if (OperatingSystem.IsWindows()) { return; } // deliberate CA1416 redundancy

        string pidFile = Path.GetTempFileName();
        try
        {
            // Background a grandchild sleep, record its PID, IGNORE TERM, then wait — so the parent
            // survives the SIGTERM and the backstop must SIGKILL the whole tree.
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
```

- [ ] **Step 3: Run — verify compile-fail (TerminateGracefully absent)**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter TerminateGracefully
```

Expected: FAIL — `ProcessTreeTerminator.TerminateGracefully` does not exist.

- [ ] **Step 4: Implement `TerminateGracefully`**

Add to `ProcessTreeTerminator.cs` (the class), and add `using System.Threading;` to its usings:

```csharp
    /// <summary>
    /// Terminates <paramref name="process"/> with a graceful escalation.
    /// <para>Unix: sends <paramref name="signal"/> (typically SIGTERM) to the <b>direct child</b> via
    /// libc <c>kill(2)</c>, waits up to <paramref name="grace"/> for it to exit on its own, then — if
    /// it is still alive — kills the entire tree (SIGKILL backstop via <see cref="KillTree"/>).</para>
    /// <para>Windows: there is no portable graceful-termination signal, so <paramref name="signal"/>
    /// and <paramref name="grace"/> are ignored and the tree is killed immediately. This platform
    /// difference is intentional and documented (family ADR D7).</para>
    /// </summary>
    /// <param name="process">The child process to terminate.</param>
    /// <param name="signal">The Unix signal number to send first (ignored on Windows). See
    /// <see cref="NativeProcess"/> for the portable constants.</param>
    /// <param name="grace">How long to wait for a graceful exit before the SIGKILL backstop
    /// (ignored on Windows). Zero or negative ⇒ no grace: signal then immediately SIGKILL-tree if not
    /// already dead.</param>
    /// <returns>
    /// <c>true</c> if the process is confirmed exited when this method returns; <c>false</c> if it may
    /// still be alive (e.g. the signal AND the SIGKILL backstop both failed — typically EPERM, a child
    /// owned by another user). A caller (<c>runfor</c>) should surface <c>false</c> as "could not kill
    /// child (may still be running)" rather than silently reporting a clean timeout.
    /// </returns>
    /// <remarks>
    /// SCOPE (v1, accepted): the graceful <paramref name="signal"/> is sent to the DIRECT CHILD ONLY,
    /// not the child's process group. A child that handles the signal and exits ITSELF within
    /// <paramref name="grace"/> may therefore ORPHAN any grandchildren it spawned (they are not
    /// signalled, and the SIGKILL tree backstop does not fire because the parent exited in time). The
    /// backstop's <see cref="KillTree"/> reaps the whole tree ONLY when the parent ignores the signal
    /// and the grace elapses. True process-group signalling (<c>kill(-pgid, …)</c>) needs the child to
    /// be a session/group leader (<c>setsid</c>/<c>setpgid</c> pre-exec), which the BCL cannot arrange
    /// and macOS lacks the <c>setsid</c> CLI for — deferred. This reconciles the family design's
    /// aspirational "SIGKILL to the process group" wording down to the runfor section's "the child".
    /// </remarks>
    public static bool TerminateGracefully(Process process, int signal, TimeSpan grace)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            // Windows / unsupported: no graceful signal model — kill immediately.
            KillTree(process);
            return ConfirmExited(process);
        }

        // Re-check HasExited immediately before signalling to NARROW (not eliminate) the PID-reuse
        // window: signalling by raw PID could otherwise hit an unrelated process that recycled the
        // PID after the child exited. See NativeProcess.SendSignal remarks.
        if (HasExitedSafe(process)) { return true; }

        int pid;
        try { pid = process.Id; }
        catch (InvalidOperationException) { return true; }  // already exited — nothing to do
        catch (ObjectDisposedException) { return true; }

        NativeProcess.SendSignal(pid, signal); // errno intentionally not surfaced here — the backstop
                                               // + the bool return below report the net outcome.

        // Give the child the grace window to handle the signal and exit on its own.
        // WaitForExit(0) (grace<=0) does NOT block — it returns the current exited-state immediately.
        int graceMs = grace <= TimeSpan.Zero ? 0 : (int)Math.Min(grace.TotalMilliseconds, int.MaxValue);
        bool exited;
        try { exited = process.WaitForExit(graceMs); }
        catch (InvalidOperationException) { return true; } // exited; handle state odd — nothing to kill
        catch (SystemException) { exited = false; }        // treat odd handle state as "still alive"

        if (!exited)
        {
            // Grace elapsed and the child ignored the signal — force-kill the whole tree (handle-based,
            // reuse-safe). This is where grandchildren get reaped.
            KillTree(process);
        }

        return ConfirmExited(process);
    }

    // Bounded confirm window: Process.Kill is asynchronous (especially on Windows), so a kill that
    // WILL succeed may not have taken effect the instant we check. Wait up to this long for the
    // process to actually die before declaring the kill failed. Returns as soon as it exits, so a
    // successful kill confirms in milliseconds — the cap only bites when the kill genuinely failed.
    private const int ConfirmExitMs = 5_000;

    // Best-effort "is it gone?" used for the bool return. A throwing/odd handle state is treated as
    // "not confirmed exited" (false) so the caller errs toward surfacing a possible survivor.
    private static bool ConfirmExited(Process process)
    {
        try { return process.WaitForExit(ConfirmExitMs); }
        catch (InvalidOperationException) { return true; } // no process associated ⇒ it's gone
        catch (SystemException) { return false; }
    }

    private static bool HasExitedSafe(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
        catch (ObjectDisposedException) { return true; }
    }
```

- [ ] **Step 5: Run the tests**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter TerminateGracefully
```

Expected on Windows: `TerminateGracefully_OnWindows_KillsImmediately` PASSES; the FOUR Unix tests (`ChildHandlesSignalWithinGrace`, `ChildIgnoresSignal`, `GraceZero`, `BackstopReapsGrandchild`) report **Skipped**. Confirm the Windows test really ran (not skipped) and the four Unix tests are Skipped (not Passed). The Unix PASS is recorded only by the CI Linux/macOS leg.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.ProcessSupervision/ProcessTreeTerminator.cs tests/Winix.ProcessSupervision.Tests/ProcessTreeTerminatorTests.cs tests/Winix.ProcessSupervision.Tests/ChildHelpers.cs
git commit -m "feat(process-supervision): add ProcessTreeTerminator.TerminateGracefully (Unix SIGTERM->grace->SIGKILL)"
```

---

## Task 4: Full-solution verification + CI-leg confirmation

**Files:** none (verification + bookkeeping)

- [ ] **Step 1: Build the whole solution (warnings are errors)**

Run:

```bash
dotnet build Winix.sln
```

Expected: Build succeeded, 0 warnings, 0 errors. (The `[LibraryImport]` partials must not trip the AOT/trim analyzers.)

- [ ] **Step 2: Run the full test suite (Windows)**

Run:

```bash
dotnet test Winix.sln
```

Expected on Windows: all pass; the new `ProcessTreeTerminator` Unix tests show Skipped, the Windows immediate-kill test passes. Note in the commit/PR that the Unix graceful path is **pending CI Linux/macOS confirmation**.

- [ ] **Step 3: Record the verification-level honestly**

This plan's Unix assertions are NOT verified by the Windows run. Before this extension is treated as done-for-runfor (plan 2b), the CI `ubuntu-latest` and `macos-latest` legs of the branch's build must be green with the three Unix `SkippableFact`s actually executing (not skipped). State this explicitly wherever completion is reported — "Unix graceful-termination path verified by CI Linux+macOS on commit <sha>", not "tests pass".

- [ ] **Step 4: Commit any bookkeeping (if a doc note is added; otherwise skip)**

No `CLAUDE.md` layout change is needed (the files live in the already-recorded `Winix.ProcessSupervision` lib). If nothing to commit, this task is verification-only.

---

## Self-Review

**Spec coverage** (against the family design §runfor "graceful Unix window" + ADR D7):
- Unix `kill(2)` P/Invoke mirroring proven suite pattern → Task 1 ✓
- Immediate tree kill, DRY (one implementation) → Task 2 ✓
- Graceful Unix SIGTERM→grace→SIGKILL; Windows immediate → Task 3 ✓
- Signal-NAME parsing, deadline orchestration, exit 124, packaging → explicitly deferred to plan 2b ✓

**Placeholder scan:** none. Every step has real code/commands.

**Type consistency:** `ProcessTreeTerminator.KillTree(Process)` and `.TerminateGracefully(Process, int, TimeSpan)` are used identically across impl + tests. `NativeProcess.SendSignal(int, int)` + `NativeProcess.SigTerm` consistent. `ChildHelpers.TrapSignalThenSleepUnix`/`IgnoreSignalThenSleepUnix` match between definition (Task 3 Step 1) and use (Task 3 Step 2).

**Verification-level discipline:** every Unix-only assertion is a `SkippableFact` (Skipped on Windows), explicitly flagged as CI-verified (Task 1 Step 6, Task 3 Step 5, Task 4 Step 3). The plan never claims the Unix path is verified from a Windows run — the single most important honesty property of this plan.

**Known follow-ups for plan 2b (runfor tool, not gaps here):**
- Signal-NAME → number mapping (`--signal TERM`) lives in `Winix.RunFor` (supported set = the identical-on-both-Unix signals: HUP/INT/QUIT/KILL/TERM).
- The deadline orchestration (start child → `WaitForExit(deadline)` → on deadline `TerminateGracefully` + return 124; on Ctrl+C return 130; else forward) lives in `Winix.RunFor`, driving this terminator + the existing `ChildProcessLauncher` (which plan 2b extracts from `ChildProcessRunner` if runfor needs to reuse the spawn+classify).
- `TerminateGracefully` returns `bool` (terminated?) — plan 2b's runfor MUST surface `false` to the user (e.g. stderr "could not kill child; it may still be running") rather than reporting a clean 124, per the no-silent-termination-failure rule.

---

## Adversarial Review Integration (2026-06-14)

A fresh-subagent adversarial-plan-review pass returned 3 blockers + 3 test gaps. All reconciled and integrated (each critiqued new code in the plan — the class that survives reconciliation). Root cause was shared: the SIGTERM is delivered by raw PID to the direct child with no errno, while the SIGKILL backstop is handle-based and tree-wide.

| ID | Bucket | Disposition |
|----|--------|-------------|
| B1 | Plan blocker (PID-reuse race) | Integrated — `TerminateGracefully` re-checks `HasExited` immediately before `SendSignal` to NARROW the window; the residual race (no signal-by-handle on Unix/BCL) is documented in `NativeProcess.SendSignal` remarks; the SIGKILL backstop is noted as handle-safe. |
| B2 | Plan blocker (signal-child vs kill-tree asymmetry; design says "process group") | Integrated as documented v1 scope — signal the DIRECT CHILD, SIGKILL-tree backstop; the accepted limitation (graceful-exit parent may orphan grandchildren) is in the `TerminateGracefully` remarks, reconciling the design's aspirational "process group" wording down to the runfor section's "the child". True process-group signalling needs `setsid`/`setpgid` pre-exec (BCL can't arrange; macOS lacks the `setsid` CLI) → deferred. **Flagged to the user as a behavioural-contract decision + an ADR reconciliation item.** |
| B3 | Plan blocker (discarded errno; EPERM silently == success) | Integrated — `SendSignal` now returns the errno (0/ESRCH/EPERM…) instead of `void`; `TerminateGracefully` returns `bool` (confirmed-exited?) via a bounded confirm wait, so a genuinely-failed kill (EPERM both ways) surfaces as `false` for runfor to report. |
| T1 | Test gap (grace==Zero) | Integrated — `TerminateGracefully_Unix_GraceZero_BackstopFiresImmediately` pins the no-block zero-grace path. |
| T2 | Test gap (backstop tree scope) | Integrated — `TerminateGracefully_Unix_BackstopReapsGrandchild` pins that the backstop reaps the whole tree (grandchild dies). |
| T3 | Test gap (KillTree extraction net) | Integrated — `KillTree_AlreadyExitedChild` + `KillTree_DisposedProcess` pin the exited/disposed catch-ordering at the new home rather than trusting the runner suite to cover it. |
