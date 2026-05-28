# mksecret — Secret / Password / Passphrase Generator

**Date:** 2026-05-28
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)
**Related:** [mksecret ADR](2026-05-28-mksecret-adr.md)

---

## Overview

`mksecret` is a cross-platform CLI for **generating** random secrets. A single AOT-compiled
binary with three subcommand modes that share one CSPRNG-bounded core:

- `mksecret password` — random-character passwords (human-facing).
- `mksecret phrase` — diceware passphrases from the EFF long wordlist (human-facing, memorable).
- `mksecret key` — high-entropy machine secrets (API keys, OAuth client secrets, HMAC keys),
  rendered as hex / base64 / base64url / base32.

It is a *generator*, not a store. It deliberately sits alongside — not on top of — the suite's
secret-*storage* tools (`envvault`, `protect`, `Winix.SecretStore`); see ADR §2 for why the name
avoids the word "secret-store" territory.

**Why it's needed (verified landscape — see Sources):**

- **Windows has no friendly secure generator out of the box.** Windows ships **Windows PowerShell
  5.1**, where the obvious idiom `Get-Random` is backed by `System.Random` — a non-cryptographic
  PRNG that Microsoft documents as unsuitable for security-sensitive use. The secure paths are
  either legacy-only (`[System.Web.Security.Membership]::GeneratePassword()` exists *only* in
  Windows PowerShell 5.1 — `System.Web` was never ported to .NET / PowerShell 7) or hand-rolled
  multi-line `RandomNumberGenerator` + encoding. PowerShell **7.4+** added `Get-SecureRandom`
  (CSPRNG-backed), but (a) it is a *primitive* — you still hand-roll character selection and
  encoding — and (b) it is not present in default Windows. **This is the primary gap mksecret fills.**
- **`pwgen` (Unix) is insecure-by-default and context-sensitive.** Its default mode produces
  *pronounceable* passwords the man page explicitly says "should not be used in places where the
  password could be attacked via an off-line brute-force attack." Security requires the `-s` flag —
  and pwgen *additionally* downgrades to weaker output when stdout is not a TTY (the scripted / CI
  case) unless `-s` was passed. It also has no first-class Windows build (it's a Unix tool).
- **Diceware is missing on every mainstream platform.** No OS ships a passphrase generator, and the
  alternatives (`diceware`, `xkcdpass`) carry a **Python runtime** dependency — fine on a dev box,
  but a heavy thing to drag onto a minimal CI image or a stock Windows machine.
- **Cross-platform uniformity.** One binary, identical flags + output on Windows / macOS / Linux,
  with no Python or PowerShell runtime. Install scripts and CI matrices stop branching per-OS
  (`openssl` here, PowerShell there, `pwgen` if-present elsewhere).

**Honest non-gaps (positioning, mirroring the `url` tool's framing):**

- On macOS/Linux, `openssl rand -base64 32` already covers `key` mode for anyone who knows the
  incantation (macOS ships LibreSSL; Linux usually has OpenSSL). Our edge there is *uniformity*,
  base32 / unpadded options, and composition — convenience, not a hard gap.
- A Linux box with `pwgen -s` already covers `password` mode (modulo the secure-flag footgun).

So the pitch leads with the genuine gaps — **Windows, secure-by-default, diceware everywhere,
CI/script uniformity** — and is upfront that on *nix the `key` mode is "you also get this
consistently," not "this didn't exist."

**Primary use cases:**

- Generate a password: `mksecret password` → 20-char alphanumeric on stdout, `≈ 119 bits` on stderr.
- Generate a passphrase: `mksecret phrase` → `correct-horse-battery-staple-anchor-medal`.
- Generate an API key: `mksecret key --bytes 32` → unpadded base64url, 256 bits.
- Generate a signing key, **store it**, then sign with it later — the key must persist or the HMAC
  is unverifiable: `mksecret key --bytes 32 > signing.key` (then `digest --hmac sha256 --key-file
  signing.key "payload"` signs/verifies against the stored key).
- Copy without scrollback (composition, no `--copy` flag): `mksecret password | clip`.
- Batch for a fixture: `mksecret password --count 5`.

**Platform:** Cross-platform (Windows, Linux, macOS). No platform-specific code — the CSPRNG
(`System.Security.Cryptography.RandomNumberGenerator` via `Winix.Codec.ISecureRandom`) and all
encoders are platform-neutral.

---

## Project Structure

```
src/Winix.MkSecret/                — class library
  SecretMode.cs                    — enum: Password, Phrase, Key
  Charset.cs                       — enum + char-table: Alphanumeric, Full, Alpha, Digits, Safe
  KeyEncoding.cs                   — enum: Hex, Base64, Base64Url, Base32
  MkSecretOptions.cs               — parsed options record (mode + per-mode fields)
  ISecretGenerator.cs              — interface: string Generate()
  PasswordGenerator.cs             — random chars via rejection-sampling mask (NanoID pattern)
  PhraseGenerator.cs               — word selection via the same mask against the EFF list
  KeyGenerator.cs                  — RNG bytes -> Hex/Base64/Base32Crockford
  EffWordList.cs                   — embedded EFF long list (7776 words) as static readonly string[]
  Entropy.cs                       — bits = f(mode, params); pure, unit-tested
  Formatting.cs                    — plain output, entropy note, JSON envelope shaping
  ArgParser.cs                     — ShellKit CommandLineParser; positional[0] subcommand dispatch
  Cli.cs                           — Run(args, stdout, stderr, randomOverride?) seam (mirrors Winix.Ids.Cli)

src/mksecret/                      — thin console app
  Program.cs                       — argv -> Cli.Run -> exit code (no top-level statements)
  mksecret.csproj                  — net10.0, PublishAot=true, PackAsTool, PackageId=Winix.MkSecret
  README.md
  man/man1/mksecret.1              — groff
  CHANGELOG.md                     — "- Initial release." (first stable tag)

tests/Winix.MkSecret.Tests/        — xUnit (InvariantGlobalization=true on the test csproj)
```

`Winix.Codec` is **reused unchanged** — `ISecureRandom`/`SecureRandom`, `Hex.Encode`,
`Base64.Encode`, `Base32Crockford.Encode` already exist and cover every encoding need.

---

## Modes

### `mksecret password`

| Flag | Default | Notes |
|------|---------|-------|
| `--length N` | 20 | Output character count. |
| `--charset NAME` | `alphanumeric` | `alphanumeric`(62), `full`(94 printable ASCII incl. symbols), `alpha`(52), `digits`(10), `safe`(alphanumeric minus visually-ambiguous `l 1 I O 0 o`). |
| `--count N` | 1 | Emit N passwords, one per line. |

Pure random — every character is an independent CSPRNG draw (ADR §5). Entropy ≈ `length ×
log₂(charset size)`: 20 alphanumeric ≈ **119.1 bits**; 20 `full` ≈ **131.1 bits**; 20 `safe` (56
chars) ≈ **116.1 bits**.

### `mksecret phrase`

| Flag | Default | Notes |
|------|---------|-------|
| `--words N` | 6 | Number of words. |
| `--sep STR` | `-` | Separator between words. |
| `--capitalize` | off | Capitalise the first letter of each word. |
| `--number` | off | Append a random digit to the passphrase. |
| `--count N` | 1 | Emit N passphrases, one per line. |

EFF **long** wordlist (7776 words, ≈ 12.925 bits/word) embedded as a `static readonly string[]`
(ADR §4, ADR §6). 6 words ≈ **77.5 bits** (before `--number`). Words selected by the same
rejection-sampling mask as `password`, against index range `[0, 7776)`.

### `mksecret key`

| Flag | Default | Notes |
|------|---------|-------|
| `--bytes N` | 32 | Raw entropy bytes drawn from the CSPRNG (32 = 256-bit). |
| `--encoding NAME` | `base64url` | `hex`, `base64` (padded), `base64url` (**unpadded** — ADR §7), `base32` (Crockford, no padding, ambiguity-free). |
| `--count N` | 1 | Emit N keys, one per line. |

Entropy = `bytes × 8` exactly (encoding does not change entropy). Wraps the existing `Winix.Codec`
encoders; `base64url` strips `=` padding after `Base64.Encode(bytes, urlSafe: true)`.

---

## Cross-cutting

**Output routing** (suite convention — secret-bearing stream is the tool's own data → stdout):

- The secret(s) → **stdout**, one per line.
- An entropy note `mksecret: ≈ 119 bits` → **stderr**, suppressed by `--quiet`. (stderr so it
  never pollutes a pipe; `mksecret password | clip` still shows the note.)
- `--json` → stdout: `{ "mode": "password", "bits": 119.1, "values": ["..."] }`. Entropy note is
  not separately emitted to stderr under `--json` (it's in the envelope).

**No `--copy` flag** (ADR §3). `mksecret <mode> | clip` already copies without putting the secret
in terminal scrollback, and keeps the stderr entropy note. The README/man document that pattern
plus the clipboard caveats (history, cross-device sync, any-process readability, no auto-clear).

**Security:**

- CSPRNG only — `Winix.Codec.ISecureRandom` (→ `RandomNumberGenerator`). `System.Random` /
  `Get-Random`-equivalents are never used. Unbiased selection via rejection sampling against a
  power-of-two mask (the reviewed `NanoidGenerator` routine), so no modulo bias.
- Secrets are never written to stderr or any log path; the entropy note contains only the bit count.
- Clipboard auto-clear is **out of scope for v1** — documented in `docs/known-issues.md`.

**Architecture / test seam:** `Cli.Run(string[] args, TextWriter stdout, TextWriter stderr,
ISecureRandom? randomOverride = null)` mirrors `Winix.Ids.Cli.Run` — ShellKit parse, `IsHandled`
for `--help/--version/--describe`, `ExitCode` conventions, `IOException` pipe-close → silent
success, catch-all → short message (AOT has `StackTraceSupport=false`). The `randomOverride`
parameter lets tests inject a deterministic `ISecureRandom` and pin exact output bytes.

**`--describe`** advertises `.ComposesWith()` snippets — but only **logically sound** ones. The
valid pipe is `mksecret <mode> | clip` (capture a generated secret somewhere you keep it). The
`digest` relationship is **not** a pipe: a generated HMAC/signing key must be *persisted* to be
verifiable, so it's expressed as "generate → store → `digest --key-file`/`--key-env`", never
`mksecret key | digest --key-stdin` (that consumes and discards the key in the same pipe, leaving an
unverifiable MAC). Per `feedback_composes_with_snippets_must_be_verified`, snippets are **executed
against the real `clip` / `digest` parsers** as a review gate — and additionally checked for logical
meaning, not merely that they run.

---

## Testing strategy

- **Deterministic RNG fake** (`ISecureRandom` returning a fixed/scripted byte stream) → pin exact
  password/phrase/key output, byte-for-byte. This is the core correctness guard.
- **Charset correctness** — every named charset contains exactly the expected members; `safe`
  excludes `l 1 I O 0 o`.
- **Rejection-sampling distribution (unbiasedness — our code)** — feed the fake bytes that land
  outside the alphabet/word-index range and assert they're *rejected*, not folded via modulo. This
  deterministically proves the mask eliminates bias without needing real randomness.
- **Real-CSPRNG liveness / no-stub guard (the production seam actually works)** — using the
  *production* `ISecureRandom` (no override), generate many values and assert they are essentially
  all distinct (catches a constant/seeded/stuck production RNG), and for `password` that the full
  charset appears across a large sample (catches "emits only a subset"). Non-flaky — collision /
  missing-char probabilities are astronomically small (e.g. a 32-byte key colliding ≈ 2⁻²⁵⁶). Also
  assert the generator factory's default `ISecureRandom` *is* `SecureRandom`, so a test stub can
  never silently become the production default. This is the inverse of the seam-failure-test rule
  (`feedback_ship_readiness_seam_failure_tests`): fakes-everywhere can hide whether the real seam
  works at all, which for an RNG is the scariest regression.
- **Wordlist integrity** — `EffWordList` has exactly 7776 entries, no duplicates, no whitespace,
  matches a checksum of the canonical EFF file.
- **Entropy math** — `Entropy` returns the documented bit values for representative params.
- **Key encodings** — round-trip each encoding (`Decode(Encode(bytes)) == bytes`); `base64url`
  output has no `=`.
- **Arg parsing** — subcommand dispatch, invalid mode/charset/encoding → `ExitCode.UsageError`;
  `--count 0`/negative, `--length 0`, `--bytes 0` handled explicitly.
- **`Cli.Run` seam** — `--json` shape, pipe-close (`IOException`) → success, catch-all error path
  emits a short message and no stack trace.
- Test csproj sets `InvariantGlobalization=true` so framework exception messages don't leak SR keys
  (per `feedback_invariant_globalization_resource_keys`).

**Explicit trust boundary (what we deliberately do NOT test):** the *cryptographic quality* —
unpredictability — of the underlying random bytes is not re-tested. No finite output sample can
prove a stream is CSPRNG-grade, and the bytes come from `RandomNumberGenerator` (the OS CSPRNG:
BCryptGenRandom on Windows, getrandom/urandom on Linux/macOS), which is not our code. We test that
our *reduction* is unbiased (deterministic) and that the production path is *wired to* the real
CSPRNG and varies (liveness); we delegate cryptographic strength to the platform. See ADR §8.

---

## Out of scope (v1)

- `--copy` / clipboard auto-clear (compose with `clip`; revisit only with a proper TTL design).
- Password strength estimation beyond raw entropy bits (no zxcvbn-style dictionary scoring).
- Structured-record secrets (vCard/TOTP-seed/etc).
- Reading a custom wordlist from a file (`--wordlist PATH`) — embedded EFF long only for v1.

---

## New-tool checklist (becomes explicit steps in the implementation plan)

scoop manifest `bucket/mksecret.json`; `.github/workflows/release.yml` (publish + pack + per-tool
zip + combined-zip + tool-map entry, in the symbol-splitting loop added on this branch);
`.github/workflows/post-publish.yml` (`update_manifest` + `generate_manifests` with winget tags);
`src/mksecret/README.md`; `src/mksecret/man/man1/mksecret.1`; `docs/ai/mksecret.md`; `llms.txt`
entry; `CLAUDE.md` (project layout + NuGet IDs list + scoop manifests list); csproj `<Description>`
+ `<PackageTags>` (baseline + `password;passphrase;diceware;secret;token;crypto`);
`src/mksecret/CHANGELOG.md` (`- Initial release.`).

---

## Verification (ship gate)

- `mksecret password|phrase|key` produce output of the expected shape on all three platforms.
- AOT binary builds clean (no trim warnings) and is in the ~1–1.5 MB range of sibling tools.
- `--version` prints clean `X.Y.Z` (the branch's `+gitsha` forward-guard applies).
- `--describe` composes-with snippets execute successfully against `digest` and `clip`.
- Manual CLI smoke per `feedback_cli_auto_defaults` (new tool — first-pass validation required).

---

## Sources (landscape verification, 2026-05-28)

- [pwgen(1) man page](https://linux.die.net/man/1/pwgen) — default pronounceable/insecure, `-s` for
  security, non-TTY downgrade.
- [Porting System.Web.Security.Membership.GeneratePassword() to PowerShell — Microsoft DevBlogs](https://devblogs.microsoft.com/powershell-community/porting-system-web-security-membership-generatepassword-to-powershell/)
  and [PowerShell/PowerShell #5352](https://github.com/PowerShell/PowerShell/issues/5352) —
  `System.Web` absent in .NET / PowerShell 7.
- [Get-Random — Microsoft Learn](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/get-random)
  — backed by `System.Random`, not cryptographically secure.
- [Get-SecureRandom — Microsoft Learn](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/get-securerandom)
  — CSPRNG primitive, PowerShell 7.4+ only.
