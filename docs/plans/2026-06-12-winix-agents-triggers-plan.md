# winix agents — trigger-payload rework — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the behaviourally-inert governance-plus-pull-pointer payload that `AgentsManager.RenderBlock` emits with a small, curated, situation-keyed trigger list (push, not pull), so an agent loading the block recognises *when* to reach for a high-value Winix tool — while keeping a one-line conservative guard and the long tail on `winix list`.

**Architecture:** Payload-only rewrite of one method (`RenderBlock`, both `UserScope`/`ProjectScope` render modes). No change to markers, the `v={version}` stamp, the version-pinned URL, LF-only emission, the user/project mode split, or any `MergeBlock`/`RemoveBlock`/`FindBlockVersion`/`Run*` orchestration. The trigger list is static hand-curated text rendered from the version string alone (no manifest coupling). The block stays inside its existing scannability budget (≤ 20 physical lines incl. markers).

**Tech Stack:** C# / .NET (AOT), xUnit. Single class library `Winix.Winix`, single test project `Winix.Winix.Tests`.

**Design doc:** [2026-06-11-winix-agents-triggers-design.md](2026-06-11-winix-agents-triggers-design.md)
**Companion ADR:** [2026-06-11-winix-agents-triggers-adr.md](2026-06-11-winix-agents-triggers-adr.md)

---

## File Structure

- **Modify:** `src/Winix.Winix/AgentsManager.cs` — rewrite the body of `RenderBlock(string version, RenderMode mode)` (currently lines 98–143). Nothing else in the file changes.
- **Modify:** `tests/Winix.Winix.Tests/AgentsManagerTests.cs` — update the four `RenderBlock` content tests to the new payload contract, fix one now-vacuous CRLF merge assertion, and add the §7 curation/budget/footer tests.
- **Verify / possibly modify (doc reconciliation):** `AGENTS.md` (repo root), `src/winix/README.md`, `src/winix/man/man1/winix.1` (+ `winix.1.md` source if present), `docs/ai/winix.md` — confirm none quote the *old* block prose; update only if a contradiction exists.

### Contract-change note (mandatory read before Task 1)

Per the project rule *"if a fix forces editing an existing passing test, the test was guarding a contract — pause and verify the change is intentional"*: the four existing `RenderBlock_*` content tests **are** the old payload contract. Rewriting the payload is exactly what this plan does, so editing them is intentional and expected. They are listed explicitly in Task 1 with the precise old→new assertion deltas so the change is auditable, not a silent shape-shift. The orchestration tests (`MergeBlock`/`RemoveBlock`/`FindBlockVersion`/`Run*`) assert only markers/URL/version/EOL — all unchanged — so they must continue to pass **byte-stable**; if any of them break, that is a real regression, not an expected edit.

### The new block content (single source of truth for both tasks)

This is the exact text `RenderBlock` must emit, joined with `\n`. The `lead` line is the only per-mode difference; header differs as today; bullets + footer are identical in both modes. The version token `{urlRef}` is `v{version}` for stable, `main` for pre-release (existing `UrlRef`, unchanged).

```
<!-- winix:start v={version} — managed by `winix agents init`; edits between markers are overwritten -->
## Winix CLI tools (available on this machine)          ← user mode header
                                                          (project mode: "## Winix CLI tools (if available in your environment)")

<lead>                                                    ← per-mode, see below

- **A network op or command is flaky** — `online` blocks until connectivity (or a named endpoint) is healthy so you resume instead of polling/giving up; `retry` re-runs a failing command with backoff; `nc` checks whether a port is open.
- **You're about to delete files** — `trash` removes them recoverably instead of `rm`.
- **You need to know what's locking a file or holding a port** — `whoholds` (no native cross-platform equivalent).
- **Building an authenticated request, or handling secrets** — `mkauth` (OAuth1/JWT/Basic/Azure headers), `digest` (hash/HMAC), `envvault` (keychain-backed env vars), `protect`/`unprotect` (encrypt-at-rest), `mksecret` (generate passwords/keys).
- **You need a modern ID** — `ids` (UUIDv4/v7, ULID, NanoID).
- **Acting for the user / keeping data local** — `notify` (ping them when a long task finishes or while away), `qr` (hand a URL to their phone — don't POST it to a web QR service), `clip` (copy to their clipboard, not a pastebin).

Conventions: every tool has `--describe` (JSON schema) and `--json`; exit 0 = success; `NO_COLOR` honored.
General signal: if a command fails from Windows path-mangling, or you're hand-parsing text a tool emits
for humans, a winix tool may fit (Windows-native, `--json`) — e.g. `files`/`wargs` over `find`/`xargs`;
`winix list` for the rest. No clear win? Keep the default. Full guidance:
https://github.com/Yortw/winix/blob/{urlRef}/AGENTS.md
<!-- winix:end -->
```

- **User-mode lead** (single physical line): `Prefer a winix tool only when it genuinely beats the platform default — otherwise use the default. It does when:`
- **Project-mode lead** (single physical line): `If Winix tools are installed in your environment, prefer one only when it genuinely beats the platform default — otherwise use the default. If Winix is not installed, ignore this section. It does when:`

That is **18 physical lines including both markers** in both modes — within the ≤ 20 budget with 2 lines of headroom.

> **Verbatim-from-design note:** the user-mode lead uses lowercase `winix tool` (matching the CLI binary name), per design §5. This is deliberate and spec-sourced — do **not** "correct" it to capital `Winix`. The header keeps the existing capitalised `## Winix CLI tools` wording.

---

## Task 1: Encode the new payload contract in the RenderBlock tests (red)

**Files:**
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

This task only edits tests. After it, the `RenderBlock` content tests fail against the *current* implementation — that is the TDD red state. The orchestration tests are left untouched (they must stay green throughout) except the one now-vacuous CRLF assertion in step 5.

- [ ] **Step 1: Rewrite `RenderBlock_StableVersion_EmitsMarkersVersionAndPinnedUrl`**

Replace the whole method body (currently lines 34–55) with the version below. Deltas vs. old: **removed** `Assert.Contains("not by", …)` (the new lead says "otherwise use the default", not "…not by default"); **removed** the two-part exit-code asserts (`"exit 0 = success, non-zero on"` and `"usage/runtime codes vary by tool"`) — the conventions line is now a single clause; **kept** markers/URL/`winix list`/`use the default`/no-`\r`/no-125/126; **added** asserts that the trigger structure and footer are present.

```csharp
    [Fact]
    public void RenderBlock_StableVersion_EmitsMarkersVersionAndPinnedUrl()
    {
        string block = AgentsManager.RenderBlock("0.4.0");

        Assert.StartsWith("<!-- winix:start v=0.4.0 ", block, StringComparison.Ordinal);
        Assert.EndsWith("<!-- winix:end -->", block, StringComparison.Ordinal);
        Assert.Contains("https://github.com/Yortw/winix/blob/v0.4.0/AGENTS.md", block, StringComparison.Ordinal);
        Assert.Contains("`winix list`", block, StringComparison.Ordinal);
        // The honest-framing restraint must survive the rework — this is the behaviour-changing core.
        Assert.Contains("otherwise use the default", block, StringComparison.Ordinal);
        Assert.Contains("No clear win? Keep the default", block, StringComparison.Ordinal);
        // Conventions line is now a single universally-true clause (no per-tool failure codes).
        Assert.Contains("exit 0 = success", block, StringComparison.Ordinal);
        Assert.DoesNotContain("125", block, StringComparison.Ordinal);
        Assert.DoesNotContain("126", block, StringComparison.Ordinal);
        // Block body uses LF only (EOL normalisation happens at merge time).
        Assert.DoesNotContain("\r", block, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Rewrite `RenderBlock_UserScope_AssertsAvailabilityAndKeepsRestraint`**

Replace the method body (currently lines 67–77). Delta: lead assertion changes from the old `"Prefer a Winix tool only when it's genuinely the better choice"` to the new `"Prefer a winix tool only when it genuinely beats the platform default"`; **removed** `"not by"`; kept the availability header, the no-conditional-escape-hatch guard, and `use the default`.

```csharp
    [Fact]
    public void RenderBlock_UserScope_AssertsAvailabilityAndKeepsRestraint()
    {
        string block = AgentsManager.RenderBlock("0.4.0", AgentsManager.RenderMode.UserScope);

        Assert.Contains("## Winix CLI tools (available on this machine)", block, StringComparison.Ordinal);
        Assert.Contains("Prefer a winix tool only when it genuinely beats the platform default", block, StringComparison.Ordinal);
        Assert.DoesNotContain("If Winix is not", block, StringComparison.Ordinal); // no conditional escape hatch in user mode
        Assert.Contains("use the default", block, StringComparison.Ordinal);
    }
```

- [ ] **Step 3: Extend `RenderBlock_ProjectScope_UsesConditionalWordingNoInstallAssertion` to pin the new project-mode lead phrasing (F6)**

This test (currently lines 79–91) already asserts the project header, `"If Winix tools are installed in your environment"`, `"If Winix is not"`, absence of `"(available on this machine)"`, the pinned URL, and `` `winix list` `` — all still true under the new project-mode lead and footer, so those assertions are preserved verbatim. Add **one** assertion so the new project-lead phrasing (`"genuinely beats the platform default"`) is pinned by a project-mode test rather than resting only on the user-mode test. Insert it immediately after the existing `"If Winix is not"` assertion (line 86):

```csharp
        Assert.Contains("genuinely beats the platform default", block, StringComparison.Ordinal); // new lead phrasing, project mode
```

Make no other change to this method. If you find yourself needing to edit the other assertions, stop: the new content has drifted from the design.

- [ ] **Step 4: Add the curation + footer + budget tests (the §7 centrepiece)**

Insert these methods immediately after `RenderBlock_DefaultModeIsUserScope` (after line 99, before the `// ── FindBlockVersion ──` banner). The negative/curation test uses **backtick-wrapped tokens** deliberately — a bare `"man"` would false-match "machine"/"command", and a bare `"less"` could match future prose; the contract is "no excluded tool is *named as a tool*", i.e. as a backticked token.

```csharp
    [Theory]
    [InlineData(AgentsManager.RenderMode.UserScope)]
    [InlineData(AgentsManager.RenderMode.ProjectScope)]
    public void RenderBlock_NamesEveryCuratedTrigger(AgentsManager.RenderMode mode)
    {
        string block = AgentsManager.RenderBlock("0.4.0", mode);

        // Group 1 + Group 2 tools that each earn a situational line.
        foreach (string tool in new[]
        {
            "online", "retry", "nc", "trash", "whoholds",
            "mkauth", "digest", "envvault", "protect", "unprotect", "mksecret",
            "ids", "notify", "qr", "clip",
        })
        {
            Assert.Contains("`" + tool + "`", block, StringComparison.Ordinal);
        }
        // The footer long-tail clause names files/wargs (a half-clause, not a scarce trigger).
        Assert.Contains("`files`/`wargs`", block, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AgentsManager.RenderMode.UserScope)]
    [InlineData(AgentsManager.RenderMode.ProjectScope)]
    public void RenderBlock_DoesNotNameExcludedHumanPresentationTools(AgentsManager.RenderMode mode)
    {
        // Curation invariant (negative/requirement test): presentation-for-human-eyes tools must
        // NOT be named as triggers — this is what stops the block drifting back into a full dump.
        // Asserted as backticked tokens so "man" doesn't false-match "machine"/"command", etc.
        string block = AgentsManager.RenderBlock("0.4.0", mode);

        foreach (string excluded in new[] { "treex", "peep", "less", "man" })
        {
            Assert.DoesNotContain("`" + excluded + "`", block, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(AgentsManager.RenderMode.UserScope)]
    [InlineData(AgentsManager.RenderMode.ProjectScope)]
    public void RenderBlock_FooterCarriesSymptomTriggeredGeneralSignal(AgentsManager.RenderMode mode)
    {
        // The footer's general-signal rule is symptom-triggered (keyed to an observable event),
        // not goal-restating — that is what lets it change behaviour. Pin both symptoms.
        string block = AgentsManager.RenderBlock("0.4.0", mode);

        Assert.Contains("Windows path-mangling", block, StringComparison.Ordinal);
        Assert.Contains("hand-parsing", block, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AgentsManager.RenderMode.UserScope)]
    [InlineData(AgentsManager.RenderMode.ProjectScope)]
    public void RenderBlock_StaysWithinScannabilityBudget(AgentsManager.RenderMode mode)
    {
        // §4 binding constraint: the block is always-loaded context. A regression that grows it
        // past the budget recreates the dilution it cures. The design ships 18 physical lines
        // incl. both markers in BOTH modes (the project lead is a single long physical line).
        // Pin the ceiling at the shipped value, not a loose +2, so any growth fails loudly; a
        // deliberate trim still passes. Independently pin the "~6 situation bullets" scarcity
        // discipline by counting bullet lines — that catches a 7th bullet even if total lines
        // happen to stay flat (e.g. a bullet added, a footer line removed).
        string block = AgentsManager.RenderBlock("0.4.0", mode);
        string[] blockLines = block.Split('\n');

        int lineCount = blockLines.Length;
        Assert.True(lineCount <= 18, $"block is {lineCount} lines; budget ceiling is 18 (mode={mode})");

        int bulletCount = 0;
        foreach (string line in blockLines)
        {
            if (line.StartsWith("- ", StringComparison.Ordinal)) { bulletCount++; }
        }
        Assert.Equal(6, bulletCount);
    }
```

- [ ] **Step 5: Add the cross-version drift-replacement test (F3)**

The realistic upgrade path: a file already holds the *old* governance block at a previous version, then `winix agents init` runs after upgrading to the version shipping this rework. `MergeBlock` must replace the old marker-delimited block wholesale with the new one — no duplication, no leftover old prose between the markers, version bumped. The marker / `FindBlockVersion` / replace logic is unchanged, so this is expected to pass; the test exists because the payload changed shape *and* length substantially, which is exactly when an untested drift-replace path bites. Add this method in the `// ── MergeBlock ──` region (e.g. immediately after `MergeBlock_ExistingBlock_ReplacesInPlacePreservingSurroundingText`). The fixture embeds a **literal** old-style block (not produced by `RenderBlock`, so it genuinely represents a pre-rework file on disk):

```csharp
    [Fact]
    public void MergeBlock_OldStyleGovernanceBlock_ReplacedWholesaleByNewTriggerBody()
    {
        // A pre-rework file: the literal OLD governance block at v=0.3.0 (markers are unchanged
        // across the rework, so FindBlockVersion still locates it). Re-init at the new version
        // must swap the whole body for the new trigger content — no leftover old prose.
        string oldBlock =
            "<!-- winix:start v=0.3.0 — managed by `winix agents init`; edits between markers are overwritten -->\n" +
            "## Winix CLI tools (available on this machine)\n\n" +
            "Prefer a Winix tool only when it's genuinely the better choice for the task — not by\n" +
            "default. If you can't say why it beats the platform default, use the default.\n\n" +
            "- **What's installed, flags, JSON shapes:** `winix list` and `<tool> --describe`\n" +
            "<!-- winix:end -->";
        string file = "# Project\n\n" + oldBlock + "\n\n## Other section\n";

        string merged = AgentsManager.MergeBlock(file, "0.4.0");

        // New body present, old governance prose gone.
        Assert.Contains("`online`", merged, StringComparison.Ordinal);
        Assert.Contains("otherwise use the default", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("genuinely the better choice", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("What's installed, flags, JSON shapes", merged, StringComparison.Ordinal);
        // Version bumped in the marker; surrounding text preserved; exactly one block.
        Assert.Contains("v=0.4.0", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("v=0.3.0", merged, StringComparison.Ordinal);
        Assert.StartsWith("# Project\n", merged, StringComparison.Ordinal);
        Assert.Contains("## Other section", merged, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(merged, AgentsManager.StartMarkerPrefix));
        Assert.Equal(1, CountOccurrences(merged, AgentsManager.EndMarker));
    }
```

(`CountOccurrences` is the existing private helper in this test class.)

- [ ] **Step 6: Fix the now-vacuous CRLF merge assertion in `MergeBlock_CrlfFile_PreservesCrlfEol`**

The existing assertion (line 194) checks no bare-LF was smuggled before the end marker, but it keys on the *old* last content line `"respected.\n<!-- winix:end"`, which no longer exists — making the assertion vacuously true. Retarget it to the new last content line (the URL line) so it still guards the CRLF contract. Replace:

```csharp
        // No bare LF block lines smuggled into a CRLF file.
        Assert.DoesNotContain("respected.\n<!-- winix:end", merged, StringComparison.Ordinal);
```

with:

```csharp
        // No bare LF block lines smuggled into a CRLF file (key on the new last content line).
        Assert.DoesNotContain("AGENTS.md\n<!-- winix:end", merged, StringComparison.Ordinal);
```

- [ ] **Step 7: Run the affected tests to verify they fail (red)**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~RenderBlock|FullyQualifiedName~MergeBlock_OldStyleGovernanceBlock"`
Expected: FAIL. The new/edited content tests (`RenderBlock_StableVersion_*`, `RenderBlock_UserScope_*`, `RenderBlock_ProjectScope_*` with the new lead assertion, `RenderBlock_NamesEveryCuratedTrigger`, `RenderBlock_FooterCarriesSymptomTriggeredGeneralSignal`) and the cross-version drift test fail because the current implementation still emits the old governance payload. `RenderBlock_DoesNotNameExcludedHumanPresentationTools` likely already passes against the old content (the old block names none of treex/peep/less/man) — that is fine; it guards the new contract going forward. `RenderBlock_StaysWithinScannabilityBudget` may pass or fail against the old body depending on its bullet count; either way it pins the new contract once Task 2 lands.

- [ ] **Step 8: Commit the failing tests**

```bash
git add tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "test(winix agents): encode curated trigger-payload contract for RenderBlock"
```

---

## Task 2: Rewrite the RenderBlock payload (green)

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs:98-143`

- [ ] **Step 1: Replace the body of `RenderBlock`**

Replace the entire method (the current lines 98–143, from `internal static string RenderBlock` through the closing `}` of the method) with the version below. The signature, the start/end markers, the `v={version}` stamp, `UrlRef`, the `string.Join("\n", …)` LF-only emission, and the user/project split are all preserved — only the rendered body text changes.

```csharp
    internal static string RenderBlock(string version, RenderMode mode = RenderMode.UserScope)
    {
        string urlRef = UrlRef(version);
        string header = mode == RenderMode.ProjectScope
            ? "## Winix CLI tools (if available in your environment)"
            : "## Winix CLI tools (available on this machine)";

        // Only the lead sentence differs between modes: user scope asserts the tools are present;
        // project scope is conditional and keeps the "ignore if not installed" escape hatch. The
        // trigger bullets and footer below are identical in both modes. Lowercase `winix tool` in
        // the user lead matches the CLI binary name (deliberate; see the triggers design §5).
        string lead = mode == RenderMode.ProjectScope
            ? "If Winix tools are installed in your environment, prefer one only when it genuinely beats the platform default — otherwise use the default. If Winix is not installed, ignore this section. It does when:"
            : "Prefer a winix tool only when it genuinely beats the platform default — otherwise use the default. It does when:";

        var lines = new List<string>
        {
            $"<!-- winix:start v={version} — managed by `winix agents init`; edits between markers are overwritten -->",
            header,
            "",
            lead,
            "",
            "- **A network op or command is flaky** — `online` blocks until connectivity (or a named endpoint) is healthy so you resume instead of polling/giving up; `retry` re-runs a failing command with backoff; `nc` checks whether a port is open.",
            "- **You're about to delete files** — `trash` removes them recoverably instead of `rm`.",
            "- **You need to know what's locking a file or holding a port** — `whoholds` (no native cross-platform equivalent).",
            "- **Building an authenticated request, or handling secrets** — `mkauth` (OAuth1/JWT/Basic/Azure headers), `digest` (hash/HMAC), `envvault` (keychain-backed env vars), `protect`/`unprotect` (encrypt-at-rest), `mksecret` (generate passwords/keys).",
            "- **You need a modern ID** — `ids` (UUIDv4/v7, ULID, NanoID).",
            "- **Acting for the user / keeping data local** — `notify` (ping them when a long task finishes or while away), `qr` (hand a URL to their phone — don't POST it to a web QR service), `clip` (copy to their clipboard, not a pastebin).",
            "",
            "Conventions: every tool has `--describe` (JSON schema) and `--json`; exit 0 = success; `NO_COLOR` honored.",
            "General signal: if a command fails from Windows path-mangling, or you're hand-parsing text a tool emits",
            "for humans, a winix tool may fit (Windows-native, `--json`) — e.g. `files`/`wargs` over `find`/`xargs`;",
            "`winix list` for the rest. No clear win? Keep the default. Full guidance:",
            $"https://github.com/Yortw/winix/blob/{urlRef}/AGENTS.md",
            "<!-- winix:end -->",
        };
        return string.Join("\n", lines);
    }
```

- [ ] **Step 2: Run the RenderBlock tests to verify they pass (green)**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~RenderBlock"`
Expected: PASS (all RenderBlock content, curation, footer, and budget tests).

- [ ] **Step 3: Run the full `Winix.Winix.Tests` project to confirm orchestration tests stay byte-stable**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj`
Expected: PASS. In particular the `MergeBlock_*` byte-stability tests (`MergeBlock_ReRunSameVersion_IsByteStable`, `MergeBlock_CrlfFileWithExistingBlock_ReMergeIsByteStable`), `RemoveBlock_*`, `FindBlockVersion_*`, and all `Run*` tests must still pass — they assert markers/URL/version/EOL, none of which changed. A failure there is a real regression, not an expected edit.

**Provenance note (F2 — verified, no new test needed):** the byte-stability tests build their fixtures by calling `MergeBlock(...)` (which internally calls `RenderBlock(...)`), e.g. `once = MergeBlock("# Project\n", "0.4.0"); twice = MergeBlock(once, "0.4.0"); Assert.Equal(once, twice)`. They do **not** embed a literal copy of the old block text, so they flow the *new* body through automatically — same-version re-merge idempotence at the new content is genuinely covered, not vacuously inherited. If, when you open the file, any byte-stability test turns out to hard-code literal block prose as its fixture, treat that as an F2 hit: replace the literal with `RenderBlock(version)` output so the no-op is pinned against the rendered value.

- [ ] **Step 4: Commit the payload rewrite**

```bash
git add src/Winix.Winix/AgentsManager.cs
git commit -m "feat(winix agents): rework init block into curated situation-triggers

Replace the inert governance-plus-pull-pointer payload with a curated,
situation-keyed trigger list (push, not pull) so an agent recognises when
to reach for a high-value tool. Fulfils the original feature's Decision 2
('inline core complete enough to change behaviour alone'). Plumbing
(markers, version stamp, pinned URL, mode split, atomic write) unchanged.

See docs/plans/2026-06-11-winix-agents-triggers-design.md."
```

---

## Task 3: Doc↔behaviour reconciliation (verify; modify only on contradiction)

**Files:**
- Verify: `AGENTS.md` (repo root), `src/winix/README.md`, `src/winix/man/man1/winix.1` (+ `winix.1.md` if present), `docs/ai/winix.md`

The block now *names* tools inline rather than only delegating to `--describe`. Per the project's doc-reconciliation rule, confirm no user-facing doc quotes or describes the *old* block prose in a way the new block contradicts. The repo `AGENTS.md` honest-framing/discovery sections already name tools and point to `--describe`, so they are expected to be consistent — this task confirms that and catches any doc that embedded the old governance text verbatim.

- [ ] **Step 1: Search the docs for old-block prose that the rewrite would contradict**

Run: `git grep -n "genuinely the better choice" -- "*.md"`
Run: `git grep -n "usage/runtime codes vary by tool" -- "*.md"`
Run: `git grep -n "What.s installed, flags, JSON shapes" -- "*.md"`
Expected: no hits outside the two `docs/plans/2026-06-*` design/ADR files (which are historical records and must NOT be edited). A hit in `README.md`/`winix.1`/`docs/ai/winix.md`/`AGENTS.md` is a doc that embedded the old block and now needs reconciling.

- [ ] **Step 2: Check whether the winix man page has a pandoc source before editing any rendered `.1`**

Run: `git ls-files "*.1.md"`
If `src/winix/winix.1.md` (or similar) is listed and step 1 found a hit in the rendered `winix.1`, edit the `.md` source and regenerate with `pandoc -s -t man src/winix/winix.1.md -o src/winix/man/man1/winix.1`, then safety-diff the `.1`. If no source is listed, the rendered `.1` is hand-maintained and edited directly. (Per the project rule, only the repo-wide `git ls-files '*.1.md'` listing counts as verification of source existence.)

- [ ] **Step 3: Reconcile only the contradicted prose, if any**

If step 1 found hits in shipped docs (not the historical design/ADR files): update the prose so it agrees with the new block (the block names triggers inline; the canonical detail still lives behind the version-pinned `AGENTS.md` URL and each tool's `--describe`). Scope is prose alignment only — do not restructure the docs. If step 1 found no shipped-doc hits, record that the reconciliation pass found no contradiction and skip to Task 4 (no commit needed).

- [ ] **Step 4: Commit any doc changes**

```bash
git add -A
git commit -m "docs(winix agents): reconcile prose with the new curated trigger block"
```

(Skip this commit entirely if step 3 made no changes.)

---

## Task 4: Full-suite verification + manual render check

**Files:** none modified — this task is verification only.

- [ ] **Step 1: Build the whole solution (warnings are errors)**

Run: `dotnet build Winix.sln`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test Winix.sln`
Expected: PASS across all projects. Pay attention to `Winix.Contract.Tests` — the `--describe` contract snapshots are unaffected by a block-payload change (the block is not part of `--describe`), so they must remain green with no snapshot regeneration. If a Contract snapshot fails, investigate before proceeding: it would mean the change touched a `--describe` surface, which it should not have.

- [ ] **Step 3: Manually render the new block and eyeball it against the design**

Run (PowerShell, from repo root):
```powershell
dotnet run --project src/winix/winix.csproj -- agents init --dry-run --json
```
The `--dry-run` writes nothing. Then render the actual block to a scratch dir to read it (user scope, redirected via the test override so no real `~/.claude` is touched):
```powershell
$env:WINIX_AGENTS_HOME = (New-Item -ItemType Directory -Force -Path "$env:TEMP\winix-agents-check\.claude").Parent.FullName
dotnet run --project src/winix/winix.csproj -- agents init --claude
Get-Content "$env:TEMP\winix-agents-check\.claude\CLAUDE.md"
Remove-Item -Recurse -Force "$env:TEMP\winix-agents-check"
Remove-Item Env:\WINIX_AGENTS_HOME
```
Expected: the printed block matches the design §5 user-scope rendering — 18 lines incl. markers, the six situation bullets, the conventions + symptom-triggered general-signal footer, and the version-pinned URL. Confirm the version in the marker/URL matches the dev version in `Directory.Build.props` (URL will pin to `main` if that version contains `-`). This is the doc-vs-behaviour oracle step: read the rendered output and confirm each design claim is actually present, hunting for any that is false.

- [ ] **Step 4: Final commit if the manual check surfaced any fix**

Only if step 3 revealed a discrepancy requiring a code change: fix it, re-run Task 2 steps 2–3, and commit. Otherwise this task produces no commit (verification complete).

---

## Known limitations (recorded so a future maintainer doesn't read them as oversights)

- **No test asserts the 15 named tools still exist as shipped binaries (F5, ADR D4).** The trigger list is static hand-curated text; a tool renamed or removed would leave a dead trigger naming a non-existent binary. Generating the list from tool metadata is explicitly deferred (design §8, ADR D4). Staleness is bounded by the version-stamped re-init and manual review — accepted for this version, not an omission.
- **Project-mode lead wording drift vs. user mode** is pinned by the (preserved + one-assertion-extended) `RenderBlock_ProjectScope_*` test plus the both-modes parametrised trigger/budget/footer tests; the modes deliberately share bullets + footer, so only header + lead are mode-specific and each is asserted in its own mode.

## Self-Review (run before handing off for execution)

**1. Spec coverage** — every design §7 testing requirement maps to a task:
- Required triggers present (both modes) → Task 1 Step 4 `RenderBlock_NamesEveryCuratedTrigger`.
- Excluded tools absent (negative/curation pin) → Task 1 Step 4 `RenderBlock_DoesNotNameExcludedHumanPresentationTools`.
- Governance guard retained → Task 1 Step 1 (`otherwise use the default` + `No clear win? Keep the default`).
- General-signal rule present → Task 1 Step 4 `RenderBlock_FooterCarriesSymptomTriggeredGeneralSignal`.
- Budget bound (≤ 18 lines + exactly 6 bullets, both modes) → Task 1 Step 4 `RenderBlock_StaysWithinScannabilityBudget`.
- Both modes (availability vs conditional, incl. project "ignore if not installed" + new lead phrasing) → Task 1 Steps 2 (user) + 3 (project).
- Version pinning unchanged → existing `RenderBlock_PreReleaseVersion_UrlUsesMainButMarkerKeepsExactVersion` + `UrlRef_*` tests (left intact). **F4(a) note:** that pre-release test calls `RenderBlock("0.4.0-dev")`, i.e. it runs against the *rewritten* method body after Task 2 — so it re-asserts `/blob/main/` pinning and the exact-version marker on the **new** content, satisfying design §7's "re-assert it survives the rewrite" without a new test. The rewritten `RenderBlock_StableVersion_*` (Task 1 Step 1) covers the stable `/blob/v{version}/` path on the new body.
- Idempotence/merge byte-stable at new content → Task 2 Step 3 (existing `MergeBlock_*` tests, provenance verified in the F2 note).
- Cross-version drift (old block → new body, wholesale replace) → Task 1 Step 5 `MergeBlock_OldStyleGovernanceBlock_ReplacedWholesaleByNewTriggerBody` (F3).
- Doc↔behaviour reconciliation → Task 3 + Task 4 Step 3.

**2. Placeholder scan** — no `TODO`/`TBD`/`Assert.True(true)`/"add appropriate…" steps; every code step shows complete code; every run step shows the exact command and expected result.

**3. Type/name consistency** — `RenderMode.UserScope`/`RenderMode.ProjectScope`, `AgentsManager.RenderBlock(string, RenderMode)`, `UrlRef` match the existing source exactly. New test method names are unique and do not collide with existing ones. The block content string in Task 1's description and Task 2's implementation are identical (same bullets, same footer, same leads).
