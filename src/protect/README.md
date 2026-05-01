# protect

Cross-platform encrypt-at-rest CLI using native OS key-storage primitives. DPAPI on Windows, AES-256-GCM with Keychain on macOS, AES-256-GCM with libsecret on Linux. Single AOT native binary. Zero key management — the OS provides the key.

## Why

Files are encrypted at rest, scoped to the current user (or machine, on Windows and macOS). Moving encrypted files between machines or users fails by design — the OS key is tied to your identity. Perfect for storing sensitive config files, API keys, or database passwords where the threat model is "secure, offline, no key rotation."

Compose with `digest` for encrypted HMAC keys, with `clip` for clipboard automation, with pipes for streaming.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install protect
```

### .NET global tool (any platform with .NET 10+)

```bash
dotnet tool install --global Winix.Protect
```

### Native binary (GitHub Releases)

Download the platform-appropriate archive from [releases](https://github.com/Yortw/winix/releases) and place `protect` on your PATH.

## Usage

```bash
protect file.json                           # encrypts to file.json.prot
protect file.json -o secure.prot            # explicit output path
protect file.json --in-place                # encrypt over the file (atomic)
protect file.json --rm                      # delete source after verify
protect --scope machine config.xml          # machine-scoped (Windows/macOS only)
protect < input.bin > output.prot           # streaming (pure piping)

cat api.key | protect -o api.key.prot       # stdin streaming
```

## Options

| Flag | Default | Description |
|---|---|---|
| `-o PATH` / `--output PATH` | `FILE.prot` for files, stdout for stdin | Explicit output path. |
| `--in-place` | off | Encrypt/decrypt over the input file (atomic via temp + rename). |
| `--rm` / `--remove-source` | off | Delete source after successful round-trip verification. |
| `--scope {user,machine}` | `user` | Key-derivation scope. Windows: DPAPI CurrentUser/LocalMachine. macOS: login/System Keychain. Linux: user only (machine unsupported). |
| `--no-verify` | off | Skip round-trip verification (faster, less safe). |
| `-f` / `--force` | off | Overwrite an existing destination file. By default the tool refuses to clobber existing data and exits 125. The overwrite is symlink-safe — if the destination is a symlink, the symlink itself is removed (the target file is untouched) before an exclusive create. |
| `--color`, `--no-color` | — | Respect `NO_COLOR`. |
| `--describe` | — | Emit tool metadata as JSON. |
| `--help`, `--version` | — | Standard introspection. |

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 125 | Usage error — bad flags, path collision, unsupported scope on Linux. |
| 126 | Runtime error — encryption failure, key store unavailable, round-trip verify failed. |

## How It Works

Each OS provides a secure key-derivation function:

- **Windows**: DPAPI (`ProtectedData.Protect`) — keys are user/machine credentials stored in the kernel.
- **macOS**: AES-256-GCM with a key stored in the login/System Keychain. Key is auto-generated on first use, never exposed to the user.
- **Linux**: AES-256-GCM with a key stored in libsecret. Same auto-generate-on-first-use pattern.

Files are chunked (64 KB per chunk) and each chunk is encrypted with AEAD (AES-256-GCM) or DPAPI. Every chunk is bound to its specific file via a 16-byte random FileId stored in the header — chunk reorder, cross-file substitution, and truncation are all detected. Round-trip verification (encrypt → decrypt → hash comparison) is included by default; disable with `--no-verify` for speed.

## Integrity Model

Each chunk is authenticated with both its position (chunk index, isFinal flag) and the file's per-file FileId. This prevents:

- **Truncation** — silent removal of chunks (final-chunk flag).
- **Reorder** — swapping chunks within a file (chunk index in AAD).
- **Cross-file substitution** — splicing a chunk from a different `.prot` file into this one (FileId in AAD).

**Threat model boundary.** The protections above defend against an attacker with **write** access to your `.prot` file but **no** key access — for example, untrusted backup software, cloud sync services, or shared NAS storage. If the attacker has both filesystem write AND access to your OS keystore (same OS user, or LocalMachine scope on a shared host), they can decrypt directly — splice protections are not the relevant defense.

## Platform Notes

- **Windows**: Both user and machine scopes are supported. Machine scope requires the process to have DPAPI LocalMachine access (typically as admin or via SYSTEM account).
- **macOS**: User scope uses the login Keychain. Machine scope uses the System Keychain and requires `sudo` or admin privileges.
- **Linux**: Only user scope is supported. The tool requires `libsecret-tools` to be installed (`sudo apt install libsecret-tools` on Debian/Ubuntu, etc.).

## Non-Portability by Design

Files encrypted with `protect` are **not portable** between:
- Machines (keys are tied to your device)
- Users (keys are tied to your OS user account)
- Scopes (machine scope cannot decrypt user scope and vice versa)

If you need portable encryption, use `age` or `gpg`. `protect` trades portability for zero-key-management convenience.

## Composition

```bash
# Encrypt an HMAC key; decrypt and use it
protect api.key --rm
unprotect < api.key.prot | digest --hmac sha256 --key-stdin "payload"

# Encrypt a config, pass to another tool
protect config.json -o .config.prot
unprotect < .config.prot | app --config -
```

## Related Tools

- [`unprotect`](../unprotect/README.md) — companion; decrypts .prot files
- [`digest`](../digest/README.md) — hash and HMAC files (pairs with encrypted keys)
- [`clip`](../clip/README.md) — clipboard bridge for credentials

## See Also

- `man protect` (after `winix install man`)
- `protect --describe` for JSON metadata
