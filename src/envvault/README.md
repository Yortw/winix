# envvault

Cross-platform keychain-backed environment-variable manager. Envchain-compatible on macOS/Linux, with a native Windows backend (DPAPI + Credential Manager). Store secrets once, export them into any child process on demand.

## Why

Secrets don't belong in shell `.rc` files, `.env` files, or commit history. `envvault` keeps them in your OS key store (Credential Manager on Windows, Keychain on macOS, libsecret on Linux) and re-injects them as environment variables only for the processes you ask for. Works as a drop-in for [envchain](https://github.com/sorah/envchain) — existing muscle-memory (`envchain github gh pr list`) still works, and there's a Windows backend envchain never had.

## Install

### via winget

```bash
winget install Winix.EnvVault
```

### via scoop

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install envvault
```

### via NuGet as a .NET global tool

```bash
dotnet tool install --global Winix.EnvVault
```

### Native binary (GitHub Releases)

Download the platform-appropriate archive from [releases](https://github.com/Yortw/winix/releases) and place `envvault` on your `PATH`.

## Usage

### Exec form (envchain-compatible)

Run a command with a namespace's variables merged into its environment:

```bash
envvault github gh pr list
envvault aws terraform apply
envvault postgres psql -h db.example.com
```

Merge multiple namespaces in order (later entries win on key collisions):

```bash
envvault github,aws ./deploy.sh
envvault base,prod,secrets ./release.sh
```

### Store a value

Interactive (no echo — value never appears in terminal or shell history):

```bash
envvault --set github GITHUB_TOKEN
# prompt: GITHUB_TOKEN: (hidden)
```

Multiple keys in one call:

```bash
envvault --set aws AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY
```

Non-interactive (convenient for scripts, but exposes the value on argv and in shell history):

```bash
envvault --value "ghp_xxx..." --set github GITHUB_TOKEN
```

### Retrieve a single value

```bash
envvault --get github GITHUB_TOKEN
```

Exits `127` with a clear message if the namespace or key doesn't exist.

### Remove a value

```bash
envvault --unset github GITHUB_TOKEN
```

Exits `127` if the key didn't exist.

### List namespaces and keys

```bash
envvault --list              # all namespaces
envvault --list github       # all keys in one namespace (values never printed)
envvault --list --json       # machine-readable output
```

## Options

| Flag | Arg | Description |
|---|---|---|
| `--set` | `<NAMESPACE> <KEY>...` | Store one or more keys. Prompts per key (hidden input) unless `--value` is given. |
| `--value` | `<VALUE>` | Non-interactive value for `--set`. Single key only. Exposes value on argv/shell history. |
| `--get` | `<NAMESPACE> <KEY>` | Print the stored value to stdout. |
| `--unset` | `<NAMESPACE> <KEY>` | Remove a stored key. |
| `--list` | `[<NAMESPACE>]` | List namespaces, or keys in one namespace. Never prints values. |
| `--json` | — | JSON output for `--list`. |
| `--noecho` | — | Accepted for envchain compat. `--set` already hides input by default. |
| `--require-passphrase` | — | **Deferred to v1.1.** Currently errors out. |
| `--no-color`, `--color` | — | ANSI colour control. Respects `NO_COLOR` env var. |
| `--describe` | — | Emit structured metadata for AI agents. |
| `--help`, `--version` | — | Standard introspection. |

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 125 | Usage error — bad flags, missing arguments, conflicting options, deferred feature used (`--require-passphrase`). |
| 126 | Runtime error — key store unavailable, permission denied launching child command. |
| 127 | Not found — namespace or key missing for `--get`/`--unset`; command for exec form not on PATH. |
| * | Exec form: on success or child failure, passes through the child process's exit code. |

## Coming from envchain?

Things that work **identically**:

- `envvault <NAMESPACE> <command>` — the bare envchain form
- `envvault --set <NAMESPACE> <KEY>` with hidden-input prompt
- `envvault --list` and `envvault --list <NAMESPACE>`
- `--noecho` is accepted (but input is always hidden)

Things that are **extended**:

- Multi-namespace merge via comma-separated list: `envvault github,aws deploy.sh`
- **Windows backend** (DPAPI + Credential Manager) — envchain is *nix only
- `--json` output for `--list`
- `--value` flag for scripted non-interactive `--set`

Things that are **deferred**:

- `--require-passphrase` — queued for v1.1, pending a native macOS Security.framework path.

Alias hint for muscle memory:

```bash
alias envchain=envvault
```

## Security Notes

- Stored values live in the OS key store — Windows Credential Manager (DPAPI under the hood), macOS Keychain, or Linux libsecret. The OS owns the encryption keys; `envvault` never sees them directly.
- `--value <V>` exposes the secret on the process argv and in your shell history. Prefer the interactive prompt.
- `envvault --get <NS> <KEY>` prints the value to stdout. Piping into another tool is safe; echoing to a terminal may leave the value in scrollback.
- The exec form never round-trips secrets through a file — values go straight into the child process environment block.
- Values in the child's environment are visible to that process and (on some OSes) to root/admin users who can read `/proc/<pid>/environ` or equivalent.

## Colour Handling

- Respects the [`NO_COLOR`](https://no-color.org) environment variable.
- `--no-color` / `--color` flags override detection.
- Warnings (e.g. deferred-feature notices, missing namespaces) go to stderr.

## Related Tools

- [`protect`](../protect/README.md) — encrypt individual files at rest using the same OS key primitives
- [`digest`](../digest/README.md) — HMAC a payload with a stored key: `envvault hmac digest --hmac sha256 --key-env HMAC_KEY payload.json`
- [`clip`](../clip/README.md) — push values to the clipboard for paste-only workflows

## See Also

- `man envvault` (after `winix install man`)
- `envvault --describe` for JSON metadata
- Upstream envchain: <https://github.com/sorah/envchain>
