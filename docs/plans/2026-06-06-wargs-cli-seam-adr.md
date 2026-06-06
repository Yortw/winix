# ADR: wargs Cli.RunAsync Seam Retrofit (seam phase 3)

**Date:** 2026-06-06
**Status:** Accepted
**Context:** Applies the phase-1 seam convention (D1–D6) and phase-2 refinements (async shape, no-blocking-on-async rule) to wargs. Records only wargs-specific decisions.
**Design doc:** [2026-06-06-wargs-cli-seam-design.md](2026-06-06-wargs-cli-seam-design.md)

---

## W1. The pre-scan + OCE catch + catch-all move into `Cli.RunAsync`

**Context:** wargs's envelope-on-every-exit-path contract (rounds 4–8) is enforced by an OCE catch and a catch-all in Main, fed by a pre-parse flag scan. Phase-1 precedent (retry) moved the catch-all into Cli; wargs adds the OCE/cancelled-envelope layer.

**Decision:** All three move into `Cli.RunAsync`, wrapping the orchestration. Main retains no catch (only OOM/SOE can escape).

**Rationale:** This makes wargs's most-litigated contract — the cancelled envelope — seam-testable via a pre-cancelled token, and the unexpected-error envelope testable via fault injection. Keeping them in Main would forfeit most of the seam's value for this tool.

**Trade-offs accepted:** None structural — the end state matches retry's Main (try/finally for unregister only). The pre-scan's documented false-positive tolerance (literal `--json` after `--`) moves with it unchanged.

**Options considered:** keep in Main (strict verbatim) — rejected: untestable contract, ~120-line Main.

## W2. The `Console.In.Close()`-on-cancel registration stays in Main

**Context:** Round 7's Linux stdin-unblock hack closes the REAL console stdin when the token fires, forcing a blocked `ReadLine` to EOF. Round 8 documents the Windows SyncTextReader lock caveat.

**Decision:** The `cts.Token.Register(() => Console.In.Close())` block stays in Main, verbatim with its full comment.

**Rationale:** It mutates process-global console state tied to the REAL `Console.In`. The seam's `stdin` parameter is a `StringReader` in tests — a library-resident close-on-cancel would close the wrong object (harmless in tests, wrong in principle) and couple the library to console specifics the seam exists to remove. Main owns the real-console lifecycle; the library observes only the token and its injected reader.

**Trade-offs accepted:** The stdin-unblock path remains seam-untestable (by construction). Coverage continues via the Linux `SkippableFact` binary test and smokes; the Windows caveat remains the known gap tracked in `project_wargs_progress.md` — unchanged by this work.

## W3. `stdin` as a required `TextReader` parameter

**Context:** wargs consumes stdin as data (items), like demux and unlike schedule/retry/peep.

**Decision:** `RunAsync(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken)` — the demux shape extended with the token.

**Rationale:** demux precedent; `InputReader` already takes a `TextReader`, so the parameter threads straight through. Making it optional-with-Console.In-default would put a console dependency back in the library.

## W4. Newly-unlocked tests land AFTER neutrality is validated (user requirement)

**Context:** The seam makes three previously-untestable paths deterministic (cancelled envelope, input_read_failed via throwing reader, the round-7 cancel-vs-read-failure re-check).

**Decision:** Two-stage test delivery: the move task validates behaviour-neutrality with the wiring/regression group only; the newly-unlocked group is a separate subsequent task.

**Rationale:** Keeps "the move changed nothing" and "we added coverage that didn't exist" as separately-auditable claims — a failed newly-unlocked test then unambiguously indicts the pre-existing behaviour or the new test, never the move.

## W5. Fixture cancellation case follows the established pattern

**Decision:** Linux-gated `timeout -s INT` case asserting the `cancelled` envelope (exit file 124 = GNU timeout's own code); Windows explicit SKIP; wargs's envelope shape probed before pinning.

**Rationale:** Pattern established and debugged on peep (P05) and retry (R08) on 2026-06-06; the GNU-timeout-124 and process-group-signal facts are recorded in the smoke memory.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| Windows GenerateConsoleCtrlEvent integration test for the stdin-close caveat | Pre-existing known gap (round-8); orthogonal to the seam; tracked in project_wargs_progress.md |
| Consolidating wargs's mode-discrimination styles | Quality follow-up; moved verbatim |
| nc Stream-shaped seam | Final phase; designed on its own terms |
