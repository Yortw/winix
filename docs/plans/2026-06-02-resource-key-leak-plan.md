# Resource-Key Leak Remediation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the framework-exception resource-key leak across the Winix suite, make it reproducible in tests, and prevent recurrence.

**Architecture:** A shared `Yort.ShellKit.SafeError.Describe(ex)` helper type-maps common CoreLib exceptions to stable English (never returns `ex.Message`). LEAK sites route through it (hybrid: genuinely-good bespoke text stays). Test csprojs mirror the app's `UseSystemResourceKeys=true` so the leak reproduces under `dotnet test`. The 4 v0.4 tools get native smoke fixtures + workflow entries. CLAUDE.md codifies the convention.

**Tech Stack:** .NET 10, C#, xUnit, NativeAOT, ShellKit `CommandLineParser`.

**Design:** `docs/plans/2026-06-02-resource-key-leak-design.md` · **ADR:** `docs/plans/2026-06-02-resource-key-leak-adr.md`

---

## File Structure

- **Create** `src/Yort.ShellKit/SafeError.cs` — the helper (one responsibility: exception → readable text).
- **Create** `tests/Yort.ShellKit.Tests/SafeErrorTests.cs` — exhaustive helper unit tests.
- **Modify** `tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj` — add `UseSystemResourceKeys=true`.
- **Modify** ~24 `tests/Winix.*.Tests/*.csproj` — add `UseSystemResourceKeys=true`.
- **Modify** ~18 tool source files — route LEAK sites through `SafeError.Describe` (see table).
- **Modify/Create** ~per-tool regression test files — one test per distinct leak-class per tool.
- **Create** `artifacts/v0.4-smoke/{demux,mksecret,trash,hcat}/run-smokes.sh` — native capability fixtures.
- **Modify** `.github/workflows/manual-smoke.yml` — add the 4 tools to the list, runner map, and sed retarget rules.
- **Modify** `CLAUDE.md` — new-tool checklist additions.

---

## Canonical recipe (applies to every sweep task in Phase 2)

**The leak-fix substitution** — at each LEAK site listed in the table:

- Raw-message form: `stderr.WriteLine($"tool: ... {ex.Message}")` → `stderr.WriteLine($"tool: ... {SafeError.Describe(ex)}")`.
- Embedded-in-wrapper form (`new XException($"...{inner.Message}", inner)`) → `new XException($"...{SafeError.Describe(inner)}", inner)`.
- Add `using Yort.ShellKit;` if absent.
- Do **not** touch ACCEPTABLE sites (already have `ex.GetType().Name`) or SAFE sites (project exceptions / native-OS messages). Only the table's sites.

**The regression test pattern** — for each distinct leak-class in the tool, through the production seam:

```csharp
private static readonly string LeakKeyFragment = "<the SR key, e.g. IO_PathNotFound_Path or MakeException>";

[Fact]
public void <ErrorPath>_MessageIsReadable_NoResourceKeyLeak()
{
    var (/* code */, err) = /* drive the real Cli.Run / library seam to hit this error path */;
    Assert.DoesNotContain(LeakKeyFragment, err, System.StringComparison.Ordinal);
    Assert.Contains("<clean English fragment the fix produces>", err, System.StringComparison.Ordinal);
}
```

The test MUST fail (red) before the fix when run under the mirrored flag — confirm by running it pre-fix. If a site cannot be triggered deterministically through a seam (network/walk-loop internals), note that in the commit message and rely on the SafeError unit tests + code inspection (do NOT write an `Assert.True(true)` placeholder).

**Per-tool task shape (every Phase-2 task):**
1. Add `<UseSystemResourceKeys>true</UseSystemResourceKeys>` to the tool's test csproj.
2. Run the tool's existing tests; fix any that break by asserting framework English (update the assertion text only — preserve intent).
3. For each distinct leak-class: write the failing regression test (run, confirm red).
4. Apply the recipe substitution at that class's site(s).
5. Run tests; confirm green + whole test project green.
6. Commit (`fix(<tool>): readable error text on <paths> via SafeError; test csproj mirrors UseSystemResourceKeys`).

---

## Task 0: `SafeError` helper (ShellKit)

**Files:**
- Create: `src/Yort.ShellKit/SafeError.cs`
- Create: `tests/Yort.ShellKit.Tests/SafeErrorTests.cs`
- Modify: `tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj`

- [ ] **Step 1: Add the resource-key flag to the ShellKit test csproj**

In `tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj`, inside the first `<PropertyGroup>` (after `<IsTestProject>true</IsTestProject>`), add:

```xml
    <!-- Mirror the AOT apps' UseSystemResourceKeys=true so SafeError's contract
         (never emit SR resource keys) is exercised under the real runtime condition. -->
    <UseSystemResourceKeys>true</UseSystemResourceKeys>
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Yort.ShellKit.Tests/SafeErrorTests.cs`:

```csharp
#nullable enable
using System;
using System.IO;
using System.Text.RegularExpressions;
using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class SafeErrorTests
{
    [Fact]
    public void DirectoryNotFound_MapsToReadableText()
    {
        string s = SafeError.Describe(new DirectoryNotFoundException("IO_PathNotFound_Path"));
        Assert.Equal("no such directory", s);
    }

    [Fact]
    public void FileNotFound_MapsToReadableText()
    {
        Assert.Equal("no such file", SafeError.Describe(new FileNotFoundException()));
    }

    [Fact]
    public void UnauthorizedAccess_MapsToReadableText()
    {
        Assert.Equal("access denied", SafeError.Describe(new UnauthorizedAccessException()));
    }

    [Fact]
    public void PathTooLong_MapsToReadableText()
    {
        Assert.Equal("path too long", SafeError.Describe(new PathTooLongException()));
    }

    [Fact]
    public void RegexParse_UsesErrorAndOffset_NotMessage()
    {
        RegexParseException rex;
        try { _ = new Regex("("); throw new InvalidOperationException("unreachable"); }
        catch (RegexParseException ex) { rex = ex; }

        string s = SafeError.Describe(rex);
        // Built from .Error/.Offset (invariant-stable), never the SR-keyed .Message.
        Assert.DoesNotContain("MakeException", s, StringComparison.Ordinal);
        Assert.Contains("offset", s, StringComparison.Ordinal);
        Assert.Contains(rex.Error.ToString(), s, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownException_FallsBackToTypeName_NotMessage()
    {
        // A bespoke exception whose Message would be an SR key if piped raw.
        string s = SafeError.Describe(new InvalidOperationException("some-internal-key"));
        Assert.Equal(nameof(InvalidOperationException), s);
    }

    // F1: the helper must NEVER throw — it runs inside the error-reporting path. A null inner
    // (common for wrapper exceptions built with a null inner) must not NRE.
    [Fact]
    public void Null_ReturnsPlaceholder_DoesNotThrow()
    {
        Assert.Equal("unknown error", SafeError.Describe(null));
    }

    // F11: aggregate/wrapper exceptions must describe the real cause, not the wrapper type.
    [Fact]
    public void Aggregate_UnwrapsToInnerCause()
    {
        var agg = new AggregateException(new DirectoryNotFoundException());
        Assert.Equal("no such directory", SafeError.Describe(agg));
    }

    // F4: JSON parse errors must keep their location, not collapse to "JsonException".
    [Fact]
    public void JsonException_KeepsLocation_NotMessage()
    {
        System.Text.Json.JsonException jex;
        try { System.Text.Json.JsonSerializer.Deserialize<int>("{bad"); throw new InvalidOperationException("unreachable"); }
        catch (System.Text.Json.JsonException ex) { jex = ex; }
        string s = SafeError.Describe(jex);
        Assert.Contains("JSON", s, StringComparison.Ordinal);
        Assert.Contains("line", s, StringComparison.Ordinal); // location preserved
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --filter "FullyQualifiedName~SafeErrorTests" --nologo`
Expected: FAIL — `SafeError` does not exist (compile error).

- [ ] **Step 4: Implement `SafeError`**

Create `src/Yort.ShellKit/SafeError.cs`:

```csharp
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Yort.ShellKit;

/// <summary>
/// Produces stable, human-readable text for an exception destined for user output.
/// </summary>
/// <remarks>
/// Winix tools publish with <c>UseSystemResourceKeys=true</c> (a NativeAOT/trim size
/// optimisation), under which framework exception <see cref="Exception.Message"/> returns the
/// bare CoreLib resource key (e.g. <c>IO_PathNotFound_Path</c>) rather than English. This helper
/// NEVER returns <c>ex.Message</c>: it type-maps the common offenders to project-controlled English
/// and falls back to the exception's type name (context without a leaked key).
/// </remarks>
public static class SafeError
{
    /// <summary>Returns a readable, resource-key-free description of <paramref name="ex"/>.
    /// Null-safe: this runs inside error-reporting paths and must never throw.</summary>
    public static string Describe(Exception? ex)
    {
        if (ex is null) { return "unknown error"; } // F1: never NRE inside the error path.

        return ex switch
        {
            // F11: unwrap aggregate/wrapper so the real cause is described, not the wrapper type.
            AggregateException agg when agg.InnerException is not null => Describe(agg.InnerException),
            DirectoryNotFoundException => "no such directory",
            FileNotFoundException => "no such file",
            UnauthorizedAccessException => "access denied",
            PathTooLongException => "path too long",
            // RegexParseException.Error/.Offset are an enum + int — VERIFY at implementation that they
            // are invariant-stable under the mirrored flag (probe). The enum-name form
            // ("InsufficientClosingParentheses at offset N") matches the already-shipped demux fix
            // (edc7262), so the enum name is the accepted user-facing text. (F6)
            RegexParseException rex => $"{rex.Error} at offset {rex.Offset}",
            // F4: System.Text.Json carries structured location; keep it (Message would be an SR key).
            System.Text.Json.JsonException jex when jex.LineNumber is long line => $"invalid JSON at line {line + 1}",
            System.Text.Json.JsonException => "invalid JSON",
            // Unknown: the type name gives the user context to act/report; the message might be a key.
            // Documented limitation (ADR): broad-catch sites get type-name only; recover full detail
            // by re-running under JIT / non-AOT where Message resolves to English.
            _ => ex.GetType().Name,
        };
    }
}
```

> **F6 note for implementer:** before finalising the regex test's expected fragment, run a 10-line probe under `UseSystemResourceKeys=true` confirming `rex.Error.ToString()` and `rex.Offset` are stable (they are not SR-resolved). The demux native binary already emits this exact form, so the format is pre-validated — this just confirms stability for the helper's own test.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --filter "FullyQualifiedName~SafeErrorTests" --nologo`
Expected: PASS (all 6).

- [ ] **Step 6: Run the full ShellKit test project**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --nologo`
Expected: PASS — adding `UseSystemResourceKeys` must not break existing ShellKit tests. If any break by asserting framework English, update those assertions (preserve intent).

- [ ] **Step 7: Commit**

```bash
git add src/Yort.ShellKit/SafeError.cs tests/Yort.ShellKit.Tests/SafeErrorTests.cs tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj
git commit -m "feat(shellkit): add SafeError.Describe — resource-key-safe exception text"
```

---

## Task 1: CLAUDE.md convention

**Files:** Modify `CLAUDE.md`

- [ ] **Step 1: Extend the "adding a new tool" checklist**

In `d:\projects\winix\CLAUDE.md`, under the "When adding a new tool:" bullet list, add three bullets:

```markdown
  - Test csproj MUST mirror the app csproj's `<UseSystemResourceKeys>true</UseSystemResourceKeys>` (NOT just `InvariantGlobalization`). Under `UseSystemResourceKeys`, framework exception `.Message` returns bare SR resource keys instead of English; only a test csproj that also sets this flag reproduces the leak. Without it, resource-key regression tests pass spuriously (the JIT host resolves English).
  - Never pipe a framework exception's `.Message` to user output. Use `Yort.ShellKit.SafeError.Describe(ex)` (type-maps common CoreLib exceptions to English, falls back to `ex.GetType().Name`). Adding `ex.GetType().Name` alongside the message is the acceptable minimum for broad catches.
  - Create a native capability `run-smokes.sh` fixture (derive cases from the tool's README options/exit-code surface) and add the tool to `.github/workflows/manual-smoke.yml` (tool list + `runner_for` map + sed retarget rule).
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(claude): require UseSystemResourceKeys test mirror, SafeError, and run-smokes fixture for new tools"
```

---

## Task 1b: pre-sweep substring-dependency triage (F3)

Before mirroring the flag onto any tool, enumerate code/tests that depend on framework message *text* (not just assertions of English — control-flow on message substrings silently changes behaviour under the flag).

**Files:** none modified; produces a triage note committed to the plan dir.

- [ ] **Step 1: Grep src + tests for message-text dependencies**

Run (ripgrep):
```
rg -n "\.Message\.(Contains|StartsWith|IndexOf|Equals)|\bMessage ==|Assert.*\bex\.Message" src tests
```

- [ ] **Step 2: Triage each hit into one of:**
  - **Assertion text update** — a test asserts framework English; will be updated when its tool's flag is mirrored (the common case).
  - **Control-flow dependency (DEFECT)** — production or test code branches on a framework message substring. Under `UseSystemResourceKeys` that substring becomes a key and the branch silently changes. This is a real bug to fix at that tool's task — NOT a text edit. Record each here.

- [ ] **Step 3: Verify `when`'s flag interaction explicitly**

`when` keeps `InvariantGlobalization=false` but will gain `UseSystemResourceKeys=true`. Run `dotnet test tests/Winix.When.Tests` with both flags set and record the result before committing when's csproj change (ICU-on + system-resource-keys-on is an untested combination).

- [ ] **Step 4: Commit the triage note**

```bash
git add docs/plans/2026-06-02-resource-key-leak-substring-triage.md
git commit -m "docs(plan): pre-sweep triage of framework-message-substring dependencies"
```

---

## Task 2 (EXEMPLAR, fully worked): `digest`

`digest` has two leak-classes: a broad catch-all (`Cli.cs:164`) and file-IO reads (`HashRunner.cs:97,126`, `KeyResolver.cs:106,127`). This task is the worked template for every Phase-2 tool.

**Files:**
- Modify: `tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj`
- Modify: `src/Winix.Digest/Cli.cs:164`, `src/Winix.Digest/HashRunner.cs:97,126`, `src/Winix.Digest/KeyResolver.cs:106,127`
- Test: `tests/Winix.Digest.Tests/` (add a `ResourceKeyLeakTests.cs` or extend existing CliTests)

- [ ] **Step 1: Mirror the flag**

In `tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj`, add inside the property group:
```xml
    <UseSystemResourceKeys>true</UseSystemResourceKeys>
```

- [ ] **Step 2: Run existing digest tests, confirm still green**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --nologo`
Expected: PASS. If a test breaks asserting framework English, fix the assertion text (preserve intent), then re-run.

- [ ] **Step 3: Write the failing regression test (file-IO class)**

Add to `tests/Winix.Digest.Tests/ResourceKeyLeakTests.cs` (use the digest test harness's existing `Cli.Run` invocation pattern — read `CliTests.cs` for the exact helper):

```csharp
#nullable enable
using System.IO;
using Xunit;

namespace Winix.Digest.Tests;

public class ResourceKeyLeakTests
{
    [Fact]
    public void MissingInputFile_MessageIsReadable_NoResourceKeyLeak()
    {
        string missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "nope.bin");
        var so = new StringWriter();
        var se = new StringWriter();
        int code = Winix.Digest.Cli.Run(new[] { "sha256", missing }, so, se); // match real Cli.Run signature
        string err = se.ToString();
        Assert.NotEqual(0, code);
        Assert.DoesNotContain("IO_PathNotFound_Path", err, System.StringComparison.Ordinal);
        Assert.DoesNotContain("UnauthorizedAccess", err, System.StringComparison.Ordinal);
    }
}
```

- [ ] **Step 4: Run it, confirm red**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --filter "FullyQualifiedName~ResourceKeyLeak" --nologo`
Expected: FAIL — `err` contains `IO_PathNotFound_Path` (the leak reproduces under the flag).

- [ ] **Step 5: Apply the recipe at all four IO sites + the broad catch**

`HashRunner.cs:97` and `:126`, `KeyResolver.cs:106` and `:127`:
`error = $"failed to read '{path}': {ex.Message}";` → `error = $"failed to read '{path}': {SafeError.Describe(ex)}";`
(`KeyResolver.cs:106` is `failed to stat key file` — keep its prefix, swap `{ex.Message}`→`{SafeError.Describe(ex)}`.)

`Cli.cs:164`: `stderr.WriteLine($"digest: error: {ex.Message}");` → `stderr.WriteLine($"digest: error: {SafeError.Describe(ex)}");`

Add `using Yort.ShellKit;` to each file if absent.

- [ ] **Step 6: Run tests, confirm green**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --nologo`
Expected: PASS (new test + all existing).

- [ ] **Step 7: Commit**

```bash
git add src/Winix.Digest tests/Winix.Digest.Tests
git commit -m "fix(digest): readable error text on file-read + catch-all paths via SafeError; test csproj mirrors UseSystemResourceKeys"
```

---

## Tasks 3–N: per-tool sweep (apply the recipe + per-tool task shape)

Each row is one task. **Order:** v0.4 tools first (gate v0.4.0), then v0.3 alphabetically. Site list is the audit's LEAK set; exception type drives the expected clean fragment. "Seam" = how to trigger the test; "—(inspect)" = no deterministic seam, rely on SafeError unit tests + code inspection (state in commit).

### v0.4 tools

| Tool | Leak sites (file:line) | Class | Test seam → assertion |
|---|---|---|---|
| **demux** | `Winix.Demux/Cli.cs:97` | broad catch (summary-format) | —(inspect): summary-format failure is should-never-happen; route through SafeError, note in commit. (Main 2 sites already fixed in `edc7262`.) |
| **mksecret** | `Winix.MkSecret/Cli.cs:60` | broad catch | Cli.Run with an input that throws in generation if reachable; else —(inspect). Assert no `GetType`-less raw key. |
| **trash** | `Winix.Trash/Cli.cs:70` | broad catch | Cli.Run path that forces a backend throw via the injectable fake backend (`tests/Winix.Trash.Tests` already injects `ITrashBackend`); assert readable. |
| **hcat** | `Winix.HCat/Cli.cs:61`; `Winix.HCat/Handlers/PipeHandler.cs:114` | broad catch ×2 | Cli.Run error path (bad bind/serve) for `Cli.cs:61`; pipe-transfer error for PipeHandler —(inspect if not triggerable). |

### v0.3 tools

| Tool | Leak sites (file:line) | Class | Test seam → assertion |
|---|---|---|---|
| **envvault** | `ExecRunner.cs:85` (decoder, embedded), `:133` (FileNotFound), `:138` (Unauthorized); `Cli.cs:160` (broad), `:239` (decoder, embedded) | IO + decoder + broad | Cli.Run exec with a missing file → `:133`; assert no `IO_`/`UnauthorizedAccess` key. Decoder paths —(inspect) if not triggerable. |
| **files** | `Cli.cs:332` (regex) **+ fix the wrong comment at `Cli.cs:330-331`** | regex | Cli.Run `files . --name '('` (or the real regex flag) → assert no `MakeException`, contains `offset`. |
| **ids** | `Cli.cs:89` (broad) | broad catch | Cli.Run error path; assert readable. —(inspect) if not triggerable. |
| **less** | `Cli.cs:190` (mixed IOException — keep the project "Is a directory" message, route only the framework path through SafeError) | IO (mixed) | Cli.Run on a genuinely unreadable file; assert no `IO_` key, project message preserved for the dir case. |
| **nc** | `NetCatListener.cs:140,160`; `NetCatClient.cs:86,96,142,149,212,235,281,322` | IO/broad (network) | mostly —(inspect): network internals. Apply SafeError to all; add one test if a deterministic path exists (e.g. bind error already SocketException=SAFE). State coverage in commit. nc has no Cli.Run seam (Program.cs) — fixes are in the library classes. |
| **notify** | `AumidShortcut.cs:52` (InvalidOp), `:57` (Unauthorized), `:62` (IO) | typed | —(inspect): Windows COM/shortcut path. Apply SafeError; assert via a unit test on the AumidShortcut error mapping if a seam exists, else inspect. |
| **peep** | `peep/Program.cs:146` (regex) | regex | peep has no Cli.Run seam (Program.cs). Fix in place. —(inspect) or a focused parse-helper test if one exists; state in commit. |
| **protect** | `Cli.cs:103` (Unauthorized), `:112` (EndOfStream), `:132` (broad) | IO + broad | Cli.Run unprotect on a truncated/again unreadable file → `:112`/`:103`; assert no key. |
| **qr** | `Cli.cs:188` (InvalidOp), `:193` (Argument) | typed | Cli.Run render path that throws if triggerable; else —(inspect). Assert readable. |
| **retry** | `retry/Program.cs:288` (NotSupported) | typed | retry has no Cli.Run seam (Program.cs). Fix in place; —(inspect). |
| **schedule** | `CrontabBackend.cs:415` (IO, embedded into CrontabUnavailableException) | IO (embedded) | —(inspect): crontab stdin write. Swap `inner.Message`→`SafeError.Describe(inner)` at the throw. |
| **treex** | `TreeBuilder.cs:149,157,164,181,283,522,528` (walk-IO, embedded in WalkError); `Cli.cs:342` (regex); `Cli.cs:348` (broad) | walk-IO + regex + broad (3 classes) | Cli.Run `treex <unreadable-dir>` → walk-IO WalkError surfaced; `treex . --regex '('` → regex. Assert no `IO_`/`MakeException`. 7 walk sites share one pattern — one test guards the class. |
| **when** | `TimezoneResolver.cs:41` (InvalidTimeZone) | typed | Add `UseSystemResourceKeys=true` but KEEP `InvariantGlobalization=false`. Cli.Run with a bad `--tz` if triggerable; else —(inspect). |
| **winix** | `ToolManifest.cs:55` (Json, embedded into ManifestParseException) | json (embedded) | Cli.Run / manifest-load path on malformed JSON → assert no `Json`-prefixed key; swap `ex.Message`→`SafeError.Describe(ex)` at the throw. |

**Note for sweep executor (F5 — close the wiring gap, don't reopen it):** "—(inspect)" must NOT mean "untested." For each such site:
- If the leak site is a **pure function** reachable without orchestration (regex-parse helpers in `peep`/`treex`/`files`, decoder/encoding helpers in `envvault`, the cron/JSON parse in `schedule`/`winix`), write a direct **unit test on that helper** asserting clean output — no `Cli.Run` seam needed. This covers most "—(inspect)" sites.
- Only where the site is genuinely unreachable deterministically (live socket I/O in `nc`, Windows COM in `notify`) is code-inspection the fallback — and then the wiring is proven mechanically by the per-site grep in the Final task (the exact line no longer matches `{ex.Message}`), NOT by eyeballing. State the chosen path per site in the commit. Never write `Assert.True(true)`.

For tools with no `Cli.Run` seam (`nc`, `peep`, `retry`), fixes land in their library classes (`Winix.NetCat`, `Winix.Peep`) or `Program.cs`; this seam residual is tracked in `cli-seam-retrofit-backlog`.

---

## Phase 3: native smoke fixtures for the 4 v0.4 tools

### Task: demux fixture (fully specified — template for the others)

**Files:** Create `artifacts/v0.4-smoke/demux/run-smokes.sh`

- [ ] **Step 1: Create the fixture** (UNIX commands — runs on ubuntu/macos; demux `--exec` uses `sh -c` there). Model the `run()` harness on `artifacts/reverify-2026-05-06/timeit/run-smokes.sh`. BIN/OUT use the rewritable path convention:

```bash
#!/bin/bash
set +e
BIN="$(pwd)/artifacts/v0.4-smoke/demux/bin/demux.exe"
OUT="$(pwd)/artifacts/v0.4-smoke/demux/out"
rm -rf "$OUT"; mkdir -p "$OUT"   # F8: idempotent — safe to re-run, no leftover state
run() {
  local id="$1"; local desc="$2"; shift 2
  echo "=== $id: $desc ==="
  eval "$*" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"; echo $? > "$OUT/$id.exitcode"
}
run S01-help "--help" "$BIN --help"
run S02-version "--version" "$BIN --version"
run S03-describe "--describe" "$BIN --describe"
run S04-route "route ERROR to file, rest passthrough" "printf 'ERROR a\ninfo b\n' | $BIN --to ERROR $OUT/err.log"
run S05-exec "exec child receives stdin" "printf 'ERROR x\n' | $BIN --exec ERROR 'cat > $OUT/exec.out'"
run S06-default "default-to file" "printf 'a\n' | $BIN --to ERROR $OUT/e.log --default-to $OUT/rest.log"
run S07-field "field routing" "printf '404\tx\n500\ty\n' | $BIN --field 1 --delimiter '\t' --to '^4' $OUT/c.tsv"
run S08-json "json summary to stderr" "printf 'ERROR a\n' | $BIN --to ERROR $OUT/e2.log --json"
run S09-badregex "bad regex -> 125, readable msg" "printf 'x\n' | $BIN --to '(' $OUT/f.log"
run S10-setupfail "unwritable -> 126, readable msg" "printf 'x\n' | $BIN --to ERROR /no_such_dir_zzz/x.log"
run S11-color "--color forces ANSI" "printf 'ERROR x\n' | $BIN --to ERROR $OUT/e3.log --color"
run S12-nocolor "--no-color" "printf 'ERROR x\n' | $BIN --to ERROR $OUT/e4.log --no-color"
echo "=== Smoke run complete ==="
```

### Task: mksecret / trash / hcat fixtures

- [ ] Create `artifacts/v0.4-smoke/{mksecret,trash,hcat}/run-smokes.sh` following the same harness (each starts `rm -rf "$OUT"; mkdir -p "$OUT"` — F8 idempotency), with cases derived from each README's options/exit-code table:
  - **mksecret:** `--help`, `--version`, `--describe`, default generation, each charset/length flag, `--json`, `--no-color`/`--color`, an invalid-arg → 125.
  - **trash:** `--help`, `--version`, `--describe`, `--list` (empty + with items), trash a file. **F8: operate ONLY inside a fixture-created temp dir (`WORK="$OUT/work"`); create throwaway files there and trash those — never a real path. Teardown `rm -rf "$WORK"` at the end.** `--json`, a nonexistent target → error path, `--color`/`--no-color`.
  - **hcat:** `--help`, `--version`, `--describe`, serve a temp dir, inspect, pipe, bad bind → 126, `--json`. **F8: bind an ephemeral port (`:0` or a CI-stop/`--once` mode) so a leftover listener cannot wedge a rerun; ensure the server process is stopped/awaited before the fixture exits (no orphaned listener).**
  - **F8 (all three):** prefer direct invocation over `eval "$*"` for cases that don't need a shell pipe, to limit the quoting surface (CLAUDE.md child-arg rule). Use `eval` only where a pipe/redirect is genuinely required.

### Task: wire `manual-smoke.yml`

**Files:** Modify `.github/workflows/manual-smoke.yml`

- [ ] **Step 1:** Append the four tools to the default list at line 94: `...,envvault,demux,mksecret,trash,hcat`.
- [ ] **Step 2:** Add a `runner_for` case (after line 140):
```bash
              demux|mksecret|trash|hcat)
                echo "v0.4-smoke/$1" ;;
```
- [ ] **Step 3 (F7 — order- and BSD-sed-sensitive):** Add the retarget rules for the new dir to the `sed -E -i.bak` block. **The bin-path rule MUST come BEFORE the bare-dir rule** (else the dir rule rewrites the prefix first and the bin rule never matches). manual-smoke runs on `ubuntu-latest` AND `macos-latest` (BSD sed) — `sed -E -i.bak` + `\1` backrefs work on both, but verify on macOS. Place these two lines immediately after the existing `reverify-2026-05-06` dir rule:
```bash
              -e 's|artifacts/v0.4-smoke/([a-z]+)/bin/\1\.exe|artifacts/manual-smoke/'"${{ matrix.rid }}"'/\1/\1|g' \
              -e 's|artifacts/v0.4-smoke/([a-z]+)|artifacts/manual-smoke/'"${{ matrix.rid }}"'/\1|g' \
```

- [ ] **Step 3b (F7 — fail loudly, not vacuously):** After the `sed` block (before the `timeout 240s bash "$dst_runner"` line), add a guard so a non-rewritten path fails the smoke instead of silently running a missing binary (the fixture's `set +e` would otherwise swallow it):
```bash
            if grep -q "v0.4-smoke" "$dst_runner"; then
              echo "::error::sed retarget failed for $t — stale v0.4-smoke path remains" ; exit 1
            fi
```
- [ ] **Step 4:** Commit:
```bash
git add artifacts/v0.4-smoke .github/workflows/manual-smoke.yml
git commit -m "test(smoke): add native run-smokes fixtures + manual-smoke wiring for demux/mksecret/trash/hcat"
```

---

## Final task: full verification

- [ ] **Step 1:** Full solution green: `dotnet test Winix.sln --nologo` → 0 failures (the parallel-run `IsOnPath_DoesNotSpawnProcess` flake is known; re-run in isolation if it appears).
- [ ] **Step 2:** Native re-smoke locally: publish each of demux/mksecret/trash/hcat (`dotnet publish src/<tool>/<tool>.csproj -c Release -r win-x64`), run its `run-smokes.sh` against the published binary (adapt path), confirm error-path messages are readable (no `IO_`/`MakeException`/SR keys) and exit codes correct.
- [ ] **Step 3 (F5 — per-site wiring proof):** For every LEAK site in the table, assert the exact line no longer pipes raw `{ex.Message}`. Run:
```
rg -n "\{ex\.Message\}|\{inner\.Message\}|\{e\.Message\}" src
```
Every remaining hit must be a known ACCEPTABLE site (has `ex.GetType().Name` adjacent) — cross-check against the audit's ACCEPTABLE list. Zero LEAK-table lines may remain. This is the mechanical proof that "—(inspect)" sites were actually rewired.

- [ ] **Step 4 (F12 — de-risk the 21 SAFE sites with one probe):** Construct a `Win32Exception` and a `SocketException` under `UseSystemResourceKeys=true` and print `.Message`. Confirm they yield native-OS English (not an SR key) — this validates the audit's "SAFE: native-OS text unaffected by the flag" assumption across all 21 SAFE sites at once. Record the probe output. If either leaks, the SAFE classification is wrong and those sites re-enter scope.

---

## Self-Review

- **Spec coverage:** P1 (Task 1) ✓, P2 guard (per-tool Step 1) ✓, P3 smokes (Phase 3) ✓, P4 leaks (Task 2 + table) ✓, helper (Task 0) ✓, convention (Task 1) ✓, no-v0.3-reship (no release task) ✓, testing strategy (recipe + helper tests) ✓.
- **Placeholders:** the "—(inspect)" markers are deliberate, justified per-site (untriggerable internals), and explicitly forbid `Assert.True(true)`. Not gaps.
- **Type consistency:** `SafeError.Describe(Exception)` used identically in helper, recipe, and every table row. Test signature `Cli.Run(args, stdout, stderr)` noted as "match real signature" because seams vary per tool (demux is 4-arg with stdin) — executor verifies per tool.
