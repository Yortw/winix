# mkauth — HTTP Authorization Header Calculator

**Date:** 2026-06-03
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)
**Related:** [mkauth ADR](2026-06-03-mkauth-adr.md)

---

## Overview

`mkauth` is a cross-platform CLI that **computes an HTTP `Authorization` header and prints it to
stdout**. It is a *pure, stateless calculator* — no network, no token storage, no caching — designed
to feed `curl` (and any other HTTP client) by command substitution:

```bash
curl -H "$(mkauth oauth1 --method GET --url https://api.example.com/v1/thing \
                          --consumer-key abc --consumer-secret 'vault:api/consumer' \
                          --token def --token-secret 'vault:api/token')" \
     https://api.example.com/v1/thing
```

A single AOT-compiled binary with per-scheme subcommands that share one secret-resolution core:

- `mkauth basic` — `Authorization: Basic base64(user:password)` (RFC 7617).
- `mkauth bearer` — `Authorization: Bearer <token>` (RFC 6750).
- `mkauth oauth1` — **flagship**: OAuth 1.0a signed header (RFC 5849; HMAC-SHA1/HMAC-SHA256/PLAINTEXT).
- `mkauth jwt` — mint + sign a JWT and wrap it as `Authorization: Bearer <jwt>` (RFC 7519/7515).
- `mkauth azure-storage` — Azure Storage **SharedKey** header for Blob/Queue/File (*spike-gated, see
  ADR §7*).

It is the **first of two sibling tools** that came out of one brainstorm. The second — an OAuth2
*flow runner* (client-credentials → token, with caching/expiry) — is deliberately **a separate tool
with its own future spec**, because it does network I/O and holds state, which would violate this
tool's "pure calculator" contract. See ADR §1.

**Why it's needed (verified landscape — see Sources):**

- **curl has no OAuth 1.0a signing.** curl ships Basic, Digest, NTLM, Negotiate/SPNEGO, `-u`,
  `--oauth2-bearer` (which only *attaches* a token you already hold), and `--aws-sigv4`. It has **no
  OAuth 1.0a support** and **no token-acquisition flow** of any kind. OAuth 1.0a signing is the
  genuine, unfilled curl gap this tool's flagship targets.
- **OAuth 1.0a signing has almost no clean CLI.** It is fiddly in exactly the ways people get wrong:
  RFC-3986 percent-encoding (which is *not* URL form-encoding — `~` stays literal, space is `%20`
  not `+`), lexicographic parameter sorting, the *double* encoding of the joined parameter string,
  and the `oauth_*` header assembly. Today people reach for Postman or hand-rolled scripts. Legacy
  but extant: WooCommerce REST, some Atlassian on-prem, Garmin, Discogs, Tumblr.
- **JWT minting has no clean cross-platform single-binary CLI.** Service-account auth (Google
  service accounts, GitHub Apps, Apple Sign-in) requires a *freshly signed* JWT per call. The
  algorithm is small but the JOSE details (base64url-no-padding, ES raw `r‖s` signatures) are
  error-prone by hand.
- **Cross-platform uniformity + keychain integration.** One binary, identical flags/output on
  Windows/macOS/Linux, no Python/PowerShell/openssl branching — and, uniquely, every secret can be
  pulled straight from the OS keychain via `vault:` references (see "Secret references" below). No
  existing auth-header CLI reads from DPAPI/Keychain/libsecret.

**Honest non-gaps (positioning, mirroring the `mksecret`/`url` framing):**

- **Basic** and **Bearer** are trivial (base64 / passthrough). They earn their place by *rounding out
  the identity* and by letting a credential come from the keychain (`vault:`) without ever touching
  argv or shell history — not because computing them is hard.
- **AWS SigV4 is deliberately NOT included** — curl already has `--aws-sigv4`, so for the "feed a
  header to curl" use case there is nothing to add. It can become a preset later for non-curl
  clients (ADR Deferred).
- **Generic / webhook HMAC signing is intentionally out of scope** — `digest --hmac` already does the
  HMAC end-to-end, and for body / `timestamp.body` schemes the canonicalization is trivial, so
  composition wins. We document the `digest` recipe instead of shipping a redundant preset (ADR §3).

So the pitch leads with the genuine gaps — **OAuth 1.0a signing, JWT minting, keychain-sourced
credentials, cross-platform uniformity** — and is upfront that Basic/Bearer are convenience/identity,
not a hard gap.

**Platform:** Cross-platform. All schemes are platform-neutral crypto (`System.Security.Cryptography`)
plus the cross-platform `Winix.SecretStore` for `vault:` references (its per-OS backends already
abstract DPAPI / Keychain / libsecret).

---

## Project Structure

```
src/Winix.MkAuth/                  — class library
  AuthScheme.cs                    — enum: Basic, Bearer, OAuth1, Jwt, AzureStorage
  SecretRef.cs                     — parse a secret reference (env:/file:/vault:/literal:/-)
  SecretResolver.cs                — resolve a SecretRef -> bytes/string; vault via ISecretStore
  HeaderResult.cs                  — record: HeaderName, HeaderValue, BaseString? (debug)
  PercentEncoder.cs                — RFC 3986 percent-encoding (OAuth1); verified against vector
  Base64Url.cs                     — base64url-no-padding (or reuse Winix.Codec.Base64 + strip)
  BasicAuthBuilder.cs              — user:password -> Basic header
  BearerAuthBuilder.cs             — token -> Bearer header
  OAuth1Signer.cs                  — base string, signing key, HMAC/PLAINTEXT, oauth_* header
  JwtSigner.cs                     — header+payload build, HS/RS/ES sign, compact serialization
  AzureStorageSigner.cs            — SharedKey StringToSign (Blob/Queue/File), HMAC-SHA256, header
  Clock.cs                         — IClock seam (UtcNow) for deterministic timestamps in tests
  Nonce.cs                         — INonceSource seam for deterministic oauth_nonce in tests
  Formatting.cs                    — plain header line / --value-only / --json envelope
  ArgParser.cs                     — ShellKit CommandLineParser; positional[0] subcommand dispatch
  Cli.cs                           — Run(args, stdout, stderr, stdin, deps?) seam

src/mkauth/                        — thin console app
  Program.cs                       — argv -> Cli.Run -> exit code (no top-level statements)
  mkauth.csproj                    — net10.0, PublishAot, PackAsTool, PackageId=Winix.MkAuth
  README.md
  man/man1/mkauth.1                — groff
  CHANGELOG.md                     — "- Initial release." (first stable tag)

tests/Winix.MkAuth.Tests/          — xUnit (UseSystemResourceKeys=true mirror of the app csproj)
```

**Reused, unchanged:** `Winix.Codec` (`Base64`, `Hex`), `Winix.SecretStore`
(`ISecretStore` for `vault:` references), `Yort.ShellKit` (`CommandLineParser`, `SafeError`,
`ExitCode`). New project references for the app csproj: `Winix.Codec`, `Winix.SecretStore`,
`Yort.ShellKit`.

---

## Secret references (shared across all subcommands)

`mkauth` never *requires* a secret on argv. Every secret-bearing flag takes a **secret reference**
string with a scheme prefix, resolved by `SecretResolver`:

| Reference | Source | Notes |
|-----------|--------|-------|
| `env:NAME` | environment variable | safe (not in argv/history) |
| `file:PATH` | file contents (trimmed trailing newline) | safe |
| `vault:NS/KEY` | OS keychain via `Winix.SecretStore` | **the differentiator** — DPAPI / Keychain / libsecret |
| `-` or `stdin` | read from stdin | safe; only one secret per invocation may use stdin |
| `literal:VALUE` | the literal value | **emits a `ps`/shell-history warning to stderr**, like `digest --key` |

A single ref syntax (rather than `digest`'s five separate `--key-*` flags) is used because some
subcommands need **two** secrets (`oauth1`: consumer secret + token secret), and a per-source flag
explosion (`--consumer-secret-env`, `--consumer-secret-vault`, …) would be unwieldy. See ADR §5.
`stdin` is single-use; if two secrets both request `stdin`, that is a usage error. For `jwt`, stdin
may feed the key (`--key stdin`) or the claims body (`--claims-stdin`) but not both. `vault:NS/KEY`
splits on the **first** `/` (a namespace cannot contain `/`; a key may). `file:`/`stdin` values have a
single trailing `\r\n`/`\n` run trimmed; all other bytes (including trailing spaces) are preserved.

---

## Subcommands

### `mkauth basic`

| Flag | Default | Notes |
|------|---------|-------|
| `--user NAME` | *(required)* | Username. |
| `--password REF` | *(required)* | Secret reference for the password. |

Output: `Authorization: Basic <base64(user ":" password, UTF-8)>` (RFC 7617). The colon is the
delimiter; a username containing `:` is a usage error.

### `mkauth bearer`

| Flag | Default | Notes |
|------|---------|-------|
| `--token REF` | *(required)* | Secret reference for the bearer token. |

Output: `Authorization: Bearer <token>` (RFC 6750). Pure passthrough — the value is whatever the
reference resolves to, trimmed of a trailing newline.

### `mkauth oauth1` *(flagship)*

| Flag | Default | Notes |
|------|---------|-------|
| `--method VERB` | *(required)* | HTTP method, upper-cased for the base string. |
| `--url URL` | *(required)* | Full request URL; query params are parsed out and folded into the signature base. |
| `--consumer-key K` | *(required)* | `oauth_consumer_key`. |
| `--consumer-secret REF` | *(required)* | Secret reference. |
| `--token T` | — | `oauth_token` (omit for 2-legged). |
| `--token-secret REF` | empty | Secret reference; empty token secret for 2-legged. |
| `--signature-method M` | `HMAC-SHA1` | `HMAC-SHA1`, `HMAC-SHA256`, `PLAINTEXT`. |
| `--param k=v` | — | Extra/body params folded into the signature base (repeatable; for `application/x-www-form-urlencoded` bodies). |
| `--realm R` | — | `realm` (NOT part of the signature base). |
| `--timestamp N` | auto (`IClock`) | `oauth_timestamp`; overridable for reproducibility/tests. |
| `--nonce S` | auto (`INonceSource`) | `oauth_nonce`; overridable. |
| `--show-base-string` | off | Emit the computed signature base string (debug; to stderr, or in `--json`). |

Signature base = `METHOD & pct(base_url) & pct(normalized_params)`, where `base_url` is
scheme+authority+path (lowercased scheme/host, default port dropped, no query/fragment), and
`normalized_params` is the RFC-3986-percent-encoded, lexicographically-sorted, `&`-joined merge of
URL query params + `--param` params + the `oauth_*` params (excluding `realm` and `oauth_signature`).
Signing key = `pct(consumer_secret) & pct(token_secret)`. Signature = base64(HMAC-SHA1/256 over the
base string), or — for `PLAINTEXT` — the signing key itself (HTTPS only; we warn on plaintext over a
non-`https` URL). Header: `Authorization: OAuth realm="…", oauth_consumer_key="…", oauth_nonce="…",
oauth_signature="…", oauth_signature_method="…", oauth_timestamp="…", oauth_token="…",
oauth_version="1.0"` with each value percent-encoded and quoted.

**Scope note:** `mkauth` *signs* a request given credentials; it does **not** perform the 3-legged
request-token→authorize→access-token dance. Token acquisition is a *flow*, out of scope for the
calculator (ADR §1, Deferred).

### `mkauth jwt`

| Flag | Default | Notes |
|------|---------|-------|
| `--alg A` | `HS256` | `HS256/384/512`, `RS256/384/512`, `ES256/384/512`. |
| `--key REF` | *(required)* | HS: shared secret. RS/ES: PEM private key (typically `file:`/`vault:`). |
| `--claim k=v` | — | Claim (repeatable). Typed coercion rules documented (string by default; `--claim-num`/`--claim-json` for non-string). |
| `--claims-file PATH` | — | JSON object merged into the claim set. |
| `--claims-stdin` | — | Merge a JSON object read from stdin into the claim set. Mutually exclusive with `--key stdin` (stdin can feed the key or the claims, not both). |
| `--claim-num k=v` | — | Numeric claim (`v` parsed as a number so NumericDate claims serialize correctly). `--claim-json k=v` parses `v` as raw JSON. |
| `--iss / --sub / --aud S` | — | Convenience standard claims. |
| `--exp DURATION` | — | Expiry as `now + DURATION` (ShellKit `DurationParser`). |
| `--iat` | off | Set `iat` to now (`IClock`). |
| `--nbf DURATION` | — | `nbf` = `now + DURATION`. |
| `--kid S` / `--header k=v` | — | JOSE header parameters. |

Builds `base64url(header) "." base64url(payload)`, signs per `alg`
(`HMACSHA*` for HS; RSASSA-PKCS1-v1_5 for RS; ECDSA in IEEE-P1363 `r‖s` form for ES — the JOSE
requirement), appends `"." base64url(signature)`. Output: `Authorization: Bearer <jwt>` (or
`--value-only` for the bare JWT). AOT serialization of arbitrary claims is a **build-time spike**
(ADR §8) — likely `JsonNode`/`JsonObject` (reflection-free).

### `mkauth azure-storage` *(spike-gated — ADR §7)*

| Flag | Default | Notes |
|------|---------|-------|
| `--account NAME` | *(required)* | Storage account name. |
| `--key REF` | *(required)* | Account key (base64); HMAC key is `base64-decode(key)`. |
| `--method VERB` | *(required)* | HTTP method. |
| `--url URL` | *(required)* | Request URL → canonicalized resource (`/account/path` + sorted query). |
| `--x-ms-date` | auto (`IClock`, RFC1123 GMT) | The `x-ms-date` header value (must match what the client sends). |
| `--x-ms-version V` | a pinned default | `x-ms-version` header. |
| `--header k=v` | — | Additional `x-ms-*` headers (folded into CanonicalizedHeaders) or the fixed StringToSign headers (Content-Type, Content-Length, …). |
| `--show-base-string` | off | Emit the computed StringToSign (debug). |

StringToSign (Blob/Queue/File) = VERB + the 12 fixed header lines (Content-Encoding … Range) +
CanonicalizedHeaders (`x-ms-*`, lowercased, sorted, `name:value\n`) + CanonicalizedResource.
Signature = base64(HMAC-SHA256(base64-decode(key), UTF-8(StringToSign))). Header:
`Authorization: SharedKey <account>:<signature>`. **v1 targets Blob/Queue/File SharedKey only**;
Table canonicalization and SharedKeyLite are deferred.

---

## Cross-cutting

**Output routing** (suite convention — the header is the tool's own data → stdout):

- The computed header → **stdout**: full `Name: value` line by default (so `-H "$(…)"` just works);
  `--value-only` prints the bare value.
- `--json` → stdout: `{ "scheme": "oauth1", "header_name": "Authorization", "header_value": "OAuth …" }`,
  plus `"base_string": "…"` when `--show-base-string` is set.
- `--show-base-string` debug (non-JSON mode) → **stderr**, so it never pollutes the header on stdout.
- Warnings (`literal:` secret exposure, PLAINTEXT-over-non-HTTPS) → **stderr**.

**Security:**

- Secrets resolve through `SecretResolver`; the safe references (`env:`/`file:`/`vault:`/`stdin`) keep
  the secret out of argv and shell history. `literal:` is the explicit escape hatch and emits a
  stderr warning, matching `digest --key` precedent.
- Constant-time comparison is **not relevant** here (mkauth produces signatures; it never verifies a
  secret against an expected value — that's `digest --verify`'s job).
- The resolved secret material is held only as long as needed to compute the header; the process is
  short-lived (resolve → sign → print → exit). Managed-string zeroing is out of scope (same rationale
  as `mksecret` ADR — `string` is immutable, process is ephemeral).

**Architecture / test seam:** `Cli.Run(string[] args, TextWriter stdout, TextWriter stderr,
TextReader stdin, MkAuthDeps? deps = null)` where `MkAuthDeps` bundles the injectable seams —
`IClock` (timestamps/`exp`/`x-ms-date`), `INonceSource` (`oauth_nonce`), and `ISecretStore` (vault
backend). Fixing these in tests pins **exact** header bytes. Follows the `Winix.Ids`/`Winix.MkSecret`
`Cli.Run` precedent — ShellKit parse, `IsHandled` for `--help/--version/--describe`, `ExitCode`
conventions, `IOException` pipe-close → silent success, catch-all → short message via
`SafeError.Describe` (AOT `StackTraceSupport=false`).

**`--describe` / `.ComposesWith()`** advertises the curl pipe (`curl -H "$(mkauth …)"`) and — for the
deliberately-excluded HMAC cases — the `digest` recipe, e.g. `printf '%s.%s' "$ts" "$body" | digest
--hmac sha256 --key-stdin --base64` for a Stripe-style webhook signature. Per
`feedback_composes_with_snippets_must_be_verified`, every snippet is executed against the real
`curl`/`digest`/`envvault` surface (and the flag names verified against their current `--help`) as a
review gate before shipping.

---

## Testing strategy

The schemes are pure functions of (inputs, fixed clock, fixed nonce), so the core guard is
**byte-for-byte pinning against published reference vectors** — and, per the project's protocol-fake
rule, **encoding helpers are NOT shared between test and implementation** (a shared percent-encoder
or base64url would make both sides agree even when both are wrong vs a real counterpart). Vectors:

- **OAuth1** — pin against a published worked example with a known `oauth_signature` (the widely
  documented Twitter "creating a signature" example and/or RFC 5849's example request).
  **Verify the exact expected signature against the cited reference at implementation time** — do not
  trust a hand-typed value in this design.
- **JWT** — pin against **RFC 7515 Appendix A** (A.1 HS256, A.2 RS256, A.3 ES256) which publish the
  exact signing input and signature bytes. (ES is non-deterministic in general, but RFC 7515 A.3
  pins a known signature for verification; for our *signing* test, round-trip verify with the public
  key, and additionally pin HS256 — which IS deterministic — byte-for-byte.)
- **Azure SharedKey** — **capture a known-good reference** by running the Azure SDK's
  `StorageSharedKeyCredential` signer over the same request in a one-off harness, and pin those bytes
  as the fixture (the spike's first job; ADR §7). No published key+signature vector exists otherwise.
- **PercentEncoder** — assert RFC 3986 behaviour explicitly (`~` literal, space → `%20`, `!*'()`
  encoded), independent of the OAuth1 vector, so an encoder regression is caught directly.
- **Secret references** — each scheme resolves correctly; `literal:` emits the warning; two `stdin`
  refs → usage error; `vault:` uses an injected `ISecretStore` fake.
- **Arg parsing** — subcommand dispatch; missing required flags → `ExitCode.UsageError`; unknown
  `--alg`/`--signature-method` → usage error.
- **`Cli.Run` seam** — `--json` shape; pipe-close (`IOException`) → success; catch-all path emits a
  short message and no stack trace; PLAINTEXT-over-non-HTTPS warning fires.
- **Real-crypto liveness** — sign with the production `HMACSHA*`/`RSA`/`ECDsa` (no fakes for the
  crypto primitive) and verify the signature with the corresponding verifier, so the production seam
  is proven to actually sign (inverse of `feedback_ship_readiness_seam_failure_tests`).
- Test csproj sets `UseSystemResourceKeys=true` (mirroring the app csproj) so framework exception
  messages don't leak SR keys (per the suite convention + `feedback_invariant_globalization_resource_keys`).

**Explicit trust boundary:** the cryptographic strength of the signing primitives is delegated to
`System.Security.Cryptography` (not re-tested). We test that our *construction* (base strings,
canonicalization, encoding, header assembly) matches reference bytes, and that the production crypto
path is wired and round-trips.

---

## Out of scope (v1)

- **OAuth2 flow runner** — separate sibling tool, separate future spec (ADR §1).
- **Token-acquisition flows for any scheme** — including OAuth 1.0a's 3-legged dance. `mkauth` signs;
  it does not fetch tokens.
- **AWS SigV4** — curl has `--aws-sigv4`; no value for the curl use case (ADR Deferred).
- **Generic / webhook HMAC presets** — compose with `digest` (ADR §3); recipes documented.
- **RFC 9421 (HTTP Message Signatures)** — preset deferred until concrete demand (ADR Deferred).
- **Azure Table canonicalization + SharedKeyLite** — v1 is Blob/Queue/File SharedKey only (ADR §7).
- **JWT encryption (JWE)** and **RSA-PSS (PS*)** — v1 is signed JWS with HS/RS/ES; PS/JWE deferred.
- **Reading/printing an existing token's claims** (`jwt decode`) — `mkauth` mints, it doesn't inspect.

---

## New-tool checklist (becomes explicit steps in the implementation plan)

scoop manifest `bucket/mkauth.json`; `.github/workflows/release.yml` (publish + pack + per-tool zip +
combined-zip + tool-map entry, in the symbol-splitting loop); `.github/workflows/post-publish.yml`
(`update_manifest` + `generate_manifests` with winget tags); `.github/workflows/manual-smoke.yml`
(tool list + `runner_for` + sed retarget); `src/mkauth/README.md`; `src/mkauth/man/man1/mkauth.1`;
`docs/ai/mkauth.md`; `llms.txt` entry; `CLAUDE.md` (project layout + NuGet IDs list + scoop manifests
list); csproj `<Description>` + `<PackageTags>` (baseline + `oauth;oauth1;jwt;http;authorization;
curl`); `src/mkauth/CHANGELOG.md` (`- Initial release.`); native `run-smokes.sh` fixture derived from
the README option/exit-code surface.

---

## Verification (ship gate)

- `mkauth basic|bearer|oauth1|jwt` produce headers that pin to reference vectors on all three platforms;
  `azure-storage` pins to the captured SDK reference (or is cleanly deferred per the spike-gate).
- A real round-trip smoke: `curl -H "$(mkauth oauth1 …)"` against a server that validates the
  signature (or a captured-request comparison), to confirm wire-correctness beyond protocol-fake
  shape (per the protocol-fake test caveat — integration against a real counterpart is the ship gate).
- AOT binary builds clean (no trim warnings) and is in the ~1–2 MB range of sibling tools.
- `--version` prints clean `X.Y.Z` (the branch's `+gitsha` forward-guard applies).
- `--describe` composes-with snippets execute successfully against `curl`/`digest`/`envvault`.
- Manual CLI smoke per `feedback_cli_auto_defaults` (new tool — first-pass validation required).

---

## Sources (landscape verification, 2026-06-03)

- curl auth options — `curl(1)` man page (`--basic`, `--digest`, `--ntlm`, `--negotiate`,
  `--oauth2-bearer`, `--aws-sigv4`); no OAuth 1.0a, no token-acquisition flow. *(Re-verify the exact
  flag list against the installed curl version at implementation time.)*
- OAuth 1.0a — [RFC 5849](https://www.rfc-editor.org/rfc/rfc5849) (signature base string, parameter
  normalization, signing key, `Authorization: OAuth` header).
- JWT / JWS — [RFC 7519](https://www.rfc-editor.org/rfc/rfc7519),
  [RFC 7515](https://www.rfc-editor.org/rfc/rfc7515) (Appendix A worked examples: HS256/RS256/ES256).
- Basic / Bearer — [RFC 7617](https://www.rfc-editor.org/rfc/rfc7617),
  [RFC 6750](https://www.rfc-editor.org/rfc/rfc6750).
- Azure Storage SharedKey — Microsoft Learn, "Authorize with Shared Key"
  (StringToSign layout for Blob/Queue/File, CanonicalizedHeaders/Resource rules).
