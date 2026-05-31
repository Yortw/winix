# hcat — AI Agent Guide

## What This Tool Does

`hcat` is "netcat for HTTP" — an instant HTTP server in a single cross-platform native binary, no runtime and no daemon. Where `nc` gives you a raw TCP socket, `hcat` gives you an HTTP one. It has four modes: serve a folder with an auto directory listing, inspect/record incoming requests (httpbin-style), pipe a command over HTTP (CGI-style), and a bare-invocation local-preview server. It overlaps with `python -m http.server`, `miniserve`, and `webhook.site` but is one binary with no Python/Node runtime, LAN-share QR codes, and CI stop conditions built in.

## When to Use This

- Quickly serving a folder over HTTP for preview or transfer: `hcat serve ./dir` (or bare `hcat` for the current directory on localhost)
- Sharing files on the LAN with a scannable QR code: `hcat serve ./dir --lan`
- Catching and inspecting incoming webhooks/requests during development or in tests: `hcat inspect`
- Recording a request stream to a file for later assertion: `hcat inspect --json > requests.jsonl`
- Exposing a CLI filter over HTTP without writing a server: `hcat pipe -- jq .`
- CI fixtures that must wait for / assert on an HTTP callback: `hcat inspect --exit-on path=/done --timeout 30s`

## When NOT to Use This

- As a production or public-facing web server — there is no auth, no IP allowlist, and `--https` uses a self-signed cert that clients distrust
- When you need per-path routing in `pipe` mode — v1 runs a single command for every request
- When you need a raw, non-HTTP TCP listener — use `nc` instead
- When binary size matters and you only need static files on localhost — `hcat` is ~12 MB (the ASP.NET Core / Kestrel tax)

## Network Safety — Localhost by Default

By default `hcat` binds **`127.0.0.1` only**. Network exposure is always explicit:

- `--lan` binds `0.0.0.0` (all interfaces) and prints a QR code with the LAN URL.
- `--host <addr>` binds an explicit address; a non-loopback address exposes the server.
- `--local` forces loopback-only, overriding any earlier `--lan` / `--host`.

There is **no authentication or IP allowlist in v1**. Treat `--lan` as "anyone on this network can reach it" — use trusted networks only.

## Basic Invocation

```bash
# Serve the current directory on http://127.0.0.1:8080 (localhost only)
hcat

# Serve a specific folder, shared on the LAN (prints a QR code)
hcat serve ./public --lan

# Echo every incoming request back as JSON
hcat inspect

# Run jq once per request: body -> stdin, stdout -> response
hcat pipe -- jq .
```

The bind banner and human-readable request log go to **stderr**. With `--json`, machine-readable output goes to **stdout** (kept clean for piping).

## Modes

- **serve** (`hcat serve [dir]`, or bare `hcat`): static files + auto directory listing. `--upload` enables a POST receiver that saves to a dedicated `./uploads` directory (created but **not** served); `--upload-dir <path>` relocates it; `--upload-dir .` (the served root) is the deliberate escape hatch that makes uploads downloadable. Path traversal outside the served/upload roots is rejected.
- **inspect** (`hcat inspect`): responds to every request with a JSON object describing it. `--status <code>` overrides the response code (default 200). `--json` also writes each record as a JSONL line to stdout.
- **pipe** (`hcat pipe -- <cmd>`): runs `<cmd>` per request. Request body → child stdin; child stdout → response body; request metadata → environment variables; exit 0 → 200, non-zero → 500; child stderr → server console (never the HTTP response).

## JSON Output

Pass `--json` for machine-readable output on **stdout**. In `inspect` (and `pipe`) mode each captured request is a JSONL line:

```json
{"method":"POST","path":"/hook","query":"","headers":{"Content-Type":"application/json"},"body":"{...}","timestamp":"2026-05-30T04:12:09Z","remote":"127.0.0.1"}
```

| Key | Description |
|---|---|
| `method` | HTTP method. |
| `path` | Request path. |
| `query` | Raw query string. |
| `headers` | Object of request headers (keys verbatim). |
| `body` | Body text, or null. Truncated to 1 MiB when oversized (see `bodyTruncated`). In `pipe` mode the record carries no body — it streams to the child. |
| `timestamp` | Capture time, UTC ISO 8601. |
| `remote` | Caller's remote address. |
| `bodyTruncated` | `true` when the body exceeded the 1 MiB cap and was truncated. |

In `serve` mode, `--json` emits a per-request **access-log** line to stdout instead — `{"method":"GET","path":"/file.txt","status":200}` (method/path and the final HTTP status). Without `--json`, every mode writes a terse human request log to stderr (`GET /file.txt 200` for serve; `METHOD /path` for inspect/pipe).

## Stop Conditions for CI

```bash
# Stop after 3 requests
hcat inspect --capture 3

# Stop on the first matching request
hcat inspect --exit-on path=/done
hcat inspect --exit-on method=POST
hcat inspect --exit-on body~deploy-complete

# Bound the wait — exit 1 if not met in time
hcat inspect --exit-on path=/done --timeout 30s

# CI stop conditions work in every mode, not just inspect
hcat serve ./dist --capture 1            # serve one request, then exit 0
hcat serve ./dist --exit-on path=/health # exit when /health is hit
```

`--exit-on` keys are exactly `path`, `method` (compared with `=`), and `body` (substring match with `~`). An unknown key is a usage error (exit 125) at parse time, not a silent never-match. `--capture`/`--exit-on`/`--timeout` apply to all modes (`serve`/`inspect`/`pipe`), but `--exit-on body~` is **inspect-only** — serve never reads the body and pipe streams it to the command, so neither captures it to match; a `body~` predicate in those modes is a usage error. If the stop condition is met the tool exits 0; if `--timeout` elapses first it exits 1.

## Composability

```bash
# Record requests, then assert on them with jq
hcat inspect --capture 1 --json | jq -r '.path'

# Serve a build output folder for a quick remote check
hcat serve ./dist --lan
```

## Limitations

- Single-command pipe only — no per-path routing in v1.
- `--https` uses an in-memory self-signed cert → clients show a trust warning (dev / LAN).
- No auth or IP allowlist in v1.
- ~12 MB binary (the ASP.NET Core / Kestrel dependency).
- An interrupted upload may leave a partial file at the target name — no atomic write-then-rename in v1 (F11).
- Concurrent pipe-mode child stderr may interleave on the server console — diagnostic only, never in any HTTP response (F12).
- Request bodies larger than 1 MiB are truncated in the record and marked `bodyTruncated`.
- If the child streams stdout, the 200 status is committed before its exit code is known, so a non-zero exit after output flushes cannot be downgraded to 500.

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success — clean shutdown (Ctrl+C), or a CI stop condition (`--capture` / `--exit-on`) was met. |
| 1 | A CI stop condition was not met before `--timeout` elapsed. |
| 125 | Usage error — unknown subcommand/flag, bad `--exit-on` key, non-integer `--port` / `--status` / `--capture`, or `pipe` with no command. |
| 126 | Startup failure — the port could not be bound (e.g. already in use) or the self-signed certificate could not be created. |

## Metadata

Run `hcat --describe` for full structured metadata (flags, modes, examples, exit codes).
