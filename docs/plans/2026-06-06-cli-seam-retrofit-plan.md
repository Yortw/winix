# Cli.Run Seam Retrofit (schedule + retry) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Retrofit `Cli.Run` library seams to schedule and retry so their full parse→orchestrate→format→route paths are end-to-end testable, with zero behaviour change.

**Architecture:** Move each tool's orchestration from `Program.cs` into a new `Cli.cs` in its class library (`Winix.Schedule`, `Winix.Retry`), threading `TextWriter stdout/stderr` (+ `CancellationToken` for retry, + optional `ISchedulerBackend` for schedule) through. `Program.Main` becomes a thin shell owning only process-globals (`ConsoleEnv` setup, `CancelKeyPress`/CTS). Per design doc `docs/plans/2026-06-06-cli-seam-retrofit-design.md` and ADR.

**Tech Stack:** .NET 10, C#, xUnit, Yort.ShellKit `CommandLineParser`.

**Branch:** `feature/cli-seam-retrofit` (already created; design + ADR committed as `c16a809`).

**Hard rules for executors:**
- **Existing tests must pass UNMODIFIED.** If a change forces editing an existing test's assertions, STOP and report — that's a contract change. (Sole exception, explicitly authorised: the comment-only file-reference fixups in `ProgramMainTests.cs` described in Tasks 6 and 7 — no assertion may change.)
- Both libraries already reference `Yort.ShellKit` — do NOT add package/project references.
- Full braces always; `#nullable enable` per file where the file being created sits next to files that have it; XML doc comments on all public members.
- Preserve moved comments VERBATIM — every one encodes a past review finding. Where a comment references `Program.cs` line numbers that move, update only the file/location reference.
- Exit-code constants: `ExitCode.UsageError` = 125, `ExitCode.NotExecutable` = 126, `ExitCode.NotFound` = 127 (const ints in ShellKit).

---

### Task 0: Baseline capture (pre-refactor `--help`/`--describe` snapshots)

**Files:**
- Create: `tmp/seam-baseline/` (NOT committed — tmp/ is scratch space)

- [ ] **Step 1: Build both tools at current HEAD**

Run:
```bash
dotnet build /d/projects/winix/src/schedule/schedule.csproj --nologo -v quiet
dotnet build /d/projects/winix/src/retry/retry.csproj --nologo -v quiet
```
Expected: Build succeeded, 0 warnings.

- [ ] **Step 2: Capture help/describe baselines — stdout and stderr SEPARATELY**

(Adversarial-review F1: a merged `2>&1` capture cannot detect stream-ROUTING drift — a line moving between stdout and stderr — which is the single most likely defect class in a refactor that rewires `Console.Out`/`Console.Error` to threaded writers. Capture each stream to its own file.)

Run (Git Bash):
```bash
mkdir -p /d/projects/winix/tmp/seam-baseline
/d/projects/winix/src/schedule/bin/Debug/net10.0/schedule.exe --help > /d/projects/winix/tmp/seam-baseline/schedule-help.out 2> /d/projects/winix/tmp/seam-baseline/schedule-help.err
/d/projects/winix/src/schedule/bin/Debug/net10.0/schedule.exe --describe > /d/projects/winix/tmp/seam-baseline/schedule-describe.out 2> /d/projects/winix/tmp/seam-baseline/schedule-describe.err
/d/projects/winix/src/retry/bin/Debug/net10.0/retry.exe --help > /d/projects/winix/tmp/seam-baseline/retry-help.out 2> /d/projects/winix/tmp/seam-baseline/retry-help.err
/d/projects/winix/src/retry/bin/Debug/net10.0/retry.exe --describe > /d/projects/winix/tmp/seam-baseline/retry-describe.out 2> /d/projects/winix/tmp/seam-baseline/retry-describe.err
```
Expected: 8 files (the `.err` files may legitimately be empty — that emptiness is itself part of the baseline). These are diffed in Tasks 4 and 7 — both content drift AND routing drift show up here.

- [ ] **Step 3: Record pre-refactor test counts**

Run:
```bash
dotnet test /d/projects/winix/tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj --nologo -v quiet
dotnet test /d/projects/winix/tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj --nologo -v quiet
```
Expected: all green. Note the totals (schedule ~339, retry 108) — Tasks 2/6 must reproduce them exactly.

---

### Task 1: Fix retry.1.md exit-code-0 doc defect (found during planning)

**Context:** `retry.1.md` EXIT CODES says `**0**: Child process exited 0, or the exit code matched **--until**.` This is wrong — empirically verified 2026-06-06: `retry --until 7 ./exit7.cmd` exits **7** (pass-through; the JSON `exit_reason` is `succeeded` but the exit *code* passes through). README and `--describe` already state pass-through correctly; the man page line is the outlier.

**Files:**
- Modify: `src/retry/retry.1.md` (EXIT CODES section)
- Regenerate: `src/retry/man/man1/retry.1`

- [ ] **Step 1: Fix the source line**

In `src/retry/retry.1.md`, change:
```markdown
**0**
:   Child process exited 0, or the exit code matched **--until**.
```
to:
```markdown
**0**
:   Child process exited 0. (A non-zero exit code that matches **--until** still passes through as that code — only the *reason* is "succeeded", not the exit code.)
```

- [ ] **Step 2: Regenerate the man page**

Run:
```bash
pandoc -s -t man /d/projects/winix/src/retry/retry.1.md -o /d/projects/winix/src/retry/man/man1/retry.1
```

- [ ] **Step 3: Safety-diff the rendered page**

Run: `git -C /d/projects/winix diff src/retry/man/man1/retry.1`
Expected: ONLY the **0** entry changes (plus possible benign reflow). WARNING (known trap): if the diff shows `\[dq]` ↔ `\(lq` changes elsewhere, pandoc smart-quoting has mangled a literal `""` — the source already escapes those as `\"\"`; report if new ones appear.

- [ ] **Step 4: Commit**

```bash
git -C /d/projects/winix add src/retry/retry.1.md src/retry/man/man1/retry.1
git -C /d/projects/winix commit -m "docs(retry): fix man-page exit-0 claim — --until match passes through the child code, not 0 (verified empirically)"
```

---

### Task 2: schedule — create `Cli.cs`, thin `Program.cs`

**Files:**
- Create: `src/Winix.Schedule/Cli.cs`
- Rewrite: `src/schedule/Program.cs` (539 lines → ~25)

This is a **move**, not a rewrite. `src/schedule/Program.cs` content relocates into `Cli.cs` with the mechanical transformations below. Read the whole of `src/schedule/Program.cs` first.

**Do NOT add a top-level try/catch to schedule's `Cli.Run`** (adversarial-review F7) — schedule has no catch-all today; adding one is a behaviour change. (retry's catch-all moves because retry already has one. The asymmetry is deliberate — design §Out of scope.)

- [ ] **Step 1: Create `src/Winix.Schedule/Cli.cs`**

File skeleton (the `…` bodies are moved verbatim from `Program.cs` with the transformations listed after):

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Yort.ShellKit;

namespace Winix.Schedule;

/// <summary>
/// Library entry point for the schedule tool: parses arguments, dispatches subcommands,
/// and routes all output through the supplied writers. <c>Program.Main</c> is a thin shell
/// over this method so the full parse→orchestrate→format→route path is testable in-process.
/// </summary>
public static class Cli
{
    /// <summary>
    /// Runs the schedule CLI.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <param name="stdout">Receives JSON envelopes (<c>--json</c> output). JSON goes to stdout
    /// per the suite-wide convention — pipelines like <c>schedule next --json X | jq</c> only
    /// work when the JSON is on stdout. Tier-1 smoke verification 2026-05-09 found the pre-fix
    /// code routed JSON to stderr, breaking that pipeline shape; same suite-convention defect
    /// class as man F12, treex r2, whoholds, files, less, winix.</param>
    /// <param name="stderr">Receives plain-text tables, status messages, human diagnostics,
    /// and usage errors.</param>
    /// <param name="backend">Scheduler backend override, or <see langword="null"/> for the
    /// platform default (<see cref="SchtasksBackend"/> on Windows, <see cref="CrontabBackend"/>
    /// elsewhere). Supplied by tests to avoid touching the real OS scheduler.</param>
    /// <returns>Process exit code: 0 success, 125 usage error, 126 backend failure.</returns>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, ISchedulerBackend? backend = null)
    {
        // … body of current Program.Main MINUS the two ConsoleEnv lines, transformed per the
        // mapping table below. After the folder computation, add:
        //     ISchedulerBackend resolvedBackend = backend ?? CreateDefaultBackend();
        // and pass resolvedBackend + stdout + stderr into the Run* dispatch calls.
    }

    // … RunAdd / RunList / RunRemove / RunEnable / RunDisable / RunRun / WriteActionResult /
    //   RunHistory / RunNext / RejectIfMultiline / MultilineChars / SafeWriteLine / SafeWrite /
    //   CreateDefaultBackend / GetVersion — all moved per the mapping table.
}
```

**Transformation mapping table** (apply to every moved method):

| Old (Program.cs) | New (Cli.cs) |
|---|---|
| `static int Main(string[] args)` body | `Run` body (minus `ConsoleEnv.EnableAnsiIfNeeded()` + `ConsoleEnv.UseUtf8Streams()` — those stay in Main) |
| `Console.Error` in `result.WriteErrors(...)` / `result.WriteError(..., Console.Error)` | `stderr` |
| `SafeWriteLine(msg)` (3-arg-free helper writing to `Console.Error`) | `SafeWriteLine(stderr, msg)` |
| `SafeWrite(msg)` | `SafeWrite(stderr, msg)` |
| `SafeWriteLineToStdout(msg)` | `SafeWriteLine(stdout, msg)` — helper deleted; its doc rationale now lives on `Run`'s `stdout` param (already in the skeleton above) |
| `ISchedulerBackend backend = GetBackend();` inside each `Run*` (8 sites) | DELETE — backend arrives as a parameter, resolved once in `Run` |
| `private static ISchedulerBackend GetBackend()` | rename to `private static ISchedulerBackend CreateDefaultBackend()` (same body; update its XML doc summary to mention it's the null-backend default) |
| `RunAdd(result, version, jsonOutput, useColor, folder)` etc. (dispatch calls) | `RunAdd(result, version, jsonOutput, useColor, folder, resolvedBackend, stdout, stderr)` |

**New private method signatures** (bodies otherwise verbatim):

```csharp
private static int RunAdd(ParseResult result, string version, bool json, bool useColor, string folder, ISchedulerBackend backend, TextWriter stdout, TextWriter stderr)
private static int RunList(ParseResult result, string version, bool json, bool useColor, string folder, ISchedulerBackend backend, TextWriter stdout, TextWriter stderr)
private static int RunRemove(ParseResult result, string version, bool json, bool useColor, string folder, ISchedulerBackend backend, TextWriter stdout, TextWriter stderr)
private static int RunEnable(ParseResult result, string version, bool json, bool useColor, string folder, ISchedulerBackend backend, TextWriter stdout, TextWriter stderr)
private static int RunDisable(ParseResult result, string version, bool json, bool useColor, string folder, ISchedulerBackend backend, TextWriter stdout, TextWriter stderr)
private static int RunRun(ParseResult result, string version, bool json, bool useColor, string folder, ISchedulerBackend backend, TextWriter stdout, TextWriter stderr)
private static int WriteActionResult(ScheduleResult scheduleResult, string action, string name, string? cronStr, string version, bool json, bool useColor, TextWriter stdout, TextWriter stderr)
private static int RunHistory(ParseResult result, string version, bool json, bool useColor, string folder, ISchedulerBackend backend, TextWriter stdout, TextWriter stderr)
private static int RunNext(ParseResult result, string version, bool json, TextWriter stdout, TextWriter stderr)
```

(`RunNext` takes no backend/folder/useColor — it never had them.)

**New helper signatures** (replacing the three Console-bound ones; swallow semantics identical, XML docs adapted from the originals):

```csharp
private static void SafeWriteLine(TextWriter writer, string message)
{
    try { writer.WriteLine(message); }
    catch (System.IO.IOException) { /* writer unwritable — accept loss; do not mask exit code */ }
    catch (ObjectDisposedException) { /* host tore down the stream; same rationale as IOException */ }
}

private static void SafeWrite(TextWriter writer, string message)
{
    try { writer.Write(message); }
    catch (System.IO.IOException) { /* see SafeWriteLine */ }
    catch (ObjectDisposedException) { /* see SafeWriteLine */ }
}
```

`GetVersion` moves verbatim (it already anchors on `typeof(ScheduledTask).Assembly` — the library assembly — so the value is unchanged).

- [ ] **Step 2: Rewrite `src/schedule/Program.cs` as the thin shell**

Complete replacement content:

```csharp
#nullable enable

using System;
using Winix.Schedule;
using Yort.ShellKit;

namespace Schedule;

internal sealed class Program
{
    /// <summary>
    /// Entry point. Owns process-global console setup only; all parsing, dispatch, and
    /// output routing live in <see cref="Cli.Run"/> so they are testable in-process.
    /// </summary>
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        return Cli.Run(args, Console.Out, Console.Error);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build /d/projects/winix/src/schedule/schedule.csproj --nologo -v quiet`
Expected: Build succeeded, **0 warnings** (warnings-as-errors is on; missing XML docs on `Cli`/`Run` would fail the build).

- [ ] **Step 4: Existing tests pass UNMODIFIED**

Run: `dotnet test /d/projects/winix/tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj --nologo -v quiet`
Expected: same total as Task 0 Step 3, 0 failures. `ProgramMainTests.cs` (spawns the real binary) is the behaviour-neutrality guard — it must pass untouched. If `ProgramMainTests.cs` contains comments referencing `Program.cs` line numbers that have now moved, check with `grep -n "Program.cs" tests/Winix.Schedule.Tests/ProgramMainTests.cs` — update ONLY file/location references in comments, never assertions, and say so in the commit message.

- [ ] **Step 5: Commit**

```bash
git -C /d/projects/winix add src/Winix.Schedule/Cli.cs src/schedule/Program.cs
git -C /d/projects/winix commit -m "refactor(schedule): extract Cli.Run library seam — Program.cs is now a thin shell

Behaviour-neutral move per docs/plans/2026-06-06-cli-seam-retrofit-design.md.
Backend resolved once at the top of Cli.Run (was 8 hidden GetBackend() calls);
optional ISchedulerBackend param for test injection. Existing tests unmodified."
```

---

### Task 3: schedule — seam tests (`FakeSchedulerBackend` + `CliRunTests`)

**Files:**
- Create: `tests/Winix.Schedule.Tests/FakeSchedulerBackend.cs`
- Create: `tests/Winix.Schedule.Tests/CliRunTests.cs`

- [ ] **Step 1: Write the fake backend**

```csharp
#nullable enable

using System;
using System.Collections.Generic;

namespace Winix.Schedule.Tests;

/// <summary>
/// Configurable in-memory <see cref="ISchedulerBackend"/> for Cli.Run seam tests.
/// Records calls; returns the pre-set result objects. Never touches the OS scheduler.
/// </summary>
internal sealed class FakeSchedulerBackend : ISchedulerBackend
{
    public ScheduleResult AddResult { get; set; } = ScheduleResult.Ok("created");
    public ScheduleListResult ListResult { get; set; } = ScheduleListResult.Ok(Array.Empty<ScheduledTask>());
    public ScheduleResult RemoveResult { get; set; } = ScheduleResult.Ok("removed");
    public ScheduleResult EnableResult { get; set; } = ScheduleResult.Ok("enabled");
    public ScheduleResult DisableResult { get; set; } = ScheduleResult.Ok("disabled");
    public ScheduleResult RunResult { get; set; } = ScheduleResult.Ok("ran");
    public IReadOnlyList<TaskRunRecord> HistoryResult { get; set; } = Array.Empty<TaskRunRecord>();

    /// <summary>Call log, e.g. "add:name:command:folder".</summary>
    public List<string> Calls { get; } = new();

    public ScheduleResult Add(string name, CronExpression cron, string command, string[] arguments, string folder)
    {
        Calls.Add($"add:{name}:{command}:{folder}");
        return AddResult;
    }

    public ScheduleListResult List(string? folder, bool all)
    {
        Calls.Add($"list:{folder ?? "(null)"}:{all}");
        return ListResult;
    }

    public ScheduleResult Remove(string name, string folder) { Calls.Add($"remove:{name}"); return RemoveResult; }
    public ScheduleResult Enable(string name, string folder) { Calls.Add($"enable:{name}"); return EnableResult; }
    public ScheduleResult Disable(string name, string folder) { Calls.Add($"disable:{name}"); return DisableResult; }
    public ScheduleResult Run(string name, string folder) { Calls.Add($"run:{name}"); return RunResult; }
    public IReadOnlyList<TaskRunRecord> GetHistory(string name, string folder) { Calls.Add($"history:{name}"); return HistoryResult; }
}
```

- [ ] **Step 2: Write the failing seam tests**

`tests/Winix.Schedule.Tests/CliRunTests.cs`. NOTE the colour assertions use `((char)27).ToString()` — never a raw ESC literal (suite convention). Assertions marked `// VERIFY` encode expected output of code paths the author read but did not run — if one fails, inspect whether the assertion or the understanding is wrong BEFORE touching production code; report any production-bug suspicion.

```csharp
#nullable enable

using System;
using System.IO;
using System.Text.Json;
using Xunit;
using Yort.ShellKit;

namespace Winix.Schedule.Tests;

/// <summary>
/// End-to-end tests for <see cref="Cli.Run"/> — the full parse→dispatch→format→route path
/// against a <see cref="FakeSchedulerBackend"/>. Wiring-focused per the seam-retrofit design:
/// stream routing, exit codes, colour wiring, one happy + one failure path per subcommand.
/// Formatter internals have their own tests in <see cref="FormattingTests"/>.
/// </summary>
public class CliRunTests
{
    private static readonly string Esc = ((char)27).ToString();

    private static (int Exit, string Stdout, string Stderr) RunCli(FakeSchedulerBackend backend, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Cli.Run(args, stdout, stderr, backend);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    // --- Dispatcher ---

    [Fact]
    public void Run_NoSubcommand_Returns125WithUsageErrorOnStderr()
    {
        var r = RunCli(new FakeSchedulerBackend());
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("missing subcommand", r.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public void Run_UnknownSubcommand_Returns125()
    {
        var r = RunCli(new FakeSchedulerBackend(), "bogus");
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("unknown subcommand 'bogus'", r.Stderr, StringComparison.Ordinal);
    }

    // --- add ---

    [Fact]
    public void Add_Happy_HumanMessageOnStderr_ExitZero()
    {
        var fake = new FakeSchedulerBackend();
        var r = RunCli(fake, "add", "--cron", "0 2 * * *", "--name", "nightly", "--", "dotnet", "build");
        Assert.Equal(0, r.Exit);
        Assert.Contains("created", r.Stderr, StringComparison.Ordinal); // VERIFY: FormatResult includes the result message
        Assert.Equal(string.Empty, r.Stdout);
        Assert.Contains("add:nightly:dotnet:", fake.Calls[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Add_Happy_Json_EnvelopeOnStdout()
    {
        var fake = new FakeSchedulerBackend();
        var r = RunCli(fake, "add", "--cron", "0 2 * * *", "--name", "nightly", "--json", "--", "dotnet", "build");
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stdout);
        Assert.Equal("schedule", doc.RootElement.GetProperty("tool").GetString());
        Assert.Equal("add", doc.RootElement.GetProperty("action").GetString());
        Assert.Equal("success", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(string.Empty, r.Stderr);
    }

    [Fact]
    public void Add_BackendFailure_Returns126()
    {
        var fake = new FakeSchedulerBackend { AddResult = ScheduleResult.Fail("permission denied") };
        var r = RunCli(fake, "add", "--cron", "0 2 * * *", "--name", "x", "--", "cmd");
        Assert.Equal(ExitCode.NotExecutable, r.Exit);
        Assert.Contains("permission denied", r.Stderr, StringComparison.Ordinal); // VERIFY: FormatResult carries failure message
    }

    [Fact]
    public void Add_MissingCron_Returns125()
    {
        var r = RunCli(new FakeSchedulerBackend(), "add", "--", "cmd");
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("--cron is required for add", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_InvalidCron_Returns125()
    {
        var r = RunCli(new FakeSchedulerBackend(), "add", "--cron", "not a cron", "--", "cmd");
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("invalid cron expression", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_MultilineName_Returns125_NeverReachesBackend()
    {
        var fake = new FakeSchedulerBackend();
        var r = RunCli(fake, "add", "--cron", "0 2 * * *", "--name", "evil\ninjected", "--", "cmd");
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("must not contain newline", r.Stderr, StringComparison.Ordinal);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void Add_EmptyCron_Returns125()
    {
        // Shell-expansion of an empty variable is a plausible real input (adversarial-review F5).
        var r = RunCli(new FakeSchedulerBackend(), "add", "--cron", "", "--", "cmd");
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("invalid cron expression", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_EmptyName_ReachesBackend_PinsCurrentBehaviour()
    {
        // PINS CURRENT (pre-refactor) behaviour, verified by code-read 2026-06-06: an empty
        // --name passes RejectIfMultiline and reaches backend.Add(name: ""). In production
        // schtasks then rejects /TN "" with its own (confusing) error → backend failure.
        // The missing empty-name validation is a PRE-EXISTING wart, out of scope for this
        // behaviour-neutral refactor — recorded here so the contract is explicit. If a future
        // change adds validation, this test should be updated deliberately, not silently.
        var fake = new FakeSchedulerBackend();
        var r = RunCli(fake, "add", "--cron", "0 2 * * *", "--name", "", "--", "cmd");
        Assert.Equal(0, r.Exit); // fake reports success; real backend would fail at the OS layer
        Assert.StartsWith("add::", fake.Calls[0], StringComparison.Ordinal); // empty name reached the backend
    }

    // --- list ---

    [Fact]
    public void List_Happy_Json_OnStdout()
    {
        var fake = new FakeSchedulerBackend
        {
            ListResult = ScheduleListResult.Ok(new[] { new ScheduledTask("t1", "0 2 * * *", null, "Ready", "dotnet build", @"\Winix\") })
        };
        var r = RunCli(fake, "list", "--json");
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stdout);
        Assert.Equal("success", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(string.Empty, r.Stderr);
    }

    [Fact]
    public void List_Unavailable_Returns126_ReasonOnStderr()
    {
        var fake = new FakeSchedulerBackend { ListResult = ScheduleListResult.Unavailable("service stopped") };
        var r = RunCli(fake, "list");
        Assert.Equal(ExitCode.NotExecutable, r.Exit);
        Assert.Contains("service stopped", r.Stderr, StringComparison.Ordinal);
    }

    // --- remove / enable / disable / run (action subcommands share WriteActionResult) ---

    [Theory]
    [InlineData("remove")]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("run")]
    public void ActionSubcommand_Happy_ExitZero_BackendCalled(string sub)
    {
        var fake = new FakeSchedulerBackend();
        var r = RunCli(fake, sub, "mytask");
        Assert.Equal(0, r.Exit);
        Assert.Single(fake.Calls);
        Assert.StartsWith($"{sub}:mytask", fake.Calls[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("run")]
    public void ActionSubcommand_MissingName_Returns125(string sub)
    {
        var r = RunCli(new FakeSchedulerBackend(), sub);
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains($"missing task name for {sub}", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_BackendFailure_Returns126_JsonErrorEnvelopeOnStdout()
    {
        var fake = new FakeSchedulerBackend { RemoveResult = ScheduleResult.Fail("task not found") };
        var r = RunCli(fake, "remove", "ghost", "--json");
        Assert.Equal(ExitCode.NotExecutable, r.Exit);
        using var doc = JsonDocument.Parse(r.Stdout);
        Assert.Equal("error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    // --- history ---

    [Fact]
    public void History_WithRecords_HumanTableOnStderr()
    {
        var fake = new FakeSchedulerBackend
        {
            HistoryResult = new[] { new TaskRunRecord(DateTimeOffset.Parse("2026-06-01T02:00:00+00:00"), 0, TimeSpan.FromSeconds(3)) }
        };
        var r = RunCli(fake, "history", "mytask");
        Assert.Equal(0, r.Exit);
        Assert.NotEqual(string.Empty, r.Stderr);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public void History_Json_OnStdout()
    {
        var fake = new FakeSchedulerBackend();
        var r = RunCli(fake, "history", "mytask", "--json");
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stdout);
        Assert.Equal("success", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    // --- next (backend-less) ---

    [Fact]
    public void Next_Happy_HumanOnStderr_NoBackendCalls()
    {
        var fake = new FakeSchedulerBackend();
        var r = RunCli(fake, "next", "0 2 * * *");
        Assert.Equal(0, r.Exit);
        Assert.NotEqual(string.Empty, r.Stderr);
        Assert.Equal(string.Empty, r.Stdout);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void Next_Json_OnStdout()
    {
        var r = RunCli(new FakeSchedulerBackend(), "next", "0 2 * * *", "--json", "--count", "3");
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stdout);
        Assert.Equal("success", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public void Next_InvalidCount_Returns125()
    {
        var r = RunCli(new FakeSchedulerBackend(), "next", "0 2 * * *", "--count", "zero");
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("invalid --count value", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Next_UnsatisfiableCron_Returns125()
    {
        // Parseable but logically impossible (Feb 30) — the RunNext InvalidOperationException
        // catch (8-year search horizon) must surface as a usage error, not a stack trace.
        var r = RunCli(new FakeSchedulerBackend(), "next", "0 0 30 2 *");
        Assert.Equal(ExitCode.UsageError, r.Exit);
    }

    // --- colour wiring through the seam ---

    [Fact]
    public void ColorAlways_AddHappy_EmitsAnsiOnStderr()
    {
        var r = RunCli(new FakeSchedulerBackend(), "add", "--cron", "0 2 * * *", "--name", "x", "--color=always", "--", "cmd");
        Assert.Equal(0, r.Exit);
        Assert.Contains(Esc, r.Stderr, StringComparison.Ordinal); // VERIFY: FormatResult colours when useColor=true
    }

    [Fact]
    public void ColorNever_AddHappy_NoAnsiAnywhere()
    {
        var r = RunCli(new FakeSchedulerBackend(), "add", "--cron", "0 2 * * *", "--name", "x", "--color=never", "--", "cmd");
        Assert.Equal(0, r.Exit);
        Assert.DoesNotContain(Esc, r.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(Esc, r.Stdout, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 3: Run the new tests**

Run: `dotnet test /d/projects/winix/tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj --nologo -v quiet --filter "FullyQualifiedName~CliRunTests"`
Expected: all pass. For any failing `// VERIFY` assertion: read the relevant formatter, adjust the ASSERTION if the author's expectation was wrong (e.g. exact message text), and STOP + report if the failure suggests a production defect instead.

- [ ] **Step 4: Full schedule suite**

Run: `dotnet test /d/projects/winix/tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj --nologo -v quiet`
Expected: Task 0 count + ~20 new, 0 failures.

- [ ] **Step 5: Commit**

```bash
git -C /d/projects/winix add tests/Winix.Schedule.Tests/FakeSchedulerBackend.cs tests/Winix.Schedule.Tests/CliRunTests.cs
git -C /d/projects/winix commit -m "test(schedule): Cli.Run seam tests — stream routing, exit codes, colour wiring, per-subcommand paths via FakeSchedulerBackend"
```

---

### Task 4: schedule — byte-stability verification

- [ ] **Step 1: Rebuild and capture post-refactor help/describe (streams separated)**

```bash
dotnet build /d/projects/winix/src/schedule/schedule.csproj --nologo -v quiet
/d/projects/winix/src/schedule/bin/Debug/net10.0/schedule.exe --help > /d/projects/winix/tmp/seam-baseline/schedule-help-after.out 2> /d/projects/winix/tmp/seam-baseline/schedule-help-after.err
/d/projects/winix/src/schedule/bin/Debug/net10.0/schedule.exe --describe > /d/projects/winix/tmp/seam-baseline/schedule-describe-after.out 2> /d/projects/winix/tmp/seam-baseline/schedule-describe-after.err
```

- [ ] **Step 2: Diff against Task 0 baselines — each stream independently**

```bash
diff /d/projects/winix/tmp/seam-baseline/schedule-help.out /d/projects/winix/tmp/seam-baseline/schedule-help-after.out
diff /d/projects/winix/tmp/seam-baseline/schedule-help.err /d/projects/winix/tmp/seam-baseline/schedule-help-after.err
diff /d/projects/winix/tmp/seam-baseline/schedule-describe.out /d/projects/winix/tmp/seam-baseline/schedule-describe-after.out
diff /d/projects/winix/tmp/seam-baseline/schedule-describe.err /d/projects/winix/tmp/seam-baseline/schedule-describe-after.err
```
Expected: **zero diff on all four.** A line that moves between the `.out` and `.err` files is a stream-ROUTING regression (adversarial-review F1) — STOP and fix before proceeding. Content drift = parser-chain drift — also STOP.

- [ ] **Step 3: Quick manual smoke of the rebuilt binary**

```bash
/d/projects/winix/src/schedule/bin/Debug/net10.0/schedule.exe next "0 2 * * *"
/d/projects/winix/src/schedule/bin/Debug/net10.0/schedule.exe next "0 2 * * *" --json
/d/projects/winix/src/schedule/bin/Debug/net10.0/schedule.exe bogus
```
Expected: next → 5 fire times (human, stderr); --json → JSON envelope on stdout; bogus → usage error, exit 125 (`echo $?`). No commit needed (verification only).

---

### Task 5: retry — create `Cli.cs`, thin `Program.cs`

**Files:**
- Create: `src/Winix.Retry/Cli.cs`
- Rewrite: `src/retry/Program.cs` (483 lines → ~35)

Read the whole of `src/retry/Program.cs` first. Same move discipline as Task 2.

- [ ] **Step 1: Create `src/Winix.Retry/Cli.cs`**

Skeleton:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Yort.ShellKit;

namespace Winix.Retry;

/// <summary>
/// Library entry point for the retry tool: parses arguments, builds <see cref="RetryOptions"/>,
/// runs the retry loop with a real process spawner, and routes summary output through the
/// supplied writers. <c>Program.Main</c> is a thin shell that owns Ctrl+C handling (process-global
/// <c>Console.CancelKeyPress</c> state) and passes a <see cref="CancellationToken"/> in.
/// </summary>
/// <remarks>
/// Seam limit (by design — do not "fix"): the child process inherits the REAL console handles
/// (<c>RedirectStandardOutput/Error/Input = false</c>) so its output passes through unmodified.
/// Tests through this seam therefore cannot observe child passthrough; that contract is covered
/// by ProgramMainTests (process-spawn) and the native smoke fixtures.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the retry CLI.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <param name="stdout">Receives the summary only when <c>--stdout</c> is set; child stdout
    /// passes through the real console handles, never through this writer.</param>
    /// <param name="stderr">Receives progress lines, the final summary (default), and all errors.
    /// Errors always go here even under <c>--stdout</c> — pipe consumers expect stdout to be
    /// clean on failure.</param>
    /// <param name="cancellationToken">Cancellation signal (Ctrl+C in production, owned by Main).
    /// Cancels the wait AND kills the running child (entire tree).</param>
    /// <returns>Child exit code passed through, or 125/126/127 for retry's own errors.</returns>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        string version = GetVersion();
        // … parser chain verbatim from Program.cs (the full .Description()….JsonField() chain) …
        // … parse + validation blocks verbatim, with transformations per the mapping table …
        // … then:
        //   try { return RunWithRetry(command, commandArgs, options, version, jsonOutput,
        //                             useColor, summaryWriter, stderr, cancellationToken); }
        //   catch (Exception ex) when (…) { … SafeWriteLine(stderr, msg); return ExitCode.NotExecutable; }
        //   (the catch-all moves here from Main, verbatim including its comment)
    }

    // … RunWithRetry / SafeWriteLine / UnwrapTypeInit / GetVersion / ParseCodeList moved
    //   per the mapping table …
}
```

**Transformation mapping table:**

| Old (Program.cs) | New (Cli.cs) |
|---|---|
| `result.WriteErrors(Console.Error)` / `result.WriteError(..., Console.Error)` | `stderr` |
| `TextWriter summaryWriter = useStdout ? Console.Out : Console.Error;` | `TextWriter summaryWriter = useStdout ? stdout : stderr;` (comment moves verbatim) |
| CTS creation + `CancelKeyPress` register/unregister + its comment block | STAYS IN MAIN — do not move |
| `try { return RunWithRetry(...) } catch (…safety net…)` | moves into `Run` (the `finally { Console.CancelKeyPress -= … }` stays in Main) |
| `RunWithRetry(command, commandArgs, options, version, jsonOutput, useColor, summaryWriter, cts)` | `RunWithRetry(command, commandArgs, options, version, jsonOutput, useColor, summaryWriter, stderr, cancellationToken)` |
| `private static int RunWithRetry(…, CancellationTokenSource cts)` | `private static int RunWithRetry(…, TextWriter summaryWriter, TextWriter stderr, CancellationToken cancellationToken)` |
| `runner.Run(command, commandArgs, options, onAttempt, cancellationToken: cts.Token)` | `…, cancellationToken: cancellationToken)` |
| `SafeWriteLine(Console.Error, …)` — 4 sites: 2 kill-warnings + orphan warning inside the runProcess delegate, 1 launch-failure plain-text error | `SafeWriteLine(stderr, …)` |
| `SafeWriteLine(TextWriter, string)` helper | moves verbatim (already writer-parameterised) |
| `UnwrapTypeInit`, `ParseCodeList`, `GetVersion` | move verbatim (`GetVersion` already anchors on `typeof(RetryResult).Assembly`) |

Every comment inside the runProcess delegate (kill-registration disposal ordering, `WaitForExitAsync` rationale, grace-window, orphan warning, exit 137) moves **verbatim**.

- [ ] **Step 2: Rewrite `src/retry/Program.cs` as the thin shell**

Complete replacement content:

```csharp
using System;
using System.Threading;
using Winix.Retry;
using Yort.ShellKit;

namespace Retry;

internal sealed class Program
{
    /// <summary>
    /// Entry point. Owns process-global state only: console setup and Ctrl+C handling.
    /// All parsing, validation, and the retry loop live in <see cref="Cli.Run"/>.
    /// </summary>
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Ctrl+C stays in Main: Console.CancelKeyPress is a process-global static event that
        // doesn't compose with xunit parallelism, and CTS disposal is entry-point lifetime
        // management. The CancelKeyPress handler must be named + unregistered in finally.
        // A captured-by-closure anonymous handler keeps a reference to the CTS past the
        // using-scope exit; a second Ctrl+C during AOT teardown would call Cancel() on a
        // disposed CTS and ObjectDisposedException would escape as a shutdown crash.
        // Reference fix: src/wargs/Program.cs:172-185.
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

- [ ] **Step 3: Build**

Run: `dotnet build /d/projects/winix/src/retry/retry.csproj --nologo -v quiet`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 4: Existing tests pass UNMODIFIED (one authorised comment fixup)**

Run: `dotnet test /d/projects/winix/tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj --nologo -v quiet`
Expected: 108 passed, 0 failures.

**Authorised comment-only edit:** `tests/Winix.Retry.Tests/ProgramMainTests.cs` line ~19 references the spawner closure at `Program.cs:262-350`, which has moved. Update the reference to `Winix.Retry/Cli.cs` (the closure inside `RunWithRetry`) — comment text only, zero assertion changes. Also its seam-extraction bullet ("Extract the spawner to a testable seam") remains a deliberate non-goal per ADR D4 — append one sentence: `The 2026-06-06 Cli.Run seam retrofit deliberately did NOT add a spawner fake (ADR D4 — seam-blindness); this gap remains open.`

- [ ] **Step 5: Commit**

```bash
git -C /d/projects/winix add src/Winix.Retry/Cli.cs src/retry/Program.cs tests/Winix.Retry.Tests/ProgramMainTests.cs
git -C /d/projects/winix commit -m "refactor(retry): extract Cli.Run library seam — Main keeps only Ctrl+C/CTS ownership

Behaviour-neutral move per docs/plans/2026-06-06-cli-seam-retrofit-design.md.
RunWithRetry now takes CancellationToken (only ever used cts.Token — verified).
ProgramMainTests: comment-only fixup of the moved spawner-closure file reference."
```

---

### Task 6: retry — seam tests (`CliRunTests`)

**Files:**
- Create: `tests/Winix.Retry.Tests/CliRunTests.cs`

- [ ] **Step 1: Write the failing seam tests**

Real-process discipline per ADR D4: no spawner fake. Failure paths use a nonexistent command (deterministic — never spawns); happy paths use a trivial real child. On Windows use `cmd.exe` BY FULL NAME (`cmd.exe`, not `cmd` — and never `echo`: Git-for-Windows puts a Cygwin `echo.exe` on PATH that breaks under inherited stdio).

```csharp
#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Xunit;
using Yort.ShellKit;

namespace Winix.Retry.Tests;

/// <summary>
/// End-to-end tests for <see cref="Cli.Run"/> — full parse→validate→retry-loop→summary-routing
/// path. Per ADR D4 (seam-retrofit design) there is NO spawner fake: failure paths use a
/// nonexistent command (fully deterministic — the spawn never succeeds), happy paths spawn a
/// trivial real child. Child-passthrough and Ctrl+C remain covered by ProgramMainTests + smokes.
/// </summary>
public class CliRunTests
{
    private static readonly string Esc = ((char)27).ToString();
    private const string NoSuchCommand = "winix-test-no-such-command-zz9";

    private static (int Exit, string Stdout, string Stderr) RunCli(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Cli.Run(args, stdout, stderr, CancellationToken.None);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Platform-conditional argv for a child that exits with <paramref name="code"/> quickly.</summary>
    private static string[] ExitWith(int code) =>
        OperatingSystem.IsWindows()
            ? new[] { "cmd.exe", "/c", $"exit {code}" }
            : new[] { "/bin/sh", "-c", $"exit {code}" };

    private static string[] Concat(string[] head, string[] tail)
    {
        var all = new string[head.Length + tail.Length];
        head.CopyTo(all, 0);
        tail.CopyTo(all, head.Length);
        return all;
    }

    // --- Usage / validation errors (no child ever spawned) ---

    [Fact]
    public void NoCommand_Returns125()
    {
        var r = RunCli();
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("no command specified", r.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Theory]
    [InlineData("--times", "abc")]
    [InlineData("--times", "-1")]
    [InlineData("--delay", "fortnight")]
    [InlineData("--backoff", "sideways")]
    [InlineData("--on", "1,x")]
    [InlineData("--until", "")]
    public void InvalidOptionValue_Returns125_ErrorOnStderr(string flag, string value)
    {
        var r = RunCli(flag, value, NoSuchCommand);
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains($"invalid {flag} value", r.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public void OnAndUntilCombined_Returns125()
    {
        var r = RunCli("--on", "1", "--until", "0", NoSuchCommand);
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("cannot be combined", r.Stderr, StringComparison.Ordinal);
    }

    // --- Launch failure (deterministic, spawn never succeeds) ---

    [Fact]
    public void CommandNotFound_Plain_Returns127_ErrorOnStderr()
    {
        var r = RunCli(NoSuchCommand);
        Assert.Equal(ExitCode.NotFound, r.Exit);
        Assert.Contains(NoSuchCommand, r.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public void CommandNotFound_Json_EnvelopeOnStderrByDefault()
    {
        var r = RunCli("--json", NoSuchCommand);
        Assert.Equal(ExitCode.NotFound, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("launch_failed", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("child_exit_code").ValueKind);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public void CommandNotFound_JsonWithStdoutFlag_EnvelopeOnStdout()
    {
        var r = RunCli("--json", "--stdout", NoSuchCommand);
        Assert.Equal(ExitCode.NotFound, r.Exit);
        using var doc = JsonDocument.Parse(r.Stdout);
        Assert.Equal("launch_failed", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    // --- Real-child paths ---

    [Fact]
    public void ChildSucceeds_ExitZero_JsonReportsSingleAttempt()
    {
        var r = RunCli(Concat(new[] { "--json" }, ExitWith(0)));
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("succeeded", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("attempts").GetInt32());
    }

    [Fact]
    public void RetriesExhausted_PassesThroughChildExitCode()
    {
        var r = RunCli(Concat(new[] { "--times", "1", "--delay", "1ms" }, ExitWith(7)));
        Assert.Equal(7, r.Exit);
        Assert.Contains("no retries remaining", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void UntilMatch_PassesThroughChildExitCode_NotZero()
    {
        // Pins the empirically-verified contract (probe 2026-06-06): --until match passes the
        // child code through; only exit_reason says "succeeded". The man page previously
        // claimed exit 0 here — fixed in Task 1 of this plan.
        var r = RunCli(Concat(new[] { "--until", "7", "--times", "0" }, ExitWith(7)));
        Assert.Equal(7, r.Exit);
        Assert.Contains("matched target", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void StdoutFlag_ProgressLinesRouteToStdout_ChildFailure()
    {
        var r = RunCli(Concat(new[] { "--stdout", "--times", "0" }, ExitWith(7)));
        Assert.Equal(7, r.Exit);
        Assert.Contains("no retries remaining", r.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("no retries remaining", r.Stderr, StringComparison.Ordinal);
    }

    // --- Colour wiring ---

    [Fact]
    public void ColorAlways_ProgressLineCarriesAnsi()
    {
        var r = RunCli(Concat(new[] { "--color=always", "--times", "0" }, ExitWith(7)));
        Assert.Contains(Esc, r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ColorNever_NoAnsiAnywhere()
    {
        var r = RunCli(Concat(new[] { "--color=never", "--times", "0" }, ExitWith(7)));
        Assert.DoesNotContain(Esc, r.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(Esc, r.Stdout, StringComparison.Ordinal);
    }

    // --- Empty command token → top-level catch-all (adversarial-review F4 + F5) ---

    [Fact]
    public void EmptyCommandToken_HitsCatchAll_Returns126()
    {
        // Probed against the pre-refactor binary 2026-06-06: `retry ""` →
        //   stderr: "retry: unexpected error: InvalidOperationException: FileNameMissing"
        //   exit:   126
        // ProcessStartInfo.FileName="" throws InvalidOperationException, which is NOT a typed
        // launch failure (CommandNotFound/NotExecutable), so it escapes RunWithRetry into the
        // top-level catch-all. This is the only deterministic catch-all trigger reachable
        // through the public seam — it pins both the exit code and the error-on-stderr contract
        // after the catch-all's move into Cli.Run. (The bare "FileNameMissing" resource key is
        // the accepted broad-catch minimum — type name present — so the assertion stays loose.)
        var r = RunCli("");
        Assert.Equal(ExitCode.NotExecutable, r.Exit);
        Assert.Contains("unexpected error", r.Stderr, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", r.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stdout);
    }

    // --- Cancellation ---

    /// <summary>Platform-conditional argv for a child that sleeps several seconds.</summary>
    private static string[] SleepChild() =>
        OperatingSystem.IsWindows()
            ? new[] { "cmd.exe", "/c", "ping -n 10 127.0.0.1 > NUL" }
            : new[] { "/bin/sh", "-c", "sleep 10" };

    [Fact]
    public void PreCancelledToken_KillsFirstAttempt_ReportsCancelled()
    {
        // Contract pinned by code-read 2026-06-06: RetryRunner "always runs at least once" —
        // a pre-cancelled token still spawns attempt 1; the kill registration fires at
        // Register time (token already signalled), the child is killed, and the outcome is
        // labelled cancelled. A LONG child is load-bearing here: with a fast child (exit 0)
        // the child can finish BEFORE the kill and the run exits 0 — a real race, not a
        // hypothetical. The sleep child guarantees the kill wins.
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int exit = Cli.Run(Concat(new[] { "--json" }, SleepChild()), stdout, stderr, cts.Token);
        using var doc = JsonDocument.Parse(stderr.ToString());
        Assert.Equal("cancelled", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.NotEqual(0, exit); // kill exit code is platform-dependent (-1 Windows, 137-class Unix) — assert nonzero only
    }

    [Fact]
    public void MidWaitCancel_KillsChild_ReturnsPromptly()
    {
        // Adversarial-review F2: exercises the kill-registration → WaitForExitAsync-cancel →
        // grace-window path that was previously untestable (needed real Ctrl+C; the seam's
        // CancellationToken makes it drivable). The child sleeps ~10s; cancel fires at 300ms;
        // a working kill path returns in well under the sleep duration. If this test takes
        // ~10s, the cancel→kill chain is broken — that slowness IS the failure signal.
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(300);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int exit = Cli.Run(Concat(new[] { "--json" }, SleepChild()), stdout, stderr, cts.Token);
        sw.Stop();
        using var doc = JsonDocument.Parse(stderr.ToString());
        Assert.Equal("cancelled", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.NotEqual(0, exit);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8),
            $"cancel→kill took {sw.Elapsed} — child was not killed promptly");
    }
}
```

- [ ] **Step 2: Run the new tests**

Run: `dotnet test /d/projects/winix/tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj --nologo -v quiet --filter "FullyQualifiedName~CliRunTests"`
Expected: all pass. The cancellation contracts were pinned at planning time (code-read + probes 2026-06-06) — if a cancellation test fails, treat it as a real signal: STOP and report; do not soften the assertion. Any failure on a `// VERIFY` line: fix the assertion if the expectation was wrong; STOP and report if production behaviour looks defective.

- [ ] **Step 3: Full retry suite**

Run: `dotnet test /d/projects/winix/tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj --nologo -v quiet`
Expected: 108 + ~15 new, 0 failures.

- [ ] **Step 4: Commit**

```bash
git -C /d/projects/winix add tests/Winix.Retry.Tests/CliRunTests.cs
git -C /d/projects/winix commit -m "test(retry): Cli.Run seam tests — validation/launch-failure/routing/colour via real processes (no spawner fake per ADR D4)"
```

---

### Task 7: retry — byte-stability verification

- [ ] **Step 1: Rebuild + capture + diff (streams separated — adversarial-review F1)**

```bash
dotnet build /d/projects/winix/src/retry/retry.csproj --nologo -v quiet
/d/projects/winix/src/retry/bin/Debug/net10.0/retry.exe --help > /d/projects/winix/tmp/seam-baseline/retry-help-after.out 2> /d/projects/winix/tmp/seam-baseline/retry-help-after.err
/d/projects/winix/src/retry/bin/Debug/net10.0/retry.exe --describe > /d/projects/winix/tmp/seam-baseline/retry-describe-after.out 2> /d/projects/winix/tmp/seam-baseline/retry-describe-after.err
diff /d/projects/winix/tmp/seam-baseline/retry-help.out /d/projects/winix/tmp/seam-baseline/retry-help-after.out
diff /d/projects/winix/tmp/seam-baseline/retry-help.err /d/projects/winix/tmp/seam-baseline/retry-help-after.err
diff /d/projects/winix/tmp/seam-baseline/retry-describe.out /d/projects/winix/tmp/seam-baseline/retry-describe-after.out
diff /d/projects/winix/tmp/seam-baseline/retry-describe.err /d/projects/winix/tmp/seam-baseline/retry-describe-after.err
```
Expected: zero diff on all four. A line moving between `.out` and `.err` = stream-routing regression — STOP and fix.

- [ ] **Step 2: Quick manual smoke**

```bash
/d/projects/winix/src/retry/bin/Debug/net10.0/retry.exe --times 1 --delay 100ms winix-no-such-cmd; echo $?
```
Expected: "command not found"-class error on stderr, exit 127. (Run as a single `bash -c` if the chained form trips the compound-command guard.)

---

### Task 8: Whole-feature wrap-up

**Files:**
- Modify: `CLAUDE.md` (two project-layout lines)

- [ ] **Step 1: Full solution test run**

Run: `dotnet test /d/projects/winix/Winix.sln --nologo -v quiet`
Expected: 0 failures across all projects. (Known flake: a single Winix.Trash.Tests recycle-bin failure has appeared once before and was not reproducible — if it appears, re-run that project in isolation before treating it as real.)

- [ ] **Step 2: Update CLAUDE.md layout lines**

In `CLAUDE.md`, change:
```
src/Winix.Schedule/        — class library (cron parser, schtasks/crontab backends, formatting)
```
to:
```
src/Winix.Schedule/        — class library (cron parser, schtasks/crontab backends, formatting, Cli.Run seam)
```
and:
```
src/Winix.Retry/           — class library (retry loop, backoff, formatting)
```
to:
```
src/Winix.Retry/           — class library (retry loop, backoff, formatting, Cli.Run seam)
```
(Exact current text may differ slightly — match what's there, append the seam mention.)

- [ ] **Step 3: Commit**

```bash
git -C /d/projects/winix add CLAUDE.md
git -C /d/projects/winix commit -m "docs: CLAUDE.md layout — schedule + retry libraries now carry the Cli.Run seam"
```

- [ ] **Step 4: Push branch + dispatch 3-OS CI**

```bash
git -C /d/projects/winix push -u origin feature/cli-seam-retrofit
gh workflow run ci.yml --repo Yortw/winix --ref feature/cli-seam-retrofit
```
Expected: workflow dispatched (feature branches don't auto-trigger CI). Verify result with `gh run list --repo Yortw/winix --branch feature/cli-seam-retrofit --limit 1`.

---

### Task 9: Main-session verification gates (NOT for subagents)

These run in the orchestrating session after Tasks 0–8 complete:

- [ ] WSL run: `wsl bash -lc "dotnet test /mnt/d/projects/winix/tests/Winix.Schedule.Tests --nologo -v quiet"` and same for `Winix.Retry.Tests` — local Linux reproduction ahead of CI. The two cancellation tests (`PreCancelledToken…`, `MidWaitCancel…`) are the ones most worth watching on Linux — kill semantics differ (SIGKILL vs TerminateProcess).
- [ ] Re-run existing smoke fixtures against REPUBLISHED binaries (stale-binary trap): `artifacts/round-stop-2026-05-09/schedule/run-smokes.sh` (if present — locate with `ls artifacts/*/schedule/run-smokes.sh artifacts/*/retry/run-smokes.sh`) after copying freshly published binaries into the fixture's expected location.
- [ ] **Name the deferred-coverage owners (adversarial-review F6):** while running the fixtures, record in the wrap-up notes WHICH smoke case covers (a) retry child-stdout passthrough and (b) real-Ctrl+C cancellation, citing fixture file + case ID. If no case covers one of them, add a one-line known-gap note to the ADR's D4 trade-offs instead of leaving the deferral pointing at "smokes" generically. (Mid-wait token-cancel is now seam-tested — `MidWaitCancel_KillsChild_ReturnsPromptly` — so the residual smoke-only surface is real-signal delivery, i.e. Ctrl+C → CancelKeyPress → CTS, which lives in Main.)
- [ ] Manual Windows smoke per the refactored-tools rule (both tools): help/version/describe, happy path, usage error, `--json` routing, `NO_COLOR`.
- [ ] 3-OS CI green on the feature branch.
- [ ] Review rounds (code-reviewer + spec-compliance per cluster already happen inside subagent-driven-development; whole-feature fresh-eyes review at the end).
- [ ] Merge `--no-ff` into `release/v0.4.0`; update memory (`project_cli_seam_retrofit_backlog.md` 5 → 3 remaining, progress notes).

---

## Adversarial-review integration record (pass 1, 2026-06-06)

Fresh-subagent review (plan + design + ADR only): 1 blocker, 5 test gaps, 2 defers. Dispositions:

| Finding | Disposition |
|---|---|
| F1 (blocker) — `2>&1` merged capture blind to stream-routing drift | **Accepted.** Tasks 0/4/7 now capture and diff stdout/stderr separately with an explicit routing-regression STOP. |
| F2 (test gap) — kill-on-cancel path untested | **Accepted, upgraded.** The seam makes mid-wait cancel drivable for the first time (token instead of real Ctrl+C) — added `MidWaitCancel_KillsChild_ReturnsPromptly`. This partially closes the long-standing documented gap in ProgramMainTests' header. |
| F3 (test gap) — PreCancelledToken asserted an assumed contract | **Accepted.** Contract pinned at planning time by code-read (RetryRunner "always runs at least once" → attempt 1 spawns, kill fires at Register) + race analysis (fast child can beat the kill → MUST use a long sleep child). Test rewritten with the pinned contract; executor instructed to treat failure as a real signal, not soften. |
| F4 (test gap) — catch-all/UnwrapTypeInit path untested | **Accepted, resolved by probe.** `retry ""` → InvalidOperationException (FileNameMissing) escapes typed launch-failure handling into the catch-all → 126 + "unexpected error" on stderr (probed against the pre-refactor binary). Added `EmptyCommandToken_HitsCatchAll_Returns126` — a deterministic catch-all trigger through the public seam. |
| F5 (test gap) — empty-string inputs | **Accepted with one correction.** `add --cron ""` → 125 added as suggested. `--name ""` does NOT 125 today — code-read shows it reaches the backend (pre-existing wart; schtasks rejects /TN "" at the OS layer). Behaviour-neutrality forbids "fixing" it here; added `Add_EmptyName_ReachesBackend_PinsCurrentBehaviour` pinning the current contract with the wart documented. Retry's empty-token case is covered by the F4 test. |
| F6 (defer) — deferred coverage points at "smokes" generically | **Accepted.** Task 9 now requires naming the fixture+case for child passthrough and real-Ctrl+C, or recording a known-gap line in ADR D4. |
| F7 (defer) — schedule no-catch-all asymmetry needs an executor-visible guardrail | **Accepted.** "Do NOT add a top-level try/catch" rule added to Task 2's preamble. |

Convergence: all findings integrated without structural plan change → no second pass required (the review's two defers are documentation tasks, not contested findings).

## Self-review record (plan author, 2026-06-06)

- **Spec coverage:** convention codified in moved-code comments + XML docs (D1/D2); schedule optional backend (D3) Task 2; no spawner fake (D4) Task 6 header; colour-in-seam (D5) colour tests; behaviour-neutrality (D6) Tasks 0/2.4/4/5.4/7. Man-page defect found during planning → Task 1.
- **Placeholder scan:** the `…` markers in Cli.cs skeletons are deliberate move instructions backed by exact transformation tables, not TBDs; `// VERIFY` assertions are explicitly marked observation points per the plan-test-assumption rule.
- **Type consistency:** `Cli.Run` signatures match design doc; `FakeSchedulerBackend` matches `ISchedulerBackend` (verified against source); `ScheduleResult.Ok/Fail`, `ScheduleListResult.Ok/Unavailable`, `ScheduledTask` ctor, `TaskRunRecord` ctor all verified against source files; `ExitCode` constants verified (125/126/127 const ints).
- **Known-risk notes for executors:** schedule `--` separator in `add` tests follows the documented invocation shape (`schedule add --cron "…" -- cmd args`); `cmd.exe /c exit N` via `ArgumentList` is MSYS-free (the conversion trap only applies to Git-Bash-launched commands, not `ProcessStartInfo`).
