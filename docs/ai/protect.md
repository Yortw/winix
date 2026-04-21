# protect — AI Agent Guide

## What This Tool Does

`protect` encrypts files and streams using the native OS key-storage primitive of each platform:
- **Windows**: DPAPI (`ProtectedData.Protect`) — the OS derives the encryption key from user/machine credentials. Zero user key management.
- **macOS**: AES-256-GCM with a 256-bit key stored in the login/System Keychain. Key is auto-generated on first use, never exposed to the user.
- **Linux**: AES-256-GCM with a 256-bit key stored in libsecret. Same auto-generate-on-first-use pattern.

Encrypted files are scoped to the **current user and machine by default**. Moving an encrypted file to a different user, machine, or scope fails — this is deliberate, not a bug. The OS key is no longer available, so decryption fails with a clear error.

Output is a `.prot` file (or stdout). Input can be a file, stdin (default), or explicit `-` for stdin. Files are chunked (64 KB per chunk) for streaming efficiency; small files are just one chunk.

## When to Use This

- Encrypt a config file, API key, or database password at rest: `protect config.json --rm`
- Encrypt an HMAC key for use with `digest`: `protect api.key --rm` then later `unprotect < api.key.prot | digest --hmac sha256 --key-stdin "payload"`
- Encrypt a file in-place atomically: `protect config.json --in-place`
- Encrypt for machine scope (Windows/macOS) — e.g., a service config: `protect --scope machine service-config.xml`
- Stream encryption via pipes: `cat secret.txt | protect -o secret.prot`
- Encrypt with verification: round-trip decryption check is built-in (disable with `--no-verify` for speed)

## When NOT to Use This

- For portable encryption (encryption you'll move between machines/users) — use `age` or `gpg`
- For password hashing — none of these algorithms are suitable. Use `argon2`, `bcrypt`, or `scrypt`
- For encrypting structured data (YAML/JSON/ENV) with external KMS — use `sops`
- For whole-disk or filesystem-level encryption — use `FileVault` (macOS), `BitLocker` (Windows), or `LUKS` (Linux)

## Basic Invocation

```bash
# Encrypt a file, keep the original
protect config.json
# creates config.json.prot

# Encrypt and delete the original after verification
protect api.key --rm

# Encrypt in place (atomic: temp write → verify → rename)
protect config.json --in-place

# Stream from stdin
cat secret.txt | protect -o secret.prot

# Read file, output to specific path
protect input.bin -o output.prot

# Machine-scoped encryption (Windows/macOS only)
protect --scope machine service-config.xml
```

## Output Behavior

- **File input** (e.g., `protect file.json`): writes to `FILE.json.prot` by default (or `-o` path). The original file is kept unless `--rm` or `--in-place` is used.
- **Stdin input** (no positional arg): writes to stdout. Use `-o` to write to a file instead.
- **Explicit `-` for stdin**: same as no positional arg. Allows distinguishing `protect -` (stdin) from `protect file` (file).

## Scope: User vs Machine

| Scope | Windows | macOS | Linux | Notes |
|---|---|---|---|---|
| `user` (default) | DPAPI CurrentUser | login Keychain | libsecret user session | Encrypted for this user only. Different user = decryption fails. |
| `machine` | DPAPI LocalMachine | System Keychain | ❌ not supported | Encrypted for any user on this machine. Requires admin/root to encrypt/decrypt. |

`--scope machine` on Linux fails fast with exit 125.

## Round-Trip Verification

By default, `protect` encrypts, then immediately re-opens the encrypted file, decrypts it, and compares the hash to the original:
- During encrypt: incremental SHA-256 of the plaintext.
- After encrypt: re-open encrypted file, incremental SHA-256 during decrypt.
- Mismatch: delete encrypted output, preserve original, exit 126 with `"Encryption integrity check failed. Source file preserved."`.

This catches bugs in framing, AAD construction, or key derivation. Disable with `--no-verify` for speed (not recommended in production).

## Atomic Rename (In-Place Mode)

`protect FILE --in-place` is safe:
1. Encrypt to a temporary file in the same directory as `FILE` (ensures same-volume).
2. Run round-trip verification.
3. Atomically rename temp → `FILE` (via `File.Move(overwrite: true)`, which maps to `MoveFileEx` on Windows and `rename(2)` on POSIX).
4. If any step fails, temp file is cleaned up and source is preserved.

Cross-volume renames are not supported in v1; the temp file is always created in the same directory as the target.

## Flag Combinations

- **`-o` and `--in-place` are mutually exclusive.** Use one or the other.
- **`--rm` with explicit `-o`**: encrypt to `-o` path, verify, then delete the source.
- **`--rm` with `--in-place`**: implicit — the source is atomically renamed after verification. Using `--rm` and `--in-place` together is redundant but allowed (clear intent in scripts).
- **`--in-place` with `-o` is an error** (exit 125).

## Error Handling

| Exit | Meaning | Action |
|---|---|---|
| 0 | Success. Encrypted file written; source handled per flags. | — |
| 125 | Usage error — bad flags, path collision, unsupported scope. | Check flags and arguments. |
| 126 | Runtime error — encryption failed, key store unavailable, round-trip verify failed. | Encrypted file deleted if verify fails; source preserved. Check OS key store. |

## Platform Notes

**Windows:**
- User scope: DPAPI CurrentUser. Key derived from logged-in user credentials.
- Machine scope: DPAPI LocalMachine. Key available to any user on the machine but requires admin/SYSTEM context to encrypt/decrypt.
- Encrypted files are not portable between users or machines.

**macOS:**
- User scope: login Keychain (automatically unlocked on login). Key is auto-generated, stored in Keychain.
- Machine scope: System Keychain. Requires root or admin (via `sudo`). Key is auto-generated, stored in System Keychain.
- Encrypted files are not portable between machines or users.

**Linux:**
- User scope only. Machine scope fails with exit 125.
- Key is stored in libsecret (typically `~/.local/share/secrets` or systemd user service).
- libsecret-tools must be installed: `sudo apt install libsecret-tools` (Debian/Ubuntu), `sudo dnf install libsecret` (Fedora), or equivalent.
- Encrypted files are not portable between machines or users.

## File Format

All platforms use the same chunked format:
1. 6-byte header: magic (`WPRT`), version (1), platform marker (1 byte identifying which backend encrypted it)
2. Chunk stream: each chunk is framed and encrypted with either DPAPI or AES-256-GCM
3. Final chunk is marked; truncation is detected at decryption

Platform markers:
- `0x01` = Windows DPAPI user scope
- `0x02` = Windows DPAPI machine scope
- `0x10` = macOS Keychain user scope
- `0x11` = macOS Keychain machine scope
- `0x20` = Linux libsecret user scope

## Composability

```bash
# Encrypt an HMAC key, decrypt and use it
protect api.key --rm
unprotect < api.key.prot | digest --hmac sha256 --key-stdin "payload"

# Encrypt a config, pass to an application
protect config.json -o config.prot
unprotect < config.prot | myapp --config -

# Stream through a compressor before encrypting
gzip -c config.json | protect -o config.json.gz.prot

# Backup encrypted
tar -c /important/dir | protect -o backup.tar.prot

# Encrypt and copy to clipboard
protect secret.txt | clip
```

## Interaction with unprotect

`unprotect` is the inverse operation. Both use the same OS key:

```bash
protect file.json       # creates file.json.prot
unprotect file.json.prot  # creates file.json (strips .prot)
```

If the encrypted file was created with `--scope machine`, decryption must also use `--scope machine` (or `unprotect` will fail with a clear message).

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 125 | Usage error — bad flags, path collision, unsupported scope on Linux. |
| 126 | Runtime error — encryption failed, key store unavailable, round-trip verify failed, permission denied. |

## Metadata

Run `protect --describe` for structured JSON metadata (flags, examples, composability).
Run `protect --help` for human-readable help.
