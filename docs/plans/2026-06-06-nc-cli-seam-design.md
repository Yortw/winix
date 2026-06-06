# nc Cli.RunAsync Seam Retrofit — Design (seam phase 4, final)

**Date:** 2026-06-06
**Status:** Approved (brainstorm 2026-06-06)
**ADR:** [2026-06-06-nc-cli-seam-adr.md](2026-06-06-nc-cli-seam-adr.md)
**Convention:** applies [2026-06-06-cli-seam-retrofit-design.md](2026-06-06-cli-seam-retrofit-design.md) (phase 1, D1's byte-stream shape) as refined by phases 2–3. This doc covers only nc-specific decisions. **Completes the seam class: 27/27 tools.**

## Code reality (explored 2026-06-06)

`src/nc/Program.cs` (468 lines) — the finale repeats the pattern: **`NetCatListener.RunAsync` and `NetCatClient.RunAsync` already take `(options, Stream stdin, Stream stdout, TextWriter stderr, ct)`** — the engines were stream-seamed during the tier-1 review. `DispatchCoreAsync` acquires `Console.OpenStandardInput()/OpenStandardOutput()` and threads them down.

In `Program.cs`: the parser chain; `BuildOptions` (a rich `UsageException` validation matrix — mode exclusions, TLS guards, bind/AF cross-checks, positional arity, port-spec parsing); the check-mode orchestration block (every line a round-fix: JSON suppression of text lines (round-3 CR-I6), DNS-failure summary (round-1 I-5), all-timeout summary (round-3 SFH-I5)); the OCE→130 "interrupted" and catch-all→126 envelope arms (options built before the dispatch try so the arms see `JsonOutput` — round-3 SFH-I3); `TryWriteJson` (diagnostic-never-masks-exit, round-2 I7); the `UsageException` class (currently app-internal); the CTS + `CancelKeyPress` (currently mid-stack in `DispatchAsync`, not Main).

**The nc-specific wrinkle:** check mode's stdout is TEXT (`Console.Out.WriteLine` port list) while connect/listen stdout is BYTES.

## Seam shape

New file `src/Winix.NetCat/Cli.cs`:

```csharp
public static async Task<int> RunAsync(string[] args, Stream stdin, Stream stdout,
    TextWriter stderr, CancellationToken cancellationToken)
```

The phase-1 D1 byte-stream shape, exactly as the convention recorded for nc.

### Check-mode text view (decided: single Stream + internal writer wrap)

Check mode wraps `stdout` in a `leaveOpen` UTF-8(no-BOM) `StreamWriter` for the port lines, flushed before return. Claim: byte-identical to today's `Console.Out` under `UseUtf8Streams`. That claim is **verified, not assumed**: the per-stream byte-stability gate plus a dedicated seam test asserting exact bytes (including newline) for an open-port line. Alternatives (two stdout parameters; TextWriter-primary) rejected — see ADR N1.

### What moves (verbatim + edge transformations; round-fix comments preserved)

- Parser chain; `BuildOptions` + the full validation matrix; the **`UsageException` class relocates to the library** (`src/Winix.NetCat/UsageException.cs`, internal→public or library-internal as visibility requires).
- The `UsageException` catch (writes `Formatting.FormatErrorLine` to `stderr`, 125).
- The OCE catch (130 + "interrupted" + envelope) and catch-all (126 + envelope) — preserving the options-before-try structure.
- `DispatchCoreAsync` → private `RunCoreAsync(options, version, stdin, stdout, stderr, cancellationToken)`: the check-mode block (`Console.Out.WriteLine` → the wrapped writer; `stderr` already a parameter inside), listen/connect dispatch (pure threading — engines already take the streams).
- `TryWriteJson`, `UnwrapTypeInit`, `GetVersion` (already anchors `typeof(NetCatOptions).Assembly` — value identical).

### Structural change, convention-mandated

The CTS + `CancelKeyPress` move from `DispatchAsync` (mid-stack) **out to Main** — process-globals in Main, handler body identical (named + finally-unregistered). `DispatchAsync` dissolves; its try/finally unregister becomes Main's.

### What `Program.Main` keeps (~30 lines)

`ConsoleEnv` setup; CTS + handler; `return await Cli.RunAsync(args, Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error, cts.Token);`

### Stream lifetime (the deferred dispose-ownership question, answered)

`Cli.RunAsync` **never disposes** `stdin`/`stdout` — caller-owned. Production: process-lifetime console streams (never disposed today either — parity). Tests: `MemoryStream`s the test owns. The internal check-mode writer is `leaveOpen` and flushed. Documented in the XML `<remarks>` as the seam contract.

### What this unlocks

The full **byte path** in-process for the first time: `MemoryStream` stdin → real loopback socket → peer → `MemoryStream` stdout, for both connect and listen modes; plus deterministic cancellation envelopes.

## Testing strategy

**Two-stage delivery** (the W4 discipline, now standard): stage 1 validates neutrality; stage 2 adds the newly-unlocked coverage after the gates pass.

**Stage 1 — wiring/regression** (`CliRunAsyncTests`, `MemoryStream`s + real loopback sockets):
- `BuildOptions` usage matrix → 125 each: `--listen --check`; `--tls --udp`; `--tls --listen`; `--insecure` w/o `--tls`; `--bind` w/o `--listen`; invalid `--bind` literal; `--bind`/`--ipv4|6` AF mismatches; `--ipv4 --ipv6`; `--verbose` w/o `--check`; `--no-shutdown --check`; wrong positional arity per mode; bad port spec; range outside `--check`.
- Check mode vs a real loopback listener: open port → exact-bytes text line on stdout (the writer-wrap pin); closed port → exit 1; `--verbose` closed line on stderr; `--json` → envelope on stderr AND no text lines on stdout (round-3 CR-I6); DNS-failure scan → stderr summary + exit (probe exact wording first).

**Stage 2 — newly-unlocked:**
- Connect-mode byte path: in-process echo server; stdin bytes **including non-UTF-8 values** (prove it's a byte path) → verbatim response bytes in stdout; `bytes_sent`/`bytes_received` in the envelope.
- Listen-mode byte path: seam listens on an ephemeral port; test client connects/sends; assert stdout + exit.
- Cancellation: pre-cancelled token → 130 + **probed** envelope (nc's `exit_reason` is `"interrupted"`, not `"cancelled"` — probe before pinning); mid-connect cancel (non-responding endpoint + `CancelAfter`) → 130 + liveness bound.
- **Deliberate non-test:** seam-level wall-clock timeout (exit 2) against a firewalled address — flake bait; stays at existing library-level coverage. Recorded so the absence is a decision, not an oversight.

Existing tests (124) pass **unmodified**; comment-only location fixups authorised if any test comments reference Program.cs locations.

## Verification & rollout

Phase 1–3 kit verbatim: branch `feature/cli-seam-nc` (created); per-stream baselines + independent diffs; full `Winix.sln`; WSL; 3-OS CI via dispatch; whole-feature fresh-eyes review; merge `--no-ff` into `release/v0.4.0`.

**Fixture NX1** (cancellation-smoke pattern, 4th adopter): Linux-gated `timeout -s INT` case against a hanging connect target, asserting the **probed** interrupted envelope (exit file 124 = GNU timeout's own code); Windows explicit SKIP.

Docs: CLAUDE.md layout line; memory backlog 1 → 0 — **seam class complete (27/27)**.

## Out of scope

- TLS-listen support, timeout-test hardening, any behaviour change.
- Suite-wide `UnwrapTypeInit` lift to ShellKit (now 3+ copies — a candidate quality follow-up AFTER the seam class closes, noted for the backlog).
