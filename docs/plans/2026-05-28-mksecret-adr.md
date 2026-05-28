# ADR: mksecret Tool Design Decisions

**Date:** 2026-05-28
**Status:** Proposed
**Context:** Design for `mksecret`, a secret / password / passphrase generator for the Winix suite,
and the first new tool of the v0.4.0 batch (alongside the already-committed release-pipeline cleanup).
**Related:** [mksecret design doc](2026-05-28-mksecret-design.md)

---

## 1. One Tool, Three Modes (password / phrase / key)

**Context:** The work began as a human password generator (`passgen`). During brainstorming the
scope broadened to also cover machine secrets (API keys, OAuth client secrets, HMAC keys). Options:
one tool with three modes, three separate tools, or human-passwords-only.

**Decision:** A single tool with three subcommand modes — `password`, `phrase`, `key` — dispatched
on `positional[0]` (the `schedule`/`url`/`qr` precedent).

**Rationale:** All three are the same primitive — draw CSPRNG bytes, map to an output space without
bias — reskinned three ways. Password = bytes→chars; phrase = bytes→word-indices; key =
bytes→encoded string. The security-critical code (unbiased selection) is *one* small, already-reviewed
routine (`NanoidGenerator`'s rejection-sampling mask) reused thrice, which is exactly what a security
tool wants: one thing to get right, not three. Bundling also gives a coherent "generate a secret of
any shape" surface that composes with the suite's storage tools.

**Trade-offs Accepted:** A slightly larger flag surface than a single-purpose tool. Mitigated by
subcommand isolation — each mode's flags are scoped to its subcommand.

**Options Considered:**
- **Three separate tools** (`passgen`, `phrasegen`, `keygen`). Rejected — triples the packaging /
  manifest / docs overhead for what is one shared primitive, and fragments discovery.
- **Human-passwords only.** Rejected — the `key` mode is nearly free (the `Winix.Codec` encoders
  and CSPRNG already exist) and fills the real `openssl rand`-on-Windows gap.

---

## 2. Name: `mksecret` (not `secret`, `passgen`, or `pwgen`)

**Context:** The name must cover passwords, passphrases, *and* machine keys, and must not collide
with established tools or with the suite's own vocabulary. Candidates: `secret`, `mksecret`,
`secretgen`, `passgen`, `mkpass`, `pwgen`, `mint`.

**Decision:** `mksecret`. NuGet `Winix.MkSecret`, binary `mksecret`, `bucket/mksecret.json`,
`src/mksecret/`.

**Rationale:** The `mk-` prefix (echoing `mktemp`/`mkdir`/`mkfifo`) signals **generation**, which is
the crux: in *this* suite the word "secret" already means *storage/management* — `envvault` stores
secrets, `protect` encrypts them, and the shared backend is literally `Winix.SecretStore` (which
shells out to Linux's `secret-tool`). A bare `secret` command would lead users to expect
`get`/`set`/`list`, the opposite of what this tool does, and sits one keystroke from the real
`secret-tool` CLI. `mksecret` keeps the evocative "secret" framing while disambiguating the verb.

**Trade-offs Accepted:** Slightly less terse than a bare word; introduces an `mk-` naming style not
yet used elsewhere in the suite (acceptable — it reads instantly).

**Options Considered:**
- **`secret`** — the original instinct. Rejected for the storage-connotation clash with
  `envvault`/`protect`/`Winix.SecretStore` and proximity to `secret-tool`.
- **`passgen` / `mkpass`** — rejected once `key` mode was in scope: a password-only name undersells
  the machine-secret capability.
- **`pwgen`** — rejected; directly shadows the well-known Linux `pwgen`. We are not a drop-in
  replacement with compatible semantics (we add diceware + key encodings + secure-by-default), so
  reusing its exact name would mislead.
- **`secretgen`** — viable but longer/less Unix-terse than `mksecret`.
- **`mint`** — evocative and collision-light, but less discoverable for "password generator" and
  collides with Swift's Mint package manager.

---

## 3. No Built-in `--copy`; Compose with `clip` Instead

**Context:** A `--copy` flag (copy the secret to the clipboard) is a common convenience and was in
the original backlog scope. Should v1 implement it?

**Decision:** **No `--copy` flag in v1.** Document `mksecret <mode> | clip` instead, with explicit
clipboard-security caveats in the README/man.

**Rationale:** In a pipe, `mksecret password | clip` connects mksecret's stdout to `clip`'s stdin —
the secret never reaches the terminal, so scrollback stays clean *without* a flag, and the stderr
entropy note still shows. A built-in `--copy` would mostly re-implement a pipe while (a) taking a
new project coupling on `Winix.Clip`, (b) adding a headless-Linux failure path (no clipboard), and
(c) *implicitly blessing the clipboard as a safe sink for secrets* — which it is not: clipboard
contents can be persisted (Windows Clipboard History), synced across devices (macOS Universal
Clipboard), and read by any same-user process, and `Winix.Clip` has no auto-clear/TTL. Tools that
ship `--copy` responsibly (`pass -c`, 1Password CLI) pair it with timed auto-clear; doing that
properly is more than v1 warrants.

**Trade-offs Accepted:** Users type a pipe instead of a flag. Acceptable — it's the Unix-composable
path and avoids endorsing an unsafe sink.

**Options Considered:**
- **`--copy` with auto-clear (TTL).** The "proper" version. Deferred — background-timer +
  clear-on-exit + headless handling is its own mini-design; revisit if there's demand.
- **Plain `--copy` (copy-only, suppress stdout), no auto-clear.** Rejected — conveys an
  ephemerality it cannot deliver.

---

## 4. EFF Long Wordlist Only (7776 words)

**Context:** Diceware needs a wordlist. EFF publishes a long list (7776 words, ≈12.9 bits/word) and
a short list (1296 words, ≈10.3 bits/word, ≤5 letters each). Embed which?

**Decision:** Embed the EFF **long** list only.

**Rationale:** It is the diceware default and gives the most entropy per word, so passphrases stay
short for a target strength (6 words ≈ 77.5 bits). One list keeps the binary, tests, and docs
simple. ~60 KB embedded is negligible against a ~1 MB AOT binary.

**Trade-offs Accepted:** No shorter-word option for users who prioritise typing ease over word
count. Acceptable for v1; a `--wordlist short|long` selector can be added later without breaking
changes.

**Options Considered:**
- **Short list only** — shorter words but more of them for the same entropy; rejected as the lone
  default since long is the diceware norm.
- **Both, selectable** — most flexible but more surface to test/document; deferred.

---

## 5. Pure Random Passwords (No Forced Character-Class Composition)

**Context:** Some generators guarantee ≥1 char from each class (lower/upper/digit/symbol) so output
always satisfies strict site validators. Should `mksecret password` do that?

**Decision:** **Pure random** — every character is an independent CSPRNG draw from the selected
charset, with no post-hoc class guarantees.

**Rationale:** Forced composition slightly *reduces* entropy and adds bias-handling complexity for a
non-security benefit. Maximum entropy and trivially-auditable selection matter more for a security
tool than appeasing the occasional strict validator. A user who needs a guaranteed digit can pick a
charset or length that makes absence vanishingly unlikely.

**Trade-offs Accepted:** A short password could, rarely, lack a given class and trip a strict
"must contain a number" validator. Acceptable and documented.

**Options Considered:**
- **Guarantee one per class.** Rejected — entropy cost + bias complexity outweigh the validator
  convenience; counter to secure-by-default simplicity.

---

## 6. Reuse `Winix.Codec` + `NanoidGenerator` Pattern; Embed Wordlist as a Static Array

**Context:** The implementation needs a CSPRNG, unbiased selection, byte encoders, and an embedded
wordlist — all AOT-compatible.

**Decision:** Reuse `Winix.Codec` (`ISecureRandom`, `Hex`, `Base64`, `Base32Crockford`) **unchanged**;
copy the `NanoidGenerator` rejection-sampling-against-a-power-of-two-mask routine for char/word
selection; embed the wordlist as a `static readonly string[]` compiled into the assembly.

**Rationale:** Everything needed already exists and is reviewed. A source-embedded `string[]` is
trim/AOT-safe with no reflection or resource-manifest lookup (avoids the AOT pitfalls seen with
manifest-resource loading). Reusing the proven rejection-sampling routine means the
security-critical selection logic is identical to code already shipped in `ids`.

**Trade-offs Accepted:** The selection routine is duplicated rather than extracted to a shared lib.
Acceptable for one small loop; if a third consumer appears, extract then (rule of three).

**Options Considered:**
- **Extract a shared `RejectionSampler` into `Winix.Codec` now.** Deferred — premature for two
  consumers; revisit on a third.
- **Embed the wordlist as a `.txt` EmbeddedResource read via `Assembly.GetManifestResourceStream`.**
  Rejected — adds an AOT/trim consideration for no benefit over a compiled array.

---

## 7. `key` Mode Default Encoding: Unpadded `base64url`

**Context:** `key` mode renders random bytes; it needs a default encoding. `Base64.Encode` keeps
`=` padding even in URL-safe mode.

**Decision:** Default `--encoding base64url`, emitted **without `=` padding** (strip after
`Base64.Encode(bytes, urlSafe: true)`).

**Rationale:** URL/filename-safe and the common shape for API keys / tokens / bearer secrets;
unpadded is what most token consumers expect and avoids `=` needing escaping in URLs/env files.
`hex`, padded `base64`, and Crockford `base32` (ambiguity-free, no padding) remain available.

**Trade-offs Accepted:** A consumer that strictly requires padded base64 must pass `--encoding
base64`. Documented.

**Options Considered:**
- **Default `hex`** — universally accepted but 2× longer; rejected as the default (offered as an option).
- **Default padded `base64`** — `+`/`/`/`=` are awkward in URLs and some env-file parsers; rejected
  as the default.

---

## 8. Randomness Testing Boundary — Test the Reduction and the Wiring, Delegate Strength to the Platform

**Context:** mksecret is a security tool, so "are the generated values actually good?" is the
sharpest question. The implementation uses an injectable `ISecureRandom` so deterministic fakes can
pin exact output — but that raises the worry that the *real* generator is never exercised, and that
a bad/constant/biased real generator would pass every fake-driven test.

**Decision:** Split "randomness correctness" into three layers and test the two we own; explicitly
delegate the third:

1. **Reduction unbiasedness (ours)** — deterministic test: feed the fake out-of-range bytes and
   assert rejection (no modulo fold). Proven without real randomness.
2. **Production-seam liveness (ours)** — run the *real* `SecureRandom` (no override), assert outputs
   vary and (for `password`) cover the charset; assert the factory default is `SecureRandom`. Guards
   against a stub/seed silently becoming the production default.
3. **CSPRNG cryptographic strength (the platform's)** — **not tested.** No finite sample proves a
   stream is CSPRNG-grade, and the bytes come from `RandomNumberGenerator` (OS CSPRNG), which is not
   our code.

**Rationale:** This closes the genuinely-dangerous, genuinely-testable gap (a degraded production
seam — the inverse of `feedback_ship_readiness_seam_failure_tests`) while refusing to ship
theatrical "randomness tests" that can't prove what they claim and would be flaky. The honest
boundary is recorded so a future reviewer doesn't either (a) assume cryptographic quality is tested
or (b) add a brittle statistical suite believing there's a hole.

**Trade-offs Accepted:** We rely on the OS/BCL CSPRNG being correct. Acceptable — re-implementing or
re-validating a CSPRNG is out of scope and worse than trusting the platform primitive.

**Options Considered:**
- **A statistical suite (chi-square / NIST STS / dieharder) on real output.** Rejected — validates
  the BCL's RNG (not our code), is flaky at any tight threshold, and still can't prove
  unpredictability. The loose charset-coverage check in layer 2 catches gross failures without the
  flakiness.
- **No real-generator test at all (fakes only).** Rejected — leaves the scariest regression (a
  stubbed/seeded production default) completely unguarded.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|-------|--------------|
| `--copy` with timed clipboard auto-clear | Needs its own background-timer / clear-on-exit / headless design; v1 composes with `clip` instead (§3). |
| Short / custom (`--wordlist`) word sources | EFF long is the v1 default; selector is additive and non-breaking later (§4). |
| Password strength scoring (zxcvbn-style) | v1 reports raw entropy bits only; dictionary scoring is a separate feature. |
| Shared `RejectionSampler` extraction | Two consumers (`ids`, `mksecret`) don't justify extraction yet — rule of three (§6). |
| Structured-record secrets (TOTP seed, etc.) | Out of the generate-a-random-secret scope for v1. |
| Zeroing generated secrets from process memory | Managed `string`s are immutable and the process is short-lived (generate → print → exit); zeroing is impossible for `string` and low-value here. Note in `docs/known-issues.md`. (adversarial-review F8) |
| Encode-only key output (no decode-round-trip guarantee for non-aligned `--bytes`) | `mksecret` never decodes; the guarantee is encode-only. Note in `docs/known-issues.md`. (adversarial-review F7) |
