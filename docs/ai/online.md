# online — AI Agent Guide

## What This Tool Does

`online` is a blocking network-readiness gate. It polls until every requested check passes, then exits 0. Primary use case: an agent or script blocks on `online` instead of writing its own poll loop — then resumes work exactly once the preconditions are met.

Two kinds of checks are available:

- **`--internet`** — a layered, captive-portal-aware check: OS route (fast negative with zero traffic when Wi-Fi is down), DNS resolution, then HTTP GET expecting status 204. A captive portal returns 200/302 (a login page), never 204, so `online` correctly treats a portal as "not online".
- **`--url URL`** — wait until a named URL returns a matching HTTP status (`--status`, default `2xx`).

When both kinds are given, the gate opens only when every check passes in the same poll cycle (AND semantics).

## Platform Story

Cross-platform. No platform ships a captive-portal-aware network-readiness gate. Windows `Test-Connection` is ICMP-based and portal-blind. Unix scripts typically `curl --retry` against a URL — which skips the OS-route and DNS rungs and can't detect a captive portal. `online` gives every platform a consistent, layered gate.

## When to Reach for This

- **Block until the network is back** — `online` exits 0 when internet connectivity is confirmed; script resumes.
- **Block until the network is back AND a named server is healthy** — combine `--internet` with `--url`.
- **CI pre-flight** — gate a test run or deploy step that needs real connectivity.
- **Laptop-resume automation** — `online --once` gives a fast is-it-up check after sleep/wake.
- **Polling pattern** — `online --once --url https://api/health` is a cheap single-shot health check for use inside a broader poll loop.

### vs `nc --check` (raw TCP port, Layer 4)

`nc -z host port` probes whether a TCP port is open. It operates at Layer 4 — it confirms the port accepts connections, not that the service behind it is HTTP-healthy or that the internet route is clean. Use `nc` when you care about port reachability; use `online` when you care about HTTP-level health or captive-portal-aware internet connectivity.

### vs `retry` (runs a command)

`retry` runs a child command and retries it on failure. `online` is a pure gate — no child command. The typical composition is:

```bash
online --internet --url https://api.example.com/health && retry --times 3 dotnet test
```

`online` blocks until ready; `retry` handles transient failures of the actual work.

## Exit-Code Contract

| Code | Meaning |
|------|---------|
| 0 | Ready — every requested check healthy |
| 1 | `--once` only: checked once, not ready right now |
| 124 | Timed out before ready (wait mode) |
| 125 | Usage error — bad arguments, unparseable duration/status, malformed URL, `--endpoint` without `--internet`, `--status` without `--url` |
| 126 | Unexpected error (tool fault) |
| 130 | Interrupted (Ctrl+C) |

## Common Patterns

**Wait for internet (default):**
```bash
online
```

**Single-shot health check — is it up right now?**
```bash
online --once
```
Exit 0 = yes. Exit 1 = no. No waiting.

**Block until network back AND server healthy:**
```bash
online --internet --url https://api.example.com/health && resume-work
```

**Custom status — wait for 200 or 204:**
```bash
online --url https://api.example.com/ready --status 200,204
```

**Shorter timeout with diagnostics:**
```bash
online --timeout 2m --verbose
```

**Override the built-in 204 probes:**
```bash
online --internet --endpoint https://myprobe.internal/generate_204
```

**JSON output for structured parsing:**
```bash
online --json --url https://api.example.com/health
```

## `--once` Polling Pattern

Use `--once` when you want a single cheap probe rather than a blocking gate — for example, inside your own loop or a CI health-check step:

```bash
if online --once --url https://api.example.com/health; then
  echo "API is up"
fi
```

`--once` runs exactly one poll cycle with no sleep. Exit 0 = every check healthy right now. Exit 1 = not ready right now.

## Redirect Handling

The HTTP client does not follow redirects (`AllowAutoRedirect` is off). This is important for two reasons:

1. A captive portal's `302 → login page` is seen as non-204 and correctly treated as "not online" by the `--internet` check.
2. A `--url` target that 301/302-redirects will not match the `2xx` default and will keep waiting. Use `-v` to diagnose which status is being returned, then either update the URL to the final destination or widen `--status` if appropriate.

## JSON Fields

`--json` writes the envelope to **stdout** (standard for own-data tools). Human summary and `-v` lines go to **stderr**.

| Field | Type | Description |
|-------|------|-------------|
| `tool` | string | `"online"` |
| `version` | string | Tool version |
| `ready` | bool | Whether every requested check passed |
| `timed_out` | bool | Whether the wait budget was exhausted |
| `elapsed_ms` | int | Wall time in milliseconds |
| `attempts` | int | Poll cycles run |
| `checks` | object[] | Per-check results: `{ kind, target, ok, detail }` |

## Gotchas

**`--endpoint` requires `--internet`.** Using `--endpoint` with only `--url` checks is a usage error (exit 125). `--endpoint` overrides the built-in 204 probes for the internet check; it is not an alias for `--url`.

**`--status` requires `--url`.** Using `--status` without any `--url` is a usage error (exit 125).

**Timeout overshoot.** The deadline is checked between poll cycles, not within a probe. If a probe is slow, the actual wait can slightly exceed `--timeout`, and `elapsed_ms` may exceed the timeout value. Invisible at the 10-minute default; only matters for very short timeouts with slow probes.

**DNS and single-family networks.** The DNS rung accepts an address of any family (IPv4 or IPv6). On a single-family network, a name that resolves to the unusable family passes DNS and falls through to "not online" at the HTTP rung. The `-v` DNS line may overstate success in this case; the HTTP failure will still be reported.

**Default is `--internet`, not `--url`.** Bare `online` runs the internet check. To gate only on a specific URL (with no internet check), pass `--url` explicitly and omit `--internet`.

## Getting Structured Data

```bash
online --json --url https://api.example.com/health
```

Parse with `jq`:
```bash
online --json 2>/dev/null | jq '.ready, .elapsed_ms'
```

**`--describe`** — machine-readable flag reference:
```bash
online --describe
```
