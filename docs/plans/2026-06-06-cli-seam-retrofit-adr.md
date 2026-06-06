# ADR: Cli.Run Seam Retrofit (convention + schedule + retry)

**Date:** 2026-06-06
**Status:** Accepted
**Context:** Five tools (schedule, nc, retry, wargs, peep) lack the `Cli.Run` library seam that the other 22 tools have, leaving their production wiring untestable end-to-end — the gap class behind the wargs-colour-unwired (v0.3.0) and hcat-QR-unwired defects.
**Design doc:** [2026-06-06-cli-seam-retrofit-design.md](2026-06-06-cli-seam-retrofit-design.md)

---

## D1. Three seam signature shapes, settled now for all five tools

**Context:** Implementing seams tool-by-tool without a convention risks signature churn (peep/wargs/nc re-opening the design later).

**Decision:** Sync `Run(args, stdout, stderr, …facts)`; cancellable `Run(args, stdout, stderr, CancellationToken, …facts)`; byte-stream `Run(args, Stream stdin, Stream stdout, TextWriter stderr, CancellationToken)` for nc only.

**Rationale:** The sync shape is the existing 22-tool convention. `CancellationToken` is the smallest extension that lets Main keep owning Ctrl+C. nc's data path is binary — a `TextWriter` cannot carry it, and pretending it can would force encoding decisions the tool deliberately avoids.

**Trade-offs accepted:** Three shapes instead of one universal signature; the byte-stream shape is designed in name only here (details deferred to nc's session).

**Options considered:**
- *Single universal signature with everything optional* — rejected: forces `Stream` params on 26 tools that never use them.
- *Defer all convention decisions to each tool's session* — rejected: churn risk; the convention questions are answerable now from existing precedent (man, demux, envvault).

## D2. Process-global responsibilities stay in `Program.Main`

**Context:** `Console.CancelKeyPress` is a process-global static event; `CancellationTokenSource` disposal races with second Ctrl+C during AOT teardown (documented in retry's comments).

**Decision:** Main owns `CancelKeyPress` registration + CTS lifetime + `ConsoleEnv` setup; the seam receives a plain `CancellationToken`.

**Rationale:** Static events don't compose with xunit parallelism (peep's comment states this verbatim). The named-handler/unregister-in-finally pattern encodes a real past bug — moving it risks re-introducing it. A token is all the library ever needed (verified: retry's `RunWithRetry` only forwards `cts.Token`).

**Trade-offs accepted:** Ctrl+C handling itself remains untested by seam tests (it stays in the ~25-line Main, covered by code inspection + smokes).

**Options considered:** moving CTS into `Cli.Run` — rejected: makes the seam own process-global state, defeating its testability purpose.

## D3. schedule backend injection via optional parameter

**Context:** schedule's eight subcommand handlers call `GetBackend()` (platform switch → `SchtasksBackend`/`CrontabBackend`) internally; seam tests must not hit the real OS scheduler.

**Decision:** `Run(args, stdout, stderr, ISchedulerBackend? backend = null)`; null → current platform switch; backend resolved once at the top of `Cli.Run` and passed down.

**Rationale:** `ISchedulerBackend` is already public with both production implementations behind it — the abstraction exists; only the injection point is missing. Optional-parameter injection is the man precedent ("facts as parameters"). Resolving once replaces 8 hidden per-method dependencies with 1 explicit one.

**Trade-offs accepted:** One structural deviation from verbatim relocation (single resolution vs 8 call sites) — flagged in the design; behaviour identical because construction is trivial and stateless.

**Options considered:** internal static `Func<ISchedulerBackend>` override seam — rejected: public optional param is simpler, matches precedent, and is usable by library consumers, not just tests.

## D4. No `RunProcess` injection seam for retry

**Context:** retry's ~170-line process-spawn delegate is the orchestration core; a test seam there is tempting.

**Decision:** Do not add a spawner injection seam. Seam tests use real process behaviour: nonexistent commands for deterministic failure paths, a trivial real child for happy paths.

**Rationale:** A fake spawner injects the very value under test — the structural-blindness class from the glob-expansion retro (`Environment.CommandLine` falsified through 302 green tests; `feedback_post_merge_ci_and_smokes_mandatory.md` §7). Launch-failure paths are already fully deterministic without any fake (no spawn succeeds).

**Trade-offs accepted:** Happy-path seam tests spawn real processes (slower, platform-conditional helper command). *(Amended after adversarial review pass 1: mid-wait cancellation is now seam-testable — the token parameter makes it drivable without real Ctrl+C — and the plan includes a `MidWaitCancel` test with a long sleep child. The residual smoke-only surface is real-signal delivery: Ctrl+C → `CancelKeyPress` → CTS, which lives in Main.)*

**Options considered:** internal `RunProcessDelegate` override — rejected for the blindness reason; it also already exists at the `RetryRunner` layer where the existing 108 tests use it, so adding it at the Cli layer duplicates coverage while subtracting realism.

## D5. Colour resolution stays inside the seam

**Context:** `ResolveColor()` queries `ConsoleEnv.IsTerminal` — a process-global read inside the library.

**Decision:** Follow demux precedent: `Cli.Run` calls `result.ResolveColor()`; tests force determinism with `--color=always/never`.

**Rationale:** Already the established pattern in the 22 seamed tools; `ResolveColorCore` provides the deterministic unit-test point separately. Threading an `isTerminal` fact through every tool (man does this for width) is justified only where the tool's output layout depends on it.

**Trade-offs accepted:** auto-mode colour decision is not covered by seam tests (covered by `ResolveColorCore` unit tests + smokes).

## D6. Strict behaviour-neutrality, enforced via unmodified existing tests

**Context:** Refactoring five shipped tools' orchestration right before a release.

**Decision:** Zero intended behaviour change. Existing tests must pass byte-for-byte unmodified; a forced test edit = contract change = stop and surface. Plus: `--help`/`--describe` output diffed against pre-refactor binaries; manual CLI smoke per refactored tool (Windows + WSL); existing smoke fixtures re-run on republished binaries.

**Rationale:** `feedback_test_modification_signals_contract_change.md` + `feedback_cli_auto_defaults.md` (manual retest after refactor). The byte-diff of `--help`/`--describe` is a cheap whole-surface drift detector for a move this mechanical.

**Trade-offs accepted:** Defects spotted en route are deferred to separate decisions even when the fix is one line — slower, but keeps the change-set audit-clean.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| peep key-polling abstraction (injectable key source vs stays-in-Main) | Needs peep-specific design; interactive loop semantics not in this phase's scope |
| wargs `RunAsync` seam details (stdin materialisation ordering, Ctrl+C-before-RunAsync) | Largest Main with the most documented traps; own session with own review |
| nc `Stream`-shaped seam details (console-stream lifetime, dispose ownership) | Different signature family; designed on its own terms |
| Lifting `UnwrapTypeInit` to ShellKit | Two copies (envvault, retry) tolerable; lift when a third tool needs it |
| Seam retrofit beyond these five (none known) | 22/27 verified done; re-grep at phase end |
