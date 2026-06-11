# online

Block until the internet — or an endpoint — is actually healthy.

A network-readiness gate for scripts and agents: `online` polls until every requested check passes, then exits 0. Use it instead of writing your own poll loop.

**No platform ships a captive-portal-aware network-readiness gate** — `online` fills that gap everywhere.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/online
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Online
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Online
```

### From Source

```bash
dotnet publish src/online/online.csproj -c Release -r <rid>
```

Replace `<rid>` with `win-x64`, `linux-x64`, or `osx-arm64` as appropriate.

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
online [options]
```

### Examples

```bash
# Wait up to 10 minutes for working internet (default)
online

# Is the internet up right now? (exit 0 yes / 1 no)
online --once

# Block until BOTH the network is back AND a named server is healthy
online --internet --url https://api.example.com/health && resume-work

# Wait for a specific URL with a custom status range
online --url https://api.example.com/health --status 200,204

# Shorter timeout with verbose diagnostics
online --timeout 2m --verbose

# Override the built-in connectivity endpoints
online --internet --endpoint https://myprobe.internal/generate_204

# Pipe into retry — wait for network then run tests
online && retry --times 3 dotnet test
```

### Output

**Default** (stderr on completion):
```
online: ready after 3 attempt(s), 4.2s
```

**JSON** (`--json`, stdout):
```json
{
  "tool": "online",
  "version": "0.1.0",
  "ready": true,
  "timed_out": false,
  "elapsed_ms": 4203,
  "attempts": 3,
  "checks": [
    { "kind": "internet", "target": "https://www.gstatic.com/generate_204", "ok": true, "detail": "204" }
  ]
}
```

## How the Internet Check Works

`--internet` (the default) runs a **three-rung layered check** designed to distinguish "no connectivity" from "network up but behind a captive portal":

1. **OS route check** — inspects network interfaces and routing table. If the interface is down (Wi-Fi disconnected, cable unplugged) the check fails immediately with zero network traffic.
2. **DNS** — resolves the host name of the probe endpoint. Failure here means the local resolver is unreachable.
3. **HTTP GET → 204** — sends a GET to the probe URL and expects HTTP status **204 No Content**. A captive portal returns 200 or 302 (a login page), never 204. The response body is never read.

Default probe endpoints (tried in randomised order, first 204 wins):
- `https://www.gstatic.com/generate_204`
- `https://cp.cloudflare.com/generate_204`

Use `--endpoint URL` to override these with your own 204-returning probe.

## Redirect Handling

The HTTP client does **not** follow redirects (`AllowAutoRedirect` is off). This is intentional:

- A captive portal's `302 → login page` is seen as a non-204 response and correctly treated as "not online".
- A `--url` target that permanently redirects (301/302) will not match the `2xx` default and will keep waiting. Add `-v` to see which status was returned, then either update the URL or widen `--status`.

## Options

| Option | Default | Description |
|--------|---------|-------------|
| `--internet` | on when no other check given | Layered, captive-portal-aware internet check |
| `--url URL` | — | Wait until URL returns a status matching `--status` (repeatable) |
| `--endpoint URL` | built-in 204 probes | Override built-in connectivity endpoints for `--internet` (repeatable; requires `--internet`) |
| `--status SPEC` | `2xx` | Expected status for `--url`. Forms: `2xx`, list `200,204`, range `200-299` (requires `--url`) |
| `--timeout DURATION` | `10m` | Total wait budget. `0` = wait forever |
| `--interval DURATION` | `2s` | Sleep between poll cycles |
| `--probe-timeout DURATION` | `3s` | Per-probe DNS/HTTP timeout |
| `--once` | off | Run one cycle and exit (no waiting). Exit 0 ready, 1 not ready |
| `-v`, `--verbose` | off | Per-attempt diagnostics to stderr |
| `--json` | off | JSON envelope to stdout |
| `--color[=auto\|always\|never]` | auto | Coloured output |
| `--no-color` | | Disable coloured output |
| `--version` | | Show version |
| `-h`, `--help` | | Show help |
| `--describe` | | AI/agent metadata (JSON) |

### Duration Format

Durations accept a number followed by a unit suffix: `ms` (milliseconds), `s` (seconds), `m` (minutes). Examples: `500ms`, `30s`, `2m`, `10m`.

### AND Semantics for Multiple Checks

When `--internet` and one or more `--url` checks are given together, the gate opens (exit 0) only when **every** check passes in the **same poll cycle**. Each cycle runs all checks before deciding.

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Ready — every requested check healthy |
| 1 | `--once` only: checked once, not ready right now |
| 124 | Timed out before ready (wait mode) |
| 125 | Usage error — bad arguments, unparseable duration/status, malformed URL, `--endpoint` without `--internet`, `--status` without `--url` |
| 126 | Unexpected error (tool fault) |
| 130 | Interrupted (Ctrl+C) |

## JSON Output

`--json` writes the envelope to **stdout** (own-data tool). Human summary and `-v` lines go to **stderr**.

| Field | Type | Description |
|-------|------|-------------|
| `tool` | string | `"online"` |
| `version` | string | Tool version |
| `ready` | bool | Whether every requested check passed |
| `timed_out` | bool | Whether the wait budget was exhausted |
| `elapsed_ms` | int | Wall time in milliseconds |
| `attempts` | int | Poll cycles run |
| `checks` | object[] | Per-check results: `{ kind, target, ok, detail }` |

## Notes / Caveats

**Timeout overshoot.** The deadline is checked between poll cycles, not within a probe. If a probe is slow, the actual wait can exceed `--timeout` by up to one cycle's probe time, and `elapsed_ms` in the JSON envelope may slightly exceed the timeout value. This is invisible at the 10-minute default; only matters for very short timeouts with slow probes.

**DNS and single-family networks.** The DNS rung of the internet check accepts an address of any address family (IPv4 or IPv6). On a single-family network, a host name that resolves to the unusable family passes the DNS rung and correctly falls through to "not online" at the HTTP rung — but the `-v` DNS line may report success even though the address returned is unusable. The HTTP failure will still be reported.

## Colour

- Automatic: colour when outputting to a terminal, plain when piped
- `--color` forces colour on (overrides `NO_COLOR`)
- `--no-color` forces colour off
- Respects the `NO_COLOR` environment variable ([no-color.org](https://no-color.org))

## Part of Winix

`online` is part of the [Winix](../../README.md) CLI toolkit.
