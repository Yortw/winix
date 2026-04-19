# digest

Cross-platform cryptographic hashing and HMAC — SHA-2, SHA-3, BLAKE2b — with safe HMAC key handling (env / file / stdin / literal). Single native binary, no runtime, one consistent flag surface across Windows, Linux, and macOS. Replaces `sha256sum` / `md5sum` / `openssl dgst` for hashing, and fills the gap left by Windows, which has no first-class HMAC CLI.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/digest
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Digest
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Digest
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
digest [options] [file ...]
digest [options] -s VALUE
digest [options]            # reads stdin
```

### Examples

```bash
# SHA-256 of a literal string
digest -s "hello"
# 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824

# SHA-256 of a file
digest file.iso
# 046e70fbe5d8e3b60d03c560579f196f6e8a9978d07afcd050ba2d0e42c2d11d *file.iso

# SHA-256 of multiple files (sha256sum-compatible output)
digest *.txt

# SHA-512
digest --sha512 -s "hello"

# BLAKE2b (single-pass, faster than SHA-2 on most CPUs)
digest --blake2b file.iso

# SHA-3 — requires Windows 11 22H2+ / recent Linux / recent macOS
digest --sha3-256 -s "hello"

# HMAC-SHA-256 with key from environment variable
digest --hmac sha256 --key-env API_SECRET -s "payload"

# HMAC of a file with key from a file (Unix permissions are checked)
digest --hmac sha256 --key-file ~/.secret file.bin

# HMAC with key piped from age-encrypted storage
age --decrypt key.age | digest --hmac sha256 --key-stdin -s "msg"

# HMAC with key piped from passwordstore
pass show mykey | digest --hmac sha256 --key-stdin -s "msg"

# Base64 output (standard alphabet)
digest --base64 -s "abc"
# ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=

# URL-safe base64 (for inclusion in URLs)
digest --base64-url -s "abc"

# Crockford base32 (uppercase, no ambiguous chars)
digest --base32 -s "abc"

# Verify a known hash in constant time (exit 0 on match, 1 on mismatch)
digest --verify e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 -s ""

# JSON output for machine parsing
digest --json -s "abc"
# {"algorithm":"sha256","format":"hex","hash":"ba7816bf…","source":"string"}

# Read payload from stdin (explicit)
echo -n "abc" | digest -

# Pipe a hash to the clipboard
digest file.bin | clip
```

## Options

| Flag | Short | Default | Description |
|---|---|---|---|
| `--sha256` | | default | SHA-256. |
| `--sha384` | | | SHA-384. |
| `--sha512` | | | SHA-512. |
| `--sha3-256` | | | SHA3-256. Requires modern OS crypto backend. |
| `--sha3-512` | | | SHA3-512. Requires modern OS crypto backend. |
| `--blake2b` | | | BLAKE2b-512. |
| `--sha1` | | | SHA-1 (legacy — emits a stderr warning). |
| `--md5` | | | MD5 (legacy — emits a stderr warning). |
| `--algo A` | `-a A` | | Alternative to individual algorithm flags: `sha256`, `sha384`, `sha512`, `sha1`, `md5`, `sha3-256`, `sha3-512`, `blake2b`. |
| `--hmac A` | | | HMAC mode using the given hash algorithm. Requires a key source (`--key-env` / `--key-file` / `--key-stdin` / `--key`). Conflicts with the plain `--sha256`/`--algo`/etc. flags. |
| `--key-env V` | | | Read HMAC key from environment variable `V`. |
| `--key-file P` | | | Read HMAC key from file `P`. Unix: emits a warning if the file is readable by group or other. |
| `--key-stdin` | | | Read HMAC key from stdin (cannot be combined with stdin payload). |
| `--key K` | | | HMAC key as a literal CLI argument. Emits a non-suppressible warning — the key is visible to `ps`, shell history, and process listings. |
| `--key-raw` | | off | Preserve bytes on `--key-file` / `--key-stdin` (skip trailing-newline strip). |
| `--hex` | | default | Hex output, lowercase. |
| `--base64` | | | Base64 output (standard alphabet). |
| `--base64-url` | | | Base64 URL-safe variant. |
| `--base32` | | | Crockford base32 output (uppercase, no ambiguous characters). |
| `--uppercase` | `-u` | off | Uppercase hex output. |
| `--string V` | `-s V` | | Hash the literal string `V` (UTF-8 bytes). Exclusive with positional file args. |
| `--verify E` | | | Compare output with expected string `E` in constant time. Exit 0 on match, 1 on mismatch. Not supported with multiple files. |
| `--json` | | off | Emit JSON output (single object, or array for multi-file). |
| `--describe` | | | Emit structured JSON metadata for AI discoverability. |
| `--help` | `-h` | | Show help and exit. |
| `--version` | `-v` | | Show version and exit. |
| `--color WHEN` | | `auto` | `auto`, `always`, or `never`. Respects `NO_COLOR`. |
| `--no-color` | | | Equivalent to `--color never`. |

## Algorithms

| Algorithm | Bit length | Family | Status |
|---|---|---|---|
| `sha256` (default) | 256 | SHA-2 | Modern, recommended. |
| `sha384` | 384 | SHA-2 | Modern. |
| `sha512` | 512 | SHA-2 | Modern. Often faster than SHA-256 on 64-bit CPUs. |
| `sha3-256` | 256 | SHA-3 / Keccak | Modern. Requires Windows 11 22H2+, recent Linux, or recent macOS. |
| `sha3-512` | 512 | SHA-3 / Keccak | Modern. Same platform caveat. |
| `blake2b` | 512 | BLAKE2 | Modern. Single-pass, typically faster than SHA-2 on modern CPUs. |
| `sha1` | 160 | SHA-1 | **Legacy** — broken for collisions. Emits stderr warning. Still acceptable as HMAC primitive. |
| `md5` | 128 | MD5 | **Legacy** — cryptographically broken. Emits stderr warning. Use only for non-security contexts (e.g. file integrity against accidental corruption). |

## HMAC Key Handling

HMAC requires a secret key; the threat model is that the key should not leak via `ps` output, shell history, core dumps, or world-readable files. `digest` supports four key sources, listed from **safest** to **least safe**:

- **`--key-stdin`** — read the key from stdin. Pair with an external secrets manager:

  ```bash
  age --decrypt key.age | digest --hmac sha256 --key-stdin -s "payload"
  pass show mykey     | digest --hmac sha256 --key-stdin -s "payload"
  secret-tool lookup svc myservice | digest --hmac sha256 --key-stdin -s "payload"
  ```

- **`--key-env VAR`** — read the key from an environment variable. Safe if the variable is set by your shell's secrets loader or a secrets manager injecting into the process environment.

- **`--key-file PATH`** — read the key from a file. On Unix, `digest` emits a stderr warning if the file is readable by group or other (suggest `chmod 0600`). On Windows, ACL inspection is deferred to a future `protect`/`unprotect` tool.

- **`--key KEY`** — literal key as a CLI argument. **Always emits a warning** because the key is visible to:
  - Other users via `ps` / `/proc/*/cmdline`
  - Your shell history (`~/.bash_history`, `~/.zsh_history`)
  - Process listing tools and auditd logs

  Use `--key` only for transient throwaway testing — never in scripts or long-running systems.

### Encrypted-at-rest key files

`--key-stdin` is designed to compose with external encryption tooling so the on-disk key file is never plaintext. Pipe from `age`, `gpg`, `pass`, `secret-tool`, or `security find-generic-password` (macOS Keychain). A future `protect`/`unprotect` tool will add a Winix-native path for this on Windows.

### Trailing-newline stripping

When reading a key from `--key-file` or `--key-stdin`, `digest` strips a single trailing `\n` or `\r\n` by default. This is what you want when the key file was created with `echo "secret" > file`, which appends a newline. Pass `--key-raw` to preserve exact bytes (e.g. when the key file contains binary material and legitimately ends in `0x0A`).

### BLAKE2b key ceiling

BLAKE2b keyed mode caps keys at 64 bytes per RFC 7693 §2.9. Longer keys are rejected at flag-parse time. For longer keys, use `--hmac sha256` (or another SHA-family algorithm) which auto-hashes oversized keys.

## Input Modes

- **String (`-s VALUE`)** — hash the UTF-8 bytes of the literal string. Cannot be combined with positional file arguments.
- **Stdin (default, or explicit `-`)** — hash bytes read from standard input.
- **Single file** — `digest FILE`. If `FILE` does not exist, `digest` exits 125 with an error; it will **not** silently treat it as a literal string. Use `--string` if you want to hash an argument as text.
- **Multiple files** — `digest FILE1 FILE2 ...`. Produces one sha256sum-compatible line per file. If **any** file is missing, the whole batch fails before any output — no partial "hashed some, errored on the rest" state.

## Output Formats

| Format | Example (SHA-256 of "abc") |
|---|---|
| `--hex` (default) | `ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad` |
| `--hex --uppercase` | `BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD` |
| `--base64` | `ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=` |
| `--base64-url` | URL-safe base64 (replaces `+`/`/` with `-`/`_`) |
| `--base32` | Crockford base32 (uppercase, no `I`/`L`/`O`/`U`) |

## Verify Mode

`--verify EXPECTED` compares the computed hash against `EXPECTED` in constant time:

```bash
digest --verify e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 -s ""
# exit 0, no output

digest --verify wronghash -s ""
# stderr: digest: verification failed
# exit 1
```

Hex comparison is case-insensitive; base64 and base32 comparisons are case-sensitive. Verify is **not supported with multiple files** — use `sha256sum -c <checksum-file>` for batch verification flows.

## Multi-file Output Format

Multi-file output is `sha256sum`-compatible with the binary-mode marker:

```
ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad *file.bin
3608bca1e44ea6c4d268eb6db02260269892c0b42b86bbf1e77a6fa16c3c9282 *other.bin
```

The `*` before the filename signals **binary mode** — the hash was computed over raw bytes with no CR/LF translation. `digest` always reads files as raw bytes, so `*` is always accurate. Output is directly consumable by `sha256sum -c` for verification.

## Differences from `sha256sum`

- **All-or-nothing validation** on multi-file mode: a missing file errors the whole command rather than emitting hashes for the first N-1 files and erroring on file N. Safer for verification flows.
- **Binary-mode marker by default**: `*` is always used because `digest` always reads raw bytes — no confusing text-vs-binary modes.
- **Built-in HMAC**: one flag, four key sources, Unix permission warnings, literal-key exposure warnings. No need to shell out to `openssl dgst`.
- **Base64 and base32 output**: useful for JWT signatures, config files, and URL-safe tokens.
- **`--describe`** emits structured JSON metadata for AI agents.

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success (or verification match). |
| 1 | Verification failed (`--verify` mismatch). |
| 125 | Usage error — bad flags, unknown value, flag conflict, missing file. |
| 126 | Runtime error — SHA-3 unavailable on this platform, file read failure. |

## Related Tools

- [`clip`](../clip/README.md) — copy a hash to the clipboard: `digest file | clip`
- [`ids`](../ids/README.md) — generate UUIDs / ULIDs / NanoIDs (complementary ID primitives)
- Future `protect` / `unprotect` — Winix-native encrypted-at-rest storage for key files (Windows DPAPI and equivalents elsewhere)

## See Also

- `man digest` (after `winix install man`)
- `digest --describe` for JSON metadata
