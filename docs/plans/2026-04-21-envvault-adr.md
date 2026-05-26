# envvault — Architecture Decision Record

**Date:** 2026-04-21
**Status:** Accepted
**Design doc:** `docs/plans/2026-04-21-envvault-design.md`

## Context

envvault is a cross-platform keychain-backed env-var manager that fills the gap left by envchain's missing Windows backend. It ships in v0.4.0 alongside `protect`/`unprotect`, riding on the `Winix.SecretStore` shared library that those tools already established. Several design choices were non-obvious and warrant a standing record.

---

## D1. CLI shape deviates from winix subcommand-dispatch convention

### Context

Most winix tools (`schedule`, `url`, `qr`, `protect`) dispatch on `positional[0]` as a subcommand name. envvault is a clone of envchain, which uses flag-mode dispatch (`--set`, `--list`) plus a bare-positional exec form (`envchain NAMESPACE cmd...`).

### Decision

envvault mimics envchain's CLI shape directly rather than imposing winix subcommand conventions.

### Rationale

- The primary user base is Unix developers with established envchain muscle memory. `alias envchain=envvault` must work for the common invocations — this is one of the three reasons anyone would choose envvault over envchain on Unix (the others: cross-platform distribution and AOT single binary).
- The winix subcommand-dispatch convention was established for tools with no canonical predecessor. It is an internal-consistency win where there is no external UX to match. envvault has a canonical predecessor; the cost of ignoring it outweighs the internal-consistency benefit.
- Suite precedent: `less`, `man`, and `nc` also mimic their clone targets rather than impose a winix-native shape, for the same reason.

### Trade-offs accepted

- Inconsistency with newer winix tools. `envvault --set` vs `qr wifi` vs `url encode` — users learning multiple winix tools will notice different patterns.
- `--describe` output is flag-oriented rather than subcommand-hierarchical, giving slightly less structure for AI-agent consumption.
- If envvault grows in scope, the flag-namespace will saturate faster than a subcommand tree would.

### Options considered

- **Pure winix-native subcommands (`envvault set NS KEY`, `envvault exec NS -- cmd`).** Rejected: breaks envchain alias compatibility; contradicts the stated reason for building envvault.
- **Hybrid — both shapes accepted.** Rejected: surface bloat, two idioms to document and test, ambiguity if a future namespace happens to share a subcommand name.

---

## D2. Enumeration is native on Windows/Linux, index-based on macOS

### Context

envvault's `--list` requires enumeration of namespaces and keys under the `envvault/` prefix in the keychain. Each backend has different enumeration support:

- Windows Credential Manager: `CredEnumerateW` with filter — clean, native, fast
- Linux libsecret: `secret-tool search --all` with attribute filters — clean, shell-out
- macOS Keychain via `security` CLI: no clean prefix-enumeration form; the native `Security.framework` API supports it but the `security` CLI does not wrap it

### Decision

Use native enumeration on Windows and Linux. On macOS, maintain an envvault-private index (`envvault/__meta__/...`) stored as keychain entries alongside real data.

### Rationale

- Native enumeration on Windows/Linux is robust and insensitive to out-of-band keychain changes. The OS is the source of truth.
- Rewriting `MacOsKeychainStore` as a native `Security.framework` P/Invoke implementation would be a significant scope bump — CFDictionary/CFString/CFData marshaling, AOT audit, additional test surface — for a feature that can be deferred.
- The index-on-macOS approach keeps the `MacOsKeychainStore` implementation-choice (`security` CLI wrapper) intact and lets envvault proceed without a backend rewrite.
- The `ISecretStore` contract does not change when a future v1.1 macOS backend uses `Security.framework` natively — it is a pure enhancement.

### Trade-offs accepted

- macOS-only desync risk between the index and real keychain state. The index can diverge if:
  - envvault crashes mid-operation (Set or Delete)
  - The user edits keychain entries out-of-band via Keychain Access.app or another tool
- Split code paths per backend for `ListKeys`/`ListNamespaces`. Each `ISecretStore` implementation carries the enumeration strategy that suits its native capability.

### Options considered

- **Index pattern on all three backends.** Rejected: introduces desync risk on Windows and Linux where native enumeration is available and clean. Uniformity is not worth the capability regression.
- **Native everywhere via `Security.framework` P/Invoke.** Deferred to v1.1: too much scope for the current release.

---

## D3. macOS index desync is mitigated by self-healing list, not rebuild-index

### Context

With the index-based enumeration on macOS (D2), the index and real keychain state can drift. A naive fix is a manual `--rebuild-index` command. A better fix is to make `list` self-correct on every read.

### Decision

On macOS, `ListKeys` and `ListNamespaces` read the index, probe each indexed entry via `Get`, prune entries whose values no longer exist, rewrite the pruned index, and return the survivors. `--rebuild-index` is deferred to v1.1 as a narrow escape hatch for out-of-band additions.

### Rationale

- Users and scripts never observe stale entries because the very operation that would have exposed them (`list`) prunes them before returning.
- The most common failure modes — envvault crashing mid-Set/Delete, or the user deleting entries via Keychain Access.app — are caught and repaired silently.
- Only out-of-band additions (user creates a keychain entry matching the `envvault/` prefix using Keychain Access.app) remain uncovered. This is a narrow, unusual workflow.

### Trade-offs accepted

- `list` becomes O(n) keychain reads instead of O(1). Acceptable: reads are fast (~10ms), typical namespaces have fewer than 20 keys, and `list` is an interactive command.
- `list` now performs a write when pruning. Side effects on a read are slightly surprising in the abstract; in this domain they are invisible and beneficial.

### Write-ordering invariant (derived decision)

To guarantee that every desync state is one self-healing list can repair:

- `Set` updates the index first, writes the value second. A crash between them leaves a phantom index entry, which self-healing list prunes.
- `Delete` deletes the value first, updates the index second. A crash between them leaves a stale index entry, which self-healing list prunes.

Reversing either order produces an orphan value that self-healing list cannot see. This invariant is load-bearing and should not be reordered for perceived "atomicity" improvements.

### Options considered

- **Manual `--rebuild-index` only, no self-healing.** Rejected: requires users to think about and run a maintenance command. Poor UX.
- **Self-healing AND `--rebuild-index` in v1.** Deferred `--rebuild-index` to v1.1 because the remaining uncovered case (out-of-band additions) is rare.

---

## D4. `--require-passphrase` deferred with the native macOS backend

### Context

envchain supports `--require-passphrase` / `--no-require-passphrase` on macOS to control whether the Keychain prompts for unlock on each access, providing a stronger security posture for highly sensitive variables.

### Decision

Defer `--require-passphrase` to v1.1, implemented together with a native `Security.framework` backend for macOS.

### Rationale

- The underlying mechanism is a Keychain ACL on the item (`kSecAttrAccessible`, `SecAccess`). The `security` CLI does not expose item-ACL configuration in a way that lets us set per-item access control.
- Implementing `--require-passphrase` in v1 would require partial native `Security.framework` integration just for that flag, duplicating effort that belongs in the v1.1 native backend.
- Accepting the flag silently in v1 would give users false confidence that they have stronger security than they do.

### Trade-offs accepted

- envvault accepts `--require-passphrase` in its argument parser and fails with a clear "requires native macOS backend (v1.1)" error rather than silently ignoring. This is surface area that must be wired through v1 to provide the clear failure.
- Unix users who rely on this flag today in envchain will see envvault as incomplete until v1.1.

### Options considered

- **Silently accept and no-op.** Rejected: false sense of security.
- **Implement via partial `Security.framework` integration in v1.** Rejected: duplicates v1.1 work; adds marshaling cost for a single feature.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| `--rebuild-index` | Self-healing list covers most desync; out-of-band additions are a narrow case. Revisit in v1.1. |
| Native `Security.framework` backend on macOS | Scope bump (marshaling, AOT audit). Works today via `security` CLI + index. Revisit in v1.1, driven by `--require-passphrase` demand. |
| Import from `.env` or envchain | Not a gap envchain users feel. Revisit in v2+ if asked. |
| Export | Deliberately absent in envchain for security reasons. Same stance. Revisit only with `--i-know-what-i-am-doing`-style opt-in if real demand materialises. |
| Namespace rename/copy | Not in envchain. Revisit in v2+. |
| Interactive TUI | Scope inflation. Revisit never unless the tool's audience changes. |
| Remote/cloud backends (Key Vault, HashiCorp Vault) | Out of envvault's local-single-user focus. Separate tool if ever. |
