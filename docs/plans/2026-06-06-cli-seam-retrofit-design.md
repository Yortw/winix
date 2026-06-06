# Cli.Run Seam Retrofit — Design (Phase 1: convention + schedule + retry)

**Date:** 2026-06-06
**Status:** Approved (brainstorm 2026-06-06)
**ADR:** [2026-06-06-cli-seam-retrofit-adr.md](2026-06-06-cli-seam-retrofit-adr.md)

## Problem

22 of 27 Winix tools expose a `Cli.Run`/`RunAsync` seam: the full parse→orchestrate→format→route path lives in the class library, `Program.cs` is a ~15-line shell, and tests drive the whole tool with `StringWriter`s. Five tools predate the convention and keep orchestration in `Program.cs` with direct `Console.*` references: **schedule, nc, retry, wargs, peep**.

Without the seam there is no deterministic end-to-end test of the production wiring. This is the gap class behind two real shipped defects:

- **wargs shipped colour unwired in v0.3.0** — formatter tests were green; nothing proved `Program.cs` invoked the formatter with colour resolved.
- **hcat shipped QR and access-log silently absent** through a fully green suite (different tool, same shape: green unit test on a helper ≠ production caller wired).

During the 2026-06-01 colour sweep these five tools had to settle for code-inspection of their `Program.cs` wiring — exactly the verification mode that has failed before.

## Scope

This phase: **settle the suite-wide seam convention for all five tools, implement schedule and retry.** peep, wargs, and nc follow in later sessions, inheriting the convention without re-design.

## Suite-wide seam convention

Three signature shapes, extending (not replacing) the existing 22-tool convention:

| Shape | Signature | Used by |
|---|---|---|
| Sync | `Cli.Run(string[] args, TextWriter stdout, TextWriter stderr, …optional facts)` | schedule (+ existing 22) |
| Cancellable | `Cli.Run(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken, …optional facts)` | retry now; peep, wargs (`RunAsync`) later |
| Byte-stream | `Cli.Run(string[] args, Stream stdin, Stream stdout, TextWriter stderr, CancellationToken)` | nc later — its data path is binary; `TextWriter` is the wrong shape |

"Optional facts" follows the man precedent: environment facts (backend, terminal width, env overrides) arrive as optional parameters with production defaults, not as `Console`/`Environment` queries hidden in the library.

### Process-global responsibilities stay in `Program.Main`

This codifies what the wargs/peep comments already state:

- `Console.CancelKeyPress` registration and `CancellationTokenSource` ownership. `CancelKeyPress` is a process-global static event that fights xunit parallelism. The named-handler/unregister-in-finally disposal-race pattern (documented in retry's comments, reference `src/wargs/Program.cs`) stays exactly as is.
- `ConsoleEnv.EnableAnsiIfNeeded()` / `ConsoleEnv.UseUtf8Streams()`.
- Passing `Console.Out` / `Console.Error` / `cts.Token` into `Cli.Run`.

### Inside the seam (library)

Parse, validate, resolve colour (`result.ResolveColor()` — demux precedent; tests force determinism with `--color=always/never`), orchestrate, format, route streams, top-level catch-all (`UnwrapTypeInit` — envvault precedent places it in `Cli`).

### Documented seam limits

- **Child-stdio inheritance (retry):** the child inherits real console handles by contract (`Redirect* = false`); `Cli.Run` tests cannot observe child passthrough. That coverage stays integration/smoke-tier. Do not "fix" this into a behaviour change.
- **Interactive key-polling (peep, later):** needs an injectable key-source abstraction or stays in Main — decided in peep's own session. Open point, deliberately not pre-designed here.

### Behaviour-neutrality policy

Zero intended behaviour change. Existing tests must pass **unmodified** — a forced edit to an existing test is a contract change: stop and surface it. Defects discovered en route are surfaced and decided separately, never silently folded in.

## schedule retrofit

New file `src/Winix.Schedule/Cli.cs`:

```csharp
public static int Run(string[] args, TextWriter stdout, TextWriter stderr,
    ISchedulerBackend? backend = null)
```

`backend: null` → current platform switch (`SchtasksBackend` on Windows, `CrontabBackend` elsewhere); `GetBackend()` moves into `Cli` as the default-resolution path. Tests inject a fake.

**Moves** (essentially all of `Program.cs` except the shell):

- Parser construction (the full `.Description()…ExitCodes()` chain).
- The subcommand dispatcher `switch`.
- All eight `Run*` methods + `WriteActionResult` + `GetVersion` (library reads its own assembly version — man precedent; identical value via shared `Directory.Build.props`).
- Every `Console.Out`/`Console.Error` reference (~22 sites) becomes the threaded `stdout`/`stderr` parameter, including the suite-convention routing (JSON envelopes → stdout, human tables/diagnostics → stderr).

**Stays in `Program.Main`** (~12 lines): `EnableAnsiIfNeeded()`, `UseUtf8Streams()`, `return Cli.Run(args, Console.Out, Console.Error);`

**One structural change beyond verbatim relocation** (flagged deliberately): today `GetBackend()` is called inside each `Run*` method (8 call sites). The seam resolves the backend **once** at the top of `Cli.Run` and passes it down. Same behaviour — construction is trivial and stateless — one fewer hidden dependency per method.

## retry retrofit

New file `src/Winix.Retry/Cli.cs`:

```csharp
public static int Run(string[] args, TextWriter stdout, TextWriter stderr,
    CancellationToken cancellationToken)
```

**Moves** (verbatim, comments preserved — every one is a past review finding):

- Parser chain + all option parsing/validation (`--times`/`--delay`/`--backoff`/`--on`/`--until` + `ParseCodeList`).
- `summaryWriter = useStdout ? stdout : stderr` (was `Console.Out`/`Console.Error`).
- `RunWithRetry` — signature changes `CancellationTokenSource cts` → `CancellationToken cancellationToken`. Verified mechanical: the method only ever forwards `cts.Token` (no `Cancel()` calls inside).
- The ~170-line `runProcess` delegate (spawn, kill-registration disposal ordering, cancellation-aware wait, 5 s grace window, orphan warning, exit 137). Its three direct `Console.Error` warning writes become the threaded `stderr`.
- Top-level catch-all + `UnwrapTypeInit` + `SafeWriteLine` (envvault precedent).
- `GetVersion` — already anchors on `typeof(RetryResult).Assembly` (the library assembly), so the value is provably identical after the move.

**Stays in `Program.Main`** (~25 lines): `ConsoleEnv` setup; the CTS + named `CancelKeyPress` handler with unregister-in-finally (disposal-race comment intact); `return Cli.Run(args, Console.Out, Console.Error, cts.Token);`

**No `RunProcess` injection seam — deliberate.** A fake spawner is exactly the "seam injects the value under test" structural blindness recorded in the glob-expansion retro (`feedback_post_merge_ci_and_smokes_mandatory.md` §7). Seam tests use real process behaviour:

- **Failure paths are deterministic without a child:** a nonexistent command exercises the full launch-failure path (exit 127, JSON `child_exit_code: null`, plain-text wording). No spawn succeeds, no timing dependency.
- **Happy/exhaustion paths** use a trivial real child. Existing retry integration tests already spawn processes; reuse their helper approach (exact mechanism verified at implementation).

## Testing strategy

Wiring-focused + per-subcommand; do not re-prove what the existing library suites (schedule 339, retry 108) already cover. Existing tests untouched.

**schedule** (`CliRunTests.cs` + `FakeSchedulerBackend : ISchedulerBackend` in `Winix.Schedule.Tests`):

- Usage errors → stderr, exit 125; missing/unknown-subcommand wording.
- Per-subcommand: one happy path (fake returns success; assert envelope/table content and stream) and one failure path (fake reports failure; assert exit 1 + error routing).
- `--json` envelope → stdout (the exact defect class the 2026-05-09 smoke caught in this tool).
- Colour wiring via `--color=always` / `--color=never` through the seam.
- `next` (backend-less, pure cron math) — happy + invalid expression.

**retry** (`CliRunTests.cs` in `Winix.Retry.Tests`):

- Per-flag invalid values → stderr + 125.
- Launch failure (127/126) in both plain and `--json` modes via nonexistent command.
- `--stdout` summary routing (summary stream flips; errors always stderr).
- `--on`/`--until` behaviour through the seam.
- Colour wiring `--color=always/never`.
- Pre-cancelled token behaviour — assert against `RetryRunner`'s actual cancelled-outcome contract (**verify at implementation**, don't assume).

## Verification & rollout

Branch: `feature/cli-seam-retrofit` off `release/v0.4.0`, created before any code.

Per-tool gate sequence (schedule first, then retry):

1. Move code → build clean (0 warnings).
2. Existing tests pass **unmodified**.
3. New seam tests added and green.
4. Manual CLI regression smoke on the rebuilt binary (refactored-tools rule): `--help`/`--version`/`--describe`, happy path, usage error, `--json` stream routing, colour — Windows + WSL.
5. Re-run existing tier-review smoke fixtures (`artifacts/**/{schedule,retry}/run-smokes.sh`) against refreshed binaries — republish fixture binaries first (stale-binary trap).
6. `--help`/`--describe` byte-stability check: diff rebuilt output against the pre-refactor binary (catches accidental parser-chain drift during the move).

Whole-feature gates:

- Full `Winix.sln` test run, 0 failures.
- 3-OS CI on the feature branch via `gh workflow run ci.yml --ref feature/cli-seam-retrofit` (feature branches don't auto-trigger — the less/demux lesson).
- Merge `--no-ff` into `release/v0.4.0` after review rounds.

Docs impact: none user-facing (behaviour-neutral). Project `CLAUDE.md` layout lines for the two libraries mention the Cli seam, matching sibling entries. Memory backlog (`project_cli_seam_retrofit_backlog.md`) updated 5 → 3 remaining.

## Out of scope (deferred to later sessions)

- **peep** `RunAsync` seam + key-polling abstraction decision.
- **wargs** full `RunAsync` seam (its `HumanSummary.Emit` partial from the colour sweep stays as is until then).
- **nc** byte-stream seam (`Stream`-shaped signature designed on its own terms).
- Lifting `UnwrapTypeInit` into ShellKit (noted in retry's comment as "worth lifting at some point"; two copies exist — envvault, retry. schedule has no top-level catch-all today and is NOT given one — that would be a behaviour change. Lift when the next tool needs it).
