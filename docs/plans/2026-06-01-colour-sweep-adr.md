# ADR: suite-wide colour sweep

**Date:** 2026-06-01
**Status:** Accepted
**Context:** The 2026-06-01 colour audit found 3 tools with unwired colour (trash/hcat/wargs — claim colour, emit none; wargs shipped in v0.3.0), 9 tools with `--color WHEN` doc drift, and no regression guard on the 15 tools that do emit colour. ShellKit `--color=when` is now built; this sweep makes colour emit and locks it.
**Related design:** `docs/plans/2026-06-01-colour-sweep-design.md`

---

## D1 — Full scope (functional fixes + regression tests + doc fixes)
- **Decision:** Do all three pieces — emit-fixes (3 tools), regression tests (~18 tools), doc fixes (9 tools).
- **Rationale:** Pieces 1+2 close current defects; Piece 3 (regression tests on the already-working tools) prevents the *class* recurring — the gap that let wargs ship broken through 17 review rounds.
- **Trade-offs accepted:** Largest option; ~18 tools get a test. User chose it explicitly for class-closure.
- **Options considered:** Bug-fix only (rejected — leaves doc drift + no regression guard); fix + docs but no regression-proofing (rejected — the working tools stay unguarded).

## D2 — Test pattern: end-to-end primary, renderer-level fallback, `(char)27` literal
- **Decision:** Primary test forces `--color`/`--no-color` through the tool's real `Cli`/output seam and asserts ESC presence/absence on the captured writer. Fallback to renderer/formatter-level for interactive tools (less/man/peep). ESC expressed as `((char)27).ToString()`.
- **Rationale:** Only the end-to-end form catches an unwired formatter (the hcat-QR class). `--color` forces colour over auto-detect so a non-TTY `StringWriter` capture works. `(char)27` avoids the escape-literal round-trip ambiguity hit earlier this session.
- **Trade-offs accepted:** Interactive tools get a slightly weaker (renderer-level) guard than full-Cli; acceptable since their colour lives in the renderer.
- **Options considered:** Formatter-level uniformly (rejected — wouldn't catch a Cli-doesn't-pass-useColor bug); a single shared helper (rejected — seam signatures vary: async, injectable deps).

## D3 — Colour convention: suite idiom, match each README's existing claim
- **Decision:** Use `AnsiColor.Dim/Green/Red/Yellow(useColor)` locals + reset (returns `""` when off), per demux/Retry. Colour only what each tool's README already claims (trash list+summary; hcat banner+log; wargs verbose output). Plain output byte-identical to today.
- **Rationale:** Consistency across the suite; making existing claims true rather than inventing new colour.
- **Trade-offs accepted:** None material.
- **Options considered:** Per-tool bespoke schemes (rejected — inconsistent); expanding colour beyond README claims (rejected — scope creep).

## D4 — Decomposition into three separately-reviewed sub-plans
- **Decision:** Sub-plan A (emit-fixes: trash/hcat/wargs + tests + their docs), B (regression tests for the 14 remaining emitters), C (7 data-tool doc fixes). Each = own implementation plan + adversarial review + build.
- **Rationale:** The sweep spans code/test/doc across ~24 tools — too large for one plan. Decomposition keeps each unit buildable and reviewable. A establishes the test pattern; B scales it.
- **Trade-offs accepted:** Three plan/review cycles instead of one.
- **Options considered:** One mega-plan (rejected — unwieldy, per the brainstorming scope-check); per-tool plans (rejected — too granular, 24 cycles).

## D5 — Sequence A → C → B
- **Decision:** Fix the functional bug first (A), then the cheap mechanical doc cleanup (C), then the large regression-test rollout (B).
- **Rationale:** A is the user-visible defect and proves the test pattern; C is quick and closes the doc class; B is the biggest and benefits from the proven pattern.
- **Trade-offs accepted:** B (the bulk) comes last; acceptable — it guards already-working tools.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| Shared cross-tool colour-test helper | Seam signatures vary too much (async, injectable deps); per-tool ~10-line tests are clearer. |
| Expanding what tools colour beyond README claims | Out of scope — sweep makes claims true, not broader. |
| Colour for the data tools (digest/url/clip/qr/mksecret/protect/unprotect) | They correctly emit none; only docs (C) are fixed. |
| Re-shipping shipped v0.3.0 tools for doc fixes | Repo doc edits only; shipped copies self-heal on next release; wargs's functional fix rides v0.4.0. |
| Concurrent-write integration test for hcat's coloured request-log (Sub-plan A, adversarial A6-DEFER) | Thread-safety rests on `_useColor` immutability + the unchanged `WriteLineLocked` lock scope (the coloured string is built as a local, then handed to the existing locked write); a Kestrel-concurrency repro is integration-tier and high-cost. |
| hcat banner AOT serve-mode smoke is best-effort (Sub-plan A, adversarial A7-DEFER) | The `Banner.Render` + `CaptureLifecycle` unit tests are the primary guard; serve is long-running, so the AOT banner smoke is confirmatory only. |
