# ADR: mkauth Tool Design Decisions

**Date:** 2026-06-03
**Status:** Proposed
**Context:** Design for `mkauth`, an HTTP `Authorization` header calculator for the Winix suite. It
arose from a brainstorm about filling curl's auth gaps (no OAuth 1.0a, no token-acquisition flow).
The brainstorm produced **two** sibling tools; this ADR covers the first (the pure calculator). The
second (an OAuth2 flow runner) gets its own spec.
**Related:** [mkauth design doc](2026-06-03-mkauth-design.md)

---

## 1. Two Tools: Pure Calculator (`mkauth`) vs. OAuth2 Flow Runner (separate, future)

**Context:** curl is missing two distinct things — (a) OAuth 1.0a request *signing*, and (b) any
OAuth2 token-*acquisition* flow (client-credentials etc.). The first is a pure, stateless transform;
the second does a network round-trip and, to beat `curl + jq`, needs to cache the token and track
expiry/refresh. Options: one tool covering both; two tools split along the seam.

**Decision:** **Two tools.** `mkauth` is a pure, stateless, no-network **header calculator**. The
OAuth2 flow runner is a **separate tool with its own brainstorm/spec**, built later.

**Rationale:** Each tool keeps one honest invariant — the same discipline `digest` ("deterministic
transform, no side effects") and `envvault` ("talks to the OS keychain, has state") already model.
Cramming a network round-trip + token cache into `mkauth` would give it two personalities and break
the pipe contexts where the pure version shines (CI assertions, `--describe`-driven agent use, `curl
-H "$(…)"`). Splitting also lets `mkauth` ship soon (bounded crypto you can finish) without being
gated by the runner's genuinely-open design questions (which grants? cache location? refresh policy?).

**Trade-offs Accepted:** Two binaries/manifests/READMEs instead of one. Acceptable — the packaging
overhead is mechanical and the contract clarity is worth it.

**Options Considered:**
- **One tool, subcommands for both.** Rejected — the stateless/stateful mix is the exact thing the
  suite separates elsewhere; it would make the calculator un-pipe-able-reasoning-about and entangle
  release timing.

---

## 2. Name: `mkauth`

**Context:** The tool computes an `Authorization` header value. Candidates: `mkauth`, `httpauth`,
`authh`, `signauth`, `authsig`.

**Decision:** `mkauth`. NuGet `Winix.MkAuth`, binary `mkauth`, `bucket/mkauth.json`, `src/mkauth/`.

**Rationale:** The `mk-` prefix signals **generation/computation** (it *makes* an auth header),
parallels the just-shipped `mksecret`, and reads instantly. No shell-builtin collision; `auth` alone
is too generic and `httpauth` is longer without being clearer.

**Trade-offs Accepted:** Introduces a second `mk-` tool, lightly establishing a naming sub-family
(`mksecret`, `mkauth`) — fine; it's a coherent "make X" group.

**Options Considered:**
- **`httpauth`** — clear but longer; the `http` prefix slightly implies a client, which it isn't.
- **`authh` / `authsig` / `signauth`** — terser but cryptic; `mkauth` telegraphs intent better.

---

## 3. HMAC Request Signing: Presets Only Where Composition Breaks; Otherwise Compose with `digest`

**Context:** "HMAC request signing" sounds like one capability but is a *category* of
mutually-incompatible specified protocols (OAuth1, AWS SigV4, Azure SharedKey, RFC 9421) plus a long
tail of bespoke webhook HMACs — and they disagree on *what bytes get signed*. Meanwhile `digest
--hmac` already performs the HMAC itself end-to-end, and `envvault` already sources keys from the
keychain. So: where does a signer add value over composition?

**Decision:** A scheme earns a built-in preset **only when its canonicalization + key-derivation is
hard enough that "pipe it to `digest`" is error-prone or impossible.** Apply that test:

- **oauth1** → preset (double-encoding + param sort + header assembly). *It's the flagship anyway.*
- **azure-storage** → preset, spike-gated (multi-line StringToSign + canonicalization; §7).
- **aws-sigv4** → **not built** — curl already has `--aws-sigv4` (Deferred).
- **rfc9421** → **not built** — niche today (Deferred).
- **body / `timestamp.body` webhook HMACs** → **not built** — `digest` already does it; document the
  recipe.

For the excluded cases, ship **documented `digest` recipes** via README, `docs/ai/`, and
`.ComposesWith()` (which `--help` now renders) rather than redundant presets.

**Rationale:** The HMAC computation adds *zero* value (it's `digest`'s), and safe key-sourcing is
also already composable (`envvault … | digest --key-stdin`). The only thing a preset can add is the
canonicalization/key-derivation/header-assembly — which is real for high-canonicalization schemes and
nil for body-only ones. Building presets that merely collapse a working pipe into one verb is the
exact convenience-wrapper the suite already declined for `mksecret --copy`. Shipping a thing called
"HMAC signing" that silently only handles OAuth1-shaped requests would be a worst-case silent-wrong
output — so we name precisely (`oauth1`, `azure-storage`) instead.

**Trade-offs Accepted:** A user who wants AWS/RFC9421/webhook HMAC from `mkauth` must use curl's flag
or a `digest` pipe. Documented; presets can be added later when a concrete target appears.

**Options Considered:**
- **Generic `hmac --scheme X` umbrella + a template mode.** Rejected for v1 — the template mode is
  where config-soup lives (caller declares components/order/separator/algo/header), hard to make
  discoverable and easy to mis-sign.
- **Convenience presets even where `digest` suffices.** Rejected — relitigates the `mksecret --copy`
  value (compose over wrap).

---

## 4. Per-Scheme Flat Subcommands (not a generic `hmac --scheme` umbrella)

**Context:** Multiple schemes; how to shape the CLI. Options: one subcommand per scheme; or a generic
signer with a `--scheme` selector.

**Decision:** **One flat subcommand per scheme** — `basic`, `bearer`, `oauth1`, `jwt`,
`azure-storage` — dispatched on `positional[0]` (the `schedule`/`url`/`qr` precedent).

**Rationale:** The schemes have genuinely different input sets (oauth1 needs consumer/token
key+secret; jwt needs alg+claims+PEM; azure needs account+x-ms headers). A shared `--scheme` flag
surface would leak — most flags would be valid only for one scheme. Flat subcommands keep each flag
set scoped and discoverable, and "add a preset later" is just "add a subcommand."

**Trade-offs Accepted:** No single umbrella verb for "sign a request." Acceptable — the schemes are
too different to share one ergonomically.

**Options Considered:**
- **`mkauth hmac --scheme oauth1|azure-storage|…`** — rejected; forces a generic flag surface that
  fits no scheme well.

---

## 5. Secret-Reference Syntax (`env:`/`file:`/`vault:`/`stdin`/`literal:`) instead of `digest`-style separate flags

**Context:** Secrets must be sourceable safely (not via argv). `digest` solved this with five
separate flags (`--key-env`, `--key-file`, `--key-stdin`, `--key`, and the safe-source set). But
`mkauth oauth1` needs **two** secrets (consumer secret + token secret), and `azure-storage`/`jwt`/
`basic` each need one of a different name.

**Decision:** A single **secret-reference string** per secret-bearing flag, with a scheme prefix:
`env:NAME`, `file:PATH`, `vault:NS/KEY`, `-`/`stdin`, `literal:VALUE`. Resolved by a shared
`SecretResolver`. `literal:` emits a `ps`/history warning to stderr (matching `digest --key`); only
one secret per invocation may use `stdin`.

**Rationale:** With up to two secrets per subcommand, the `digest` separate-flag pattern would
explode (`--consumer-secret-env`, `--consumer-secret-vault`, `--token-secret-env`, …). A ref syntax
collapses that to `--consumer-secret 'vault:api/consumer'` / `--token-secret 'env:TOK'`, scales to N
secrets, and *adds the `vault:` keychain source* — the cross-tool differentiator no competitor offers.
The prefix scheme is explicit (no guessing whether a bare value is a literal or a filename).

**Trade-offs Accepted:** A small mini-syntax to learn, and a divergence from `digest`'s flag style.
Acceptable — `digest` has exactly one key so separate flags suffice there; `mkauth` has up to two, so
the ref syntax is the right tool. Documented prominently; `--help` shows the ref table.

**Options Considered:**
- **`digest`-style separate `--*-env/--*-file/...` flags.** Rejected — flag explosion with two
  secrets.
- **Bare value with heuristic detection (looks-like-a-path → file).** Rejected — ambiguous and a
  security footgun (a literal that looks like a path gets read from disk, or vice versa).

---

## 6. Output: Full `Name: value` Header Line by Default

**Context:** The primary use is `curl -H "$(mkauth …)"`, which wants `Authorization: <value>`. But
some callers want just the value.

**Decision:** Default output is the **full `Authorization: <value>` line** to stdout. `--value-only`
prints the bare value. `--json` emits `{scheme, header_name, header_value[, base_string]}`.
`--show-base-string` adds the signature base/StringToSign (to stderr in plain mode, or into the JSON
envelope) for debugging.

**Rationale:** Optimise for the dominant `-H "$(…)"` path so the common case needs no extra flag.
`--value-only` covers clients that take a bare value; `--json` follows the suite convention for
machine/agent consumption.

**Trade-offs Accepted:** A caller wanting only the value types one flag. Fine.

**Options Considered:**
- **Bare value by default.** Rejected — the most common consumer (`curl -H`) would then need string
  concatenation (`-H "Authorization: $(…)"`), more error-prone than emitting the whole line.

---

## 7. `azure-storage` Is In v1 but Spike-Gated to Blob/Queue/File SharedKey

**Context:** The user explicitly wants to attempt Azure Storage SharedKey signing in v1, accepting
deferral if the first attempt proves too involved. Azure SharedKey canonicalization **differs by
service** (Blob/Queue/File share one StringToSign; Table uses a different one) and has a SharedKeyLite
variant.

**Decision:** Build `azure-storage` in v1, **scoped to Blob/Queue/File SharedKey**. The spike's first
task is to capture a known-good reference signature from the Azure SDK's `StorageSharedKeyCredential`
and pin it as the test fixture. **If the canonicalization balloons past estimate** (e.g. the x-ms
header handling or Content-Length-empty-vs-zero rules prove fiddly beyond a bounded effort), defer the
whole subcommand cleanly to a later version rather than ship something half-verified. Table + Lite are
out of scope regardless.

**Rationale:** It's a genuine gap (curl has no Azure SharedKey; no clean CLI exists) and fits Troy's
Azure work. Bounding to one service's canonicalization keeps "attempt Azure" from becoming an
open-ended four-service matrix, and the spike-gate honours the "defer if it fails first attempt"
instruction with an explicit exit criterion.

**Trade-offs Accepted:** v1 may ship without Azure if the spike says so. Acceptable and pre-agreed.

**Options Considered:**
- **All four services + Lite now.** Rejected — open-ended; multiplies the canonicalization surface
  and the fixture-capture work.
- **Defer Azure entirely to a later tool/version.** Rejected — the user wants the attempt now;
  the spike-gate gives a safe fallback to exactly this outcome.

---

## 8. JWT: Hand-Built Compact JWS via `System.Security.Cryptography`; AOT Claim-Serialization Spike Deferred

**Context:** JWT minting needs JSON serialization of an arbitrary claim set plus signing across
HS/RS/ES families, all AOT-compatible.

**Decision:** **Hand-build** the compact JWS (`base64url(header).base64url(payload).base64url(sig)`)
using `System.Security.Cryptography` directly (`HMACSHA*`; `RSA` PKCS1; `ECDsa` with
`DSASignatureFormat.IeeeP1363` for JOSE `r‖s`). **Do not** take a dependency on
`System.IdentityModel.Tokens.Jwt`. The **arbitrary-claim JSON serialization under AOT** is flagged as
a **build-time spike** (likely `JsonNode`/`JsonObject`, which is reflection-free) — not solved in this
design.

**Rationale:** Hand-building is small and keeps the binary AOT-clean and tiny, consistent with the
suite's "no heavy reflection-based libraries" stance (cf. `qr` avoiding `System.Drawing`). The only
genuine AOT unknown is serializing a dynamic claim dictionary; isolating it as a spike avoids baking
an unverified serialization assumption into the plan (per the "verify plan test assumptions" rule).

**Trade-offs Accepted:** We own the JWS assembly (more code than calling a library). Acceptable — it's
~small and fully testable against RFC 7515 Appendix A vectors.

**Options Considered:**
- **`System.IdentityModel.Tokens.Jwt` / `Microsoft.IdentityModel.*`.** Rejected — heavy, reflection-
  leaning, AOT-risky, and overkill for minting a signed token.

---

## 9. Protocol Correctness Is Pinned to Reference Vectors; Encoding Helpers Are Not Shared Between Test and Impl

**Context:** These are wire protocols. The project's protocol-fake rule warns that sharing encoding
helpers (percent-encoder, base64url, base-string builder) between the implementation and its tests
makes both sides compute identical bytes — so the tests pass even when the encoding is wrong vs a real
counterpart.

**Decision:** Pin **byte-for-byte against published reference vectors** (RFC 7515 App A for JWT;
Twitter/RFC 5849 example for OAuth1; an Azure-SDK-captured fixture for SharedKey), and **duplicate**
(or independently assert) encoding logic on the test side rather than importing the implementation's
helpers. Add a **real round-trip / real-counterpart smoke** as the ship gate (e.g. `curl -H "$(mkauth
oauth1 …)"` validated server-side, or a captured-request comparison), since protocol-fake tests verify
shape, not wire-correctness.

**Rationale:** Directly applies `CLAUDE.md`'s protocol-fake guidance. Reference vectors are the
"pinned wire bytes from a known-good reference implementation" option; the real-counterpart smoke is
the explicit ship gate the rule requires.

**Trade-offs Accepted:** Some duplicated encoding logic in tests, and an integration smoke that needs
a real endpoint/captured fixture. Both are the point.

**Options Considered:**
- **Shared encoding helpers + shape-only assertions.** Rejected — the exact invisible-defect trap the
  rule names.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|-------|--------------|
| OAuth2 flow runner (client-credentials → token, caching/expiry/refresh) | Separate sibling tool; stateful + network; its own brainstorm/spec (§1). |
| Token-acquisition flows generally (incl. OAuth 1.0a 3-legged dance) | `mkauth` signs, it doesn't fetch tokens; flows are the runner's domain (§1). |
| `aws-sigv4` preset | curl already ships `--aws-sigv4`; no value for the curl use case (§3). Add later for non-curl clients. |
| `rfc9421` (HTTP Message Signatures) preset | Real but niche today; add on concrete demand (§3). |
| Generic `hmac` template / webhook-body presets | Composition with `digest` covers it; document recipes (§3). |
| Azure Table canonicalization + SharedKeyLite | v1 is Blob/Queue/File SharedKey only (§7). |
| JWT PS* (RSA-PSS) and JWE (encryption) | v1 is signed JWS with HS/RS/ES; PS/JWE are additive later (design "Out of scope"). |
| `jwt decode` / token inspection | `mkauth` mints; inspection is a separate capability. |
| Managed-string secret zeroing | `string` is immutable, process is ephemeral (resolve→sign→print→exit); same rationale as `mksecret`. |
| AOT serialization of arbitrary JWT claims | Build-time spike (`JsonNode` vs source-gen) before the `jwt` subcommand lands (§8). |
