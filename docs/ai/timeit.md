# timeit — AI Agent Guide

## What This Tool Does

`timeit` wraps a command and reports wall clock time, CPU time, peak memory, and exit code after it finishes. It is a transparent wrapper — the child's stdout, stderr, and exit code pass through unmodified, so it can be dropped into any pipeline. Use it whenever you need to measure how long something takes, especially on Windows where there is no native `time` command.

## Platform Story

Cross-platform. On **Windows**, there is no built-in `time` equivalent for commands — `timeit` fills that gap entirely. On **Unix/macOS**, the shell builtin `time` exists but lacks peak memory reporting and JSON output. `timeit` provides consistent measurement and machine-readable output across all platforms.

## When to Use This

- Timing a build to establish a baseline: `timeit dotnet build`
- Capturing CI metrics (wall time, CPU, peak RSS) in structured form: `timeit --json dotnet test`
- Comparing two implementations to see which is faster
- Diagnosing memory growth — the peak memory figure catches allocations that don't show up in wall time
- Logging a one-liner timestamp to a file: `timeit -1 dotnet publish >> build.log`

Prefer `timeit` over shell timing hacks (`date` before/after) — those give wall time only and break on Windows.

## Common Patterns

**Time a build and print results to stderr (default):**
```bash
timeit dotnet build
```

**CI pipeline — JSON to stderr, redirect to file:**
```bash
timeit --json dotnet test 2>timing.json
```

**One-line format for log files:**
```bash
timeit -1 dotnet publish -c Release
# Output: [timeit] 12.4s wall | 9.1s user | 0.300s sys | 482.0 MB peak | exit 0
```

**Capture child stdout while still timing:**
```bash
timeit dotnet test 2>/dev/null | grep "passed"
# Child stdout passes through; timeit summary is on stderr so it doesn't pollute the pipe
```

**Use -- to prevent child flags being parsed as timeit flags:**
```bash
timeit -- myapp --json --help
```

## Composing with Other Tools

**timeit + peep** — watch how a build's duration changes over time as you edit:
```bash
peep -- timeit dotnet build
```

**timeit + jq** — extract just the wall time from JSON output:
```bash
timeit --json dotnet test 2>&1 >/dev/null | jq '.wall_seconds'
```

**timeit --stdout** — write summary to stdout so it can be captured alongside child output:
```bash
timeit --stdout --json dotnet test > combined.json
```

## Gotchas

**Summary goes to stderr by default.** This is intentional — it prevents timing output from polluting piped stdout. If you need the summary in a variable or file and the child also writes to stdout, use `--stdout` or redirect stderr explicitly (`2>timing.txt`).

**CPU time can exceed wall time.** If the child uses multiple cores, CPU time is the sum of all cores and will be higher than wall time. This is expected, not a bug.

**Peak memory is the high-water mark, not average.** It reflects the largest RSS seen during the run, which may be brief.

**`--` separator is required when child args look like timeit flags.** If your command takes `--json` or `--version`, put `--` before the command to stop timeit from consuming those flags.

## Getting Structured Data

`timeit` supports JSON output via `--json`:

```bash
timeit --json dotnet test
```

JSON is written to **stderr** (use `--stdout` to redirect to stdout). Fields:

| Field | Type | Description |
|---|---|---|
| `tool` | string | Always `"timeit"`. |
| `version` | string | Tool version. |
| `exit_code` | int | **Tool's** exit code (0 = success). NOT the child's. Use this to detect timeit failures (command not found / not executable / start error). |
| `exit_reason` | string | Machine-readable: `success`, `command_not_found`, `command_not_executable`, `start_error`. |
| `child_exit_code` | int \| null | **Child process's** exit code. `null` when the child never ran (e.g. `command_not_found`). This is the value scripts care about for the program's own success/failure. |
| `wall_seconds` | float | Elapsed wall clock time in seconds. |
| `user_cpu_seconds` | float \| null | User-mode CPU time in seconds. |
| `sys_cpu_seconds` | float \| null | Kernel-mode CPU time in seconds. |
| `cpu_seconds` | float \| null | Total CPU time (`user_cpu_seconds + sys_cpu_seconds`). |
| `peak_memory_bytes` | int \| null | Peak resident set size in bytes. |

**Important: `exit_code` vs `child_exit_code`** — top-level `exit_code` reports whether *timeit itself* succeeded; the child's actual exit code is in `child_exit_code`. So a successful run of a child that exits non-zero (e.g. failing build) shows `exit_code: 0, exit_reason: "success", child_exit_code: 1`. Scripts parsing JSON should check `child_exit_code` for the program's outcome and `exit_code` only for timeit infrastructure failures.

**--describe** — machine-readable flag reference:
```bash
timeit --describe
```
