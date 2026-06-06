# wargs Cli.RunAsync Seam Retrofit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Retrofit a `Cli.RunAsync` library seam to wargs (parse + validation + full pipeline orchestration, including the envelope-on-every-exit-path catches), strictly behaviour-neutral; then add the previously-impossible tests as a separate stage; then the fixture cancellation case.

**Architecture:** Move `src/wargs/Program.cs` orchestration into `src/Winix.Wargs/Cli.cs` per `docs/plans/2026-06-06-wargs-cli-seam-design.md` + ADR (W1–W5) and the inherited phase-1/2 convention. Main keeps `ConsoleEnv`, the CTS/`CancelKeyPress` handler, and the **real-console** `Console.In.Close()`-on-cancel registration (W2).

**Tech Stack:** .NET 10, C#, xUnit, Yort.ShellKit.

**Branch:** `feature/cli-seam-wargs` (created; design + ADR committed as `4d031f7`).

**Hard rules for executors:**
- **Existing tests pass UNMODIFIED.** Pre-refactor: **167 passed + 7 skipped = 174** in `Winix.Wargs.Tests`. Sole authorised exception: comment-only `Program.cs` location-reference fixups in `tests/Winix.Wargs.Tests/ProgramMainTests.cs` (~8 sites at lines ~512/586/594/602/618/629/647/651) — wording only, zero assertion changes, noted in the commit message. The Linux `SkippableFact` Ctrl+C binary tests are untouched. Anything else = STOP, BLOCKED.
- **No blocking-on-async** (`.Result`/`.Wait()`/`.GetAwaiter().GetResult()`) anywhere in the seam, Main, or tests (phase-2 rule).
- The `cts.Token.Register(() => Console.In.Close())` block STAYS IN MAIN verbatim with its full round-7/8 comment (ADR W2) — the library must never close the real console stdin; tests inject `StringReader`s.
- Preserve every moved comment VERBATIM (round-4 through round-16 findings); only file/location references inside comments may be updated.
- `Winix.Wargs` already references Yort.ShellKit and grants `InternalsVisibleTo("Winix.Wargs.Tests")`; `ExceptionUnwrap` is already library-resident — no csproj changes, nothing to relocate there.
- Full braces; XML docs on public members; Bash tool blocks `&&`/`;` (separate calls or one `bash -c '…'`); commit per task; no Co-Authored-By.
- **Probed contracts (2026-06-06, Linux binary) — pinned:**
  - SIGINT mid-job under `--json` → stderr envelope exactly `{"tool":"wargs","version":"…","exit_code":130,"exit_reason":"cancelled"}`, wargs exits ~40ms after the signal, nothing lingers (the fixture's exit file records GNU timeout's own 124).
  - **Spawn-failure shape (adversarial-review F4 — the documented `-1` was WRONG without a flag):** with shell fallback active (the default), a nonexistent command is run via `sh -c`/`cmd /c` and `child_exit_code` is the SHELL's code (127 Linux / 9009-class Windows), with the child's own stderr leaking through. Only `--no-shell-fallback` produces a true spawn failure: probed → `child_exit_code:-1` + `fault_message:"failed to spawn '…'"`. **Every deterministic-failure test in this plan therefore uses `--no-shell-fallback`.**
- **W4 sequencing (user requirement):** Task 2 contains ONLY the wiring/regression tests; the newly-unlocked tests are Task 4, AFTER Task 3's neutrality gates pass. Do not merge the two groups.

---

### Task -1: Doc clarification — connect shell fallback to `child_exit_code` (found during planning)

**Context:** The `--describe` field says `child_exit_code: … -1 on spawn failure` — TRUE, but disconnected from the shell-fallback docs: with the DEFAULT fallback, a not-found command is retried via the shell, which spawns fine and exits 127 (sh) / 9009 (cmd) — so users (and this plan's first draft) reasonably but wrongly expect `-1` for a typo'd command. Probed 2026-06-06. This task runs BEFORE Task 0 because it changes `--describe` output (baselines must capture the post-fix text).

**Files:**
- Modify: `src/wargs/Program.cs` (the `child_exit_code` JsonField — it moves to Cli.cs verbatim in Task 1 afterwards)
- Modify: `src/wargs/README.md` (exit-code 123 row)
- Modify: `docs/ai/wargs.md` (the shell-fallback paragraph)

- [ ] **Step 1:** In `src/wargs/Program.cs`, change the `child_exit_code` JsonField description from:
`"(--ndjson per-job) Child process exit code; -1 on spawn failure"`
to:
`"(--ndjson per-job) Child process exit code. -1 only on a TRUE spawn failure (with --no-shell-fallback, or when the fallback shell itself cannot start) — under the default shell fallback a not-found command is retried via sh -c / cmd /c, so the SHELL's exit code (127 / 9009) appears here instead, with no fault_message."`
(Match the exact current text when editing — paraphrase above; the field text is one string in the `.JsonField("child_exit_code", …)` call.)

- [ ] **Step 2:** In `src/wargs/README.md`, extend the 123 exit-code row's note with one sentence: `With the default shell fallback, a not-found command reports the fallback shell's exit code (127 sh / 9009 cmd) rather than a spawn failure; -1/fault_message appear only with --no-shell-fallback or when the shell itself cannot start.`

- [ ] **Step 3:** In `docs/ai/wargs.md`, append to the shell-fallback paragraph: `Consequence for exit codes: a typo'd command under the default fallback yields the shell's 127/9009 as child_exit_code, not a spawn failure; -1 + fault_message require --no-shell-fallback.`

- [ ] **Step 4:** Build wargs, run the existing test suite (must stay 167/7/174 — describe-text changes don't affect tests unless one pins this string; if one does, STOP and report). Commit:

```bash
git -C /d/projects/winix add src/wargs/Program.cs src/wargs/README.md docs/ai/wargs.md
git -C /d/projects/winix commit -m "docs(wargs): connect shell-fallback semantics to child_exit_code — -1 requires a TRUE spawn failure; default fallback surfaces the shell's 127/9009 (probed)"
```

---

### Task 0: Baseline capture (stream-separated) + test count — AFTER Task -1

- [ ] **Step 1: Build + capture**

```bash
dotnet build /d/projects/winix/src/wargs/wargs.csproj --nologo -v quiet
mkdir -p /d/projects/winix/tmp/seam-baseline
/d/projects/winix/src/wargs/bin/Debug/net10.0/wargs.exe --help > /d/projects/winix/tmp/seam-baseline/wargs-help.out 2> /d/projects/winix/tmp/seam-baseline/wargs-help.err
/d/projects/winix/src/wargs/bin/Debug/net10.0/wargs.exe --describe > /d/projects/winix/tmp/seam-baseline/wargs-describe.out 2> /d/projects/winix/tmp/seam-baseline/wargs-describe.err
```
Expected: `.out` non-empty, `.err` empty (the emptiness is baseline — phase-1 F1 routing gate).

- [ ] **Step 2: Test count** — `dotnet test /d/projects/winix/tests/Winix.Wargs.Tests/Winix.Wargs.Tests.csproj --nologo -v quiet` → 167 passed, 7 skipped, 174 total.

---

### Task 1: Create `Winix.Wargs/Cli.cs`, thin `Program.cs`

**Files:**
- Create: `src/Winix.Wargs/Cli.cs`
- Rewrite: `src/wargs/Program.cs` (632 lines → ~55)
- Comment-only fixups: `tests/Winix.Wargs.Tests/ProgramMainTests.cs`

Read ALL of `src/wargs/Program.cs` first. Sibling exemplars: `src/Winix.Peep/Cli.cs` (async seam), `src/Winix.Demux/Cli.cs` (TextReader stdin) — wargs's plan is authoritative for wargs.

- [ ] **Step 1: Create `src/Winix.Wargs/Cli.cs`**

Skeleton (`…` = moved verbatim per the mapping table):

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Yort.ShellKit;

namespace Winix.Wargs;

/// <summary>
/// Library entry point for the wargs tool: parses arguments, validates mode combinations,
/// reads items from the supplied reader, runs the job pipeline, and routes all output —
/// including the envelope-on-every-exit-path cancellation/error catches — through the
/// supplied writers. <c>Program.Main</c> is a thin shell owning console setup, Ctrl+C
/// registration, and the real-console stdin-unblock-on-cancel hook.
/// </summary>
/// <remarks>
/// Seam limit (by design — do not "fix"): the <c>Console.In.Close()</c>-on-cancel
/// registration lives in Main because it mutates the REAL console's stdin to unblock a
/// pending read (round-7 Linux fix; round-8 Windows caveat). The seam's <paramref
/// name="stdin"/> is an injected reader in tests; the library never touches Console.In.
/// That path stays covered by the Linux SkippableFact binary test and the smoke fixture.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the wargs CLI.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <param name="stdin">Source of input items (one per line by default; <c>-0</c>,
    /// <c>--delimiter</c>, <c>--compat</c> alter splitting). Production passes
    /// <c>Console.In</c>; tests pass a <see cref="StringReader"/>.</param>
    /// <param name="stdout">Receives child process stdout (buffered per job by default).</param>
    /// <param name="stderr">Receives the failure summary, JSON envelope (<c>--json</c>),
    /// or streaming NDJSON lines (<c>--ndjson</c>) — every exit path emits an envelope
    /// under structured-output modes, including cancellation and unexpected errors.</param>
    /// <param name="cancellationToken">Cancellation signal (Ctrl+C in production, owned by
    /// Main). Observed between/during jobs (kill-on-cancel) and during input materialisation.</param>
    /// <returns>0 success; 123 child_failed; 124 fail_fast_abort; 125 usage; 126
    /// input_read_failed/unexpected; 130 cancelled.</returns>
    public static async Task<int> RunAsync(string[] args, TextReader stdin,
        TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        string version = GetVersion();
        // … pre-scan (ContainsFlag for --json/--ndjson) verbatim from Main, with its comment …
        // try {
        //     return await RunCoreAsync(args, version, stdin, stdout, stderr, cancellationToken)
        //         .ConfigureAwait(false);
        // }
        // catch (OperationCanceledException) { … verbatim from Main: 130 + cancelled envelope to stderr … }
        // catch (Exception ex) when (…) { … verbatim from Main: UnwrapTypeInit + mode-discriminated envelope … }
    }

    // private static async Task<int> RunCoreAsync(string[] args, string version,
    //     TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    // { … the entire current Program.RunAsync body, transformed per the mapping table … }

    // … ContainsFlag / UsageError / SafeWriteLine / GetVersion moved per the table …
}
```

**Transformation mapping table:**

| Old (`src/wargs/Program.cs`) | New (`Cli.cs`) |
|---|---|
| Main's pre-scan (`jsonOutput`/`ndjsonOutput` via `ContainsFlag`) + its comment | top of `Cli.RunAsync`, verbatim |
| Main's `catch (OperationCanceledException)` (130 + cancelled envelope) + comment | `Cli.RunAsync`'s OCE catch; `Console.Error` → `stderr` |
| Main's catch-all (`ExceptionUnwrap.UnwrapTypeInit`, mode-discriminated) + comments | `Cli.RunAsync`'s catch-all; `Console.Error` → `stderr` |
| Main's CTS + `cancelHandler` + registration + `finally` unregister + the round-7 broadened-catch comment | **STAYS IN MAIN** |
| Main's `cts.Token.Register(() => Console.In.Close())` + the full round-7/8 comment block | **STAYS IN MAIN** (ADR W2) |
| `private static async Task<int> RunAsync(string[] args, string version, CancellationToken cancellationToken)` | `private static async Task<int> RunCoreAsync(string[] args, string version, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)` |
| `new InputReader(Console.In, delimMode, customDelimiter)` | `new InputReader(stdin, delimMode, customDelimiter)` |
| `jobRunner.RunAsync(invocations, Console.Out, Console.Error, cancellationToken)` | `…(invocations, stdout, stderr, cancellationToken)` |
| Every `SafeWriteLine(Console.Error, …)` (≈12 sites: ndjson parse-error, input_read_failed pair, no_input trio, NDJSON callbacks ×2, dry_run pair, json summary, faults loop) | `SafeWriteLine(stderr, …)` |
| `HumanSummary.Emit(result, wargsResult, Console.Error)` | `…(result, wargsResult, stderr)` |
| `result.WriteErrors(Console.Error)` / `result.WriteError(…, Console.Error)` (incl. inside `UsageError`) | `stderr` |
| `UsageError(…)` helper | moves; gains `TextWriter stderr` parameter: `UsageError(ParseResult result, string message, bool jsonOutput, bool ndjsonOutput, string version, TextWriter stderr)` |
| `ContainsFlag`, `SafeWriteLine`, `GetVersion` | move verbatim (`GetVersion` already anchors `typeof(WargsExitCode).Assembly`) |

All review-round comments (round-4 NDJSON discipline, round-6 double-envelope, round-7 EINTR re-check, round-12/12.5 streaming-callback contract, round-12 SkipReason classifier, round-16 notes) move **verbatim**.

- [ ] **Step 2: Rewrite `src/wargs/Program.cs`** — complete replacement:

```csharp
using Winix.Wargs;
using Yort.ShellKit;

namespace Wargs;

internal sealed class Program
{
    /// <summary>
    /// Entry point. Owns process-global state only: console setup, Ctrl+C registration,
    /// and the real-console stdin-unblock-on-cancel hook. All parsing, validation, the
    /// job pipeline, and the envelope-on-every-exit-path catches live in
    /// <see cref="Cli.RunAsync"/>.
    /// </summary>
    static async Task<int> Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Register Console.CancelKeyPress AT MAIN SCOPE, BEFORE RunAsync — including before
        // input materialisation. Round-4 wrongly assumed an in-RunAsync handler caught
        // Ctrl+C-during-stdin-read; in fact .NET's default Ctrl+C handling tears the
        // process down before any handler installed later in the call stack can fire.
        // With this early registration, e.Cancel=true keeps the process alive long enough
        // for InputReader to observe the cancellation (Console.In.ReadLine returns null
        // after Ctrl+C with e.Cancel=true) and for the OCE catch (now in Cli.RunAsync) to
        // emit the cancelled envelope.
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            // Round-7 SFH: broaden from ObjectDisposedException to all exceptions.
            // CancellationTokenSource.Cancel() invokes registered callbacks synchronously
            // and aggregates any callback throws into AggregateException. A SIGINT handler
            // that throws ANY exception lets the runtime tear the process down — bypassing
            // the entire envelope-emission catch in Cli.RunAsync. Best-effort: never crash
            // the process from the SIGINT handler.
            try { cts.Cancel(); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* see comment */ }
        };
        Console.CancelKeyPress += cancelHandler;

        // Round-7 SFH C1/I1: a blocked Console.In.ReadLine() on Linux may not return null
        // after `e.Cancel=true` (the runtime may translate EINTR to IOException, restart
        // the read, or block indefinitely depending on the .NET runtime version). The
        // round-6 fix assumed null-return — fragile on Linux. Closing stdin on cancel
        // forces the read to unblock with EOF; the InputReader's cancellation-aware
        // enumerator (round-7) then propagates OCE before the empty-input branch fires.
        //
        // Coverage caveat: this is end-to-end pinned for Linux only via SkippableFact
        // CtrlCDuringStdin_UnderNdjson_EmitsCancelledEnvelope. On Windows, SyncTextReader
        // wraps Close() with `lock(this)` — same lock the read holds — so cross-thread
        // close from the SIGINT handler is a documented synchronization concern. The
        // fallback Windows behaviour (CancelKeyPress + e.Cancel=true alone returning null
        // from ReadLine) is empirically observed to work for the dev-box smoke test but
        // not regression-pinned. Round-8 SFH I2 / TA I2 — left as a known coverage gap;
        // see project_wargs_progress.md for the planned Windows GenerateConsoleCtrlEvent
        // integration test.
        //
        // This registration stays in Main (seam ADR W2): it mutates the REAL console's
        // stdin; Cli.RunAsync's injected reader must never be conflated with Console.In.
        cts.Token.Register(() =>
        {
            try { Console.In.Close(); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* best-effort — Console.In may already be closed */ }
        });

        try
        {
            return await Cli.RunAsync(args, Console.In, Console.Out, Console.Error, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            // Unregister before 'using' disposes cts, so a late Ctrl+C
            // doesn't call Cancel() on a disposed CancellationTokenSource.
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
```

- [ ] **Step 3: Build** — `dotnet build /d/projects/winix/src/wargs/wargs.csproj --nologo -v quiet` → 0 warnings.

- [ ] **Step 4: Existing tests + authorised comment fixups**

Run the suite → 167/7/174 unchanged. Then `grep -n "Program.cs" tests/Winix.Wargs.Tests/ProgramMainTests.cs` — retarget each stale reference (e.g. `Program.cs:272` → `Winix.Wargs/Cli.cs RunCoreAsync`), wording only.

- [ ] **Step 5: Commit**

```bash
git -C /d/projects/winix add src/Winix.Wargs/Cli.cs src/wargs/Program.cs tests/Winix.Wargs.Tests/ProgramMainTests.cs
git -C /d/projects/winix commit -m "refactor(wargs): extract Cli.RunAsync library seam — Main keeps console setup, Ctrl+C/CTS, and the real-console stdin-unblock hook

Behaviour-neutral move per docs/plans/2026-06-06-wargs-cli-seam-design.md.
Pre-scan + OCE catch + catch-all move into Cli.RunAsync (ADR W1); the
Console.In.Close-on-cancel registration stays in Main (ADR W2).
ProgramMainTests: comment-only location-reference fixups."
```

---

### Task 2: Wiring/regression seam tests (W4 stage 1 — NOT the newly-unlocked group)

**Files:**
- Create: `tests/Winix.Wargs.Tests/CliRunAsyncTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Yort.ShellKit;

namespace Winix.Wargs.Tests;

/// <summary>
/// End-to-end tests for <see cref="Cli.RunAsync"/> — parse→validate→pipeline with injected
/// StringReader stdin, threaded writers, and real child processes. This file contains the
/// WIRING/REGRESSION group only (seam ADR W4 stage 1); the newly-unlocked cancellation and
/// fault-injection tests live in CliRunAsyncUnlockedTests (stage 2, added after the move's
/// behaviour-neutrality was validated).
/// </summary>
public class CliRunAsyncTests
{
    private static async Task<(int Exit, string Stdout, string Stderr)> RunCliAsync(
        string stdinText, params string[] args)
    {
        using var stdin = new StringReader(stdinText);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = await Cli.RunAsync(args, stdin, stdout, stderr, CancellationToken.None);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Command argv for a child that echoes its appended item and exits 0.
    /// Items are APPENDED to the command by wargs, so the command must tolerate a
    /// trailing argument. /bin/echo prints it; cmd's echo prints it.</summary>
    private static string[] EchoCommand() =>
        OperatingSystem.IsWindows()
            ? new[] { "--", "cmd.exe", "/c", "echo" }
            : new[] { "--", "/bin/echo" };

    private const string NoSuchCommand = "winix-test-no-such-command-zz9";

    private static string[] Concat(string[] head, string[] tail)
    {
        var all = new string[head.Length + tail.Length];
        head.CopyTo(all, 0);
        tail.CopyTo(all, head.Length);
        return all;
    }

    // --- Mutual-exclusion validations → 125 (the 6 rules) ---

    [Theory]
    [InlineData("--null", "--compat")]
    [InlineData("--confirm", "--parallel", "2")]
    [InlineData("--line-buffered", "--keep-order")]
    [InlineData("--line-buffered", "--parallel", "2")]
    [InlineData("--ndjson", "--line-buffered")]
    [InlineData("--json", "--confirm")]
    [InlineData("--json", "--verbose")]
    public async Task MutuallyExclusiveFlags_Return125(params string[] flags)
    {
        var r = await RunCliAsync("x\n", Concat(flags, EchoCommand()));
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.NotEqual(string.Empty, r.Stderr);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public async Task NdjsonModeUsageError_EmitsEnvelopeOnly()
    {
        // Strict NDJSON discipline: the usage-error path under --ndjson must emit ONLY the
        // envelope (round-4/round-6 line-discipline contract). Every stderr line must parse.
        var r = await RunCliAsync("x\n", Concat(new[] { "--ndjson", "--verbose" }, EchoCommand()));
        Assert.Equal(ExitCode.UsageError, r.Exit);
        string[] lines = r.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Single(lines);
        using var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task NdjsonParserError_EnvelopeOnly_NoShellKitMultiline()
    {
        // Parser-level error (unknown flag) under --ndjson: wargs suppresses ShellKit's
        // multi-line error output and emits its own single envelope (round-6 CR/SFH I1).
        var r = await RunCliAsync("", "--ndjson", "--no-such-flag");
        Assert.Equal(ExitCode.UsageError, r.Exit);
        string[] lines = r.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Single(lines);
        using var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    // --- no_input / dry_run envelopes ---

    [Fact]
    public async Task EmptyInput_Json_NoInputEnvelope_ExitZero()
    {
        var r = await RunCliAsync("", Concat(new[] { "--json" }, EchoCommand()));
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("no_input", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task EmptyInput_Ndjson_NoInputEnvelopeLine()
    {
        var r = await RunCliAsync("", Concat(new[] { "--ndjson" }, EchoCommand()));
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("no_input", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task EmptyInput_Human_DiagnosticOnStderr()
    {
        var r = await RunCliAsync("", EchoCommand());
        Assert.Equal(0, r.Exit);
        Assert.Contains("no input items", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DryRun_Json_ReportsPlanCount()
    {
        var r = await RunCliAsync("a\nb\nc\n", Concat(new[] { "--json", "--dry-run" }, EchoCommand()));
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("dry_run", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("total_jobs").GetInt32());
    }

    // --- Real-child paths ---

    [Fact]
    public async Task HappyPath_ChildStdoutOnStdoutWriter_ExitZero()
    {
        var r = await RunCliAsync("WARGS-SEAM-MARKER\n", EchoCommand());
        Assert.Equal(0, r.Exit);
        Assert.Contains("WARGS-SEAM-MARKER", r.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpawnFailure_Returns123_FaultSurfacedInHumanMode()
    {
        // True spawn failure is the deterministic child-failure vehicle — but ONLY with
        // --no-shell-fallback (adversarial-review F4, probe-pinned 2026-06-06): the default
        // shell fallback runs `sh -c`/`cmd /c` instead, yielding the SHELL's exit code and
        // no fault_message.
        var r = await RunCliAsync("x\n", "--no-shell-fallback", "--", NoSuchCommand);
        Assert.Equal(WargsExitCode.ChildFailed, r.Exit);
        Assert.Contains("job 1", r.Stderr, StringComparison.Ordinal);
        Assert.Contains("failed to spawn", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpawnFailure_Json_SummaryEnvelope123()
    {
        var r = await RunCliAsync("x\n", "--json", "--no-shell-fallback", "--", NoSuchCommand);
        Assert.Equal(WargsExitCode.ChildFailed, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("child_failed", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task FailFast_SkipsRemaining_Returns124()
    {
        // 3 items, all true spawn-failures, sequential: first fails, fail-fast skips the rest.
        // Exit must be 124 (fail_fast_abort) per the round-12 SkipReason-filtered classifier.
        var r = await RunCliAsync("a\nb\nc\n", "--json", "--fail-fast", "--no-shell-fallback", "--", NoSuchCommand);
        Assert.Equal(WargsExitCode.FailFastAbort, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("fail_fast_abort", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.True(doc.RootElement.GetProperty("skipped").GetInt32() >= 1);
    }

    // --- NDJSON streaming ---

    [Fact]
    public async Task Ndjson_EveryStderrLineParses_PerJobFields_NoSummaryLine()
    {
        var r = await RunCliAsync("a\nb\n", "--ndjson", "--no-shell-fallback", "--", NoSuchCommand);
        Assert.Equal(WargsExitCode.ChildFailed, r.Exit);
        string[] lines = r.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Exactly N per-job lines and NOTHING else: no summary envelope may follow the
        // stream on the normal completion path (round-4/6 line discipline; adversarial-
        // review F6 — the count + per-line "job" property pin stream purity, not just
        // per-line parseability).
        Assert.Equal(2, lines.Length);
        foreach (string line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("job", out _), "non-per-job line found in NDJSON stream");
            Assert.Equal("child_failed", doc.RootElement.GetProperty("exit_reason").GetString());
            // child_exit_code:-1 is PROBE-PINNED for --no-shell-fallback spawn failures
            // (adversarial-review F4; without the flag the shell's 127/9009 appears instead).
            Assert.Equal(-1, doc.RootElement.GetProperty("child_exit_code").GetInt32());
        }
    }

    [Fact]
    public async Task Ndjson_Parallel_NoKeepOrder_EveryLineParses()
    {
        // Adversarial-review F1: the only multi-writer path into the stderr writer is the
        // DEFAULT --ndjson callback under --parallel (keep-order drains single-threaded
        // through the reorder buffer). The production lock around SafeWriteLine is what
        // keeps concurrent callback writes line-atomic; if that lock is ever dropped or
        // mis-scoped, torn/interleaved lines appear here as JSON parse failures or a wrong
        // line count. StringWriter is NOT thread-safe — this test is the lock's regression pin.
        var r = await RunCliAsync("a\nb\nc\nd\ne\nf\ng\nh\n",
            "--ndjson", "--parallel", "4", "--no-shell-fallback", "--", NoSuchCommand);
        Assert.Equal(WargsExitCode.ChildFailed, r.Exit);
        string[] lines = r.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(8, lines.Length);
        foreach (string line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("job", out _));
        }
    }

    [Fact]
    public async Task Ndjson_KeepOrder_LinesInInputOrder()
    {
        // Under -P4 completion order is nondeterministic; --keep-order must reorder the
        // NDJSON stream to input order (the original-design second clause that round-12's
        // first streaming attempt missed).
        var r = await RunCliAsync("one\ntwo\nthree\nfour\n",
            "--ndjson", "--keep-order", "--parallel", "4", "--no-shell-fallback", "--", NoSuchCommand);
        string[] lines = r.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(4, lines.Length);
        string[] expected = { "one", "two", "three", "four" };
        for (int i = 0; i < 4; i++)
        {
            using var doc = JsonDocument.Parse(lines[i]);
            Assert.Equal(expected[i], doc.RootElement.GetProperty("input").GetString());
        }
    }

    // --- Delimiter-mode threading (adversarial-review F3) ---

    [Fact]
    public async Task NullDelimiter_ThreadsThroughInputReader()
    {
        // A move that mis-wired delimMode/customDelimiter through the relocated
        // InputReader construction would pass every newline-based test; -0 with NUL-
        // separated items proves the delimiter args thread correctly. Plan-count via
        // --dry-run avoids any child dependency.
        var r = await RunCliAsync("a\0b\0c", Concat(new[] { "--json", "--dry-run", "--null" }, EchoCommand()));
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal(3, doc.RootElement.GetProperty("total_jobs").GetInt32());
    }

    // --- Batching ---

    [Fact]
    public async Task Batch_GroupsItemsPerInvocation()
    {
        // 4 items, --batch 2 → 2 jobs. Dry-run plan count proves the grouping without
        // depending on child argv echo formats.
        var r = await RunCliAsync("a\nb\nc\nd\n", Concat(new[] { "--json", "--dry-run", "--batch", "2" }, EchoCommand()));
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal(2, doc.RootElement.GetProperty("total_jobs").GetInt32());
    }
}
```

- [ ] **Step 2: Run** — `--filter "FullyQualifiedName~CliRunAsyncTests"` → all pass. Non-pinned message-wording assertions: fix the ASSERTION if wrong (record it); production-defect suspicion = STOP.

- [ ] **Step 3: Full suite** — 174 + ~16 new ≈ 190 total shape, 0 failed.

- [ ] **Step 4: Commit**

```bash
git -C /d/projects/winix add tests/Winix.Wargs.Tests/CliRunAsyncTests.cs
git -C /d/projects/winix commit -m "test(wargs): Cli.RunAsync wiring/regression seam tests — validations, NDJSON discipline, envelopes, real-child paths (W4 stage 1)"
```

---

### Task 3: Byte-stability verification (neutrality gate — no commit)

- [ ] **Step 1: Rebuild + capture + diff each stream** (same shape as Task 0, `-after` suffixes, 4 independent diffs). Expected: zero diff on all four; any `.out`↔`.err` migration = routing regression = STOP.

```bash
dotnet build /d/projects/winix/src/wargs/wargs.csproj --nologo -v quiet
/d/projects/winix/src/wargs/bin/Debug/net10.0/wargs.exe --help > /d/projects/winix/tmp/seam-baseline/wargs-help-after.out 2> /d/projects/winix/tmp/seam-baseline/wargs-help-after.err
/d/projects/winix/src/wargs/bin/Debug/net10.0/wargs.exe --describe > /d/projects/winix/tmp/seam-baseline/wargs-describe-after.out 2> /d/projects/winix/tmp/seam-baseline/wargs-describe-after.err
diff /d/projects/winix/tmp/seam-baseline/wargs-help.out /d/projects/winix/tmp/seam-baseline/wargs-help-after.out
diff /d/projects/winix/tmp/seam-baseline/wargs-help.err /d/projects/winix/tmp/seam-baseline/wargs-help-after.err
diff /d/projects/winix/tmp/seam-baseline/wargs-describe.out /d/projects/winix/tmp/seam-baseline/wargs-describe-after.out
diff /d/projects/winix/tmp/seam-baseline/wargs-describe.err /d/projects/winix/tmp/seam-baseline/wargs-describe-after.err
```

- [ ] **Step 2: Manual smoke**

```bash
bash -c 'printf "hello\n" | /d/projects/winix/src/wargs/bin/Debug/net10.0/wargs.exe -- cmd.exe /c echo; echo EXIT=$?'
bash -c 'printf "hello\n" | /d/projects/winix/src/wargs/bin/Debug/net10.0/wargs.exe --json -- cmd.exe /c echo 1>/dev/null; echo EXIT=$?'
```
Expected: first → "hello" + EXIT=0; second → JSON summary envelope on stderr, EXIT=0.

---

### Task 4: Newly-unlocked tests (W4 stage 2 — only after Task 3 passes)

**Files:**
- Create: `tests/Winix.Wargs.Tests/CliRunAsyncUnlockedTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Yort.ShellKit;

namespace Winix.Wargs.Tests;

/// <summary>
/// The previously-IMPOSSIBLE coverage the seam unlocks (ADR W4 stage 2 — added after the
/// move's behaviour-neutrality was validated by Task 3's gates): the cancelled envelope
/// (rounds 4–8's most-litigated contract), input_read_failed via fault injection, and the
/// round-7 cancel-vs-read-failure classification.
/// </summary>
public class CliRunAsyncUnlockedTests
{
    /// <summary>TextReader that yields nothing and throws IOException on every read —
    /// drives the input_read_failed path that previously needed a broken OS pipe.</summary>
    private sealed class ThrowingTextReader : TextReader
    {
        public override int Read() => throw new IOException("synthetic stdin fault");
        public override int Read(char[] buffer, int index, int count) => throw new IOException("synthetic stdin fault");
        public override string? ReadLine() => throw new IOException("synthetic stdin fault");
    }

    /// <summary>Path to a Windows sleep helper batch file, created once per test class in
    /// the temp dir. DECIDED AT PLANNING (adversarial-review F5 — no implementation-time
    /// fork): a generated .cmd beats cmd-parsing cleverness (`&amp;rem` item-swallowing) because
    /// a batch file ignores extra arguments it never references — wargs's appended item is
    /// harmless by construction, no VERIFY needed. The batch sleeps ~30s via ping.</summary>
    private static readonly Lazy<string> WindowsSleepCmd = new(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), $"wargs-seam-sleep-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(path, "@ping -n 30 127.0.0.1 > NUL\r\n");
        return path;
    });

    /// <summary>Command argv whose appended item is ignored, so the child sleeps ~30s
    /// regardless of the item (long child is load-bearing — phase-1 lesson: a fast child
    /// can beat the kill and the 130 assert fails loudly, which is the designed protection;
    /// see the F5 analysis in the review-integration record).</summary>
    private static string[] SleepCommand() =>
        OperatingSystem.IsWindows()
            ? new[] { "--", WindowsSleepCmd.Value }
            : new[] { "--", "/bin/sh", "-c", "sleep 30" };

    private static string[] Concat(string[] head, string[] tail)
    {
        var all = new string[head.Length + tail.Length];
        head.CopyTo(all, 0);
        tail.CopyTo(all, head.Length);
        return all;
    }

    // --- Cancelled envelope (pre-cancelled token; "always at least the envelope") ---

    [Theory]
    [InlineData("--json")]
    [InlineData("--ndjson")]
    public async Task PreCancelledToken_EmitsCancelledEnvelope_130(string mode)
    {
        using var stdin = new StringReader("30\n");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int exit = await Cli.RunAsync(Concat(new[] { mode }, SleepCommand()), stdin, stdout, stderr, cts.Token);
        Assert.Equal(130, exit);
        // The cancelled envelope must be the LAST stderr line (NDJSON may have per-job
        // lines before it in mid-run scenarios; pre-cancelled typically yields just the
        // envelope — parse the last non-empty line to be robust to both).
        string[] lines = stderr.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        using var doc = JsonDocument.Parse(lines[^1]);
        Assert.Equal("cancelled", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(130, doc.RootElement.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public async Task PreCancelledToken_HumanMode_130_NoEnvelope()
    {
        using var stdin = new StringReader("30\n");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int exit = await Cli.RunAsync(SleepCommand(), stdin, stdout, stderr, cts.Token);
        Assert.Equal(130, exit);
        Assert.DoesNotContain("exit_reason", stderr.ToString(), StringComparison.Ordinal);
    }

    // --- Mid-run cancel: kill-on-cancel through JobRunner ---

    [Fact]
    public async Task MidRunCancel_Parallel_KillsAllInFlightChildren_130_Promptly()
    {
        // Adversarial-review F2: a single-job cancel can pass even if the parallel kill
        // fan-out is broken. 4 long children in flight under -P4; the cancel must kill
        // ALL of them for the run to return inside the liveness bound. Self-protecting
        // against a fast-exiting child (broken sleep helper): the run would then complete
        // BEFORE the 300ms cancel and exit 0/123, failing the 130 assert loudly.
        using var stdin = new StringReader("a\nb\nc\nd\n");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(300);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int exit = await Cli.RunAsync(
            Concat(new[] { "--json", "--parallel", "4" }, SleepCommand()), stdin, stdout, stderr, cts.Token);
        sw.Stop();
        Assert.Equal(130, exit);
        string[] lines = stderr.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        using var doc = JsonDocument.Parse(lines[^1]);
        Assert.Equal("cancelled", doc.RootElement.GetProperty("exit_reason").GetString());
        // Coarse LIVENESS bound (not perf): must sit well under the children's ~30s sleep.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15),
            $"cancel→kill took {sw.Elapsed} — in-flight children were not all killed promptly");
    }

    // --- input_read_failed via fault injection ---

    [Theory]
    [InlineData("--json")]
    [InlineData("--ndjson")]
    public async Task ThrowingStdin_InputReadFailed_126_Envelope(string mode)
    {
        using var stdin = new ThrowingTextReader();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = await Cli.RunAsync(new[] { mode, "--", "cmd-irrelevant" }, stdin, stdout, stderr, CancellationToken.None);
        Assert.Equal(ExitCode.NotExecutable, exit);
        using var doc = JsonDocument.Parse(stderr.ToString());
        Assert.Equal("input_read_failed", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task ThrowingStdin_HumanMode_126_DiagnosticWithExceptionType()
    {
        using var stdin = new ThrowingTextReader();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = await Cli.RunAsync(new[] { "--", "cmd-irrelevant" }, stdin, stdout, stderr, CancellationToken.None);
        Assert.Equal(ExitCode.NotExecutable, exit);
        Assert.Contains("failed to read input", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("IOException", stderr.ToString(), StringComparison.Ordinal);
    }

    // --- Round-7 classification: cancel during read is CANCELLED, not input_read_failed ---

    [Fact]
    public async Task ThrowingStdin_WithCancelledToken_ClassifiesAsCancelled_130()
    {
        // Pins the round-7 SFH fix: on Linux a SIGINT can surface as an IOException from a
        // blocked stdin read while the token is already signalled. The materialisation
        // catch must re-check the token and classify as cancelled (130), NOT
        // input_read_failed (126). The seam makes that race deterministically composable:
        // a reader that throws IOException + a token that is already cancelled.
        using var stdin = new ThrowingTextReader();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int exit = await Cli.RunAsync(new[] { "--json", "--", "cmd-irrelevant" }, stdin, stdout, stderr, cts.Token);
        Assert.Equal(130, exit);
        using var doc = JsonDocument.Parse(stderr.ToString());
        Assert.Equal("cancelled", doc.RootElement.GetProperty("exit_reason").GetString());
    }
}
```

- [ ] **Step 2: Run + full suite** — all pass; cancellation/classification contracts are pinned (probe + code-read) — failures are real signals, STOP rather than soften. The Windows sleep helper is a generated `.cmd` (decided at planning, F5) — no parsing cleverness, no VERIFY fork.

- [ ] **Step 3: Commit**

```bash
git -C /d/projects/winix add tests/Winix.Wargs.Tests/CliRunAsyncUnlockedTests.cs
git -C /d/projects/winix commit -m "test(wargs): newly-unlocked seam coverage — cancelled envelope, mid-run kill-on-cancel, input_read_failed fault injection, round-7 cancel-vs-read classification (W4 stage 2)"
```

---

### Task 5: Fixture cancellation case (established pattern)

**Files:**
- Modify: `artifacts/round-stop-2026-05-09/wargs/run-smokes.sh` (locate first: `ls artifacts/*/wargs/run-smokes.sh`; use `git add -f` when committing — artifacts/ is dir-ignored but the fixture is tracked)

- [ ] **Step 1: Add the case** (after the last existing W/R-series case, matching the file's `run` helper conventions):

```bash
# ── Capability-surface addition (2026-06-06) ──
# WX1: SIGINT mid-job — the envelope-on-cancel contract (rounds 4-8) end-to-end.
# EXPECTED RESULT: exit file = 124 AND stderr envelope = {"...,"exit_code":130,"exit_reason":"cancelled"}.
# 124 is GNU timeout's OWN code (it reports 124 whenever it had to send the signal; the
# child's subsequent exit never passes through). Probed 2026-06-06 on the linux-x64 binary:
# wargs exits 130 ~40ms after the INT, envelope exactly as above, nothing lingers.
# timeout signals its process GROUP, so wargs's sleep child receives INT directly too.
# Linux-only: MSYS cannot deliver SIGINT to native Windows exes; covered on Windows by
# the manual real-terminal Ctrl+C smoke.
if [ "$(uname -s)" = "Linux" ]; then
  run WX1 "SIGINT mid-job -> cancelled envelope (exit 124 = timeout's own code)" bash -c "printf '30\n' | timeout -s INT 2 \"\$0\" --json -- sleep" "$BIN"
else
  echo "=== WX1: SKIPPED (Windows: no SIGINT delivery to native exe from this harness) ==="
  echo "skipped" > "$OUT/WX1.exit"
fi
```

NOTE for the executor: the `run` helper wraps commands with its own outer `timeout`; the
`bash -c` + `$0` form is needed because the case requires a PIPE (stdin items). Verify the
quoting by RUNNING it — if the layered quoting fights the helper, fall back to a tiny
companion script file next to run-smokes.sh (committed alongside) instead of inline `bash -c`.

- [ ] **Step 2: Run on Windows** (expect WX1 SKIP marker + all existing cases unchanged) after refreshing `fresh-publish/wargs.exe` from a new win-x64 publish.

- [ ] **Step 3: Run on Linux** via a retargeted copy in tmp/ (linux-x64 publish from WSL; pattern: sed BIN/OUT). Expect WX1 exit file 124 + cancelled envelope in WX1.stderr.

- [ ] **Step 4: Commit** (with `git add -f`)

```bash
git -C /d/projects/winix add -f artifacts/round-stop-2026-05-09/wargs/run-smokes.sh
git -C /d/projects/winix commit -m "test(smokes): wargs capability-surface addition — SIGINT mid-job cancelled envelope (WX1, Linux-gated), per the established cancellation-smoke pattern"
```

---

### Task 6: Wrap-up

- [ ] **Step 1: Full solution test** — 0 failures (known one-off flakes: Winix.Trash recycle-bin; isolate-re-run before treating as real).
- [ ] **Step 2: CLAUDE.md layout line** — `src/Winix.Wargs/           — class library (input reading, command builder, job execution, formatting)` → append `, Cli.RunAsync seam` (match exact current text).
- [ ] **Step 3: Commit + push + CI**

```bash
git -C /d/projects/winix add CLAUDE.md
git -C /d/projects/winix commit -m "docs: CLAUDE.md layout — Winix.Wargs now carries the Cli.RunAsync seam"
git -C /d/projects/winix push -u origin feature/cli-seam-wargs
gh workflow run ci.yml --repo Yortw/winix --ref feature/cli-seam-wargs
```

---

### Task 7: Main-session verification gates (NOT for subagents)

- [ ] WSL: full `Winix.Wargs.Tests` run — the cancellation/fault-injection tests under Linux kill semantics are the watch items.
- [ ] 3-OS CI green on the feature branch.
- [ ] Live interactive Ctrl+C smoke is NOT required this time — wargs has no interactive TUI; the real-console signal path is covered by the existing Linux `SkippableFact` binary test (which runs in WSL/CI) + the new WX1 fixture case.
- [ ] Whole-feature fresh-eyes review (full branch diff).
- [ ] Merge `--no-ff` into `release/v0.4.0`; post-merge CI watch; delete branch; memory update (backlog 2 → 1: nc only).

---

## Adversarial-review integration record (pass 1, 2026-06-06)

Fresh-subagent review: **2 blockers, 4 test gaps, 2 defers**. Dispositions:

| Finding | Disposition |
|---|---|
| F4 (blocker) — `child_exit_code:-1` asserted from documentation, unverified | **Confirmed REAL by probe, then resolved.** Default shell fallback masks spawn failure (`sh -c` → 127; the documented -1 never appears). All deterministic-failure tests now use `--no-shell-fallback`; the `-1` + `fault_message` shape is probe-pinned. The reviewer caught a wrong assertion that would have shipped. |
| F5 (blocker) — Windows `&rem` item-swallow was an unresolved correctness fork | **Resolved at planning.** Replaced with a generated `.cmd` helper (batch files ignore unreferenced extra args — correct by construction). Recorded analysis: the 130 asserts self-protect against a fast-exiting child in MidRunCancel (run completes before cancel → not 130 → loud fail), and the pre-cancelled test never spawns (wargs throws post-materialisation), so "passes for the wrong reason" is structurally closed. |
| F1 (test gap) — parallel non-keep-order NDJSON (the only true multi-writer path) untested | **Accepted.** `Ndjson_Parallel_NoKeepOrder_EveryLineParses` added — 8 items, -P4, every line parses + count exact; the production lock's regression pin. |
| F2 (test gap) — single-child mid-run cancel doesn't prove kill fan-out | **Accepted.** MidRunCancel upgraded to -P4 with 4 in-flight sleep children. |
| F3 (test gap) — no delimiter-mode threading test | **Accepted.** `NullDelimiter_ThreadsThroughInputReader` added (-0 + dry-run plan count). |
| F6 (test gap) — NDJSON stream purity/ordering not positionally pinned | **Accepted.** The per-job test now asserts exact line count + every line carries the `job` property (no summary line may follow the stream); the F1 test extends the same pin to the parallel path. |
| F7/F8 (defers) — real-console close path; mode-discrimination styles | **Acknowledged** — both already recorded (ADR W2 / design out-of-scope); F8's advisory diff-self-check noted for the executor. |

Convergence: both blockers resolved with ground truth (probe + decided helper), gaps integrated as concrete tests. No structural plan change. The F4 catch is the pass's headline: a documented contract the plan trusted was empirically wrong without a flag — the probe-before-pin rule applied late beats not at all. Second pass not warranted: integrations are test additions/flag corrections backed by probes, no new unverified material.

## Self-review record (plan author, 2026-06-06)

- **Spec coverage:** design §seam shape → Task 1 (W1/W2/W3 all encoded in the mapping table + Main listing); §testing stage 1 → Task 2; stage 2 (W4) → Task 4 gated on Task 3; §fixture (W5) → Task 5 with the probed envelope; §verification → Tasks 0/3/6/7.
- **Placeholder scan:** `…` markers in the Cli.cs skeleton are mapping-table-backed move instructions; two explicit VERIFY markers (Windows `&rem` item-swallow; fixture quoting) name their fallback strategies.
- **Type consistency:** `Cli.RunAsync(string[], TextReader, TextWriter, TextWriter, CancellationToken)` consistent across skeleton, Main listing, and both test files; `RunCoreAsync` private signature consistent; `WargsExitCode.ChildFailed`/`FailFastAbort` verified against source (123/124); `ExceptionUnwrap` confirmed library-resident (no move).
- **Verified at planning:** SIGINT envelope probed on the linux-x64 binary (exact JSON pinned); pre-refactor count 167+7=174; ProgramMainTests fixup sites enumerated by grep; InternalsVisibleTo present; ndjson `child_exit_code:-1` on spawn failure from the `--describe` field documentation (loose-pinned via the documented contract).
