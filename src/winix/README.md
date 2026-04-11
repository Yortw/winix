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

# Install via a specific package manager
winix install --via scoop

# Preview what would happen without making changes
winix install --dry-run

# AI agent metadata
winix --describe
```

## Options

| Option | Description |
|--------|-------------|
| `--via PM` | Force a specific package manager: `scoop`, `winget`, `dotnet`, `brew` |
| `--dry-run` | Print what would be done without executing any changes |
| `--describe` | Print machine-readable metadata (flags, examples, composability) and exit |
| `--no-color` | Disable colored output |
| `--version` | Show version |
| `-h`, `--help` | Show help |

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
