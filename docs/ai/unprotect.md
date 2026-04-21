# unprotect — AI Agent Guide

## What This Tool Does

`unprotect` decrypts files that `protect` encrypted, using the same native OS key-storage primitive of each platform:
- **Windows**: DPAPI (`ProtectedData.Unprotect`)
- **macOS**: AES-256-GCM with a key retrieved from the Keychain
- **Linux**: AES-256-GCM with a key retrieved from libsecret

Decryption succeeds only if:
1. The file was encrypted on the current machine and by the current user (default scope), or
2. The file was encrypted with machine scope on Windows/macOS (and you have admin/root access), and
3. The OS key is still available in the key store.

If any condition fails, `unprotect` fails with a clear error message. Truncation and tampering are detected via AEAD authentication tags (GCM) or DPAPI integrity checks.

## When to Use This

- Decrypt a file encrypted with `protect`: `unprotect secret.prot`
- Decrypt in-place (atomic): `unprotect secret.prot --in-place`
- Decrypt and delete the encrypted file: `unprotect secret.prot --rm`
- Decrypt and pipe to another tool: `unprotect < api.key.prot | digest --hmac sha256 --key-stdin "payload"`
- Decrypt and decompress: `unprotect < backup.tar.gz.prot | tar -xz`
- Stream decryption via pipes: `cat encrypted.prot | unprotect > plaintext.bin`

## When NOT to Use This

- For decrypting files from a different user or machine — the OS key won't be available (by design).
- For decrypting files encrypted with `age`, `gpg`, or `sops` — use the corresponding tool.
- For general-purpose decompression — `unprotect` only handles files encrypted by `protect`.

## Basic Invocation

```bash
# Decrypt a file (strips .prot suffix)
unprotect secret.prot
# creates secret

# Decrypt and delete the encrypted file
unprotect secret.prot --rm

# Decrypt in place (atomic: temp write → verify → rename)
unprotect secret.prot --in-place

# Stream from stdin
cat encrypted.prot | unprotect > plaintext.bin
# or
unprotect < encrypted.prot

# Decrypt to a specific output path
unprotect input.prot -o output.bin

# Decrypt with machine scope (must match encrypt's scope)
unprotect --scope machine service-config.prot
```

## Output Behavior

- **File input** (e.g., `unprotect secret.prot`): writes to `secret` (strips `.prot` suffix) by default, or `-o` path. The encrypted file is kept unless `--rm` or `--in-place` is used.
- **Stdin input** (no positional arg): writes to stdout. Use `-o` to write to a file instead.
- **Explicit `-` for stdin**: same as no positional arg.

## Scope: User vs Machine

The `--scope` flag must match the scope that was used to encrypt:

| Scope | Windows | macOS | Linux | Notes |
|---|---|---|---|---|
| `user` (default) | DPAPI CurrentUser | login Keychain | libsecret user session | Decrypt files encrypted with user scope. |
| `machine` | DPAPI LocalMachine | System Keychain | ❌ not supported | Decrypt files encrypted with machine scope. Requires admin/root. |

If you try to decrypt a machine-scoped file with `--scope user` (or vice versa), `unprotect` reads the platform marker from the file header and fails with:
```
unprotect: This file was encrypted with scope 'machine' but '--scope user' was given. Retry with '--scope machine'.
```

## Round-Trip Verification

By default, `unprotect` decrypts, then computes a hash of the decrypted plaintext and compares it to the hash computed during encryption:
- During decrypt: incremental SHA-256 of the plaintext.
- Compare: hash computed during encrypt (stored implicitly in the file structure).
- Mismatch: delete decrypted output, preserve encrypted source, exit 126 with `"Decryption integrity check failed. Encrypted file preserved."`.

This catches bugs in framing, AAD validation, or AEAD logic. Disable with `--no-verify` for speed (not recommended in production).

## Atomic Rename (In-Place Mode)

`unprotect FILE --in-place` is safe:
1. Decrypt to a temporary file in the same directory as `FILE` (ensures same-volume).
2. Run round-trip verification.
3. Atomically rename temp → `FILE` (via `File.Move(overwrite: true)`, which maps to `MoveFileEx` on Windows and `rename(2)` on POSIX).
4. If any step fails, temp file is cleaned up and encrypted source is preserved.

## Flag Combinations

- **`-o` and `--in-place` are mutually exclusive.** Use one or the other.
- **`--rm` with explicit `-o`**: decrypt to `-o` path, verify, then delete the encrypted source.
- **`--rm` with `--in-place`**: implicit — the source is atomically renamed after verification.
- **`--in-place` with `-o` is an error** (exit 125).

## Error Handling

| Exit | Meaning | Action |
|---|---|---|
| 0 | Success. Decrypted file written; encrypted source handled per flags. | — |
| 125 | Usage error — bad flags, path collision, scope mismatch, unsupported combo. | Check flags and arguments. |
| 126 | Runtime error — decryption failed, wrong platform/user, key unavailable, truncated/tampered ciphertext. | Decrypted file deleted if verify fails; encrypted source preserved. Check OS key store. |

## Decryption Failures (by Design)

`unprotect` fails clearly if:

1. **File encrypted on a different machine**: Platform marker identifies the machine/key, but the OS key is machine-tied. Exit 126 with `"This file was encrypted on a different machine and cannot be decrypted here."`.
2. **File encrypted by a different user**: Windows/macOS/Linux all tie keys to user identity. Exit 126 with `"This file was encrypted by a different user and cannot be decrypted."`
3. **Scope mismatch**: File says `machine` but you pass `--scope user`. Exit 126 with suggestion to retry with correct scope.
4. **Key store unavailable**: Keychain/libsecret not running or not accessible. Exit 126 with platform-specific hint (e.g., `"libsecret-tools not installed. Install with 'sudo apt install libsecret-tools'."`)
5. **Truncated or tampered ciphertext**: AEAD tag mismatch or DPAPI integrity check fails. Exit 126 with `"Ciphertext is corrupt or truncated."`.

All these are **expected failures**, not bugs. `protect` / `unprotect` are not portable by design.

## Platform Notes

**Windows:**
- User scope: DPAPI CurrentUser. Key derived from your Windows credentials. Only you can decrypt.
- Machine scope: DPAPI LocalMachine. Any user on the machine can decrypt (requires admin/SYSTEM context). Files encrypted by a different Windows user on a different machine cannot be decrypted.
- No decryption of files from other machines.

**macOS:**
- User scope: login Keychain (automatically unlocked on user login). Only you can decrypt.
- Machine scope: System Keychain (requires `sudo` or admin). Files encrypted by a different user or on a different machine cannot be decrypted.
- No decryption of files from other machines.

**Linux:**
- User scope only. Machine scope fails with exit 125.
- Key is retrieved from libsecret (typically `~/.local/share/secrets` or systemd user service).
- libsecret-tools must be installed: `sudo apt install libsecret-tools` (Debian/Ubuntu), `sudo dnf install libsecret` (Fedora), or equivalent.
- Files encrypted by a different user or on a different machine cannot be decrypted.

## File Format Detection

`unprotect` reads the 6-byte header to determine:
1. **Magic** (`WPRT`): file is a `protect` file (vs random binary data)
2. **Version** (1): file format version (future compatibility)
3. **Platform marker**: which backend encrypted it (Windows DPAPI user/machine, macOS Keychain, Linux libsecret)

The platform marker lets `unprotect` emit specific, helpful errors:
- Decrypting a Windows-DPAPI file on macOS: `"This file was encrypted on Windows and cannot be decrypted on macOS."`
- Decrypting a machine-scope file with user scope: `"This file was encrypted with scope 'machine' but '--scope user' was given."`

## Composability

```bash
# Decrypt an HMAC key and use it
unprotect < api.key.prot | digest --hmac sha256 --key-stdin "payload"

# Decrypt a config, pass to an application
unprotect config.prot | myapp --config -

# Decrypt and decompress
unprotect < backup.tar.gz.prot | tar -xz

# Decrypt and decompress and extract
unprotect backup.tar.prot | zcat | tar -x

# Decrypt and pipe to another tool
unprotect secret.prot | myapp
```

## Interaction with protect

`protect` is the inverse operation. Both use the same OS key:

```bash
protect file.json         # creates file.json.prot
unprotect file.json.prot  # creates file.json
```

The workflow is typically:
1. Encrypt once: `protect api.key --rm`
2. Use many times: `unprotect < api.key.prot | digest --hmac sha256 --key-stdin "payload"`

Or for config files:
1. Encrypt once: `protect config.json --in-place`
2. On startup: `unprotect config.json.prot | myapp --config -` or `unprotect config.json.prot -o /tmp/config && myapp --config /tmp/config`

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 125 | Usage error — bad flags, path collision, scope mismatch, unsupported combo. |
| 126 | Runtime error — decryption failed, wrong platform/user, key unavailable, truncated/tampered ciphertext. |

## Metadata

Run `unprotect --describe` for structured JSON metadata (flags, examples, composability).
Run `unprotect --help` for human-readable help.
