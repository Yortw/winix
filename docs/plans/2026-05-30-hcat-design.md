# hcat — Design

**Date:** 2026-05-30
**Status:** Approved (brainstorm)
**Tool:** `hcat` (package `Winix.HCat`) — the last tool in the v0.4.0 batch.
**Companion ADR:** [`2026-05-30-hcat-adr.md`](2026-05-30-hcat-adr.md)

## 1. Positioning

**"netcat for HTTP."** Listen on a port and inspect requests, route paths to a command, or serve files — one binary, no signup, no daemon. Pretty output for humans, `--json` for scripts and CI. The pitch leads with **record / inspect / pipe**; static serving + uploads + HTTPS + LAN-QR are baseline conveniences ("you also get this"), not the headline. Audience framing: "anyone who needs HTTP to behave like a CLI primitive," the same playbook as `nc`.

This design assumes the prior scoping in project memory (positioning, four-bucket cost model, name decision) and does not re-derive it.

## 2. Command surface (v1)

`hcat` is a subcommand tool dispatching on `positional[0]` (precedent: `schedule`, `url`, `qr`), with a **default action** when no subcommand is given.

| Invocation | Behaviour |
|---|---|
| `hcat` | Serve the current directory on `127.0.0.1` (local preview). |
| `hcat serve [dir]` | Static file server + automatic directory listing. |
| `hcat inspect` | Receive and display incoming requests; echo each back as JSON by default. |
| `hcat pipe -- <cmd> [args]` | Run `<cmd>` once per request (CGI-style). |

**Disambiguation rule:** if `positional[0]` is a known subcommand verb (`serve`/`inspect`/`pipe`), dispatch to it; otherwise it is a **usage error** with a hint (`unknown subcommand 'foo'; did you mean 'hcat serve foo'?`). A specific directory is served via the explicit verb (`hcat serve ./public`), never by `hcat ./public`. This avoids the verb-vs-path ambiguity (a directory named `inspect` would otherwise silently dispatch to a mode).

### Global flags

- `--port <n>` — listen port (default `8080`).
- `--lan` — bind `0.0.0.0` (expose to the LAN) and print a QR + LAN URL(s) on bind. **The only way to expose to the network.**
- `--host <addr>` — bind a specific address (e.g. one NIC). Implies network exposure if non-loopback.
- `--local` — explicit alias for the default loopback bind.
- `--https` — enable TLS with a self-signed certificate generated fresh in-memory per run.
- `--json` — emit structured JSONL on stdout instead of human output (see Output routing): the request-record line for `inspect`; a per-request access-log line (method/path/status) for `serve` and `pipe`.
- Standard ShellKit flags: `--help`, `--version`, `--describe`, `--color`, `--no-color`.

### Bind / safety model (the central safety invariant)

**hcat is localhost-only unless you pass `--lan` (or a non-loopback `--host`).** Default bind is `127.0.0.1` for **every** invocation — bare `hcat` and explicit subcommands alike. LAN exposure is always a deliberate, typed choice; the tool never silently exposes the network. The asymmetry is the safety mechanism: only the dangerous option must be explicit; the safe option is the default.

On bind, a banner always prints the actual URL(s):
- loopback: `serving ./ on http://127.0.0.1:8080 (localhost only — pass --lan to share on your LAN)`
- `--lan`: `serving ./ to your LAN → http://192.168.1.42:8080` plus a terminal QR (via `Winix.QrCode`). The QR is shown only when a phone can actually reach the address (i.e. `--lan` or non-loopback `--host`); a loopback QR is never shown.

## 3. Modes

### serve

Static file server rooted at `[dir]` (default cwd) via `UseStaticFiles` + `UseDirectoryBrowser` (automatic listing for directories without an index).

- `--upload` (opt-in, off by default) enables a POST receiver that saves uploaded files into the served directory. **Path safety:** the uploaded filename is reduced to its base name (no directory components), `..` and absolute paths are rejected, and the resolved target must remain under the served root (canonicalise + prefix check). On name collision, a numeric suffix is appended (never overwrite). Uploading-to-disk is a sharper edge than read-only serving, hence opt-in.

### inspect

Receives any request (any method, any path) and:
- **Responds** `200` by default with a JSON body **echoing the request** (method, path, query, headers, body, timestamp, remote address) — httpbin `/anything`-style. The sender gets a 2xx *and* sees exactly what it sent, so inspect is both a webhook sink and an inspector.
- `--status <code>` makes it respond with a chosen status instead (to test a client's error handling).
- **Records** the same request-record schema to the `--json` JSONL stream, so `hcat inspect --json > capture.jsonl` is the recording mechanism (no separate format). The echoed response body and the captured JSONL line share one schema.

### pipe

`hcat pipe -- <cmd> [args]` runs `<cmd>` **once per request** (CGI-style single-command primitive; routing tables are deferred):

- request body → command **stdin**
- command **stdout** → response body
- request metadata → **environment variables** (CGI/1.1 convention): `REQUEST_METHOD`, `PATH_INFO`, `QUERY_STRING`, `CONTENT_TYPE`, `CONTENT_LENGTH`, `REMOTE_ADDR`, `SERVER_PROTOCOL`, and `HTTP_<HEADER>` (uppercased, `-`→`_`) for request headers.
- exit code → status: `0` → `200`, non-zero → `500`.
- command **stderr** → hcat's console (for debugging) — never leaked into the HTTP response.

The child process is spawned via `ProcessStartInfo.ArgumentList` (mandatory; never string concatenation).

### CI / determinism flags (work across `inspect` and `pipe`)

The same core, different mode — interactive vs CI is flags, not separate tools:
- `--capture <N>` — exit after N requests have been handled.
- `--exit-on <expr>` — exit when a request matches `<expr>` (match against method/path/header/body; exact predicate grammar defined at implementation, kept simple — e.g. `path=/done` / substring on body).
- `--timeout <dur>` — if the stop condition (`--capture`/`--exit-on`) is not met within the duration, exit non-zero (see Exit codes).

With `--json`, this makes hcat a deterministic CI fixture: capture a webhook, assert on the JSONL, fail the build if it never arrived.

## 4. Architecture

**Layout** (suite convention — logic in the class library, thin console app):
- `src/Winix.HCat/` — class library: server construction, middleware/handlers, request-record formatting, arg parsing, options model.
- `src/hcat/` — thin console app: ShellKit parse → build `HCatOptions` → run the server. UTF-8 console + ANSI setup, return exit code.
- `tests/Winix.HCat.Tests/` — xUnit.

**Server stack:** ASP.NET Core hosted via `WebApplication.CreateSlimBuilder` + **Kestrel**, with request handling at the **middleware / `HttpContext`** level — **not** the Minimal API endpoint-binding layer, and **not** MVC. The handlers are catch-all terminal middleware reading the raw `HttpContext.Request` (full control of streams, headers, status), so Minimal-API endpoint/binding limitations do not apply. This is the least-constraining ASP.NET Core option: all deferred v2 features (auth, IP allowlist, fault injection, pipe routing tables, reverse proxy) are additional middleware. AOT feasibility verified by spike (see ADR): `CreateSlimBuilder` + Kestrel + `UseStaticFiles` + `UseDirectoryBrowser` + source-gen JSON publishes with **zero AOT/trim warnings**, ~9.8 MB win-x64 binary, and runs.

**Key pieces:**
- **Arg parsing:** ShellKit `CommandLineParser`, subcommand dispatch on `positional[0]`, bare → serve, per-subcommand sub-parsers (mandatory; precedent `schedule`/`url`/`qr`).
- **JSON:** source-gen `JsonSerializerContext` for the request-record JSONL/echo (structured, nested headers, AOT-proven). `--describe` uses ShellKit's emitter for suite consistency.
- **QR:** `Winix.QrCode` unicode renderer on `--lan`.
- **HTTPS:** self-signed `X509Certificate2` via `CertificateRequest`, in-memory, passed to Kestrel `UseHttps`. (Confirm AOT-clean at implementation — low risk, BCL crypto.)
- **Pipe exec:** `ProcessStartInfo.ArgumentList`, one child per request, streamed stdin/stdout.

**Output routing:** banner, human output, and diagnostics → **stderr**; the request-capture JSONL (the tool's data) → **stdout**. Keeps stdout clean for piping.

**Exit codes:**
- `0` — success: clean interactive shutdown (Ctrl+C), or a CI stop condition (`--capture`/`--exit-on`) satisfied.
- `1` — CI condition **not** met: `--timeout` expired before the stop condition (grep-style "the expected thing didn't happen").
- `125` — usage error (unknown subcommand, bad flag).
- `126` — bind / backend failure (port in use, cert generation failure).

**Lifecycle:** runs until Ctrl+C (interactive) or until a CI stop condition; graceful shutdown.

## 5. Testing strategy

hcat is *more* testable than the platform-native tools — a localhost HTTP server behaves identically on every OS, so integration tests need **no root, no tmpfs, no native APIs** and run fully on CI across Windows/Linux/macOS.

- **Unit (library):** subcommand dispatch + verb/path disambiguation; bind/host resolution (loopback-default invariant); request-record JSON shape; CGI env-var mapping; upload path-safety (traversal rejection, collision suffixing); CI predicate logic (`--capture`/`--exit-on`/`--timeout`).
- **Integration (real Kestrel on an ephemeral loopback port, real `HttpClient`):** serve returns a file + a directory listing; inspect echoes + writes a JSONL line; pipe runs a portable command and maps stdin/stdout/exit→status; `--upload` saves safely and rejects `../`; `--capture N` exits after N; `--https` performs TLS with the generated cert.
- **Cross-platform wrinkle:** the pipe-mode child command differs by OS — use a portable test command or a tiny cross-platform echo helper.

## 6. v1 scope

**In:** the four invocations above; loopback-default + `--lan`/`--host` exposure with QR; `serve` static + listing + opt-in `--upload`; `inspect` echo + `--status` + JSONL record; `pipe` single-command CGI; `--https` self-signed; CI flags `--capture`/`--exit-on`/`--timeout`; `--json`; `--describe`; full docs + suite wiring.

**Deferred to v2 (non-goals for v1):** pipe path-routing tables; finer pipe status control (command-set status); `--cert <file>` bring-your-own cert; latency/fault injection; basic auth; IP allowlist; mDNS advertise; reverse proxy (YARP — verify AOT first); request replay; HAR / `.http` export formats; markdown rendering; Windows temporary firewall rule.

## 7. Open items to confirm at implementation

- Self-signed cert generation (`CertificateRequest` → Kestrel `UseHttps`) AOT-clean — spike during implementation; low risk.
- `--exit-on <expr>` predicate grammar — keep minimal (path/header/body substring); finalise at implementation.
- Portable pipe-mode test command across Windows/Linux/macOS.
- Binary size with the full feature set (~10 MB baseline from the spike; re-check after HTTPS/QR added).
