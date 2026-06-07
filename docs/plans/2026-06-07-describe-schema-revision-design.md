# Design: Coordinated `--describe` Schema Revision for Agent Adoption

**Date:** 2026-06-07
**Status:** Approved (brainstorm with Troy, section-by-section)
**Implements:** Recommendations 1–3 of [Agent Adoption Hardening](2026-06-06-agent-adoption-hardening-design.md). Recommendation 4 (`winix init`) follows as the next v0.4.0 work item with its own design cycle.
**ADR:** [2026-06-07-describe-schema-revision-adr.md](2026-06-07-describe-schema-revision-adr.md)

## Goal

Turn the suite's machine contract from prose promises and ad-hoc per-tool pins into uniform, enforced, machine-readable guarantees: a versioned `--describe` envelope, an honest per-tool maturity signal, machine-actionable "prefer the incumbent when…" guidance, a suite-wide contract-lock test, and a written stability policy.

All three schema additions land as ONE coordinated revision so `schema_version` exists from the first envelope that carries the new fields.

## 1. Schema changes (ShellKit emitter + builder APIs)

All changes centralise in `CommandLineParser.GenerateDescribe()` (`src/Yort.ShellKit/CommandLineParser.cs:919`):

- **`"schema_version": 1`** — first field in the envelope (before `"tool"`), unconditional, a ShellKit constant. Versions the DESCRIBE ENVELOPE STRUCTURE ONLY (field names, nesting, types). Additive fields do NOT bump it; renames/removals/type changes DO. The constant carries an XML doc + comment stating the bump rule.
- **`"maturity": "core" | "fresh"`** — emitted after `"version"`. New builder API `.Maturity(ToolMaturity)` with enum `ToolMaturity { Core, Fresh }` in ShellKit. REQUIRED for Winix tools: an unset maturity emits nothing, and the contract-lock harness fails that tool ("maturity unset") — a new tool cannot ship untiered. ShellKit itself stays usable outside Winix (no parser-level default/throw).
- **`"prefer_default_when": ["…", …]`** — optional builder API `.PreferDefaultWhen(params string[])`, emitted adjacent to the `platform` block (conceptual siblings: "what I replace" / "when not to replace it"; the exact position is chosen at implementation and pinned thereafter by the contract snapshots). Absent when unconfigured; absence means "no guidance".

Every parser — including subcommand parsers — emits all three (same envelope structure), so `mkauth jwt --describe` carries the contract fields too.

Per-tool wiring: `.Maturity(…)` chained where each tool builds its parser; `.PreferDefaultWhen(…)` only on tools with genuine incumbents. ShellKit unit tests cover presence/absence/ordering of all three fields.

## 2. Maturity tiers

**Rule (also in STABILITY.md):** `core` = completed multi-round review to round-stop AND survived ≥1 stable release in the wild without interface-breaking changes. Otherwise `fresh`. Promotion = a one-line `.Maturity()` change + snapshot update, expected the release after exposure.

**Initial assignment — 23 core / 5 fresh (counting protect and unprotect separately, matching the 28-entry llms.txt / NuGet-ID canon):**
- core (23): timeit, squeeze, peep, wargs, files, treex, man, less, whoholds, schedule, nc, winix, retry, when, clip, ids, digest, notify, url, qr, protect, unprotect (one library, two binaries — same tier by definition), envvault
- fresh (5): mksecret, trash, hcat, mkauth, demux — round-stop-reviewed 2026-06-07, zero wild exposure; promote at v0.5.0

**Surfacing:** `--describe` (machine signal); llms.txt (a `(fresh)` marker on the five lines + one header sentence defining the tiers, linking STABILITY.md; core is the unmarked default); root README (one paragraph under the Tools table — definitions, the five fresh tools, STABILITY.md link; no per-row column). docs/ai guides of the five fresh tools get a one-line maturity note. AGENTS.md gets ONE sentence pointing at the `maturity` field (the fuller AGENTS.md refresh stays deferred as recorded).

## 3. `prefer_default_when` content

**Shape:** 1–4 short clauses per tool, each an actionable fragment naming the case and the incumbent (e.g. files: `"complex find expressions (-perm, -newer, -exec chains) — use find directly"`).

**Binding distillation rule:** entries may only CONDENSE existing reviewed prose — the tool's `docs/ai/{tool}.md` "When NOT to use" section or its README incumbent comparison. No new claims. No prose source → no field (correct, not a gap).

**Indicative coverage (finalised at plan time by reading each guide):** files→find/fd, treex→tree, wargs→xargs, less/man→system pagers, nc→netcat/ncat, squeeze→gzip/zstd, timeit→time/hyperfine, when→date, clip→native clipboard tools, digest→sha256sum/openssl, whoholds→lsof, schedule→crontab/schtasks, trash→rm, hcat→python -m http.server/miniserve, demux→tee/awk. Likely 15–18 of 28 tools.

**Verification leg:** the post-build review's docs-auditor checks BOTH directions — every emitted hint traces to existing prose (cite source line), and no docs/ai "When NOT to use" section with a real incumbent case is left un-distilled.

## 4. Contract-lock harness

**Project:** new `tests/Winix.Contract.Tests` (xUnit, in `Winix.sln`), referencing all 28 class libraries, `UseSystemResourceKeys` mirrored.

**Adapter table:** static registry, one entry per describe surface: key (`tool` or `tool/subcommand`) → `Func` invoking the library seam with given args, returning captured stdout. Each lambda wraps its tool's seam shape (sync `Run`, `RunAsync`, wargs' TextReader, nc's byte-stream) the same way that tool's own seam tests do. Multi-subcommand tools contribute entries per subcommand (enumerated at plan time from each dispatch table).

**Test (one `[Theory]` over the registry):**
1. Invoke with `["--describe"]` / `[sub, "--describe"]`; assert exit 0 + empty stderr.
2. Parse stdout as JSON; normalise: `version` value → `"<normalised>"`. Nothing else masked — any other variance is a finding, not noise.
3. Byte-equal compare against checked-in `snapshots/{key}.describe.json` (pretty-printed for reviewable diffs).
4. Assert `schema_version` present and `maturity` ∈ {core, fresh} (the can't-ship-untiered gate).

**Failure UX:** assertion message shows a unified diff plus: "Intentional contract change? Re-run with `WINIX_UPDATE_SNAPSHOTS=1` to regenerate, commit the snapshot diff, and bump schema_version if the envelope STRUCTURE changed. See docs/STABILITY.md." Update mode writes the snapshot AND fails the run (CI can never silently self-update).

**Platform variance:** design assumption — describe output is platform-invariant after version masking (the `.Platform()` block emits both `value_on_windows` and `value_on_unix` unconditionally). The plan carries an explicit PROBE task: run the harness on Windows + WSL before pinning; any genuinely per-OS field becomes a recorded per-tool decision (normalise it or split snapshots), not an improvisation.

**New-tool checklist (CLAUDE.md):** add one line — register the tool in the contract harness + commit its snapshot.

## 5. `docs/STABILITY.md`

One page, consumer-facing, scoped to the agent surface:
1. **Covered:** flag names/shapes, exit-code meanings, `--json` field names, the `--describe` envelope. NOT covered: human-readable text (help prose, error wording, summaries), performance, undocumented behaviour.
2. **Rules:** additive changes may land any release; renames/removals/meaning-changes are breaking. Deprecation: a renamed/removed flag keeps the old name as a working alias for ≥2 minor releases with a one-line stderr notice; `--json`/exit-code breaks get the same two-release runway via parallel emission where feasible.
3. **`schema_version`:** what it versions, the bump rule, current value.
4. **Tiers:** core/fresh definitions, promotion rule, read it from `--describe`.
5. **Enforcement:** the contract-lock suite — every machine-surface change is a reviewable snapshot diff; CI fails on undeclared drift.
6. **0.x honesty:** `fresh` tools may move faster; the deprecation rule binds `core` strictly, `fresh` best-effort.

Linked from: root README (section-2 paragraph), AGENTS.md (one sentence), llms.txt header, harness failure message.

## 6. Ripple + verification

- **Existing describe-pin tests** (clip shape pin, digest SHA-3 reflection pin, help/describe tests in mksecret/mkauth, etc.): additive fields break full-shape pins — the anticipated, recorded contract-change class. Assertions get EXTENDED, never weakened. A caller-audit grep for `--describe` assertions across all 28 test projects happens at plan time so the count is known up front.
- **Doc bookkeeping:** the AI-discoverability ADR's deferred `schema_version` row closes with a pointer here; the 2026-06-06 recommendations doc gets a status note (Recs 1–3 implemented, Rec 4 next).
- **Verification:** ShellKit emitter units; harness green on Windows + WSL with all snapshots committed; docs-auditor two-direction hint check + STABILITY.md claims-vs-behaviour; full solution suite; adversarial-plan-review before build; post-build 4-reviewer round per house process.

## Out of scope (recorded)

- `winix init` (Rec 4) — next v0.4.0 work item, own design cycle.
- Publishing snapshots as consumer-visible contract docs (e.g. `docs/contract/`) — cheap follow-up decision once the snapshots exist.
- Fuller AGENTS.md refresh — deferred as previously recorded.
