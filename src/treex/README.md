# treex

Enhanced directory tree with colour, filtering, size rollups, gitignore, and clickable hyperlinks. Cross-platform `tree` replacement.

**`tree` replacement** (and works on Linux/macOS too).

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/treex
```

### Winget (Windows, stable releases)

```bash
winget install Winix.TreeX
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.TreeX
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
treex [options] [paths...]
```

Walks one or more directories (default: `.`) and renders a tree-formatted listing to stdout. Filters, sizes, and sort order are all configurable.

### Examples

```bash
# Basic tree of current directory
treex

# Show only C# files with their ancestor directories
treex src --ext cs

# Clean tree with sizes, skipping hidden and gitignored
treex --size --gitignore --no-hidden

# Sort by size to find the largest files
treex --size --sort size

# Limit to 2 levels deep
treex -d 2

# Multiple roots
treex src tests

# Structured output (NDJSON to stdout)
treex --ndjson | jq '.name'

# AI agent metadata
treex --describe
```

## Options

| Option | Description |
|--------|-------------|
| `-g`, `--glob PATTERN` | Match filenames against glob pattern (repeatable) |
| `-e`, `--regex PATTERN` | Match filenames against regex (repeatable) |
| `--ext EXT` | Match file extension, e.g. `cs`, `log` (repeatable) |
| `-t`, `--type TYPE` | Filter by type: `f` (file), `d` (directory), `l` (symlink) |
| `--min-size SIZE` | Minimum file size (e.g. `100k`, `10M`, `1G`) |
| `--max-size SIZE` | Maximum file size (e.g. `100k`, `10M`) |
| `--newer DURATION` | Modified within duration (e.g. `1h`, `30m`, `7d`) |
| `--older DURATION` | Modified before duration (e.g. `1h`, `7d`) |
| `-d`, `--max-depth N` | Maximum directory depth (0 = root only) |
| `--no-hidden` | Skip hidden files and directories |
| `--gitignore` | Respect `.gitignore` rules |
| `-i`, `--ignore-case` | Case-insensitive matching |
| `--case-sensitive` | Case-sensitive matching |
| `-s`, `--size` | Show file sizes |
| `--date` | Show last-modified dates |
| `--sort MODE` | Sort: `name` (default), `size`, `modified` |
| `-D`, `--dirs-only` | Show only directories |
| `--no-links` | Disable clickable terminal hyperlinks |
| `--ndjson` | Streaming NDJSON to stdout (one JSON object per node) |
| `--json` | JSON summary to stderr on exit |
| `--describe` | Print machine-readable metadata (flags, examples, composability) and exit |
| `--no-color` | Disable colored output |
| `--color` | Force colored output |
| `--version` | Show version |
| `-h`, `--help` | Show help |

### Size Units

`--min-size` and `--max-size` accept values with optional unit suffix: `b` (bytes), `k` (kilobytes, 1024), `M` (megabytes), `G` (gigabytes). No suffix = bytes. Examples: `500`, `10k`, `2M`, `1G`.

### Duration Units

`--newer` and `--older` accept a duration: a number followed by `s` (seconds), `m` (minutes), `h` (hours), `d` (days), `w` (weeks). Examples: `30m`, `1h`, `7d`.

## Differences from tree

| Behaviour | tree | treex |
|-----------|------|-------|
| Windows availability | Yes (DOS-era, limited) | Yes |
| Linux/macOS | Varies by distro | Yes |
| Colour | No (Windows) / Basic (Linux) | Full ANSI |
| Clickable hyperlinks | No | Yes (when terminal supports) |
| Filter by name/ext | No | `--glob`, `--ext`, `--regex` |
| Filter by size | No | `--min-size` / `--max-size` |
| Filter by date | No | `--newer` / `--older` |
| Respect .gitignore | No | `--gitignore` |
| Skip hidden | No | `--no-hidden` |
| Size rollups | No | `--size` |
| JSON output | No | `--ndjson` / `--json` |
| Sort order | Fixed | `name`, `size`, `modified` |

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Runtime error (permission denied, invalid path) |
| 125 | Usage error (bad arguments) |

## Colour

- Automatic: colour when outputting to a terminal, plain when piped
- `--color` forces colour on (overrides `NO_COLOR`)
- `--no-color` forces colour off
- Respects the `NO_COLOR` environment variable ([no-color.org](https://no-color.org))
- Clickable hyperlinks are disabled when colour is off (e.g. `--no-color`, piped output) or when `--no-links` is passed

## Part of Winix

`treex` is part of the [Winix](../../README.md) CLI toolkit.
