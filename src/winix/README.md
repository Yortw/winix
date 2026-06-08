# winix

Cross-platform installer for the Winix CLI tool suite. Installs, updates, and uninstalls all Winix tools by delegating to your platform's native package manager.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/winix
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Winix
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Winix
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
winix <command> [tools...] [options]
```

Manages all Winix tools via your platform's native package manager. On Windows, delegates to Scoop or Winget. On Linux/macOS, delegates to the appropriate system package manager or falls back to direct download.

### Commands

| Command | Description |
|---------|-------------|
| `install` | Install all Winix tools (or a subset) |
| `update` | Update all installed Winix tools |
| `uninstall` | Uninstall all Winix tools (or a subset) |
| `list` | List all available Winix tools |
| `status` | Show install status and version of each tool |
| `agents` | Write, check, or remove the Winix discoverability pointer in a project |

### Examples

```bash
# Install all Winix tools using the auto-detected package manager
winix install

# Install only specific tools
winix install timeit peep

# Update all installed tools
winix update

# Uninstall all tools
winix uninstall

# List available tools
winix list

# Show install status and versions
winix status

# Get machine-readable list output (pipeable to jq, scripts, agents)
winix list --json

# Install via a specific package manager
winix install --via scoop

# Preview what would happen without making changes
winix install --dry-run

# Write the Winix discoverability pointer into AGENTS.md (and CLAUDE.md if present)
winix agents init

# Check whether the pointer block is present and current (exit 1 if absent or stale)
winix agents status

# Remove the Winix discoverability pointer block
winix agents remove

# AI agent metadata
winix --describe
```

## Options

| Option | Description |
|--------|-------------|
| `--via PM` | Force a specific package manager: `scoop`, `winget`, `dotnet`, `brew` |
| `--dry-run` | Print what would be done without executing any changes |
| `--json` | Emit machine-readable JSON to stdout (supported on `list` and `status`) |
| `--describe` | Print machine-readable metadata (flags, examples, composability) and exit |
| `--color[=auto\|always\|never]` | Coloured output: auto (default when omitted), always, or never. |
| `--no-color` | Disable colored output |
| `--version` | Show version |
| `-h`, `--help` | Show help |

## `agents` — Project Discoverability Pointer

`winix agents <verb>` writes, checks, or removes a marker-delimited Winix discoverability block in a project's `AGENTS.md` (and `CLAUDE.md` if applicable). This lets any AI agent loading the project automatically discover that Winix tools are available and how to use them — without requiring the project maintainer to write this guidance by hand.

### Verbs

| Verb | Description |
|------|-------------|
| `init` | Write or refresh the managed block in `AGENTS.md` (and `CLAUDE.md` if it exists or `--claude` is given) |
| `status` | Report whether the block is present and current; exits 1 if absent or stale in any applicable file |
| `remove` | Strip the managed block from all applicable files |

### Agents Options

| Option | Description |
|--------|-------------|
| `--path DIR` | Project directory to operate on (default: current directory) |
| `--claude` | Also include `CLAUDE.md` even when it does not already exist |
| `--dry-run` | Show what would be written without making any changes |
| `--json` | Emit a JSON envelope on stdout instead of the plain text status |

### Exit Codes (agents subcommand)

| Code | Meaning |
|------|---------|
| 0 | Success — block is present and current (for `status`), or operation completed |
| 1 | Block is absent or stale in at least one applicable file (for `status`) |
| 125 | Usage error (invalid arguments or path is not a directory) |
| 127 | I/O failure (cannot read or write the target file) |

### Managed Block Contract

The block is delimited by HTML comments invisible in rendered Markdown:

```
<!-- winix:start v=X.Y.Z — managed by `winix agents init`; edits between markers are overwritten -->
...pointer content...
<!-- winix:end -->
```

- The opening marker embeds the version of the binary that wrote the block (the `v=` token).
- The block body embeds a version-pinned URL (`https://github.com/Yortw/winix/blob/v{version}/AGENTS.md`). Pre-release versions (version string contains `-`) fall back to `/blob/main/AGENTS.md`.
- `winix agents init` is idempotent and byte-stable: re-running at the same version produces no change to the file.
- Any edits you make between the markers are overwritten on the next `init` or `init --dry-run`. Keep project-specific guidance outside the markers.
- `winix agents status` reports `stale` when the block's `v=` version differs from the running binary, and `absent` when no valid block is found. Exit 1 in both cases.

### Usage Pattern

```bash
# Bootstrap: write the pointer if absent or stale
winix agents status --path . || winix agents init --path .

# Preview what init would write
winix agents init --path . --dry-run

# Include CLAUDE.md even if it does not yet exist
winix agents init --path . --claude

# Machine-readable status (for CI)
winix agents status --path . --json
```

## Side Effects on First `install`

The first time `winix install` runs against Scoop or Homebrew on a given machine,
`winix` automatically registers the `winix` bucket (Scoop) or the `yortw/winix`
tap (Homebrew) so the suite's tools are discoverable to the package manager. A
one-line notice is written to stderr on the registration call only:

```
winix: registered scoop bucket 'winix' (https://github.com/Yortw/winix)
```

Subsequent invocations stay silent because the bucket/tap is already present.
`--dry-run` never registers anything.

## Manifest Sources (offline-correctness)

`winix` resolves the suite manifest (the catalogue of tools and their per-package-manager identifiers) from up to three sources, in precedence order:

1. **Per-user cache** (`%LOCALAPPDATA%\winix\winix-manifest.json` on Windows; `$XDG_CACHE_HOME/winix/winix-manifest.json` or `$HOME/.cache/winix/winix-manifest.json` elsewhere) — populated by an explicit refresh; used when newer than the bundle.
2. **Bundled manifest** (`<install-dir>/share/winix/winix-manifest.json`) — every released binary ships with a current manifest, so the catalogue-lookup commands (`winix list`, `winix status`) work offline immediately after install. `winix uninstall` also resolves tool names from this manifest, but it mutates installed state by shelling to the package manager — not a query-safe command.
3. **Network fallback** — only consulted when neither local source is available (typical of dev `dotnet run` builds where the publish-output layout is absent).

Whichever local source has the later mtime wins, with one safeguard: a cache file stamped more than 5 minutes in the future of the current time is treated as untrustworthy and the bundle wins instead. This protects against a clock-skewed laptop or a restored backup pinning the user to last release's tool list indefinitely.

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success — all requested operations completed |
| 1 | One or more tools failed to install/update/uninstall |
| 125 | Usage error (bad arguments or unrecognised command) |
| 126 | Cannot execute — no supported package manager found |
| 127 | Internal error |

## Colour

- Automatic: colour when outputting to a terminal, plain when piped
- `--no-color` forces colour off
- Respects the `NO_COLOR` environment variable ([no-color.org](https://no-color.org))

## Part of Winix

`winix` is part of the [Winix](../../README.md) CLI toolkit.
