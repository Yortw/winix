# peep Cli.RunAsync Seam Retrofit — Design (seam phase 2)

**Date:** 2026-06-06
**Status:** Approved (brainstorm 2026-06-06)
**ADR:** [2026-06-06-peep-cli-seam-adr.md](2026-06-06-peep-cli-seam-adr.md)
**Convention:** applies [2026-06-06-cli-seam-retrofit-design.md](2026-06-06-cli-seam-retrofit-design.md) (settled in phase 1 — schedule + retry, merged `c4c0f87`). This doc covers only peep-specific decisions.

## Code reality (explored 2026-06-06, reshapes the deferred question)

`InteractiveSession` — the watch loop, TUI rendering, key handling — **already lives in the library** (`src/Winix.Peep/InteractiveSession.cs`) and registers its own `Console.CancelKeyPress` handler internally (with documented race rationale). The interactive path is inherently console-bound: alternate screen buffer, `ReadKey(intercept: true)`, the `KeyAvailable`/`IsInputRedirected` SR-key probe, cursor rendering via `Console.Out`.

What actually lacks a seam is `src/peep/Program.cs` (~338 lines):

- Parser chain + validation, including the `--json-output`-implies-JSON envelope bridging (R5 SFH I1) duplicated at **three** early-return sites, all writing to `Console.Error`.
- `RunOnceAsync` — the once-mode orchestration (~140 lines, six typed catch arms, each a past review finding). Unlike retry, once-mode **captures** child output and writes it (`Console.Write(peepResult.Output)`) — fully writer-seamable, output included. This is the automation/CI path.
- Once-mode's conditional CTS + `CancelKeyPress` (handler body already extracted: `SessionHelpers.RequestCancellationSilently`).

**Consequence:** the ADR-deferred "key-polling abstraction (injectable key source vs stays-in-Main)" question is **closed as not-needed** — the loop was never in `Program.cs`; it stays in the session, untouched, covered by the existing session-level tests.

## Seam shape

New file `src/Winix.Peep/Cli.cs`:

```csharp
public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr,
    CancellationToken cancellationToken)
```

First async seam in the suite — the convention's cancellable shape as `RunAsync` (anticipated in phase 1's D1).

### What moves (verbatim + writer transformations, comments preserved)

- The full parser chain and validation, including the three `jsonOnlyViaJsonOutput` envelope blocks — kept as **three verbatim sites**. Consolidating them is a quality change, not a move; noted as a possible follow-up, not done here.
- `RunOnceAsync`: all six typed catch arms, the R6 SFH N1 `peepResult` hoist, `Console.Write(peepResult.Output)` → `stdout.Write(…)`, every `Console.Error.WriteLine` → `stderr`. Its CTS/`CancelKeyPress` block is **deleted** — replaced by the Main-supplied token. The kill-on-cancel plumbing into `CommandExecutor.RunAsync(…, token)` is unchanged.
- `GetVersion` (already anchors on `typeof(PeepResult).Assembly` — the library — value provably identical).
- SessionConfig build + `new InteractiveSession(config)` + `await session.RunAsync(CancellationToken.None)`.

### `CancellationToken.None` to the session — deliberate

The session owns its own Ctrl+C internally. Threading the real token into `session.RunAsync` would change interactive cancellation semantics (token-cancel vs internal-quit paths could alter exit reason) — a behaviour change deferred to its own decision. A comment at the call site records this.

### What `Program.Main` keeps (~30 lines)

`ConsoleEnv` setup; **one CTS + named handler, always registered** (retry shape), via `SessionHelpers.RequestCancellationSilently`; `try { return await Cli.RunAsync(args, Console.Out, Console.Error, cts.Token); } finally { unregister }`.

Main can no longer know once-vs-interactive before calling `Cli.RunAsync` (parsing moves inside), so registration is unconditional. **Observable delta, pinned honestly:** Main's handler is now registered during interactive mode too (previously only the session's own). Both handlers set `e.Cancel = true`; Main's token is unobserved on the interactive path — no user-visible change, but a structural delta this design records rather than hides.

### Documented seam limits (the retry-passthrough analogue)

The interactive TUI path stays console-bound: alternate buffer, `ReadKey`, the `KeyAvailable` probe, the session's internal `CancelKeyPress`, and the session's own `Console.Out`/`Console.Error` usage are out of writer-seam scope, covered by the existing session-level tests. Seam tests must **never invoke the interactive path** (it enters the alternate screen buffer) — every seam test uses `--once` or an error path.

**Colour is out of seam scope** — `UseColor` only feeds the interactive renderer; once-mode and validation output are uncoloured. Recorded instead of adding a vacuous test.

## Testing strategy

`CliRunAsyncTests.cs` in `Winix.Peep.Tests`, real children (no-fake discipline, phase-1 D4):

- **Validation → 125 + stderr:** no command; `--interval 0`/non-numeric; bad `--exit-on-match` regex; negative `--debounce`/`--history`.
- **`--json-output` bridging trio:** no-command / parse-error / bad-regex each → JSON envelope on stderr, exit 125; plus one non-bridged `--json` counterpart.
- **Once-mode end-to-end:**
  - Happy: child stdout verbatim on the `stdout` writer; exit passthrough.
  - `--json` → envelope on **stderr** (peep's deliberate convention — stdout carries output): `exit_reason:"once"`, `runs:1`, `history_retained:0`; `--json-output` → `last_output` populated.
  - Nonexistent command → 127, plain and envelope arms.
  - Empty command token → pin whatever the planning-time probe shows (do **not** assume the retry analogue's 126).
  - Cancellation: pre-cancelled token → `OperationCanceledException` arm → exit 130 + `cancelled` envelope; mid-wait cancel with a long child (`CancelAfter` + elapsed guard) → 130, child killed promptly.

Existing tests (219: 216 + 3 skipped) pass **unmodified** (phase-1 D6 policy; `ProgramMainTests` comment-only location fixups authorised if references move).

## Rider: GitIgnoreChecker flake hardening (test-only)

CI flake observed 2026-06-06 (`ClearCache_SubsequentQueryReEvaluates`, windows-latest, 3 s vs ~215 ms, same commit green on re-run + 3× local): the test asserts two consecutive `IsIgnored()` calls agree, but `GitIgnoreChecker` spawns `git check-ignore` with process-global `_gitDisabled` fallback — transient git-spawn slowness makes one call take the fallback and disagree.

Scope: one **test-only** hardening task (make the test robust against the transient fallback, or pin the fallback contract explicitly). **No production change.** If investigation shows `GitIgnoreChecker` needs a production seam, that is surfaced as a separate decision, not folded in.

## Verification & rollout

Phase-1 kit reused verbatim: branch `feature/cli-seam-peep` (created); pre-refactor `--help`/`--describe` captured per-stream and diffed per-stream (the F1 lesson); existing tests unmodified; full `Winix.sln`; WSL suite runs; smoke fixture re-run on refreshed AOT binaries on **both** Windows and Linux (fixture located at plan time); 3-OS CI on the feature branch via workflow_dispatch; whole-feature review; merge `--no-ff` into `release/v0.4.0`.

## Out of scope

- Threading the real token into `InteractiveSession.RunAsync` (behaviour change — own decision later).
- Consolidating the three `jsonOnlyViaJsonOutput` envelope sites (quality follow-up).
- Any `GitIgnoreChecker` production change.
- wargs and nc seams (next sessions; convention settled).
