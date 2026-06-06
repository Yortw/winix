# wargs Cli.RunAsync Seam Retrofit — Design (seam phase 3)

**Date:** 2026-06-06
**Status:** Approved (brainstorm 2026-06-06)
**ADR:** [2026-06-06-wargs-cli-seam-adr.md](2026-06-06-wargs-cli-seam-adr.md)
**Convention:** applies [2026-06-06-cli-seam-retrofit-design.md](2026-06-06-cli-seam-retrofit-design.md) (phase 1) as refined by [2026-06-06-peep-cli-seam-design.md](2026-06-06-peep-cli-seam-design.md) (phase 2). This doc covers only wargs-specific decisions.

## Code reality (explored 2026-06-06)

`src/wargs/Program.cs` (632 lines) is friendlier than its reputation — past review rounds already parameterised the interior:

- `JobRunner.RunAsync(invocations, TextWriter stdout, TextWriter stderr, token)` — the execution engine is already writer-seamed.
- `InputReader(TextReader, …)` — stdin already injectable at the library boundary.
- `HumanSummary.Emit(result, wargsResult, TextWriter)` — the 2026-06-01 colour-sweep partial already takes a writer.

`Console.In/Out/Error` appear only at the edges: InputReader construction, the two NDJSON streaming-callback writes, the `SafeWriteLine(Console.Error, …)` sites, `UsageError`, and the `jobRunner.RunAsync` call.

**The genuinely Main-bound piece:** `cts.Token.Register(() => Console.In.Close())` — the round-7 Linux stdin-unblock hack (closing the REAL console stdin forces a blocked `ReadLine` to EOF on cancel; round-8 documents the Windows SyncTextReader lock caveat). This mutates process-global console state and must never run against a test's `StringReader`.

## Seam shape

New file `src/Winix.Wargs/Cli.cs`:

```csharp
public static async Task<int> RunAsync(string[] args, TextReader stdin,
    TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
```

The demux shape + token — wargs is the suite's only other stdin-consuming seam; `stdin` is a required parameter feeding `InputReader`.

### What moves (verbatim + edge transformations; comments preserved — this file's comments are the densest review-finding record in the suite)

- The `--json`/`--ndjson` **pre-scan** (`ContainsFlag`) + the **OCE catch** (exit 130 + cancelled envelope) + the **catch-all** (`ExceptionUnwrap.UnwrapTypeInit`, mode-discriminated envelope) — wrapping the orchestration inside `Cli.RunAsync`. Decision rationale in ADR W1.
- The entire current inner `RunAsync`: parser chain; the 6 mutual-exclusion validations; the `UsageError` mode-discrimination helper; pipeline build with `new InputReader(stdin, …)`; the input-materialisation catch (input_read_failed + the round-7 cancellation re-check); empty-input / dry-run envelopes (all three modes); both NDJSON streaming callbacks (writes to the `stderr` parameter under the existing lock); `jobRunner.RunAsync(invocations, stdout, stderr, cancellationToken)`; exit-code derivation (incl. the round-12 SkipReason-filtered fail-fast classification); fault surfacing + `HumanSummary.Emit`.
- `SafeWriteLine`, `GetVersion` (already anchors `typeof(WargsExitCode).Assembly` — the library; value identical).

### What `Program.Main` keeps (~45 lines)

`ConsoleEnv` setup; CTS + `CancelKeyPress` handler **verbatim** (including the round-7 broadened catch rationale); the `cts.Token.Register(() => Console.In.Close())` block **verbatim with its full round-7/8 comment**; `try { return await Cli.RunAsync(args, Console.In, Console.Out, Console.Error, cts.Token); } finally { unregister }`. With the catch-all inside `Cli.RunAsync`, only OOM/SOE can escape to Main — no catch there.

### What this unlocks (previously untestable, now deterministic)

- **The cancelled envelope** — rounds 4–8's most-litigated contract — via a pre-cancelled token.
- **input_read_failed** — via a throwing `TextReader`; previously needed a broken OS pipe.
- **The round-7 cancel-vs-input_read_failed re-check** — throwing reader + already-cancelled token must classify as cancelled (130), not input_read_failed (126).

### Seam limits

The real-console stdin-unblock path (`Console.In.Close()` on cancel, incl. the documented Windows SyncTextReader caveat) is out of seam scope by construction — covered by the existing Linux `SkippableFact` binary tests (`CtrlCDuringStdin_UnderNdjson_EmitsCancelledEnvelope`) and the smoke fixture.

## Testing strategy

**Sequencing (user requirement):** the move lands and is validated behaviour-neutral FIRST (existing tests unmodified, byte-stability, smokes); the newly-unlocked tests are a separate task on top of the proven-neutral base.

`CliRunAsyncTests.cs` in `Winix.Wargs.Tests` (`StringReader` stdin + real children):

**Wiring/regression group:**
- The 6 mutual-exclusion validations → 125, in human and `--ndjson` (envelope-only) modes.
- `--ndjson` parser-error envelope vs `--json` ShellKit-envelope deference (the round-6 double-envelope fix).
- no_input / dry_run envelopes in all three modes.
- Happy path (`StringReader` items → children → child stdout on the `stdout` writer, exit 0); child-failed → 123; fail-fast → 124 with SkipReason-filtered classification.
- NDJSON streaming: every stderr line parses as JSON; per-job fields; `--keep-order` reorder-buffer ordering.
- `--batch` and `--dry-run` plan count.

**Newly-unlocked group (separate task, after neutrality is validated):**
- Cancelled envelope: pre-cancelled token → 130 + `cancelled` envelope under `--json` and `--ndjson`; no envelope in human mode.
- Mid-run cancel: long children + `CancelAfter` → kill-on-cancel through JobRunner → 130, liveness bound (30s children — the established pattern; no blocking-on-async rule applies).
- input_read_failed: `ThrowingTextReader` (IOException mid-read) → 126 + envelope per mode.
- Cancel-during-read classification: throwing reader + already-cancelled token → cancelled (130), NOT input_read_failed — pinning the round-7 fix.

Existing tests (174: 167 + 7 skipped) pass **unmodified**; `ProgramMainTests` comment-only location fixups authorised; the Linux `SkippableFact` binary Ctrl+C tests are untouched and keep covering the real-console path.

## Verification & rollout

Phase-1/2 kit verbatim: branch `feature/cli-seam-wargs` (created); per-stream `--help`/`--describe` baselines + independent diffs; full `Winix.sln`; WSL suite runs; 3-OS CI via workflow_dispatch; whole-feature fresh-eyes review; merge `--no-ff` into `release/v0.4.0`.

**Fixture addition** (established cancellation-smoke pattern, commits `02ce8d4`/`97604df`): a Linux-gated SIGINT case in `artifacts/round-stop-2026-05-09/wargs/run-smokes.sh` asserting the `cancelled` envelope (exit file 124 = GNU timeout's own code); Windows explicit SKIP. Probe wargs's envelope shape before pinning — peep and retry both say `cancelled` but differ in detail fields.

Docs: CLAUDE.md layout line; memory backlog 2 → 1 remaining (nc).

## Out of scope

- Any change to the stdin-close-on-cancel mechanism or its Windows caveat (tracked separately in `project_wargs_progress.md`).
- nc's Stream-shaped seam (final phase).
- Consolidating wargs's three mode-discrimination styles (UsageError vs inline) — quality follow-up at most; moved verbatim here.
