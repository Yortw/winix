# retry

Run a command with automatic retries on failure. Configurable retry count, delay, backoff strategy, and exit-code filtering.

A drop-in replacement for ad-hoc shell retry loops — more robust, readable, and cross-platform.

**No native retry command exists on any platform** — `retry` fills that gap everywhere.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/retry
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Retry
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Retry
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
retry [options] [--] <command> [args...]
```

### Examples

```bash
# Retry flaky tests (3 retries, 1s delay)
retry dotnet test

# More retries with longer delay
retry --times 5 --delay 2s dotnet test

# API health check with exponential backoff and jitter
retry --times 5 --delay 1s --backoff exp --jitter curl -f http://api/health

# Poll until Docker is ready (retry until exit code 0)
retry --until 0 --delay 5s docker ps

# Retry only on specific exit codes
retry --on 1,2 --times 3 make build

# Time the entire retry sequence
timeit retry make test

# File-watch with auto-retry on change
peep -- retry --times 2 make test
```

### Output

**Default** (stderr):
```
[retry] attempt 1/4 failed (exit 1) — retrying in 1s
[retry] attempt 2/4 failed (exit 1) — retrying in 1s
[retry] attempt 3/4 failed (exit 1) — retrying in 1s
[retry] attempt 4/4 failed (exit 1) — exhausted
```

**JSON** (`--json`, stderr):
```json
{"tool":"retry","version":"0.2.0","exit_code":1,"exit_reason":"exhausted","attempts":4,"child_exit_code":1}
```

## Options

| Option | Short | Default | Description |
|--------|-------|---------|-------------|
| `--times N` | `-n N` | `3` | Max retry attempts (not counting initial run) |
| `--delay D` | `-d D` | `1s` | Delay before retries (e.g. `500ms`, `2s`, `1m`) |
| `--backoff S` | `-b S` | `fixed` | Backoff strategy: `fixed`, `linear`, `exp` |
| `--jitter` | | off | Add random jitter (50–100% of computed delay) |
| `--on X,Y` | | (none) | Retry only on these exit codes |
| `--until X,Y` | | (none) | Stop retrying when exit code matches |
| `--stdout` | | off | Write summary to stdout instead of stderr |
| `--json` | | off | JSON output |
| `--color` | | auto | Force coloured output (overrides `NO_COLOR`) |
| `--no-color` | | auto | Disable coloured output |
| `--version` | `-v` | | Show version |
| `--help` | `-h` | | Show help |
| `--describe` | | | AI/agent metadata (JSON) |

### Delay Format

Delays accept a number followed by a unit suffix: `ms` (milliseconds), `s` (seconds), `m` (minutes). Examples: `500ms`, `2s`, `1m`, `90s`.

### Backoff Strategies

| Strategy | Behaviour |
|----------|-----------|
| `fixed` | Same delay between every retry |
| `linear` | Delay increases by the base delay each retry (1×, 2×, 3×, …) |
| `exp` | Delay doubles each retry (1×, 2×, 4×, 8×, …) |

Add `--jitter` to any strategy to randomise the delay within 50–100% of the computed value — useful for avoiding thundering-herd when multiple processes retry simultaneously.

### Exit-Code Filtering

`--on` and `--until` let you control exactly when retries happen:

- `--on 1,2` — only retry if the exit code is 1 or 2; pass through any other exit code immediately
- `--until 0` — keep retrying until the exit code matches (poll mode)
- These flags accept comma-separated lists of integers

## Exit Codes

`retry` passes through the child process's exit code on success, exhaustion, or non-retryable result. When `retry` itself fails:

| Code | Meaning |
|------|---------|
| 125 | Usage error — bad retry arguments |
| 126 | Command found but not executable |
| 127 | Command not found |

## Colour

- Automatic: colour when outputting to a terminal, plain when piped
- `--color` forces colour on (overrides `NO_COLOR`)
- `--no-color` forces colour off
- Respects the `NO_COLOR` environment variable ([no-color.org](https://no-color.org))

## Part of Winix

`retry` is part of the [Winix](../../README.md) CLI toolkit.
