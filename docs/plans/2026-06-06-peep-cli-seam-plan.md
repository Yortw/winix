# peep Cli.RunAsync Seam Retrofit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Retrofit a `Cli.RunAsync` library seam to peep covering parse + validation + once-mode, with zero behaviour change; plus a test-only hardening of the known GitIgnoreChecker CI flake.

**Architecture:** Move `src/peep/Program.cs` orchestration into `src/Winix.Peep/Cli.cs` per the settled seam convention (`docs/plans/2026-06-06-cli-seam-retrofit-design.md`) and the peep-specific design (`docs/plans/2026-06-06-peep-cli-seam-design.md` + ADR). `InteractiveSession` is untouched (already library-resident, console-bound by design). Main keeps `ConsoleEnv` + one always-registered CTS/`CancelKeyPress`.

**Tech Stack:** .NET 10, C#, xUnit (+ Xunit.SkippableFact, already referenced), Yort.ShellKit.

**Branch:** `feature/cli-seam-peep` (created; design + ADR committed as `3f706a9`).

**Hard rules for executors:**
- **Existing tests pass UNMODIFIED.** Pre-refactor: **216 passed + 3 skipped = 219** in `Winix.Peep.Tests`. Two authorised exceptions ONLY: (a) comment-only `Program.cs` location-reference fixups in `tests/Winix.Peep.Tests/ProgramMainTests.cs` (~6 sites reference `Program.cs` line locations that move — retarget wording to `Winix.Peep/Cli.cs`, zero assertion changes, say so in the commit message); (b) the Task 4 rider's explicitly-authorised modification of `GitIgnoreCheckerTests.ClearCache_SubsequentQueryReEvaluates` (spelled out in Task 4). Anything else = STOP, report BLOCKED.
- `InteractiveSession`, `ScreenRenderer`, `CommandExecutor`, `SessionHelpers`, `GitIgnoreChecker` production code: DO NOT TOUCH (the rider is test-only).
- Preserve every moved comment VERBATIM (each encodes a past review finding); only file/location references inside comments may be updated.
- `Winix.Peep` already references `Yort.ShellKit` and grants `InternalsVisibleTo` to both `peep` and `Winix.Peep.Tests` — no csproj changes.
- Full braces; XML docs on public members (warnings-as-errors); Bash tool blocks `&&`/`;` (separate calls or one `bash -c '…'`); commit per task, no Co-Authored-By.
- **Probed contracts (2026-06-06, pre-refactor Debug binary) — pinned, do not soften:** `peep --once -- ""` → exit 126, stderr `peep: permission denied: ` (typed `CommandNotExecutableException` arm — NOT the catch-all; differs from retry); `peep --once --json -- ""` → stderr envelope `{"tool":"peep","version":"…","exit_code":126,"exit_reason":"command_not_executable"}`, exit 126.

---

### Task 0: Baseline capture (pre-refactor, stream-separated)

- [ ] **Step 1: Build and capture**

```bash
dotnet build /d/projects/winix/src/peep/peep.csproj --nologo -v quiet
mkdir -p /d/projects/winix/tmp/seam-baseline
/d/projects/winix/src/peep/bin/Debug/net10.0/peep.exe --help > /d/projects/winix/tmp/seam-baseline/peep-help.out 2> /d/projects/winix/tmp/seam-baseline/peep-help.err
/d/projects/winix/src/peep/bin/Debug/net10.0/peep.exe --describe > /d/projects/winix/tmp/seam-baseline/peep-describe.out 2> /d/projects/winix/tmp/seam-baseline/peep-describe.err
```
Expected: `.out` files non-empty; `.err` files empty (that emptiness IS baseline — a line moving to `.err` post-refactor is a routing regression, the phase-1 F1 lesson).

- [ ] **Step 2: Record pre-refactor test count**

Run: `dotnet test /d/projects/winix/tests/Winix.Peep.Tests/Winix.Peep.Tests.csproj --nologo -v quiet`
Expected: 216 passed, 3 skipped, 219 total, 0 failed.

---

### Task 1: Create `Winix.Peep/Cli.cs`, thin `Program.cs`

**Files:**
- Create: `src/Winix.Peep/Cli.cs`
- Rewrite: `src/peep/Program.cs` (338 lines → ~45)
- Comment-only fixups: `tests/Winix.Peep.Tests/ProgramMainTests.cs`

This is a **move**. Read ALL of `src/peep/Program.cs` first. Sibling exemplar: `src/Winix.Retry/Cli.cs` + `src/retry/Program.cs` (phase 1) — but peep's plan sections are authoritative.

- [ ] **Step 1: Create `src/Winix.Peep/Cli.cs`**

Skeleton (`…` = moved verbatim per the mapping table):

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.Peep;

/// <summary>
/// Library entry point for the peep tool: parses arguments, validates, and dispatches to
/// once-mode (fully seam-routed) or the interactive session. <c>Program.Main</c> is a thin
/// shell owning console setup and Ctrl+C registration.
/// </summary>
/// <remarks>
/// Seam limits (by design — do not "fix"): the interactive path (<see cref="InteractiveSession"/>)
/// is console-bound — alternate screen buffer, <c>ReadKey</c>, its own internal
/// <c>Console.CancelKeyPress</c> handler, and direct <c>Console.Out</c>/<c>Console.Error</c>
/// rendering. It is out of writer-seam scope and covered by its own session-level tests.
/// Seam tests must never invoke the interactive path (it enters the alternate buffer):
/// use <c>--once</c> or an error path.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the peep CLI.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <param name="stdout">Receives once-mode child output verbatim (peep captures, then
    /// writes — unlike retry's handle inheritance). The interactive path renders to the real
    /// console regardless of this writer (see remarks).</param>
    /// <param name="stderr">Receives errors, diagnostics, and the JSON envelope (peep's
    /// deliberate convention — stdout carries the watched command's output).</param>
    /// <param name="cancellationToken">Cancellation signal (Ctrl+C in production, owned by
    /// Main). Consumed by once-mode; the interactive session deliberately receives
    /// <see cref="CancellationToken.None"/> (see the call-site comment).</param>
    /// <returns>Exit code: child pass-through / 0, 125 usage, 126 not-executable, 127 not
    /// found, 130 cancelled.</returns>
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr,
        CancellationToken cancellationToken)
    {
        string version = GetVersion();
        // … parser chain verbatim from Program.cs (.Description() … .JsonField(…)) …
        // … parse, jsonOnlyViaJsonOutput bridging, validation, regex compilation —
        //   all verbatim with the writer transformations in the mapping table …
        // … once-mode dispatch:
        //   if (once) { return await RunOnceAsync(command, commandArgs, commandDisplay,
        //       jsonOutput, result.Has("--json-output"), version, stdout, stderr, cancellationToken); }
        // … SessionConfig build verbatim …
        // var session = new InteractiveSession(config);
        // The session owns its own Ctrl+C handling internally (it registers Console.CancelKeyPress
        // itself); threading Main's real token in would change interactive cancellation semantics
        // (token-cancel vs internal-quit could alter exit reason) — a behaviour change deferred to
        // its own decision (ADR 2026-06-06-peep-cli-seam P2). Keep None, matching pre-seam behaviour.
        // return await session.RunAsync(CancellationToken.None);
    }

    // … RunOnceAsync + GetVersion moved per the mapping table …
}
```

**Transformation mapping table:**

| Old (`src/peep/Program.cs`) | New (`Cli.cs`) |
|---|---|
| `Main` body minus `ConsoleEnv.EnableAnsiIfNeeded()`/`UseUtf8Streams()` | `RunAsync` body |
| `Console.Error.WriteLine(Formatting.FormatJsonError(…))` — the 3 `jsonOnlyViaJsonOutput` bridging sites (HasErrors / no-command / bad-regex) | `stderr.WriteLine(Formatting.FormatJsonError(…))` — three verbatim sites, NOT consolidated (design: consolidation is a quality follow-up, not a move) |
| `result.WriteErrors(Console.Error)` / `result.WriteError(…, Console.Error)` | `stderr` |
| `RunOnceAsync(command, commandArgs, commandDisplay, jsonOutput, result.Has("--json-output"), version)` | `RunOnceAsync(…, version, stdout, stderr, cancellationToken)` |
| `private static async Task<int> RunOnceAsync(string command, string[] commandArgs, string commandDisplay, bool jsonOutput, bool jsonOutputIncludeOutput, string version)` | same + `, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken` |
| RunOnceAsync's CTS + `cancelHandler` + `Console.CancelKeyPress +=` block AND the `finally { Console.CancelKeyPress -= … }` | **DELETED** — Main supplies the token. The CR I4/I9 ownership comment moves to Main (Step 2 listing). The R4 TA I6 comment (handler body in SessionHelpers) also moves to Main. The finally's unregister-before-dispose comment moves to Main's finally. |
| `CommandExecutor.RunAsync(command, commandArgs, TriggerSource.Initial, cts.Token)` | `…, cancellationToken)` |
| `Console.Write(peepResult.Output)` | `stdout.Write(peepResult.Output)` (Write, NOT WriteLine — output is verbatim) |
| Every `Console.Error.WriteLine(…)` in RunOnceAsync's catch arms and JSON emit | `stderr.WriteLine(…)` |
| `session.RunAsync(CancellationToken.None)` | unchanged — with the new explanatory comment from the skeleton |
| `GetVersion` | moves verbatim (already anchors `typeof(PeepResult).Assembly`) |

All other comments (R5 SFH I1 bridging, R6 SFH N1 hoist, CR I9 stream arm, R5 SFH I2 symmetry, TA R2-C4 historyRetained) move **verbatim** at their code's new location.

- [ ] **Step 2: Rewrite `src/peep/Program.cs`** — complete replacement:

```csharp
using Winix.Peep;
using Yort.ShellKit;

namespace Peep;

internal sealed class Program
{
    /// <summary>
    /// Entry point. Owns process-global state only: console setup and Ctrl+C registration.
    /// All parsing, validation, and once-mode orchestration live in <see cref="Cli.RunAsync"/>.
    /// </summary>
    static async Task<int> Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // CR I4 / CR I9: once-mode must respect Ctrl+C and not orphan the child. Without our
        // own CTS + CancelKeyPress handler, hitting Ctrl+C during `peep --once -- some-slow-cmd`
        // lets the .NET default handler tear down peep without ever cancelling the token we
        // passed to CommandExecutor — so its kill-on-cancel callback never fires and the child
        // leaks. Registration is now unconditional (Main cannot know once-vs-interactive before
        // Cli.RunAsync parses): during interactive mode this handler is benign — it sets
        // e.Cancel (as the session's own internal handler also does) and cancels a token the
        // interactive path does not observe (ADR P2).
        // R4 TA I6: handler body lives in SessionHelpers.RequestCancellationSilently so the
        // "Cancel after dispose must not throw" contract is regression-pinned
        // (Console.CancelKeyPress is a static event that doesn't compose with xunit).
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e)
            => SessionHelpers.RequestCancellationSilently(e, cts);
        Console.CancelKeyPress += cancelHandler;
        try
        {
            return await Cli.RunAsync(args, Console.Out, Console.Error, cts.Token);
        }
        finally
        {
            // Unregister BEFORE the using disposes the CTS, so a late Ctrl+C cannot
            // call Cancel() on a disposed source and surface as ObjectDisposedException
            // (the cancel-handler swallows that, but unregistering first is cleaner).
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
```

- [ ] **Step 3: Build** — `dotnet build /d/projects/winix/src/peep/peep.csproj --nologo -v quiet` → 0 warnings.

- [ ] **Step 4: Existing tests pass; authorised comment fixups**

Run: `dotnet test /d/projects/winix/tests/Winix.Peep.Tests/Winix.Peep.Tests.csproj --nologo -v quiet` → 216/3/219, 0 failed.
Then `grep -n "Program.cs" tests/Winix.Peep.Tests/ProgramMainTests.cs` — retarget each stale location reference (wording only, e.g. `Program.cs:148+` → `Winix.Peep/Cli.cs RunOnceAsync`); zero assertion changes.

- [ ] **Step 5: Commit**

```bash
git -C /d/projects/winix add src/Winix.Peep/Cli.cs src/peep/Program.cs tests/Winix.Peep.Tests/ProgramMainTests.cs
git -C /d/projects/winix commit -m "refactor(peep): extract Cli.RunAsync library seam — Main keeps console setup + unconditional Ctrl+C/CTS ownership

Behaviour-neutral move per docs/plans/2026-06-06-peep-cli-seam-design.md.
Once-mode fully writer-routed; InteractiveSession untouched (keeps
CancellationToken.None deliberately — ADR P2). ProgramMainTests:
comment-only location-reference fixups."
```

---

### Task 2: Seam tests (`CliRunAsyncTests`)

**Files:**
- Create: `tests/Winix.Peep.Tests/CliRunAsyncTests.cs`

- [ ] **Step 1: Write the tests** (real children; never invoke the interactive path — every test uses `--once` or an error path):

```csharp
#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Yort.ShellKit;

namespace Winix.Peep.Tests;

/// <summary>
/// End-to-end tests for <see cref="Cli.RunAsync"/> — parse→validate→once-mode with threaded
/// writers and real child processes (no fakes — phase-1 ADR D4 discipline). The interactive
/// path is deliberately out of seam scope (console-bound; enters the alternate buffer) and is
/// never invoked here: every test uses --once or an error path. Colour is also out of seam
/// scope (interactive-only — peep ADR P3).
/// </summary>
public class CliRunAsyncTests
{
    private const string NoSuchCommand = "winix-test-no-such-command-zz9";

    private static async Task<(int Exit, string Stdout, string Stderr)> RunCliAsync(
        string[] args, CancellationToken? token = null)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = await Cli.RunAsync(args, stdout, stderr, token ?? CancellationToken.None);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Child that prints a marker to stdout and exits 0.</summary>
    private static string[] EchoChild(string marker) =>
        OperatingSystem.IsWindows()
            ? new[] { "--once", "--", "cmd.exe", "/c", $"echo {marker}" }
            : new[] { "--once", "--", "/bin/sh", "-c", $"echo {marker}" };

    /// <summary>Child that exits with the given code, no output.</summary>
    private static string[] ExitChild(int code, params string[] flagsBeforeOnce)
    {
        string[] cmd = OperatingSystem.IsWindows()
            ? new[] { "--once", "--", "cmd.exe", "/c", $"exit {code}" }
            : new[] { "--once", "--", "/bin/sh", "-c", $"exit {code}" };
        var all = new string[flagsBeforeOnce.Length + cmd.Length];
        flagsBeforeOnce.CopyTo(all, 0);
        cmd.CopyTo(all, flagsBeforeOnce.Length);
        return all;
    }

    /// <summary>Child that sleeps ~10s (cancellation tests — long child is load-bearing).</summary>
    private static string[] SleepChild(params string[] flagsBeforeOnce)
    {
        string[] cmd = OperatingSystem.IsWindows()
            ? new[] { "--once", "--", "cmd.exe", "/c", "ping -n 10 127.0.0.1 > NUL" }
            : new[] { "--once", "--", "/bin/sh", "-c", "sleep 10" };
        var all = new string[flagsBeforeOnce.Length + cmd.Length];
        flagsBeforeOnce.CopyTo(all, 0);
        cmd.CopyTo(all, flagsBeforeOnce.Length);
        return all;
    }

    // --- Validation → 125 ---

    [Fact]
    public async Task NoCommand_Returns125_PlainErrorOnStderr()
    {
        var r = await RunCliAsync(Array.Empty<string>());
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("no command specified", r.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Theory]
    [InlineData("--interval", "0")]
    [InlineData("--interval", "-1")]
    [InlineData("--interval", "abc")]
    [InlineData("--debounce", "-5")]
    [InlineData("--history", "-1")]
    public async Task InvalidOptionValue_Returns125(string flag, string value)
    {
        var r = await RunCliAsync(new[] { flag, value, "--", "cmd" });
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.NotEqual(string.Empty, r.Stderr);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public async Task BadExitOnMatchRegex_Returns125_PlainError()
    {
        var r = await RunCliAsync(new[] { "--exit-on-match", "[", "--", "cmd" });
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.Contains("invalid regex pattern", r.Stderr, StringComparison.Ordinal);
    }

    // --- The --json-output bridging trio (R5 SFH I1 — envelope without --json) ---

    [Fact]
    public async Task JsonOutputBridging_NoCommand_EnvelopeOnStderr()
    {
        var r = await RunCliAsync(new[] { "--json-output" });
        Assert.Equal(ExitCode.UsageError, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public async Task JsonOutputBridging_ParseError_EnvelopeOnStderr()
    {
        var r = await RunCliAsync(new[] { "--json-output", "--interval", "abc", "--", "cmd" });
        Assert.Equal(ExitCode.UsageError, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task JsonOutputBridging_BadRegex_EnvelopeOnStderr()
    {
        var r = await RunCliAsync(new[] { "--json-output", "--exit-on-match", "[", "--", "cmd" });
        Assert.Equal(ExitCode.UsageError, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("usage_error", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task JsonProper_NoCommand_EnvelopeViaWriteErrors()
    {
        // Non-bridged counterpart: with --json proper, ShellKit's WriteError emits the envelope.
        var r = await RunCliAsync(new[] { "--json" });
        Assert.Equal(ExitCode.UsageError, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal(ExitCode.UsageError, doc.RootElement.GetProperty("exit_code").GetInt32());
    }

    // --- Once-mode end-to-end ---

    [Fact]
    public async Task Once_ChildOutput_VerbatimOnStdout_ExitZero()
    {
        var r = await RunCliAsync(EchoChild("PEEP-SEAM-MARKER"));
        Assert.Equal(0, r.Exit);
        Assert.Contains("PEEP-SEAM-MARKER", r.Stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stderr);
    }

    [Fact]
    public async Task Once_ChildNonZero_ExitPassthrough()
    {
        var r = await RunCliAsync(ExitChild(7));
        Assert.Equal(7, r.Exit);
    }

    [Fact]
    public async Task Once_Json_EnvelopeOnStderr()
    {
        var r = await RunCliAsync(ExitChild(0, "--json"));
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("once", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("runs").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("history_retained").GetInt32());
    }

    [Fact]
    public async Task Once_JsonOutput_IncludesLastOutput()
    {
        var r = await RunCliAsync(
            OperatingSystem.IsWindows()
                ? new[] { "--json-output", "--once", "--", "cmd.exe", "/c", "echo CAPTURED" }
                : new[] { "--json-output", "--once", "--", "/bin/sh", "-c", "echo CAPTURED" });
        Assert.Equal(0, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Contains("CAPTURED", doc.RootElement.GetProperty("last_output").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Once_NotFound_Plain_127()
    {
        var r = await RunCliAsync(new[] { "--once", "--", NoSuchCommand });
        Assert.Equal(ExitCode.NotFound, r.Exit);
        Assert.StartsWith("peep: ", r.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Stdout);
    }

    [Fact]
    public async Task Once_NotFound_Json_Envelope127()
    {
        var r = await RunCliAsync(new[] { "--json", "--once", "--", NoSuchCommand });
        Assert.Equal(ExitCode.NotFound, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("command_not_found", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task Once_EmptyCommandToken_126_NotExecutable()
    {
        // PROBED 2026-06-06 pre-refactor: empty token routes through the typed
        // CommandNotExecutableException arm (NOT the catch-all — retry differs here):
        // plain → `peep: permission denied: `, exit 126.
        var r = await RunCliAsync(new[] { "--once", "--", "" });
        Assert.Equal(ExitCode.NotExecutable, r.Exit);
        Assert.Contains("permission denied", r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Once_EmptyCommandToken_Json_Envelope126()
    {
        // PROBED 2026-06-06: {"…","exit_code":126,"exit_reason":"command_not_executable"}.
        var r = await RunCliAsync(new[] { "--json", "--once", "--", "" });
        Assert.Equal(ExitCode.NotExecutable, r.Exit);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.Equal("command_not_executable", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    // --- Cancellation (once-mode OperationCanceledException arm → 130) ---
    // JSON-parse safety: in --json once-mode the envelope is the SOLE stderr content —
    // child output (stdout AND stderr, line-merged) goes to the captured Output written to
    // the stdout writer; the cancelled arm writes only the envelope.

    [Fact]
    public async Task Once_PreCancelledToken_130_CancelledEnvelope()
    {
        // Long child is load-bearing (phase-1 lesson): a fast child can finish before the
        // kill-on-cancel fires and the run exits 0 — a real race, not hypothetical.
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int exit = await Cli.RunAsync(SleepChild("--json"), stdout, stderr, cts.Token);
        Assert.Equal(130, exit);
        using var doc = JsonDocument.Parse(stderr.ToString());
        Assert.Equal("cancelled", doc.RootElement.GetProperty("exit_reason").GetString());
    }

    [Fact]
    public async Task Once_MidWaitCancel_130_ChildKilledPromptly()
    {
        // Exercises kill-on-cancel through the seam token (previously only reachable via
        // real Ctrl+C). Child sleeps ~10s; cancel at 300ms; a working kill path returns
        // far sooner — taking ~10s IS the failure signal.
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(300);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int exit = await Cli.RunAsync(SleepChild("--json"), stdout, stderr, cts.Token);
        sw.Stop();
        Assert.Equal(130, exit);
        using var doc = JsonDocument.Parse(stderr.ToString());
        Assert.Equal("cancelled", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8),
            $"cancel→kill took {sw.Elapsed} — child was not killed promptly");
    }
}
```

- [ ] **Step 2: Run the new tests**

Run: `dotnet test /d/projects/winix/tests/Winix.Peep.Tests/Winix.Peep.Tests.csproj --nologo -v quiet --filter "FullyQualifiedName~CliRunAsyncTests"`
Expected: all pass. The probed/pinned contracts (empty token, cancellation) are real signals if they fail — STOP and report; do not soften. For non-pinned message-wording assertions, fix the ASSERTION if wrong and record it.

- [ ] **Step 3: Full peep suite** — expect 216 + 3 skipped + 17 new ≈ 236 total, 0 failed.

- [ ] **Step 4: Commit**

```bash
git -C /d/projects/winix add tests/Winix.Peep.Tests/CliRunAsyncTests.cs
git -C /d/projects/winix commit -m "test(peep): Cli.RunAsync seam tests — validation, --json-output bridging trio, once-mode end-to-end incl. cancellation (real children)"
```

---

### Task 3: Byte-stability verification (no commit)

- [ ] **Step 1: Rebuild, capture post-refactor, diff each stream**

```bash
dotnet build /d/projects/winix/src/peep/peep.csproj --nologo -v quiet
/d/projects/winix/src/peep/bin/Debug/net10.0/peep.exe --help > /d/projects/winix/tmp/seam-baseline/peep-help-after.out 2> /d/projects/winix/tmp/seam-baseline/peep-help-after.err
/d/projects/winix/src/peep/bin/Debug/net10.0/peep.exe --describe > /d/projects/winix/tmp/seam-baseline/peep-describe-after.out 2> /d/projects/winix/tmp/seam-baseline/peep-describe-after.err
diff /d/projects/winix/tmp/seam-baseline/peep-help.out /d/projects/winix/tmp/seam-baseline/peep-help-after.out
diff /d/projects/winix/tmp/seam-baseline/peep-help.err /d/projects/winix/tmp/seam-baseline/peep-help-after.err
diff /d/projects/winix/tmp/seam-baseline/peep-describe.out /d/projects/winix/tmp/seam-baseline/peep-describe-after.out
diff /d/projects/winix/tmp/seam-baseline/peep-describe.err /d/projects/winix/tmp/seam-baseline/peep-describe-after.err
```
Expected: zero diff on all four. A line moving between `.out`/`.err` = routing regression — STOP.

- [ ] **Step 2: Manual smoke** (binary):

```bash
bash -c '/d/projects/winix/src/peep/bin/Debug/net10.0/peep.exe --once -- git status; echo EXIT=$?'
bash -c '/d/projects/winix/src/peep/bin/Debug/net10.0/peep.exe --once --json -- git status 1>/dev/null; echo EXIT=$?'
```
Expected: first → git output then EXIT=0; second → JSON envelope on stderr only, EXIT=0.

---

### Task 4: GitIgnoreChecker flake hardening (test-only rider — ADR P4)

**Files:**
- Modify: `tests/Winix.Peep.Tests/GitIgnoreCheckerTests.cs` (ONLY the `ClearCache_SubsequentQueryReEvaluates` method — this modification is explicitly authorised; the other two tests in the file are untouched)

- [ ] **Step 1: Replace the flaky test**

Replace the existing `ClearCache_SubsequentQueryReEvaluates` `[Fact]` with:

```csharp
    [SkippableFact]
    public void ClearCache_SubsequentQueryReEvaluates()
    {
        // After clearing, the same path should be re-evaluated (not served from cache).
        // We can't directly observe the cache, but we can verify the method still works
        // after clear without throwing.
        GitIgnoreChecker.ResetForTests();
        string testPath = $"clear-cache-test-{Guid.NewGuid():N}.txt";

        bool firstResult = GitIgnoreChecker.IsIgnored(testPath);
        GitIgnoreChecker.ClearCache();
        bool secondResult = GitIgnoreChecker.IsIgnored(testPath);

        // CI flake 2026-06-06 (windows-latest, 3s vs ~215ms): transient git-spawn slowness
        // engaged the process-global _gitDisabled fallback BETWEEN the two calls, making
        // them legitimately disagree (first evaluated via git; second returned the
        // disabled-fallback false). When the fallback engaged mid-test the consistency
        // assertion is INCONCLUSIVE, not failed — skip honestly. Healthy-git runs (the
        // overwhelming majority) still assert equality, so a real cache-consistency
        // regression is still caught.
        Skip.If(GitIgnoreChecker.IsDisabledForTests,
            "git transiently unavailable — _gitDisabled fallback engaged mid-test; consistency check inconclusive");

        // Both calls should return the same result for the same non-existent file
        Assert.Equal(firstResult, secondResult);
    }
```

Notes: `[SkippableFact]` (Xunit.SkippableFact, already referenced) replaces `[Fact]`; `ResetForTests()` (internal, visible via InternalsVisibleTo) clears cache AND re-arms git so the test re-attempts rather than inheriting a prior test's disable; the file's `[Collection("GitIgnoreCheckerStatic")]` serialisation is unchanged. Add `using Xunit;` already present — no new usings needed beyond what `SkippableFact` requires (it lives in the `Xunit` namespace).

- [ ] **Step 2: Run the GitIgnoreChecker tests** — `--filter "FullyQualifiedName~GitIgnoreCheckerTests"` → 3 passed (or 2 passed + 1 skipped if git is genuinely wedged — both acceptable).

- [ ] **Step 3: Commit**

```bash
git -C /d/projects/winix add tests/Winix.Peep.Tests/GitIgnoreCheckerTests.cs
git -C /d/projects/winix commit -m "test(peep): harden ClearCache consistency test against transient git-spawn fallback (CI flake 2026-06-06) — Skip.If inconclusive, no production change"
```

---

### Task 5: Wrap-up

**Files:**
- Modify: `CLAUDE.md` (one layout line)

- [ ] **Step 1: Full solution test** — `dotnet test /d/projects/winix/Winix.sln --nologo -v quiet` → 0 failures (known one-off flakes: Winix.Trash recycle-bin contention; re-run in isolation before treating as real).

- [ ] **Step 2: CLAUDE.md** — change layout line
`src/Winix.Peep/            — class library (command execution, scheduling, file watching, rendering)`
to
`src/Winix.Peep/            — class library (command execution, scheduling, file watching, rendering, Cli.RunAsync seam)`
(match the exact current text, append the seam mention).

- [ ] **Step 3: Commit + push + CI**

```bash
git -C /d/projects/winix add CLAUDE.md
git -C /d/projects/winix commit -m "docs: CLAUDE.md layout — Winix.Peep now carries the Cli.RunAsync seam"
git -C /d/projects/winix push -u origin feature/cli-seam-peep
gh workflow run ci.yml --repo Yortw/winix --ref feature/cli-seam-peep
```

---

### Task 6: Main-session verification gates (NOT for subagents)

- [ ] WSL: `wsl bash -lc "dotnet test /mnt/d/projects/winix/tests/Winix.Peep.Tests --nologo -v quiet"` — cancellation tests under Linux kill semantics are the watch items.
- [ ] Smoke fixture re-run on refreshed AOT binaries, Windows AND Linux (locate: `ls artifacts/*/peep/run-smokes.sh`; republish win-x64 from Windows, linux-x64 from inside WSL; retarget BIN/OUT via copies in tmp/, never edit the committed fixture).
- [ ] 3-OS CI green on the feature branch.
- [ ] Whole-feature fresh-eyes review (full branch diff).
- [ ] Merge `--no-ff` into `release/v0.4.0`; push; post-merge CI watch; delete feature branch; memory update (backlog 3 → 2 remaining; flake-hardening recorded).

---

## Self-review record (plan author, 2026-06-06)

- **Spec coverage:** design §seam shape → Task 1; §testing → Task 2 (validation, bridging trio, once-mode, cancellation; colour explicitly absent per ADR P3); §rider → Task 4; §verification → Tasks 0/3/5/6. ADR P1 (no key abstraction) = nothing to build — enforced by the DO-NOT-TOUCH rule. ADR P2 = Task 1 Main listing + None comment.
- **Placeholder scan:** the `…` markers in the Cli.cs skeleton are move instructions backed by the mapping table; probed contracts are pinned with probe dates; no TBDs.
- **Type consistency:** `RunAsync` signature consistent across skeleton/tests; helper names (`EchoChild`/`ExitChild`/`SleepChild`) consistent; `SessionHelpers.RequestCancellationSilently(e, cts)` signature verified against source (internal, visible to `peep` via InternalsVisibleTo — verified in csproj).
- **Verified at planning time:** empty-token probes (both modes); `InternalsVisibleTo("peep")` + `("Winix.Peep.Tests")`; SkippableFact package present; `ResetForTests`/`IsDisabledForTests` seams exist; library already references ShellKit; pre-refactor count 216+3=219; envelope-on-stderr is peep's deliberate convention (suite memory, 2026-05-10 re-classification).
