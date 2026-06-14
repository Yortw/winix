# Process-Supervision Tool Family — Design

**Date:** 2026-06-13
**Status:** Approved (brainstorm complete)
**Companion ADR:** `2026-06-13-process-supervision-family-adr.md`
**Target release:** v0.5.0

## Overview

Four new Winix CLI tools that wrap a child command with a *policy* — a deadline, a lock, a try/catch/finally control flow, or a run-until-failure loop. They form a coherent **process-supervision family** alongside the existing `retry`/`timeit`/`peep`/`demux`, sharing a process-supervision spine and a composition contract that lets them nest.

| Tool | Role | One-line pitch |
|---|---|---|
| **`runfor`** | deadline runner | "coreutils `timeout`, cross-platform — Windows `timeout.exe` is a *sleep*, not this" |
| **`lock`** | overlap guard | "util-linux `flock`, cross-platform — absent on Windows *and* macOS" |
| **`attempt`** | try/catch/finally | "exit-code-driven try/catch/finally, identical on every platform" |
| **`soak`** | run-until-failure | "the until-failure dual of `retry` — a flaky-test hunter" |

All four are built in v0.5.0. Names are final (each dodges a shell/coreutils collision — the `switch`→`demux` trap): `timeout`→**runfor**, `flock`→**lock**, `try`→**attempt**, run-until-fail→**soak**.

## Why these (gap summary)

- **runfor** — STRONG gap. POSIX `timeout cmd` runs a deadline; Windows `timeout.exe` just *sleeps*. A same-named tool doing the wrong thing is an active footgun for anyone porting scripts.
- **lock** — STRONG gap. `flock(1)` is Linux-only (util-linux); absent on macOS and Windows. Cross-platform `lock <path> -- cmd` fills a hole on 2 of 3 platforms.
- **attempt** — MEDIUM gap. bash `A || B` + `trap … EXIT` covers Unix; the gap is cmd's missing `finally` + pwsh `try/catch` **not catching a native `.exe`'s non-zero exit** (sails past `catch` unless you inspect `$LASTEXITCODE`). Value = consistent exit-code-driven control flow on every platform.
- **soak** — MEDIUM/narrow gap. bash `while cmd; do :; done` exists; the gap is Windows (no shell loop) + CI flaky-hunting. Orthogonal to everything: `retry`=until-success, `soak`=until-failure, `peep`=watch.

## Architecture

### Shared library: `Winix.ProcessSupervision`

Internal shared library (ProjectReference, *not* a published NuGet package — like `Winix.FileWalk`/`Winix.SecretStore`). Owns the cross-cutting spine so it is implemented and tested **once**, not four times:

- **Child spawn** via `ProcessStartInfo.ArgumentList` (never string concatenation — suite rule). Default stdio: the child **inherits** the parent's stdin/stdout/stderr so the wrapper is invisible in the pipeline. (`soak --quiet` is the one capture case.)
- **Process-tree termination** — the hard cross-platform primitive: Unix does SIGTERM → (grace) → SIGKILL; Windows has no signal model, so `Process.Kill(entireProcessTree: true)`. (v1 signals the **direct child** then SIGKILL-tree backstop, NOT the process group — see **ADR D10**, which supersedes the earlier "process group" wording.)
- **Exit-code mapping** + the family scheme (below).
- An **injectable process-runner abstraction** (the seam through which tests feed fake child outcomes + timing).

Each tool's *own* library then exposes a **`Cli.Run(args, stdin, stdout, stderr, CancellationToken)` seam** (the suite's testability pattern) so parse→orchestrate→exit is drivable deterministically in-process — driving the shared injectable runner. Lock-specific cross-platform file-locking lives in the `lock` tool's own library (only `lock` needs it), not the shared spine.

### Shared conventions

- **`--` argument boundary**: tool flags before `--`, child command + args after (precedent: `retry`/`wargs`/`envvault`). `attempt` extends this with `--catch`/`--finally` delimiters *inside* the post-`--` region.
- **Exit-code family scheme:**
  - Forward the child's exit code whenever the wrapper's own policy does not fire.
  - **124** — `runfor` deadline exceeded (coreutils parity).
  - **125 / 126 / 127** — tool usage error / command-not-executable / command-not-found (ShellKit convention).
  - **130** — SIGINT (Ctrl+C), forwarded to the child tree.
  - **1** (overridable via `lock --conflict-exit`) — `lock` could not acquire (util-linux `flock` parity).
- **`DurationParser`** (ShellKit; `ms`/`s`/`m`/`h`) for `runfor`'s deadline, `soak --timeout`, `lock -w`.
- **`--json`** on all four (CI structured envelope) plus standard `--help`/`--version`/`--describe`/`--color`/`NO_COLOR`.

## Tool surfaces (v1)

### `runfor` — deadline runner

```
runfor DURATION -- cmd [args...]
```
- Child exits before the deadline → **forward its exit code**. Deadline hits → kill the child *tree*, exit **124**.
- `-k, --kill-after GRACE` — Unix: at the deadline send SIGTERM, wait GRACE, then SIGKILL (lets the child clean up). **Windows: no signal model → kill immediately at the deadline**; `--kill-after` is documented as a Unix-only graceful window (no-op on Windows).
- `-s, --signal NAME` — Unix only; signal sent at the deadline (default TERM). Ignored on Windows (documented).
- `--json` → `{ timed_out, exit_code, duration_ms }`.

### `lock` — overlap guard

```
lock LOCKPATH -- cmd [args...]
```
- Acquire the handle-lock on `LOCKPATH` (blocking by default) → run child, forward its code, release on exit/death. `LOCKPATH` is a filesystem path on **local storage**; it is the lock anchor and is *not* deleted on release.
- `-n, --nonblock` — fail immediately if held.
- `-w, --wait DURATION` — wait up to DURATION for acquisition, then give up.
- `--conflict-exit N` — exit code when the lock cannot be acquired (**default 1**, `flock` parity).
- `--json` → `{ acquired, exit_code }`.
- Acquisition timeout is native (`-w`); *execution* timeout is composed by nesting `runfor` **inside** `lock`.

#### File-path locking (mechanism, safety, spike)

The lock is **the open handle, not the file on disk**: open `LOCKPATH` → get an OS handle → take a kernel lock *via that handle* (`flock(2)` on Unix, `LockFileEx`/share-mode on Windows). "Auto-release on death" is really **release-on-handle-close**, and the OS guarantees it closes every handle a process held on termination (clean exit, crash, `kill -9`, power loss). The dying process does nothing; the kernel reclaims the handle and the lock vanishes with it. This is why it beats a PID/lock-*file* approach: a lock *file* is persistent data that outlives its creator → stale lock; a handle-held lock has no persistent state to go stale. These are **advisory** locks (only block other lock-takers — a coordination primitive, not a security boundary).

**.NET reality:** there is no single portable API. Windows: open with `FileShare.None` (or P/Invoke `LockFileEx`). Unix: P/Invoke `flock(2)` from libc (the BCL does not expose it; `FileStream.Lock` is believed Windows-only — *verify*). Both rest on the same handle-close guarantee.

**Cross-filesystem safety:** reliable on **local** filesystems (NTFS/ext4/APFS); **NFS is a danger zone** (`flock(2)` over NFS was historically local-only — two machines could both "hold" the same lock; later emulation via POSIX `fcntl` has its own stale-lock/grace issues); SMB/CIFS is better but has disconnect-recovery edges. **v1 stance:** lock files must live on a local filesystem; network-share coordination is out of scope and documented (optionally warn on a detected network path).

**Pre-build gate (spike):** a ~30-line throwaway probe per platform confirming acquire / block-second-instance / auto-release-on-`kill -9`, and the exact .NET surface (`FileShare.None` on Windows, P/Invoke `flock` on Unix). Named-mutex was considered and rejected (abandoned-mutex wrinkle; uneven cross-process named-mutex support on Unix in .NET; loses the `flock` mental model).

### `attempt` — try / catch / finally

```
attempt -- TRY... [--catch CATCH...] [--finally FIN...]
```
- All three commands run **exec-style (no shell)**; segments split by the `--catch` / `--finally` delimiters in the post-`--` region. Shell is explicit opt-in (`--catch -- sh -c '…'`). This is deliberate: routing catch/finally through a platform shell would reintroduce the exact pwsh/cmd exit-code inconsistency the tool exists to fix.
- **Exit model (recovery + report-original):**
  - TRY exits 0 → skip CATCH; run FINALLY; exit 0.
  - TRY exits non-zero → run CATCH; **exit = CATCH's code** (a successful catch = recovered). The **original TRY failure code is reported** on stderr and in `--json` so it is not lost.
  - FINALLY always runs; its exit code is **ignored by default** (best-effort cleanup). `--finally-strict` makes a non-zero FINALLY override the final exit code.
- `--finally-strict` — a failing FINALLY fails the invocation.
- `--json` → `{ try_exit, catch_exit, finally_exit, outcome: "ok"|"recovered"|"failed", exit_code }`.
- Edge: a TRY command that literally needs `--catch`/`--finally` as an argument is a documented collision (reorder); the first occurrence delimits. Acceptable for v1.

### `soak` — run-until-failure

```
soak -- cmd [args...]
```
- Re-run the command in a loop; pass child stdout/stderr through (inherit). Stop the moment it exits non-zero → exit with that failing code. A stop cap reached with no failure → exit 0.
- `--max N` — stop after N iterations.
- `--timeout DURATION` — stop after wall-clock duration.
- `--quiet` — suppress per-iteration output; print only the *failing* run's captured output (the one capture mode; default is live passthrough).
- `--json` → `{ iterations, failed, exit_code }`; iteration count to stderr on stop.
- Orthogonality (documented to prevent wrong-tool reach): `retry`=until-success · `soak`=until-failure · `peep`=watch. Inherits the watch-class non-TTY requirement (`Console.IsInputRedirected` precedent — must not assume a tty).

## Composition contract

The universal property that makes them a family: **every tool forwards the child's exit code unless its own policy fires** (runfor→124, lock→conflict, soak→failing code, attempt→recovery). They nest cleanly and the inner result survives:

```
lock -w 10s /tmp/job.lock -- runfor 5m -- mytask        # guard overlap, then bound runtime
soak --max 100 -- attempt -- flaky --catch -- notify     # hunt a flake, handle each failure
retry --until 0 -- runfor 30s -- curl …                  # bound each retry attempt
```

**Ordering rule:** `runfor` nests **inside** `lock` — `lock` owns acquisition-wait (`-w`), `runfor` owns the execution-deadline. Reversing them lets acquisition time bleed into the job's budget (a generic timeout wrapper is blind to the acquire→run boundary). Ctrl+C (130) propagates to the whole child tree.

## Testing strategy

- Each tool exposes the `Cli.Run(...)` seam → deterministic in-process tests driving parse→orchestrate→exit via a **fake process runner** that injects child outcome **and timing** (the fake must mimic lifecycle timing, not just final state — the CLIo BUG-010 lesson; e.g. a child that exits at T+Δ, not instantly).
- **Process-tree kill** integration test: spawn child→grandchild, fire the kill, assert *both* die (the wargs "killed all in flight" property), platform-gated.
- **Exit-code matrices** with **negative/invariant cases** (test the requirement, not the mechanism): runfor child-in-time forwards code *and is not 124*; soak cap-reached-no-failure is *0, not the last child's code*; attempt try-success means CATCH is *not* run; recovery means exit = CATCH's code *not* TRY's.
- **Ctrl+C → 130**: the real OS signal hop has no automated coverage anywhere in the suite (ADR D4 precedent) → seam-level test with a cancelled token + an **honest "manual-verified" note**, not a false completeness claim.
- **Platform-gated `SkippableFact`** for Unix signals (`--signal`, TERM→KILL) vs Windows kill.
- **`lock` file-locking spike** is a pre-build gate (above), captured as a throwaway probe.

## Build order

Shared `Winix.ProcessSupervision` lib → **`runfor`** (simplest consumer, validates the spine) → **`lock`** (run the spike here) → **`soak`** → **`attempt`** (most parsing). The lock spike is done up front to de-risk. Each tool (and the shared lib) gets its **own** implementation plan so they build/review as small independent units rather than one giant plan.

## Packaging

Four separate binaries, each through the full new-tool checklist: csproj (`Description`, `PackageTags`), `bucket/{tool}.json` scoop manifest, `release.yml` + `post-publish.yml` (nuget + winget) entries, `README.md`, `man/man1/{tool}.1.md`→pandoc, `docs/ai/{tool}.md`, `llms.txt`, contract snapshot in `Winix.Contract.Tests` with **`.Maturity(ToolMaturity.Fresh)`**, and a `run-smokes.sh` fixture. The shared `Winix.ProcessSupervision` lib is internal (ProjectReference, no package). Update `CLAUDE.md` (project layout + NuGet package-ID list + scoop manifest list). NuGet package IDs: `Winix.RunFor`, `Winix.Lock`, `Winix.Attempt`, `Winix.Soak`.

## Out of scope / deferred (v1)

- Network-filesystem locking (`lock` over NFS/SMB) — documented unsupported.
- `lock` by abstract name (vs path) — v1 is path-only.
- Windows graceful-signal emulation for `runfor --kill-after` (CTRL_BREAK) — v1 kills immediately on Windows.
- `attempt` alternate delimiter for the `--catch`/`--finally` arg collision — v1 documents the reorder workaround.
- `soak --quiet` advanced output policies (ring buffer, tail-N) — v1 captures and prints the failing run only.
