# timeit

Time a command and show wall clock, CPU time, peak memory, and exit code.

A transparent wrapper — the child's stdout, stderr, and exit code pass through unmodified.

**`time` equivalent for Windows** (and works on Linux/macOS too).

## Install

```bash
dotnet tool install -g winix.timeit
```

## Usage

```
timeit [options] [--] <command> [args...]
```

### Examples

```bash
# Time a build
timeit dotnet build

# JSON output for CI pipelines
timeit --json dotnet test

# One-line format for log files
timeit -1 dotnet publish -c Release

# Force colour even when piped
timeit --color dotnet build 2>&1 | tee build.log

# Use -- when child args look like timeit flags
timeit -- myapp --help
```

### Output Formats

**Default** (multi-line, stderr):
```
  real  12.4s
  cpu    9.1s
  peak  482 MB
  exit  0
```

**One-line** (`-1` / `--oneline`):
```
[timeit] 12.4s wall | 9.1s cpu | 482 MB peak | exit 0
```

**JSON** (`--json`):
```json
{"wall_seconds":12.4,"cpu_seconds":9.1,"peak_memory_bytes":505413632,"exit_code":0}
```

## Options

| Option | Description |
|--------|-------------|
| `-1`, `--oneline` | Single-line output format |
| `--json` | JSON output format |
| `--stdout` | Write summary to stdout instead of stderr |
| `--no-color` | Disable colored output |
| `--color` | Force colored output (even when piped) |
| `--version` | Show version |
| `-h`, `--help` | Show help |

## Exit Codes

`timeit` passes through the child process's exit code. When `timeit` itself fails:

| Code | Meaning |
|------|---------|
| 125 | No command specified or bad timeit arguments |
| 126 | Command found but not executable |
| 127 | Command not found |

## Colour

- Automatic: colour when outputting to a terminal, plain when piped
- `--color` forces colour on (overrides `NO_COLOR`)
- `--no-color` forces colour off
- Respects the `NO_COLOR` environment variable ([no-color.org](https://no-color.org))

## Part of Winix

`timeit` is part of the [Winix](../../README.md) CLI toolkit.
