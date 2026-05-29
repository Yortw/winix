# mksecret — AI Agent Guide

## What This Tool Does

`mksecret` generates cryptographic secrets in three modes — random-character passwords, EFF diceware passphrases, and encoded high-entropy keys — as a single cross-platform native binary with no runtime dependency. All modes use the OS CSPRNG; there is no insecure fallback.

## When to Use This

- Generating a random password for a new account or credential: `mksecret`
- Generating a human-memorable passphrase (e.g. for a master password or a shared secret): `mksecret phrase`
- Generating an API key, HMAC signing key, OAuth secret, or session token: `mksecret key`
- Any context where Windows is involved — Windows ships no secure generator out of the box; `Get-Random` and `Get-SecureRandom` are non-cryptographic
- Cross-platform secret generation where `pwgen`, `openssl rand`, or Python diceware packages are not available or not trusted to be present
- Scripted secret generation that needs `--json` output for machine consumption

## When NOT to Use This

- When you need to hash or sign data — use `digest` instead; `mksecret` only generates, it does not verify
- When you need a unique identifier (UUID, ULID, NanoID) rather than a secret — use `ids`
- When your pipeline already has a trusted secret store (e.g. Azure Key Vault, HashiCorp Vault) and you want vault-managed rotation — `mksecret` generates local secrets only

## Basic Invocation

```bash
# 20-char alphanumeric password (default)
mksecret

# 32 chars with symbols
mksecret --length 32 --charset full

# Safe charset (no visually-ambiguous characters: l/1/I/O/0/o)
mksecret --charset safe

# Six-word EFF diceware passphrase
mksecret phrase

# Eight words, space-separated, title-cased, trailing digit
mksecret phrase --words 8 --sep ' ' --capitalize --number

# 32-byte key as unpadded base64url (256-bit)
mksecret key

# 64-byte key as hex
mksecret key --bytes 64 --encoding hex

# Crockford base32 (no ambiguous characters)
mksecret key --encoding base32

# Five passwords
mksecret --count 5
```

## JSON Output

Pass `--json` for a machine-parseable JSON envelope:

```bash
mksecret phrase --json
```

Output shape:
```json
{"mode": "phrase", "bits": 77.5, "values": ["cosmic-table-river-beyond-flame-dusk"]}
```

The `bits` field is the entropy estimate in bits. `values` is always an array even when `--count 1`.

## Character Sets (password mode)

| Name | Size | Notes |
|---|---|---|
| `alphanumeric` (default) | 62 | A–Za–z0–9 |
| `full` | 94 | All printable ASCII 33–126, includes symbols |
| `alpha` | 52 | Letters only |
| `digits` | 10 | Digits only |
| `safe` | 56 | Alphanumeric minus l, 1, I, O, 0, o — for spoken or typed sharing |

## Encodings (key mode)

| Name | Notes |
|---|---|
| `base64url` (default) | URL-safe base64, unpadded |
| `base64` | Standard base64 with `=` padding |
| `hex` | Lowercase hexadecimal |
| `base32` | Crockford base32 (no ambiguous characters) |

## Composability

```bash
# Copy a generated password to the clipboard
mksecret | clip

# Copy a passphrase to the clipboard
mksecret phrase | clip

# Copy a 32-byte key to the clipboard
mksecret key --bytes 32 | clip

# Generate a key, save it, then use it with digest
mksecret key --bytes 32 > signing.key
digest --hmac sha256 --key-file signing.key "payload"

# JSON output for scripted pipelines
mksecret key --json | jq -r '.values[0]'
```

## CRITICAL: Do NOT pipe a generated key into digest --key-stdin

```bash
# WRONG — the key is discarded as soon as the pipe closes
mksecret key | digest --hmac sha256 --key-stdin "payload"
```

This produces a valid-looking MAC, but the key is gone. You can never verify the MAC because you do not have the key. Always generate→store→use:

```bash
# CORRECT
mksecret key --bytes 32 > signing.key
digest --hmac sha256 --key-file signing.key "payload"
```

The `--key-stdin` flag on `digest` is for supplying a *known* key from a secure source (e.g. a vault that writes to stdout), not for receiving a freshly generated one.

## Platform Notes

`mksecret` is fully cross-platform — Windows, Linux, macOS. All modes, all flags, all exit codes are identical everywhere. No external helpers, no Python runtime, no PowerShell version dependency.

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success. A closed downstream pipe (e.g. `mksecret --count 100000 \| head -1`) also exits 0 — not an error. |
| 125 | Usage error — unknown flag, bad `--charset`/`--encoding`, non-positive or oversized limit, unexpected positional. Stderr carries the message. |
| 126 | Runtime error — OS CSPRNG failure or output write failure (disk full, device error). Stderr carries the message. |

## Metadata

Run `mksecret --describe` for full structured metadata (flags, modes, examples, exit codes).
Run `mksecret SUBCOMMAND --help` for mode-specific help.
