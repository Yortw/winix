# Colour Sweep — Sub-plan A (emit-fixes: trash, hcat, wargs) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `--color` actually emit ANSI in `trash`, `hcat`, and `wargs` (today they thread/claim colour but emit none), lock each with a regression test, and fix their `--color WHEN`→`--color=WHEN` doc drift.

**Architecture:** Per tool, apply the suite colour idiom (`AnsiColor.Dim/Green/Red/Yellow(useColor)` locals + `Reset`, each returning `""` when off so plain output is byte-identical) to the formatter elements each README already claims. trash + hcat already have `useColor`/`options.UseColor` plumbed (formatter just ignores it); wargs has **no** colour resolution and no `Cli.cs` seam — it needs `ResolveColor` added in `Program.cs` and a formatter-level test.

**Tech Stack:** C# / .NET 10, NativeAOT, xUnit, `Yort.ShellKit.AnsiColor`.

**Spec:** `docs/plans/2026-06-01-colour-sweep-design.md` + ADR `docs/plans/2026-06-01-colour-sweep-adr.md`.

**Test ESC literal convention (all tasks):** express the escape byte as `((char)27).ToString()` — NOT a `""`/`"\x1b"` string literal (round-trips ambiguously). Assert with `StringComparison.Ordinal`.

---

## Task 1: trash — colour the `--list` table + summary

**Files:**
- Modify: `src/Winix.Trash/Formatting.cs` (`ListTable`, `TrashSummary` gain `useColor`)
- Modify: `src/Winix.Trash/Cli.cs` (`RunList`/`RunTrash` pass `r.UseColor`; colour the per-path failure lines)
- Modify: `src/trash/README.md`, `src/trash/man/man1/trash.1` (`--color WHEN`→boolean/`=WHEN`)
- Test: `tests/Winix.Trash.Tests/ColorTests.cs` (new)

- [ ] **Step 1: Write the failing test** — `tests/Winix.Trash.Tests/ColorTests.cs`:

```csharp
#nullable enable
using System.IO;
using Winix.Trash;
using Xunit;

namespace Winix.Trash.Tests;

public class ColorTests
{
    private static readonly string Esc = ((char)27).ToString();

    // Drives the real Cli.Run path with a stubbed backend that returns one listed item,
    // so the test exercises production wiring (ArgParser.ResolveColor → r.UseColor → ListTable),
    // not a side formatter.
    private static (string outText, string errText) RunList(bool color)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        string flag = color ? "--color" : "--no-color";
        Cli.Run(new[] { "--list", flag }, so, se, backendOverride: new OneItemBackend());
        return (so.ToString(), se.ToString());
    }

    [Fact]
    public void List_WithColor_EmitsAnsi()
    {
        var (outText, _) = RunList(color: true);
        Assert.Contains(Esc, outText, System.StringComparison.Ordinal);
    }

    [Fact]
    public void List_NoColor_IsPlain()
    {
        var (outText, _) = RunList(color: false);
        Assert.DoesNotContain(Esc, outText, System.StringComparison.Ordinal);
        Assert.Contains("Name", outText, System.StringComparison.Ordinal); // header still present
    }

    [Fact]
    public void TrashSummary_ColorTogglesAnsi()
    {
        Assert.Contains(Esc, Formatting.TrashSummary(3, useColor: true), System.StringComparison.Ordinal);
        Assert.DoesNotContain(Esc, Formatting.TrashSummary(3, useColor: false), System.StringComparison.Ordinal);
    }

    // Minimal in-memory backend returning one trashed item for the --list path.
    private sealed class OneItemBackend : ITrashBackend
    {
        public TrashResult Trash(System.Collections.Generic.IReadOnlyList<string> paths)
            => throw new System.NotImplementedException();
        public System.Collections.Generic.IReadOnlyList<TrashedItem> List()
            => new[] { new TrashedItem("a.txt", "/x/a.txt", System.DateTime.UtcNow, 12, "home") };
        public EmptyResult Empty() => throw new System.NotImplementedException();
    }
}
```

> **Verify-at-implementation:** confirm the `ITrashBackend` interface method set and the `TrashedItem` constructor signature against `src/Winix.Trash` (adjust the stub to match — the names `Trash`/`List`/`Empty` and the `TrashedItem(name, originalPath, deletedUtc, size, trashLocation)` shape are from the current code but verify exact arity/types). Also confirm `Cli.Run`'s `backendOverride` parameter name.

- [ ] **Step 2: Run — expect FAIL** (compile: `TrashSummary` has no `useColor` param yet).
Run: `dotnet test tests/Winix.Trash.Tests --filter "FullyQualifiedName~ColorTests"`

- [ ] **Step 3: Add `useColor` to `Formatting.TrashSummary`**

```csharp
    /// <summary>Returns the stderr summary line for a trash operation, e.g. <c>trash: moved 3 item(s) to trash</c>.
    /// The count is rendered green when <paramref name="useColor"/> is set.</summary>
    public static string TrashSummary(int n, bool useColor)
    {
        string green = Yort.ShellKit.AnsiColor.Green(useColor);
        string reset = Yort.ShellKit.AnsiColor.Reset(useColor);
        return $"trash: moved {green}{n}{reset} item(s) to trash";
    }
```

- [ ] **Step 4: Add `useColor` to `Formatting.ListTable`** — dim the header + separator rows

Change the signature to `ListTable(IReadOnlyList<TrashedItem> items, bool useColor)`, capture the colour locals, and give `AppendRow` optional wrap params so the reset lands BEFORE the `\n` (no leak past the line). Data rows pass empty wraps (stay plain):

```csharp
    public static string ListTable(IReadOnlyList<TrashedItem> items, bool useColor)
    {
        if (items.Count == 0) { return string.Empty; }

        string dim = Yort.ShellKit.AnsiColor.Dim(useColor);
        string reset = Yort.ShellKit.AnsiColor.Reset(useColor);

        int nameW    = ColName.Length;
        int deletedW = Math.Max(ColDeleted.Length, DeletedWidth);
        int origW    = ColOriginal.Length;

        foreach (TrashedItem item in items)
        {
            if (item.Name.Length > nameW) { nameW = item.Name.Length; }
            string origCell = item.OriginalPath ?? "—";
            if (origCell.Length > origW) { origW = origCell.Length; }
        }

        var sb = new StringBuilder();

        // Header + separator dimmed; data rows plain.
        AppendRow(sb, ColName, ColDeleted, ColOriginal, nameW, deletedW, origW, dim, reset);
        AppendRow(sb,
            new string('-', nameW), new string('-', deletedW), new string('-', origW),
            nameW, deletedW, origW, dim, reset);

        foreach (TrashedItem item in items)
        {
            string deletedCell = item.DeletedUtc.HasValue
                ? item.DeletedUtc.Value.ToUniversalTime().ToString(DateFormat, CultureInfo.InvariantCulture)
                : string.Empty;
            string origCell = item.OriginalPath ?? "—";
            AppendRow(sb, item.Name, deletedCell, origCell, nameW, deletedW, origW); // plain
        }

        return sb.ToString();
    }

    // wrapPrefix/wrapSuffix wrap the whole row (before the trailing '\n') so a reset can't leak.
    private static void AppendRow(
        StringBuilder sb,
        string col1, string col2, string col3,
        int w1, int w2, int w3,
        string wrapPrefix = "", string wrapSuffix = "")
    {
        sb.Append(wrapPrefix);
        sb.Append(col1.PadRight(w1));
        sb.Append("  ");
        sb.Append(col2.PadRight(w2));
        sb.Append("  ");
        sb.Append(col3.PadRight(w3));
        sb.Append(wrapSuffix);
        sb.Append('\n');
    }
```

- [ ] **Step 5: Thread `r.UseColor` at the call sites in `Cli.cs`**

- `RunList`: `string table = Formatting.ListTable(items, r.UseColor);` — but `RunList(r, backend, stdout)` already has `r`. ✓
- `RunTrash`: `stderr.WriteLine(Formatting.TrashSummary(result.SuccessCount, r.UseColor));` and colour the per-path failure lines red:
```csharp
                if (!outcome.Succeeded)
                {
                    string red = Yort.ShellKit.AnsiColor.Red(r.UseColor);
                    string reset = Yort.ShellKit.AnsiColor.Reset(r.UseColor);
                    stderr.WriteLine($"trash: {red}{outcome.Path}: {outcome.Error}{reset}");
                }
```

- [ ] **Step 6: Run — expect PASS.** Then full `dotnet test tests/Winix.Trash.Tests`.

- [ ] **Step 7: Fix trash docs** — `src/trash/README.md` and `src/trash/man/man1/trash.1`

In the README options table, replace the `--color WHEN` row + the `--no-color` "Equivalent to --color never" line with:
```
| `--color[=auto\|always\|never]` | | Force or suppress coloured output. Bare `--color` = always. |
| `--no-color` | | Disable coloured output. Respects `NO_COLOR`. |
```
In `trash.1`, replace the `--color WHEN` / auto-always-never groff block with a `--color[=auto|always|never]` description + `--no-color`. (Mirror the corrected demux man-page form.)

- [ ] **Step 8: Build solution + commit**

Run: `dotnet build Winix.sln` (0 warnings).
```bash
git add src/Winix.Trash/Formatting.cs src/Winix.Trash/Cli.cs tests/Winix.Trash.Tests/ColorTests.cs src/trash/README.md src/trash/man/man1/trash.1
git commit -m "feat(trash): emit colour in --list table + summary; regression test; --color=when docs"
```

---

## Task 2: hcat — colour the banner + request-log

**Files:**
- Modify: `src/Winix.HCat/Banner.cs` (`Render` gains `useColor`)
- Modify: `src/Winix.HCat/CaptureLifecycle.cs` (human log lines coloured by status class)
- Modify: call sites (`HCatServer.cs` banner write; `CaptureLifecycle` ctor) to pass `options.UseColor`
- Modify: `src/hcat/README.md`, `src/hcat/man/man1/hcat.1` (`--color WHEN`→`=WHEN`)
- Test: `tests/Winix.HCat.Tests/ColorTests.cs` (new)

- [ ] **Step 1: Write the failing test** — `tests/Winix.HCat.Tests/ColorTests.cs`:

```csharp
#nullable enable
using System.IO;
using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class ColorTests
{
    private static readonly string Esc = ((char)27).ToString();

    [Fact]
    public void Banner_WithColor_EmitsAnsi_WithoutIsPlain()
    {
        var info = new BindInfo(new[] { "http://127.0.0.1:8080/" }, exposed: false);
        var options = new HCatOptions { Mode = HCatMode.Serve, Directory = ".", Upload = false };

        string colored = Banner.Render(info, options, qr: null, useColor: true);
        string plain = Banner.Render(info, options, qr: null, useColor: false);

        Assert.Contains(Esc, colored, System.StringComparison.Ordinal);
        Assert.DoesNotContain(Esc, plain, System.StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", plain, System.StringComparison.Ordinal);
    }

    [Fact]
    public void RequestLog_ColorsStatusByClass()
    {
        var sink = new StringWriter();
        var lifecycle = new CaptureLifecycle(new CaptureController(null, null), jsonSink: null,
                                             humanSink: sink, useColor: true);
        lifecycle.OnServeAccess(new RequestRecord("GET", "/ok", null, null, System.DateTimeOffset.UtcNow), status: 200);
        lifecycle.OnServeAccess(new RequestRecord("GET", "/bad", null, null, System.DateTimeOffset.UtcNow), status: 500);

        string log = sink.ToString();
        Assert.Contains(Esc, log, System.StringComparison.Ordinal);
        Assert.Contains("/ok", log, System.StringComparison.Ordinal);
        Assert.Contains("/bad", log, System.StringComparison.Ordinal);
    }

    [Fact]
    public void RequestLog_NoColor_IsPlain()
    {
        var sink = new StringWriter();
        var lifecycle = new CaptureLifecycle(new CaptureController(null, null), jsonSink: null,
                                             humanSink: sink, useColor: false);
        lifecycle.OnServeAccess(new RequestRecord("GET", "/x", null, null, System.DateTimeOffset.UtcNow), status: 200);
        Assert.DoesNotContain(Esc, sink.ToString(), System.StringComparison.Ordinal);
    }
}
```

> **Verify-at-implementation:** confirm exact constructors — `BindInfo(urls, exposed)`, `HCatOptions { … }` initialisable shape, `RequestRecord(method, path, query, remote, timestamp)` arity/types, and `CaptureController` ctor. Adjust the test literals to the real signatures (read `BindInfo.cs`, `RequestRecord.cs`, `CaptureController.cs`). The colour CONTRACT (ESC iff useColor; status visible) is what matters; fix the constructor calls to compile.

- [ ] **Step 2: Run — expect FAIL** (compile: `Render`/`CaptureLifecycle` have no `useColor`).

- [ ] **Step 3: Add `useColor` to `Banner.Render`**

Signature → `Render(BindInfo info, HCatOptions options, string? qr, bool useColor)`. Capture `dim`/`yellow`/`cyan`/`reset` from `AnsiColor`. Colour: the `Serving …` line label dim; each URL cyan; the `(localhost only …)` hint dim; the `⚠ uploads …` warning yellow. Plain output (useColor false) byte-identical to today.

> **Verify-at-implementation:** there are TWO call sites that matter — `HCatServer.cs:307` `Banner.Render(bind, options, qr: RenderQr(bind))` and any test/other caller. Update them to pass `options.UseColor`.

- [ ] **Step 4: Add `useColor` to `CaptureLifecycle`** — colour the human log lines

Add a `bool useColor = false` ctor param (stored). In `OnRecord`'s human branch and `OnServeAccess`'s human branch, colour: method dim; status by class — 2xx green, 3xx cyan, 4xx yellow, 5xx red — via a private helper:
```csharp
    private string ColorStatus(int status)
    {
        if (!_useColor) { return status.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        string code =
            status >= 500 ? Yort.ShellKit.AnsiColor.Red(true) :
            status >= 400 ? Yort.ShellKit.AnsiColor.Yellow(true) :
            status >= 300 ? Yort.ShellKit.AnsiColor.Cyan(true) :
                            Yort.ShellKit.AnsiColor.Green(true);
        return $"{code}{status.ToString(System.Globalization.CultureInfo.InvariantCulture)}{Yort.ShellKit.AnsiColor.Reset(true)}";
    }
```
Use it in `OnServeAccess`'s human line: `$"{dim}{record.Method}{reset} {record.Path} {ColorStatus(status)}"`. `OnRecord` (no status) colours just the method dim.

> **Verify-at-implementation:** thread `options.UseColor` to the `CaptureLifecycle` ctor at its construction site (grep `new CaptureLifecycle(` in `HCatServer.cs`). The JSONL branches must stay byte-identical (never colour JSON).

- [ ] **Step 5: Run — expect PASS.** Then full `dotnet test tests/Winix.HCat.Tests`.

- [ ] **Step 6: Fix hcat docs** — `src/hcat/README.md` + `src/hcat/man/man1/hcat.1`: `--color WHEN`→`--color[=auto|always|never]` + `--no-color`, mirroring Task 1 Step 7.

- [ ] **Step 7: Build + commit**
```bash
git add src/Winix.HCat/Banner.cs src/Winix.HCat/CaptureLifecycle.cs src/Winix.HCat/HCatServer.cs tests/Winix.HCat.Tests/ColorTests.cs src/hcat/README.md src/hcat/man/man1/hcat.1
git commit -m "feat(hcat): emit colour in banner + request-log; regression test; --color=when docs"
```

---

## Task 3: wargs — add colour support (from scratch) to the failure summary

**Files:**
- Modify: `src/Winix.Wargs/Formatting.cs` (`FormatHumanSummary` gains `useColor`)
- Modify: `src/wargs/Program.cs` (resolve `useColor` via `ResolveColor(checkStdErr: true)`; pass it)
- Modify: `src/wargs/README.md`, `src/wargs/man/man1/wargs.1` (align Colour section to `--color[=auto|always|never]`)
- Test: `tests/Winix.Wargs.Tests/ColorTests.cs` (new — formatter-level; wargs has no `Cli.Run` seam)

- [ ] **Step 1: Write the failing test** — `tests/Winix.Wargs.Tests/ColorTests.cs`:

```csharp
#nullable enable
using System.Collections.Generic;
using Winix.Wargs;
using Xunit;

namespace Winix.Wargs.Tests;

public class ColorTests
{
    private static readonly string Esc = ((char)27).ToString();

    private static WargsResult Failed()
        => new WargsResult(totalJobs: 3, succeeded: 1, failed: 2, skipped: 0,
                           wallTime: System.TimeSpan.Zero, jobs: new List<JobResult>());

    [Fact]
    public void FailureSummary_WithColor_EmitsAnsi()
    {
        string? s = Formatting.FormatHumanSummary(Failed(), useColor: true);
        Assert.NotNull(s);
        Assert.Contains(Esc, s!, System.StringComparison.Ordinal);
    }

    [Fact]
    public void FailureSummary_NoColor_IsPlain()
    {
        string? s = Formatting.FormatHumanSummary(Failed(), useColor: false);
        Assert.NotNull(s);
        Assert.DoesNotContain(Esc, s!, System.StringComparison.Ordinal);
        Assert.Contains("2/3 jobs failed", s!, System.StringComparison.Ordinal);
    }

    [Fact]
    public void NoFailures_ReturnsNull()
    {
        var ok = new WargsResult(1, 1, 0, 0, System.TimeSpan.Zero, new List<JobResult>());
        Assert.Null(Formatting.FormatHumanSummary(ok, useColor: true));
    }
}
```

> **Verify-at-implementation:** confirm the `WargsResult` constructor arity/parameter order against `src/Winix.Wargs` (the `(totalJobs, succeeded, failed, skipped, wallTime, jobs)` shape is from `Program.cs` usage; verify). Test is formatter-level because wargs's orchestration is in `src/wargs/Program.cs` with no library `Cli.Run` seam (architectural finding) — the renderer-level fallback from the design §2.

- [ ] **Step 2: Run — expect FAIL** (compile: `FormatHumanSummary` has no `useColor`).

- [ ] **Step 3: Add `useColor` to `FormatHumanSummary`** — red failure summary

```csharp
    public static string? FormatHumanSummary(WargsResult result, bool useColor)
    {
        if (result.Failed == 0) { return null; }
        string red = Yort.ShellKit.AnsiColor.Red(useColor);
        string reset = Yort.ShellKit.AnsiColor.Reset(useColor);
        return $"wargs: {red}{result.Failed}/{result.TotalJobs} jobs failed{reset}";
    }
```

- [ ] **Step 4: Resolve + pass `useColor` in `src/wargs/Program.cs`**

wargs builds a `CommandLineParser` (Program.cs ~line 148) and gets a `ParseResult`. After the successful parse, resolve colour for stderr (the summary goes to stderr): `bool useColor = parseResult.ResolveColor(checkStdErr: true);` and pass it to the `FormatHumanSummary` call at line 573: `string? summary = Formatting.FormatHumanSummary(wargsResult, useColor);`.

> **Verify-at-implementation:** locate the `ParseResult` variable in `Program.cs` (it's the result of `.Parse(args)`). Resolve `useColor` once after parse-success and before the summary emission; thread it to line 573. Confirm `--color`/`--no-color`/`--json` interaction — wargs forbids `--verbose`/`--confirm` with `--json`; `--color` has no such restriction. If `--json`/`--ndjson` is active the human summary isn't emitted, so colour only applies to the plaintext summary path.

- [ ] **Step 5: Run — expect PASS.** Then full `dotnet test tests/Winix.Wargs.Tests`.

- [ ] **Step 6: Align wargs docs** — `src/wargs/README.md` + `src/wargs/man/man1/wargs.1` Colour section: ensure `--color` is shown as `--color[=auto|always|never]` (bare = always) + `--no-color`; the wargs README "## Colour" section should describe the now-real behaviour (failure summary coloured on a terminal).

- [ ] **Step 7: Build + commit**
```bash
git add src/Winix.Wargs/Formatting.cs src/wargs/Program.cs tests/Winix.Wargs.Tests/ColorTests.cs src/wargs/README.md src/wargs/man/man1/wargs.1
git commit -m "feat(wargs): resolve + emit colour in failure summary (was unwired); regression test; docs"
```

---

## Task 4: Final verification

- [ ] **Step 1: Full solution build** — `dotnet build Winix.sln` → 0 warnings.
- [ ] **Step 2: Full solution test** — `dotnet test Winix.sln` → 0 failed (modulo the known parallel-run `IsOnPath_DoesNotSpawnProcess` flake; if it fails, re-run `tests/Winix.Winix.Tests` in isolation to confirm it passes alone).
- [ ] **Step 3: AOT smoke each tool** — publish + run with `--color=always` (expect ANSI) and `--no-color` (expect none):
  - `trash --list --color=always` vs `--no-color` (needs items in the bin, or accept empty-table output).
  - `hcat` — banner: start serve briefly with `--color=always` and confirm ANSI in the banner (or rely on the unit test; serve is long-running).
  - `wargs` — run a failing job with `--color=always` and confirm the red summary; `--no-color` plain.
  Capture to `artifacts/colour-sweep-A/` (gitignored).
- [ ] **Step 4: Confirm plain output unchanged** — for each tool, `--no-color` output is byte-identical to pre-change (the `AnsiColor.X(false)=""` guarantee). Spot-check trash `--list` and wargs summary.

## Notes / known plan risks (resolve during execution)

- **Constructor/interface signatures** in the tests (ITrashBackend, TrashedItem, BindInfo, HCatOptions, RequestRecord, CaptureController, WargsResult) are transcribed from current usage but MUST be verified against the actual source when each task runs — adjust to compile; the colour CONTRACT (ESC iff useColor) is the invariant, not the literal constructor shape.
- **wargs is the heaviest task** — it adds colour resolution that doesn't exist today (no `ResolveColor` call, no `Cli.cs`); the test is formatter-level by necessity.
- **hcat banner is long-running to smoke** — the unit test (Banner.Render + CaptureLifecycle) is the primary guard; the AOT serve smoke is best-effort.
- Each tool's `--no-color` / non-TTY output MUST stay byte-identical to today (plain). The `AnsiColor.X(false)` returning `""` guarantees this — but the table-row wrapping in trash Task 1 Step 4 must place reset before `\n` so no stray bytes leak when colour is off (empty strings) or on.

---

## Adversarial Review Integration — Pass 1 (2026-06-01)

Fresh-subagent review against the 15-category taxonomy: **2 blockers, 4 test gaps, 2 explicit defers**. Apply ALL of the following during the relevant task.

| ID | Bucket | Disposition |
|---|---|---|
| **A1** no blanket caller-audit for 5 changed signatures (breaks existing callers AND existing tests) | Plan blocker | **Fixed:** each task gains a **Step 0 caller-audit** (below). |
| **A2** wargs Program.cs resolve→thread wiring untested (formatter-only) — the unwired-caller class the sweep kills | Plan blocker | **Fixed:** Task 3 adds a minimal `EmitHumanSummary` seam + a test that drives resolve→thread (below). |
| **A3** "no ESC when off" ≠ plain output unchanged | Test gap | **Fixed:** each tool's tests add an **exact-plain-string** assertion (below). |
| **A4** hcat `ColorStatus` status-class boundaries untested | Test gap | **Fixed:** Task 2 adds a status-class `[Theory]` (below). |
| **A5** trash/hcat colour-on tests use bare `--color` (auto-detect variable) | Test gap | **Fixed:** use `--color=always` in colour-on tests (bare→always is correct, but explicit removes the variable). |
| **A6** CaptureLifecycle colour must not change the lock scope | Test gap→note | **Fixed:** Task 2 Step 4 note (below); concurrency repro deferred. |
| **A6-DEFER** concurrent-write integration test for hcat coloured log | Explicit defer | ADR deferred (rests on `_useColor` immutability + unchanged lock scope). |
| **A7-DEFER** hcat banner AOT serve smoke best-effort | Explicit defer | ADR deferred (unit test is the primary guard). |

### A1 — add as **Step 0** of every task
- [ ] **Step 0: Caller audit.** Grep `src/` AND `tests/` for callers of the signature(s) this task changes (`TrashSummary(` / `ListTable(` for Task 1; `Banner.Render(` / `new CaptureLifecycle(` for Task 2; `FormatHumanSummary(` for Task 3). Update **every** call site: production passes the resolved `useColor`; **pre-existing tests pass `useColor: false`** (preserving their asserted plain output). Confirm the test project compiles (`dotnet build`) before running the new colour tests. A missed caller is a hard compile break.

### A2 — Task 3: minimal `EmitHumanSummary` seam (verifies the wiring)
Add to `src/Winix.Wargs/` an internal seam so the resolve→thread→emit chain is testable without a full `Program.cs` refactor or process spawn:
```csharp
namespace Winix.Wargs;

public static class HumanSummary
{
    /// <summary>Resolves colour from the parsed args (stderr stream) and writes the human failure
    /// summary (if any) to <paramref name="stderr"/>. Extracted from Program.cs so the
    /// resolve→colour→emit wiring is testable (wargs has no Cli.Run seam).</summary>
    public static void Emit(Yort.ShellKit.ParseResult parsed, WargsResult result, System.IO.TextWriter stderr)
    {
        bool useColor = parsed.ResolveColor(checkStdErr: true);
        string? summary = Formatting.FormatHumanSummary(result, useColor);
        if (summary is not null) { stderr.WriteLine(summary); }
    }
}
```
`Program.cs` Step 4 then calls `HumanSummary.Emit(parseResult, wargsResult, Console.Error);` instead of inlining the resolve+format+write. Test (`tests/Winix.Wargs.Tests/ColorTests.cs`) drives the wiring:
```csharp
    private static readonly string Esc = ((char)27).ToString();

    private static Yort.ShellKit.ParseResult ParsedWith(string colorFlag)
        => /* build the SAME CommandLineParser wargs builds (StandardFlags + wargs flags), then .Parse(new[]{colorFlag}) */
           BuildWargsParser().Parse(new[] { colorFlag });

    [Fact]
    public void Emit_ColorAlways_WritesAnsiSummary()
    {
        var sw = new System.IO.StringWriter();
        HumanSummary.Emit(ParsedWith("--color=always"), Failed(), sw);
        Assert.Contains(Esc, sw.ToString(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_NoColor_WritesPlainSummary()
    {
        var sw = new System.IO.StringWriter();
        HumanSummary.Emit(ParsedWith("--no-color"), Failed(), sw);
        Assert.Equal("wargs: 2/3 jobs failed" + System.Environment.NewLine, sw.ToString()); // exact plain (A3)
        Assert.DoesNotContain(Esc, sw.ToString(), System.StringComparison.Ordinal);
    }
```
> **Verify-at-implementation:** `BuildWargsParser()` must construct the parser the SAME way `Program.cs` does (so `--color` is a registered optional-value flag via `StandardFlags`). Either expose Program.cs's parser-builder as an internal static method and call it from both Program.cs and the test, OR (simpler) the test builds a `new CommandLineParser("wargs", "0.0.0").StandardFlags()` — sufficient since only `--color`/`--no-color` resolution is under test. Confirm `WargsResult` ctor arity. This seam closes the A2 blocker: a regression where Program.cs passes `false` or doesn't wire `useColor` now fails `Emit_ColorAlways_WritesAnsiSummary`.

### A3 — exact-plain-string assertions (each tool)
- **trash** Task 1: in `List_NoColor_IsPlain`, additionally `Assert.Equal(<pinned plain table string>, outText.Replace("\r\n","\n"))` — capture the exact expected table once and pin it. And `Assert.Equal("trash: moved 3 item(s) to trash", Formatting.TrashSummary(3, useColor: false))`.
- **hcat** Task 2: assert the exact plain banner line(s) for `useColor:false` and the exact plain log line `"GET /x 200"` for the no-colour `OnServeAccess`.
- **wargs** Task 3: the `Emit_NoColor_WritesPlainSummary` exact-equal above + `Assert.Equal("wargs: 2/3 jobs failed", Formatting.FormatHumanSummary(Failed(), useColor:false))`.

### A4 — Task 2: hcat status-class `[Theory]`
```csharp
    [Theory]
    [InlineData(200)] [InlineData(204)] [InlineData(301)] [InlineData(404)] [InlineData(500)] [InlineData(503)]
    public void RequestLog_StatusClasses_AllColoredOn(int status)
    {
        var sink = new System.IO.StringWriter();
        var lifecycle = new CaptureLifecycle(new CaptureController(null, null), jsonSink: null, humanSink: sink, useColor: true);
        lifecycle.OnServeAccess(new RequestRecord("GET", "/p", null, null, System.DateTimeOffset.UtcNow), status);
        Assert.Contains(((char)27).ToString(), sink.ToString(), System.StringComparison.Ordinal);
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), sink.ToString(), System.StringComparison.Ordinal);
    }
```
(Exercises 2xx/3xx/4xx/5xx; the status text must always be present regardless of colour.)

### A5 — colour-on tests use `--color=always`
In Task 1 `RunList(color:true)` use `"--color=always"`; in Task 2 the banner colour-on test already calls `Render(..., useColor:true)` directly (no flag) — fine. Anywhere a flag string drives colour-on, use `--color=always`, not bare `--color`.

### A6 — Task 2 Step 4 note
Add: "Build the coloured log line as a local string, then pass it to the existing `WriteLineLocked` path **unchanged** — do not move work into/out of the lock scope. `_useColor` is set once in the ctor (immutable), so `ColorStatus` is thread-safe." No concurrency test (deferred, A6-DEFER).

---

## Adversarial Review Integration — Pass 2 (confirming, 2026-06-01)

Second fresh subagent confirmed the architecture is sound and the pass-1 integration is internally consistent (A1 Step-0 on all signature-changing tasks; A3 newline distinction correct; A4/A5/A6 sane). **0 new blockers, 2 new test gaps**, both fixed below. (Two passes run — skill maximum. Proceed to execution.)

| ID | Bucket | Disposition |
|---|---|---|
| **P2-A** residual A2: nothing asserts `Program.cs` actually *calls* `HumanSummary.Emit` (the seam could be tested-but-dead — the unwired class one level up) | Test gap | **Fixed:** Task 3 Step 4 note (below) — Program.cs MUST emit via `HumanSummary.Emit`; Step-0 grep confirms it's the only `FormatHumanSummary(` caller outside the formatter+tests. |
| **P2-B** two added colour branches untested: trash per-path failure line (red), hcat `OnRecord` dim-method line | Test gap | **Fixed:** one test each (below). |
| (minor) `HumanSummary` prose says "internal seam" but code declares `public static class` | — | Keep it `public` (consistent with the other `public static` formatters); read the A2 prose "internal seam" as "a seam," not an access modifier. |

### P2-A — Task 3 Step 4 note
Add: "**Program.cs MUST emit the human summary via `HumanSummary.Emit(parseResult, wargsResult, Console.Error)`** — do NOT inline `FormatHumanSummary(...)` directly. After the change, the Step-0 caller-audit grep for `FormatHumanSummary(` must find exactly two callers: inside `HumanSummary.Emit` and inside the tests. Any direct `FormatHumanSummary(` call left in `Program.cs` means the seam test is dead and the production path is unguarded — re-route it through `Emit`."

### P2-B — two added-branch tests

**trash** — add to `tests/Winix.Trash.Tests/ColorTests.cs` (drives `RunTrash`'s red per-path failure line via a backend that fails one path):
```csharp
    [Fact]
    public void TrashFailureLine_ColorTogglesAnsi()
    {
        var seColor = new StringWriter();
        Cli.Run(new[] { "x.txt", "--color=always" }, new StringWriter(), seColor, backendOverride: new OneFailBackend());
        Assert.Contains(Esc, seColor.ToString(), System.StringComparison.Ordinal);

        var sePlain = new StringWriter();
        Cli.Run(new[] { "x.txt", "--no-color" }, new StringWriter(), sePlain, backendOverride: new OneFailBackend());
        Assert.DoesNotContain(Esc, sePlain.ToString(), System.StringComparison.Ordinal);
        Assert.Contains("x.txt", sePlain.ToString(), System.StringComparison.Ordinal); // path still present
    }

    // Backend whose Trash returns one FAILED outcome (drives the red per-path failure line).
    private sealed class OneFailBackend : ITrashBackend
    {
        public TrashResult Trash(System.Collections.Generic.IReadOnlyList<string> paths)
            => /* a TrashResult with SuccessCount 0 and one failed PathOutcome(path: "x.txt", error: "no such file") */
               throw new System.NotImplementedException(); // VERIFY: build the real TrashResult/PathOutcome shape
        public System.Collections.Generic.IReadOnlyList<TrashedItem> List() => System.Array.Empty<TrashedItem>();
        public EmptyResult Empty() => throw new System.NotImplementedException();
    }
```
> **Verify-at-implementation:** construct a real `TrashResult` with one failed `PathOutcome` (the `OneFailBackend.Trash` body) — read `TrashResult`/`PathOutcome` ctors. The contract: a failed trash op writes a red failure line to stderr when colour is on, plain (path still present) when off.

**hcat** — add to `tests/Winix.HCat.Tests/ColorTests.cs` (drives `OnRecord`, the non-serve per-request line):
```csharp
    [Fact]
    public void OnRecord_MethodColoredOn_PlainOff()
    {
        var on = new StringWriter();
        new CaptureLifecycle(new CaptureController(null, null), jsonSink: null, humanSink: on, useColor: true)
            .OnRecord(new RequestRecord("POST", "/submit", null, null, System.DateTimeOffset.UtcNow));
        Assert.Contains(Esc, on.ToString(), System.StringComparison.Ordinal);
        Assert.Contains("/submit", on.ToString(), System.StringComparison.Ordinal);

        var off = new StringWriter();
        new CaptureLifecycle(new CaptureController(null, null), jsonSink: null, humanSink: off, useColor: false)
            .OnRecord(new RequestRecord("POST", "/submit", null, null, System.DateTimeOffset.UtcNow));
        Assert.DoesNotContain(Esc, off.ToString(), System.StringComparison.Ordinal);
        Assert.Equal("POST /submit" + System.Environment.NewLine, off.ToString()); // exact plain (A3)
    }
```
> **Verify-at-implementation:** confirm `OnRecord`'s plain human line is exactly `"{Method} {Path}"` (per CaptureLifecycle today) so the exact-equal holds; adjust the literal if the format differs.

**Review status: COMPLETE** (two passes; architecture converged at pass 1; pass 2 added two branch tests + a wiring-assertion note). Proceed to execution.
