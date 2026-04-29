# unprotect

Companion to `protect` — decrypts files that `protect` encrypted using the same native OS key-storage primitives (DPAPI on Windows, Keychain on macOS, libsecret on Linux). Single AOT native binary. Zero key management.

## Why

Files encrypted with `protect` are scoped to a specific user and machine. `unprotect` reverses the encryption using the same OS key that encrypted them. If the key is gone (different user, different machine, or cleared key store), decryption fails — a deliberate safety feature.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install unprotect
```

### .NET global tool (any platform with .NET 10+)

```bash
dotnet tool install --global Winix.Unprotect
```

### Native binary (GitHub Releases)

Download the platform-appropriate archive from [releases](https://github.com/Yortw/winix/releases) and place `unprotect` on your PATH.

## Usage

```bash
unprotect file.json.prot                    # decrypts to file.json
unprotect file.json.prot -o plaintext.json  # explicit output path
unprotect file.json.prot --in-place         # decrypt over the file (atomic)
unprotect < encrypted.prot > plaintext.bin  # streaming (pure piping)
unprotect file.prot --rm                    # delete encrypted file after success
```

## Options

| Flag | Default | Description |
|---|---|---|
| `-o PATH` / `--output PATH` | `FILE` (strips `.prot`), or stdout for stdin | Explicit output path. |
| `--in-place` | off | Decrypt/decrypt over the input file (atomic via temp + rename). |
| `--rm` / `--remove-source` | off | Delete source after successful decryption. |
| `--scope {user,machine}` | `user` | Key-derivation scope (must match the scope used to encrypt). |
| `--no-verify` | off | Skip round-trip verification (faster, less safe). |
| `-f` / `--force` | off | Overwrite an existing destination file. By default the tool refuses to clobber existing data and exits 125. The overwrite is symlink-safe — if the destination is a symlink, the symlink itself is removed (the target file is untouched) before an exclusive create. |
| `--color`, `--no-color` | — | Respect `NO_COLOR`. |
| `--describe` | — | Emit tool metadata as JSON. |
| `--help`, `--version` | — | Standard introspection. |

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 125 | Usage error — bad flags, path collision, scope mismatch. |
| 126 | Runtime error — decryption failure, wrong platform/user, key store unavailable, round-trip verify failed. |

## How It Works

`unprotect` reads the encrypted file's header to determine:
1. Which platform originally encrypted it (Windows DPAPI, macOS Keychain, or Linux libsecret)
2. Which scope was used (user or machine)
3. Whether it's still encrypted (vs already plaintext)

It then decrypts using the same OS key that was used to encrypt. If the key is no longer available (different user, different machine, or key deleted), decryption fails with a clear error message.

Truncation and tampering are detected via AEAD authentication tags (GCM) or DPAPI integrity checks.

## Platform Notes

- **Windows**: Both user and machine scopes are supported. Machine-scope files require the process to have DPAPI LocalMachine access.
- **macOS**: User scope uses the login Keychain. Machine scope uses the System Keychain and requires admin privileges.
- **Linux**: Only user scope is supported. The tool requires `libsecret-tools` to be installed.

## Cross-Platform Failures (by Design)

`unprotect` will fail clearly if:
- The file was encrypted on a different machine or by a different user (wrong key)
- The file was encrypted with machine scope but you're running user scope (or vice versa)
- The OS key store has been cleared or the key deleted
- The file has been truncated or tampered with

## Composition

```bash
# Decrypt an HMAC key and use it
unprotect < api.key.prot | digest --hmac sha256 --key-stdin "payload"

# Decrypt a config, pass to an application
unprotect config.prot | myapp --config -

# Pipe through a decompressor
unprotect < backup.prot | zcat | tar -x
```

## Related Tools

- [`protect`](../protect/README.md) — companion; encrypts files
- [`digest`](../digest/README.md) — hash files (pairs with encrypted keys)
- [`clip`](../clip/README.md) — clipboard bridge for credentials

## See Also

- `man unprotect` (after `winix install man`)
- `unprotect --describe` for JSON metadata
