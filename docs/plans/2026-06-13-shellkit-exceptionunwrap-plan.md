# ShellKit ExceptionUnwrap Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lift the duplicated `UnwrapTypeInit` exception-unwrap helper into `Yort.ShellKit`, retarget the four tools that copy it (wargs, retry, nc, envvault) to the shared version, delete the copies, and replace nc's hand-rolled `--timeout` int parse with `ParseResult.GetInt`.

**Architecture:** A new `Yort.ShellKit.ExceptionUnwrap` static class exposes two methods — `UnwrapTypeInit(ex) → Exception` (the common case, 3 callers) and `UnwrapTypeInitWithDepth(ex) → (Surface, DepthCapped)` (wargs, which surfaces a depth-cap notice). The lifted logic is byte-for-byte the existing wargs implementation, so every retarget is behaviour-neutral: the gate for each is "existing project tests still pass + build is clean under warnings-as-errors". The one dedicated unit-test file (wargs `ExceptionUnwrapTests.cs`) migrates to `Yort.ShellKit.Tests`; retry/nc/envvault have no dedicated unwrap tests, so their existing suites verify neutrality.

**Tech Stack:** .NET 10, C#, xUnit, `Yort.ShellKit` shared library (already referenced by all four tool libraries).

---

## File Structure

| File | Responsibility | Action |
|---|---|---|
| `src/Yort.ShellKit/ExceptionUnwrap.cs` | The shared unwrap helper (new home) | Create |
| `tests/Yort.ShellKit.Tests/ExceptionUnwrapTests.cs` | Unit tests for the helper | Create (migrated from wargs + 1 new) |
| `src/Winix.Wargs/ExceptionUnwrap.cs` | wargs-local copy | Delete |
| `src/Winix.Wargs/Cli.cs` | wargs broad-catch depth-cap notice | Modify (call `UnwrapTypeInitWithDepth`) |
| `tests/Winix.Wargs.Tests/ExceptionUnwrapTests.cs` | wargs-local unit tests | Delete (migrated) |
| `tests/Winix.Wargs.Tests/ProgramMainTests.cs` | comment pointer to the unit tests | Modify (comment only) |
| `src/Winix.Retry/Cli.cs` | retry broad-catch | Modify (call shared + delete private method) |
| `src/Winix.NetCat/Cli.cs` | nc broad-catch + `--timeout` parse | Modify (call shared + delete private method + `GetInt`) |
| `src/Winix.EnvVault/Cli.cs` | envvault backend/broad catches | Modify (2 call sites + delete internal method) |
| `src/envvault/Program.cs` | envvault bootstrap catch | Modify (2 call sites) |

**Behaviour-neutrality enforcement:** the lifted method body is copied verbatim from `src/Winix.Wargs/ExceptionUnwrap.cs` (32-iteration cap, `tie.InnerException != null` loop guard, `depthCapped` computation). No logic is changed. Naming: the bare `UnwrapTypeInit` returns just the surfaced exception (what retry/nc/envvault want); `UnwrapTypeInitWithDepth` returns the tuple (what wargs wants for its notice).

---

### Task 1: Create `Yort.ShellKit.ExceptionUnwrap` + tests

**Files:**
- Create: `tests/Yort.ShellKit.Tests/ExceptionUnwrapTests.cs`
- Create: `src/Yort.ShellKit/ExceptionUnwrap.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Yort.ShellKit.Tests/ExceptionUnwrapTests.cs` (the 5 cases migrated from wargs, renamed to `UnwrapTypeInitWithDepth`, plus one new case pinning the simple `UnwrapTypeInit` overload):

```csharp
using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

/// <summary>
/// Unit tests for <see cref="ExceptionUnwrap"/>. Pins both the happy-path unwrap and the
/// depth-cap detection (the depthCapped flag surfaces when a TIE chain exceeds MaxDepth so
/// the caller can warn the displayed message may not be the genuine root cause). Migrated
/// from Winix.Wargs.Tests when the helper was consolidated into ShellKit.
/// </summary>
public class ExceptionUnwrapTests
{
    [Fact]
    public void UnwrapTypeInitWithDepth_NonTie_ReturnedUnchanged_NotCapped()
    {
        var ex = new System.InvalidOperationException("plain");
        var (surface, capped) = ExceptionUnwrap.UnwrapTypeInitWithDepth(ex);
        Assert.Same(ex, surface);
        Assert.False(capped);
    }

    [Fact]
    public void UnwrapTypeInitWithDepth_SingleTieWrapper_ReturnsInner_NotCapped()
    {
        var inner = new System.InvalidOperationException("real cause");
        var tie = new System.TypeInitializationException("SomeType", inner);
        var (surface, capped) = ExceptionUnwrap.UnwrapTypeInitWithDepth(tie);
        Assert.Same(inner, surface);
        Assert.False(capped);
    }

    [Fact]
    public void UnwrapTypeInitWithDepth_DeepChainBelowCap_FullyUnwrapped_NotCapped()
    {
        // 31 nested TIE wrappers — exactly at the cap boundary, MUST unwrap fully.
        System.Exception current = new System.InvalidOperationException("real cause");
        for (int i = 0; i < 31; i++)
        {
            current = new System.TypeInitializationException($"Type{i}", current);
        }
        var (surface, capped) = ExceptionUnwrap.UnwrapTypeInitWithDepth(current);
        Assert.IsType<System.InvalidOperationException>(surface);
        Assert.Equal("real cause", surface.Message);
        Assert.False(capped);
    }

    [Fact]
    public void UnwrapTypeInitWithDepth_ChainExceedingCap_StopsAtCap_FlagsCapped()
    {
        // 33 nested TIE wrappers — exceeds the 32-cap by one. The unwrap must stop at the
        // cap and signal depthCapped so the caller can append the "(unwrap depth limit
        // reached)" notice.
        System.Exception current = new System.InvalidOperationException("real cause buried at depth 33");
        for (int i = 0; i < 33; i++)
        {
            current = new System.TypeInitializationException($"Type{i}", current);
        }
        var (surface, capped) = ExceptionUnwrap.UnwrapTypeInitWithDepth(current);
        Assert.True(capped, "depth cap should be detected when chain exceeds MaxDepth");
        // Surface must still be a TIE (the cap stopped us mid-unwrap), not the real cause.
        Assert.IsType<System.TypeInitializationException>(surface);
    }

    [Fact]
    public void UnwrapTypeInitWithDepth_TieWithNullInner_ReturnsTie_NotCapped()
    {
        // Defensive: TIE constructed with null inner should be returned as-is (loop guard
        // is `tie.InnerException != null`, which evaluates false at iteration 0).
        var tie = new System.TypeInitializationException("SomeType", null);
        var (surface, capped) = ExceptionUnwrap.UnwrapTypeInitWithDepth(tie);
        Assert.Same(tie, surface);
        Assert.False(capped);
    }

    [Fact]
    public void UnwrapTypeInit_SimpleOverload_ReturnsSurfaceOnly()
    {
        // The bare overload (used by retry/nc/envvault) returns just the surfaced cause.
        var inner = new System.InvalidOperationException("real cause");
        var tie = new System.TypeInitializationException("SomeType", inner);
        System.Exception surface = ExceptionUnwrap.UnwrapTypeInit(tie);
        Assert.Same(inner, surface);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test d:/projects/winix/tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --filter "FullyQualifiedName~ExceptionUnwrapTests" --nologo`
Expected: FAIL to **compile** — `ExceptionUnwrap` does not exist in `Yort.ShellKit`.

- [ ] **Step 3: Write the implementation**

Create `src/Yort.ShellKit/ExceptionUnwrap.cs` (body copied verbatim from the wargs version; only the namespace, the new simple overload, and the doc remarks differ):

```csharp
namespace Yort.ShellKit;

/// <summary>
/// Helpers for unwrapping CLR exception wrappers that obscure the actionable inner cause.
/// </summary>
/// <remarks>
/// Consolidated from per-tool copies (wargs, retry, nc, envvault) so every Winix tool
/// surfaces the real cause of a failed static constructor / native P/Invoke cctor instead
/// of the framework's useless "The type initializer for X threw an exception." wrapper text.
/// </remarks>
public static class ExceptionUnwrap
{
    /// <summary>
    /// Maximum unwrap depth before the loop bails out. Pathological cases (cyclic type-init
    /// or generic-instantiation chains) exceeding this are extremely unlikely; the cap exists
    /// to guarantee the helper terminates regardless of the input shape.
    /// </summary>
    public const int MaxDepth = 32;

    /// <summary>
    /// Peels <see cref="System.TypeInitializationException"/> wrappers to reveal the
    /// actionable inner exception, discarding the depth-cap signal. Use this overload when the
    /// caller only needs the surfaced cause for a one-line error message.
    /// </summary>
    /// <param name="ex">Exception to unwrap. Returned unchanged if not a TIE.</param>
    /// <returns>The innermost exception after unwrapping.</returns>
    public static System.Exception UnwrapTypeInit(System.Exception ex) => UnwrapTypeInitWithDepth(ex).Surface;

    /// <summary>
    /// Peels <see cref="System.TypeInitializationException"/> wrappers to reveal the
    /// actionable inner exception. The wrapper's Message is "The type initializer for X
    /// threw an exception." — useless to the user.
    /// </summary>
    /// <param name="ex">Exception to unwrap. Returned unchanged if not a TIE.</param>
    /// <returns>
    /// Tuple of (innermost exception after unwrap, depthCapped flag). depthCapped is true
    /// when the depth cap stopped the loop while a further TIE was still wrapping a real
    /// cause — callers should surface this so the user knows the displayed message may not
    /// be the genuine root cause.
    /// </returns>
    public static (System.Exception Surface, bool DepthCapped) UnwrapTypeInitWithDepth(System.Exception ex)
    {
        System.Exception current = ex;
        int depth;
        for (depth = 0; depth < MaxDepth && current is System.TypeInitializationException tie && tie.InnerException != null; depth++)
        {
            current = tie.InnerException;
        }
        bool depthCapped = depth == MaxDepth && current is System.TypeInitializationException capTie && capTie.InnerException != null;
        return (current, depthCapped);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test d:/projects/winix/tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --filter "FullyQualifiedName~ExceptionUnwrapTests" --nologo`
Expected: PASS — 6 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Yort.ShellKit/ExceptionUnwrap.cs tests/Yort.ShellKit.Tests/ExceptionUnwrapTests.cs
git commit -m "feat(shellkit): add shared ExceptionUnwrap helper"
```

---

### Task 2: Retarget wargs to the shared helper, delete the copy

**Files:**
- Modify: `src/Winix.Wargs/Cli.cs` (the line calling `ExceptionUnwrap.UnwrapTypeInit(ex)` that destructures a tuple)
- Delete: `src/Winix.Wargs/ExceptionUnwrap.cs`
- Delete: `tests/Winix.Wargs.Tests/ExceptionUnwrapTests.cs`
- Modify: `tests/Winix.Wargs.Tests/ProgramMainTests.cs` (comment pointer)

- [ ] **Step 1: Point the wargs call site at the renamed tuple method**

In `src/Winix.Wargs/Cli.cs`, find the broad-catch line:

```csharp
(Exception surface, bool depthCapped) = ExceptionUnwrap.UnwrapTypeInit(ex);
```

Change it to call the renamed tuple method:

```csharp
(Exception surface, bool depthCapped) = ExceptionUnwrap.UnwrapTypeInitWithDepth(ex);
```

Ensure `src/Winix.Wargs/Cli.cs` has `using Yort.ShellKit;` near the top (it already uses ShellKit types such as `SafeWriteLine`/`ExitCode`; add the using only if it is missing). After deleting the wargs-local class, the bare `ExceptionUnwrap` identifier resolves to `Yort.ShellKit.ExceptionUnwrap`.

- [ ] **Step 2: Delete the wargs-local copy and its tests**

```bash
git rm src/Winix.Wargs/ExceptionUnwrap.cs tests/Winix.Wargs.Tests/ExceptionUnwrapTests.cs
```

- [ ] **Step 3: Update the stale comment pointer in ProgramMainTests.cs**

In `tests/Winix.Wargs.Tests/ProgramMainTests.cs`, the comment block (around line 646) reads:

```csharp
    // SFH I3 / TA I4 (UnwrapTypeInit depth cap) is unit-tested in ExceptionUnwrapTests.cs
    // — the helper was extracted from the entry point in round 15 specifically to make the
    // depth-cap notice behaviour directly testable (CLAUDE.md: "Test-infeasible branches →
```

Update the first line so it points at the new location:

```csharp
    // SFH I3 / TA I4 (UnwrapTypeInit depth cap) is unit-tested in Yort.ShellKit.Tests'
    // ExceptionUnwrapTests.cs — the helper was consolidated into ShellKit; it was originally
    // extracted from the entry point in round 15 to make the depth-cap notice behaviour
    // directly testable (CLAUDE.md: "Test-infeasible branches →
```

(Preserve the remaining lines of the comment unchanged.)

- [ ] **Step 4: Build and run the wargs test project**

Run: `dotnet test d:/projects/winix/tests/Winix.Wargs.Tests/Winix.Wargs.Tests.csproj --nologo`
Expected: PASS — same pass count as before minus the 5 deleted `ExceptionUnwrapTests` cases (those now live in ShellKit.Tests); 0 failed.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(wargs): use shared ShellKit ExceptionUnwrap; remove local copy"
```

---

### Task 3: Retarget retry to the shared helper

**Files:**
- Modify: `src/Winix.Retry/Cli.cs` (call site + delete the private `UnwrapTypeInit` method)

- [ ] **Step 1: Update the call site**

In `src/Winix.Retry/Cli.cs`, find the broad-catch line:

```csharp
            Exception surface = UnwrapTypeInit(ex);
```

Change it to call the shared helper:

```csharp
            Exception surface = ExceptionUnwrap.UnwrapTypeInit(ex);
```

Ensure `using Yort.ShellKit;` is present at the top of the file (add if missing).

- [ ] **Step 2: Delete the private method**

In `src/Winix.Retry/Cli.cs`, delete the entire private helper method (its XML doc comment block plus the method body):

```csharp
    /// <summary>
    /// Peels TypeInitializationException wrappers to reveal the actionable inner exception.
    /// ... (full doc comment) ...
    /// </summary>
    private static Exception UnwrapTypeInit(Exception ex)
    {
        Exception current = ex;
        for (int depth = 0; depth < 32 && current is TypeInitializationException tie && tie.InnerException != null; depth++)
        {
            current = tie.InnerException;
        }
        return current;
    }
```

- [ ] **Step 3: Build and run the retry test project**

Run: `dotnet test d:/projects/winix/tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj --nologo`
Expected: PASS — same counts as before; 0 failed.

- [ ] **Step 4: Commit**

```bash
git add src/Winix.Retry/Cli.cs
git commit -m "refactor(retry): use shared ShellKit ExceptionUnwrap; remove local copy"
```

---

### Task 4: Retarget nc to the shared helper + use `GetInt` for `--timeout`

**Files:**
- Modify: `src/Winix.NetCat/Cli.cs` (unwrap call site + delete private method + `--timeout` parse)
- Test: `tests/Winix.NetCat.Tests/` (confirm an existing `--timeout` parse test; add one if absent)

- [ ] **Step 1: Update the unwrap call site**

In `src/Winix.NetCat/Cli.cs`, find:

```csharp
            Exception surface = UnwrapTypeInit(ex);
```

Change to:

```csharp
            Exception surface = ExceptionUnwrap.UnwrapTypeInit(ex);
```

Ensure `using Yort.ShellKit;` is present (add if missing).

- [ ] **Step 2: Delete the private method**

Delete the entire `private static Exception UnwrapTypeInit(Exception ex)` method and its XML doc comment from `src/Winix.NetCat/Cli.cs`.

- [ ] **Step 3: Replace the hand-rolled `--timeout` parse with `GetInt`**

In `src/Winix.NetCat/Cli.cs`, find:

```csharp
        TimeSpan timeout = TimeSpan.Zero;
        if (result.Has("--timeout"))
        {
            int seconds = int.Parse(result.GetString("--timeout")!, CultureInfo.InvariantCulture);
            timeout = TimeSpan.FromSeconds(seconds);
        }
```

Replace with:

```csharp
        TimeSpan timeout = TimeSpan.Zero;
        if (result.Has("--timeout"))
        {
            timeout = TimeSpan.FromSeconds(result.GetInt("--timeout"));
        }
```

(`ParseResult.GetInt(string, int?)` is defined in `src/Yort.ShellKit/ParseResult.cs:97`. If `CultureInfo` becomes unused after this change, remove its now-orphaned `using System.Globalization;` only if the compiler flags it under warnings-as-errors.)

- [ ] **Step 4: Confirm / add a `--timeout` parse test**

Search `tests/Winix.NetCat.Tests/` for an existing test that parses `--timeout` into a `TimeSpan`. Run:

```bash
grep -rn "timeout" tests/Winix.NetCat.Tests/
```

If a test already asserts `--timeout N` → `TimeSpan.FromSeconds(N)`, no new test is needed (it pins the `GetInt` equivalence). If none exists AND the timeout-resolving method is reachable from a test (public/internal seam), add a minimal one asserting `--timeout=5` resolves to `TimeSpan.FromSeconds(5)`. If the parse is buried in a non-seamed private path, do NOT fabricate a seam for this neutral change — note in the commit body that `GetInt` equivalence is covered by build + the existing nc suite.

- [ ] **Step 5: Build and run the nc test project**

Run: `dotnet test d:/projects/winix/tests/Winix.NetCat.Tests/Winix.NetCat.Tests.csproj --nologo`
Expected: PASS — 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.NetCat/Cli.cs
git commit -m "refactor(nc): use shared ShellKit ExceptionUnwrap + GetInt for --timeout"
```

---

### Task 5: Retarget envvault (Cli + Program) to the shared helper

**Files:**
- Modify: `src/Winix.EnvVault/Cli.cs` (2 call sites + delete the internal `UnwrapTypeInit` method)
- Modify: `src/envvault/Program.cs` (2 call sites)

- [ ] **Step 1: Update the envvault Cli call sites**

In `src/Winix.EnvVault/Cli.cs`, there are two usages:

```csharp
            Exception surface = UnwrapTypeInit(ex);
```
and
```csharp
                    $"failed to store {o.Namespaces[0]}.{o.Keys[0]}: {DescribeSurface(UnwrapTypeInit(ex))}", o.UseColor));
```

Change the bare `UnwrapTypeInit(ex)` calls to `ExceptionUnwrap.UnwrapTypeInit(ex)`:

```csharp
            Exception surface = ExceptionUnwrap.UnwrapTypeInit(ex);
```
and
```csharp
                    $"failed to store {o.Namespaces[0]}.{o.Keys[0]}: {DescribeSurface(ExceptionUnwrap.UnwrapTypeInit(ex))}", o.UseColor));
```

`DescribeSurface` is an envvault-local helper and stays as-is. Ensure `using Yort.ShellKit;` is present in `Cli.cs` (add if missing).

- [ ] **Step 2: Delete the internal method**

Delete the entire `internal static Exception UnwrapTypeInit(Exception ex)` method and its XML doc comment from `src/Winix.EnvVault/Cli.cs`.

- [ ] **Step 3: Update the envvault Program.cs call sites**

In `src/envvault/Program.cs`, there are two usages that currently call the (now-deleted) `Cli.UnwrapTypeInit`:

```csharp
            Exception surface = Cli.UnwrapTypeInit(ex);
```
and
```csharp
            SafeWriteLine(Console.Error, Formatting.ErrorLine(Cli.DescribeSurface(Cli.UnwrapTypeInit(ex)), useColor));
```

Change the `Cli.UnwrapTypeInit(ex)` calls to `ExceptionUnwrap.UnwrapTypeInit(ex)` (leave `Cli.DescribeSurface` unchanged — it stays an envvault helper):

```csharp
            Exception surface = ExceptionUnwrap.UnwrapTypeInit(ex);
```
and
```csharp
            SafeWriteLine(Console.Error, Formatting.ErrorLine(Cli.DescribeSurface(ExceptionUnwrap.UnwrapTypeInit(ex)), useColor));
```

Ensure `using Yort.ShellKit;` is present in `src/envvault/Program.cs` (add if missing).

- [ ] **Step 4: Build and run the envvault test project**

Run: `dotnet test d:/projects/winix/tests/Winix.EnvVault.Tests/Winix.EnvVault.Tests.csproj --nologo`
Expected: PASS — 0 failed (platform-skips unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/Winix.EnvVault/Cli.cs src/envvault/Program.cs
git commit -m "refactor(envvault): use shared ShellKit ExceptionUnwrap; remove internal copy"
```

---

### Task 6: Whole-solution verification + stray-copy sweep

**Files:** none (verification only)

- [ ] **Step 1: Confirm no `UnwrapTypeInit` definitions remain outside ShellKit**

Run:

```bash
grep -rn "UnwrapTypeInit" src/ | grep -v "src/Yort.ShellKit/ExceptionUnwrap.cs"
```

Expected: every remaining hit is a **call** of the form `ExceptionUnwrap.UnwrapTypeInit(` / `ExceptionUnwrap.UnwrapTypeInitWithDepth(` — NO `private`/`internal static ... UnwrapTypeInit` *definition* lines. If any definition remains, a retarget task was missed; fix before continuing.

- [ ] **Step 2: Confirm the deleted files are gone**

Run:

```bash
git status --porcelain
ls src/Winix.Wargs/ExceptionUnwrap.cs tests/Winix.Wargs.Tests/ExceptionUnwrapTests.cs 2>&1
```

Expected: both paths report "No such file or directory".

- [ ] **Step 3: Full solution build under warnings-as-errors**

Run: `dotnet build d:/projects/winix/Winix.sln --nologo`
Expected: Build succeeded, 0 warnings, 0 errors (the suite is `warnings-as-errors`; an orphaned `using` would fail here).

- [ ] **Step 4: Full solution test run**

Run: `dotnet test d:/projects/winix/Winix.sln --nologo`
Expected: PASS — 0 failed. (The total count should equal the prior baseline: the 5 migrated wargs cases now run under ShellKit.Tests, plus the 1 new simple-overload test = net +1.)

- [ ] **Step 5: Commit (only if Step 3/4 required any fixup)**

```bash
git add -A
git commit -m "chore: tidy after ExceptionUnwrap consolidation"
```

(If no fixups were needed, skip this commit — Tasks 1-5 already captured all changes.)

---

## Self-Review

**Spec coverage:**
- Lift `UnwrapTypeInit` to ShellKit → Task 1 (create) + Tasks 2-5 (retarget all 4 tools, delete 3 private/internal copies + 1 standalone file).
- nc `--timeout` `GetInt` → Task 4 Step 3.
- wargs `jsonOnly` consolidation → **explicitly out of scope** (grep found no such sites; the follow-up note was stale). Not in this plan.
- Caller-audit-includes-tests → wargs dedicated tests migrated (Task 2); retry/nc/envvault have no dedicated unwrap tests, neutrality covered by their suites + the full-solution run (Task 6).

**Placeholder scan:** No TBD/TODO/"handle appropriately". Every code step shows full code. The only conditional is Task 4 Step 4 (add a `--timeout` test *iff* a seam exists), with an explicit instruction not to fabricate a seam for a neutral change — consistent with CLAUDE.md's test-infeasible guidance.

**Type consistency:** `UnwrapTypeInit(Exception) → Exception` and `UnwrapTypeInitWithDepth(Exception) → (Exception Surface, bool DepthCapped)` are used consistently across Tasks 1-5. wargs is the only `WithDepth` caller; retry/nc/envvault use the bare overload. `MaxDepth = 32` matches the original loop bound.

**Behaviour-neutrality:** the lifted body is verbatim the wargs implementation; the three deleted private copies had identical loop logic (32-cap, same guard) returning only the surface, which `UnwrapTypeInit(ex)` reproduces exactly via `.Surface`.
