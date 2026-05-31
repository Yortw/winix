# hcat

Netcat for HTTP — an instant HTTP server in one native binary, no runtime, no daemon. Serve a folder, catch incoming requests, or pipe a command over HTTP. Uses ASP.NET Core's Kestrel under the hood, packaged as a single AOT binary.

Where `nc` gives you a raw TCP socket, `hcat` gives you an HTTP one: static-file serving with directory listings, an httpbin-style request echo/recorder, and a CGI-style "run this command per request" pipe. It overlaps with `python -m http.server`, [`miniserve`](https://github.com/svenstaro/miniserve), and [`webhook.site`](https://webhook.site) — but it is one cross-platform binary with no Python/Node runtime, LAN-share QR codes, and CI stop conditions built in.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/hcat
```

### Winget (Windows, stable releases)

```bash
winget install Winix.HCat
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.HCat
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Safety: localhost-only unless you opt in

By default `hcat` binds **`127.0.0.1` only** — nobody else on your network can reach it. Network exposure is always explicit:

- `--lan` binds `0.0.0.0` (every interface) and prints a QR code with the LAN URL so a phone can scan it.
- `--host <addr>` binds an explicit address. A non-loopback `--host` is also an explicit choice to expose the server.
- `--local` forces loopback-only, overriding any earlier `--lan` / `--host`.

There is **no authentication or IP allowlist in v1** — `--lan` exposes the server to everyone on your network. Use it on trusted networks only.

> **Which address does `--lan` show?** On a machine with virtual adapters (Hyper-V, WSL, Docker, VPNs), `--lan` deliberately lists only addresses on a gateway-routed interface — the ones a device on your real LAN can actually reach — and hides host-only/virtual addresses to keep the banner and QR unambiguous. If you specifically want to bind a host-only/virtual address (e.g. to reach a VM), pass it with `--host <addr>`. If no interface has a default gateway, all addresses are shown.

## Usage

```
hcat                       Serve the current directory on 127.0.0.1 (local preview).
hcat serve [dir]           Serve static files with an auto directory listing.
hcat inspect               Echo each request back as JSON (httpbin-style).
hcat pipe -- <cmd>         Run <cmd> per request (CGI-style): body→stdin, stdout→response.
```

### Serve files (default mode)

```bash
# Serve the current directory on http://127.0.0.1:8080 (localhost only)
hcat
# Serving ./myproject
#   http://127.0.0.1:8080
#   (localhost only — pass --lan to share on your LAN)

# Serve a specific folder
hcat serve ./public

# Share it on your LAN — binds 0.0.0.0 and prints a QR code
hcat serve ./public --lan
```

`serve` serves static files and renders an automatic directory listing for folders with no `index.html`. Path traversal outside the served root is rejected.

### Receive uploads

```bash
# Enable a POST upload receiver. Files are saved to ./uploads (created, NOT served).
hcat serve ./public --upload

# Relocate the upload target
hcat serve ./public --upload --upload-dir /tmp/incoming

# Escape hatch: point uploads at the served root so they are downloadable
hcat serve . --upload --upload-dir .
```

With `--upload`, `hcat` accepts `POST` bodies and writes them to a dedicated `./uploads` directory that is **created but not served** — so uploads can't immediately be re-fetched. `--upload-dir <path>` relocates that target. Pointing it at the served root (`--upload-dir .`) is the deliberate escape hatch that makes uploads downloadable. Upload paths are sanitised: a request can never write outside the upload directory.

### Inspect requests (httpbin-style)

```bash
# Echo each incoming request back as JSON
hcat inspect

# Catch webhooks from your LAN
hcat inspect --lan

# Override the response status (e.g. to test a client's 500 handling)
hcat inspect --status 503

# Record every request as a JSONL stream to stdout
hcat inspect --json > requests.jsonl
```

`inspect` responds to every request with a JSON object describing that request (method, path, query, headers, body, timestamp, remote address). `--status <code>` overrides the response code (default 200). With `--json`, the same record is also written as a JSONL line to **stdout**, so you can capture a request stream to a file or pipe it to `jq`.

### Pipe a command over HTTP (CGI-style)

```bash
# Expose jq over HTTP: POST a body, get the jq output back
hcat pipe -- jq .

# Anything that reads stdin and writes stdout works
hcat pipe -- tr a-z A-Z
```

`pipe` runs `<cmd>` once **per request**: the request body is fed to the child's **stdin**, the child's **stdout** becomes the HTTP response body, and request metadata is exposed as environment variables (CGI-style). The child's exit code maps to the response status — exit `0` → `200`, any non-zero → `500`. The child's **stderr** is written to the server console (diagnostic only; it never enters the HTTP response).

### Stop conditions for CI

```bash
# Exit cleanly after catching 3 requests
hcat inspect --capture 3

# Exit when a specific request arrives
hcat inspect --exit-on path=/done
hcat inspect --exit-on method=POST
hcat inspect --exit-on body~deploy-complete

# Fail (exit 1) if the condition isn't met within 30 seconds
hcat inspect --exit-on path=/done --timeout 30s
```

`--capture <N>` stops after N requests, `--exit-on <expr>` stops on the first matching request (`path=`, `method=`, or `body~` substring), and `--timeout <dur>` bounds the wait. If the stop condition is met the tool exits **0**; if `--timeout` elapses first it exits **1**.

These flags work in **all** modes (`serve`, `inspect`, `pipe`) — e.g. `hcat serve ./dist --capture 1` serves one request then exits. Note `--exit-on body~` is **inspect-only**: serve never reads the body and pipe streams it to the command, so neither captures it to match against (a `body~` predicate there is a usage error rather than a silent never-match).

### HTTPS

```bash
# Serve over TLS with an in-memory self-signed certificate
hcat serve ./public --https --lan
```

`--https` enables TLS using a self-signed certificate generated in memory. Clients will show a trust warning (it isn't CA-signed) — this is intended for development and LAN use.

## Options

| Flag | Argument | Description |
|---|---|---|
| `--lan` | | Bind `0.0.0.0` to share on the local network (prints a QR code). LAN URLs/QR prefer gateway-routed (reachable) addresses; virtual host-only adapters (Hyper-V/WSL/Docker) are skipped from the banner unless none have a gateway. Use `--host` to pin a specific (incl. host-only) address. |
| `--local` | | Force loopback-only binding (overrides `--lan` / `--host`). |
| `--host` | `ADDR` | Explicit bind address. A non-loopback address exposes the server. |
| `--port` | `N` | Listen port (default `8080`). |
| `--https` | | Enable TLS with an in-memory self-signed certificate. |
| `--upload` | | (serve) Enable the POST upload receiver. |
| `--upload-dir` | `DIR` | (serve) Upload target directory (default `./uploads`). |
| `--status` | `CODE` | (inspect) HTTP status to respond with (default `200`). |
| `--capture` | `N` | (CI) Exit after capturing N requests. |
| `--exit-on` | `EXPR` | (CI) Exit when a request matches: `path=/x`, `method=POST`, or `body~text` (`body~` is inspect-only). |
| `--timeout` | `DUR` | (CI) Fail (exit 1) if the stop condition is not met within DUR (e.g. `30s`, `5m`). |
| `--json` | | Emit machine-readable JSON: a JSONL request-record stream (inspect/pipe) or per-request access-log lines (serve), to stdout. |
| `--describe` | | Emit structured JSON metadata for AI discoverability. |
| `--help`, `-h` | | Show help and exit. |
| `--version`, `-v` | | Show version and exit. |
| `--color WHEN` | | `auto`, `always`, or `never`. Respects `NO_COLOR`. |
| `--no-color` | | Equivalent to `--color never`. |

## JSON output

Pass `--json` for machine-readable output on **stdout**.

In `inspect` (and `pipe`) mode, each captured request is written as a JSONL line (one JSON object per line). The request record has these keys (camelCase):

| Key | Description |
|---|---|
| `method` | HTTP method (`GET`, `POST`, …). |
| `path` | Request path. |
| `query` | Raw query string. |
| `headers` | Object of request headers (keys preserved verbatim). |
| `body` | Request body as text, or null. Truncated to 1 MiB when oversized (see `bodyTruncated`). In `pipe` mode the record carries no body (it streams to the child). |
| `timestamp` | Capture time, UTC ISO 8601. |
| `remote` | Remote address of the caller. |
| `bodyTruncated` | `true` when the body exceeded the 1 MiB cap and was truncated in the record. Absent/false otherwise. |

In `serve` mode, `--json` emits a per-request **access-log** line to stdout instead — one JSON object per request with `method`, `path`, and `status` (the final HTTP status). Without `--json`, every mode prints a terse human-readable request log to **stderr** (serve: `GET /file 200`; inspect/pipe: `METHOD /path`). The bind banner and all errors also go to **stderr**, so `--json` stdout stays clean for piping.

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success — clean shutdown (Ctrl+C), or a CI stop condition (`--capture` / `--exit-on`) was met. |
| 1 | A CI stop condition was **not** met before `--timeout` elapsed. |
| 125 | Usage error — unknown subcommand/flag, bad `--exit-on` key, non-integer `--port` / `--status` / `--capture`, or `pipe` with no command. Stderr carries the message. |
| 126 | Startup failure — the port could not be bound (e.g. already in use) or the self-signed certificate could not be created. Stderr carries the message. |

## Colour

`hcat` colours its banner and request-log lines when stderr is a terminal. The `--color` and `--no-color` flags control this explicitly, and `NO_COLOR` is respected (no-color.org).

## Known limitations

- **Single-command pipe only.** `pipe` runs one command for every request; there is no per-path routing in v1.
- **Self-signed cert → trust warning.** `--https` uses an in-memory self-signed certificate, so clients show a trust warning. It is for dev / LAN use, not public HTTPS.
- **No auth or IP allowlist in v1.** `--lan` exposes the server to everyone on your network; use trusted networks only.
- **~12 MB binary.** The ASP.NET Core / Kestrel dependency makes the binary larger than the other Winix tools (the price of a real HTTP server).
- **Interrupted uploads can leave a partial file (F11).** An upload aborted mid-stream may leave a partial file at the target name — there is no atomic write-then-rename in v1.
- **Concurrent pipe stderr may interleave (F12).** When several pipe-mode requests run concurrently, their children's *stderr* can interleave on the server console. This is diagnostic output only — it never enters any HTTP response.
- **Oversized bodies are truncated.** Request bodies larger than 1 MiB are truncated in the inspect record and marked `bodyTruncated`.
- **Streaming pipe output commits the status early.** If the child streams stdout, the `200` status is sent before the child's exit code is known, so a non-zero exit *after* output has flushed cannot be downgraded to `500` (an inherent streaming-CGI constraint). `pipe` `--json` records carry no body (it streams to the child).

## Related Tools

- [`nc`](../nc/README.md) — the raw-TCP sibling. `hcat` is `nc` for HTTP.
- [`qr`](../qr/README.md) — the QR encoder `hcat --lan` uses to print the LAN URL.

## See Also

- `man hcat` (after `winix install man`)
- `hcat --describe` for JSON metadata
