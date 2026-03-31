# wargs

Read items from stdin and execute a command for each one. Cross-platform xargs replacement with sane defaults.

Line-delimited by default, `{}` placeholder substitution, parallel execution, batching, dry-run, confirm, and structured output.

**`xargs` replacement** (and works on Linux/macOS too).

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/wargs
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Wargs
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Wargs
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
<input> | wargs [options] [--] <command> [args...]
```

Items are read from stdin (one per line by default) and each item is passed to the command. If the command contains `{}`, each item replaces the placeholder; otherwise items are appended as trailing arguments.

### Examples

```bash
# Simple foreach — ssh to each server
cat servers.txt | wargs ssh {} "uptime"

# Parallel — format 4 files at a time
find . -name "*.cs" | wargs -P4 dotnet format {}

# Batch — pass 10 URLs per curl invocation
cat urls.txt | wargs -n10 curl

# Dry run — see what would be executed
find . -name "*.log" | wargs --dry-run rm

# Confirm — prompt before each deletion
find . -name "*.bak" | wargs -p rm

# JSON summary to stderr
git ls-files "*.cs" | wargs --json dotnet format {}

# Null-delimited input (find -print0)
find . -name "*.tmp" -print0 | wargs -0 rm

# Default echo — no command means print items
seq 5 | wargs

# Verbose — print each command before running
cat hosts.txt | wargs -v ping -c1 {}

# Fail fast — stop on first error
cat servers.txt | wargs --fail-fast ssh {} "systemctl restart app"

# Keep order — parallel but output in input order
cat urls.txt | wargs -P4 -k curl -s {}

# NDJSON — streaming per-job results to stderr
cat servers.txt | wargs --ndjson ssh {} "uptime"
```

## How It Works

1. **Read** items from stdin (line-delimited, null-delimited, custom delimiter, or POSIX whitespace)
2. **Build** command invocations by substituting `{}` or appending items, with optional batching (`-n`)
3. **Execute** jobs sequentially or in parallel (`-P`), with configurable output buffering
4. **Report** results as human text, JSON summary, or streaming NDJSON

## Options

| Option | Description |
|--------|-------------|
| `-P`, `--parallel N` | Max concurrent jobs (default 1, 0 = unlimited) |
| `-n`, `--batch N` | Items per invocation (default 1) |
| `-0`, `--null` | Null-delimited input (for `find -print0`) |
| `-d`, `--delimiter CHAR` | Custom single-character input delimiter |
| `--compat` | POSIX whitespace splitting with quote handling |
| `--fail-fast` | Stop spawning after first child failure |
| `-k`, `--keep-order` | Print output in input order (parallel only) |
| `--line-buffered` | Children inherit stdio directly (no buffering) |
| `-p`, `--confirm` | Prompt before each job |
| `--dry-run` | Print commands without executing |
| `-v`, `--verbose` | Print each command to stderr before running |
| `--json` | JSON summary to stderr on exit |
| `--ndjson` | Streaming NDJSON per job to stderr |
| `--no-color` | Disable colored output |
| `--color` | Force colored output |
| `--version` | Show version |
| `-h`, `--help` | Show help |

## Differences from xargs

| Behaviour | xargs | wargs |
|-----------|-------|-------|
| Default delimiter | Whitespace with quote parsing | Newline (`\n`) |
| No command | `echo` | `echo` |
| Placeholder | `-I{}` required | `{}` auto-detected |
| Parallel | `-P N` | `-P N` (same) |
| Batch | `-n N` appends multiple args | `-n N` (same) |
| Fail fast | Not built-in | `--fail-fast` |
| Confirm | `-p` | `-p` (same) |
| Dry run | Not built-in | `--dry-run` |
| JSON output | Not built-in | `--json` / `--ndjson` |
| Keep order | Not built-in | `-k` / `--keep-order` |
| Verbose | `-t` | `-v` / `--verbose` |

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | All jobs succeeded |
| 123 | One or more child processes failed |
| 124 | Aborted due to `--fail-fast` |
| 125 | Usage error (bad arguments) |
| 126 | Command not executable |
| 127 | Command not found |

## Colour

- Automatic: colour when outputting to a terminal, plain when piped
- `--color` forces colour on (overrides `NO_COLOR`)
- `--no-color` forces colour off
- Respects the `NO_COLOR` environment variable ([no-color.org](https://no-color.org))

## Part of Winix

`wargs` is part of the [Winix](../../README.md) CLI toolkit.
