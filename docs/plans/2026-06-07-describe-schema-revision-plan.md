# `--describe` Schema Revision Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One coordinated `--describe` schema revision — `schema_version`, `maturity` tiers, `prefer_default_when` hints — enforced by a new suite-wide contract-lock test project and promised by `docs/STABILITY.md`.

**Architecture:** All schema emission centralises in `CommandLineParser.GenerateDescribe()` (ShellKit); per-tool wiring is one or two fluent calls. The contract harness (`tests/Winix.Contract.Tests`) invokes every tool's library `Cli` seam in-process with `["--describe"]`, captures **`Console.Out`** (ShellKit auto-writes describe via `Console.WriteLine` during `Parse` — `CommandLineParser.cs:577` — NOT the seam's stdout writer), normalises the `version` field, and byte-compares against checked-in snapshots.

**Tech Stack:** .NET 10, xUnit, System.Text.Json. Branch: `release/v0.4.0`, commit directly, NO Co-Authored-By.

**Design/ADR:** docs/plans/2026-06-07-describe-schema-revision-{design,adr}.md — read both first.

**House rules that bind every task:** full braces always; no `[^1]`/`..` range/index expressions; XML doc comments on public/internal members; terse why-comments; warnings-as-errors stays clean; existing test assertions are EXTENDED, never weakened; deviations from this plan are reported, never silent.

---

### Task 1: ShellKit — `schema_version` emission

**Files:**
- Modify: `src/Yort.ShellKit/CommandLineParser.cs` (~line 919, `GenerateDescribe`)
- Test: `tests/Yort.ShellKit.Tests/DescribeTests.cs`

- [ ] **Step 1: Write the failing test** (add to `DescribeTests.cs`, matching its existing parser-builder style — read the file's first test for the construction pattern):

```csharp
[Fact]
public void GenerateDescribe_EmitsSchemaVersionAsFirstField()
{
    var parser = new CommandLineParser("demo", "1.0.0", "demo tool").StandardFlags();
    string json = parser.GenerateDescribeForTests();
    // schema_version must be the FIRST field so consumers can branch before
    // parsing the rest; value 1 is the initial envelope version.
    Assert.StartsWith("{\"schema_version\":1,", json, StringComparison.Ordinal);
}
```

(VERIFY AT IMPLEMENTATION: the exact test-visible accessor name — `DescribeTests.cs` already tests `GenerateDescribe` output somehow; reuse its existing access path, e.g. an internal method or captured Console. Use whatever the sibling tests use; do NOT invent a new seam if one exists.)

- [ ] **Step 2: Run it; expect FAIL** (`dotnet test tests/Yort.ShellKit.Tests --filter EmitsSchemaVersionAsFirstField`) — fails because the field doesn't exist.

- [ ] **Step 3: Implement.** In `GenerateDescribe()` immediately after `writer.WriteStartObject();` and BEFORE `writer.WriteString("tool", …)`:

```csharp
// Versions the STRUCTURE of this envelope only (field names/nesting/types).
// Additive fields do NOT bump it; renames/removals/type changes DO.
// See docs/STABILITY.md. Bump rule must be honoured by any future editor.
writer.WriteNumber("schema_version", DescribeSchemaVersion);
```

with a class-level constant:

```csharp
/// <summary>
/// Version of the --describe envelope STRUCTURE. Bump ONLY on renames, removals,
/// or type changes of envelope fields — never for additive fields. docs/STABILITY.md
/// documents the rule for consumers.
/// </summary>
public const int DescribeSchemaVersion = 1;
```

- [ ] **Step 4: Run the ShellKit suite** (`dotnet test tests/Yort.ShellKit.Tests`) — the new test passes; note any OTHER ShellKit describe tests that broke (they pin envelope text); extend their expected strings for the new first field (assertion-extension, not weakening — record in the commit message).

- [ ] **Step 5: Commit** — `feat(shellkit): emit schema_version:1 as the first --describe field`

---

### Task 2: ShellKit — `ToolMaturity` enum + `.Maturity()` + emission

**Files:**
- Create: `src/Yort.ShellKit/ToolMaturity.cs`
- Modify: `src/Yort.ShellKit/CommandLineParser.cs` (builder + `GenerateDescribe`)
- Test: `tests/Yort.ShellKit.Tests/DescribeTests.cs`

- [ ] **Step 1: Write the failing tests:**

```csharp
[Fact]
public void GenerateDescribe_EmitsMaturityWhenConfigured()
{
    var parser = new CommandLineParser("demo", "1.0.0", "demo tool")
        .Maturity(ToolMaturity.Fresh).StandardFlags();
    string json = parser.GenerateDescribeForTests();
    Assert.Contains("\"maturity\":\"fresh\"", json, StringComparison.Ordinal);
}

[Fact]
public void GenerateDescribe_OmitsMaturityWhenUnset()
{
    var parser = new CommandLineParser("demo", "1.0.0", "demo tool").StandardFlags();
    string json = parser.GenerateDescribeForTests();
    // ShellKit stays unopinionated outside Winix; the WINIX gate lives in the
    // contract harness (ADR D3), not here.
    Assert.DoesNotContain("\"maturity\"", json, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run; expect FAIL** (enum/method don't exist — compile error counts as the red).

- [ ] **Step 3: Implement.** New file `src/Yort.ShellKit/ToolMaturity.cs`:

```csharp
namespace Yort.ShellKit;

/// <summary>
/// Maturity tier advertised in the --describe envelope. Winix rule (docs/STABILITY.md):
/// Core = multi-round-reviewed AND survived at least one stable release without
/// interface-breaking changes; Fresh = everything else (reviewed but unexposed —
/// the interface may still move).
/// </summary>
public enum ToolMaturity
{
    /// <summary>Stable, supported; deprecation policy binds strictly.</summary>
    Core,
    /// <summary>Reviewed but not yet through a stable release in the wild; interface may move.</summary>
    Fresh,
}
```

In `CommandLineParser`: a `private ToolMaturity? _maturity;` field, fluent setter following the existing builder pattern (copy the shape of a neighbouring one-arg fluent method, e.g. `.Platform()`):

```csharp
/// <summary>Advertises the tool's maturity tier in --describe. See <see cref="ToolMaturity"/>.</summary>
public CommandLineParser Maturity(ToolMaturity maturity)
{
    _maturity = maturity;
    return this;
}
```

In `GenerateDescribe()`, immediately after `writer.WriteString("version", _version);`:

```csharp
if (_maturity is not null)
{
    writer.WriteString("maturity", _maturity == ToolMaturity.Core ? "core" : "fresh");
}
```

- [ ] **Step 4: Run; expect PASS.** Full ShellKit suite green.

- [ ] **Step 5: Commit** — `feat(shellkit): ToolMaturity enum + .Maturity() --describe emission`

---

### Task 3: ShellKit — `.PreferDefaultWhen()` + emission

**Files:**
- Modify: `src/Yort.ShellKit/CommandLineParser.cs`
- Test: `tests/Yort.ShellKit.Tests/DescribeTests.cs`

- [ ] **Step 1: Failing tests:**

```csharp
[Fact]
public void GenerateDescribe_EmitsPreferDefaultWhenArray()
{
    var parser = new CommandLineParser("demo", "1.0.0", "demo tool")
        .PreferDefaultWhen("case one — use incumbent", "case two")
        .StandardFlags();
    string json = parser.GenerateDescribeForTests();
    Assert.Contains("\"prefer_default_when\":[\"case one — use incumbent\",\"case two\"]",
        json, StringComparison.Ordinal);
}

[Fact]
public void GenerateDescribe_OmitsPreferDefaultWhenWhenUnset()
{
    var parser = new CommandLineParser("demo", "1.0.0", "demo tool").StandardFlags();
    Assert.DoesNotContain("\"prefer_default_when\"",
        parser.GenerateDescribeForTests(), StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run; expect FAIL (compile).**

- [ ] **Step 3: Implement.** Field `private string[]? _preferDefaultWhen;`, fluent setter:

```csharp
/// <summary>
/// Machine-readable "prefer the incumbent when…" hints emitted in --describe as
/// <c>prefer_default_when</c>. Entries must condense existing reviewed docs prose
/// (never new claims). Omit entirely when the tool has no real incumbent case —
/// absence means "no guidance".
/// </summary>
public CommandLineParser PreferDefaultWhen(params string[] cases)
{
    _preferDefaultWhen = cases;
    return this;
}
```

Emission: place adjacent to the `platform` block in `GenerateDescribe()` (immediately AFTER the `_platformScope` if-block closes — pick the exact spot once and the contract snapshots pin it thereafter):

```csharp
if (_preferDefaultWhen is not null && _preferDefaultWhen.Length > 0)
{
    writer.WriteStartArray("prefer_default_when");
    foreach (string c in _preferDefaultWhen)
    {
        writer.WriteStringValue(c);
    }
    writer.WriteEndArray();
}
```

- [ ] **Step 4: Run; PASS; full ShellKit suite green.**

- [ ] **Step 5: Commit** — `feat(shellkit): .PreferDefaultWhen() --describe emission`

---

### Task 4: Wire `.Maturity()` into all 28 tools

**Files:** every tool's parser-construction site (the file where `new CommandLineParser(...)` is chained — usually `src/Winix.{Tool}/ArgParser.cs`; locate with `grep -rn "new CommandLineParser" src/`). Subcommand parsers in the same files get the SAME call (every envelope carries the fields).

**The tier table (from the design — apply exactly):**

| maturity | tools |
|---|---|
| `ToolMaturity.Core` | timeit, squeeze, peep, wargs, files, treex, man, less, whoholds, schedule, nc, winix, retry, when, clip, ids, digest, notify, url, qr, protect, unprotect (same `Winix.Protect` parser — one call covers both binaries; VERIFY the parser is shared at implementation), envvault |
| `ToolMaturity.Fresh` | mksecret, trash, hcat, mkauth, demux |

- [ ] **Step 1:** `grep -rn "new CommandLineParser" src/` — enumerate every construction site (top-level AND subcommand parsers). Record the count in the commit message.
- [ ] **Step 2:** Add `.Maturity(ToolMaturity.X)` per the table at every site (a tool's subcommand parsers carry the tool's tier).
- [ ] **Step 3:** Build the full solution (`dotnet build Winix.sln`) — zero warnings.
- [ ] **Step 4:** Run the FULL test suite (`dotnet test Winix.sln`). Any failing test that pins describe/help text gets its expectation EXTENDED for the new fields (assertion-extension; list every touched test in the commit message). Anything else failing: stop and report.
- [ ] **Step 5: Commit** — `feat(suite): wire ToolMaturity into all 28 tools (23 core / 5 fresh)`

---

### Task 5: Describe-pin ripple audit

**Files:** test projects only.

- [ ] **Step 1:** `grep -rn '"tool\\":' tests/ --include="*.cs"` and `grep -rn 'Assert.Equal.*describe' tests/ --include="*.cs" -i` — triage each hit: `Assert.Contains` pins are additive-safe (no action); full-shape `Assert.Equal`/`StartsWith("{\"tool\"` pins need extending for `schema_version` first-field + `maturity`.
- [ ] **Step 2:** Extend (never weaken) each affected pin; run that project's suite; list every change in the commit message.
- [ ] **Step 3:** Full `dotnet test Winix.sln` green.
- [ ] **Step 4: Commit** — `test(suite): extend describe-shape pins for schema_version + maturity` (skip the commit if Step 1 finds nothing — Task 4 Step 4 may already have caught them all; say so in the report).

---

### Task 6: `prefer_default_when` distillation + wiring

**Files:** the tools' ArgParser files (same sites as Task 4); content sourced from `docs/ai/{tool}.md` + `src/{tool}/README.md`.

**Binding rule (ADR D4):** every entry CONDENSES an existing "When NOT to use" / incumbent-comparison passage. NO new claims. No source prose → no field. Each tool's commit message cites the source file+section per entry.

- [ ] **Step 1:** For each of the 28 tools, read `docs/ai/{tool}.md` (and the README comparison section if present). Build the distillation list. Expected shape — three worked examples (VERIFY wording against the actual source prose at implementation; these are patterns, not final copy):

```csharp
// files (source: docs/ai/files.md "When NOT to use")
.PreferDefaultWhen(
    "complex find expressions (-perm, -newer, -exec chains) — use find directly",
    "scripted environments where find/fd is already a hard dependency")

// nc (source: docs/ai/nc.md "When NOT to use")
.PreferDefaultWhen(
    "advanced netcat modes (proxying, port scanning breadth) — use ncat/socat",
    "long-lived production tunnels — use a real proxy or ssh -L")

// hcat (source: docs/ai/hcat.md "When NOT to use")
.PreferDefaultWhen(
    "production file serving — use a real web server (nginx, caddy)",
    "trivial one-off static serving where python3 -m http.server is already at hand")
```

- [ ] **Step 2:** Wire `.PreferDefaultWhen(...)` into each tool that has source prose (expected 15–18 tools; the exact set is whatever the sources support). Tools without source prose get NOTHING — record the omit-list + reason ("no incumbent case documented") in the commit message.
- [ ] **Step 3:** Build + full suite green (describe-pin ripple handled as in Task 5 if any new hits).
- [ ] **Step 4: Commit** — `feat(suite): prefer_default_when hints distilled from docs/ai (sources cited)`

---

### Task 7: Contract-lock harness — project + capture helper + registry

**Files:**
- Create: `tests/Winix.Contract.Tests/Winix.Contract.Tests.csproj`
- Create: `tests/Winix.Contract.Tests/DescribeSurfaces.cs` (registry)
- Create: `tests/Winix.Contract.Tests/ConsoleCapture.cs`
- Create: `tests/Winix.Contract.Tests/ContractSnapshotTests.cs`
- Modify: `Winix.sln` (add the project — `dotnet sln add`)

- [ ] **Step 1: csproj** — copy the shape of `tests/Winix.MkSecret.Tests/Winix.MkSecret.Tests.csproj` (xUnit + net10.0 + `UseSystemResourceKeys` + `InvariantGlobalization`), with `<ProjectReference>` entries for ALL 28 class libraries (`src/Winix.*/Winix.*.csproj` + `src/Yort.ShellKit/Yort.ShellKit.csproj`). VERIFY at implementation: envvault/protect library project names from the layout table in CLAUDE.md.

- [ ] **Step 2: ConsoleCapture helper.** Describe output goes through `Console.WriteLine` inside `Parse` (CommandLineParser.cs:577), NOT the seam's writer params — capture must wrap `Console.Out`:

```csharp
#nullable enable
using System;
using System.IO;

namespace Winix.Contract.Tests;

/// <summary>
/// Captures Console.Out around a seam invocation. ShellKit auto-writes --help/--version/
/// --describe via Console.WriteLine during Parse (CommandLineParser.cs:577), NOT through
/// the Cli seam's stdout writer — so contract capture must intercept the console itself.
/// Console state is process-global: all snapshot cases run inside ONE test class (xUnit
/// serialises within a class), so no cross-test race.
/// </summary>
internal static class ConsoleCapture
{
    public static (string Stdout, string Stderr, int ExitCode) Run(Func<int> invoke)
    {
        TextWriter origOut = Console.Out;
        TextWriter origErr = Console.Error;
        var outW = new StringWriter();
        var errW = new StringWriter();
        Console.SetOut(outW);
        Console.SetError(errW);
        try
        {
            int exit = invoke();
            return (outW.ToString(), errW.ToString(), exit);
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }
}
```

- [ ] **Step 3: Registry.** One entry per describe surface; each adapter takes `string[] args`, returns the exit code, and is invoked under `ConsoleCapture`. Seam shapes vary — wrap each exactly the way that tool's own seam tests do (read them when unsure). Representative entries (write ALL 28 + subcommand surfaces; the async seams block on the task — acceptable in tests via `.GetAwaiter().GetResult()` ONLY if that is what the tool's own tests do — VERIFY per tool and copy their pattern):

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace Winix.Contract.Tests;

/// <summary>
/// Every --describe surface in the suite: key → adapter invoking the library seam.
/// Keys: "tool" or "tool/subcommand". A new tool MUST be registered here and its
/// snapshot committed (CLAUDE.md new-tool checklist).
/// </summary>
internal static class DescribeSurfaces
{
    public static readonly IReadOnlyDictionary<string, Func<string[], int>> All =
        new Dictionary<string, Func<string[], int>>(StringComparer.Ordinal)
        {
            // sync Run(args, stdout, stderr) family — writers unused for --describe
            // (ShellKit writes to Console.Out) but required by the signature:
            ["timeit"] = args => Winix.TimeIt.Cli.Run(args, TextWriter.Null, TextWriter.Null),
            // protect/unprotect share one parser via invocationName:
            ["protect"] = args => Winix.Protect.Cli.Run(args, "protect"),
            ["unprotect"] = args => Winix.Protect.Cli.Run(args, "unprotect"),
            // …(all remaining tools; copy each signature from that tool's own seam tests —
            //   schedule has an optional backend param: pass null/omit; retry takes a
            //   CancellationToken: pass CancellationToken.None; peep/winix are async;
            //   wargs takes a TextReader stdin: pass TextReader.Null; nc is the
            //   byte-stream seam: pass Stream.Null/MemoryStream per its tests)…
            // subcommand surfaces (final list from the Task 8 probe):
            ["qr/wifi"] = args => /* qr seam */ 0, // VERIFY signature at implementation
        };
}
```

(The `// …` above is a deliberate plan-level elision of 25 mechanical one-liners whose EXACT signatures the implementer copies from each tool's existing seam tests — listing guessed signatures here would bake in wrong assumptions, the documented plan-test-code failure mode. The rule is explicit: copy from the tool's own tests, never guess.)

- [ ] **Step 4:** Build the project; placeholder test asserting `DescribeSurfaces.All.Count > 0`; commit scaffold — `test(contract): contract-lock harness scaffold (project + capture + registry)`

---

### Task 8: Probe — subcommand surfaces + platform variance (BEFORE pinning anything)

- [ ] **Step 1:** For each multi-subcommand tool (schedule, url, qr, mkauth, mksecret, winix, trash — VERIFY the full list via each tool's dispatch on `positional[0]`), run `dotnet run --project src/{tool} -- {sub} --describe` for one subcommand: does it emit a DISTINCT envelope (subcommand-specific tool name/options) with exit 0? Register every surface that does. Record any tool whose subcommand describe is identical to top-level (register top-level only; note it).
- [ ] **Step 2:** Platform-variance probe: with the registry complete and fields wired (Tasks 4+6), dump every surface's normalised describe JSON on Windows AND inside WSL (`wsl bash -lc "bash /mnt/d/projects/winix/tmp/dump-describes.sh"` — script file, never inline compound wsl commands; mkdir -p tmp first). Diff the two sets. Expected: identical (the `.Platform()` block emits both OS values unconditionally). ANY difference = a recorded per-tool decision (normalise that field or split the snapshot) — report it, don't improvise.
- [ ] **Step 3:** Record probe results in the task report (surface count, variance findings).

---

### Task 9: Snapshot test + snapshots

**Files:**
- Modify: `tests/Winix.Contract.Tests/ContractSnapshotTests.cs`
- Create: `tests/Winix.Contract.Tests/snapshots/{key}.describe.json` (one per surface; `/` in keys → `_` in filenames, matching the test code's `key.Replace('/', '_')`)

- [ ] **Step 1: The test:**

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Winix.Contract.Tests;

public class ContractSnapshotTests
{
    public static IEnumerable<object[]> Surfaces()
        => DescribeSurfaces.All.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void Describe_matches_committed_snapshot(string key)
    {
        string[] parts = key.Split('/');
        string[] args = parts.Length == 2
            ? new[] { parts[1], "--describe" }
            : new[] { "--describe" };

        var (stdout, stderr, exit) = ConsoleCapture.Run(() => DescribeSurfaces.All[key](args));

        Assert.Equal(0, exit);
        Assert.Equal("", stderr);

        string actual = Normalise(stdout);

        // Maturity gate (ADR D3): a Winix tool may not ship untiered.
        JsonNode node = JsonNode.Parse(stdout)!;
        Assert.NotNull(node["schema_version"]);
        string? maturity = node["maturity"]?.GetValue<string>();
        Assert.True(maturity is "core" or "fresh",
            $"{key}: maturity is unset or invalid ('{maturity}') — every Winix tool must call .Maturity(...)");

        string snapshotPath = Path.Combine(SnapshotDir, key.Replace('/', '_') + ".describe.json");

        if (Environment.GetEnvironmentVariable("WINIX_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(snapshotPath, actual);
            Assert.Fail($"snapshot regenerated for {key} — update mode always fails so CI can never silently self-update; commit the diff");
        }

        Assert.True(File.Exists(snapshotPath),
            $"no snapshot for {key} — run with WINIX_UPDATE_SNAPSHOTS=1 once and commit snapshots/");
        string expected = File.ReadAllText(snapshotPath);

        // Byte-equal after normalisation. On mismatch the message carries the contract
        // instructions (docs/STABILITY.md): regenerate if intentional; bump
        // CommandLineParser.DescribeSchemaVersion if the envelope STRUCTURE changed.
        Assert.True(expected == actual,
            $"{key}: --describe drifted from the committed contract snapshot.\n" +
            $"Intentional? Re-run with WINIX_UPDATE_SNAPSHOTS=1, commit the snapshot diff, " +
            $"and bump schema_version if the envelope STRUCTURE changed. See docs/STABILITY.md.\n" +
            $"--- expected ---\n{expected}\n--- actual ---\n{actual}");
    }

    private static string Normalise(string describeJson)
    {
        JsonNode node = JsonNode.Parse(describeJson)!;
        // The version field is the dev/release build number — the ONLY masked field.
        // Anything else that varies is a contract finding, not noise.
        node["version"] = "<normalised>";
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static string SnapshotDir =>
        Path.Combine(AppContext.BaseDirectory, "snapshots");
}
```

(VERIFY at implementation: snapshots must be copied to output — add `<Content Include="snapshots\**" CopyToOutputDirectory="PreserveNewest" />` to the csproj; the UnsafeRelaxedJsonEscaping choice must round-trip the suite's non-ASCII describe text (em-dashes) — probe one tool with non-ASCII description and adjust if escaping differs from expectations. Update mode writes to the SOURCE tree path, not bin — compute the source path via a `[CallerFilePath]`-anchored helper, the standard snapshot-test pattern; implement that instead of AppContext.BaseDirectory for the write path, read from output dir.)

- [ ] **Step 2:** Generate all snapshots (`WINIX_UPDATE_SNAPSHOTS=1 dotnet test tests/Winix.Contract.Tests` — expect all-fail with "regenerated"); inspect a sample (timeit, mkauth, qr/wifi) by eye: fields present, ordering sane, nothing leaking.
- [ ] **Step 3:** Re-run WITHOUT the env var — ALL GREEN.
- [ ] **Step 4:** Run the harness inside WSL too (Task 8's variance evidence) — green there as well.
- [ ] **Step 5:** Registry-completeness guard — add:

```csharp
[Fact]
public void Every_tool_in_the_suite_is_registered()
{
    // 28 top-level surfaces (CLAUDE.md NuGet-ID canon). If you added a tool,
    // register it and commit its snapshot (new-tool checklist).
    int topLevel = DescribeSurfaces.All.Keys.Count(k => !k.Contains('/'));
    Assert.Equal(28, topLevel);
}
```

- [ ] **Step 6: Commit** — `test(contract): suite-wide --describe contract snapshots (N surfaces)`

---

### Task 10: `docs/STABILITY.md`

**Files:** Create `docs/STABILITY.md`.

- [ ] **Step 1:** Write the six sections per the design (§5): covered surface / rules + deprecation (≥2 minor releases aliasing + stderr notice; core strict, fresh best-effort) / schema_version meaning + bump rule + current value / tier definitions + promotion rule / enforcement (contract suite) / 0.x honesty. One page. Use plain declarative language; every claim must be true TODAY (the doc is audited against behaviour in the review round).
- [ ] **Step 2: Commit** — `docs: STABILITY.md — the agent-surface stability policy`

---

### Task 11: Surfacing sweep (llms.txt, README, AGENTS.md, docs/ai)

- [ ] **Step 1:** `llms.txt`: append ` (fresh)` to the five new tools' lines; add one header sentence defining core/fresh + STABILITY.md link.
- [ ] **Step 2:** Root `README.md`: one paragraph under the Tools table — tier definitions, the five fresh tools, STABILITY.md link.
- [ ] **Step 3:** `AGENTS.md`: ONE sentence pointing at the `maturity` describe field (no other changes — the fuller refresh stays deferred).
- [ ] **Step 4:** `docs/ai/{mksecret,trash,hcat,mkauth,demux}.md`: one-line maturity note each.
- [ ] **Step 5: Commit** — `docs: surface maturity tiers on llms.txt/README/AGENTS.md/agent guides`

---

### Task 12: Bookkeeping (old ADR, recommendations doc, CLAUDE.md)

- [ ] **Step 1:** `docs/plans/2026-03-31-ai-discoverability-adr.md`: close the deferred `schema_version` row with a pointer to the 2026-06-07 ADR.
- [ ] **Step 2:** `docs/plans/2026-06-06-agent-adoption-hardening-design.md`: status note at top — Recs 1–3 implemented (link), Rec 4 next.
- [ ] **Step 3:** Project `CLAUDE.md`: add the new-tool checklist line ("Register the tool (and its subcommand describe surfaces) in tests/Winix.Contract.Tests/DescribeSurfaces.cs + commit its snapshot; set `.Maturity(ToolMaturity.Fresh)` on a new tool") and the project-layout entry for `tests/Winix.Contract.Tests/`.
- [ ] **Step 4: Commit** — `docs: bookkeeping for the describe schema revision`

---

### Task 13: Verification gate

- [ ] **V1:** `dotnet build Winix.sln` — zero warnings.
- [ ] **V2:** `dotnet test Winix.sln` — zero failures (now includes the contract project).
- [ ] **V3:** Harness green on WSL (re-run; quote counts).
- [ ] **V4:** Spot end-to-end on a BUILT binary (not the seam): `dotnet run --project src/mkauth -- --describe` → starts with `{"schema_version":1,"tool":"mkauth"` and carries `"maturity":"fresh"`; same for one core tool.
- [ ] **V5:** Report: snapshot surface count, the prefer_default_when coverage list (included + omitted-with-reason), every commit, every deviation.

**Post-build (orchestrator, not this plan's executor):** adversarial 4-reviewer round per house process — docs-auditor brief MUST include the two-direction hint check (every hint traces to source prose; no un-distilled real case) and STABILITY.md claims-vs-behaviour.
