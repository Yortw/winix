# online — design

**Date:** 2026-06-11
**Status:** Approved (brainstorm complete; pending implementation plan)
**Tool:** `online` — package `Winix.Online`, class lib `src/Winix.Online`, console app `src/online`
**Tagline:** *block until the internet — or an endpoint — is actually healthy*

## 1. Positioning

`online` is a blocking **readiness gate** for network health. It polls a layered check and
exits `0` the moment everything is healthy, or a timeout code if it gives up. It runs **no child
process** (that is `retry`'s job) and does **no raw TCP port wait** (that is `nc --check`'s job).

Primary use case: an **agent or script blocks on `online` instead of burning its own poll loop.**
On a flaky network, a long-running agent (e.g. Claude Code) otherwise either polls wastefully —
spending turns/tokens — or gives up after a few tries and has to be re-prompted later. `online`
moves the wait *out* of the expensive agent loop and *into* a cheap native binary that blocks
until things are genuinely healthy, then returns exit `0` so the caller resumes exactly once:

```
online --internet --url https://api.anthropic.com/... && resume-work
```

The irreducible capability — the reason it earns a slot — is the layered, **captive-portal-aware
"is the internet *actually* up"** check, which no existing wait-for tool has. `--url` (wait for a
named endpoint to return a healthy status, treating 5xx/429 as "keep waiting") is a *bundled
convenience*, not the headline — that capability already exists elsewhere (see Landscape).

### Landscape (why it earns a place)

| Tool | Layered "internet up" (captive-portal-aware)? | Named-URL health-wait (5xx = keep waiting)? | Runtime / positioning |
|---|---|---|---|
| `wait-for-it.sh` | ❌ TCP host:port only | ❌ no HTTP at all | bash snippet; *nix; TCP only |
| `wait-on` (npm) | ❌ named resources only | ✅ waits for 2xx (configurable) | **needs Node.js**; also does file/tcp/socket, stability window, reverse |
| `dockerize -wait` | ❌ named resources only | ⚠️ http wait, status handling undocumented | Go binary, **Docker-entrypoint positioned**; tcp/http/file/unix |
| `nc --check` (Winix) | ❌ raw TCP port (L4) | ❌ socket-accept only | own suite; L4 only |
| `retry -- nc --check` (Winix) | ❌ | ❌ socket-accept only | composition; L4 only |

**The capability every competitor lacks:** a concept of *general connectivity*. All of them require
you to name a specific resource; none answer "do I have working internet at all," and none detect a
captive portal (a 200-with-HTML masquerading as success). That is the gap `online --internet` fills.

**Honest scoping of the rest:** the `--url` health-wait is **not novel** — `wait-on` does it (if you
have Node). `online` includes it because it lets the exact target use case — *"network back **and** my
own server is healthy"* — be expressed in **one self-contained call** with no Node runtime, and
because the wait engine is already there. The native single-AOT-binary, no-runtime, cross-platform
(incl. Windows-native), already-in-the-suite properties are **force multipliers on the internet-health
gap** — they stack on top of a real capability gap; they are explicitly *not* the justification on
their own (that argument would license cloning every capable Node tool into the suite). See ADR D1/D3.

### Name

`online` — chosen over the alternatives because it is the most *intuitive* name for the headline
case (not merely the prettiest), it is collision-free on every shell, and it reads naturally as a
gate (`online && deploy`). The generalisation-friendly name `wait-for` was considered and rejected
*because* we deliberately ruled out file generalisation (ADR D3); the door it props open leads to
already-occupied land, so its only justification evaporated. Other rejects: `await` (generic — you
await *any* task, same weakness as `ready`; also overloaded), `wait`/`waitfor` (collision-blocked:
`wait` is a bash/zsh builtin → shadowed on `PATH`; `waitfor` collides with Windows `waitfor.exe`),
`ready`/`reachable` (generic / long). See ADR D2.

## 2. CLI surface

```
online [--internet] [--url URL]... [--endpoint URL]... \
       [--status SPEC] [--timeout DURATION] [--interval DURATION] \
       [--probe-timeout DURATION] [--once] [-v|--verbose] [--json]
```

Bare `online` (no check flag) defaults to `--internet` — the headline question. When multiple
checks are given they are **AND-combined**: the gate opens (exit `0`) only when *every* requested
check is healthy in the same cycle.

| Flag | Operands | Meaning |
|---|---|---|
| `--internet` | 0 | Layered connectivity check (route → DNS → connectivity endpoint returns its expected empty-204). The flagship. Default when no check flag is given. |
| `--url URL` | 1, repeatable | Wait until URL returns a status matching `--status` (default `2xx`). Connection failure, timeout, **5xx, 429**, and any non-matching status all mean *not ready, keep waiting*. |
| `--endpoint URL` | 1, repeatable | Override the built-in connectivity-endpoint list used by `--internet`. Replacements must be `generate_204`-style (return 204 No Content); for arbitrary-status checks use `--url` instead. |
| `--status SPEC` | 1 | Expected-status spec for `--url`. Default `2xx`. Accepts `2xx`, an explicit list (`200,204`), or a range (`200-299`). |
| `--timeout DURATION` | 1 | Total wait budget. Default **10m**. `0` = wait forever. On expiry → exit `124`. |
| `--interval DURATION` | 1 | Sleep between poll cycles. Default **2s** (fixed; backoff deferred). |
| `--probe-timeout DURATION` | 1 | Per-probe (DNS / HTTP) timeout. Default **3s** — this is what makes a dead cycle quick. |
| `--once` | 0 | Run exactly one cycle and exit with the resulting status; no waiting. The cheap "is it up *right now*" mode an agent can poll. |
| `-v`, `--verbose` | 0 | Per-attempt diagnostics to stderr: which rung failed, actual HTTP status, elapsed. |
| standard | — | `--help`, `--version`, `--json`, `--describe`, `--color`, `--no-color`. |

Durations are parsed with ShellKit's `DurationParser` (precedent: `timeit`, `retry`, `schedule`).

Examples:
```
online                                       # wait (≤10m) for working internet
online --once                                # is the internet up right now? (exit 0/1)
online --internet --url https://api/health   # network back AND my server healthy
online --url https://x --status 200,204      # wait for an exact status set
```

## 3. Semantics & data flow

### `--internet` — layered, short-circuits on the first failing rung (cheap negatives)

1. **Route present** — `NetworkInterface.GetIsNetworkAvailable()` used as a *fast negative only*.
   `false` ⇒ definitely offline → sleep, **zero external traffic**. `true` ⇒ continue (it lies about
   virtual adapters — learned in hcat `--lan` — so `true` ≠ online). This is why the common outage
   (wifi dropped, cable out) is detected with no network requests at all.
2. **DNS resolves** — `Dns.GetHostAddressesAsync` for the chosen endpoint's host.
3. **Connectivity endpoint returns its expected response** — HTTP GET to a built-in
   `generate_204`-style endpoint; **204 No Content with an empty body ⇒ online.** A `200`-with-body,
   a redirect, or any other response ⇒ **captive portal / not online** (this is what makes the check
   honest rather than fooled by a portal's login page). Endpoints are tried in **randomised order,
   first expected-204 wins** (short-circuit) — so the healthy case is **one request**, and the order
   spreads load across providers over repeated polling (good-citizen; ADR D6).

> Default endpoint list: neutral 204-style endpoints (e.g. Google `gstatic` `generate_204`,
> Cloudflare `cp.cloudflare.com` `generate_204`). **Exact URLs and their expected 204 responses are
> to be pinned and tested at implementation** — do not hard-code unverified URLs. Apple/Microsoft
> NCSI endpoints return 200-with-body, *not* 204, so they are excluded from the uniform "expect 204"
> default list (a per-endpoint expected-response table is deferred — ADR Deferred).

### `--url` — named-endpoint health-wait

HTTP **GET** the URL (not HEAD — health endpoints commonly need GET; HEAD deferred). **Ready iff the
status matches `--status`** (default `2xx`). Connection failure, timeout, **5xx, 429**, and any
non-matching status ⇒ *not ready, keep waiting*. Deliberate trade-off: a permanently-wrong URL (e.g.
a stable `404`) also keeps waiting until timeout rather than fast-failing — predictable, and `-v`
prints the real status each attempt so it is diagnosable. Fast-fail on non-429 4xx is deferred
(ADR D7).

### The wait loop

```
deadline = now + --timeout            (--timeout 0 ⇒ no deadline)
loop:
    if every requested check passes:  exit 0
    if --once:                        exit (all passed ? 0 : 1)   # single cycle, no sleep
    if deadline reached:              exit 124
    sleep --interval
    retry
```

Fixed interval (backoff deferred — the layered check already fails fast). The loop and each check
run under the process cancellation token (Ctrl-C), so a wait is always interruptible.

## 4. Exit codes (diagnosable, conventional)

| Code | Meaning |
|---|---|
| `0` | Became ready — every requested check healthy. ✅ |
| `1` | `--once` only: checked once, **not ready right now.** A normal negative result, distinct from a timeout. |
| `124` | Wait mode: **timed out** before ready (matches GNU `timeout(1)`). |
| `125` | Usage error — bad args, unparseable duration/status spec, malformed URL. (ShellKit convention.) |

`1` vs `124` are deliberately distinct: `1` means "I asked once and it's down" (`--once`), `124`
means "I waited the whole budget and gave up." A caller can tell a single-probe miss from an
exhausted wait without parsing stderr.

## 5. Architecture (library/app split per Winix convention)

Standalone tool: class library `Winix.Online` + thin console app `src/online` (parse, validate,
build options, run, set exit code).

Components:
- **`OnlineOptions`** — parsed, validated config: which checks, the `--url` targets, `--status`
  spec, endpoint overrides, durations, `--once`, `--verbose`.
- **`IReadinessCheck`** — `Task<CheckResult> RunAsync(CancellationToken)`. Implementations:
  - **`InternetCheck`** — the layered rung sequence. Takes **injected seams**: a route-available
    `Func<bool>`, a DNS-resolve `Func`, and an HTTP-probe `Func` → unit-testable with **no real
    network**, and the randomised endpoint order is fed an injectable ordering so tests are
    deterministic (no `Math.random`/`Random` in the test path).
  - **`UrlCheck`** — GET + status-match. Takes an **injected HTTP-probe** seam.
- **`WaitEngine`** — the poll loop, with an **injected delay/clock seam** (precedent: `RetryRunner`)
  so the loop is tested without real waiting. AND-combines the checks; owns the deadline, `--once`,
  and exit-code mapping.
- **`Formatting`** — human summary (stderr) and `--json` envelope (stdout).
- **`Cli.RunAsync`** seam — the established library-seam pattern (happy-path *and* seam-failure
  tests; Program.cs diffed against `qr`/`digest` before ship, per suite rule).

Console app `src/online` — thin `Program.Main`: UTF-8 console encoding set up front (Windows
round-trip rule), ShellKit `CommandLineParser` (`StandardFlags`, `--describe`, `Maturity.Fresh`),
validation, build `OnlineOptions`, call `Cli.RunAsync`, set exit code.

Implementation primitives: `HttpClient` over `SocketsHttpHandler` (AOT-compatible), shared/reused
across probes; `Dns.GetHostAddressesAsync`; `NetworkInterface.GetIsNetworkAvailable()`. Reuse from
ShellKit: `DurationParser`, `ConsoleEnv`, `AnsiColor`, `JsonHelper`, `ExitCode`, `SafeError`.

**Output routing:** `online`'s stdout carries the tool's **own** result (not a passthrough child or
remote stream), so per the suite convention for own-data tools (`treex`/`whoholds`) the **`--json`
envelope goes to stdout** and the human summary goes to **stderr**. Quiet on success (exit `0` plus a
one-line stderr summary); `-v` adds per-attempt stderr lines. `NO_COLOR` respected.

JSON envelope (shape; fields pinned at implementation):
```json
{ "ready": true, "timed_out": false, "elapsed_ms": 1234, "attempts": 3,
  "checks": [ {"kind":"internet","ok":true,"detail":"204 via <endpoint>"},
              {"kind":"url","target":"https://api/health","ok":true,"detail":"200"} ] }
```

## 6. Testing

- **`WaitEngine`** — fake clock + fake checks: ready-first-cycle → 0; ready-after-N cycles (assert
  the delay seam was invoked exactly N−1 times — a **counting fake**, per the suite's "assert
  invocation count" rule); deadline exceeded → 124; `--once` ready → 0 and not-ready → 1; **AND
  invariant** — one check failing keeps the gate closed even when the other passes (a negative case,
  per "test the requirement, not the mechanism").
- **`InternetCheck`** — injected probes: route `false` short-circuits with **zero DNS/HTTP calls**
  (assert via counting fakes — the invariant that no traffic flows when offline); DNS-fail → not
  online; **captive portal** (200 + body) → not online; 204 → online; randomised-order + short-circuit
  (first success stops — assert the remaining endpoints are **not** probed, via counting fake).
- **`UrlCheck`** — 2xx → ready; **503 → not ready**; **429 → not ready**; connection failure → not
  ready; custom `--status` match/non-match.
- **`Cli`** seam-failure tests — a seam that throws is mapped through `SafeError`, no framework stack
  trace or SR resource key leaks to user output (test csproj mirrors `UseSystemResourceKeys`).
- **Integration (platform-gated `SkippableFact` + `Skip.IfNot`)** — real DNS + real HTTP GET to a
  live 204 endpoint for the genuine wire path. Per the protocol-fake caution in `CLAUDE.md`, the
  fakes verify *shape* only; **real connectivity is the ship gate**, explicitly noted.
- **Manual smokes (cmd / pwsh / bash)** — `online --once` while online (→0) and with the link pulled
  (→1); `online --url` against a real 200 and a deliberately-503 endpoint; a short-`--timeout` wait
  against an unreachable target → 124. `run-smokes.sh` fixture + `manual-smoke.yml` entry.

## 7. Deferred / out of scope (v1)

| Topic | Why deferred |
|---|---|
| File predicates (exists / settled / min-size) | Covered well by `wait-on`; rebuilding adds no value (ADR D3). |
| Windows file-lock-release waiter | Genuine Windows gap (mandatory locking) but a **file** concern and asymmetric cross-platform → its own tool, its own brainstorm. |
| Raw TCP host:port wait | That's `nc --check`; re-skinning it is explicitly out (ADR D3). |
| Gate + run `-- command` | `online && cmd` composes; child exec is `retry`'s domain (ADR D4). |
| Backoff between cycles | Fixed interval; the layered check fails fast. Add on demand. |
| HEAD instead of GET for `--url` | GET default (health endpoints want GET); HEAD option later. |
| Fast-fail on non-429 4xx for `--url` | v1 keeps waiting until timeout; `-v` shows the status (ADR D7). |
| ICMP ping rung | ICMP is frequently blocked on corporate networks; HTTP-204 is the better, portal-aware signal. |
| Per-endpoint expected-response table (Apple/MS NCSI 200-body style) | Default list restricted to 204-style for a uniform rule; arbitrary-status = use `--url`. |
| Wait-until-wall-clock-time / fixed timespan | timespan = `sleep`; until-a-time is adjacent to `schedule`/`when`. |
