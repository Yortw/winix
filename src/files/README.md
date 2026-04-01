# files

Find files by name, size, date, type, and content. Cross-platform `find` replacement with glob patterns, text/binary detection, JSON output, and AI discoverability.

**`find` replacement** (and works on Linux/macOS too).

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/files
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Files
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Files
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
files [options] [paths...]
```

Walks one or more directories (default: `.`) and prints matching file paths to stdout — one per line by default.

### Examples

```bash
# Find C# files
files src --ext cs

# Find text files only (skip binaries)
files . --text --type f

# Recently modified files
files . --newer 1h --type f

# Detailed listing (path, size, date, type)
files . --long --ext cs

# fd-style: skip hidden and gitignored, source files only
files . --gitignore --no-hidden --ext cs

# Compose with wargs to act on results
files . --glob '*.log' | wargs rm

# Structured output (NDJSON to stdout)
files . --ndjson | jq '.name'

# JSON summary to stderr
files . --ext cs --json 2>manifest.json

# AI agent metadata
files --describe

# Files between 1 KB and 10 MB
files . --min-size 1k --max-size 10M

# Find files modified more than 7 days ago
files . --older 7d --type f

# Absolute paths (useful in scripts)
files src --absolute --ext cs

# Null-delimited output (safe for filenames with spaces)
files . --glob '*.log' --print0 | xargs -0 rm
```

## Options

| Option | Description |
|--------|-------------|
| `-g`, `--glob PATTERN` | Match filenames against glob pattern (repeatable) |
| `-e`, `--regex PATTERN` | Match filenames against regex (repeatable) |
| `--ext EXT` | Match file extension, e.g. `cs`, `log` (repeatable) |
| `-t`, `--type TYPE` | Filter by type: `f` (file), `d` (directory), `l` (symlink) |
| `--text` | Only text files |
| `--binary` | Only binary files |
| `--min-size SIZE` | Minimum file size (e.g. `100k`, `10M`, `1G`) |
| `--max-size SIZE` | Maximum file size (e.g. `100k`, `10M`) |
| `--newer DURATION` | Modified within duration (e.g. `1h`, `30m`, `7d`) |
| `--older DURATION` | Modified before duration (e.g. `1h`, `7d`) |
| `-d`, `--max-depth N` | Maximum directory depth (0 = search root only) |
| `-L`, `--follow` | Follow symlinks |
| `--absolute` | Output absolute paths |
| `--no-hidden` | Skip hidden files and directories |
| `--gitignore` | Respect `.gitignore` rules |
| `-i`, `--ignore-case` | Case-insensitive matching |
| `--case-sensitive` | Case-sensitive matching |
| `-l`, `--long` | Tab-delimited detail output (path, size, date, type) |
| `-0`, `--print0` | Null-delimited output (for `xargs -0`) |
| `--ndjson` | Streaming NDJSON to stdout (one JSON object per file) |
| `--json` | JSON summary to stderr on exit |
| `--describe` | Print machine-readable metadata (flags, examples, composability) and exit |
| `--no-color` | Disable colored output |
| `--color` | Force colored output |
| `--version` | Show version |
| `-h`, `--help` | Show help |

### Size Units

`--min-size` and `--max-size` accept values with optional unit suffix: `k` (kilobytes, 1024), `M` (megabytes), `G` (gigabytes). No suffix = bytes. Examples: `500`, `10k`, `2M`, `1G`.

### Duration Units

`--newer` and `--older` accept a duration: a number followed by `s` (seconds), `m` (minutes), `h` (hours), `d` (days), `w` (weeks). Examples: `30m`, `1h`, `7d`.

## Differences from find

| Behaviour | find | files |
|-----------|------|-------|
| Default path | Required | `.` (current directory) |
| Name matching | `-name '*.cs'` | `--glob '*.cs'` or `--ext cs` |
| Regex | `-regex` (anchored, varies by OS) | `--regex` (filename only) |
| Type filter | `-type f/d/l` | `--type f/d/l` (same) |
| Newer than | `-newer <file>` | `--newer 1h` (duration-based) |
| Size filter | `-size +1M` | `--min-size 1M` / `--max-size 1M` |
| Skip hidden | No built-in | `--no-hidden` |
| Respect .gitignore | No | `--gitignore` |
| Text/binary filter | No | `--text` / `--binary` |
| JSON output | No | `--ndjson` / `--json` |
| Windows | Not available | Yes |

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

## Part of Winix

`files` is part of the [Winix](../../README.md) CLI toolkit.
