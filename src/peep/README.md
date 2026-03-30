# peep

Run a command repeatedly and display output on a refreshing screen.

Supports interval polling, file-watch triggers, diff highlighting, time-machine history, and auto-exit conditions.

**`watch` + `entr` replacement** (and works on Linux/macOS too).

## Install

```bash
dotnet tool install -g winix.peep
```

## Usage

```
peep [options] [--] <command> [args...]
```

### Examples

```bash
# Watch a command every 2 seconds (default)
peep git status

# Custom interval
peep -n 5 df -h

# Re-run on file changes (no interval polling)
peep -w "src/**/*.cs" dotnet test

# Combine interval + file watching
peep -n 10 -w "*.config" dotnet build

# Highlight differences between runs
peep -d kubectl get pods

# Exit when output changes
peep -g curl -s https://api.example.com/status

# Exit when command succeeds (exit code 0)
peep --exit-on-success -- dotnet build

# Exit when output matches a regex
peep --exit-on-match "READY" -- kubectl get pods

# Run once and display (no loop)
peep --once -- docker ps

# JSON summary on exit (for scripts)
peep --json -n 5 -- dotnet test

# Use -- when child args look like peep flags
peep -- myapp --help
```

## Interactive Controls

While running, peep responds to keyboard input:

| Key | Action |
|-----|--------|
| `q` / `Ctrl+C` | Quit |
| `Space` | Pause/unpause display |
| `r` / `Enter` | Force immediate re-run |
| `d` | Toggle diff highlighting |
| `Up`/`Down` | Scroll while paused |
| `PgUp`/`PgDn` | Scroll by page |
| `Left`/`Right` | Time travel (older/newer snapshots) |
| `t` | History overlay (browse all snapshots) |
| `?` | Show/hide help overlay |
| `Escape` | Exit time-machine or close overlay |

### Time Machine

Press `Left` to enter time-machine mode, browsing historical snapshots of command output. Use `Left`/`Right` to navigate, `t` for an overview, `Enter` to jump to a specific snapshot, and `Space` or `Escape` to return to live mode.

## Options

| Option | Description |
|--------|-------------|
| `-n`, `--interval N` | Seconds between runs (default: 2) |
| `-w`, `--watch GLOB` | Re-run on file changes matching glob (repeatable) |
| `--debounce N` | Milliseconds to debounce file changes (default: 300) |
| `--history N` | Max history snapshots to retain (default: 1000, 0=unlimited) |
| `-g`, `--exit-on-change` | Exit when output changes |
| `--exit-on-success` | Exit when command returns exit code 0 |
| `-e`, `--exit-on-error` | Exit when command returns non-zero |
| `--exit-on-match PAT` | Exit when output matches regex (repeatable) |
| `-d`, `--differences` | Highlight changed lines between runs |
| `--no-gitignore` | Disable automatic .gitignore filtering for `--watch` |
| `--once` | Run once, display, and exit |
| `-t`, `--no-header` | Hide the header lines |
| `--json` | JSON summary to stderr on exit |
| `--json-output` | Include last captured output in JSON (implies `--json`) |
| `--no-color` | Disable colored output |
| `--color` | Force colored output |
| `--version` | Show version |
| `-h`, `--help` | Show help |

### File Watching

When `--watch` is used, peep monitors for file changes matching the glob pattern. Multiple patterns can be specified. By default, files matching `.gitignore` rules are excluded (disable with `--no-gitignore`).

If only `--watch` is specified (no `-n`), interval polling is disabled — peep only re-runs on file changes. If both are specified, either trigger causes a re-run.

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Auto-exit condition met, or manual quit with last child exit 0 |
| *N* | Last child process exit code (manual quit) |
| 125 | Usage error (bad arguments) |
| 126 | Command not executable |
| 127 | Command not found |

## Colour

- Automatic: colour when outputting to a terminal, plain when piped
- `--color` forces colour on (overrides `NO_COLOR`)
- `--no-color` forces colour off
- Respects the `NO_COLOR` environment variable ([no-color.org](https://no-color.org))

## Part of Winix

`peep` is part of the [Winix](../../README.md) CLI toolkit.
