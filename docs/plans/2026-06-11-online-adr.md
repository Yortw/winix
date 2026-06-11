# online — Architecture Decision Record

**Date:** 2026-06-11
**Status:** Accepted
**Context:** Adding `online`, a blocking network-readiness gate, as a new Winix tool. Companion to
the design doc: `2026-06-11-online-design.md`. Motivated by a concrete agent use case: a flaky
work network drops mid-task, and a long-running agent (Claude Code) either polls wastefully or
gives up and must be re-prompted. `online` lets the agent block on a cheap native binary until the
network — and optionally its own servers — are genuinely healthy.

---

## D1 — Build it at all (value vs `wait-on` / `wait-for-it.sh` / `dockerize` / `nc + retry`)

- **Context:** "Wait for a resource" is partly served: `wait-for-it.sh` (TCP host:port, bash),
  `wait-on` (file/http/tcp/socket + stability window + reverse, needs Node), `dockerize -wait`
  (tcp/http/file/unix, Docker-entrypoint positioned), and our own `retry -- nc --check` (L4 TCP).
- **Decision:** Build it, **narrowly** — identity is *network health above L4, as a blocking gate*.
- **Rationale:** Every competitor requires you to **name a specific resource**; none has any concept
  of *general connectivity*, and none detects a **captive portal** (a 200-with-HTML masquerading as
  success). The layered, portal-aware `--internet` check is genuinely uncovered. On top of that gap,
  the native single-AOT-binary (no Node runtime), cross-platform-incl-Windows, already-in-the-suite,
  reachable-without-a-Docker/npm-mental-model properties stack as multipliers.
- **Trade-offs accepted:** The `--url` health-wait is **not** novel — `wait-on` already waits for 2xx
  on a named URL and keeps waiting through 5xx (an earlier framing of "503 = keep waiting" as our
  unique differentiator was **wrong and is retracted**). We include `--url` anyway as a *bundled
  convenience* so the target use case (*network back AND my own server healthy*) is one self-contained
  call with no Node — not as the headline.
- **Options considered:** (a) Don't build; recommend `retry -- nc --check` — rejected: that's L4
  only, no DNS/HTTP/portal awareness, and can't express "is the internet up." (b) Don't build;
  recommend `wait-on` — rejected: needs Node, and still has no internet/portal concept. (c) Build a
  general waiter — rejected, see D3.

## D2 — Name: `online`

- **Context:** Need a collision-free, intuitive name for a blocking network-readiness gate.
- **Decision:** Ship as `online`.
- **Rationale:** Most *intuitive* name for the headline case (not just the prettiest), collision-free
  on every shell, reads as a gate (`online && deploy`).
- **Trade-offs accepted:** Slightly understates the bundled `--url` health-wait (`online --url …`
  reads as "is this online", which is close enough). A network-only identity forecloses file
  generalisation under this name — accepted deliberately (D3).
- **Options considered:** `wait-for` (generalisation-friendly, dodges the `wait`/`waitfor`
  collisions) — rejected *because* file generalisation is ruled out (D3), so its only justification
  (keeping that door open) evaporated; `await` (generic — you await any task; overloaded keyword
  connotation); `wait` / `waitfor` (**collision-blocked** — `wait` is a bash/zsh builtin and is
  shadowed on `PATH`; `waitfor` collides with Windows `waitfor.exe`); `ready` (generic);
  `reachable` (literal but long, less punchy than the suite).

## D3 — Scope: network-only; no file generalisation (value-gradient boundary)

- **Context:** The wait engine is predicate-agnostic, so it *could* generalise to files (exists,
  settled, min-size) and time (timespan, until-a-time). Tempting to name it `wait-for` and grow it.
- **Decision:** **Network checks only.** No file or time predicates. Keep the name and identity sharp.
- **Rationale:** "One thing well" / SRP is satisfiable at many altitudes; the right altitude is the
  highest one where you are still *differentiated at every sub-capability*. One rung up ("any gate")
  immediately includes file-waiting and generic-HTTP — capabilities `wait-on`/`dockerize` already
  cover for the general CLI user. So the **value gradient**, not the English phrasing, sets the
  boundary: generalising into already-occupied land adds surface without value. Timespan = `sleep`;
  until-a-time is adjacent to `schedule`/`when`.
- **Trade-offs accepted:** If a compelling non-network wait need arises it becomes a *separate* tool,
  not a predicate bolted on here. The internal `IReadinessCheck`/`WaitEngine` split is kept purely
  for testability/hygiene — explicitly **not** as a generalisation runway.
- **Options considered:** (a) `wait-for` with network v1 + files v2 — rejected: the v2 land is
  occupied; the broad name buys nothing. (b) `wait-for` with network + files in v1 — rejected: larger
  surface, TOCTOU/file-access edge cases, all to rebuild `wait-on`. (c) A Windows file-lock-release
  waiter — **parked as a legitimate separate future tool** (mandatory-locking is a real Windows gap
  `wait-on`'s stability window does not cover), to be brainstormed on its own merits.

## D4 — Pure gate; no `-- command` execution

- **Context:** wait-for-it.sh style tools optionally exec a command once the target is ready.
- **Decision:** Pure gate — exit `0` when ready; the caller chains (`online && cmd`). No child exec.
- **Rationale:** `online && cmd` composes with everything and keeps `online` a primitive with no
  child-process/exit-code-passthrough surface. Running a command after a condition is `retry`'s
  domain; duplicating it here overlaps an existing tool.
- **Trade-offs accepted:** Callers needing exec write `online && cmd` rather than `online -- cmd`.
- **Options considered:** Gate + optional `-- cmd` (wait-for-it parity) — rejected as scope/overlap;
  exec-only — rejected (not a gate).

## D5 — `--internet` is a layered check; `GetIsNetworkAvailable()` is a negative-only signal

- **Context:** "Is the internet up" is not a single probe — link, naming, and reachability are
  distinct, and a captive portal returns 200 to fool naive checks.
- **Decision:** Layer it: (1) `NetworkInterface.GetIsNetworkAvailable()` as a **fast negative only**,
  (2) DNS resolves, (3) HTTP GET to a `generate_204` endpoint expecting **status `204`**;
  short-circuit on the first failing rung.
- **Update (post-adversarial-review, 2026-06-11):** the rung-3 rule is **status `204` alone**, not
  "204 + empty body". Review findings F3/F9 showed the body requirement adds a per-cycle full-body
  read and a false-negative risk (an intermediary injecting a byte into an empty 204) for **zero**
  extra portal protection — a captive portal must return 200/302, never 204, so the status is the
  whole discriminator. The probe uses `ResponseHeadersRead` and never buffers the body; redirects are
  not followed (`AllowAutoRedirect=false`) so a portal's `302` is visible rather than chased to a 200.
- **Rationale:** The cheap OS rung detects the common outage with **zero external traffic**; the 204
  rung is **captive-portal-aware** (a portal's 200-with-HTML fails it). `GetIsNetworkAvailable()`
  returns `true` for virtual adapters (learned in hcat `--lan`), so `true` is untrustworthy and only
  `false` is acted on. Layering also yields per-rung diagnosability (which rung failed) — the
  suite's diagnosability value.
- **Trade-offs accepted:** The 204 rung depends on an external endpoint (inherent — you cannot prove
  "internet up" without reaching out); mitigated by D6.
- **Options considered:** ICMP ping (frequently blocked on corporate networks → false negatives;
  rejected); DNS-only (misses captive portals); single HTTP probe with no OS pre-check (spends a
  request on the common no-link case).

## D6 — Multi-endpoint default list, randomised order, short-circuit on first success

- **Context:** The 204 rung must talk to *some* external host; hammering one provider every few
  seconds during an outage is a bad citizen, and one provider may be blocked on a given network.
- **Decision:** Ship a small list of neutral 204-style endpoints; try them in **randomised order**,
  **first expected-204 wins** (short-circuit). Override the whole list with `--endpoint` (repeatable).
- **Rationale:** Healthy case = **one request**; randomised order spreads load across providers over
  repeated polling; robust to one provider being down/blocked. Combined with the D5 OS pre-check, the
  dead-link case makes **zero** requests and the healthy case makes **one** — politeness and speed
  fall out of the layering rather than trading against each other.
- **Trade-offs accepted:** Randomness needs an injectable ordering seam for deterministic tests
  (no `Random` in the test path). A provider consistently blocked on a corporate network may still be
  tried first some cycles (then the next is tried) — acceptable; `--endpoint` lets the user pin a
  known-good host.
- **Options considered:** **Parallel-all-endpoints every cycle** — rejected: hammers all providers
  under repeated polling and is *slower* in the common case (the single short-circuit wins). Single
  fixed endpoint — rejected: a single point of failure if blocked.

## D7 — `--url`: 5xx/429/connect-fail = keep waiting; ready iff `--status` (default 2xx)

- **Context:** "Is my server healthy" must treat transient server errors as *not ready*, not as a
  reply that ends the wait.
- **Decision:** Ready iff the HTTP status matches `--status` (default `2xx`). Connection failure,
  timeout, **5xx, 429**, and any non-matching status ⇒ keep waiting.
- **Rationale:** An agent does not want "the server replied"; it wants "the server is healthy enough
  that retrying real work will succeed." Treating 503/429 as not-ready is the whole point of waiting
  on a *health* endpoint.
- **Trade-offs accepted:** A permanently-wrong URL (stable 404) also keeps waiting until timeout
  rather than fast-failing — predictable, and `-v` prints the actual status each attempt so it is
  diagnosable.
- **Options considered:** Treat any HTTP response as success (defeats the health intent); fast-fail on
  non-429 4xx now — **deferred** (adds a "definitively wrong vs transient" classification; revisit if
  the wait-on-404 behaviour annoys in practice).

## D8 — Default `--timeout` 10m; `0` = infinite; exit `124` on timeout; `1` for `--once` not-ready

- **Context:** A gate must bound its wait so a misconfigured call cannot hang a CI job forever, while
  still surviving a real work-network outage (which runs ~5–15 min).
- **Decision:** Default total `--timeout` **10m**; `0` = wait forever; override with `--timeout`. On
  expiry exit **124** (GNU `timeout(1)` convention). `--once` (single cycle, no wait) exits **1** when
  not ready right now — distinct from the wait-mode timeout.
- **Rationale:** 10m covers real outages without the classic wait-for-it footgun of an infinite
  default hanging a misconfigured pipeline. `124` is the conventional, greppable "timed out" code; the
  separate `1` lets a caller distinguish "asked once, it's down" from "waited the budget and gave up"
  without parsing stderr.
- **Trade-offs accepted:** Two negative codes (`1`, `124`) to document and test.
- **Options considered:** 5m default (may give up before a longer outage clears — the exact case to
  survive); 15m; infinite-by-default (the footgun); collapsing `--once` not-ready into `124` (loses
  the single-probe-vs-exhausted-wait distinction).

## D9 — `--json` to stdout, human summary to stderr (own-data tool)

- **Context:** Suite convention splits on whether stdout carries the tool's own data or a passthrough
  stream.
- **Decision:** `online`'s stdout carries its **own** result, so `--json` → **stdout**, human summary
  → **stderr**; quiet on success.
- **Rationale:** Matches the own-data tools (`treex`/`whoholds`) and keeps a piped `--json` consumer
  clean while the exit code remains the primary signal for the agent case.
- **Trade-offs accepted:** Differs from the passthrough tools (`wargs`/`peep`/`nc`/`demux`) that put
  `--json` on stderr — correct, because `online` has no passthrough stream to protect.
- **Options considered:** Everything on stderr (would force a `--json` consumer to read stderr —
  inconsistent with own-data tools).

## D10 — Pluggable `IReadinessCheck` + seam-injected probes, for testability not generalisation

- **Context:** Checks hit the real network; the wait loop really sleeps. Both must be unit-testable.
- **Decision:** `IReadinessCheck` with `InternetCheck`/`UrlCheck`; inject route/DNS/HTTP probe seams
  and an ordering seam into the checks, and a delay/clock seam into `WaitEngine` (precedent
  `RetryRunner`). `Cli.RunAsync` library seam with happy-path *and* seam-failure tests.
- **Rationale:** No real network or wall-clock waiting in unit tests; counting fakes pin the
  invariants (no traffic when offline; no extra sleeps when ready; short-circuit stops probing).
  Real connectivity is covered by platform-gated integration tests as the ship gate.
- **Trade-offs accepted:** The pluggable shape *looks* like a generalisation runway; it is explicitly
  not one (D3) — it exists for testability/hygiene only.
- **Options considered:** Hard-coded probes (untestable without the network); a shared waiter library
  now (speculative — no second consumer).

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| File predicates (exists / settled / min-size) | Covered by `wait-on`; rebuilding adds no value (D3). |
| Windows file-lock-release waiter | Real Windows gap, but a file concern and cross-platform-asymmetric → separate tool + brainstorm (D3). |
| Raw TCP host:port wait | That's `nc --check`; re-skinning is out (D1/D3). |
| Gate + run `-- command` | `online && cmd` composes; exec is `retry`'s domain (D4). |
| Backoff between cycles | Fixed interval; layered check fails fast. Add on demand. |
| HEAD vs GET for `--url` | GET default (health endpoints want GET); HEAD option later (D7 design). |
| Fast-fail on non-429 4xx for `--url` | v1 keeps waiting until timeout; `-v` shows status (D7). |
| ICMP ping rung | Frequently blocked corporately; HTTP-204 is the portal-aware signal (D5). |
| Per-endpoint expected-response table (Apple/MS NCSI 200-body) | Default list restricted to 204-style for a uniform rule; arbitrary status = `--url` (D5/D6). |
| Wait-until-wall-clock-time / fixed timespan | timespan = `sleep`; until-a-time adjacent to `schedule`/`when` (D3). |
| Sub-cycle timeout precision (F6) | Deadline checked between cycles ⇒ overshoot up to one cycle's probe time; acceptable, documented in README. |
| IPv4/IPv6 family-specific probing (F8) | DNS rung accepts any family; single-family mismatch falls through correctly at the HTTP rung; per-family probing not worth the surface for v1. |
| Empty-body requirement on the 204 rung (F3/F9) | Dropped — status 204 alone is the portal discriminator; requiring an empty body adds buffering + false-negative risk for no gain (see D5 update). |
