# ADR: winix agents — trigger-payload rework

**Date:** 2026-06-11
**Status:** Accepted
**Context:** The `winix agents init` block (from `2026-06-08-winix-agents`) ships a behaviourally
inert payload — generalised governance plus pull-pointers — that does not meet that feature's own
**Decision 2** ("the inline core must be complete enough to change behaviour alone"). An agent
receiving it learns *that* Winix exists, never *when* to reach for a tool. This ADR records the
decisions for reworking the block's payload into a curated, situation-keyed trigger list.
**Design doc:** [2026-06-11-winix-agents-triggers-design.md](2026-06-11-winix-agents-triggers-design.md)
**Supersedes:** the "block content" of `2026-06-08-winix-agents-design.md` only; all plumbing
decisions (D1, D3–D6 of the original ADR) stand.

---

## D1 — Rework the payload; fulfil Decision 2 rather than overturn it

- **Context.** The shipped block is inert (goal-restating governance + a pull-discovery path agents
  don't run). Is this a redesign or a content fix?
- **Decision.** Treat it as a **payload rework that delivers the original Decision 2 intent**, not a
  reversal. The block's purpose, markers, scope split, version pinning, and write mechanics are
  unchanged; only `RenderBlock`'s rendered text changes.
- **Rationale.** Decision 2 already mandated a behaviour-changing inline core; the content simply
  fell short of it. Framing this as "fulfil D2" keeps the change small and low-conflict — no
  plumbing, no new commands, no contract change.
- **Trade-offs accepted.** The block now contains more (and more opinionated) inline text than the
  minimal governance paragraph — a larger always-loaded footprint, bounded by D6.
- **Options considered.** Leave it as-is (rejected: inert, the feature's value is unrealised);
  rebuild the whole feature (rejected: the plumbing is sound and well-tested — only the payload is wrong).

## D2 — Curate by "recognisable situation where the default is genuinely worse"

- **Context.** 28 tools cannot all earn always-loaded lines; an index is the inert dump again.
- **Decision.** A tool earns a line only when there is a recognisable situation in which (a) the
  output feeds the agent's decision or is a needed deliverable, **and** (b) the agent's default move
  is *genuinely* worse — brittle, wrong, destructive, a privacy leak, or "give up." Three composable
  filters implement it: (1) exclude human-presentation tools, (2) exclude tools that duplicate the
  agent's native capability, (3) require the (a)/(b) bar. A "CI/scripting-heavy → in" heuristic
  reinforces inclusion.
- **Rationale.** The disease is inattention to a generalised blob; the cure is a short list of sharp,
  *recognisable* triggers. Selecting on "where the agent would otherwise do worse" maximises the
  behaviour-change-per-line.
- **Trade-offs accepted.** A curated subset is not exhaustive — some bar-passing tools are omitted
  (D5). The full catalogue stays reachable via `winix list`.
- **Options considered.** List every tool (rejected: dilution = the original disease); list only by
  category without naming tools (rejected: less actionable than naming the tool at the trigger);
  generate from `.Platform` metadata (rejected: D4).

## D3 — Two groups: "your task is blocked" vs "keep it local / offer to the user"

- **Context.** Some valuable tools serve the *human* (notify/qr/clip), not the agent's own reasoning
  — but still clear the bar via a different value (privacy / proactive-offer). Mixing them into the
  "you're blocked, reach for this" list would dilute that list's signal.
- **Decision.** Two labelled groups. **Group 1** "your task hit this → reach for it" (agent consumes,
  default worse): the network/flaky cluster (`online`/`retry`/`nc`), `trash`, `whoholds`, the
  auth/secrets cluster (`mkauth`/`digest`/`envvault`/`protect`/`unprotect`/`mksecret`), `ids`.
  **Group 2** "keep it local / offer to the user" (human consumes, but privacy/offer value):
  `notify`, `qr`, `clip`.
- **Rationale.** The two groups answer different questions ("am I stuck?" vs "can I help the user / am
  I about to leak data?"). Separating them keeps Group 1's "you're stuck" signal sharp, and gives
  Group 2 the framing that earns it — *keep data local* (don't POST a payload to a web QR service or a
  pastebin) and *proactive offer* (a capability the agent wouldn't web-search for).
- **Trade-offs accepted.** Group 2 is the weakest cluster; `clip`/`qr` are the first to trim under
  budget pressure (D6).
- **Options considered.** Drop the human-facing tools (rejected: privacy + offer value is real and
  passes the bar — without the note an agent may leak a payload to a third-party web service); one
  undifferentiated list (rejected: dilutes Group 1).

## D4 — Static hand-curated text, not generated from tool metadata

- **Context.** The trigger content overlaps the per-tool `.Platform` `value_on_windows`/`_unix`
  statements; it could be generated from them.
- **Decision.** Keep the trigger list **static, hand-curated** in `RenderBlock`, rendered from the
  version string alone. No manifest/`.Platform` coupling.
- **Rationale.** The original ADR deferred "embedding the full tool list inline … to avoid manifest
  coupling/staleness." A curated *handful of triggers* is categorically different from the *full
  enumeration* that deferral ruled out, and curation is a judgement (which situations are sharp,
  which default is worse) that generation cannot make. Staleness is bounded: the block is
  version-stamped and `winix agents init` refreshes it; the named tools are stable core tools.
- **Trade-offs accepted.** A second place (besides each tool's own `--describe`) to keep roughly in
  sync as the suite evolves — a small, low-frequency maintenance surface.
- **Options considered.** Generate from `.Platform` (rejected: coupling the original ADR avoided, and
  it can't make the curation judgement); pull everything at runtime (rejected: that is the inert
  status quo).

## D5 — Footer carries the long tail + a symptom-triggered general-signal rule

- **Context.** A finite trigger list cannot cover every situation; `files`/`wargs` are a real-but-
  narrow win that doesn't earn a scarce Group-1 line.
- **Decision.** The footer carries: (i) the conventions (`--describe`/`--json`/exit-0/`NO_COLOR`);
  (ii) a **symptom-triggered** general-signal rule — *"if a command fails from Windows path-mangling,
  or you're hand-parsing human-text output, a winix tool may fit (`--json`, Windows-native) — check
  `winix list`"* — carrying its own concrete example (`files`/`wargs` over `find`/`xargs`); (iii) an
  anti-over-reach guard ("No clear win? Keep the default"); (iv) the version-pinned URL.
- **Rationale.** "Symptom-triggered generic" (keyed to an observable event) changes behaviour where
  "goal-restating generic" ("use the best tool") cannot. Riding the rule with its example keeps it
  from floating into blob-land. The long-tail fallback (`winix list`) is the *correct* place for the
  weaker pull-mechanism — the named triggers carry the common cases with push; the tail gets a softer
  nudge by design.
- **Trade-offs accepted.** Marginal coverage (the strongest path-mangling/text-parse instances are
  already named triggers); it leans on the pull-path for the tail.
- **Options considered.** A prominent dedicated bullet for the rule (rejected: spends a scarce line
  for incremental coverage); omit it (rejected: loses long-tail generalisation, which beats
  enumeration for coverage); a goal-restating "use the best tool" line (rejected: inert — the
  original disease).

## D6 — Scannability budget is the binding constraint

- **Context.** The block is loaded into always-loaded context every session.
- **Decision.** Design to a tight, scannable budget — ~6 situation bullets, on the order of 12–18
  physical lines incl. markers/footer — optimising **scarcity over completeness**, and enforce a
  hard line-ceiling regression test.
- **Rationale.** A long block recreates the dilution it cures (the agent skims and tunes out).
  Scarcity *is* the design; the budget is what forces clustering-by-situation and the omission of
  marginal tools.
- **Trade-offs accepted.** Genuinely useful tools are omitted to protect the budget (D2/D5).
- **Options considered.** No budget / completeness (rejected: reproduces the inert blob); a much
  tighter 3–4 line block (rejected: too sparse to cover the high-value situations).

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| Generate triggers from `.Platform` metadata | Manifest coupling the original ADR avoided; static curation is consistent and can make the judgement generation cannot (D4). |
| Trigger lines for `url`/`when`/`schedule`/`squeeze`/`hcat`/`demux` | Pass the (a)/(b) bar in niches but lose the scarce-line budget contest; reachable via `winix list` (D6). |
| Generating from / reconciling the full repo `AGENTS.md` "honest framing" prose | The block is the surface that matters; a `--describe`-vs-block consistency pass is in-scope, a full prose rewrite is not. |
| A deterministic hook for the top tools (e.g. `online`) | A stronger mechanism than advice for the highest-value cases, but harness/settings work, not a `winix agents` change. |
| Per-machine usage telemetry to data-drive curation | No telemetry exists; curation is judgement-based for now. |
