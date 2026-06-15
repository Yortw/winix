# runfor (Plan 2b) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `runfor` deadline-runner tool — a cross-platform `timeout(1)` — on top of the `Winix.ProcessSupervision` spine, with full packaging.

**Architecture:** A new `Winix.RunFor` class library exposes a `Cli.Run(args, stdout, stderr, ct, starter?)` seam driving a pure `RunForRunner.Execute(...)` orchestrator over an injectable `IChildStarter`/`ISupervisedChild` timing seam. The shared `Winix.ProcessSupervision` lib gains two reusable spine pieces consumed here and by future siblings: `ChildProcessLauncher.Launch` (spawn+classify, extracted from `ChildProcessRunner`) and `ProcessTreeTerminator.TerminateAtDeadline` (the platform/mode dispatcher returning a `TerminationOutcome`). A thin `src/runfor` console app owns Ctrl+C. Then the standard new-tool packaging checklist.

**Tech Stack:** .NET 10, NativeAOT, xUnit + Xunit.SkippableFact, `Yort.ShellKit.CommandLineParser` / `DurationParser` / `JsonHelper`.

**Deadline-termination contract (Troy ratified 2026-06-15 — coreutils-faithful):**
- **Default (no `--kill-after`), Unix:** at the deadline send `--signal` (default SIGTERM) to the direct child **once**, exit 124. A child that ignores the signal **survives** — exactly like `timeout` without `-k`. No SIGKILL backstop.
- **`--kill-after GRACE`, Unix:** send the signal, wait GRACE, then SIGKILL the whole tree if still alive (`TerminateGracefully`).
- **Windows (any mode):** no signal model → kill the tree immediately at the deadline (ADR D7); `--signal`/`--kill-after` are documented no-ops.
- **Ctrl+C (any platform):** terminate the child tree promptly (grace 0 = signal then immediate SIGKILL) and exit 130. The child has usually already received the interrupt from the terminal's foreground process group; this is the backstop.
- **Direct-child only** for the graceful signal (ADR D10): a child that handles the signal and exits within grace may orphan grandchildren; the SIGKILL backstop reaps the tree only when the child ignores the signal past grace.

---

## File Structure

**Shared lib additions (`src/Winix.ProcessSupervision/`):**
- Create `ChildProcessLauncher.cs` — `public static Process Launch(string command, IReadOnlyList<string> arguments)`. Spawn via `ArgumentList`, inherit stdio, classify launch failures. Extracted from `ChildProcessRunner.Run`.
- Modify `ChildProcessRunner.cs` — call `ChildProcessLauncher.Launch` instead of inlining the spawn (DRY).
- Create `TerminationOutcome.cs` — `public enum TerminationOutcome { ConfirmedDead, KillFailed, SignalSentNoGuarantee }`.
- Modify `ProcessTreeTerminator.cs` — add `public static TerminationOutcome TerminateAtDeadline(Process process, int signal, TimeSpan? killAfter)`.
- Create `UnixSignal.cs` — `public static class UnixSignal` with `bool TryParse(string, out int)` + `int DefaultSignal` + `string ToName(int)`.

**runfor library (`src/Winix.RunFor/`):**
- `ISupervisedChild.cs`, `IChildStarter.cs` — the runfor timing seam.
- `ProcessSupervisedChild.cs`, `ProcessChildStarter.cs` — real impls over `Process` (internal).
- `RunForOutcome.cs` — `enum { Completed, TimedOut, Interrupted, LaunchFailed }`.
- `RunForResult.cs` — immutable result (private-init + static factories).
- `RunForOptions.cs` — `Deadline`, `Signal`, `KillAfter`.
- `RunForRunner.cs` — `static RunForResult Execute(...)` orchestrator (pure, drivable by fake).
- `Formatting.cs` — `--json` envelope + plain stderr notice.
- `Cli.cs` — `Run(args, stdout, stderr, ct, starter?)`.
- `Winix.RunFor.csproj`.

**Console app (`src/runfor/`):** `Program.cs`, `runfor.csproj`, `README.md`, `runfor.1.md` + `man/man1/runfor.1`.

**Tests (`tests/Winix.RunFor.Tests/`):** `Winix.RunFor.Tests.csproj`, `FakeChild.cs`, `RunForRunnerTests.cs`, `RunForOptionsTests.cs`, `CliRunTests.cs`, `ProgramIntegrationTests.cs`. Plus new cases in `tests/Winix.ProcessSupervision.Tests/`.

**Packaging:** `docs/ai/runfor.md`, `llms.txt`, `bucket/runfor.json`, `.github/workflows/{release,post-publish,manual-smoke}.yml`, `tests/Winix.Contract.Tests/DescribeSurfaces.cs` + snapshot, a `run-smokes.sh` fixture, `CLAUDE.md`, `Winix.sln`.

---

## Phase A — Shared library spine additions

### Task 1: Extract `ChildProcessLauncher`

**Files:**
- Create: `src/Winix.ProcessSupervision/ChildProcessLauncher.cs`
- Modify: `src/Winix.ProcessSupervision/ChildProcessRunner.cs`
- Test: `tests/Winix.ProcessSupervision.Tests/ChildProcessLauncherTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Diagnostics;
using System.Threading;
using Xunit;
using Yort.ShellKit;

namespace Winix.ProcessSupervision.Tests;

public class ChildProcessLauncherTests
{
    [Fact]
    public void Launch_ValidCommand_ReturnsRunningProcess_ThatExitsWithItsCode()
    {
        (string cmd, string[] args) = ChildHelpers.ExitWith(5);

        using Process p = ChildProcessLauncher.Launch(cmd, args);
        p.WaitForExit();

        Assert.Equal(5, p.ExitCode);
    }

    [Fact]
    public void Launch_CommandNotFound_ThrowsCommandNotFound()
    {
        Assert.Throws<CommandNotFoundException>(() =>
            ChildProcessLauncher.Launch("this-command-does-not-exist-xyzzy", Array.Empty<string>()));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Winix.ProcessSupervision.Tests --filter ChildProcessLauncherTests`
Expected: FAIL to compile — `ChildProcessLauncher` does not exist.

- [ ] **Step 3: Create `ChildProcessLauncher.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace Winix.ProcessSupervision;

/// <summary>
/// Spawns a child process the way the whole supervision family requires: arguments via
/// <c>ProcessStartInfo.ArgumentList</c> (never string concatenation — suite rule), inheriting the
/// parent's console handles so the child is invisible in the pipeline, and classifying launch
/// failures into the suite's typed exceptions. Shared by <see cref="ChildProcessRunner"/> (immediate
/// kill-on-cancel consumers: lock/soak/attempt) and runfor's deadline orchestration.
/// </summary>
public static class ChildProcessLauncher
{
    /// <summary>
    /// Starts <paramref name="command"/> with <paramref name="arguments"/> and returns the running
    /// <see cref="Process"/>. The caller owns disposal and lifecycle (wait/kill).
    /// </summary>
    /// <exception cref="CommandNotFoundException">The command was not found on PATH (errno 2/3).</exception>
    /// <exception cref="CommandNotExecutableException">The command exists but could not be executed
    /// (errno 5/13 or any other launch error such as ERROR_BAD_EXE_FORMAT 193).</exception>
    public static Process Launch(string command, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
        };

        foreach (string arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            // Process.Start returns null only when an existing process is reused — effectively
            // unreachable with UseShellExecute=false. Surface a neutral error rather than
            // mislabelling it "command not found".
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Process.Start returned no process for '{command}'.");
        }
        catch (Win32Exception ex)
        {
            throw ChildProcessLaunch.ClassifyWin32(ex, command);
        }
    }
}
```

- [ ] **Step 4: Refactor `ChildProcessRunner.Run` to use the launcher**

In `src/Winix.ProcessSupervision/ChildProcessRunner.cs`, replace the spawn block (the `ProcessStartInfo` construction, the `foreach` arg loop, and the `try { process = Process.Start(...) } catch (Win32Exception ...)`) with a single call:

```csharp
public int Run(string command, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
{
    Process process = ChildProcessLauncher.Launch(command, arguments);

    try
    {
        // ORDERING INVARIANT (load-bearing — do not move): killReg is a `using` declared INSIDE
        // this try, so on any exit path its Dispose runs as the try-scope unwinds — BEFORE the
        // finally's process.Dispose(). CancellationTokenRegistration.Dispose() blocks until any
        // in-flight callback completes, so the kill callback can never run against an
        // already-disposed Process. Mirrors retry's disposal-order fix (Cli.cs).
        using CancellationTokenRegistration killReg =
            cancellationToken.Register(() => ProcessTreeTerminator.KillTree(process));

        process.WaitForExit();
        return process.ExitCode;
    }
    finally
    {
        process.Dispose();
    }
}
```

Remove the now-unused `using System.ComponentModel;` and `using System.Diagnostics;`? Keep `System.Diagnostics` (still references `Process`); drop `System.ComponentModel` only if no `Win32Exception` reference remains in the file (it does not after this change). Verify with the build (warnings-as-errors will flag an unused using).

- [ ] **Step 5 (review F11): ground the behaviour-preserving claim**

Confirm the pre-existing `ChildProcessRunnerTests` cover BOTH the success path AND the `CommandNotFoundException` classification path (they do: `Run_ChildExitsZero/NonZero` + `Run_CommandNotFound_ThrowsCommandNotFound`). The new `ChildProcessLauncherTests` re-pin not-found classification at the launcher. State in the commit that the refactor is grounded by these tests, not assumed — `ChildProcessRunner` is the spine for the not-yet-built lock/soak/attempt.

- [ ] **Step 6: Run tests to verify pass**

Run: `dotnet test tests/Winix.ProcessSupervision.Tests`
Expected: PASS — `ChildProcessLauncherTests` green AND all pre-existing `ChildProcessRunnerTests` still green (the refactor is behaviour-preserving).

- [ ] **Step 7: Commit**

```bash
git add src/Winix.ProcessSupervision/ChildProcessLauncher.cs src/Winix.ProcessSupervision/ChildProcessRunner.cs tests/Winix.ProcessSupervision.Tests/ChildProcessLauncherTests.cs
git commit -m "refactor(process-supervision): extract ChildProcessLauncher (spawn+classify) for runfor reuse"
```

---

### Task 2: `TerminationOutcome` + `TerminateAtDeadline` dispatcher

**Files:**
- Create: `src/Winix.ProcessSupervision/TerminationOutcome.cs`
- Modify: `src/Winix.ProcessSupervision/ProcessTreeTerminator.cs`
- Test: `tests/Winix.ProcessSupervision.Tests/TerminateAtDeadlineTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.ProcessSupervision.Tests --filter TerminateAtDeadlineTests`
Expected: FAIL to compile — `TerminationOutcome` and `TerminateAtDeadline` do not exist.

- [ ] **Step 3: Create `TerminationOutcome.cs`**

```csharp
namespace Winix.ProcessSupervision;

/// <summary>
/// The result of a deadline-driven termination attempt (<see cref="ProcessTreeTerminator.TerminateAtDeadline"/>).
/// Distinguishes "the child is gone" from "we tried to kill it and couldn't" from "we only sent a
/// signal with no kill guarantee" — the last is the coreutils-faithful default and is NOT an error.
/// </summary>
public enum TerminationOutcome
{
    /// <summary>The child (and its tree, where a kill was issued) is confirmed exited.</summary>
    ConfirmedDead,

    /// <summary>A kill was attempted but the process may still be alive (e.g. EPERM — a child owned
    /// by another user). The caller should warn the user the child may still be running.</summary>
    KillFailed,

    /// <summary>Coreutils-default signal-only mode: the signal was sent to the direct child with NO
    /// SIGKILL backstop. The child may legitimately still be running (it ignored the signal) — this
    /// is `timeout` semantics, not a failure. The caller does NOT warn.</summary>
    SignalSentNoGuarantee,
}
```

- [ ] **Step 4: Add `TerminateAtDeadline` to `ProcessTreeTerminator.cs`**

Add this method (and a `using System;` is already present). It reuses the existing private `ConfirmExited` / `HasExitedSafe` and the public `KillTree` / `TerminateGracefully`:

```csharp
/// <summary>
/// The deadline-termination dispatcher used by <c>runfor</c>. Encapsulates the platform × mode
/// matrix so the orchestrator stays platform-agnostic:
/// <list type="bullet">
/// <item>Windows/unsupported (any mode): no signal model — kill the tree immediately (ADR D7).</item>
/// <item>Unix, <paramref name="killAfter"/> set: SIGTERM → grace → SIGKILL-tree backstop
///   (<see cref="TerminateGracefully"/>).</item>
/// <item>Unix, <paramref name="killAfter"/> null: coreutils-faithful default — send
///   <paramref name="signal"/> to the direct child ONCE, no backstop. A child that ignores it
///   survives (Troy ratified 2026-06-15).</item>
/// </list>
/// </summary>
/// <param name="process">The child to terminate.</param>
/// <param name="signal">The Unix signal to send (ignored on Windows). See <see cref="NativeProcess"/>.</param>
/// <param name="killAfter">Grace window before the SIGKILL backstop. <c>null</c> ⇒ coreutils default
/// (signal-only, no backstop). A value (incl. <see cref="TimeSpan.Zero"/>) ⇒ escalate to SIGKILL-tree
/// if the child has not exited after the grace.</param>
/// <returns>A <see cref="TerminationOutcome"/> describing what happened — see that enum's members.</returns>
public static TerminationOutcome TerminateAtDeadline(Process process, int signal, TimeSpan? killAfter)
{
    if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
    {
        // Windows / unsupported: kill the tree immediately — there is no graceful signal model.
        KillTree(process);
        return ConfirmExited(process) ? TerminationOutcome.ConfirmedDead : TerminationOutcome.KillFailed;
    }

    if (killAfter.HasValue)
    {
        return TerminateGracefully(process, signal, killAfter.Value)
            ? TerminationOutcome.ConfirmedDead
            : TerminationOutcome.KillFailed;
    }

    // Coreutils default: signal-only, no backstop. Re-check HasExited to narrow the PID-reuse window
    // (see NativeProcess.SendSignal remarks); if it is already gone, report ConfirmedDead.
    if (HasExitedSafe(process)) { return TerminationOutcome.ConfirmedDead; }

    int pid;
    try { pid = process.Id; }
    catch (InvalidOperationException) { return TerminationOutcome.ConfirmedDead; }

    // errno deliberately NOT surfaced (review F3 — accepted coreutils-parity limitation): in default
    // signal-only mode, both "child ignored the signal" (benign) AND "kill(2) returned EPERM, child
    // owned by another user" collapse to SignalSentNoGuarantee → kill_failed:false. coreutils `timeout`
    // is identical (it exits 124 with the child possibly alive); --kill-after is the escape hatch that
    // DOES surface a failed kill (KillFailed). Documented in known-issues.
    NativeProcess.SendSignal(pid, signal);
    return TerminationOutcome.SignalSentNoGuarantee;
}
```

- [ ] **Step 5: Run tests to verify pass**

Run: `dotnet test tests/Winix.ProcessSupervision.Tests --filter TerminateAtDeadlineTests`
Expected: PASS on the host platform (Unix cases run on Linux/macOS, the Windows case runs on Windows; the others Skip). Full CI verifies all three OS.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.ProcessSupervision/TerminationOutcome.cs src/Winix.ProcessSupervision/ProcessTreeTerminator.cs tests/Winix.ProcessSupervision.Tests/TerminateAtDeadlineTests.cs
git commit -m "feat(process-supervision): add TerminateAtDeadline dispatcher (coreutils-faithful default + TerminationOutcome)"
```

---

### Task 3: `UnixSignal` name parser

**Files:**
- Create: `src/Winix.ProcessSupervision/UnixSignal.cs`
- Test: `tests/Winix.ProcessSupervision.Tests/UnixSignalTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Xunit;

namespace Winix.ProcessSupervision.Tests;

public class UnixSignalTests
{
    [Theory]
    [InlineData("TERM", 15)]
    [InlineData("term", 15)]
    [InlineData("SIGTERM", 15)]
    [InlineData("HUP", 1)]
    [InlineData("INT", 2)]
    [InlineData("QUIT", 3)]
    [InlineData("KILL", 9)]
    public void TryParse_KnownNames_ReturnsNumber(string name, int expected)
    {
        Assert.True(UnixSignal.TryParse(name, out int signal));
        Assert.Equal(expected, signal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("BOGUS")]
    [InlineData("SIGBOGUS")]
    [InlineData("9")] // numeric form not supported in v1 — names only
    public void TryParse_UnknownOrEmpty_ReturnsFalse(string name)
    {
        Assert.False(UnixSignal.TryParse(name, out int signal));
        Assert.Equal(0, signal);
    }

    [Fact]
    public void ToName_KnownNumber_RoundTrips()
    {
        Assert.Equal("TERM", UnixSignal.ToName(15));
        Assert.Equal("KILL", UnixSignal.ToName(9));
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/Winix.ProcessSupervision.Tests --filter UnixSignalTests`
Expected: FAIL to compile — `UnixSignal` does not exist.

- [ ] **Step 3: Create `UnixSignal.cs`**

```csharp
using System;

namespace Winix.ProcessSupervision;

/// <summary>
/// Maps the small set of signal NAMES <c>runfor --signal</c> accepts (HUP/INT/QUIT/KILL/TERM, with
/// an optional <c>SIG</c> prefix) to their numbers, and back. v1 accepts names only, not numbers,
/// to keep the surface tight and the help finite.
/// </summary>
public static class UnixSignal
{
    /// <summary>SIGTERM (15) — the default deadline signal.</summary>
    public const int DefaultSignal = 15;

    /// <summary>
    /// Parses a signal name (case-insensitive, optional <c>SIG</c> prefix) into its number.
    /// </summary>
    /// <returns><c>true</c> and sets <paramref name="signal"/> for a known name; otherwise <c>false</c>
    /// and <paramref name="signal"/> = 0.</returns>
    public static bool TryParse(string name, out int signal)
    {
        signal = 0;
        if (string.IsNullOrWhiteSpace(name)) { return false; }

        string n = name.Trim().ToUpperInvariant();
        if (n.StartsWith("SIG", StringComparison.Ordinal)) { n = n.Substring(3); }

        switch (n)
        {
            case "HUP": signal = NativeProcess.SigHup; return true;
            case "INT": signal = NativeProcess.SigInt; return true;
            case "QUIT": signal = NativeProcess.SigQuit; return true;
            case "KILL": signal = NativeProcess.SigKill; return true;
            case "TERM": signal = NativeProcess.SigTerm; return true;
            default: return false;
        }
    }

    /// <summary>Returns the canonical name for a known signal number, or the number as a string.</summary>
    public static string ToName(int signal)
    {
        switch (signal)
        {
            case NativeProcess.SigHup: return "HUP";
            case NativeProcess.SigInt: return "INT";
            case NativeProcess.SigQuit: return "QUIT";
            case NativeProcess.SigKill: return "KILL";
            case NativeProcess.SigTerm: return "TERM";
            default: return signal.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/Winix.ProcessSupervision.Tests --filter UnixSignalTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.ProcessSupervision/UnixSignal.cs tests/Winix.ProcessSupervision.Tests/UnixSignalTests.cs
git commit -m "feat(process-supervision): add UnixSignal name<->number parser for runfor --signal"
```

---

## Phase B — runfor library core

> Create the `Winix.RunFor` project first so subsequent tasks compile. Do this as part of Task 4's setup.

### Task 4: `Winix.RunFor` project + `RunForOutcome` + `RunForResult`

**Files:**
- Create: `src/Winix.RunFor/Winix.RunFor.csproj`
- Create: `src/Winix.RunFor/RunForOutcome.cs`
- Create: `src/Winix.RunFor/RunForResult.cs`
- Create: `tests/Winix.RunFor.Tests/Winix.RunFor.Tests.csproj`
- Create: `tests/Winix.RunFor.Tests/RunForResultTests.cs`

- [ ] **Step 1: Create the library csproj**

`src/Winix.RunFor/Winix.RunFor.csproj` (internal library — not packed; mirrors `Winix.Retry.csproj`'s library, which references ShellKit and is consumed by the console app):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.ProcessSupervision\Winix.ProcessSupervision.csproj" />
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
  </ItemGroup>
</Project>
```

> Verify the exact property set against `src/Winix.Retry/Winix.Retry.csproj` and match it (e.g. `<Nullable>`, analyzer flags). Use that file as the source of truth for library-csproj conventions if it differs from the above.

- [ ] **Step 2: Create the test csproj**

`tests/Winix.RunFor.Tests/Winix.RunFor.Tests.csproj` (mirror `Winix.ProcessSupervision.Tests.csproj`, incl. `UseSystemResourceKeys` and SkippableFact):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <UseSystemResourceKeys>true</UseSystemResourceKeys>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.*" />
    <PackageReference Include="Xunit.SkippableFact" Version="1.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Winix.RunFor\Winix.RunFor.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write the failing test**

`tests/Winix.RunFor.Tests/RunForResultTests.cs`:

```csharp
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
        Assert.Equal(SupervisionExitCode.Interrupted, r.ExitCode);
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
```

- [ ] **Step 4: Create `RunForOutcome.cs`**

```csharp
namespace Winix.RunFor;

/// <summary>How a <c>runfor</c> invocation ended.</summary>
public enum RunForOutcome
{
    /// <summary>The child exited on its own before the deadline; its code is forwarded.</summary>
    Completed,

    /// <summary>The deadline fired; the child was terminated and runfor returns 124.</summary>
    TimedOut,

    /// <summary>Ctrl+C: the child tree was terminated and runfor returns 130.</summary>
    Interrupted,

    /// <summary>The child never started (not found / not executable); runfor returns 127/126.</summary>
    LaunchFailed,
}
```

- [ ] **Step 5: Create `RunForResult.cs`**

```csharp
using System;
using Winix.ProcessSupervision;

namespace Winix.RunFor;

/// <summary>
/// The immutable outcome of a <c>runfor</c> invocation. Library-produced (the caller only observes
/// it), so all properties are get-only; construct via the static factories.
/// </summary>
public sealed class RunForResult
{
    /// <summary>How the invocation ended.</summary>
    public RunForOutcome Outcome { get; private init; }

    /// <summary>runfor's own exit code (forwarded child code, 124, 130, or 126/127).</summary>
    public int ExitCode { get; private init; }

    /// <summary>The child's exit code when it ran to completion; <c>null</c> for timeout/interrupt/launch-fail.</summary>
    public int? ChildExitCode { get; private init; }

    /// <summary>True when a kill was attempted at the deadline/interrupt but could not be confirmed
    /// (the child may still be running). Surfaced as a warning. Always false for the coreutils
    /// signal-only default and for a clean completion.</summary>
    public bool KillFailed { get; private init; }

    /// <summary>Wall-clock time from launch to resolution.</summary>
    public TimeSpan Duration { get; private init; }

    /// <summary>The child exited before the deadline; forward its code.</summary>
    public static RunForResult Completed(int childExitCode, TimeSpan duration) => new()
    {
        Outcome = RunForOutcome.Completed,
        ExitCode = childExitCode,
        ChildExitCode = childExitCode,
        Duration = duration,
    };

    /// <summary>The deadline fired; runfor returns 124.</summary>
    public static RunForResult TimedOut(TimeSpan duration, bool killFailed) => new()
    {
        Outcome = RunForOutcome.TimedOut,
        ExitCode = SupervisionExitCode.Timeout,
        ChildExitCode = null,
        KillFailed = killFailed,
        Duration = duration,
    };

    /// <summary>Ctrl+C; runfor returns 130.</summary>
    public static RunForResult Interrupted(TimeSpan duration, bool killFailed) => new()
    {
        Outcome = RunForOutcome.Interrupted,
        ExitCode = SupervisionExitCode.Interrupted,
        ChildExitCode = null,
        KillFailed = killFailed,
        Duration = duration,
    };

    /// <summary>The child never started; <paramref name="exitCode"/> is the classified 126/127.</summary>
    public static RunForResult LaunchFailed(int exitCode, TimeSpan duration) => new()
    {
        Outcome = RunForOutcome.LaunchFailed,
        ExitCode = exitCode,
        ChildExitCode = null,
        Duration = duration,
    };
}
```

- [ ] **Step 6: Run tests + commit**

Run: `dotnet test tests/Winix.RunFor.Tests --filter RunForResultTests` → PASS.

```bash
git add src/Winix.RunFor/ tests/Winix.RunFor.Tests/
git commit -m "feat(runfor): add Winix.RunFor project, RunForOutcome, immutable RunForResult"
```

---

### Task 5: `RunForOptions` (with validation)

**Files:**
- Create: `src/Winix.RunFor/RunForOptions.cs`
- Test: `tests/Winix.RunFor.Tests/RunForOptionsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using Winix.ProcessSupervision;
using Xunit;

namespace Winix.RunFor.Tests;

public class RunForOptionsTests
{
    [Fact]
    public void Constructs_WithDefaults()
    {
        var o = new RunForOptions(TimeSpan.FromSeconds(5), UnixSignal.DefaultSignal, killAfter: null);
        Assert.Equal(TimeSpan.FromSeconds(5), o.Deadline);
        Assert.Equal(15, o.Signal);
        Assert.Null(o.KillAfter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ZeroOrNegativeDeadline_Throws(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RunForOptions(TimeSpan.FromSeconds(seconds), UnixSignal.DefaultSignal, null));
    }

    [Fact]
    public void NegativeKillAfter_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RunForOptions(TimeSpan.FromSeconds(5), UnixSignal.DefaultSignal, TimeSpan.FromSeconds(-1)));
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/Winix.RunFor.Tests --filter RunForOptionsTests`
Expected: FAIL to compile — `RunForOptions` does not exist.

- [ ] **Step 3: Create `RunForOptions.cs`**

```csharp
using System;

namespace Winix.RunFor;

/// <summary>Validated configuration for one <c>runfor</c> invocation.</summary>
public sealed class RunForOptions
{
    /// <summary>The time budget before the deadline fires. Must be positive.</summary>
    public TimeSpan Deadline { get; }

    /// <summary>The Unix signal number sent at the deadline (default SIGTERM). Ignored on Windows.</summary>
    public int Signal { get; }

    /// <summary>The <c>--kill-after</c> grace window. <c>null</c> ⇒ coreutils default (signal-only,
    /// no SIGKILL backstop). A value ⇒ escalate to SIGKILL-tree after the grace.</summary>
    public TimeSpan? KillAfter { get; }

    /// <param name="deadline">Positive time budget.</param>
    /// <param name="signal">Signal number (see <see cref="Winix.ProcessSupervision.UnixSignal"/>).</param>
    /// <param name="killAfter">Grace window, or null for the signal-only default. Must be non-negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">Deadline is non-positive, or killAfter is negative.</exception>
    public RunForOptions(TimeSpan deadline, int signal, TimeSpan? killAfter)
    {
        if (deadline <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(deadline), deadline, "Deadline must be positive.");
        }
        if (killAfter.HasValue && killAfter.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(killAfter), killAfter, "Kill-after grace cannot be negative.");
        }

        Deadline = deadline;
        Signal = signal;
        KillAfter = killAfter;
    }
}
```

- [ ] **Step 4: Run + commit**

Run: `dotnet test tests/Winix.RunFor.Tests --filter RunForOptionsTests` → PASS.

```bash
git add src/Winix.RunFor/RunForOptions.cs tests/Winix.RunFor.Tests/RunForOptionsTests.cs
git commit -m "feat(runfor): add validated RunForOptions"
```

---

### Task 6: Seam interfaces + `RunForRunner` orchestrator (the heart)

**Files:**
- Create: `src/Winix.RunFor/ISupervisedChild.cs`
- Create: `src/Winix.RunFor/IChildStarter.cs`
- Create: `src/Winix.RunFor/RunForRunner.cs`
- Create: `tests/Winix.RunFor.Tests/FakeChild.cs`
- Create: `tests/Winix.RunFor.Tests/RunForRunnerTests.cs`

- [ ] **Step 1: Create the seam interfaces**

`src/Winix.RunFor/ISupervisedChild.cs`:

```csharp
using System;
using Winix.ProcessSupervision;

namespace Winix.RunFor;

/// <summary>
/// A started child process under <c>runfor</c>'s deadline supervision. The seam through which tests
/// model child lifecycle TIMING (exits-in-time vs runs-past-deadline) without a real process — the
/// CLIo BUG-010 lesson: the fake must reproduce timing, not just a final state.
/// </summary>
public interface ISupervisedChild : IDisposable
{
    /// <summary>
    /// Blocks until the child exits, the <paramref name="timeout"/> elapses, or
    /// <paramref name="cancellationToken"/> is signalled.
    /// </summary>
    /// <returns><c>true</c> iff the child exited within the timeout and was not cancelled;
    /// <c>false</c> on timeout OR cancellation (the caller inspects the token to tell them apart).</returns>
    bool WaitForExit(TimeSpan timeout, System.Threading.CancellationToken cancellationToken);

    /// <summary>The child's exit code. Only valid after <see cref="WaitForExit"/> returned <c>true</c>.</summary>
    int ExitCode { get; }

    /// <summary>
    /// Terminates the child at the deadline/interrupt per the platform × mode matrix
    /// (<see cref="ProcessTreeTerminator.TerminateAtDeadline"/>).
    /// </summary>
    /// <param name="signal">Signal to send (Unix).</param>
    /// <param name="killAfter">Grace before the SIGKILL backstop; <c>null</c> ⇒ signal-only default.</param>
    TerminationOutcome Terminate(int signal, TimeSpan? killAfter);
}
```

`src/Winix.RunFor/IChildStarter.cs`:

```csharp
using System.Collections.Generic;

namespace Winix.RunFor;

/// <summary>Starts a supervised child. The injection point for the in-process test fake.</summary>
public interface IChildStarter
{
    /// <summary>Starts <paramref name="command"/> with <paramref name="arguments"/>.</summary>
    /// <exception cref="Yort.ShellKit.CommandNotFoundException">Command not found on PATH.</exception>
    /// <exception cref="Yort.ShellKit.CommandNotExecutableException">Command exists but cannot run.</exception>
    ISupervisedChild Start(string command, IReadOnlyList<string> arguments);
}
```

- [ ] **Step 2: Write the failing tests + fake**

`tests/Winix.RunFor.Tests/FakeChild.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Winix.ProcessSupervision;
using Yort.ShellKit;

namespace Winix.RunFor.Tests;

/// <summary>
/// Lifecycle-timing fake. <see cref="ExitsWithinDeadline"/> drives whether <see cref="WaitForExit"/>
/// reports the child finishing in time; the test controls the CancellationToken externally to model
/// Ctrl+C. Records the termination call so the decision tree can be asserted.
/// </summary>
internal sealed class FakeChild : ISupervisedChild
{
    public bool ExitsWithinDeadline { get; init; }
    public int FakeExitCode { get; init; }
    public TerminationOutcome TerminateResult { get; init; } = TerminationOutcome.ConfirmedDead;

    public int TerminateCallCount { get; private set; }
    public int? LastSignal { get; private set; }
    public TimeSpan? LastKillAfter { get; private set; }
    public bool Disposed { get; private set; }

    public bool WaitForExit(TimeSpan timeout, CancellationToken cancellationToken)
        => ExitsWithinDeadline; // deterministic: no real waiting.

    public int ExitCode => FakeExitCode;

    public TerminationOutcome Terminate(int signal, TimeSpan? killAfter)
    {
        TerminateCallCount++;
        LastSignal = signal;
        LastKillAfter = killAfter;
        return TerminateResult;
    }

    public void Dispose() => Disposed = true;
}

internal sealed class FakeChildStarter : IChildStarter
{
    private readonly Func<ISupervisedChild> _factory;
    public FakeChildStarter(ISupervisedChild child) => _factory = () => child;
    public FakeChildStarter(Func<ISupervisedChild> factory) => _factory = factory;
    public ISupervisedChild Start(string command, IReadOnlyList<string> arguments) => _factory();
}

internal sealed class ThrowingChildStarter : IChildStarter
{
    private readonly Exception _toThrow;
    public ThrowingChildStarter(Exception toThrow) => _toThrow = toThrow;
    public ISupervisedChild Start(string command, IReadOnlyList<string> arguments) => throw _toThrow;
}
```

`tests/Winix.RunFor.Tests/RunForRunnerTests.cs`:

```csharp
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
}
```

> **Verify before implementing:** confirm `Yort.ShellKit.ExitCode.NotFound == 127` and `ExitCode.NotExecutable == 126` by reading `src/Yort.ShellKit/ExitCode.cs`. If the member names differ, use the actual names; the values 127/126 are asserted via the constants, not literals, so a name change is a compile error, not a silent wrong value.

- [ ] **Step 3: Run to verify fail**

Run: `dotnet test tests/Winix.RunFor.Tests --filter RunForRunnerTests`
Expected: FAIL to compile — `RunForRunner` does not exist.

- [ ] **Step 4: Create `RunForRunner.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Winix.ProcessSupervision;
using Yort.ShellKit;

namespace Winix.RunFor;

/// <summary>
/// The pure deadline-orchestration decision tree: launch → wait-against-deadline → forward / 124 / 130
/// / launch-fail. Drives an injected <see cref="IChildStarter"/> so the whole tree is testable with a
/// timing fake.
/// </summary>
public static class RunForRunner
{
    /// <summary>Runs <paramref name="command"/> under the deadline policy in <paramref name="options"/>.</summary>
    /// <param name="starter">The child starter (real or fake).</param>
    /// <param name="cancellationToken">Ctrl+C signal (owned by Program.Main in production).</param>
    public static RunForResult Execute(
        IChildStarter starter,
        string command,
        IReadOnlyList<string> arguments,
        RunForOptions options,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        ISupervisedChild child;
        try
        {
            child = starter.Start(command, arguments);
        }
        catch (CommandNotFoundException)
        {
            return RunForResult.LaunchFailed(ExitCode.NotFound, stopwatch.Elapsed);
        }
        catch (CommandNotExecutableException)
        {
            return RunForResult.LaunchFailed(ExitCode.NotExecutable, stopwatch.Elapsed);
        }

        using (child)
        {
            bool exited = child.WaitForExit(options.Deadline, cancellationToken);

            // Ctrl+C takes priority over a coincident deadline: the user asked to stop.
            if (cancellationToken.IsCancellationRequested)
            {
                // Prompt ensure-dead (grace 0 = signal then immediate SIGKILL backstop). The child has
                // usually already received the interrupt from the terminal's foreground process group.
                TerminationOutcome outcome = child.Terminate(options.Signal, TimeSpan.Zero);
                return RunForResult.Interrupted(stopwatch.Elapsed, outcome == TerminationOutcome.KillFailed);
            }

            if (!exited)
            {
                TerminationOutcome outcome = child.Terminate(options.Signal, options.KillAfter);
                return RunForResult.TimedOut(stopwatch.Elapsed, outcome == TerminationOutcome.KillFailed);
            }

            return RunForResult.Completed(child.ExitCode, stopwatch.Elapsed);
        }
    }
}
```

- [ ] **Step 5 (review F4): pin "Ctrl+C wins over a coincident clean exit"**

The orchestrator checks the token BEFORE `!exited`, so a child that exits 0 exactly as Ctrl+C arrives is reported Interrupted/130 (its real exit code discarded). That is a deliberate decision — pin it. Add to `RunForRunnerTests`:

```csharp
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
}
```

> Note: `FakeChild.WaitForExit` ignores the token (returns `ExitsWithinDeadline`), so with `ExitsWithinDeadline=true` the orchestrator's `exited` is true but the token-check short-circuits first — exactly the coincident-race shape. The real `ProcessSupervisedChild.WaitForExit` distinguishes these via `OperationCanceledException`; that real path is integration-covered (Task 11), this fake pins the orchestrator's priority decision.

- [ ] **Step 6: Run + commit**

Run: `dotnet test tests/Winix.RunFor.Tests --filter RunForRunnerTests` → PASS.

```bash
git add src/Winix.RunFor/ISupervisedChild.cs src/Winix.RunFor/IChildStarter.cs src/Winix.RunFor/RunForRunner.cs tests/Winix.RunFor.Tests/FakeChild.cs tests/Winix.RunFor.Tests/RunForRunnerTests.cs
git commit -m "feat(runfor): add child-supervision seam + RunForRunner decision-tree orchestrator"
```

---

### Task 7: `Formatting` (`--json` + plain notice)

**Files:**
- Create: `src/Winix.RunFor/Formatting.cs`
- Test: `tests/Winix.RunFor.Tests/FormattingTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using Xunit;

namespace Winix.RunFor.Tests;

public class FormattingTests
{
    [Fact]
    public void FormatJson_TimedOut_HasEnvelopeAndTimeoutFields()
    {
        RunForResult r = RunForResult.TimedOut(TimeSpan.FromMilliseconds(5003), killFailed: false);
        string json = Formatting.FormatJson(r, "runfor", "1.2.3", signalName: "TERM");

        Assert.Contains("\"tool\":\"runfor\"", json);
        Assert.Contains("\"version\":\"1.2.3\"", json);
        Assert.Contains("\"exit_code\":124", json);
        Assert.Contains("\"outcome\":\"timed_out\"", json);
        Assert.Contains("\"timed_out\":true", json);
        Assert.Contains("\"child_exit_code\":null", json);
        Assert.Contains("\"signal\":\"TERM\"", json);
        Assert.Contains("\"kill_failed\":false", json);
        Assert.Contains("\"duration_ms\":5003", json);
    }

    [Fact]
    public void FormatJson_Completed_CarriesChildCode_TimedOutFalse()
    {
        RunForResult r = RunForResult.Completed(0, TimeSpan.FromSeconds(1));
        string json = Formatting.FormatJson(r, "runfor", "1.2.3", signalName: "TERM");

        Assert.Contains("\"outcome\":\"completed\"", json);
        Assert.Contains("\"timed_out\":false", json);
        Assert.Contains("\"child_exit_code\":0", json);
    }

    [Fact]
    public void FormatNotice_TimedOut_MentionsDeadline()
    {
        string notice = Formatting.FormatNotice(
            RunForResult.TimedOut(TimeSpan.FromSeconds(5), killFailed: false),
            command: "sleep", deadline: TimeSpan.FromSeconds(5), useColor: false);

        Assert.Contains("runfor", notice);
        Assert.Contains("timed out", notice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sleep", notice);
    }
}
```

- [ ] **Step 2: Run to verify fail.** `dotnet test tests/Winix.RunFor.Tests --filter FormattingTests` → FAIL (no `Formatting`).

- [ ] **Step 3: Create `Formatting.cs`** (mirror `Winix.Retry/Formatting.cs`'s `JsonHelper` usage exactly):

```csharp
using System;
using Yort.ShellKit;

namespace Winix.RunFor;

/// <summary>JSON envelope and human stderr notice for runfor.</summary>
public static class Formatting
{
    /// <summary>
    /// The <c>--json</c> envelope: standard fields (tool/version/exit_code) then runfor-specific
    /// (outcome/timed_out/child_exit_code/signal/kill_failed/duration_ms).
    /// </summary>
    public static string FormatJson(RunForResult result, string toolName, string version, string signalName)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", result.ExitCode);
            writer.WriteString("outcome", OutcomeToString(result.Outcome));
            writer.WriteBoolean("timed_out", result.Outcome == RunForOutcome.TimedOut);
            if (result.ChildExitCode.HasValue)
            {
                writer.WriteNumber("child_exit_code", result.ChildExitCode.Value);
            }
            else
            {
                writer.WriteNull("child_exit_code");
            }
            writer.WriteString("signal", signalName);
            writer.WriteBoolean("kill_failed", result.KillFailed);
            writer.WriteNumber("duration_ms", (long)result.Duration.TotalMilliseconds);
            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }

    /// <summary>A terse one-line stderr notice for non-JSON mode (timeout/interrupt only; silent on
    /// clean completion). <paramref name="command"/> is the child executable name for context.</summary>
    public static string FormatNotice(RunForResult result, string command, TimeSpan deadline, bool useColor)
    {
        string yellow = AnsiColor.Yellow(useColor);
        string reset = AnsiColor.Reset(useColor);
        string warn = result.KillFailed
            ? $" {AnsiColor.Red(useColor)}(could not terminate child — it may still be running){reset}"
            : string.Empty;

        return result.Outcome switch
        {
            RunForOutcome.TimedOut =>
                $"runfor: {yellow}timed out{reset} after {DisplayFormat.FormatDuration(deadline)}: {command}{warn}",
            RunForOutcome.Interrupted =>
                $"runfor: {yellow}interrupted{reset}: {command}{warn}",
            _ => string.Empty, // Completed/LaunchFailed are handled by the caller, not this notice.
        };
    }

    private static string OutcomeToString(RunForOutcome outcome) => outcome switch
    {
        RunForOutcome.Completed => "completed",
        RunForOutcome.TimedOut => "timed_out",
        RunForOutcome.Interrupted => "interrupted",
        RunForOutcome.LaunchFailed => "launch_failed",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "unhandled runfor outcome"),
    };
}
```

> **Verify before implementing:** confirm `AnsiColor.Yellow/Red/Reset(bool)` and `DisplayFormat.FormatDuration(TimeSpan)` signatures against `src/Yort.ShellKit/` (retry's `Formatting.cs` uses both — copy its call shapes).

- [ ] **Step 4 (review F6): pin that a clean completion produces NO notice**

Add to `FormattingTests` (regression guard: runfor must be silent on success, never chatter "completed" to stderr):

```csharp
[Fact]
public void FormatNotice_Completed_IsEmpty()
{
    string notice = Formatting.FormatNotice(
        RunForResult.Completed(0, TimeSpan.FromSeconds(1)),
        command: "echo", deadline: TimeSpan.FromSeconds(5), useColor: false);
    Assert.Equal(string.Empty, notice);
}
```

- [ ] **Step 5: Run + commit.** `dotnet test tests/Winix.RunFor.Tests --filter FormattingTests` → PASS.

```bash
git add src/Winix.RunFor/Formatting.cs tests/Winix.RunFor.Tests/FormattingTests.cs
git commit -m "feat(runfor): add JSON envelope + stderr notice formatting"
```

---

### Task 8: Real `ProcessSupervisedChild` + `ProcessChildStarter`

**Files:**
- Create: `src/Winix.RunFor/ProcessSupervisedChild.cs`
- Create: `src/Winix.RunFor/ProcessChildStarter.cs`

(No standalone unit test — these wrap the BCL `Process`; they are exercised end-to-end by the real-process integration test in Task 11 and the smokes. A unit test here would just re-test `Process`/the shared lib.)

- [ ] **Step 1: Create `ProcessSupervisedChild.cs`**

```csharp
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Winix.ProcessSupervision;

namespace Winix.RunFor;

/// <summary>Real <see cref="ISupervisedChild"/> over a BCL <see cref="Process"/>.</summary>
internal sealed class ProcessSupervisedChild : ISupervisedChild
{
    private readonly Process _process;

    public ProcessSupervisedChild(Process process) => _process = process;

    public int ExitCode => _process.ExitCode;

    public bool WaitForExit(TimeSpan timeout, CancellationToken cancellationToken)
    {
        int ms = timeout <= TimeSpan.Zero ? 0 : (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);
        try
        {
            // WaitForExitAsync completes when the child exits; the token aborts the wait on Ctrl+C.
            // .Wait(ms) bounds it by the deadline (returns false on timeout). Blocking the calling
            // thread is fine — runfor is a single-purpose console tool driven from Program.Main, not
            // a thread-pool work item (so the pool-starvation class in the memory notes does not apply).
            return _process.WaitForExitAsync(cancellationToken).Wait(ms);
        }
        catch (OperationCanceledException)
        {
            return false; // Ctrl+C — caller checks the token to distinguish from a deadline timeout.
        }
        catch (AggregateException)
        {
            // .Wait wraps a faulted/cancelled task; treat as "did not exit cleanly" — the caller's
            // token check decides interrupt vs timeout.
            return false;
        }
    }

    public TerminationOutcome Terminate(int signal, TimeSpan? killAfter)
        => ProcessTreeTerminator.TerminateAtDeadline(_process, signal, killAfter);

    public void Dispose() => _process.Dispose();
}
```

- [ ] **Step 2: Create `ProcessChildStarter.cs`**

```csharp
using System.Collections.Generic;
using Winix.ProcessSupervision;

namespace Winix.RunFor;

/// <summary>Production <see cref="IChildStarter"/>: spawns a real process via the shared launcher.</summary>
public sealed class ProcessChildStarter : IChildStarter
{
    /// <inheritdoc />
    public ISupervisedChild Start(string command, IReadOnlyList<string> arguments)
        => new ProcessSupervisedChild(ChildProcessLauncher.Launch(command, arguments));
}
```

- [ ] **Step 3: Build + commit.** `dotnet build src/Winix.RunFor` → 0 warnings.

```bash
git add src/Winix.RunFor/ProcessSupervisedChild.cs src/Winix.RunFor/ProcessChildStarter.cs
git commit -m "feat(runfor): add real Process-backed child starter/supervisor"
```

---

### Task 9: `Cli.Run` (parse → orchestrate → format → exit)

**Files:**
- Create: `src/Winix.RunFor/Cli.cs`
- Test: `tests/Winix.RunFor.Tests/CliRunTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
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
        int code = Run(new[] { "--describe" }, out string outText, out _);
        Assert.Equal(0, code);
        Assert.Contains("\"tool\":\"runfor\"", outText);
    }

    [Fact]
    public void NoArgs_UsageError()
    {
        int code = Run(Array.Empty<string>(), out _, out string err);
        Assert.Equal(ExitCode.UsageError, code);
        Assert.Contains("DURATION", err);
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
        Assert.Contains("\"timed_out\":true", err);   // --json envelope goes to stderr (stdout stays clean)
    }
}
```

> **Verify before implementing:** confirm `ExitCode.UsageError` is the member name (retry uses `ExitCode.UsageError`). Confirm the parser routes `--describe` to `result.IsHandled` with exit 0 and writes to the `stdout` passed to `Run` (the contract test relies on this).

- [ ] **Step 2: Run to verify fail.** `dotnet test tests/Winix.RunFor.Tests --filter CliRunTests` → FAIL (no `Cli`).

- [ ] **Step 3: Create `Cli.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Winix.ProcessSupervision;
using Yort.ShellKit;

namespace Winix.RunFor;

/// <summary>
/// Library entry point for runfor: parse → build options → run the deadline orchestrator → format →
/// exit. <c>Program.Main</c> is a thin shell owning Ctrl+C. runfor's own output (the stderr notice and
/// the --json envelope) goes to <paramref name="stderr"/>; the child inherits the real stdout/stderr,
/// so runfor never writes to the child's stdout.
/// </summary>
public static class Cli
{
    /// <summary>Runs the runfor CLI.</summary>
    /// <param name="args">Raw command-line args: <c>[options] DURATION -- command [args...]</c>.</param>
    /// <param name="stdout">Used only for parser introspection (--help/--describe/--version).</param>
    /// <param name="stderr">runfor's notice + --json envelope + all errors.</param>
    /// <param name="cancellationToken">Ctrl+C (owned by Program.Main).</param>
    /// <param name="starter">Child starter; defaults to the real process starter. Injected in tests.</param>
    /// <returns>Forwarded child code, 124 (deadline), 130 (Ctrl+C), or 125/126/127 (runfor errors).</returns>
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken,
        IChildStarter? starter = null)
    {
        string version = GetVersion();

        var parser = new CommandLineParser("runfor", version)
            .Description("Run a command with a time limit — cross-platform timeout(1).")
            .Maturity(ToolMaturity.Fresh)
            .StandardFlags()
            .Positional("DURATION")
            .Option("--signal", "-s", "NAME", "Signal sent at the deadline on Unix: TERM (default), HUP, INT, QUIT, KILL. Ignored on Windows.")
            .Option("--kill-after", "-k", "GRACE", "Unix: after the deadline signal, wait GRACE then SIGKILL the tree. No-op on Windows (kills immediately).")
            .ExitCodes(
                (0, "Child exited 0 before the deadline (or forwarded child code 1–123)"),
                (SupervisionExitCode.Timeout, "Deadline exceeded — the child was terminated"),
                (SupervisionExitCode.Interrupted, "Interrupted by Ctrl+C"),
                (ExitCode.UsageError, "Usage error: missing/invalid DURATION, no command, bad --signal/--kill-after"),
                (ExitCode.NotExecutable, "Command not executable"),
                (ExitCode.NotFound, "Command not found on PATH"))
            .Platform("cross-platform",
                replaces: new[] { "timeout" },
                valueOnWindows: "Windows timeout.exe only SLEEPS — it does not bound a command; runfor actually enforces a deadline",
                valueOnUnix: "Same role as coreutils timeout, with a consistent --json envelope and exit-code family across platforms")
            .StdinDescription("Not used (child process inherits stdin)")
            .StdoutDescription("Child process stdout passes through unmodified")
            .StderrDescription("Child stderr passes through; runfor's own notice and --json summary also go here")
            .Example("runfor 30s -- curl https://example.com", "Abort a request after 30 seconds")
            .Example("runfor 5m -- dotnet test", "Cap a test run at 5 minutes")
            .Example("runfor --kill-after 3s 10s -- ./server", "SIGTERM at 10s, SIGKILL 3s later if it ignores it (Unix)")
            .Example("runfor --signal INT 1m -- ./job", "Send SIGINT instead of SIGTERM at the deadline (Unix)")
            .JsonField("tool", "string", "Tool name (\"runfor\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("exit_code", "int", "runfor's exit code (child's on completion, 124 timeout, 130 interrupt, 126/127 launch)")
            .JsonField("outcome", "string", "completed | timed_out | interrupted | launch_failed")
            .JsonField("timed_out", "bool", "True iff the deadline fired")
            .JsonField("child_exit_code", "int|null", "Child's exit code if it completed, else null")
            .JsonField("signal", "string", "Signal name configured for the deadline (Unix)")
            .JsonField("kill_failed", "bool", "True iff a kill was attempted and could not be confirmed")
            .JsonField("duration_ms", "int", "Wall-clock time from launch to resolution, milliseconds");

        ParseResult result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(stderr); }

        IReadOnlyList<string> positionals = result.Positionals;
        if (positionals.Count == 0)
        {
            return result.WriteError("no DURATION given. Usage: runfor DURATION -- command [args...]", stderr);
        }

        if (!DurationParser.TryParse(positionals[0], out TimeSpan deadline) || deadline <= TimeSpan.Zero)
        {
            return result.WriteError($"invalid DURATION: '{positionals[0]}' (e.g. 500ms, 30s, 5m, 1h)", stderr);
        }

        // positionals[0] is DURATION; everything after is the child command + args. The '--' boundary
        // (non-CommandMode) routes post-'--' tokens into Positionals without flag-parsing, so child
        // flags survive. Use '--' before commands that take their own dashed flags.
        string[] childArgv = positionals.Skip(1).ToArray();
        if (childArgv.Length == 0)
        {
            return result.WriteError("no command given. Usage: runfor DURATION -- command [args...]", stderr);
        }

        int signal = UnixSignal.DefaultSignal;
        if (result.Has("--signal"))
        {
            string sigStr = result.GetString("--signal");
            if (!UnixSignal.TryParse(sigStr, out signal))
            {
                return result.WriteError($"invalid --signal: '{sigStr}' (one of TERM, HUP, INT, QUIT, KILL)", stderr);
            }
        }

        TimeSpan? killAfter = null;
        if (result.Has("--kill-after"))
        {
            string kaStr = result.GetString("--kill-after");
            if (!DurationParser.TryParse(kaStr, out TimeSpan ka) || ka < TimeSpan.Zero)
            {
                return result.WriteError($"invalid --kill-after: '{kaStr}' (e.g. 2s, 500ms)", stderr);
            }
            killAfter = ka;
        }

        bool jsonOutput = result.Has("--json");
        bool useColor = result.ResolveColor(checkStdErr: true);

        var options = new RunForOptions(deadline, signal, killAfter);
        string command = childArgv[0];
        string[] commandArgs = childArgv.Skip(1).ToArray();

        IChildStarter effectiveStarter = starter ?? new ProcessChildStarter();

        RunForResult runResult;
        try
        {
            runResult = RunForRunner.Execute(effectiveStarter, command, commandArgs, options, cancellationToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Safety net: any unexpected exception from the orchestrator/real process surfaces as a
            // readable one-liner rather than an AOT stack-traceless "Unhandled exception" crash.
            string msg = string.IsNullOrEmpty(ex.Message)
                ? $"runfor: unexpected error: {ex.GetType().Name}"
                : $"runfor: unexpected error: {ex.GetType().Name}: {ex.Message}";
            SafeWriteLine(stderr, msg);
            return ExitCode.NotExecutable;
        }

        if (runResult.Outcome == RunForOutcome.LaunchFailed)
        {
            if (jsonOutput)
            {
                SafeWriteLine(stderr, Formatting.FormatJson(runResult, "runfor", version, UnixSignal.ToName(signal)));
            }
            else
            {
                SafeWriteLine(stderr, $"runfor: {(command)}: {ExitReasonText(runResult.ExitCode)}");
            }
            return runResult.ExitCode;
        }

        if (jsonOutput)
        {
            SafeWriteLine(stderr, Formatting.FormatJson(runResult, "runfor", version, UnixSignal.ToName(signal)));
        }
        else
        {
            string notice = Formatting.FormatNotice(runResult, command, deadline, useColor);
            if (!string.IsNullOrEmpty(notice)) { SafeWriteLine(stderr, notice); }
        }

        return runResult.ExitCode;
    }

    private static string ExitReasonText(int exitCode)
    {
        if (exitCode == ExitCode.NotFound) { return "command not found"; }
        if (exitCode == ExitCode.NotExecutable) { return "command not executable"; }
        return "launch failed";
    }

    private static void SafeWriteLine(TextWriter writer, string message)
    {
        try { writer.WriteLine(message); }
        catch (IOException) { /* downstream pipe closed */ }
        catch (ObjectDisposedException) { /* writer disposed */ }
    }

    private static string GetVersion()
    {
        string raw = typeof(RunForResult).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
```

> **Verify before implementing (API shapes against ShellKit + a sibling Cli):**
> - `CommandLineParser` methods used: `.Description`, `.Maturity`, `.StandardFlags`, `.Positional`, `.Option`, `.ExitCodes`, `.Platform`, `.StdinDescription`/`.StdoutDescription`/`.StderrDescription`, `.Example`, `.JsonField`. All are used by retry/online — copy exact signatures. `.Positional(string)` exists (CommandLineParser.cs:195).
> - `ParseResult` members: `.IsHandled`, `.ExitCode`, `.HasErrors`, `.WriteErrors(TextWriter)`, `.WriteError(string, TextWriter)`, `.Positionals`, `.Has(string)`, `.GetString(string)`, `.ResolveColor(bool checkStdErr)`. Confirm `.Positionals` is the property name (CommandLineParser.cs:205 references `ParseResult.Positionals`).
> - Optionally add a `prefer_default_when` hint: check how `online`'s `Cli.cs` calls it (grep `online` for the builder method — likely `.PreferDefaultWhen(...)`) and mirror with text like "you need to actually bound a command's runtime cross-platform (Windows timeout.exe only sleeps)". If the method name cannot be confirmed, omit it (do not fabricate).

- [ ] **Step 4 (review F1/F2/F6/F7/F8): add the negative-invariant CLI tests**

Add these to `CliRunTests` (each pins a decision that is already correct in the code but unguarded):

```csharp
// F1: --kill-after 0 must ESCALATE (TimeSpan.Zero), NOT degrade to the signal-only default (null).
[Fact]
public void KillAfterZero_Escalates_NotSignalOnlyDefault()
{
    var child = new FakeChild { ExitsWithinDeadline = false };
    Run(new[] { "--kill-after", "0s", "5s", "--", "x" }, out _, out _, new FakeChildStarter(child));
    Assert.Equal(TimeSpan.Zero, child.LastKillAfter); // a value, not null → escalation mode
}

// F2: an absurdly large DURATION must not overflow — it either parses or is a clean UsageError.
// VERIFY at implementation: read DurationParser to learn which (parse-ok vs reject). Assert the
// actual behaviour; do not assume. If it parses, the .Wait(ms)/grace clamps already handle the size.
[Fact]
public void HugeDuration_DoesNotOverflow()
{
    var child = new FakeChild { ExitsWithinDeadline = true, FakeExitCode = 0 };
    int code = Run(new[] { "1000000h", "--", "x" }, out _, out _, new FakeChildStarter(child));
    Assert.True(code == 0 || code == ExitCode.UsageError); // tighten to the real behaviour once known
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
// Guards the suite's recurring "happy-path --json leaked to stdout" defect class.
[Fact]
public void CleanCompletion_Json_EnvelopeOnStderr_StdoutEmpty()
{
    var child = new FakeChild { ExitsWithinDeadline = true, FakeExitCode = 0 };
    Run(new[] { "--json", "10s", "--", "x" }, out string outText, out string err, new FakeChildStarter(child));
    Assert.Equal(string.Empty, outText);
    Assert.Contains("\"outcome\":\"completed\"", err);
}

// F8: --color never actually suppresses ANSI in the timeout notice at the Cli seam (not just in
// Formatting). Guards the suite's "--color wired?" class.
[Fact]
public void TimeoutNotice_ColorNever_NoAnsi()
{
    var child = new FakeChild { ExitsWithinDeadline = false };
    Run(new[] { "--color", "never", "5s", "--", "x" }, out _, out string err, new FakeChildStarter(child));
    // Byte-precise: assert the ESC control char is absent (raw-ESC literals are unreliable
    // through tool-arg/source round-trips — see feedback_ansi_test_char27 / xunit_assert_culture_aware).
    Assert.DoesNotContain(((char)27).ToString(), err, StringComparison.Ordinal);
}
```

- [ ] **Step 5: Run + commit.** `dotnet test tests/Winix.RunFor.Tests` → all green.

```bash
git add src/Winix.RunFor/Cli.cs tests/Winix.RunFor.Tests/CliRunTests.cs
git commit -m "feat(runfor): add Cli.Run (parse DURATION -- cmd, --signal/--kill-after, --json)"
```

---

## Phase C — Console app + solution wiring

### Task 10: `src/runfor` console app + `Winix.sln`

**Files:**
- Create: `src/runfor/Program.cs`
- Create: `src/runfor/runfor.csproj`
- Modify: `Winix.sln`

- [ ] **Step 1: Create `Program.cs`** (mirror `src/retry/Program.cs` — Ctrl+C ownership):

```csharp
using System;
using System.Threading;
using Winix.RunFor;
using Yort.ShellKit;

namespace RunFor;

internal sealed class Program
{
    /// <summary>
    /// Entry point. Owns process-global state only: console setup and Ctrl+C handling. All parsing,
    /// validation, and orchestration live in <see cref="Cli.Run"/>.
    /// </summary>
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* raced with shutdown — safe to drop */ }
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            return Cli.Run(args, Console.Out, Console.Error, cts.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
```

- [ ] **Step 2: Create `runfor.csproj`** (mirror `src/retry/retry.csproj`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <OptimizationPreference>Size</OptimizationPreference>
    <StackTraceSupport>false</StackTraceSupport>
    <UseSystemResourceKeys>true</UseSystemResourceKeys>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>runfor</ToolCommandName>
    <PackageId>Winix.RunFor</PackageId>
    <Description>Run a command with a time limit — cross-platform timeout(1) with a graceful Unix kill window.</Description>
    <PackageTags>cli;command-line;cross-platform;windows;macos;linux;aot;dotnet-tool;winix;timeout;deadline;process;watchdog</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.RunFor\Winix.RunFor.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
  <ItemGroup>
    <Content Include="man\man1\runfor.1" CopyToPublishDirectory="PreserveNewest" Link="share\man\man1\runfor.1" />
  </ItemGroup>
</Project>
```

(The `man\man1\runfor.1` content reference is satisfied by Task 13; create the file there before the first publish.)

- [ ] **Step 3: Add the three new projects to `Winix.sln`**

Run (from repo root):

```bash
dotnet sln Winix.sln add src/Winix.RunFor/Winix.RunFor.csproj
dotnet sln Winix.sln add src/runfor/runfor.csproj
dotnet sln Winix.sln add tests/Winix.RunFor.Tests/Winix.RunFor.Tests.csproj
```

- [ ] **Step 4: Build + commit.**

Run: `dotnet build Winix.sln` → 0 warnings (a placeholder `man/man1/runfor.1` may be needed; if the build errors on the missing Content file, create an empty `src/runfor/man/man1/runfor.1` now and fill it in Task 13).

```bash
git add src/runfor/ Winix.sln
git commit -m "feat(runfor): add console app entry point + solution wiring"
```

---

## Phase D — Real-process integration

### Task 11: End-to-end integration test (real process)

**Files:**
- Create: `tests/Winix.RunFor.Tests/ProgramIntegrationTests.cs`

- [ ] **Step 1: Write the tests** (real `ProcessChildStarter`, platform-agnostic where possible):

```csharp
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

    // Unix default mode (no --kill-after) with a TERM-handling child: child exits itself on the
    // signal, runfor forwards... no — at the deadline the child is terminated, runfor returns 124.
    // This asserts the deadline path still returns 124 in signal-only mode with a cooperating child.
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
```

> Note: these reuse `ChildHelpers` from the `Winix.ProcessSupervision.Tests` project. That class is `internal` to *that* assembly, so either (a) add a small copy of the three helpers to `Winix.RunFor.Tests`, or (b) make `ChildHelpers` public and `InternalsVisibleTo`. Prefer (a) — a 15-line local `ChildHelpers` in the runfor test project (duplication is cheaper than a cross-test-assembly coupling; see the protocol-fake/duplication guidance). Create `tests/Winix.RunFor.Tests/ChildHelpers.cs` with `ExitWith`, `SleepSeconds`, `TrapSignalThenSleepUnix` (copy from `tests/Winix.ProcessSupervision.Tests/ChildHelpers.cs`).

- [ ] **Step 2: Run + commit.** `dotnet test tests/Winix.RunFor.Tests` → all green on the host platform.

```bash
git add tests/Winix.RunFor.Tests/ProgramIntegrationTests.cs tests/Winix.RunFor.Tests/ChildHelpers.cs
git commit -m "test(runfor): end-to-end real-process deadline integration (forward / 124 / signal-only)"
```

---

## Phase E — Packaging & docs

### Task 12: `src/runfor/README.md`

**Files:** Create `src/runfor/README.md`.

- [ ] **Step 1:** Follow the `src/retry/README.md` structure exactly: title + one-line description; Install (Scoop / dotnet tool / GitHub release); Usage with the `runfor DURATION -- command [args...]` synopsis; Examples (the four from `Cli.cs` `.Example(...)`); Options table (`--signal`, `--kill-after`, `--json`, plus the standard `--help`/`--version`/`--describe`/`--color`); a **Platform behaviour** subsection stating the coreutils-faithful default (signal-only, child may survive), `--kill-after` escalation, and **Windows kills immediately (no signal model)**; Exit codes table (0 forward, 124 timeout, 130 interrupt, 125/126/127); Colour section (NO_COLOR honoured). Note the `--` boundary: "use `--` before commands that take their own dashed flags."
- [ ] **Step 2 (review F5 — blocker): add a "Limitations" subsection documenting grandchild orphaning.** User-visible per ADR D10; the suite's doc↔behaviour rule classes an undocumented surprise as a ship-defect, and the man-page NOTE alone (Task 13) is insufficient because most users read the README. Add text equivalent to: *"runfor signals only the **direct child** (ADR D10). A child that handles the signal and exits within the `--kill-after` grace may leave its own **grandchildren** running — the SIGKILL tree-backstop only reaps the whole tree when the child **ignores** the signal past grace. For a wrapper that spawns long-lived workers, have it forward the signal to its children."*
- [ ] **Step 3: Commit.** `git add src/runfor/README.md && git commit -m "docs(runfor): add README incl. direct-child-signalling limitation"`

### Task 13: man page (pandoc source → groff)

**Files:** Create `src/runfor/runfor.1.md`, generate `src/runfor/man/man1/runfor.1`.

- [ ] **Step 1:** Write `src/runfor/runfor.1.md` (pandoc source) mirroring `src/retry/retry.1.md`: NAME, SYNOPSIS (`runfor DURATION -- command [args...]`), DESCRIPTION, OPTIONS, EXIT STATUS (0/124/130/125/126/127), EXAMPLES, NOTES (Windows immediate-kill; coreutils-faithful default; direct-child-only signalling per ADR D10), SEE ALSO (`timeout(1)`, `retry(1)`).
- [ ] **Step 2:** Generate the groff page: `pandoc -s -t man src/runfor/runfor.1.md -o src/runfor/man/man1/runfor.1`
- [ ] **Step 3:** Safety-diff the generated `.1` (only intended content + reflow). Confirm `src/runfor/runfor.csproj` `<Content Include="man\man1\runfor.1" ...>` (added in Task 10) now resolves.
- [ ] **Step 4: Commit.** `git add src/runfor/runfor.1.md src/runfor/man/man1/runfor.1 && git commit -m "docs(runfor): add man page (pandoc source + groff)"`

### Task 14: AI agent guide + `llms.txt`

**Files:** Create `docs/ai/runfor.md`; modify `llms.txt`.

- [ ] **Step 1:** Write `docs/ai/runfor.md` mirroring `docs/ai/online.md` (when to reach for it vs the platform default; the Windows `timeout.exe`-is-a-sleep footgun; exit-code family; `--json` shape; composition with `lock`/`retry`).
- [ ] **Step 1b (review F5 — blocker): include the direct-child-signalling limitation** in `docs/ai/runfor.md` (an agent choosing runfor for a process-spawning wrapper must know grandchildren can survive a graceful exit): one line equivalent to the README Limitations note — "runfor signals only the direct child (ADR D10); a child that exits within `--kill-after` grace may orphan grandchildren; the tree-backstop fires only when the child ignores the signal."
- [ ] **Step 2:** Add an `llms.txt` entry mirroring the `online` line (line ~21): `- [runfor](docs/ai/runfor.md): Run a command with a time limit — cross-platform timeout(1)... Exit 0 forward / 124 timeout / 130 interrupt. (fresh)`
- [ ] **Step 3: Commit.** `git add docs/ai/runfor.md llms.txt && git commit -m "docs(runfor): add AI agent guide + llms.txt entry"`

### Task 15: Scoop manifest

**Files:** Create `bucket/runfor.json`.

- [ ] **Step 1:** Copy `bucket/online.json` → `bucket/runfor.json`; change the binary name, description, and any tool-specific fields to `runfor`/`RunFor`. (Hashes are filled by the release pipeline — leave placeholders matching the online template.)
- [ ] **Step 2: Commit.** `git add bucket/runfor.json && git commit -m "build(runfor): add scoop manifest"`

### Task 16: release.yml wiring

**Files:** Modify `.github/workflows/release.yml`.

- [ ] **Step 1:** Run `grep -n online .github/workflows/release.yml`. At **every** match, add a sibling `runfor` line/step mirroring `online`:
  - the `Pack online` step → add `Pack runfor` (`dotnet pack src/runfor/runfor.csproj ...`);
  - the `Publish online (${{ matrix.rid }})` step → add `Publish runfor`;
  - the bash `TOOLS="… online"` array → append ` runfor`;
  - the pwsh `$tools = @('…','online')` array → append `,'runfor'`;
  - the combined-zip `Copy-Item src/online/...online.exe` (Windows) → add the `runfor.exe` Copy-Item.
- [ ] **Step 2:** Check for a Linux/macOS combined-zip section (the bash equivalent of the Windows `winix-combined`) and add `runfor` there too if present.
- [ ] **Step 3: Commit.** `git add .github/workflows/release.yml && git commit -m "build(runfor): wire into release pipeline"`

### Task 17: post-publish.yml wiring

**Files:** Modify `.github/workflows/post-publish.yml`.

- [ ] **Step 1:** Add `update_manifest bucket/runfor.json aot/runfor-win-x64.zip` next to the `online` line (~81).
- [ ] **Step 2:** Add `generate_manifests "runfor" "RunFor" "Run a command with a time limit — cross-platform timeout(1)." "timeout,deadline,process,watchdog"` next to the `online` line (~245).
- [ ] **Step 3: Commit.** `git add .github/workflows/post-publish.yml && git commit -m "build(runfor): wire into post-publish (scoop + winget manifests)"`

### Task 18: manual-smoke.yml + run-smokes.sh fixture

**Files:** Modify `.github/workflows/manual-smoke.yml`; create a `runfor` smoke fixture under the current smokes dir (mirror where `online`'s lives — find it: `grep -rl 'runfor\|online' artifacts/ 2>/dev/null` or follow the path in `manual-smoke.yml`).

- [ ] **Step 1:** In `manual-smoke.yml`: append `,runfor` to the `TOOLS="…"` csv (line ~94); add `runfor` to the appropriate `runner_for`/sed group (lines ~137/141 — runfor has no Windows-specific backend, so it goes in the cross-platform group like `online`).
- [ ] **Step 2:** Create the `run-smokes.sh` fixture for runfor deriving cases from the README option/exit-code surface: (a) `runfor 30s -- <true>` → exit 0; (b) a fast-deadline over a sleep → exit 124 + prompt return; (c) `runfor --json 1s -- <sleep>` → JSON envelope with `"timed_out":true`; (d) bad duration → 125; (e) not-found command → 127. Mirror the structure of `online`'s fixture.
- [ ] **Step 3: Commit.** `git add .github/workflows/manual-smoke.yml <smoke fixture path> && git commit -m "test(runfor): add manual-smoke wiring + native capability fixture"`

### Task 19: Contract snapshot

**Files:** Modify `tests/Winix.Contract.Tests/DescribeSurfaces.cs`; commit the generated snapshot under `tests/Winix.Contract.Tests/snapshots/`.

- [ ] **Step 1:** Add the alias `using RunForCli = global::Winix.RunFor.Cli;` and a registry entry mirroring `retry`:

```csharp
// ── runfor ────────────────────────────────────────────────────────────────
// Signature from Winix.RunFor.Tests/CliRunTests.cs:
//   Cli.Run(args, stdout, stderr, CancellationToken.None, starter?)  (starter optional)
["runfor"] = args => Task.FromResult(
    RunForCli.Run(args, TextWriter.Null, TextWriter.Null, CancellationToken.None)),
```

Add a ProjectReference to `Winix.RunFor` in `Winix.Contract.Tests.csproj` if not pulled transitively.

- [ ] **Step 2:** Run the contract test to generate the snapshot, then commit it. Run: `dotnet test tests/Winix.Contract.Tests` (the harness writes/verifies the `runfor` snapshot; on first run create-and-commit per the existing snapshot workflow). Expected: snapshot contains `"schema_version":1`, `"maturity":"fresh"`, the runfor exit-code/JSON surface.
- [ ] **Step 3: Commit.** `git add tests/Winix.Contract.Tests/ && git commit -m "test(runfor): register --describe contract surface + snapshot"`

### Task 20: CLAUDE.md + final full-suite verification

**Files:** Modify `CLAUDE.md`.

- [ ] **Step 1:** Update `CLAUDE.md`:
  - **Project layout:** add `src/Winix.RunFor/`, `src/runfor/`, `tests/Winix.RunFor.Tests/` lines (place near the process-supervision family entries).
  - **NuGet package IDs list:** add `Winix.RunFor`.
  - **Scoop manifests list:** add `runfor.json`.
  - Update the `src/Winix.ProcessSupervision/` description to mention runfor as the first consumer.
- [ ] **Step 2: Full-suite verification.** Run:
  - `dotnet build Winix.sln` → 0 warnings.
  - `dotnet test Winix.sln` → all green (note any pre-existing known flakes from memory; runfor + ProcessSupervision projects fully green).
  - `dotnet publish src/runfor/runfor.csproj -c Release -r <host-rid>` → AOT native binary builds; run it: `runfor 1s -- <sleep 5>` returns 124; `runfor 5s -- <true>` returns 0; `runfor --describe` emits the envelope.
- [ ] **Step 3:** Doc↔behaviour reconciliation: enumerate every claim in `runfor --help`/`--describe`, `README.md`, the man page, `docs/ai/runfor.md`, `llms.txt`, and run the command demonstrating each — hunt for the false claim (per the suite verification-oracle rule).
- [ ] **Step 4: Commit.** `git add CLAUDE.md && git commit -m "docs(runfor): update CLAUDE.md project layout + package/scoop lists"`

---

## Self-Review (run before handoff)

1. **Spec coverage** (design §runfor): `runfor DURATION -- cmd` ✓ (Task 9 parsing); forward child code ✓ (Task 6); deadline → 124 ✓; `-k/--kill-after` ✓; `-s/--signal` ✓ (Unix; Windows no-op documented); `--json {timed_out, exit_code, duration_ms}` ✓ (enriched, Task 7); 130 on Ctrl+C ✓; Windows immediate-kill ✓ (ADR D7, Task 2). Packaging checklist ✓ (Tasks 12–20). `TerminateGracefully==false` → "could not kill child" ✓ (`KillFailed` flag, Task 7 notice).
2. **Placeholder scan:** no `Assert.True(true)`, no TBD — every test has real assertions; the contract snapshot bytes are intentionally generated-not-asserted (correct per the no-fabricated-snapshot rule).
3. **Type consistency:** `ISupervisedChild.Terminate(int, TimeSpan?)` ↔ `TerminateAtDeadline(Process, int, TimeSpan?)` ↔ `ProcessSupervisedChild.Terminate` all aligned; `RunForResult` factory names (`Completed/TimedOut/Interrupted/LaunchFailed`) match `RunForOutcome`; `UnixSignal.DefaultSignal` used consistently.

## Adversarial Review Integration

Single-pass `adversarial-plan-review` run 2026-06-15 (fresh subagent, plan+design+ADR only). Health: 2 blockers, 7 test gaps, 4 defers, 4 N/A. Reconciled with a stricter bar (these findings critique the plan's *new* code, so most are real):

| ID | Bucket | Resolution | Where |
|----|--------|------------|-------|
| F1 | Test gap | ACCEPT — `--kill-after 0s` must escalate (TimeSpan.Zero ≠ null) | Task 9 Step 4 |
| F2 | Test gap | ACCEPT (light) — huge-DURATION no-overflow test + verify `DurationParser` bound at impl | Task 9 Step 4 |
| F3 | Test gap | ACCEPT as doc — default-mode EPERM not surfaced is accepted coreutils parity | Task 2 Step 4 comment + known-issues |
| F4 | Test gap | ACCEPT — Ctrl+C wins a coincident clean exit (pins the priority decision) | Task 6 Step 5 |
| F5 | **Plan blocker** | ACCEPT — grandchild orphaning (ADR D10) must be in README + docs/ai, not only the man page | Task 12 Step 2, Task 14 Step 1b |
| F6 | Test gap | ACCEPT — clean completion emits NO stderr notice (silent on success) | Task 7 Step 4 + Task 9 Step 4 |
| F7 | Test gap | ACCEPT — `--json` clean completion → envelope on stderr, stdout EMPTY | Task 9 Step 4 |
| F8 | Test gap | ACCEPT (upgraded from defer) — `--color never` suppresses ANSI at the Cli seam (suite "--color wired?" class) | Task 9 Step 4 |
| F9 | Explicit defer | DEFER — the `--kill-after` integration test already proves the flags are accepted on all platforms; isolating "Windows ignores grace" is low-value | below |
| F10 | Explicit defer | ACCEPT as note — state the default-mode/signal-ignoring end-to-end split honestly | below |
| F11 | Explicit defer | ACCEPT as checkbox — ground the ChildProcessRunner behaviour-preserving claim on existing tests | Task 1 Step 5 |

The ratified coreutils-faithful decision was not relitigated by the reviewer. No taste-level findings were carried.

## Known verification-level notes (state honestly, do not imply more)

- **Ctrl+C → 130 real OS-signal hop is not automatically covered** (suite-wide gap, ADR D4 precedent). Task 6 covers the decision via a cancelled token; the real terminal-signal path is verified only by manual smoke — note this in the PR, do not claim automated coverage.
- **`ProcessSupervisedChild.WaitForExit` real impl** (sync-over-async `.Wait(ms)`) is exercised by the Task 11 integration test on the host platform + CI's three OS, not by the pure decision tests. State that the decision tree is unit-pinned and the real wait/kill is integration-pinned.
- **Default-mode end-to-end against a signal-ignoring real child** (review F10): the headline coreutils-faithful behaviour — runfor returns 124 while the child *survives* — is verified at the `TerminateAtDeadline` shared-lib layer (child survives) PLUS the runner returns 124 via the fake; the two are not combined into a single end-to-end test (it would deliberately leave a live child to reap). State this split; do not imply a single test proves both.
- **Windows `--signal`/`--kill-after` inertness** (review F9, deferred): documented no-ops verified at the dispatcher level (`TerminateAtDeadlineTests_Windows`) and that the flags are *accepted* (not rejected) by the all-platform `--kill-after` integration test. No dedicated "Windows ignores the grace" isolation test in v1.
- **Suite fresh-eyes code-review gate** (4-reviewer, multi-round to zero/zero) is still outstanding for the whole `Winix.ProcessSupervision` + `Winix.RunFor` surface (see `project_winix_status.md`) — run it after this plan lands, before v0.5.0 tags.
