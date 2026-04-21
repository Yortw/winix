# envvault — AI Agent Guide

## What This Tool Does

`envvault` stores named environment variables in the OS key store and merges them into the environment of a child process on demand. It is wire-compatible with [envchain](https://github.com/sorah/envchain) on macOS/Linux and adds a native Windows backend:

- **Windows**: Credential Manager (DPAPI-protected entries).
- **macOS**: login Keychain via `security(1)`.
- **Linux**: libsecret via `secret-tool(1)`.

Secrets never leave the OS key store except when merged into a child process environment by the exec form, or explicitly printed by `--get`. `envvault` itself does not manage any encryption keys — the OS does.

If the user has envchain instincts, envvault accepts the same bare `<NAMESPACE> <command>` form. Use it as a drop-in replacement.

## When to Use This

- Stash API tokens, database passwords, signing keys out of `.env` / `.bashrc` and into the OS key store
- Run any CLI with secrets injected as env vars: `envvault github gh pr list`
- Compose multiple secret scopes in one command: `envvault github,aws ./deploy.sh`
- Store secrets on Windows the way envchain users do on Linux/macOS — same commands, different backend

## When NOT to Use This

- For shared team secrets — `envvault` is per-user, per-machine. Use Vault, Doppler, AWS Secrets Manager, etc.
- For secrets that must be decryptable by another user or machine — use `sops` or `age`
- For TLS certificates and key material that needs hardware-backed key storage — use platform-native tooling (Keychain reference entries, TPM, HSM)
- For plaintext config that isn't actually a secret — keep using environment files

## Operations

| Subcommand | Input | Output |
|---|---|---|
| `envvault <NS>[,<NS>...] <cmd> [args]` | command + args | child's stdout/stderr; exit code passthrough |
| `envvault --set <NS> <KEY>...` | hidden prompt per key (or one line per key on stdin if piped) | none; stores values |
| `envvault --value <V> --set <NS> <KEY>` | value on argv (single key only) | none; stores value |
| `envvault --get <NS> <KEY>` | — | value on stdout |
| `envvault --unset <NS> <KEY>` | — | none; removes key |
| `envvault --list` | — | namespace list on stdout |
| `envvault --list <NS>` | — | key list on stdout (values NOT printed) |
| `envvault --list --json` | — | JSON namespaces/keys |

## Basic Invocation

```bash
# Store a GitHub token, hidden input
envvault --set github GITHUB_TOKEN

# Run any tool with the token in its environment
envvault github gh pr list

# Store multiple keys in one namespace
envvault --set aws AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY

# Merge multiple namespaces (later wins on collisions)
envvault github,aws ./deploy.sh

# Retrieve a single value
envvault --get github GITHUB_TOKEN

# List namespaces / keys
envvault --list
envvault --list github
```

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 1 | Runtime error — key/namespace not found on `--get`/`--unset`, key store unavailable, deferred feature (`--require-passphrase`) used. |
| 2 | Usage error — bad flags, missing arguments, conflicting options. |
| *N* | Exec form: passes through the child process's exit code. |

## envchain Compatibility

envvault accepts the exact bare form envchain users already know:

```bash
envchain github gh pr list      # unchanged
envvault github gh pr list      # identical behaviour
```

Suggest `alias envchain=envvault` for muscle-memory compatibility. Differences:

- **Extended**: comma-separated multi-namespace (`envvault a,b,c cmd`), `--json` output, Windows backend, `--value` flag for scripted set.
- **Deferred**: `--require-passphrase` flag is currently stubbed and exits with a clear error message. Target v1.1.
- **Accepted for compat**: `--noecho` is a no-op — interactive `--set` already hides input.

## Security Guidance for Agents

- **Never pass secrets on argv.** `--value <V> --set` works but puts the secret in `ps` output and shell history. Prefer `--set <NS> <KEY>` with the hidden prompt, or pipe one-line-per-key via stdin.
- **Don't echo `--get` output to a terminal casually.** Piping into another tool is safe; rendering to a TTY leaves the value in scrollback.
- **Child processes see the full merged environment.** Anything spawned under `envvault <ns> cmd` can read and propagate the secrets. Run only trusted commands.
- **Per-user scope.** Secrets written as one user aren't visible to another user on the same machine.
- **Multi-namespace order matters.** `envvault a,b,c cmd` merges in left-to-right order; later namespaces overwrite earlier entries for colliding keys.

## Machine-Parseable Output

`envvault --list --json` emits:

```json
{ "namespaces": ["github", "aws"] }
```

or, with a namespace argument:

```json
{ "namespace": "github", "keys": ["GITHUB_TOKEN", "GH_ENTERPRISE_TOKEN"] }
```

Values are never included in `--list` JSON.

## Platform-Specific Notes

**Windows:**
- Entries are stored as DPAPI-protected generic credentials in Credential Manager under the target name `envvault:<namespace>:<key>`.
- Visible in `cmdkey /list` and `rundll32.exe keymgr.dll,KRShowKeyMgr` but the blob is ciphertext.

**macOS:**
- Entries are generic password items in the login Keychain (`security(1)` backend).
- A self-healing index item tracks the namespace/key list. Entries added via raw `security` CLI bypass the index and won't appear in `--list` until a subsequent envvault write re-establishes it.

**Linux:**
- Entries are stored via libsecret (`secret-tool(1)`), typically in the GNOME keyring or KWallet depending on desktop environment.
- Requires `libsecret-tools` / `libsecret` to be installed.

## Metadata

Run `envvault --describe` for JSON metadata (subcommands, flags, modes).
Run `envvault --help` for human-readable help.
