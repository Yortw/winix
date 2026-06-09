# winix — AI Agent Guide

## What This Tool Does

`winix` is the installer and lifecycle manager for the Winix CLI tool suite. It installs, updates, uninstalls, and reports the status of all Winix tools by delegating to the platform's native package manager — Scoop or Winget on Windows, Homebrew on macOS, or the system package manager on Linux. Use it when you need to provision or update the Winix suite in a single command rather than managing each tool individually.

## Platform Story

Cross-platform. On **Windows**, `winix` detects whether Scoop or Winget is available and uses it automatically; pass `--via scoop` or `--via winget` to override. On **macOS**, it delegates to Homebrew. On **Linux**, it selects the appropriate package manager or falls back to direct binary download. Use `--dry-run` on any platform to preview exactly what commands would be executed.

## When to Use This

- Provisioning a new machine with all Winix tools: `winix install`
- Installing a specific subset of tools: `winix install timeit squeeze`
- Keeping tools current: `winix update`
- Auditing what is installed and at what version: `winix status`
- Removing all tools from a machine: `winix uninstall`
- Scripting reproducible environment setup: `winix install --dry-run` to verify, then run without `--dry-run`

Prefer `winix` over invoking each tool's package manager command individually when you need consistent, idempotent management across the whole suite.

## Common Patterns

**Install all tools using the auto-detected package manager:**
```bash
winix install
```

**Install only specific tools:**
```bash
winix install timeit peep wargs
```

**Force a specific package manager:**
```bash
winix install --via scoop
winix install --via winget
```

**Preview changes without applying them:**
```bash
winix install --dry-run
winix update --dry-run
```

**Check what is installed and at what version:**
```bash
winix status
```

**Update everything:**
```bash
winix update
```

**Uninstall a specific tool:**
```bash
winix uninstall squeeze
```

## Composing with Other Tools

`winix` is a provisioning tool rather than a data-processing tool, so it does not compose via pipes in the way that `files`, `wargs`, or `treex` do. Its primary use in automation is in setup scripts and CI environments:

**CI environment setup:**
```bash
winix install --dry-run && winix install
```

**Status check in a script:**
```bash
winix status --no-color
```

## Gotchas

**Auto-detection picks the first available package manager.** On Windows machines with both Scoop and Winget installed, Scoop is preferred. Use `--via winget` to override if your organisation standardises on Winget.

**--dry-run shows package manager commands, not Winix internals.** The output shows the exact commands that would be run (e.g. `scoop install winix/timeit`), not Winix's internal logic. This is intentional — it makes the preview auditable.

**Partial failures exit with code 1.** If one tool fails to install but others succeed, exit code is 1. Check the output for per-tool status; the tools that succeeded are usable.

**update only affects installed tools.** Running `winix update` on a machine where only `timeit` is installed will update `timeit` only. It will not install tools that are absent.

**uninstall without a tool list removes all tools.** Passing `winix uninstall` with no tool names removes everything. Use `winix uninstall timeit` to target a specific tool.

**Exit code 126 means no package manager was found.** On a minimal system with no supported package manager, `winix` cannot operate. Install Scoop, Winget, or Homebrew first, or use `--via dotnet` to fall back to .NET global tool installation.

**`winix list` / `status` work offline.** Every released `winix` binary bundles the suite manifest at `<install-dir>/share/winix/winix-manifest.json`, so the catalogue-lookup commands (`list` and `status`) succeed without network access. A per-user cache layer (`%LOCALAPPDATA%\winix\` on Windows; `$XDG_CACHE_HOME/winix/` or `~/.cache/winix/` elsewhere) layers on top when an explicit refresh has happened since the binary's release. There is no automatic network refresh on `LoadAsync`; agents should not assume the manifest is up-to-the-minute. Note: `winix uninstall` mutates installed state — it consults the bundled manifest to resolve tool names, but it is not a read-only / query-safe command and should not be invoked speculatively.

## `agents` — Discoverability Pointer

`winix agents <verb>` writes, checks, or removes a marker-delimited Winix discoverability block that tells any AI agent that Winix tools are available and how to use them.

**Default scope is user/machine, not project.** With no `--project`, the block is written into the per-user agent homes (`~/.claude/CLAUDE.md`, `~/.codex/AGENTS.md`) — run once per machine, true for that machine, never committed to a repo. This is the right default: the block's content is machine-scoped (it delegates "what's installed" to the runtime `winix list` pointer), so committing it into a shared repo would make a false claim on any clone where Winix isn't installed. `--project` is an explicit opt-in for teams standardized on Winix; it writes the block into committed `AGENTS.md`/`CLAUDE.md` with **conditional** wording ("if available… if Winix is not installed, ignore this section").

### Verbs

- **`init`** — write or refresh the block. Idempotent: re-running at the same version leaves the file byte-identical. In user scope, writes every known agent home whose directory exists (or is force-created with `--claude`/`--codex`); with no home and no force flag, writes nothing and exits 125 (`no agent home found`).
- **`status`** — report whether the block is present and current. Exit 0 = current; exit 1 = absent or stale in any applicable file, or (user scope) no agent home exists.
- **`remove`** — strip the managed block from all applicable files.

Scope/target flags: `--project` (committed repo files instead of user homes), `--path DIR` (project directory, only valid with `--project`), `--claude` (force the Claude home/file even when absent), `--codex` (force the Codex user home even when absent; user scope only — cannot combine with `--project`). All verbs accept `--json`; `init` and `remove` also accept `--dry-run`.

The `WINIX_AGENTS_HOME` env var (absolute path) overrides the user-profile directory for user-scope resolution — test/smoke isolation only, so a harness never clobbers the real `~/.claude`.

### Bootstrap pattern (CI / onboarding script)

```bash
# Write the pointer to your user agent config if absent or stale; no-op if already current
winix agents status || winix agents init
```

### JSON output

- `init` and `remove` emit `{"action":"init"|"remove","dryRun":bool,"files":["path1",…]}` to stdout when `--json` is given.
- `status` emits `{"current":bool,"files":[{"path":"…","state":"current"|"stale"|"absent","version":"…"|null}]}` to stdout.

### Managed block contract

The block is delimited by HTML comments (invisible in rendered Markdown):

```
<!-- winix:start v=X.Y.Z — managed by `winix agents init`; edits between markers are overwritten -->
...pointer body...
<!-- winix:end -->
```

Key properties:

- The opening marker records the binary version (`v=X.Y.Z`). `winix agents status` compares this to the running binary's version to detect drift.
- The body embeds a version-pinned URL: `https://github.com/Yortw/winix/blob/v{version}/AGENTS.md`. Pre-release versions (the version string contains `-`) fall back to `/blob/main/AGENTS.md`.
- Re-running `init` at the same version is byte-stable (no spurious diff).
- Any text between the markers is overwritten on the next `init` run. Keep project-specific guidance outside the markers.

**Limitation (F6):** the first `<!-- winix:start … -->` … `<!-- winix:end -->` pair in the file is treated as the managed block. Do not place that literal marker pair in your own prose (e.g. as a documentation example), or `init`/`remove` will operate on it. A start marker with no matching end marker is ignored; `init` will append a fresh block after it.

### Exit codes (agents subcommand)

| Code | Meaning |
|------|---------|
| 0 | Success / block is current |
| 1 | Block absent or stale in at least one applicable file, or (user scope) no agent home exists (`status` only) |
| 125 | Usage error (bad arguments; `--path` without `--project`; `--project` with `--codex`; project path not a directory; or user-scope `init` with no agent home and no `--claude`/`--codex`) |
| 127 | I/O failure (cannot read or write a target file) |

### Known limitations

- **Multi-target writes are not atomic across files.** `init`/`remove` write each target independently; on a mid-run I/O error, already-written targets are kept and the command exits non-zero naming the failing target. Re-run to converge — the operation is idempotent.
- **The managed block records its version, not its wording mode.** A block at the current version is reported `current` even if it carries the other scope's wording (e.g. a project-mode block hand-placed in a user home). Status detects version drift, not wording drift; `remove` + `init` re-writes the correct wording.

## Getting Structured Data

**--describe** — machine-readable flag reference and metadata:
```bash
winix --describe
```

Output is JSON to stdout and always exits 0. Use this when you need to verify exact flag names, available commands, and supported package managers before constructing a command. Note: `--describe` lists `--json` as a top-level flag (inherited from the suite-wide `StandardFlags()`); `--json` is only meaningful on the `list` and `status` subcommands — install/update/uninstall ignore it.

**status command** — per-tool install state and version:
```bash
winix status --no-color
```

Output lists each tool with its installed version (or `not installed`). Suitable for parsing in scripts when combined with `--no-color` to strip ANSI codes.
