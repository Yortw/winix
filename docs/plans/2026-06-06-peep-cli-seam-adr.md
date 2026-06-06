# ADR: peep Cli.RunAsync Seam Retrofit (seam phase 2)

**Date:** 2026-06-06
**Status:** Accepted
**Context:** Applies the phase-1 seam convention ([2026-06-06-cli-seam-retrofit-adr.md](2026-06-06-cli-seam-retrofit-adr.md), D1–D6 inherited unchanged) to peep. This ADR records only peep-specific decisions.
**Design doc:** [2026-06-06-peep-cli-seam-design.md](2026-06-06-peep-cli-seam-design.md)

---

## P1. The deferred "key-polling abstraction" question is closed as not-needed

**Context:** Phase 1's deferred table carried "peep key-polling abstraction (injectable key source vs stays-in-Main)". Code exploration shows the premise was wrong: the interactive loop was never in `Program.cs` — `InteractiveSession` is already library-resident with its own internal `CancelKeyPress`, key handling, and session-level tests.

**Decision:** Build no key-source abstraction. The session stays untouched; the seam covers parse + validation + once-mode.

**Rationale:** The abstraction would be an invasive change to a heavily-reviewed TUI for coverage the session-level tests already approximate — YAGNI, and a behaviour-neutrality risk for zero seam benefit.

**Trade-offs accepted:** The interactive path remains untestable through the writer seam (documented seam limit, same class as retry's child passthrough).

**Options considered:** injectable key source + screen writer — rejected as above; covering interactive via the seam by stubbing the console — rejected (alternate-buffer entry wrecks the test console; mock-of-console hides real failure modes).

## P2. Main always registers the CTS + CancelKeyPress; the session keeps `CancellationToken.None`

**Context:** Pre-seam, Ctrl+C ownership was asymmetric: once-mode registered in Main conditionally; interactive relied on the session's internal handler; the session received `None`. Post-seam, Main cannot know once-vs-interactive before parsing (which moves into the library).

**Decision:** Main registers one CTS + named handler unconditionally (retry shape, reusing `SessionHelpers.RequestCancellationSilently`) and passes the token to `Cli.RunAsync`. Once-mode consumes it (its own registration block is deleted). The interactive call site keeps `session.RunAsync(CancellationToken.None)` with an explanatory comment.

**Rationale:** Convention (process-globals in Main); the once-mode semantics are identical (same handler body, same token plumbing into `CommandExecutor`). Threading the real token into the session would change interactive cancellation semantics — out of bounds for a behaviour-neutral refactor.

**Trade-offs accepted:** A structural delta on the interactive path: Main's handler is now registered alongside the session's (both set `e.Cancel = true`; Main's token unobserved there). No user-visible change; recorded rather than hidden.

**Options considered:** conditional registration inside the library — rejected (the exact static-event anti-pattern the convention exists to prevent); real token into the session — deferred (see table).

## P3. Colour wiring is explicitly out of seam-test scope for peep

**Context:** Phase 1's test recipe includes `--color=always/never` wiring tests through the seam.

**Decision:** No seam colour tests for peep.

**Rationale:** `UseColor` feeds only the interactive renderer; once-mode and validation output are uncoloured. A seam colour test would assert nothing real (the green-formatter-test-but-caller-unwired anti-pattern in reverse — a vacuous pass). Renderer colour stays covered by the existing `ColorTests`.

## P4. GitIgnoreChecker flake hardening rides along, test-only

**Context:** CI flake observed 2026-06-06 (`ClearCache_SubsequentQueryReEvaluates`: transient git-spawn fallback makes consecutive `IsIgnored()` calls disagree on a loaded runner); memory earmarked it for this session.

**Decision:** One test-only hardening task in this effort. No production change to `GitIgnoreChecker`; if investigation shows a production seam is needed, that becomes a separate surfaced decision.

**Rationale:** We're in peep anyway (session-cost argument); a test-only change cannot violate behaviour-neutrality; folding a production redesign in would.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| Threading the real CancellationToken into `InteractiveSession.RunAsync` | Behaviour change to interactive cancellation semantics — needs its own decision + tests, not a refactor rider |
| Consolidating the three `jsonOnlyViaJsonOutput` envelope sites | Quality follow-up; this refactor moves them verbatim |
| `GitIgnoreChecker` production seam (deterministic git-spawn injection) | Only if the test-only hardening proves insufficient |
| wargs `RunAsync` seam, nc `Stream` seam | Next sessions; convention settled in phase 1 |
