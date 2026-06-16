# runfor — AI Agent Guide

## What This Tool Does

`runfor` runs a command with a hard time limit — cross-platform timeout(1) with a graceful Unix kill window. It enforces a deadline on any child process, forwarding the child's exit code on success and returning 124 if the deadline fires.

## Platform Story

Cross-platform. The headline value on Windows: **`timeout.exe` only sleeps — it does not bound a command.** `timeout 30` on Windows just waits 30 seconds and then returns; it cannot enforce a deadline on an arbitrary child process. `runfor` actually enforces the deadline. On Unix it fills the same role as coreutils `timeout`, adding a consistent `--json` envelope and a unified exit-code family across platforms.

### vs coreutils `timeout` (Unix)

`timeout 30s curl https://example.com` and `runfor 30s -- curl https://example.com` are functionally equivalent on Unix. Reach for `runfor` when you need the `--json` envelope, a consistent exit-code family across platforms, or composition with other Winix tools. Reach for coreutils `timeout` when it is already available and you have no need for JSON or cross-platform portability.

### vs `retry` (retries on failure)

`retry` re-runs a child on failure. `runfor` enforces a per-run deadline. The typical composition is:

```bash
retry --times 3 -- runfor 30s -- curl https://example.com
```

`runfor` enforces the per-attempt deadline; `retry` handles transient failures of the actual work.

### Composition with other supervision tools

`runfor` is one of a process-supervision family. It composes with sibling tools at the shell level by nesting `--` command boundaries (an outer tool wraps `runfor`, which wraps the real command). `retry` (above) is the shipped example. A `lock` tool (advisory mutual-exclusion) is planned for the same v0.5.0 family but is **not yet shipped** — once it lands you will be able to bound a lock-holding command by nesting the two; its exact flag surface is not finalised, so no concrete `lock` example is given here yet.

## When to Reach for This

- **Bound an HTTP request, build, or test run** — wrap any command that hangs: `runfor 30s -- curl https://example.com`.
- **Cross-platform scripts** — Windows `timeout.exe` only sleeps; `runfor` works identically on Windows and Unix.
- **Graceful escalation (Unix)** — `--kill-after` sends SIGTERM at the deadline then SIGKILL after a grace period: `runfor --kill-after 3s 10s -- ./server`.
- **Structured pipeline output** — `--json` emits a machine-readable envelope on stderr for post-processing or logging.
- **Composition with retry** — wrap a flaky command: `retry --times 3 -- runfor 30s -- cmd`.

### When NOT to reach for this

- **You only need to sleep for a fixed duration** — use `sleep`, `timeout.exe` (Windows), or a platform sleep primitive; `runfor` requires a command to wrap.
- **Unix and the command respects SIGTERM reliably, with no need for `--json` or a consistent exit-code family** — coreutils `timeout` is already present and sufficient.

## Exit-Code Contract

| Code | Meaning |
|------|---------|
| 0–123 | Child exited 0 before the deadline (or forwarded child code 1–123) |
| 124 | Deadline exceeded — the child was terminated |
| 125 | Usage error: missing/invalid DURATION, no command, bad `--signal`/`--kill-after` |
| 126 | Command not executable |
| 127 | Command not found on PATH |
| 130 | Interrupted by Ctrl+C |

## Common Patterns

**Bound a request after 30 seconds:**
```bash
runfor 30s -- curl https://example.com
```

**Cap a test run at 5 minutes:**
```bash
runfor 5m -- dotnet test
```

**SIGTERM at 10s, SIGKILL 3s later if it ignores it (Unix):**
```bash
runfor --kill-after 3s 10s -- ./server
```

**Send SIGINT instead of SIGTERM at the deadline (Unix):**
```bash
runfor --signal INT 1m -- ./job
```

**JSON output for structured parsing:**
```bash
runfor --json 30s -- curl https://example.com
```

**Wrap a flaky command with retry + per-attempt deadline:**
```bash
retry --times 3 -- runfor 30s -- curl https://api.example.com/health
```

## `--json` Output

`--json` writes the envelope to **stderr** (runfor's own output channel — the child owns stdout, which passes through unmodified). Human notices (timeout/interrupt) also go to stderr.

| Field | Type | Description |
|-------|------|-------------|
| `tool` | string | `"runfor"` |
| `version` | string | Tool version |
| `exit_code` | int | runfor's exit code (forwarded child code, 124 on timeout, 130 on interrupt, 126/127 on launch failure) |
| `outcome` | string | `completed` \| `timed_out` \| `interrupted` \| `launch_failed` |
| `timed_out` | bool | `true` iff the deadline fired |
| `child_exit_code` | int\|null | Child's exit code if it completed; `null` on timeout/interrupt/launch failure |
| `signal` | string | Signal name configured for the deadline (Unix): e.g. `"TERM"`, `"KILL"` |
| `kill_failed` | bool | `true` iff a kill was attempted but could not be confirmed (child may still be running) |
| `duration_ms` | int | Wall-clock time from launch to resolution, in milliseconds |

**Example timeout envelope:**
```json
{"tool":"runfor","version":"0.5.0","exit_code":124,"outcome":"timed_out","timed_out":true,"child_exit_code":null,"signal":"TERM","kill_failed":false,"duration_ms":30012}
```

Parse with `jq`:
```bash
runfor --json 30s -- curl https://example.com 2>&1 >/dev/null | jq '.outcome, .duration_ms'
```

Note: the envelope is on stderr. `2>&1 >/dev/null` routes stderr into the pipe and discards the child's own stdout, so `jq` sees only runfor's JSON (not, e.g., the curl response body).

## Platform Behaviour

### Unix

At the deadline, runfor sends `--signal` (default `TERM`) to the direct child. A child that **ignores** the signal survives — there is no automatic SIGKILL backstop unless `--kill-after` is used. This matches `timeout` without `-k`, so existing scripts behave identically.

`--kill-after GRACE` opts into escalation: after the deadline signal, runfor waits GRACE then sends `SIGKILL` to the entire process tree. Use this when the child may ignore `TERM`.

### Windows

runfor kills the entire process tree immediately at the deadline using `TerminateProcess`. There is no signal model on Windows; `--signal` and `--kill-after` are accepted but have no effect.

## Limitations

runfor signals only the **direct child**. A child that handles the signal and exits within the `--kill-after` grace may leave its own grandchildren running — the SIGKILL tree-backstop only reaps the whole tree when the child **ignores** the signal past grace. For a wrapper that spawns long-lived workers, have it forward the signal to its children.

## Gotchas

**Use `--` before commands that take their own dashed flags.** Without `--`, runfor may interpret the command's flags as its own. Example: `runfor 30s -- curl --max-time 10 https://example.com`.

**`--signal` and `--kill-after` are no-ops on Windows.** Windows kills the process tree immediately; the signal model does not apply.

**The `--json` `signal` field is the *configured* signal, not proof one was sent.** It reflects what `--signal` resolved to (default `TERM`). On Windows the field is still emitted (e.g. `"signal":"TERM"`) even though Windows sends no signal at all — don't read it as evidence SIGTERM was delivered.

**`kill_failed: true` means the child may still be running** — but it is only ever set when a kill was actually *attempted with confirmation*. In the coreutils-faithful default mode (no `--kill-after`), runfor sends the signal once and does **not** confirm the kill, so `kill_failed` is always `false` there even if delivery failed (e.g. the child is owned by another user / EPERM). If you need runfor to *confirm* the child is dead — and to surface `kill_failed: true` when it can't — pass `--kill-after` (which adds the SIGKILL tree-backstop). When the deadline fires under `--kill-after` and the kill cannot be confirmed, runfor still returns 124 and emits the warning.

**Duration format:** a number followed by a unit suffix — `ms` (milliseconds), `s` (seconds), `m` (minutes), `h` (hours). Examples: `500ms`, `30s`, `5m`, `1h`.

## Getting Structured Data

```bash
runfor --json 30s -- curl https://example.com
```

The envelope goes to stderr. To capture and parse it:
```bash
result=$(runfor --json 30s -- curl https://example.com 2>&1 >/dev/null)
echo "$result" | jq '.outcome, .duration_ms'
```

**`--describe`** — machine-readable flag reference:
```bash
runfor --describe
```
