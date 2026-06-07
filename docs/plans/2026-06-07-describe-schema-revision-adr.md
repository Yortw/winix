# ADR: `--describe` Schema Revision for Agent Adoption

**Date:** 2026-06-07
**Status:** Accepted
**Context:** Implements Recommendations 1–3 of the [agent adoption hardening review](2026-06-06-agent-adoption-hardening-design.md) as one coordinated schema revision before v0.4.0 tags. Design: [2026-06-07-describe-schema-revision-design.md](2026-06-07-describe-schema-revision-design.md).

---

## D1 — `schema_version` versions the describe envelope only

- **Context:** The AI-discoverability ADR deferred schema versioning ("add when the schema changes"). Adding maturity/prefer_default_when IS that change — consumers need a version to branch on from the first envelope that carries new fields.
- **Decision:** Integer `schema_version: 1`, first field, ShellKit constant. Scope: the structure of `--describe` itself. Additive fields don't bump; renames/removals/type changes do.
- **Rationale:** Single-purpose and cheap. The broader contract (per-tool `--json` fields, exit codes) is enforced by the contract-lock snapshots and promised by STABILITY.md — a number can't carry that nuance.
- **Trade-offs accepted:** Consumers must read STABILITY.md for the full contract story; the number alone won't signal a tool-level `--json` break (the snapshot diff + deprecation rule covers that).
- **Options considered:** Whole-agent-contract version (coarse — one tool's break bumps the suite, noisy); per-tool contract versions (28 numbers, heavier than 0.x warrants).

## D2 — Maturity vocabulary `core`/`fresh`; rule = round-stop review + one stable release of exposure

- **Context:** Review-history alone no longer discriminates (every tool has now had multi-round review); age alone undersells the v0.4.0 tools' review depth.
- **Decision:** `core` = round-stop review AND ≥1 stable release in the wild without interface breaks; otherwise `fresh`. Initial: 23 core / 5 fresh (mksecret, trash, hcat, mkauth, demux — promote at v0.5.0). Vocabulary `fresh`, not `experimental`.
- **Rationale:** The combination is the only honest discriminator. "fresh" states the truth (reviewed, unexposed) without "experimental"'s unfair implication of un-reviewed instability.
- **Trade-offs accepted:** `fresh` is a less-standard term than `experimental` — STABILITY.md defines it where consumers look.
- **Options considered:** `core/experimental` (the source doc's words — overstates risk for heavily-reviewed tools); `stable/preview` ("stable" overpromises at 0.x); review-history-only or age-only rules (non-discriminating / unfair, respectively).

## D3 — `.Maturity()` required-by-harness, not required-by-parser

- **Context:** A new tool must not ship untiered, but ShellKit is a general-purpose parser usable outside Winix.
- **Decision:** Parser emits nothing when unset; the contract-lock harness fails any Winix tool whose describe lacks `maturity`.
- **Rationale:** Puts the Winix-specific policy in the Winix-specific gate; ShellKit stays unopinionated.
- **Trade-offs accepted:** Enforcement lives one step away from the API; mitigated by the harness being mandatory in `dotnet test Winix.sln`.
- **Options considered:** Parser throws when unset (breaks non-Winix consumers); default to `fresh` silently (a guess that masks the omission).

## D4 — `prefer_default_when` distilled-only, absent-when-none

- **Context:** The breadth-vs-depth guardrail lives only in AGENTS.md prose, invisible to agents at runtime; but fabricated guidance violates the suite's factual-rigor rules.
- **Decision:** Optional string array; entries may only condense existing reviewed prose (docs/ai "When NOT to use" / README comparisons); tools without a real incumbent case omit the field. Docs-auditor verifies both directions (every hint traces to a source; no real case left un-distilled).
- **Rationale:** Machine-actionable guardrail with zero invented claims.
- **Trade-offs accepted:** Agents can't rely on the field existing; absence-means-no-guidance is documented in STABILITY.md and the field docs.
- **Options considered:** Mandatory on all tools (forces filler); defer to the `winix init` cycle (a second describe-touching pass in the same release — wasteful).

## D5 — Contract-lock harness: in-process seam invocation with checked-in snapshots

- **Context:** Stability is asserted in prose and pinned ad-hoc by a few tools; the seam retrofit (27/27 tools, completed 2026-06-06) makes uniform in-process enforcement cheap for the first time.
- **Decision:** `tests/Winix.Contract.Tests` referencing all 28 libraries; per-surface adapter registry (incl. subcommand describes); normalise only the `version` field; byte-equal compare against pretty-printed checked-in snapshots; env-gated regeneration (`WINIX_UPDATE_SNAPSHOTS=1`) that writes AND fails; failure message carries the update/bump instructions.
- **Rationale:** Fast, deterministic, 3-OS via `dotnet test Winix.sln`, one line per future tool, and the snapshots double as machine-readable contract artifacts.
- **Trade-offs accepted:** Doesn't exercise Program.cs/console wiring (describe is parser-emitted inside the seam; smokes cover binaries); adapter table is hand-maintained (new-tool checklist line + the harness itself fails on a missing-but-snapshotted tool, and a registry-vs-CLAUDE.md-tool-list count assert catches a missing registration).
- **Options considered:** Spawned-binary harness (slow, path-fragile, duplicates the smoke fixtures' role); generation-time pinning via custom build step (more moving parts; its one advantage — consumer-visible artifacts — is captured by the checked-in snapshots anyway).

## D6 — Stability policy in `docs/STABILITY.md`

- **Context:** The deprecation/promotion rules need a citable home; no CONTRIBUTING.md exists.
- **Decision:** Dedicated one-pager scoped to the agent surface; linked from README, AGENTS.md, llms.txt, and the harness failure message. Deprecation rule: ≥2 minor releases of aliasing + stderr notice; binds `core` strictly, `fresh` best-effort.
- **Rationale:** Consumer-facing promise, not a contributor rule; a dedicated file is linkable from every surface including error messages.
- **Trade-offs accepted:** One more doc to keep honest — mitigated by the docs-auditor check (STABILITY.md claims vs behaviour) in the review round.
- **Options considered:** CONTRIBUTING.md (wrong audience, near-empty otherwise); README section (too long for the just-rebuilt README; it gets a summary + link instead).

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| `winix init` (Rec 4) | Next v0.4.0 work item with its own brainstorm→ADR→plan→review cycle — it writes into users' project files (idempotency/removal semantics need real design). |
| Publishing snapshots as consumer-visible contract docs (`docs/contract/`) | Cheap follow-up once snapshots exist; mechanism unaffected. |
| Fuller AGENTS.md refresh | Pre-v0.3.0 drift, previously recorded; only the one maturity-field sentence lands now. |
| Per-OS snapshot splitting | Only if the plan-time Windows+WSL probe finds genuine platform variance; decided per-tool then, not speculatively. |
| Maturity surfacing as a README table column | Rejected for noise (5 exceptions in 28 rows); revisit only if the fresh set grows. |
