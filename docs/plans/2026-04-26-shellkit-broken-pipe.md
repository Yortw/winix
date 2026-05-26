# ShellKit Broken-Pipe Hardening Implementation Plan

> **STATUS (2026-04-26): COMPLETE — scope drastically reduced after probe phase falsified the plan's premise. See "Final outcome" section below before reading the original plan.**

## Final outcome (2026-04-26)

The plan's underlying premise — that `<tool> --help | head -1` produces `unexpected_error` envelopes across all 22 Winix tools because `Console.WriteLine` raises `IOException` on broken pipe — turned out to be **empirically false** on both runtimes Winix targets:

| Platform | Runtime | Result |
|----------|---------|--------|
| Windows  | .NET 10.0.4 | `Console.WriteLine` silently absorbs broken pipe at the runtime layer (10,000 writes to a closed pipe → zero exceptions) |
| Linux (WSL) | .NET 8.0.22 | Same — silent absorption for broken-pipe specifically |

The probe machinery in Tasks 1–4 captured this. Troy chose Option B (defensive change anyway, scope reduced) — implement a small belt-and-braces try/catch (IOException) at the three parser sites in case a future runtime stops absorbing, and pin the contract with both an in-process unit test (synthetic IOException via `Console.SetOut`) and a subprocess regression test.

### What was actually done

1. **Three parser sites wrapped:** `src/Yort.ShellKit/CommandLineParser.cs:464,470,476` — `Console.WriteLine` calls now wrapped in `try { … } catch (IOException) { }`. **Narrow** catch on purpose: `ObjectDisposedException`, `InvalidOperationException` from `GenerateHelp`/`GenerateDescribe`, etc. all propagate.
2. **In-process contract tests:** `tests/Yort.ShellKit.Tests/StandardFlagsBrokenPipeTests.cs` — 7 tests pinning that synthetic IOException (any HResult) is swallowed AND that `ObjectDisposedException` propagates.
3. **Subprocess regression tests:** `tests/Yort.ShellKit.Tests/StandardFlagsPipeSubprocessTests.cs` — 6 tests across `timeit` × `wargs` × `--help`/`--version`/`--describe`, asserting clean exit and no error envelope when stdout is closed early.
4. **ProjectReferences added** to ShellKit.Tests csproj for `timeit` and `wargs` (build-only, with `Private=false` + `ReferenceOutputAssembly=false`) so the subprocess tests have stable build ordering.
5. **Memory updates:** `project_shellkit_standardflags_pipe.md` + `project_broken_pipe_design_question.md` marked RESOLVED with the empirical finding (runtime absorbs).

### What was DROPPED versus the original plan

| Dropped item | Why |
|--------------|-----|
| ADR (Task 4) | The decision became trivial — defensive try/catch, no architectural debate |
| `BrokenPipeDetector` helper (Option B in original plan) | Runtime absorbs broken-pipe on its own; no detector needed |
| `WINIX_DEBUG_IO=1` env-var escape hatch (F7) | Production catch is now narrow (IOException only); programmer bugs already propagate, so the escape hatch's main value (surfacing swallowed bugs) is unnecessary |
| Cross-suite audit of `Console.Write*` sites (Task 7) | Scope was justified by "bug across 22 tools"; no bug, no audit |
| CLAUDE.md convention bullet (Task 8) | The change is too small + too contingent on runtime version to warrant a permanent suite-wide convention statement; if Linux .NET ever stops absorbing, this gets revisited |
| `AnonymousPipeServerStream` real-broken-pipe test (F6) | Reviewer feedback walked it back during plan integration — synthetic + subprocess tests cover the gap |

### Test impact

- ShellKit.Tests: 153 → 159 (the +6 reflects 6 new test methods; xunit theory cases per parameterised test inflate the on-screen pass count)
- Full Winix.sln: ~2,431 tests pass + 7 platform-skipped (EnvVault Linux/macOS, wargs platform-skip)

### Probe artefacts

`tmp/iox-probe/`, `tmp/iox-probe-linux/` — gitignored, kept local. Findings captured in `tmp/iox-probe/findings.md` (also gitignored).

---

## Original plan (kept for context)

Below is the original plan as written + adversarially reviewed. Most of the elaborate Phase A / Phase B / Tasks 5–10 machinery was made unnecessary by the probe results. Kept verbatim so the divergence is traceable.

---

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve the suite-wide broken-pipe design question via empirical probe, then apply the chosen pattern to fix the three unwrapped `Console.WriteLine` calls in `CommandLineParser.cs` that currently surface `unexpected_error` envelopes when `<tool> --help | head -1` is piped.

**Architecture:** Two phases. Phase A is empirical: a tiny .NET probe captures actual `IOException.HResult` + `Message` + concrete exception type for broken-pipe (and best-effort disk-full) scenarios on Windows and Linux. Phase B applies the chosen pattern (broad-swallow OR `BrokenPipeDetector` helper) to ShellKit's three sites, then audits the rest of the suite for direct-`Console.Write*` callers and brings them onto the same convention. Regression risk is contained: the only tools touched are those with bare console writes outside `Safe*` helpers; tests are subprocess-level pinning the externally visible behaviour (`--help | head -1` exits clean, no envelope).

**Tech Stack:** .NET 8/9, AOT-friendly, xUnit subprocess tests via `Process.Start`, `Yort.ShellKit.CommandLineParser`.

---

## Pre-flight

### Task 0: Branch off release/v0.4.0

**Files:** none

- [ ] **Step 1: Verify current branch and clean state**

Run: `git branch --show-current`
Expected: `release/v0.4.0`

Run: `git status --short`
Expected: only untracked `.claude/`, `artifacts/`, `dttest/`, `tmp/` — no modified tracked files.

- [ ] **Step 2: Create feature branch**

Run: `git checkout -b fix/shellkit-broken-pipe`
Expected: `Switched to a new branch 'fix/shellkit-broken-pipe'`

- [ ] **Step 3: Verify upstream is NOT auto-set to main**

Run: `git rev-parse --abbrev-ref --symbolic-full-name @{u}` (expects to error — no upstream yet, which is correct).
If it returns `origin/main` or any other branch, STOP and reset before pushing.

---

## Phase A — Empirical probe

### Task 1: Probe scaffold

**Files:**
- Create: `tmp/iox-probe/iox-probe.csproj`
- Create: `tmp/iox-probe/Program.cs`
- Create: `tmp/iox-probe/.gitignore`
- Create: `tmp/iox-probe/findings.md`

- [ ] **Step 1: Create .gitignore so the probe stays out of the repo**

Write `tmp/iox-probe/.gitignore`:

```
bin/
obj/
*.user
```

The repo's top-level `.gitignore` already excludes `tmp/`, but adding a local one makes the probe self-contained if it ever gets relocated.

- [ ] **Step 2: Create the csproj**

Write `tmp/iox-probe/iox-probe.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>IoxProbe</RootNamespace>
    <AssemblyName>iox-probe</AssemblyName>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Write the probe program**

Write `tmp/iox-probe/Program.cs`:

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace IoxProbe;

internal static class Program
{
    /// <summary>
    /// Probe modes:
    ///   spam        — write lines flushed per iteration until pipe breaks (or 8MB sent).
    ///   spam-large  — write 4096-byte chunks via Console.OpenStandardOutput() until 8MB sent.
    ///   close-then-write — close stdout then write (ObjectDisposedException baseline).
    /// Sizing rationale: Linux default pipe buffer is 64 KiB, macOS 16 KiB. We must vastly
    /// exceed both so that a `| head -1` consumer DEFINITELY closes the read end before
    /// the producer's last write — otherwise the kernel can absorb everything and the
    /// producer never observes the broken pipe (silently misclassifying the platform).
    /// Captured fields are emitted to STDERR as one tab-separated record so the test
    /// harness can scrape them without colliding with the broken-pipe stdout.
    /// </summary>
    public static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "spam";
        try
        {
            switch (mode)
            {
                case "spam":
                {
                    // Flush per write so kernel-side pipe buffer fills predictably.
                    long written = 0;
                    for (int i = 0; written < 8L * 1024 * 1024; i++)
                    {
                        string line = $"line {i:D8} ----------------------------------------------------------------\n";
                        Console.Write(line);
                        Console.Out.Flush();
                        written += line.Length;
                    }
                    return 0;
                }
                case "spam-large":
                {
                    using var stdout = Console.OpenStandardOutput();
                    var chunk = new byte[4096];
                    Array.Fill(chunk, (byte)'A');
                    chunk[^1] = (byte)'\n';
                    long written = 0;
                    while (written < 8L * 1024 * 1024)
                    {
                        stdout.Write(chunk, 0, chunk.Length);
                        stdout.Flush();
                        written += chunk.Length;
                    }
                    return 0;
                }
                case "close-then-write":
                    Console.Out.Close();
                    Console.WriteLine("after close");
                    return 0;
                default:
                    Console.Error.WriteLine($"unknown mode: {mode}");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            int hrLow = ex.HResult & 0xFFFF;
            string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos"
                : "other";
            // Tab-separated for easy scraping. Inner exceptions sometimes carry the
            // platform-native HResult on Linux — capture both.
            Console.Error.WriteLine(
                $"PROBE\t{platform}\t{mode}\t{ex.GetType().FullName}\t" +
                $"hr=0x{ex.HResult:X8}\thr_low={hrLow}\t" +
                $"inner={ex.InnerException?.GetType().FullName ?? "null"}\t" +
                $"inner_hr={(ex.InnerException is { } ie ? $"0x{ie.HResult:X8}" : "null")}\t" +
                $"msg={ex.Message.Replace('\t', ' ').Replace('\n', ' ')}");
            return 1;
        }
    }
}
```

- [ ] **Step 4: Build the probe**

Run: `dotnet build tmp/iox-probe/iox-probe.csproj -c Release`
Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Initialise findings record**

Write `tmp/iox-probe/findings.md`:

```markdown
# IOException probe findings

Captured by `tmp/iox-probe`. Each row is one tab-separated `PROBE` line emitted to stderr.

## Windows (host)

| Mode | Type | HResult | HResult low 16 | Message |
|------|------|---------|----------------|---------|
| (pending) | | | | |

## Linux (WSL or CI)

| Mode | Type | HResult | HResult low 16 | Inner type | Inner HResult | Message |
|------|------|---------|----------------|------------|---------------|---------|
| (pending) | | | | | | |

## macOS

Not run locally (no host available). Treated as "same as Linux" for decision purposes;
verified later via CI if a macOS runner is added.

## Decision

(filled in after probe runs)
```

- [ ] **Step 6: Commit the probe scaffold**

Run: `git add tmp/iox-probe/iox-probe.csproj tmp/iox-probe/Program.cs tmp/iox-probe/.gitignore tmp/iox-probe/findings.md`

Wait — `tmp/` is gitignored. We want the probe to stay local-only and NOT be committed (it's throwaway diagnostic infrastructure). Skip this step. The `findings.md` content will be hand-copied into the ADR in Task 4.

Verify: `git status --short` should still NOT show any of the probe files (because `tmp/` is in the root `.gitignore`).

---

### Task 2: Capture Windows broken-pipe data

**Files:** modify `tmp/iox-probe/findings.md`

- [ ] **Step 1: Run broken-pipe scenario via Git Bash**

Run: `tmp/iox-probe/bin/Release/net9.0/iox-probe.exe spam | head -1`
Expected: stdout shows the first line. Stderr shows one or more `PROBE\twin\tspam\t...` lines.

Capture the entire stderr output. If multiple `PROBE` lines appear (one per write attempt after pipe closes), keep only the FIRST — that's the diagnostic moment.

**Validity check (mandatory):** if stderr shows ZERO `PROBE` lines, the run is INVALID — the pipe buffer absorbed all 8MB without the producer noticing, OR `head` consumed faster than we expected. Do NOT record this as "no broken pipe seen → broad swallow safe." Instead, increase the volume in `Program.cs` (raise the 8MB cap) and re-run. The probe must observe at least one exception per mode for the data to be usable.

- [ ] **Step 2: Run large-chunk binary stream variant**

Run: `tmp/iox-probe/bin/Release/net9.0/iox-probe.exe spam-large | head -c 5`
Expected: stdout shows partial bytes. Stderr shows `PROBE\twin\tspam-large\t...`.

Same validity check applies — at least one `PROBE` line required.

- [ ] **Step 3: Run close-then-write baseline**

Run: `tmp/iox-probe/bin/Release/net9.0/iox-probe.exe close-then-write`
Expected: Stderr `PROBE\twin\tclose-then-write\tSystem.ObjectDisposedException\t...`. This establishes the baseline shape for an unrelated exception class so we can confirm the broken-pipe HResult is distinct AND so we can decide explicitly whether to catch ObjectDisposedException at the parser sites (separate from broken-pipe IOException).

- [ ] **Step 4: Update findings.md Windows table**

Edit `tmp/iox-probe/findings.md` Windows section with the captured rows. Note specifically:
- Was the exception `IOException` directly, or `Win32Exception` wrapped, or something else?
- Is the low-16-bit HResult `109` (`ERROR_BROKEN_PIPE`) or `232` (`ERROR_NO_DATA`) — or something else?
- Is the message localised (which would weaken any message-string match strategy)?
- Is the inner exception non-null and what is its `GetType().FullName`?

- [ ] **Step 5: No commit — findings stays under tmp/**

---

### Task 3: Capture Linux broken-pipe data (best effort)

**Files:** modify `tmp/iox-probe/findings.md`

- [ ] **Step 1: Detect WSL availability**

Run: `wsl.exe -e uname -s 2>/dev/null`
Expected on success: `Linux`.
If it errors or returns empty, WSL is not available — skip Linux-host capture, proceed to Step 4 to record the gap and rely on CI.

- [ ] **Step 2: If WSL is available, build the probe inside WSL**

Run inside WSL: `dotnet build /mnt/d/projects/winix/tmp/iox-probe/iox-probe.csproj -c Release`
Expected: Build succeeded. (Note: a Linux-target build will produce `bin/Release/net9.0/iox-probe` without the `.exe` suffix.)

- [ ] **Step 3: If WSL is available, run the three modes**

Inside WSL:
- `tmp/iox-probe/bin/Release/net9.0/iox-probe spam | head -1`
- `tmp/iox-probe/bin/Release/net9.0/iox-probe spam-large | head -c 5`
- `tmp/iox-probe/bin/Release/net9.0/iox-probe close-then-write`

Capture stderr `PROBE` lines. Specifically check whether the `IOException`'s inner exception is non-null and carries a Linux errno (typically `EPIPE = 32`).

**Validity check (mandatory):** as in Task 2, ZERO `PROBE` lines for a spam mode means the run is invalid — the producer never observed the broken pipe. Do NOT record "no exception" as evidence for broad-swallow. Instead diagnose (raise the 8MB cap, or check whether `head` actually closed) and re-run.

- [ ] **Step 4: Update findings.md Linux table**

If WSL was not available, write the row as: `(deferred to CI / not captured)` and add a one-line note:

> Linux host capture was not feasible in this session. Deferring to a CI smoke job if one is added later. Decision below is grounded in Windows observations + documented Mono/.NET runtime behaviour.

- [ ] **Step 5: No commit — findings stays under tmp/**

---

### Task 4: Decide and write ADR

**Files:**
- Create: `docs/plans/2026-04-26-shellkit-broken-pipe-adr.md`

- [ ] **Step 1: Decide the strategy based on observed data**

Decision rule (apply in order — first match wins):

1. **If both Windows and Linux observations show a stable, distinguishable broken-pipe HResult (low 16 bits `109` on Windows, inner `EPIPE = 32` on Linux), AND the exception types are predictable (`IOException` directly, not deeply wrapped):** choose **Option B — `BrokenPipeDetector` helper**. We can reliably detect broken-pipe without false positives on disk-full.

2. **If observations are inconsistent (HResults vary, types differ across runtimes, messages localised):** choose **Option A — broad-swallow** at the parser sites only. Document explicitly that this hides disk-full for the help/version/describe path but the path is fundamentally diagnostic-shaped (short interactive output, not data-shaped), so the visibility loss is acceptable. Do NOT extend broad-swallow to data-path Safe* helpers without separate analysis.

3. **If only Windows data is available:** choose **Option A** with a "Linux observation pending — revisit if CI adds a Linux probe" caveat in the ADR.

- [ ] **Step 2: Write the ADR**

Write `docs/plans/2026-04-26-shellkit-broken-pipe-adr.md`:

```markdown
# ADR — Suite-wide broken-pipe handling for Winix tools

**Date:** 2026-04-26
**Status:** Accepted
**Related design / probe:** `docs/plans/2026-04-26-shellkit-broken-pipe.md` (this plan), `tmp/iox-probe/findings.md` (transient — captured below).

## Context

`<any-winix-tool> --help | head -1` produced an `unexpected_error` envelope across all 22 tools because the three `Console.WriteLine` calls in `Yort.ShellKit/CommandLineParser.cs` (lines 464, 470, 476) were unwrapped. The fix shape depended on a suite-wide design question: should Winix tools distinguish broken-pipe `IOException` from disk-full / permission `IOException`, or broad-swallow all of them?

The two competing concerns:
- ✅ Broad swallow is simple and matches Unix convention for `tool --help | head`.
- ❌ Broad swallow on data paths silently truncates files on disk-full / permission errors.

The blocker for resolution was empirical: no clean cross-platform .NET API distinguishes the cases, and the documented HResult values are Windows-only. Whether they survive cleanly on Linux/macOS through the .NET runtime's exception wrapping was unknown.

## Probe results

(Paste the contents of `tmp/iox-probe/findings.md` here so the ADR is self-contained — the probe directory is gitignored.)

## Decisions

### Decision 1: Strategy

(Write either "Option A — broad-swallow" or "Option B — BrokenPipeDetector helper", with the rationale per Step 1's decision rule.)

**Rationale:**

- (Tied to actual probe data — concrete numbers, not hand-waving.)

**Trade-offs accepted:**

- (Spell out what we're losing.)

### Decision 2: Scope of application

The chosen pattern applies to:

1. `src/Yort.ShellKit/CommandLineParser.cs` — three `Console.WriteLine` sites for `--help`, `--version`, `--describe` (direct fix).
2. `src/wargs/Program.cs` `SafeWriteLine` and `src/Winix.Wargs/JobRunner.cs` `SafeWrite` / `SafeWriteAsync` — only updated **if Option B** was chosen (otherwise they already match Option A in shape).
3. Other tools' direct console writes (audited in Task 9 below) — same treatment.

### Decision 3: Documentation

A "Console output discipline" section is added to `CLAUDE.md` (root) so the convention is discoverable for the next tool author.

## Options considered

- **Option A — broad swallow at parser sites:** chosen if probe shows inconsistent / unreliable HResult.
- **Option B — `BrokenPipeDetector` helper in ShellKit:** chosen if probe shows stable, distinguishable HResult/inner-exception across Windows + Linux.
- **Option C — message-string match:** rejected. Localisation-fragile; no probe data needed to disqualify.
- **Option D — refactor parser to return text and let caller write:** rejected as out of scope. Cleaner layering but a much wider change to all 22 tools' Program.cs; can be revisited later as a refactor PR.

## Decisions explicitly deferred

| Topic | Why deferred |
|-------|--------------|
| macOS empirical validation | No host available locally. Will validate via CI if a macOS runner is added. |
| `Winix.FileWalk`-based glob expansion on Windows positionals | Separate ShellKit-level fix, deferred to its own PR (see `project_windows_glob_gap.md`). |
| Refactor parser to non-writing API (Option D) | Tracked but not blocking. Revisit in a future ShellKit cleanup pass. |
| Console codepage / `EncoderFallbackException` on non-UTF-8 Windows consoles | Probe did not exercise this path. Per the suite-wide CLAUDE.md rule, every Winix tool's `Main` sets UTF-8 console encoding up front, so the parser is reached after that fix is in effect. Revisit if a non-UTF-8 environment regression is reported. |
```

- [ ] **Step 3: Commit the ADR**

Run: `git add docs/plans/2026-04-26-shellkit-broken-pipe-adr.md`
Run: `git commit -m "docs(shellkit): ADR for broken-pipe handling decision"`

---

## Phase B — Apply the fix

The remaining tasks branch slightly depending on the ADR decision. Each task spells out both Option A and Option B variants where they differ.

### Task 5: Subprocess regression test for `--help | head -1` (TDD red)

**Files:**
- Create: `tests/Yort.ShellKit.Tests/StandardFlagsPipeTests.cs`

- [ ] **Step 1: Identify a stable test target**

`Yort.ShellKit` is a library — it has no executable. The cleanest subprocess test is to run an existing built tool (e.g. `timeit`) and assert `--help | head -1` exits clean and emits nothing on stderr. We test the *fixed behaviour* by spawning a real Winix tool. Pick `timeit` because it has the simplest help text and lowest dependency surface.

- [ ] **Step 2: Add ProjectReference so the build graph forces timeit to be built first**

> **Adversarial-review note (F5):** without `<ProjectReference>` to timeit (and any other tools the test exercises), concurrent-test-run build ordering is undefined and the resolver may find a stale binary. Declare the dependency explicitly.

Edit `tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj` and add to the `<ItemGroup>` containing `<ProjectReference>` entries (or create one if absent):

```xml
    <ProjectReference Include="..\..\src\timeit\timeit.csproj">
      <!-- Build-only reference: we shell out to the compiled binary, we don't link to it. -->
      <Private>false</Private>
      <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
    </ProjectReference>
```

`Private=false` and `ReferenceOutputAssembly=false` mean the test assembly does NOT pick up timeit's outputs as references — it only forces the build order.

- [ ] **Step 3: Write the failing test**

Write `tests/Yort.ShellKit.Tests/StandardFlagsPipeTests.cs`:

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Yort.ShellKit.Tests;

/// <summary>
/// Pins the externally observable contract: a Winix tool whose `--help`, `--version`,
/// or `--describe` output is consumed by an early-closing pipe (`| head -1`) MUST exit
/// clean (0) and MUST NOT emit an `unexpected_error` envelope on stderr. Regression
/// guard for the IOException leak fixed in 2026-04-26 (see
/// docs/plans/2026-04-26-shellkit-broken-pipe-adr.md).
/// </summary>
public class StandardFlagsPipeTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    [InlineData("--describe")]
    public void StandardFlag_PipedToHead_ExitsCleanAndNoErrorEnvelope(string flag)
    {
        string toolPath = ResolveToolPath("timeit");

        // Spawn timeit with stdin=closed so it can't be confused for needing input,
        // and read stdout one line then close — equivalent to `| head -1`.
        var psi = new ProcessStartInfo(toolPath, flag)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)!;
        proc.StandardInput.Close();

        // Read the first line of stdout, then close — this triggers the broken-pipe
        // condition on the next write attempt by the tool.
        string? firstLine = proc.StandardOutput.ReadLine();
        proc.StandardOutput.Close();

        // Drain stderr fully so we can assert on its content.
        string stderr = proc.StandardError.ReadToEnd();

        bool exited = proc.WaitForExit(15_000);
        if (!exited)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best-effort cleanup */ }
            try { proc.WaitForExit(5_000); } catch { /* best-effort cleanup */ }
            Assert.Fail($"tool {toolPath} {flag} did not exit within 15s after stdout was closed. " +
                $"stdout-first-line='{firstLine}' stderr='{stderr}'");
        }

        Assert.NotNull(firstLine);
        Assert.Equal(0, proc.ExitCode);
        Assert.DoesNotContain("unexpected_error", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("IOException", stderr, StringComparison.Ordinal);
    }

    internal static string ResolveToolPath(string toolName)
    {
        // Walk up from the test assembly to the repo root, then into the tool's
        // build output. This avoids hard-coding solution-relative paths.
        string asmDir = Path.GetDirectoryName(typeof(StandardFlagsPipeTests).Assembly.Location)!;
        string? cursor = asmDir;
        while (cursor is not null && !File.Exists(Path.Combine(cursor, "Winix.sln")))
        {
            cursor = Path.GetDirectoryName(cursor);
        }
        Assert.NotNull(cursor);

        // Pick the matching configuration of the tool. The test's own configuration
        // (Debug/Release) is the most reliable cue.
        string config = asmDir.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar)
            ? "Release" : "Debug";

        // Match the test framework moniker (net8.0 / net9.0) by reading from the
        // assembly path. Validate it actually looks like a TFM — if a future runtime
        // identifier directory (e.g. win-x64) gets injected, fail loudly with a
        // diagnostic rather than producing a bogus "binary not found" path.
        string tfm = new DirectoryInfo(asmDir).Name;
        Assert.Matches(@"^net\d+\.\d+$", tfm);

        string exe = OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;
        string candidate = Path.Combine(cursor!, "src", toolName, "bin", config, tfm, exe);
        Assert.True(File.Exists(candidate), $"{toolName} binary not found at {candidate}. " +
            "Build the solution (`dotnet build Winix.sln`) before running this test.");
        return candidate;
    }
}
```

- [ ] **Step 4: Run the test to confirm it fails**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --filter "FullyQualifiedName~StandardFlagsPipeTests" -c Debug`
Expected: BUILD timeit first if it isn't already, then the test FAILS — likely with `unexpected_error` in stderr OR a non-zero exit code from timeit. Capture the actual failure output for the commit message.

If for any reason the test does not fail (e.g. on this specific platform the broken-pipe doesn't reach the parser write), record that as data and skip to Step 6 — the test still earns its keep as a regression pin going forward.

- [ ] **Step 5: Build timeit explicitly if the test errored on missing binary**

Run: `dotnet build src/timeit/timeit.csproj -c Debug`
Then re-run Step 4.

- [ ] **Step 6: Do not commit yet** — wait until the implementation makes it green.

---

### Task 6: Implement the chosen pattern in CommandLineParser

**Files:**
- Modify: `src/Yort.ShellKit/CommandLineParser.cs:461-479`

#### Option A — narrow swallow (IOException only)

> **Adversarial-review note (F2):** the original draft of this task swallowed `Exception` minus OOM/SOE, mirroring wargs's `SafeWriteLine`. That precedent is for stderr-diagnostic writes whose rationale is "diagnostic strictly weaker than production." The parser's `--help`/`--version`/`--describe` are *production* paths for an introspection invocation, and `GenerateHelp()` / `GenerateDescribe()` can raise real bugs (e.g. `InvalidOperationException` from a misconfigured argument table). Catching them silently makes `--help` exit 0 with no output — indistinguishable from success. So we narrow the catch to `IOException` only — the empirically-justified surface from the probe.

- [ ] **Step 1A: Extract a helper and route the three sites through it**

In `src/Yort.ShellKit/CommandLineParser.cs`, replace lines 461-479 with:

```csharp
        // Handle --help and --version
        if (flagsSet.Contains("--help") && _standardFlagsRegistered)
        {
            WriteIntrospectionLine(GenerateHelp());
            isHandled = true;
            handledExitCode = 0;
        }
        else if (flagsSet.Contains("--version") && _standardFlagsRegistered)
        {
            WriteIntrospectionLine($"{_toolName} {_version}");
            isHandled = true;
            handledExitCode = 0;
        }
        else if (flagsSet.Contains("--describe") && _standardFlagsRegistered)
        {
            WriteIntrospectionLine(GenerateDescribe());
            isHandled = true;
            handledExitCode = 0;
        }
```

Then add the helper method just below the `Parse` method (search for `private void BuildLookups()` and insert immediately above it):

```csharp
    /// <summary>
    /// Best-effort write of introspection output (--help / --version / --describe) to stdout.
    /// A broken downstream pipe (`tool --help | head -1`) raises <see cref="IOException"/>;
    /// so does a disk-full / permission-revoked redirection. For the introspection path
    /// these are all non-actionable: the user invoked a help command, the consumer
    /// truncated, exit silently.
    ///
    /// Catch is narrow on purpose: ONLY <see cref="IOException"/>. We do NOT swallow
    /// <see cref="ObjectDisposedException"/> (programmer bug — stdout closed before
    /// parser ran), <see cref="EncoderFallbackException"/> (codepage mismatch — surface
    /// so the user knows to set UTF-8), or any exception from <see cref="GenerateHelp"/>
    /// / <see cref="GenerateDescribe"/> (a thrown InvalidOperationException there is a
    /// parser-config bug we MUST report, not hide).
    ///
    /// Trade-off accepted: disk-full visibility is lost for the introspection path.
    /// That is acceptable here because the path is short text to interactive users, not
    /// a data flow. Captured in 2026-04-26 ADR.
    ///
    /// Developer escape hatch: set environment variable WINIX_DEBUG_IO=1 to surface
    /// the swallowed exception's type+message to stderr (best-effort). Useful when
    /// debugging "my tool produces no output" reports.
    /// </summary>
    private static void WriteIntrospectionLine(string text)
    {
        try
        {
            Console.WriteLine(text);
        }
        catch (IOException ex)
        {
            if (string.Equals(Environment.GetEnvironmentVariable("WINIX_DEBUG_IO"), "1", StringComparison.Ordinal))
            {
                try
                {
                    Console.Error.WriteLine($"[WINIX_DEBUG_IO] WriteIntrospectionLine swallowed {ex.GetType().Name} (HResult=0x{ex.HResult:X8}): {ex.Message}");
                }
                catch { /* best-effort diagnostic */ }
            }
        }
    }
```

- [ ] **Step 2A: Run the regression test from Task 5**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --filter "FullyQualifiedName~StandardFlagsPipeTests" -c Debug`

Expected: timeit must be rebuilt first because it now picks up the new ShellKit. The test framework should rebuild it automatically due to ProjectReference; if it doesn't:

Run: `dotnet build src/timeit/timeit.csproj -c Debug`

Then re-run the test. Expected: PASS — exit code 0, no `unexpected_error` on stderr.

- [ ] **Step 3A: Add in-process trade-off pin tests**

> **Adversarial-review note (F4):** the subprocess test from Task 5 pins the broken-pipe case. We also need to pin the documented trade-off: under Option A, a non-broken-pipe `IOException` (e.g. synthetic disk-full HResult 112) is ALSO swallowed. Without this test, a future refactor that narrows the catch silently breaks the documented contract.

Add to `tests/Yort.ShellKit.Tests/StandardFlagsPipeTests.cs` (in the same file — they share fixtures):

```csharp
using System.IO;

// ... existing usings retained ...

[Collection(nameof(ConsoleOutputCollection))]
public class StandardFlagsHelperTests
{
    /// <summary>
    /// TextWriter that throws a synthetic IOException with a configurable HResult on
    /// any Write attempt. Used to verify that WriteIntrospectionLine swallows
    /// IOException regardless of HResult (Option A's documented trade-off).
    /// </summary>
    private sealed class ThrowingWriter : TextWriter
    {
        private readonly int _hrLow;
        public ThrowingWriter(int hrLow) { _hrLow = hrLow; }
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(char value) { throw IO(); }
        public override void Write(string? value) { throw IO(); }
        public override void WriteLine(string? value) { throw IO(); }
        private IOException IO() => new IOException("synthetic") { HResult = unchecked((int)0x80070000 | _hrLow) };
    }

    [Theory]
    [InlineData(109)]   // ERROR_BROKEN_PIPE — should be swallowed
    [InlineData(112)]   // ERROR_DISK_FULL — also swallowed under Option A (trade-off pin)
    [InlineData(32)]    // EPIPE — swallowed
    public void Parse_HelpFlag_SwallowsIOExceptionRegardlessOfHResult(int hrLow)
    {
        // Build a parser with --help registered (matches every Winix tool's setup).
        var parser = new CommandLineParser("test-tool", "1.0.0").RegisterStandardFlags();
        var originalOut = Console.Out;
        Console.SetOut(new ThrowingWriter(hrLow));
        try
        {
            // Should not throw — Option A swallows all IOException at this site.
            var result = parser.Parse(new[] { "--help" });
            Assert.True(result.IsHandled);
            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Parse_HelpFlag_DoesNotSwallowProgrammerBugException()
    {
        // ObjectDisposedException is a programmer bug — must propagate.
        var parser = new CommandLineParser("test-tool", "1.0.0").RegisterStandardFlags();
        var originalOut = Console.Out;
        var disposed = new StreamWriter(Stream.Null);
        disposed.Dispose();
        Console.SetOut(disposed);
        try
        {
            Assert.Throws<ObjectDisposedException>(() => parser.Parse(new[] { "--help" }));
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
```

(Adjust constructor / RegisterStandardFlags signature to match the actual ShellKit API surface — the implementer should follow what the existing `CommandLineParserTests.cs` does for setup.)

- [ ] **Step 4A: Run the full ShellKit test suite to confirm no regression**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj -c Debug`
Expected: ALL pass — including the new `StandardFlagsHelperTests` cases.

#### Option B — BrokenPipeDetector helper

(Apply this variant only if the ADR decision was Option B.)

- [ ] **Step 1B: Add the detector**

Create `src/Yort.ShellKit/BrokenPipeDetector.cs`:

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Yort.ShellKit;

/// <summary>
/// Detects whether an <see cref="IOException"/> represents a broken downstream pipe
/// (consumer closed stdout early, e.g. `tool --help | head -1`) versus a real I/O failure
/// (disk full, permission revoked) that the user should hear about.
/// </summary>
/// <remarks>
/// Cross-platform HResult values are based on the empirical probe captured 2026-04-26.
/// Windows: ERROR_BROKEN_PIPE = 109, ERROR_NO_DATA = 232, ERROR_PIPE_NOT_CONNECTED = 233.
/// Linux/macOS: EPIPE = 32, surfaced via the inner exception's HResult on .NET's
/// translation through Win32Exception.
/// </remarks>
public static class BrokenPipeDetector
{
    public static bool IsBrokenPipe(IOException ex)
    {
        if (ex is null) { throw new ArgumentNullException(nameof(ex)); }

        int hr = ex.HResult & 0xFFFF;
        int innerHr = (ex.InnerException?.HResult ?? 0) & 0xFFFF;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // 109 = ERROR_BROKEN_PIPE, 232 = ERROR_NO_DATA, 233 = ERROR_PIPE_NOT_CONNECTED
            return hr is 109 or 232 or 233 || innerHr is 109 or 232 or 233;
        }

        // Linux + macOS: EPIPE = 32. Some .NET versions surface it on the outer
        // IOException, others wrap inside a Win32Exception or similar.
        return hr == 32 || innerHr == 32;
    }
}
```

- [ ] **Step 2B: Replace the three sites with try/catch using the detector**

In `src/Yort.ShellKit/CommandLineParser.cs:461-479`, replace with:

```csharp
        // Handle --help and --version. A broken downstream pipe (tool --help | head -1)
        // is benign for introspection output — exit silently. Any other IOException
        // (disk full, etc.) propagates so the caller can surface it.
        if (flagsSet.Contains("--help") && _standardFlagsRegistered)
        {
            try { Console.WriteLine(GenerateHelp()); }
            catch (IOException ex) when (BrokenPipeDetector.IsBrokenPipe(ex)) { }
            isHandled = true;
            handledExitCode = 0;
        }
        else if (flagsSet.Contains("--version") && _standardFlagsRegistered)
        {
            try { Console.WriteLine($"{_toolName} {_version}"); }
            catch (IOException ex) when (BrokenPipeDetector.IsBrokenPipe(ex)) { }
            isHandled = true;
            handledExitCode = 0;
        }
        else if (flagsSet.Contains("--describe") && _standardFlagsRegistered)
        {
            try { Console.WriteLine(GenerateDescribe()); }
            catch (IOException ex) when (BrokenPipeDetector.IsBrokenPipe(ex)) { }
            isHandled = true;
            handledExitCode = 0;
        }
```

- [ ] **Step 3B: Add unit tests for `BrokenPipeDetector`**

Create `tests/Yort.ShellKit.Tests/BrokenPipeDetectorTests.cs`:

```csharp
using System.IO;
using Xunit;
using Yort.ShellKit;

namespace Yort.ShellKit.Tests;

public class BrokenPipeDetectorTests
{
    [Fact]
    public void IsBrokenPipe_NullArg_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() => BrokenPipeDetector.IsBrokenPipe(null!));
    }

    // Construct an IOException with a specific HResult to verify detection.
    [Theory]
    [InlineData(109, true)]
    [InlineData(232, true)]
    [InlineData(233, true)]
    [InlineData(32,  true)]   // EPIPE — only relevant on non-Windows but we don't
                              //         platform-gate the test, since our detector returns
                              //         true on all platforms when low-16 == 32.
                              //         If you're on Windows and this fails, the detector
                              //         must be platform-gated; see the test on the next line.
    [InlineData(112, false)]  // ERROR_DISK_FULL
    [InlineData(5,   false)]  // ERROR_ACCESS_DENIED
    [InlineData(0,   false)]
    public void IsBrokenPipe_DetectsExpectedHResults(int hrLow, bool expected)
    {
        var ex = new IOException("synthetic") { HResult = unchecked((int)0x80070000 | hrLow) };
        Assert.Equal(expected, BrokenPipeDetector.IsBrokenPipe(ex));
    }
}
```

> **NOTE on EPIPE = 32 expectation:** if the probe data shows that on Windows the value 32 *can* legitimately appear with a non-broken-pipe meaning (it shouldn't — Windows error codes don't collide with EPIPE in this range), platform-gate the detector and remove the `(32, true)` row from the Windows table. Otherwise leave as-is — the detector treats 32 as broken pipe on all platforms because no Windows IOException with low-16 == 32 represents anything else in practice.

- [ ] **Step 4B: Run all ShellKit tests**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj -c Debug`
Expected: ALL pass, including the new `BrokenPipeDetectorTests` and `StandardFlagsPipeTests`.

#### Common: commit

- [ ] **Step 4: Commit the fix**

Run: `git add src/Yort.ShellKit/ tests/Yort.ShellKit.Tests/`
Run (Option A): `git commit -m "fix(shellkit): wrap StandardFlags Console writes in best-effort swallow"`
Run (Option B): `git commit -m "fix(shellkit): detect broken pipe in StandardFlags writes"`

---

### Task 7: Audit other tools for direct Console.Write* sites

**Files:** check across `src/`

- [ ] **Step 1: Grep for direct stdout writes**

Use the Grep tool with pattern `Console\.(Write|Out\.)` against `src/`. Capture hits in a scratch table.

> **Adversarial-review note (F9):** the unfiltered grep includes Safe* helper *implementations*, comments, and string literals — those are valid sites that already do the right thing. Manually filter:

For each hit:
1. **Drop** if the surrounding method's name starts with `Safe` (Safe* helper internals — these legitimately call `Console.Write` inside their try/catch).
2. **Drop** if the line is inside a comment (`//` or `///` lines, `/* */` blocks).
3. **Drop** if the line is inside a string literal (e.g. an example in a help text, `"Console.Write..."`).
4. **Keep** everything else.

**Sanity check:** if the keep list is empty across all 22 tools, the grep was likely misapplied — re-run with a different pattern (e.g. add `-w` for word-boundary or check for `System.Console.`). A truly empty result is suspicious because we'd expect at least a few tools to have direct Console writes in their `Program.cs` startup path. Document this calibration check in the audit notes either way (empty-after-verification IS a valid result, but it must be a verified empty, not a default).

- [ ] **Step 2: Catalogue the findings**

For each direct write site that survived the filter, record in a temporary scratch list:
- File and line.
- Method name surrounding the call.
- Is it stdout or stderr?
- Is it on a path that runs under `--help`, `--version`, `--describe`, or other introspection-shaped output?
- Is it on a path that emits user data?

If the list is empty (verified empty per the sanity check above), skip Task 8. Note in the ADR that the audit was performed and produced no in-scope sites.

If the list is non-empty, the writes that are introspection-shaped get the same treatment as the parser fix. Writes that are data-shaped do NOT get touched in this PR — record them in the ADR's "Decisions explicitly deferred" table for later.

- [ ] **Step 3: Apply the fix to introspection-shaped sites**

For each in-scope site found, wrap in the chosen pattern (broad swallow per Option A, or `BrokenPipeDetector` per Option B). Match the helper convention already present in that tool — don't introduce a third pattern.

- [ ] **Step 4: Run the full solution test suite**

Run: `dotnet test Winix.sln -c Debug`
Expected: ALL pass.

- [ ] **Step 5: Commit any audit-driven changes**

Run: `git add` for the modified files + tests.
Run: `git commit -m "fix(suite): bring direct Console writes onto Safe* convention (audit follow-up)"`

If no audit findings, skip the commit.

---

### Task 8: Add CLAUDE.md convention entry

**Files:**
- Modify: `d:/projects/winix/CLAUDE.md` — under `## Conventions`

- [ ] **Step 1: Add the convention bullet**

Locate the `## Conventions` section in `d:/projects/winix/CLAUDE.md`. Add this bullet immediately after the existing "All output formatting in class library" bullet (or wherever the surrounding bullets group thematically — match local placement style):

```markdown
- **Console writes must use a Safe* helper or the parser's introspection helper.** Bare `Console.WriteLine` / `Console.Out.Write*` is a defect. Why: an early-closing downstream consumer (`tool --help | head -1`, `tool --json | jq '.[0]'`) raises `IOException` on the first write after the pipe closes; if unwrapped it surfaces as an `unexpected_error` envelope and exit code 126. The chosen suite-wide pattern is documented in `docs/plans/2026-04-26-shellkit-broken-pipe-adr.md`. New stdout writes go through `SafeWrite` / `SafeWriteAsync`; new tool-introspection writes (help/version/describe) go through `WriteIntrospectionLine` (Option A) OR a `BrokenPipeDetector`-guarded `try`/`catch` (Option B), matching whichever the ADR settled on.
```

(Edit "Option A" / "Option B" wording out — keep only the one the ADR chose. Do not leave both.)

- [ ] **Step 2: Commit**

Run: `git add CLAUDE.md`
Run: `git commit -m "docs: add Console-write discipline convention to CLAUDE.md"`

---

### Task 9: Subprocess regression test for one more tool (defence in depth)

**Files:**
- Modify: `tests/Yort.ShellKit.Tests/StandardFlagsPipeTests.cs`

- [ ] **Step 1: Parametrise across two tools**

The Task 5 test pinned `timeit`. Extend it to also exercise one tool with a more substantial help text — `wargs` — so we don't regress per-tool. In `StandardFlagsPipeTests.cs`, add a `[Theory]` overload that takes both `tool` and `flag`:

```csharp
[Theory]
[InlineData("timeit", "--help")]
[InlineData("timeit", "--version")]
[InlineData("timeit", "--describe")]
[InlineData("wargs",  "--help")]
public void Tool_StandardFlagPipedToHead_ExitsCleanAndNoErrorEnvelope(string tool, string flag)
{
    string toolPath = ResolveToolPath(tool);
    // (same body as the existing test — extract into a method, share with the original
    //  single-tool test, OR just delete the original and keep this one.)
    ...
}
```

Refactor `ResolveTimeitPath` into `ResolveToolPath(string toolName)` and adjust accordingly. Keep the original single-tool test method or delete it — your call, both are fine.

- [ ] **Step 2: Build wargs (if not already)**

Run: `dotnet build src/wargs/wargs.csproj -c Debug`
Expected: Build succeeded.

- [ ] **Step 3: Run the parameterised test**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --filter "FullyQualifiedName~StandardFlagsPipeTests" -c Debug`
Expected: ALL four (or more) cases PASS.

- [ ] **Step 4: Commit**

Run: `git add tests/Yort.ShellKit.Tests/StandardFlagsPipeTests.cs`
Run: `git commit -m "test(shellkit): expand StandardFlags pipe test to wargs"`

---

### Task 10: Full solution build + memory updates

**Files:**
- Modify: `~/.claude/projects/d--projects-winix/memory/project_shellkit_standardflags_pipe.md`
- Modify: `~/.claude/projects/d--projects-winix/memory/project_broken_pipe_design_question.md`
- Modify: `~/.claude/projects/d--projects-winix/memory/MEMORY.md`

- [ ] **Step 1: Run the full solution test suite one more time**

Run: `dotnet test Winix.sln -c Debug`
Expected: ALL pass. Note total test count — should be ≥2057 (the prior baseline) plus the new tests added this PR.

- [ ] **Step 2: Update the StandardFlags memory to "resolved"**

Edit `~/.claude/projects/d--projects-winix/memory/project_shellkit_standardflags_pipe.md`:

Change the `description:` frontmatter to reflect resolution. Add a new line at the top of the body:

```
**Status (2026-04-26): RESOLVED.** Fixed via `WriteIntrospectionLine` helper (Option A) / `BrokenPipeDetector` (Option B). See `docs/plans/2026-04-26-shellkit-broken-pipe-adr.md`. Subprocess regression test in `tests/Yort.ShellKit.Tests/StandardFlagsPipeTests.cs` pins the contract.
```

(Use whichever Option the ADR chose. Delete the alternative.)

- [ ] **Step 3: Update the broken-pipe design question memory**

Edit `~/.claude/projects/d--projects-winix/memory/project_broken_pipe_design_question.md`:

Change the `description:` frontmatter to reflect resolution. Add at the top:

```
**Status (2026-04-26): RESOLVED.** Empirical probe in tmp/iox-probe (transient — captured in ADR). Decision: <Option A | Option B>. See `docs/plans/2026-04-26-shellkit-broken-pipe-adr.md`.
```

- [ ] **Step 4: Update MEMORY.md**

Edit `~/.claude/projects/d--projects-winix/memory/MEMORY.md`:

Update the two affected lines:

```
- [ShellKit StandardFlags pipe defect](project_shellkit_standardflags_pipe.md) — RESOLVED 2026-04-26. Fixed in CommandLineParser; subprocess regression test in ShellKit.Tests.
- [Broken-pipe handling: design question RESOLVED](project_broken_pipe_design_question.md) — RESOLVED 2026-04-26. See `docs/plans/2026-04-26-shellkit-broken-pipe-adr.md` for chosen pattern.
```

- [ ] **Step 5: No commit for memory** (memory is outside the repo).

- [ ] **Step 6: Final summary commit if anything is still staged**

Run: `git status --short`
Expected: clean working tree.

---

## Out of scope (explicitly deferred)

- **Windows glob expansion gap** (`project_windows_glob_gap.md`) — separate ShellKit-level fix, separate PR.
- **macOS empirical validation** — pending CI runner availability.
- **Refactor parser to non-writing API** — Option D in the ADR; revisit later as a ShellKit cleanup.
- **Audit of data-path Safe* helpers in tools beyond wargs** — only happens if Option B was chosen, and only for tools where the existing helper materially differs from the chosen pattern. Otherwise deferred to per-tool review when those tools are next touched.

## Self-review

- ✅ Spec coverage: Phase A (probe + decision) and Phase B (apply pattern + audit + tests + docs + memory) are both covered.
- ✅ Placeholders: each step contains the actual code, file paths, and commands needed.
- ✅ Type consistency: `WriteIntrospectionLine` (Option A) / `BrokenPipeDetector.IsBrokenPipe` (Option B) names are stable across all tasks that reference them.
- ✅ Branch hygiene: Task 0 verifies upstream is not auto-set to main before any push, per Troy's CLAUDE.md.
- ✅ Commit cadence: discrete commits per logical unit (ADR, fix, audit, tests, docs).
- ✅ Memory hygiene: Task 10 updates the two affected memory files plus MEMORY.md.

## Adversarial review integration log

Findings from the 2026-04-26 adversarial review (9 findings: 2 blockers, 5 test gaps, 2 explicit defers) were integrated as follows:

| ID | Bucket | Category | Resolution |
|----|--------|----------|------------|
| F1 | Plan blocker | 11 (State edges) / 1 (Input edges) | Probe rewritten: per-write `Flush()`, 8MB volume cap, third `spam-large` mode for binary path. Validity check added in Tasks 2 & 3 — zero `PROBE` lines is INVALID, not a permission to choose broad-swallow. |
| F2 | Plan blocker | 9 (Failure surfacing) / 4 (Resource limits) | Option A's `WriteIntrospectionLine` narrowed from `Exception` minus OOM/SOE to `IOException` only. `ObjectDisposedException`, `EncoderFallbackException`, and any exception from `GenerateHelp`/`GenerateDescribe` now propagate. XML doc updated to spell out the rationale. |
| F3 | Test gap | 6 (Lifecycle & cancellation) | Subprocess test now calls `proc.Kill(entireProcessTree: true)` on hang, with captured stdout/stderr in the failure message via `Assert.Fail`. |
| F4 | Test gap | 15 (Adversarial coverage) / 4 | New `StandardFlagsHelperTests` in-process class added with `ThrowingWriter` redirected via `Console.SetOut` — pins both the Option A trade-off (synthetic disk-full IS swallowed) and the programmer-bug propagation (synthetic `ObjectDisposedException` IS thrown). |
| F5 | Test gap | 12 (Testability) | `<ProjectReference>` to `timeit` added to test csproj with `Private=false` + `ReferenceOutputAssembly=false` for build-order-only dependency. TFM resolver now validates with `^net\d+\.\d+$` regex. |
| F6 | Test gap | 15 / 1 | **Resolution revised after reviewer pushback.** The original suggestion was an in-process `AnonymousPipeServerStream` round-trip; on closer inspection that API is designed for cross-process use and the snippet had bugs. Synthetic-HResult coverage in `BrokenPipeDetectorTests` (driven by Phase A probe data — the source of truth for the exception shape .NET actually raises) plus the end-to-end subprocess test in Task 5 (which exercises the real broken-pipe path) together cover what the in-process test would have covered. No third layer added. |
| F7 | Test gap | 14 (Observability) | Option A's `WriteIntrospectionLine` adds a `WINIX_DEBUG_IO=1` env var escape hatch — when set, swallowed exception type/HResult/message is written to stderr (best-effort). |
| F8 | Explicit defer | 1 (Input edges) | Codepage / EncoderFallbackException row added to the ADR template's "Decisions explicitly deferred" table, with cross-reference to the suite-wide UTF-8 console rule. |
| F9 | Explicit defer | 3 (Resource lifetime) | Task 7 audit step adds explicit filter rules (drop Safe* methods, comments, string literals) and a sanity check (empty result requires verification, not default acceptance). |

Plan is now READY for execution.

