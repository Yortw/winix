# winix — Cross-Platform Suite Installer

**Date:** 2026-04-05
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)

---

## Overview

`winix` is a cross-platform CLI tool that installs, updates, and uninstalls the other Winix tools by delegating to the platform's native package manager. It is stateless — it does not track what it installed; it queries the underlying package manager for truth.

**Why?** Installing the Winix suite today requires multiple commands per platform:
- Scoop has `winix.json` (all-in-one), but only for Scoop users
- Winget's dependency system doesn't work with portable installers — each tool is a separate `winget install`
- NuGet requires 6 separate `dotnet tool install` commands
- macOS/Linux have no native installer at all (Homebrew tap doesn't exist yet)

`winix` reduces this to one command on any platform: `winix install`.

**Why not a multi-call binary?** `winix timeit ...` adds noise for no value, especially mid-pipeline. Tools stay independently invocable. `winix` manages the suite; it doesn't wrap it.

---

## Project Structure

```
src/Winix.Winix/            ← class library (PM adapters, manifest, orchestration)
src/winix/                  ← thin console app (arg parsing, call library, exit code)
tests/Winix.Winix.Tests/    ← xUnit tests
```

Follows standard Winix conventions: library does all work, console app is thin, ShellKit provides arg parsing and terminal detection.

---

## Data Flow

```
user command (install/update/uninstall/list/status)
  → parse args (tool names, --via, --dry-run)
  → fetch manifest from GitHub releases
  → detect available package managers on PATH
  → select PM (--via override, or platform default chain)
  → for each target tool:
      → resolve package ID from manifest for selected PM
      → shell out to PM (install/update/uninstall/query)
      → capture exit code + stderr
      → report result to user
  → aggregate exit code (0 = all ok, 1 = partial failure)
```

---

## Command Surface

```
winix install [tool...]         # install all tools, or specific ones
winix update [tool...]          # update all installed tools, or specific ones
winix uninstall [tool...]       # uninstall all tools, or specific ones
winix list                      # show available tools, installed status, version, which PM
winix status                    # short summary: X of Y installed, via Z
```

When no tool names are given, `install`/`update`/`uninstall` operate on all tools.

### Flags

| Flag | Description |
|------|-------------|
| `--via <pm>` | Override package manager: `winget`, `scoop`, `brew`, `dotnet` |
| `--dry-run` | Show what would be executed, don't run anything |
| `--describe` | AI discoverability metadata (JSON, consistent with all tools) |
| `--no-color` | Disable colour output (also respects `NO_COLOR` env var) |

### Output

- `install`/`update`/`uninstall` stream each tool as it's processed:
  ```
  ✓ timeit (via winget)
  ✓ squeeze (via winget)
  ✗ peep — winget returned exit code 1: <stderr snippet>
  ✓ wargs (via winget)
  ```
- `list` outputs a table: tool name, description, installed (yes/no), version, which PM owns it
- `status` outputs a short summary: `4 of 6 tools installed (3 via winget, 1 via dotnet)`
- `--via` only affects mutation commands (`install`, `update`, `uninstall`). `list` and `status` always probe all PMs to show the full picture.
- All summary/progress goes to stderr (don't pollute piped output)

### Exit Codes

| Code | Meaning |
|------|---------|
| 0 | All operations succeeded |
| 1 | One or more tools failed (partial success) |
| 125 | winix usage error (bad args) |
| 126 | Cannot execute (PM not found, network error) |
| 127 | winix internal error |

---

## Package Manager Adapters

Each PM gets an adapter implementing a common interface:

```csharp
public interface IPackageManagerAdapter
{
    string Name { get; }
    bool IsAvailable();
    bool IsInstalled(string packageId);
    string? GetInstalledVersion(string packageId);
    Task<int> Install(string packageId);
    Task<int> Update(string packageId);
    Task<int> Uninstall(string packageId);
}
```

### Adapter Details

| Adapter | Install | Check Installed | Update | Uninstall |
|---------|---------|-----------------|--------|-----------|
| **Winget** | `winget install --id X --exact --accept-source-agreements` | `winget list --id X --exact` | `winget upgrade --id X --exact` | `winget uninstall --id X --exact` |
| **Scoop** | `scoop install X` | `scoop list X` | `scoop update X` | `scoop uninstall X` |
| **Brew** | `brew install yortw/winix/X` | `brew list X` | `brew upgrade X` | `brew uninstall X` |
| **Dotnet** | `dotnet tool install -g Winix.X` | `dotnet tool list -g` (parse output) | `dotnet tool update -g Winix.X` | `dotnet tool uninstall -g Winix.X` |

All process execution uses `ProcessStartInfo.ArgumentList` (project convention). Stdout/stderr from the child PM is captured and only surfaced on error or in verbose mode.

### Querying Installed State

`list` and `status` need to determine which PM owns each tool. Strategy: probe PMs in the platform default chain order and report the first one that claims the tool is installed. If a tool is installed via multiple PMs simultaneously (unlikely but possible), only the first-found PM is reported — `winix` doesn't attempt to reconcile duplicate installations.

### Platform Default Chain

When `--via` is not specified, the first available PM in the chain is selected:

| Platform | Chain |
|----------|-------|
| **Windows** | winget → scoop → dotnet → fail |
| **macOS** | brew → dotnet → fail |
| **Linux** | dotnet → fail |

"Available" means the PM's executable is found on PATH.

If no PM is found, exit with code 126 and a message listing how to install a supported PM.

### Auto-Setup

- **Scoop:** If the Winix bucket is not added, `winix install` adds it automatically (`scoop bucket add winix https://github.com/Yortw/winix`).
- **Brew:** If the `yortw/winix` tap is not added, `winix install` taps it automatically (`brew tap yortw/winix`).
- Other PMs need no setup.

---

## Manifest

The manifest is a JSON file hosted as a GitHub release asset (`winix-manifest.json`), generated by the release pipeline. It lists all tools and their package IDs per PM.

```json
{
  "version": "0.2.0",
  "tools": {
    "timeit": {
      "description": "Time a command — wall clock, CPU time, peak memory, exit code.",
      "packages": {
        "winget": "Winix.TimeIt",
        "scoop": "timeit",
        "brew": "timeit",
        "dotnet": "Winix.TimeIt"
      }
    },
    "squeeze": {
      "description": "Compress and decompress files — gzip, brotli, zstd.",
      "packages": {
        "winget": "Winix.Squeeze",
        "scoop": "squeeze",
        "brew": "squeeze",
        "dotnet": "Winix.Squeeze"
      }
    },
    "peep": {
      "description": "Watch commands and files for changes.",
      "packages": {
        "winget": "Winix.Peep",
        "scoop": "peep",
        "brew": "peep",
        "dotnet": "Winix.Peep"
      }
    },
    "wargs": {
      "description": "Cross-platform xargs replacement with sane defaults.",
      "packages": {
        "winget": "Winix.Wargs",
        "scoop": "wargs",
        "brew": "wargs",
        "dotnet": "Winix.Wargs"
      }
    },
    "files": {
      "description": "Cross-platform file finder with glob, regex, and filters.",
      "packages": {
        "winget": "Winix.Files",
        "scoop": "files",
        "brew": "files",
        "dotnet": "Winix.Files"
      }
    },
    "treex": {
      "description": "Visual directory tree with colour, filtering, and size rollups.",
      "packages": {
        "winget": "Winix.TreeX",
        "scoop": "treex",
        "brew": "treex",
        "dotnet": "Winix.TreeX"
      }
    }
  }
}
```

### Manifest Fetch

- `winix` fetches the manifest from the latest GitHub release on every `install`/`update`/`list`/`status` invocation.
- URL: `https://github.com/Yortw/winix/releases/latest/download/winix-manifest.json`
- No caching in v1. The manifest is small (<1 KB) and these commands are infrequent.
- On network failure, exit with code 126 and a clear error message.

### Manifest Generation

The release pipeline generates `winix-manifest.json` from the tool list already defined in the workflow. It's uploaded as a release asset alongside the per-tool zips. Adding a new tool means adding it to the manifest generation step — same as scoop/winget today.

---

## Post-Install Hooks

On platforms with post-install support, installing `winix` automatically installs all tools.

### Scoop

`bucket/winix.json` gains a `post_install` script:
```json
"post_install": "winix install --via scoop"
```

Note: this replaces the current `winix.json` behaviour of bundling all binaries in one zip. The combined zip approach is removed — `winix` is now the all-in-one mechanism, and each tool is installed individually via Scoop.

### Homebrew

The formula gets a `post_install` block:
```ruby
def post_install
  system bin/"winix", "install", "--via", "brew"
end
```

### Winget / dotnet tool

No hook mechanism. On first run, if zero tools are installed, `winix` prints to stderr:
```
No Winix tools installed. Run 'winix install' to install all tools.
```

This hint appears only when running `list` or `status` with nothing installed. No unsolicited auto-install.

### Post-Install Failure

The hooks pass `--via` explicitly, so PM selection is deterministic. If the post-install fails (network down, PM error), `winix` is still installed and the user can retry with `winix install`.

---

## Self-Update

`winix` does **not** manage itself. `winix update` updates the other tools, not `winix`. Updating `winix` is done through however you installed it:
- `scoop update winix`
- `winget upgrade Winix.Winix`
- `brew upgrade yortw/winix/winix`
- `dotnet tool update -g Winix.Winix`

This avoids the "replace yourself while running" problem and keeps the tool simple.

---

## Testing Strategy

### Unit Tests (class library, no process spawning)

- **Manifest parsing** — valid JSON, missing fields, unknown tools, version handling
- **PM detection logic** — mock which PMs are "available", verify fallback chain order per platform
- **PM selection with `--via`** — verify it skips the chain and uses the specified PM
- **`list`/`status` output formatting** — given tool states, verify table/summary output
- **Argument parsing** — all command/flag combinations, invalid inputs
- **Exit code logic** — all succeed → 0, some fail → 1, winix error → 125/126/127
- **Dry-run mode** — verify commands are reported but not executed

### Integration Tests (fake PM scripts)

Each adapter is tested against a fake PM — a small script placed on PATH during tests that mimics the real PM's responses (exit codes, stdout patterns). This verifies:
- Process spawning and argument passing
- Output parsing (version extraction, installed-check parsing)
- Error handling (non-zero exit, stderr capture)
- Auto-setup (bucket add / tap detection)

The fake PMs are test-only scripts, not shipped.

### Not Tested

Actual package installation — that's the PM's job. We test that we call the PM correctly and handle its responses correctly.

---

## Non-Goals (v1)

| Item | Reason |
|------|--------|
| Multi-call binary | `winix` manages the suite, it doesn't wrap it |
| Self-update | Update via whatever PM you used to install winix |
| Version pinning (`timeit@0.1.0`) | Install latest only; add pinning if needed later |
| Offline/cached installs | Delegates to PMs which have their own caching |
| apt/snap adapter | Linux starts with `dotnet` only; add native PM adapters on demand |
| Homebrew formula creation | Adapter is ready; creating the tap is a release pipeline task |
| Manifest caching | Manifest is tiny, fetched infrequently; add ETags later if needed |

---

## Files to Create/Modify

| File | Action |
|------|--------|
| `src/Winix.Winix/Winix.Winix.csproj` | Create — class library |
| `src/Winix.Winix/*.cs` | Create — adapters, manifest, orchestration |
| `src/winix/winix.csproj` | Create — console app |
| `src/winix/Program.cs` | Create — thin entry point |
| `tests/Winix.Winix.Tests/` | Create — xUnit tests |
| `Winix.sln` | Modify — add new projects |
| `bucket/winix.json` | Modify — replace combined-zip approach with post_install |
| `.github/workflows/release.yml` | Modify — add manifest generation, add winix to publish steps |
| `src/winix/README.md` | Create — tool documentation |
| `docs/ai/winix.md` | Create — AI agent guide |
| `llms.txt` | Modify — add winix entry |
| `CLAUDE.md` | Modify — add winix to project layout and conventions |
