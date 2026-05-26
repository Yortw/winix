# envvault — Cross-Platform Keychain-Backed Env Var Manager

**Date:** 2026-04-21
**Status:** Approved (design)
**Targets:** v0.4.0 (release/v0.4.0 branch, after `protect`/`unprotect`)

## Summary

`envvault` is a cross-platform CLI that stores named environment-variable sets (namespaces) in the OS-native keychain and injects them into child processes on demand. It is a drop-in-compatible reimplementation of [envchain](https://github.com/sorah/envchain), plus a Windows backend (which envchain explicitly does not support) and a handful of narrow extensions.

The tool rides on the existing `Winix.SecretStore` shared library (already battle-tested by `protect`), adding a new capability — enumeration — required by envvault's `--list` operation. No new shared libraries are introduced by envvault itself.

## Motivation

Developers routinely store secrets (`GITHUB_TOKEN`, `AWS_SECRET_ACCESS_KEY`, `OPENAI_API_KEY`) in shell rc files or plaintext `.env` files. envchain established the "OS-keychain-backed, inject-on-exec" model as a better answer on macOS and Linux a decade ago, and Unix developers rely on it daily. Windows developers have no equivalent — the author of envchain has declined to add a Windows backend.

The winix suite already ships `Winix.SecretStore` with DPAPI/Keychain/libsecret backends. With that primitive in place, the cost of shipping a cross-platform envchain-shaped tool is small, and the gap is genuine: no existing cross-platform CLI fills this slot without requiring a vendor account (1Password/Bitwarden CLI), GPG setup (`pass`/`gopass`), or a remote KMS (`sops`, HashiCorp Vault).

## Tier-1 Landscape

| Tool | Why it doesn't fill the gap |
|---|---|
| **envchain** | No Windows. Otherwise the reference implementation. |
| **1Password CLI / Bitwarden CLI** | Requires vendor account; online dependency. |
| **pass / gopass** | GPG key management friction. |
| **sops** | Team/cloud tool (KMS-backed); not a local-dev single-user workflow. |
| **HashiCorp Vault** | Server-based; overkill for local development. |
| **direnv** | Plaintext `.envrc` files; different security model. |
| **PowerShell SecretManagement** | PowerShell-only; awkward from bash/zsh/fish. |
| **Doppler / Infisical** | Cloud service; subscription; vendor lock-in. |

The "local, OS-native, cross-platform, no-account, no-GPG, single-binary" slot is empty. `envvault` fills it.

## Design Principles

1. **envchain UX compatibility is a feature.** `alias envchain=envvault` should work for Unix users with existing muscle memory. This is the explicit reason `envvault` uses flag-mode dispatch (`--set`/`--list`) plus a bare-positional exec form, deviating from the suite's usual subcommand-dispatch convention. The suite has precedent for this in `less`, `man`, and `nc`: when cloning a well-known tool to fill a platform gap, we mimic the clone target's UX rather than impose winix-native shape.
2. **Native enumeration where native enumeration is clean.** Windows (`CredEnumerateW`) and Linux (`secret-tool search --all`) support prefix-filtered enumeration well; we use them directly. macOS `security` CLI does not; macOS uses an envvault-private index entry as a fallback.
3. **Index desync is invisible to users.** On macOS, `list` self-heals: it probes each indexed key and prunes entries whose values no longer exist. The only residual failure mode — entries added out-of-band via Keychain Access.app — is narrow, documented, and addressed in v1.1 by `--rebuild-index` and/or a native `Security.framework` backend.
4. **`exec` is the documented recommendation.** `--get` exists as an escape hatch for tools that cannot read env vars; `--help` and the man page lead with `exec` and frame `--get` as "you own the exposure."

## Scope — v1

### In

- `envvault <NAMESPACE>[,<NAMESPACE>...] <cmd> [args...]` — run command with merged namespace env injected (later namespaces override on key collision, matching envchain)
- `envvault --set <NAMESPACE> <KEY> [<KEY>...]` — set one or more keys, prompting for each (stdin if piped)
- `envvault --unset <NAMESPACE> <KEY>` — **envvault extension** (envchain lacks delete)
- `envvault --get <NAMESPACE> <KEY>` — **envvault extension** (envchain lacks single-value read)
- `envvault --list` — list namespaces
- `envvault --list <NAMESPACE>` — **envvault extension** (envchain lacks key listing)
- `envvault --value <V> --set <NS> <K>` — **envvault extension**, non-interactive set with stderr warning about argv exposure
- `envvault --noecho` — accepted for envchain compat; we already default to echo-off prompts, so the flag is effectively a no-op but does not error

### Out (deferred)

| Item | Reason | Target |
|---|---|---|
| `--require-passphrase` / `--no-require-passphrase` | Cannot set Keychain ACLs via the `security` CLI; arrives naturally with the native `Security.framework` backend | v1.1 alongside native macOS |
| `--rebuild-index` | Only covers out-of-band keychain additions on macOS; rare | v1.1 |
| Import from `.env` or envchain | Not in envchain; not a cross-platform gap | v2+ |
| Export | Deliberately absent in envchain for security reasons; same stance | Never without `--i-know-what-i-am-doing` opt-in |
| Namespace rename/copy | Not in envchain | v2+ |
| Interactive browse / TUI | Not in envchain; scope inflation | Never |
| Remote backends (Key Vault, HashiCorp Vault) | Out of tool's local-single-user focus | Separate tool |

## CLI Shape

### Dispatch Rules

- If the argv contains any action flag (`--set`, `--list`, `--get`, `--unset`), use **flag mode**. The flag determines the operation; remaining positionals provide arguments.
- Otherwise, **exec mode** applies: `positional[0]` is a namespace (possibly comma-separated), and `positional[1..]` is the command and its arguments to run.
- The `--` separator is accepted in exec mode before the command name for readers who want to be explicit, but not required.

### Command Summary

```
# Exec (envchain-compatible bare form):
envvault <NAMESPACE>                    <cmd> [args...]
envvault <NAMESPACE>,<NAMESPACE>,...    <cmd> [args...]

# Set one or more keys in a namespace (prompts for each; stdin if piped):
envvault --set <NAMESPACE> <KEY> [<KEY>...]
envvault --set --noecho <NAMESPACE> <KEY>            # --noecho is a no-op but accepted for compat
envvault --value <VALUE> --set <NAMESPACE> <KEY>     # non-interactive; emits argv-exposure warning on stderr

# Retrieve:
envvault --get <NAMESPACE> <KEY>                     # extension; stderr warning when stdout is a tty
envvault --list                                      # namespaces
envvault --list <NAMESPACE>                          # extension: keys in namespace (never values)

# Delete:
envvault --unset <NAMESPACE> <KEY>                   # extension
```

### Compatibility Table (alias `envchain=envvault`)

| envchain invocation | Works under alias? |
|---|---|
| `envchain aws env` | ✓ |
| `envchain aws,hubot env` | ✓ |
| `envchain --set aws AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY` | ✓ |
| `envchain --list` | ✓ |
| `envchain --set --noecho foo BAR` | ✓ (--noecho accepted, effectively always on) |
| `envchain --require-passphrase ...` | ✗ (v1.1) |

## Security Model

| Form | argv visible? | Shell history | Shell env | Notes |
|---|---|---|---|---|
| `--set NS K` (tty) | No | No | No | Echo-off prompt. Recommended. |
| `--set NS K` (piped) | No | Depends on caller | No | `cat key.txt \| envvault --set NS K` is OK — file reads don't hit `ps`. For multi-key set via stdin, values read one per line. |
| `--value V --set NS K` | **Yes** | **Yes** | No | **stderr warning emitted.** Escape hatch for scripts. |
| `--get NS K` | No | Stdout via `$(…)` can land in history | No | stderr warning when stdout is a tty. |
| `<NS> -- cmd` (exec) | No | No | Child only | **Safest form.** The documented recommendation. |

## Architecture

### Project Structure

```
src/Winix.EnvVault/              — class library
  SubCommand.cs                 — enum: Exec, Set, Get, Unset, List
  EnvVaultOptions.cs            — parsed args
  ArgParser.cs                  — ShellKit.CommandLineParser wrapper
  NamespaceIndex.cs             — meta-entry read/update/self-heal (macOS only)
  ExecRunner.cs                 — spawn child with injected env (ProcessStartInfo.ArgumentList)
  ValuePrompt.cs                — tty echo-off prompt; stdin fallback (one line per key)
  Formatting.cs                 — list output, --describe output, warnings
  Cli.cs                        — dispatch orchestrator
src/envvault/                    — console app entry point
  Program.cs                    — thin: ConsoleEnv, ArgParser, Cli.Run, exit code
  README.md
  man/man1/envvault.1
tests/Winix.EnvVault.Tests/      — xUnit tests
```

### SecretStore Extension

`ISecretStore` gains two methods:

```csharp
/// <summary>Enumerate the keys stored under a namespace. Returns empty if the namespace has no entries.</summary>
IReadOnlyList<string> ListKeys(string namespace_);

/// <summary>Enumerate all namespaces that contain at least one entry under the given tool prefix
/// (e.g. "envvault"). The tool prefix scopes the search and keeps tools isolated in the shared keychain.</summary>
IReadOnlyList<string> ListNamespaces(string toolPrefix);
```

Per-backend implementation:

| Backend | `ListKeys` | `ListNamespaces` |
|---|---|---|
| `WindowsCredentialManagerStore` | `CredEnumerateW` with filter `"{toolPrefix}/{namespace}/*"`, parse target names | `CredEnumerateW` with filter `"{toolPrefix}/*"`, extract distinct namespace segments |
| `LinuxLibsecretStore` | `secret-tool search --all` filtered by attributes; store schema gains `tool` and `namespace` attributes | `secret-tool search --all tool {toolPrefix}`, extract distinct namespace attribute values |
| `MacOsKeychainStore` | Read meta-entry `{toolPrefix}/__meta__/{namespace}/keys`; probe each with `Get`; prune missing; rewrite if pruned; return survivors | Read meta-entry `{toolPrefix}/__meta__/namespaces`; probe each via `ListKeys`; prune empty; rewrite if pruned; return survivors |

Attribute schema for Linux (`LinuxLibsecretStore`):

```
xdg:schema = io.winix.secretstore
tool       = envvault        (or "protect", etc.)
namespace  = github
key        = GITHUB_TOKEN
```

Existing `LinuxLibsecretStore` implementation needs a light extension to tag entries with these attributes on `Set` (already takes `namespace` and `key` — the `tool` tag can be a constructor parameter so each tool passes its own prefix).

### Namespace Storage Convention

- All tools using `SecretStore` prefix their entries with `{toolName}/`. `envvault` uses `envvault/`. `protect` already uses `protect/`. This prevents cross-tool collision in the shared keychain.
- Envvault target composition: `envvault/{namespace}/{key}`.
- macOS meta-entries: `envvault/__meta__/namespaces` (list of namespaces) and `envvault/__meta__/{namespace}/keys` (list of keys in that namespace). These are envvault-private and never appear in user-facing listings.

### Write Ordering Invariant (macOS)

To ensure self-healing list can always repair desync:

- **`Set`**: update index first, write value second. A crash between leaves a phantom index entry, which self-healing list prunes on next read.
- **`Delete`**: delete value first, update index second. A crash between leaves a stale index entry, which self-healing list prunes on next read.

This invariant is captured in the ADR and should not be "optimized" away.

### Self-Healing List (macOS)

```
ListKeys(namespace):
  index = read envvault/__meta__/{ns}/keys      # may be stale
  keep = []
  for key in index:
    if Get(envvault/{ns}/{key}) != null:
      keep.append(key)
  if keep != index:
    write envvault/__meta__/{ns}/keys = keep    # prune silently
  return keep
```

Same shape for `ListNamespaces`, probing each namespace via `ListKeys` to detect empty (orphaned) namespaces and pruning.

Cost: `ListKeys` becomes O(n) Keychain reads (n = keys in namespace). Typical n < 20, read ~10ms each → ~200ms for a list. Acceptable for an interactive command. `ListNamespaces` becomes O(n_ns × n_keys_avg) — still fast for realistic workloads.

Residual uncovered failure mode: keys added directly to the keychain (e.g. via Keychain Access.app) using the envvault prefix — the index does not know to look for them. Documented in the man page; addressed in v1.1 by `--rebuild-index` or native backend.

## Distribution

Standard suite pipeline — follow the "When adding a new tool" checklist in `CLAUDE.md`:

- NuGet package `Winix.EnvVault` (dotnet global tool)
- Scoop manifest `bucket/envvault.json`
- Winget manifest generated by release pipeline
- GitHub release with AOT binaries × 4 RIDs
- `docs/ai/envvault.md` AI agent guide
- `llms.txt` entry
- Per-tool `<Description>` and `<PackageTags>` (domain tags: `env;secrets;keychain;dpapi;libsecret;envchain`)

## Testing Strategy

### Unit tests (all platforms) — target ~55-65 tests

- `ArgParser` — flag-mode vs exec-mode dispatch, multi-namespace parsing, multi-key parsing, mutual exclusion, error cases
- `EnvVaultOptions` — validation rules
- `NamespaceIndex` — read, write, self-heal-on-read via mocked `ISecretStore`, write-ordering invariants
- `ExecRunner` — env injection via `IProcessLauncher` interface (spawn mocked)
- `Formatting` — list output, --describe output, warnings
- `ValuePrompt` — tty prompt via `IConsolePrompt` interface, stdin-piped multi-key parsing

### Integration tests (platform-guarded, mirrors `protect` precedent in commit `682552a`)

- Real `SecretStore` round-trips behind `SkipUnlessWindows`/`Linux`/`MacOS` traits
- Per platform: set → list → get → exec → unset → list
- macOS-only: self-healing list (set → delete value directly, then list → observe prune)
- Windows-only: multi-namespace collision via `CredEnumerateW`
- Exec verification: spawn `cmd /c set` (Windows) or `env` (Unix) and assert injected vars appear in child stdout

## Relation to `protect` and Future Tools

- **Shares `Winix.SecretStore`.** Does not introduce new shared libraries.
- **`protect` unaffected.** `MacOsKeychainStore` gains new `ListKeys`/`ListNamespaces` methods but its existing `Set`/`Get`/`Delete` behaviour is unchanged.
- **Future `creds` tool.** When/if we build a general-purpose keychain browser, it will leverage the same `ListNamespaces`/`ListKeys` methods envvault adds to `ISecretStore`.
- **Future native macOS backend.** A v1.1 `MacOsSecurityFrameworkStore` can replace `MacOsKeychainStore` without breaking `ISecretStore` consumers. It enables `--require-passphrase` and eliminates the index-entry fallback.

## Known Limitations

1. **macOS index desync on out-of-band additions.** Users who add entries to their keychain via Keychain Access.app using the `envvault/` prefix will not see them in `envvault --list`. Self-healing list cannot detect additions, only deletions. Addressed by v1.1 `--rebuild-index` or native backend.
2. **`--require-passphrase` unavailable on v1.** Accepting the flag and failing with a clear "requires native macOS backend (v1.1)" error is preferred over silently ignoring, to prevent users from thinking they have stronger security than they do.
3. **Linux attribute-schema migration.** `LinuxLibsecretStore` must gain attribute-tagging on `Set`. Entries written before envvault (i.e., `protect`'s entries) lack these attributes. `ListNamespaces("protect")` would return empty for legacy entries until they are re-written. This is acceptable because `protect` does not currently call `ListNamespaces`, and will only gain that need after v0.4.0 ships.

## References

- envchain README: https://github.com/sorah/envchain
- `protect` design and ADR: `docs/plans/2026-04-20-protect-design.md`, `docs/plans/2026-04-20-protect-adr.md`
- `Winix.SecretStore` current interface: `src/Winix.SecretStore/ISecretStore.cs`
