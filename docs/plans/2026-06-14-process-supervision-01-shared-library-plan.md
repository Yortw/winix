# Process-Supervision Shared Library (`Winix.ProcessSupervision`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the internal shared `Winix.ProcessSupervision` class library — the process-supervision spine (child spawn, immediate process-tree kill-on-cancel, launch-failure classification, family exit-code constants) that `runfor`/`lock`/`soak`/`attempt` will all consume.

**Architecture:** A `netstandard`-style internal library (`ProjectReference`, *not* a published NuGet package — mirrors `Winix.FileWalk`/`Winix.SecretStore`). It exposes an injectable `IChildProcessRunner` seam (real impl `ChildProcessRunner`; tests inject fakes), a pure `ChildProcessLaunch` Win32→exception classifier lifted from `retry`, and a `SupervisionExitCode` constants class adding the family's 124/130 to ShellKit's 125/126/127. Each tool's *own* library will later expose a `Cli.Run(args, stdout, stderr, token)` seam driving this runner.

**Tech Stack:** .NET 10, C#, NativeAOT-compatible (`IsTrimmable`/`IsAotCompatible`), xUnit, `Yort.ShellKit` (`ExitCode`, `CommandNotFoundException`, `CommandNotExecutableException`).

**Scope boundary (deliberate, per design "runfor validates the spine"):** This plan ships only the *immediate* `Process.Kill(entireProcessTree: true)` termination that all four tools share. The *graceful Unix signal escalation* (`SIGTERM` → grace window → `SIGKILL` to the process group, P/Invoke `kill(2)`) is a `runfor`-only feature and is added to this library **in the runfor plan**, where it has a consumer and integration coverage. Output **capture** (`soak --quiet`) is likewise deferred to the soak plan. Building either now would be a consumer-less abstraction.

**Reference files (read before implementing):**
- `src/Winix.Retry/Cli.cs:203-370` — the real spawn + kill-on-cancel delegate this library generalises (disposal order, catch set, Win32 classification).
- `src/Winix.Wargs/JobRunner.cs:689-772` — the `Register`-kill + dispose-in-finally precedent.
- `src/Winix.FileWalk/Winix.FileWalk.csproj` — the internal-lib csproj template.
- `tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj` — the test csproj template (`UseSystemResourceKeys=true`).
- `tests/Winix.Wargs.Tests/CliRunAsyncUnlockedTests.cs:35-49` — the cross-platform long-running-child spawn pattern (`ping` vs `sleep`).

---

## File Structure

**Library — `src/Winix.ProcessSupervision/`:**
- `Winix.ProcessSupervision.csproj` — internal lib (`IsPackable=false`), references `Yort.ShellKit`, `InternalsVisibleTo` its test project.
- `SupervisionExitCode.cs` — family exit-code constants: `Timeout = 124`, `Interrupted = 130` (125/126/127 stay in `ShellKit.ExitCode`).
- `ChildProcessLaunch.cs` — `static Exception ClassifyWin32(Win32Exception, string command)`: the Win32 `NativeErrorCode` → typed-exception mapping (pure, lifted from `retry`).
- `IChildProcessRunner.cs` — the injectable seam: `int Run(string command, IReadOnlyList<string> arguments, CancellationToken cancellationToken)`.
- `ChildProcessRunner.cs` — real impl: spawn via `ArgumentList`, inherit stdio, kill-tree-on-cancel, return child exit code, throw on launch failure.

**Tests — `tests/Winix.ProcessSupervision.Tests/`:**
- `Winix.ProcessSupervision.Tests.csproj` — `UseSystemResourceKeys=true`, references the lib.
- `SupervisionExitCodeTests.cs` — pin the constant values.
- `ChildProcessLaunchTests.cs` — pure classification matrix.
- `ChildProcessRunnerTests.cs` — integration: exit-code forward, launch failure, kill-on-cancel, child→grandchild tree kill (platform-gated).
- `ChildHelpers.cs` — cross-platform child-command builders shared by the integration tests.

---

## Task 1: Scaffold the library and test projects

**Files:**
- Create: `src/Winix.ProcessSupervision/Winix.ProcessSupervision.csproj`
- Create: `tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj`
- Modify: `Winix.sln`

- [ ] **Step 1: Create the library csproj**

Create `src/Winix.ProcessSupervision/Winix.ProcessSupervision.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
    <IsPackable>false</IsPackable>
    <PackageId>Winix.ProcessSupervision.Library</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Winix.ProcessSupervision.Tests" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create a placeholder source file so the lib compiles**

Create `src/Winix.ProcessSupervision/SupervisionExitCode.cs` with a minimal stub (filled in Task 2):

```csharp
namespace Winix.ProcessSupervision;

/// <summary>Placeholder — replaced in Task 2.</summary>
internal static class SupervisionExitCodePlaceholder
{
}
```

- [ ] **Step 3: Create the test csproj**

Create `tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <!-- Mirror the consuming AOT tools' UseSystemResourceKeys=true so tests observe the same
         bare CoreLib resource-key behaviour on framework exception .Message. -->
    <UseSystemResourceKeys>true</UseSystemResourceKeys>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.*" />
    <PackageReference Include="Xunit.SkippableFact" Version="1.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Winix.ProcessSupervision\Winix.ProcessSupervision.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add both projects to the solution**

Run:

```bash
dotnet sln Winix.sln add src/Winix.ProcessSupervision/Winix.ProcessSupervision.csproj tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj
```

Expected: `Project ... added to the solution.` twice.

- [ ] **Step 5: Add a trivial test so the test project is non-empty**

Create `tests/Winix.ProcessSupervision.Tests/ScaffoldTests.cs`:

```csharp
using Xunit;

namespace Winix.ProcessSupervision.Tests;

public class ScaffoldTests
{
    [Fact]
    public void Scaffold_Compiles()
    {
        Assert.True(true);
    }
}
```

(This `Assert.True(true)` is a deliberate, temporary scaffold smoke — it is deleted in Task 2 Step 1 once a real test exists. It exists only so Step 6 proves the projects build and the runner is wired before any real code lands.)

- [ ] **Step 6: Build and test to verify the scaffold**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj
```

Expected: build succeeds (0 warnings — warnings are errors), 1 test passes.

- [ ] **Step 7: Commit**

```bash
git add src/Winix.ProcessSupervision/ tests/Winix.ProcessSupervision.Tests/ Winix.sln
git commit -m "scaffold(process-supervision): add Winix.ProcessSupervision library + test project"
```

---

## Task 2: `SupervisionExitCode` family constants

**Files:**
- Create: `src/Winix.ProcessSupervision/SupervisionExitCode.cs` (replaces the placeholder)
- Delete: the placeholder content
- Test: `tests/Winix.ProcessSupervision.Tests/SupervisionExitCodeTests.cs`

- [ ] **Step 1: Write the failing test (and delete the scaffold test)**

Delete `tests/Winix.ProcessSupervision.Tests/ScaffoldTests.cs`.

Create `tests/Winix.ProcessSupervision.Tests/SupervisionExitCodeTests.cs`:

```csharp
using Xunit;

namespace Winix.ProcessSupervision.Tests;

public class SupervisionExitCodeTests
{
    // coreutils `timeout` exits 124 on deadline; runfor matches it.
    [Fact]
    public void Timeout_Is124()
    {
        Assert.Equal(124, SupervisionExitCode.Timeout);
    }

    // 128 + SIGINT(2); the shell convention for Ctrl+C.
    [Fact]
    public void Interrupted_Is130()
    {
        Assert.Equal(130, SupervisionExitCode.Interrupted);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter SupervisionExitCodeTests
```

Expected: FAIL — `SupervisionExitCode` does not contain `Timeout`/`Interrupted` (or does not exist).

- [ ] **Step 3: Write the implementation**

Replace the contents of `src/Winix.ProcessSupervision/SupervisionExitCode.cs`:

```csharp
namespace Winix.ProcessSupervision;

/// <summary>
/// Exit-code constants specific to the process-supervision tool family. The usage/not-executable/
/// not-found codes (125/126/127) live in <see cref="Yort.ShellKit.ExitCode"/> and are reused
/// directly; this class adds only the two codes the family introduces.
/// </summary>
public static class SupervisionExitCode
{
    /// <summary>
    /// Deadline exceeded — <c>runfor</c> killed the child because its time budget ran out.
    /// Matches coreutils <c>timeout</c> (124) so ported scripts behave identically.
    /// </summary>
    public const int Timeout = 124;

    /// <summary>
    /// Interrupted by SIGINT (Ctrl+C), forwarded to the child tree. 128 + signal number 2,
    /// the conventional shell exit code for a process killed by SIGINT.
    /// </summary>
    public const int Interrupted = 130;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter SupervisionExitCodeTests
```

Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Winix.ProcessSupervision/SupervisionExitCode.cs tests/Winix.ProcessSupervision.Tests/SupervisionExitCodeTests.cs tests/Winix.ProcessSupervision.Tests/ScaffoldTests.cs
git commit -m "feat(process-supervision): add SupervisionExitCode family constants (124/130)"
```

---

## Task 3: `ChildProcessLaunch` Win32 classifier

**Files:**
- Create: `src/Winix.ProcessSupervision/ChildProcessLaunch.cs`
- Test: `tests/Winix.ProcessSupervision.Tests/ChildProcessLaunchTests.cs`

This extracts the Win32 `NativeErrorCode` → typed-exception logic currently inlined in `src/Winix.Retry/Cli.cs:234-262` so all four tools classify launch failures identically. `Win32Exception` is thrown on every platform (.NET maps POSIX `errno` to Win32 codes), so the classifier is cross-platform.

- [ ] **Step 1: Write the failing test**

Create `tests/Winix.ProcessSupervision.Tests/ChildProcessLaunchTests.cs`:

```csharp
using System.ComponentModel;
using Xunit;
using Yort.ShellKit;

namespace Winix.ProcessSupervision.Tests;

public class ChildProcessLaunchTests
{
    // ERROR_FILE_NOT_FOUND (2) / ERROR_PATH_NOT_FOUND (3) / ENOENT (2) → command not found.
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ClassifyWin32_NotFoundCodes_ReturnsCommandNotFound(int nativeCode)
    {
        Exception result = ChildProcessLaunch.ClassifyWin32(new Win32Exception(nativeCode), "ghost");
        Assert.IsType<CommandNotFoundException>(result);
    }

    // ERROR_ACCESS_DENIED (5) / EACCES (13) → not executable.
    [Theory]
    [InlineData(5)]
    [InlineData(13)]
    public void ClassifyWin32_AccessCodes_ReturnsCommandNotExecutable(int nativeCode)
    {
        Exception result = ChildProcessLaunch.ClassifyWin32(new Win32Exception(nativeCode), "noperm");
        Assert.IsType<CommandNotExecutableException>(result);
    }

    // ERROR_BAD_EXE_FORMAT (193) and any other code → not-executable, message preserved,
    // original Win32Exception retained as inner for diagnostics (NOT the single-arg ctor,
    // which prepends a misleading "permission denied:"). Also pins the "message verbatim"
    // claim: Win32Exception.Message comes from the OS (FormatMessage), not .NET resources,
    // so it is unaffected by UseSystemResourceKeys — assert it carries the command name and
    // is non-empty rather than assuming so.
    [Fact]
    public void ClassifyWin32_OtherCode_ReturnsNotExecutableWithInner()
    {
        var win32 = new Win32Exception(193);
        Exception result = ChildProcessLaunch.ClassifyWin32(win32, "badexe");
        Assert.IsType<CommandNotExecutableException>(result);
        Assert.Same(win32, result.InnerException);
        Assert.Contains("badexe", result.Message, System.StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter ChildProcessLaunchTests
```

Expected: FAIL — `ChildProcessLaunch` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Winix.ProcessSupervision/ChildProcessLaunch.cs`:

```csharp
using System;
using System.ComponentModel;
using Yort.ShellKit;

namespace Winix.ProcessSupervision;

/// <summary>
/// Classifies <see cref="Process.Start"/> launch failures into the suite's typed exceptions.
/// Extracted from retry so the whole process-supervision family maps launch failures identically.
/// </summary>
public static class ChildProcessLaunch
{
    /// <summary>
    /// Maps a <see cref="Win32Exception"/> raised by <see cref="System.Diagnostics.Process.Start"/>
    /// to the appropriate typed exception. <see cref="Win32Exception"/> is thrown on all platforms —
    /// .NET maps POSIX <c>errno</c> values onto Win32 error codes on Linux/macOS.
    /// </summary>
    /// <param name="ex">The exception raised by <c>Process.Start</c>.</param>
    /// <param name="command">The command that failed to launch (for the message).</param>
    /// <returns>
    /// A <see cref="CommandNotFoundException"/> (codes 2/3 — ENOENT/path-not-found),
    /// or a <see cref="CommandNotExecutableException"/> (codes 5/13 — access denied, or any
    /// other code such as ERROR_BAD_EXE_FORMAT 193). The caller throws the returned exception.
    /// </returns>
    public static Exception ClassifyWin32(Win32Exception ex, string command)
    {
        // ERROR_ACCESS_DENIED (5) on Windows, EACCES (13) on Linux/macOS → not executable.
        if (ex.NativeErrorCode == 5 || ex.NativeErrorCode == 13)
        {
            return new CommandNotExecutableException(command);
        }

        // ERROR_FILE_NOT_FOUND (2), ERROR_PATH_NOT_FOUND (3), ENOENT (2) → not found.
        if (ex.NativeErrorCode == 2 || ex.NativeErrorCode == 3)
        {
            return new CommandNotFoundException(command);
        }

        // Other errors (ERROR_BAD_EXE_FORMAT 193, etc.). Use the (message, inner) ctor: the
        // single-arg ctor prepends "permission denied: " unconditionally, which is misleading
        // for non-permission errors. The 2-arg ctor preserves the message verbatim and keeps
        // the underlying Win32Exception for diagnostics.
        return new CommandNotExecutableException($"{command}: {ex.Message}", ex);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter ChildProcessLaunchTests
```

Expected: PASS (5 cases).

- [ ] **Step 5: Commit**

```bash
git add src/Winix.ProcessSupervision/ChildProcessLaunch.cs tests/Winix.ProcessSupervision.Tests/ChildProcessLaunchTests.cs
git commit -m "feat(process-supervision): add ChildProcessLaunch Win32 failure classifier"
```

---

## Task 4: `IChildProcessRunner` + `ChildProcessRunner` — exit-code forwarding

**Files:**
- Create: `src/Winix.ProcessSupervision/IChildProcessRunner.cs`
- Create: `src/Winix.ProcessSupervision/ChildProcessRunner.cs`
- Create: `tests/Winix.ProcessSupervision.Tests/ChildHelpers.cs`
- Test: `tests/Winix.ProcessSupervision.Tests/ChildProcessRunnerTests.cs`

- [ ] **Step 1: Add the cross-platform child-command helper**

Create `tests/Winix.ProcessSupervision.Tests/ChildHelpers.cs`:

```csharp
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Winix.ProcessSupervision.Tests;

/// <summary>
/// Cross-platform child-command builders for the runner integration tests. The runner spawns
/// command+args directly (no shell), so each helper returns the executable plus an argument list.
/// </summary>
internal static class ChildHelpers
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>A child that exits immediately with the given code.</summary>
    public static (string Command, string[] Args) ExitWith(int code)
    {
        return IsWindows
            ? ("cmd", new[] { "/c", "exit", code.ToString() })
            : ("/bin/sh", new[] { "-c", $"exit {code}" });
    }

    /// <summary>A child that sleeps for the given seconds (used for kill-on-cancel tests).</summary>
    public static (string Command, string[] Args) SleepSeconds(int seconds)
    {
        return IsWindows
            // ping is a portable sleep on Windows: -n N pings ≈ N-1 seconds. Add 1 to compensate.
            ? ("cmd", new[] { "/c", $"ping -n {seconds + 1} 127.0.0.1 > NUL" })
            : ("/bin/sh", new[] { "-c", $"sleep {seconds}" });
    }
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/Winix.ProcessSupervision.Tests/ChildProcessRunnerTests.cs`:

```csharp
using System.Threading;
using Xunit;

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
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter ChildProcessRunnerTests
```

Expected: FAIL — `ChildProcessRunner` does not exist.

- [ ] **Step 4: Write the interface**

Create `src/Winix.ProcessSupervision/IChildProcessRunner.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using Yort.ShellKit;

namespace Winix.ProcessSupervision;

/// <summary>
/// The process-supervision family's injectable child-runner seam. Tools depend on this interface
/// so tests can substitute a fake that models child lifecycle timing (exit-after-delay, killed-mid-run)
/// rather than driving a real process.
/// </summary>
public interface IChildProcessRunner
{
    /// <summary>
    /// Spawns <paramref name="command"/> with <paramref name="arguments"/>, letting the child inherit
    /// the parent's stdin/stdout/stderr, waits for it to exit, and returns its exit code.
    /// </summary>
    /// <param name="command">The executable to run (resolved against PATH by the OS).</param>
    /// <param name="arguments">Arguments passed verbatim via <c>ProcessStartInfo.ArgumentList</c>.</param>
    /// <param name="cancellationToken">
    /// When signalled before the child exits, the child's entire process tree is killed and the
    /// (killed) child's exit code is returned. The caller decides how to map that — e.g. <c>runfor</c>
    /// returns 124 when it cancelled for its own deadline.
    /// </param>
    /// <returns>
    /// The child process exit code. NOTE: the return value does NOT encode whether the run was
    /// cancelled — a tree-killed child's code is platform-specific and (on Windows) not reliably
    /// distinguishable from a legitimate non-zero exit. A caller that needs to apply a policy on
    /// cancellation (e.g. map to 124/130) MUST observe its own <paramref name="cancellationToken"/>
    /// to decide; it cannot infer cancellation from this return value.
    /// </returns>
    /// <exception cref="CommandNotFoundException">The command was not found on PATH.</exception>
    /// <exception cref="CommandNotExecutableException">The command exists but could not be executed.</exception>
    int Run(string command, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Write the implementation (exit-code path only; cancel path added in Task 5/6)**

Create `src/Winix.ProcessSupervision/ChildProcessRunner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace Winix.ProcessSupervision;

/// <summary>
/// Real <see cref="IChildProcessRunner"/>: spawns the child via <c>ProcessStartInfo.ArgumentList</c>
/// (never string concatenation — suite rule), inherits the parent's console handles so the child is
/// invisible in the pipeline, and kills the child's process tree if the supervising token is cancelled.
/// </summary>
public sealed class ChildProcessRunner : IChildProcessRunner
{
    /// <inheritdoc />
    public int Run(string command, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            // Inherit the real console handles so child output passes through unmodified.
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
        };

        foreach (string arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            // Process.Start returns null only when an existing process is reused — effectively
            // unreachable with UseShellExecute=false (a new process is always started, or an
            // exception is thrown). Belt-and-braces: surface a neutral error rather than
            // mislabelling it "command not found" (null != "not on PATH").
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Process.Start returned no process for '{command}'.");
        }
        catch (Win32Exception ex)
        {
            throw ChildProcessLaunch.ClassifyWin32(ex, command);
        }

        try
        {
            process.WaitForExit();
            return process.ExitCode;
        }
        finally
        {
            process.Dispose();
        }
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter ChildProcessRunnerTests
```

Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add src/Winix.ProcessSupervision/IChildProcessRunner.cs src/Winix.ProcessSupervision/ChildProcessRunner.cs tests/Winix.ProcessSupervision.Tests/ChildHelpers.cs tests/Winix.ProcessSupervision.Tests/ChildProcessRunnerTests.cs
git commit -m "feat(process-supervision): add IChildProcessRunner seam + exit-code-forwarding impl"
```

---

## Task 5: `ChildProcessRunner` launch-failure path

**Files:**
- Modify: `tests/Winix.ProcessSupervision.Tests/ChildProcessRunnerTests.cs`
- (No production change — Task 4's `Process.Start` catch already routes through `ChildProcessLaunch`; this task adds the integration test that proves it end-to-end.)

- [ ] **Step 1: Write the failing test**

Add to `tests/Winix.ProcessSupervision.Tests/ChildProcessRunnerTests.cs` (inside the class), and add `using Yort.ShellKit;` to the file's usings:

```csharp
    [Fact]
    public void Run_CommandNotFound_ThrowsCommandNotFound()
    {
        var runner = new ChildProcessRunner();

        Assert.Throws<CommandNotFoundException>(() =>
            runner.Run("this-command-does-not-exist-xyzzy", System.Array.Empty<string>(), CancellationToken.None));
    }
```

- [ ] **Step 2: Run the test to verify it passes (the production path already exists)**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter ChildProcessRunnerTests
```

Expected: PASS (3 tests). This confirms a missing executable surfaces as `CommandNotFoundException` rather than a raw `Win32Exception`. If it FAILS, the Task 4 `Process.Start` catch is wrong — fix `ChildProcessRunner` before continuing.

- [ ] **Step 3: Commit**

```bash
git add tests/Winix.ProcessSupervision.Tests/ChildProcessRunnerTests.cs
git commit -m "test(process-supervision): pin launch-failure surfaces as CommandNotFoundException"
```

---

## Task 6: `ChildProcessRunner` kill-on-cancel + process-tree kill

**Files:**
- Modify: `src/Winix.ProcessSupervision/ChildProcessRunner.cs`
- Modify: `tests/Winix.ProcessSupervision.Tests/ChildProcessRunnerTests.cs`

- [ ] **Step 1: Write the failing single-child kill test**

Add to `ChildProcessRunnerTests.cs` (add `using System;` and `using System.Diagnostics;` if not present):

```csharp
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
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter Run_TokenCancelledMidRun
```

Expected: FAIL — the current `WaitForExit()` ignores the token, so `Run` blocks for the full sleep and exceeds the 30s bound.

- [ ] **Step 3: Add kill-on-cancel to the implementation**

Replace the `try { process.WaitForExit(); return process.ExitCode; }` block in `ChildProcessRunner.cs` with the registered-kill version:

```csharp
        try
        {
            // ORDERING INVARIANT (load-bearing — do not move): killReg is a `using` declared INSIDE
            // this try, so on any exit path its Dispose runs as the try-scope unwinds — BEFORE the
            // finally's process.Dispose(). CancellationTokenRegistration.Dispose() blocks until any
            // in-flight callback completes, so the kill callback can never run against an
            // already-disposed Process. Disposing the process inside the try, or hoisting killReg
            // out, would reintroduce that race. Mirrors retry's disposal-order fix (Cli.cs).
            //
            // Register a kill-the-tree callback on cancel. The synchronous WaitForExit() below
            // returns once the kill terminates the child. The careful catch set mirrors retry/wargs:
            // a CancellationToken callback that throws makes the cancelling Cancel() call throw,
            // which would escape the supervising tool — so the kill is strictly best-effort.
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

            process.WaitForExit();
            return process.ExitCode;
        }
        finally
        {
            process.Dispose();
        }
```

- [ ] **Step 4: Run the single-child kill test to verify it passes**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter Run_TokenCancelledMidRun
```

Expected: PASS.

- [ ] **Step 4b: Add the cancellation-invariant tests (pre-cancelled token + never-cancelled inert registration)**

Two adversarial cases the happy-path tests miss: (1) a token *already cancelled* when `Run` is called (`runfor 0s`, or a parent already shutting down); (2) the negative/invariant case — a child that exits **naturally** under a live, never-cancelled token returns its true code, proving the kill registration is present but does **not** fire spuriously (the `CancellationToken.None` tests in Task 4 bypass the registration code path entirely). Add to `ChildProcessRunnerTests.cs`:

```csharp
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
```

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter "Run_TokenAlreadyCancelled|Run_LiveTokenNeverCancelled"
```

Expected: PASS (2 tests). Both exercise the registration added in Step 3 — the pre-cancelled case proves prompt return, the never-cancelled case proves the registration does not fire spuriously.

- [ ] **Step 5: Write the failing child→grandchild tree-kill test (Unix-gated)**

The Unix process-tree kill is the genuinely-hard path to prove (Windows `Kill(entireProcessTree)` is well-trodden). Add to `ChildProcessRunnerTests.cs`:

```csharp
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
```

Add `using System.IO;` to the file's usings.

- [ ] **Step 6: Run the tree-kill test to verify it passes (on Unix) or skips (on Windows)**

Run:

```bash
dotnet test tests/Winix.ProcessSupervision.Tests/Winix.ProcessSupervision.Tests.csproj --filter Run_TokenCancelled_KillsGrandchildToo
```

Expected: PASS on Linux/macOS; reported **Skipped** on Windows (not Passed — `SkippableFact`). If on Windows, confirm the result says Skipped.

- [ ] **Step 7: Commit**

```bash
git add src/Winix.ProcessSupervision/ChildProcessRunner.cs tests/Winix.ProcessSupervision.Tests/ChildProcessRunnerTests.cs
git commit -m "feat(process-supervision): kill child process tree on cancel"
```

---

## Task 7: Full-solution verification

**Files:** none (verification + bookkeeping)

- [ ] **Step 1: Build the whole solution (warnings are errors)**

Run:

```bash
dotnet build Winix.sln
```

Expected: Build succeeded, 0 warnings, 0 errors. (The new lib must not break AOT/trim analyzers in any consuming project — there are none yet, but the build proves the lib itself is clean.)

- [ ] **Step 2: Run the full test suite**

Run:

```bash
dotnet test Winix.sln
```

Expected: all tests pass (existing suite + the new `Winix.ProcessSupervision.Tests`); the Unix-only tree-kill test shows Skipped on Windows.

- [ ] **Step 3: Update project layout docs**

In `CLAUDE.md`, under "## Project layout", add after the `src/Winix.Online/` line:

```
src/Winix.ProcessSupervision/ — shared library (child spawn, process-tree kill-on-cancel, launch classifier, family exit codes; consumed by runfor/lock/soak/attempt)
```

And under the `tests/` block, add after `tests/Winix.Online.Tests/`:

```
tests/Winix.ProcessSupervision.Tests/ — xUnit tests
```

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(process-supervision): record shared library in project layout"
```

---

## Self-Review

**Spec coverage** (against `2026-06-13-process-supervision-family-design.md` §Architecture "Shared library"):
- Child spawn via `ArgumentList`, inherit stdio → Task 4 ✓
- Process-tree termination (immediate) → Task 6 ✓; graceful Unix escalation explicitly deferred to runfor plan (scope boundary) ✓
- Exit-code mapping + family scheme → Task 2 (`SupervisionExitCode`) + Task 3 (launch classification) ✓
- Injectable process-runner abstraction → Task 4 (`IChildProcessRunner`) ✓
- Capture mode (`soak --quiet`) → explicitly deferred to soak plan ✓

**Placeholder scan:** The only `Assert.True(true)` is the Task 1 scaffold smoke, explicitly deleted in Task 2 Step 1 — not a deferred-test smell. No TBD/TODO.

**Type consistency:** `IChildProcessRunner.Run(string, IReadOnlyList<string>, CancellationToken)` is used identically in the interface (Task 4 Step 4), the impl (Task 4 Step 5), and all tests. `ChildProcessLaunch.ClassifyWin32(Win32Exception, string)` matches between Task 3 impl and Task 4 usage. `SupervisionExitCode.Timeout`/`Interrupted` consistent.

**Known follow-ups for later plans (not gaps here):**
- The shared **fake** (`FakeChildProcessRunner`, modelling lifecycle timing per CLIo BUG-010) is introduced in the **runfor plan** — the first tool that needs to drive orchestration through the seam in-process. Decide there whether it lives as a compile-linked shared file or a small per-tool fake.
- Graceful Unix signal escalation (`--signal`/`--kill-after`) extends `ChildProcessRunner` (or adds a sibling) in the **runfor plan**.

**Documented boundary (F7):** `ChildProcessRunner.Run` assumes a **non-empty** `command`. Validating an empty/whitespace command is the **consuming tool's** job — each tool parses the `--` boundary and rejects a missing command at the arg-parser layer (retry precedent: `if (result.Command.Length == 0) error`). An empty `FileName` passed to `Process.Start` throws an *unclassified* `InvalidOperationException` (not a `Win32Exception`, so it skips `ChildProcessLaunch`); the tool layer prevents that input from ever reaching the runner. The runfor plan re-confirms this guard exists in the tool.

---

## Adversarial Review Integration (2026-06-14)

A fresh-subagent adversarial-plan-review pass (15-category taxonomy) returned 3 blockers, 4 test gaps, 2 defers. All reconciled against the plan and integrated (each critiqued new code written into the plan, the class of finding that survives reconciliation):

| ID | Bucket | Disposition |
|----|--------|-------------|
| F1 | Plan blocker (concurrency) | Integrated — explicit dispose-ordering invariant comment added to Task 6 Step 3. Suggested race-stress test **declined** (non-deterministic — would flake, counter to the wargs-flake lesson); the ordering is guaranteed by `using`/`finally` unwinding semantics, the comment protects it from refactors. |
| F2 | Plan blocker (input/failure surfacing) | Integrated — Task 3 test now asserts the code-193 message contains the command name and is non-empty (pins the "Win32 message verbatim" claim; `Win32Exception.Message` is OS-sourced, unaffected by `UseSystemResourceKeys`). |
| F3 | Plan blocker (failure surfacing) | Integrated — `Process.Start()==null` now throws a neutral `InvalidOperationException`, not `CommandNotFoundException` (effectively unreachable under `UseShellExecute=false`; honesty fix + comment). |
| F4 | Test gap (cancellation) | Integrated — pre-cancelled-token test added (Task 6 Step 4b). |
| F5 | Test gap (negative/invariant) | Integrated — never-cancelled-token natural-exit test added (Task 6 Step 4b); closes the gap where `CancellationToken.None` bypassed the registration. |
| F6 | Test gap (observability) | Integrated — `IChildProcessRunner.Run` XML doc now states the return value does NOT encode cancellation; callers must observe their own token. |
| F7 | Explicit defer | Integrated — documented boundary above (empty-command validation is the tool layer's job). |
| F8 | Explicit defer (honesty) | Integrated — Task 6 Step 5 comment softened; no Windows-grandchild coverage claimed here (deferred to tool integration tests). |
