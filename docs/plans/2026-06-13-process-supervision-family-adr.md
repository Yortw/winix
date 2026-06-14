# ADR — Process-Supervision Tool Family

**Date:** 2026-06-13
**Status:** Accepted
**Context:** v0.5.0 adds a family of four command-wrapping tools (`runfor`, `lock`, `attempt`, `soak`). This ADR records the cross-cutting design decisions made during the brainstorm.
**Related design doc:** `2026-06-13-process-supervision-family-design.md`

---

## D1 — Build all four cohesively in v0.5.0

- **Context:** The family has two strong-gap members (runfor, lock) and two medium-gap ones (attempt, soak). Scope could be one, two, or all four.
- **Decision:** Design and build all four in v0.5.0.
- **Rationale:** They share a process-supervision spine and a composition contract; building them together amortises the shared library and yields a coherent family story.
- **Trade-offs accepted:** attempt/soak carry the weaker "bash already does this" gap; ~4× the packaging boilerplate.
- **Options considered:** Strong-gap pair first (defer attempt/soak); single strongest only (runfor); landscape-audit-gated scope. Rejected in favour of the cohesive family per Troy's call.

## D2 — Four separate binaries + a shared class library

- **Context:** Package as four standalone binaries, or one umbrella multi-subcommand binary (`guard timeout…`, `guard lock…`).
- **Decision:** Four separate binaries (`runfor`/`lock`/`attempt`/`soak`), each through the full new-tool checklist, sharing an internal `Winix.ProcessSupervision` class library.
- **Rationale:** Matches every existing Winix tool, including the sibling `retry`/`timeit`/`demux` which are already standalone. Composition reads naturally (`lock -- runfor -- cmd`). The shared library keeps the common spine (spawn, process-tree kill, exit-code mapping, `Cli.Run` seam) DRY — so "4 binaries" is not "4× the logic", only 4× the thin shells + packaging.
- **Trade-offs accepted:** ~4× packaging boilerplate (csproj/scoop/winget/nuget/release/man/docs/contract-snapshot).
- **Options considered:** Umbrella multi-subcommand binary (rejected — breaks one-tool-one-binary, awkward nesting `guard lock -- guard timeout`, inconsistent with standalone `retry`); split standalone+grouped hybrid (rejected — harder to explain, inconsistent).

## D3 — Names: runfor / lock / attempt / soak

- **Context:** Each tool's natural name collides with a shell keyword or coreutils tool (the `switch`→`demux` trap): `timeout` (occupied on both platforms), `flock` (util-linux), `try` (pwsh keyword), and the run-until-fail loop.
- **Decision:** `runfor` (timeout-equiv), `lock` (flock-equiv), `attempt` (try/catch), `soak` (run-until-failure).
- **Rationale:** Each is collision-free, reads well in composition, and (runfor/soak) is self-descriptive. `attempt` distinguishes from `retry` ("try *again*"). `soak` = the established "soak test" endurance term.
- **Trade-offs accepted:** `soak` has a minor conceptual overlap with a future `sponge` ("soaks stdin"); accepted.
- **Options considered:** runfor vs deadline/cap/tmo; lock vs single/once/mutex; attempt vs rescue/onfail/fallback; soak vs flake/runwhile/untilfail.

## D4 — `attempt` runs all commands exec-style (delimiter-separated), not via a shell

- **Context:** `attempt` needs up to three commands (try/catch/finally). They could be exec-style (delimiter-separated argv) or shell strings (`--catch '…'`).
- **Decision:** All three run exec-style (`ArgumentList`, no shell), split by `--catch`/`--finally` delimiters in the post-`--` region. Shell is explicit opt-in (`--catch -- sh -c '…'`).
- **Rationale:** `attempt` exists *because* pwsh/cmd handle native exit codes inconsistently. Routing catch/finally through a platform shell would reintroduce exactly that inconsistency, defeating the tool's purpose. Exec-style keeps exit-code behaviour identical on every platform.
- **Trade-offs accepted:** Verbose; no inline `&&`/redirects (must `-- sh -c '…'` to opt into shell). Edge case: a try command needing a literal `--catch`/`--finally` arg (documented reorder workaround).
- **Options considered:** Shell-string catch/finally (rejected — platform-divergent exit codes); all shell-strings (rejected — kills the consistency pitch).

## D5 — `attempt` exit model: recovery + report-original; finally best-effort + opt-in strict

- **Context:** When TRY fails and CATCH runs, the overall exit code could be CATCH's (recovery) or TRY's (handler). FINALLY failure could be ignored or fatal.
- **Decision:** Recovery model — TRY fails → exit = CATCH's code (successful catch = recovered), with the original TRY failure code reported on stderr + `--json`. FINALLY always runs, exit ignored by default, `--finally-strict` makes a non-zero FINALLY fatal.
- **Rationale:** Mirrors shell `A || B` (intuitive for fallback workflows) while preserving the original failure for diagnostics. Best-effort finally matches `trap`; strict is available for "cleanup must succeed".
- **Trade-offs accepted:** Slightly more machinery (reporting original code; one extra flag).
- **Options considered:** Handler model (try's code always wins — rejected, catch can't rescue); pure recovery without reporting original (rejected — loses diagnostic); strict-by-default finally (rejected — surprising).

## D6 — `lock` uses handle-based file locking on a local path; named-mutex rejected

- **Context:** The lock must auto-release when the holder dies (no stale locks). Mechanism options: handle-based file locks (`flock(2)`/`LockFileEx`/`FileShare.None`) vs a named kernel mutex.
- **Decision:** Handle-based file locking on a filesystem path (`LOCKPATH`), local filesystems only. Auto-release is via OS handle-close on process death.
- **Rationale:** The lock is tied to an open handle; the OS closes all handles on termination (clean, crash, `kill -9`), releasing the lock with no cooperation from the dying process — no stale-lock problem (the bug a PID/lock-file version has). Matches the util-linux `flock` mental model.
- **Trade-offs accepted:** Advisory only (coordination primitive, not a security boundary). **Network filesystems out of scope** (NFS `flock` historically local-only; SMB has disconnect edges). No single portable .NET API → platform-specific code + a pre-build spike.
- **Options considered:** Named mutex/semaphore (rejected — abandoned-mutex wrinkle; uneven cross-process named-mutex support on Unix in .NET; loses the flock model). PID/lock-file (rejected — stale-lock footgun, the version not worth building).

## D7 — `runfor --kill-after` kills immediately on Windows

- **Context:** `--kill-after GRACE` gives a SIGTERM→grace→SIGKILL window on Unix. Windows has no signal model.
- **Decision:** Windows kills the process tree immediately at the deadline; `--kill-after` is documented as a Unix-only graceful window (no-op on Windows).
- **Rationale:** No portable graceful-termination signal on Windows; emulating one (CTRL_BREAK) is fiddly and unreliable. Document the platform difference honestly rather than fake it.
- **Trade-offs accepted:** No graceful cleanup window for Windows children on timeout.
- **Options considered:** CTRL_BREAK graceful-ish emulation on Windows (deferred — fiddly; revisit if demand).

## D8 — `lock --conflict-exit` defaults to 1 (flock parity)

- **Context:** When the lock can't be acquired (`-n`/`-w` expiry), `lock` needs an exit code.
- **Decision:** Default 1 (util-linux `flock` parity), overridable via `--conflict-exit N`.
- **Rationale:** Familiar to `flock` users; the override disambiguates "lock held" from "child exited 1" when a script needs it.
- **Trade-offs accepted:** Default 1 is ambiguous with a child that legitimately exits 1 (same as flock); the override exists for that.
- **Options considered:** A distinct suite code by default (rejected — breaks flock parity / least surprise for flock users).

## D9 — `soak` default output is live passthrough

- **Context:** A run-until-failure loop may run many iterations; output could stream every iteration or be suppressed until the failing run.
- **Decision:** Default live passthrough (inherit stdio); `--quiet` suppresses per-iteration output and prints only the failing run (the one capture mode).
- **Rationale:** Passthrough is transparent and gives live progress; `--quiet` serves the clean flaky-hunt case. Matches the backlog's stated default.
- **Trade-offs accepted:** Noisy across many iterations unless `--quiet`.
- **Options considered:** Default-quiet (rejected — hides live progress; passthrough is the more honest default, quiet is opt-in).

## D10 — `runfor` graceful Unix termination signals the direct child, not the process group (v1)

- **Context:** The graceful Unix escalation (SIGTERM → grace → SIGKILL) must target *something*. The design's Architecture section says "SIGKILL to the **process group**"; its runfor section says send SIGTERM to "**the child**" — an internal inconsistency surfaced during the plan-2a adversarial review (finding B2).
- **Decision:** v1 sends the graceful signal to the **direct child PID only** (libc `kill(childPid, sig)`); if the child ignores it past the grace window, a handle-based `Process.Kill(entireProcessTree: true)` SIGKILL backstop reaps the **whole tree**. No process-group signalling.
- **Rationale:** Matches coreutils `timeout`'s default and the design's runfor-section wording. Portable with no pre-exec machinery. The handle-based backstop is PID-reuse-safe and tree-wide, so a non-cooperating child and its descendants are always reaped.
- **Trade-offs accepted:** A child that *handles* SIGTERM and exits itself **within grace** may **orphan grandchildren** it spawned (they are never signalled, and the tree backstop does not fire because the parent exited in time). Acceptable for v1: the common case is a single child or one that manages its own children on SIGTERM. Documented in the `TerminateGracefully` API and `runfor`'s docs. Also: the initial signal is by raw PID (a narrow reuse window, narrowed by a pre-signal `HasExited` re-check; the BCL exposes no signal-by-handle on Unix).
- **Options considered:** Process-group signalling (`kill(-pgid, …)` — rejected for v1: needs the child to be a session/group leader via `setsid`/`setpgid` pre-exec, which the .NET `Process` API cannot arrange and macOS lacks the `setsid` CLI for; a real cross-platform spike deferred to a future version). This **supersedes the design Architecture section's aspirational "process group" wording**, reconciling it to "direct child".

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| Network-filesystem locking (NFS/SMB) | Unreliable semantics; documented unsupported in v1 |
| `lock` by abstract name (vs path) | Path-only is simpler and matches flock; revisit on demand |
| Windows graceful-signal emulation (`runfor` CTRL_BREAK) | Fiddly/unreliable; v1 kills immediately on Windows |
| `attempt` alternate delimiter for `--catch`/`--finally` arg collision | Rare edge; v1 documents the reorder workaround |
| `soak --quiet` advanced output (ring buffer / tail-N) | v1 prints the failing run only |
| Publishing `Winix.ProcessSupervision` as a package | Internal-only for now (ProjectReference), like FileWalk/SecretStore |
