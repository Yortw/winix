# digest — AI Agent Guide

## What This Tool Does

`digest` computes cryptographic hashes (SHA-2, SHA-3, BLAKE2b) and HMACs (same family, keyed) over a literal string, standard input, or one-or-more files. Single cross-platform AOT native binary. Produces `sha256sum`-compatible multi-file output and also supports base64/base64url/base32/hex encodings and JSON.

## When to Use This

- Hash a file or string on any platform without worrying about whether `sha256sum`, `certutil`, or `Get-FileHash` is available: `digest file.iso`
- Compute an HMAC on Windows (no first-class HMAC CLI exists otherwise): `digest --hmac sha256 --key-env API_SECRET -s "payload"`
- Compute an HMAC from a key stored encrypted at rest: `age --decrypt key.age | digest --hmac sha256 --key-stdin -s "msg"`
- Produce base64 / base32 output for use in JWTs, URL slugs, or config files: `digest --base64 -s "abc"`
- Verify a download's integrity against a known hash in constant time: `digest --verify <hash> file.iso`
- Emit machine-parseable hash output for scripts and pipelines: `digest --json file.iso`
- Anywhere `sha256sum` / `md5sum` / `openssl dgst` would be used and you want a consistent flag surface across platforms

## When NOT to Use This

- For verifying a `sha256sum`-style checkfile (lines of "hash  filename") — use `sha256sum -c` for that. `digest --verify` is single-hash only.
- For password hashing — none of these algorithms are suitable. Use `argon2`, `bcrypt`, or `scrypt`.
- For file encryption/decryption — use `age` or `gpg`. `digest` computes one-way hashes, not reversible encryption.
- For BLAKE3 — not supported in v1 (deferred to v2).

## Basic Invocation

```bash
# SHA-256 (default) of a literal string
digest -s "hello"

# SHA-256 of a file (sha256sum-compatible line with * binary-mode marker)
digest file.iso

# SHA-512
digest --sha512 -s "hello"

# BLAKE2b-512 (often faster than SHA-2 on modern CPUs)
digest --blake2b file.iso

# SHA-3 (requires modern OS crypto backend)
digest --sha3-256 -s "hello"

# Read payload from stdin
echo -n "abc" | digest

# Multiple files — one line per file, sha256sum-compatible
digest *.txt
```

## HMAC Invocation

`digest` supports HMAC for all its hash algorithms. The `--hmac ALGO` flag implies HMAC mode and carries its own algorithm — do not also pass `--sha256` / `--algo`. The HMAC key must come from exactly one of four sources (listed from safest to least safe):

```bash
# 1. From stdin, typically piped from an external secrets manager
age --decrypt key.age | digest --hmac sha256 --key-stdin -s "payload"
pass show mykey     | digest --hmac sha256 --key-stdin -s "payload"
secret-tool lookup service myservice | digest --hmac sha256 --key-stdin -s "payload"

# 2. From an environment variable
digest --hmac sha256 --key-env API_SECRET -s "payload"

# 3. From a file (on Unix, permissions are checked; warning if group/other-readable)
digest --hmac sha256 --key-file ~/.secret file.bin

# 4. Literal argument (ALWAYS emits a non-suppressible stderr warning)
digest --hmac sha256 --key "throwaway-test-key" -s "payload"
```

**Do not use `--key` in scripts.** The literal key is visible via `ps`, shell history, and process listings.

### BLAKE2b key ceiling

BLAKE2b keyed mode caps keys at 64 bytes (RFC 7693 §2.9). If you have a longer key, use `--hmac sha256` (or another SHA-family algorithm) which auto-hashes oversized keys per RFC 2104.

### Payload-vs-key stdin conflict

You cannot use `--key-stdin` when the payload is also coming from stdin — there is only one stdin stream. `digest` detects this and exits 125.

## Output Formats

```bash
# Hex (default)
digest -s "abc"
# ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad

# Base64 (standard alphabet)
digest --base64 -s "abc"
# ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=

# Base64 URL-safe (for inclusion in URLs, JWTs)
digest --base64-url -s "abc"

# Crockford base32 (uppercase, avoids I/L/O/U)
digest --base32 -s "abc"

# Uppercase hex
digest --uppercase -s "abc"
```

## JSON Output

```bash
digest --json -s "abc"
```

Shape (single input):

```json
{
  "algorithm": "sha256",
  "format": "hex",
  "hash": "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
  "source": "string"
}
```

Shape (multi-file): JSON array of objects, each also carrying `"path": "..."`.

HMAC outputs prefix the algorithm with `hmac-`:

```json
{ "algorithm": "hmac-sha256", ... }
```

## Verify Mode

`--verify EXPECTED` compares the hash in **constant time** — timing-safe against an attacker who can measure response time:

```bash
digest --verify e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 -s ""
# exit 0 if match, 1 if mismatch
```

Hex comparison is case-insensitive; base64 and base32 comparisons are case-sensitive. Verify is single-hash only — not supported with multiple files.

## Input-Mode Rules

- Positional arguments are **always** files. If a file is missing, `digest` errors with exit 125 and suggests `--string`. There is no silent fallback to "treat the argument as a string."
- `--string VALUE` (or `-s VALUE`) is required if you want to hash a literal string.
- Multi-file mode uses **all-or-nothing validation** — if any file is missing, no hashes are emitted.
- A lone `-` positional means "read from stdin" (standard Unix convention).

## Legacy Algorithm Warnings

- `--md5` and `--sha1` emit a stderr warning before the hash. These algorithms are broken for collision resistance; `--sha1` is still acceptable as an HMAC primitive (HMAC-SHA-1 is not affected by the collision attacks), but `--md5` should be avoided for any security-relevant purpose.
- The warnings go to stderr so they don't pollute stdout used in pipes.

## Platform Notes

- Fully cross-platform — Windows, Linux, macOS.
- SHA-3 (`--sha3-256`, `--sha3-512`) requires a modern OS crypto backend: Windows 11 22H2+, recent Linux, recent macOS. On older platforms, `digest` exits 126 with a clear message.
- Unix-only: key-file permission warnings (group/other-readable files).
- Windows: ACL inspection is deferred to a future `protect`/`unprotect` tool — `digest` does not warn on Windows about ACL weaknesses.

## Composability

```bash
# Hash a file and copy it to the clipboard
digest file.bin | clip

# HMAC a request body with a key from an age-encrypted file
age --decrypt key.age | digest --hmac sha256 --key-stdin -s "$body"

# HMAC with a key from passwordstore
pass show api-secret | digest --hmac sha256 --key-stdin -s "$body"

# HMAC with a key from macOS Keychain
security find-generic-password -a myacct -s myservice -w | digest --hmac sha256 --key-stdin -s "$body"

# Verify downloaded files against a published hash
digest --verify "$EXPECTED_HASH" downloaded.iso

# Hash multiple files and feed into sha256sum verification
digest *.iso > checksums.txt
sha256sum -c checksums.txt
```

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success, or verification match. |
| 1 | Verification failed (`--verify` mismatch). |
| 125 | Usage error — bad flags, unknown value, flag conflict, missing file. |
| 126 | Runtime error — SHA-3 unavailable on this platform, file read failure. |

## Metadata

Run `digest --describe` for full structured metadata (flags, algorithms, examples, exit codes, JSON output fields).
Run `digest --help` for human-readable help.
