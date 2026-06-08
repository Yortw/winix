# Design Notes: Hardening Winix for Agent Adoption & Consumer Confidence

> **Status as of 2026-06-08:** Recs 1–3 (the `--describe` schema revision — `schema_version`, maturity tiers `core`/`fresh`, `prefer_default_when` hints, contract-lock test suite `tests/Winix.Contract.Tests/`, `docs/STABILITY.md`) were **IMPLEMENTED** on `release/v0.4.0`. See [2026-06-07-describe-schema-revision-design.md](2026-06-07-describe-schema-revision-design.md), [-adr.md](2026-06-07-describe-schema-revision-adr.md), and [-plan.md](2026-06-07-describe-schema-revision-plan.md) (all in this `docs/plans/` directory). **Rec 4 (`winix init`)** is the next piece of work — not yet started.

**Date:** 2026-06-06
**Status:** Recommendations (for triage — no decisions taken yet)
**Author/context:** Produced by a fresh-eyes review. A Claude Code instance was pointed at the public repo (`release/v0.4.0`) cold — as if encountering it for the first time — and asked "is this useful / should we adopt it?", then asked to capture the resulting concerns as actionable design notes. This is an *external consumer's* perspective, not the maintainer's.
**Related:** [AI Discoverability ADR](2026-03-31-ai-discoverability-adr.md), [CLI Conventions](2026-03-29-winix-cli-conventions.md)

---

## TL;DR

The suite is genuinely good and the verdict was "yes, adopt it." The concerns that came out of the review are mostly *not* about the code — they're about making the suite's **trustworthiness legible and enforced**, which matters disproportionately for an AI-agent consumer who depends on the interface contract.

The through-line of the actionable items: **Winix's differentiator is its machine contract (`--describe`/`--json`/exit codes). Lean into it.** Turn the things that are currently *prose promises* or *ad-hoc per-tool tests* into *uniform, enforced, machine-readable guarantees.* That does more for adoption confidence than a 1.0 version bump ever would.

---

## Baseline verification (done during the review)

Before recommending anything, the installed `release/v0.4.0` binaries were exercised. All passed:

- **Breadth:** all 29 tools emit a valid `--describe` contract (`{"tool":"<name>",…}`).
- **Depth (representative, real I/O):**
  - `digest -s "abc"` matches the published SHA-256 vector (known-answer test).
  - `digest --verify` returns 0 on a correct hash.
  - `squeeze --zstd` → `squeeze -d -c` is **byte-identical** to the original (round-trip integrity).
  - `files --ext md` locates a known file.
  - `wargs echo` runs over stdin lines (composition works).
  - `timeit --json` reports `wall_seconds` for a child process.
  - `treex --size` renders a tree.
  - `ids` emits a well-formed UUIDv7.

This is recorded only as context — the recommendations below are about confidence *at scale and over time*, not about any failing behaviour found.

---

## Recommendation 1 — Make the agent-facing contract a *uniform, enforced* guarantee

**Concern (from review):** "Young, v0.x — will my scripts/agents break?"

**Root issue:** The real fear isn't the version number; it's interface drift. A consumer (especially an agent) binds to flag names, `--json` field names, exit-code meanings, and the `--describe` schema. Today:
- Stability is asserted as a *constraint* in the discoverability ADR ("JSON schema must be stable, additive only") but is **not enforced suite-wide**.
- Some tools pin their `--describe` shape **ad-hoc** (e.g. `Winix.Clip.Tests` has a "--describe shape regression pin"; `Winix.Digest.Tests` pins SHA-3 reflection in describe). Good, but uneven — there's no guarantee every tool is covered, or covered the same way.
- `--describe` **schema versioning is explicitly deferred** (discoverability ADR, deferred table: "add `schema_version` when the schema changes").

**Recommended actions:**
- [ ] Add a **uniform contract-lock test** that runs for *every* tool: snapshot the `--describe` JSON, the `--json` output field names, and the exit-code map; fail CI on any change not accompanied by an explicit snapshot update. (A single shared test harness in `Yort.ShellKit.Tests` driven over the tool list, rather than 29 hand-written pins.)
- [ ] Introduce the deferred `"schema_version"` field in `--describe` now, *before* the schema needs to change, so consumers have a version to branch on from day one.
- [ ] Write a one-paragraph **stability policy** (in `CONTRIBUTING.md` or a dedicated doc) scoped exactly to the agent surface: flag names, `--json` fields, exit codes, `--describe` schema. State the deprecation rule (e.g. a removed/renamed flag keeps the old name as an alias for N minor versions, with a stderr deprecation notice).

**Why this beats the obvious alternative (cut a 1.0):** A major-version bump changes no behaviour and gives a consumer no actual guarantee. An *enforced, versioned contract* is a structural advantage over the very incumbents Winix competes with — `find`/`xargs` have decades of de-facto stability but no machine contract; Winix could have an *enforced* one at 0.4.

**Priority:** High. This is the single highest-confidence-per-unit-effort item for agent adoption.

---

## Recommendation 2 — Tier the suite by maturity (`core` vs `experimental`)

**Concern (from review):** "Single maintainer, ~29 tools — wide surface for one person; bus-factor."

**Root issue (refined):** For a *consumer who can build from source* (the case here — a full AOT rebuild of all 29 tools took ~10 minutes from a pinned SDK, and it's MIT), "maintainer disappears" is largely survivable: fork, patch, rebuild. So the consumer-side risk is lower than it first looks. What's left is the **maintenance load** and the **implicit uniform support promise** across 29 tools of varying age — `timeit` (months old, tier-2 reviewed) and `mkauth`/`demux`/`hcat` (days old) currently carry the same implied bar.

**Recommended actions:**
- [ ] Define two tiers — `core` (stable, supported, heavily tested) and `experimental` (works, but interface may move) — and assign every tool to one.
- [ ] Surface the tier as a **machine-readable field in `--describe`** (e.g. `"maturity":"core"|"experimental"`) so an agent can be appropriately cautious reaching for a young tool.
- [ ] Reflect the tiering in `llms.txt` and the README so human and agent expectations are set honestly.

**Why:** This manages the breadth risk *honestly* instead of pretending all 29 tools are equally battle-tested. It concentrates the maintenance bar where it's earned and gives consumers a real signal.

**Priority:** Medium. Low effort, high honesty payoff.

---

## Recommendation 3 — Push the "prefer the default when…" guidance into the machine contract

**Concern (from review):** "Breadth vs depth — mature incumbents (`find`, `less`, `nc`) have decades of edge-case hardening; young Winix tools don't."

**Root issue:** The *philosophy* here is already correct and closed — `AGENTS.md` explicitly says "if you can't articulate why a Winix tool is better, use the default." The gap is that this guidance lives **only in prose** (`AGENTS.md` / `docs/ai/`), and — as `AGENTS.md` itself admits — an agent working in an *unrelated* project never sees those files. So the guardrail that's supposed to stop misuse is invisible at exactly the moment it's needed.

**Recommended actions:**
- [ ] Add structured "when NOT to use me / prefer the incumbent" hints to `--describe` (e.g. a `"prefer_default_when": ["deep/edge-case use of find-style queries", …]` array, or per-tool `not_recommended_for`).
- [ ] Keep the prose guides as the human-readable expansion, but make the *guardrail itself* machine-actionable, since `--describe` is the one source an agent reliably reaches at runtime.

**Why:** Turns the breadth-vs-depth guardrail from a hope ("the agent will have read AGENTS.md") into data the agent acts on. Complements, doesn't duplicate, the existing `docs/ai/` "When NOT to use this" sections.

**Priority:** Medium.

---

## Recommendation 4 — Ship `winix init` (project-level discoverability)

**Concern (from review / underlying everything):** An agent working in an unrelated project has **no automatic way to know Winix is installed or preferred.** `AGENTS.md` names this exact gap and says the intended fix — a `winix init` subcommand that writes a pointer into the project's `CLAUDE.md`/`AGENTS.md` — is "on the roadmap but not yet shipped."

**Status check:** Confirmed unshipped. The [AI Discoverability ADR](2026-03-31-ai-discoverability-adr.md) covers `--describe`, `llms.txt`, and `docs/ai/`, but its deferred list only mentions suite-level `winix --list --describe` — the **project-pointer `init` is not yet designed**.

**Recommended actions:**
- [ ] Design and ship `winix init`: writes/updates a short, clearly-delimited Winix pointer block into the current project's `CLAUDE.md` and/or `AGENTS.md` (idempotent; re-runnable; removable).
- [ ] Have the pointer block reference `llms.txt` and the `--describe` convention so any agent loading that project picks up the suite automatically.

**Why this is the highest-leverage item overall:** Without it, the entire suite is invisible to agents in other repos, and every project needs a *manual* "use Winix here" step. (This review had to set up exactly such a manual pointer + usage log by hand — `winix init` is the productised version of that workaround.) Every other recommendation improves a contract that this one is required to even *surface*.

**Priority:** High (arguably highest).

---

## Explicitly NOT recommended

| Non-action | Why |
|---|---|
| Cut a 1.0 purely to raise confidence | Theatre — changes no behaviour and gives consumers no guarantee. Spend the effort on Recommendation 1 (enforced contract) instead. A 1.0 is worth cutting when the *contract is locked*, as a signal that it is — not as the mechanism. |
| "Address" the incumbents/maturity concern at the philosophy level | Already closed — `AGENTS.md` says the right thing. The only actionable slice is Recommendation 3 (make that guidance machine-readable). |
| Solve bus-factor by "getting more maintainers" | Not willable into being, and partially mitigated already by MIT + reproducible-from-source builds (see Recommendation 2). |

## Counter-signal worth recording

The "young = risky" concern is **partially self-rebutting** given visible evidence of adversarial hardening already in the codebase. Example: the `squeeze` AI guide documents real tier-2-review fixes — truncated-gzip integrity validation (was silently producing partial output), multi-member gzip now rejected loudly rather than silently accepting truncation, and a binary-safe stdin fix (was UTF-8 round-tripping and corrupting non-UTF-8 bytes). That is not the profile of an unreviewed young project. The recommendations above are about making that quality *legible and enforced*, not about creating it from scratch.

---

## Suggested ordering for an implementing instance

1. **Rec 1** (contract-lock test + `schema_version` + stability policy) — protects everything else.
2. **Rec 4** (`winix init`) — makes the suite discoverable; unblocks real-world agent adoption.
3. **Rec 2** (maturity tiers in `--describe`) — cheap honesty.
4. **Rec 3** (`prefer_default_when` in `--describe`) — folds naturally into the Rec 1/Rec 2 describe-schema work.

Note that Recs 1–3 all touch the `--describe` schema, so doing them as one coordinated schema revision (with `schema_version` bumped once) is cheaper than three separate passes.
