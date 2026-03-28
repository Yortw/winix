# timeit — Command Timer

**Date:** 2026-03-28
**Status:** Design approved
**Project:** Winix (`D:\projects\winix`)
**Parent doc:** `2026-03-28-winix-design-notes.md`

## Purpose

`timeit` times how long a command takes, showing wall clock time, CPU time, peak memory, and exit code. It's a transparent wrapper — the child's stdout, stderr, and exit code pass through unmodified.

The first tool in the Winix suite. Establishes the project structure, AOT pipeline, and patterns that all subsequent tools follow.

## Usage

```
timeit [options] [--] <command> [args...]

Options:
  -1, --oneline       Single-line output format
  --json              JSON output format
  --stdout            Write summary to stdout instead of stderr
  --no-color          Disable colored output
  --color             Force colored output (even when piped)
  --version           Show version
  -h, --help          Show help
```

The `--` separator is optional. `timeit` stops parsing its own flags at the first unrecognised argument, so `timeit dotnet build` works without `--`. Use `timeit -- myapp --help` when the child's arguments could be mistaken for timeit flags.

## Output Formats

### Default (multi-line, stderr)

```
  real  12.4s
  user   9.1s
  sys    0.3s
  peak  482 MB
  exit  0
```

- Labels (`real`, `user`, `sys`, `peak`, `exit`) in dim/grey
- Values in normal brightness
- Exit code: green for 0, red for non-zero
- Written to stderr so it doesn't pollute piped command output

### One-line (`-1` / `--oneline`, stderr)

```
[timeit] 12.4s wall | 9.1s user | 0.3s sys | 482 MB peak | exit 0
```

Compact, grep-friendly, good for CI logs. Same colour rules apply.

### JSON (`--json`, stderr)

```json
{"wall_seconds":12.400,"user_cpu_seconds":9.100,"sys_cpu_seconds":0.300,"cpu_seconds":9.400,"peak_memory_bytes":505413632,"exit_code":0}
```

Raw values: seconds as float (3dp fixed), bytes as integer. `null` for any metric the OS could not provide. No colour. Machine-parseable.

### `--stdout` flag

All three formats default to stderr. The `--stdout` flag redirects timeit's summary to stdout instead. Useful when you want to capture timing data in a pipe and don't care about the command's stdout.

## Time Formatting

Human-friendly, auto-scaling:

| Duration | Format |
|----------|--------|
| < 1s | `0.842s` |
| 1s – 60s | `12.4s` |
| 1m – 60m | `3m 27.1s` |
| > 60m | `1h 12m 03s` |

## Memory Formatting

| Size | Format |
|------|--------|
| < 1 MB | `384 KB` |
| 1 MB – 1 GB | `482 MB` |
| > 1 GB | `2.3 GB` |

JSON always uses raw bytes (integer) regardless of magnitude.

## Process Execution

### How it works

1. Parse arguments — separate timeit flags from child command
2. Start `Stopwatch` immediately before `Process.Start()`
3. Spawn child via `System.Diagnostics.Process`:
   - `UseShellExecute = false`
   - Inherit stdin, stdout, stderr directly (no redirection, no buffering)
   - The child writes to the terminal exactly as if timeit wasn't there
4. `WaitForExit()` — block until child completes
5. Read metrics via platform-native APIs (`NativeMetrics.GetMetrics`), read `Process.ExitCode`
6. Write summary to stderr (or stdout with `--stdout`)
7. Exit with the child's exit code

### Metrics collected

| Metric | Source | Notes |
|--------|--------|-------|
| Wall clock | `System.Diagnostics.Stopwatch` | High-resolution timer |
| User CPU time | Platform-native API | `GetProcessTimes` (Windows), `getrusage(RUSAGE_CHILDREN)` (Linux/macOS) |
| System CPU time | Platform-native API | Same APIs as user CPU |
| Peak memory | Platform-native API | `GetProcessMemoryInfo` (Windows), `getrusage` `ru_maxrss` (Linux/macOS) |
| Exit code | `Process.ExitCode` | Passed through as timeit's own exit code |

### Cross-platform

Platform-native APIs via `LibraryImport` (source-generated P/Invoke, AOT-compatible):
- **Windows:** `GetProcessTimes` + `GetProcessMemoryInfo` via process handle (reliable after `WaitForExit()`, handle keeps kernel object alive)
- **Linux:** `getrusage(RUSAGE_CHILDREN)` via `libc` (works post-reap by design, `ru_maxrss` in KB)
- **macOS:** `getrusage(RUSAGE_CHILDREN)` via `libSystem` (same approach, `ru_maxrss` in bytes)

All metric fields are nullable — `null` means the OS could not provide the value (rare in practice). Human output shows `N/A`, JSON shows `null`. This is distinct from zero, which is a valid measurement.

## Exit Codes

`timeit` passes through the child's exit code. When timeit itself fails:

| Situation | Exit code | Convention |
|-----------|-----------|------------|
| Child ran and exited | Child's exit code | Pass-through |
| Command not found | 127 | POSIX shell convention |
| Command not executable (permission denied) | 126 | POSIX shell convention |
| No command given / bad timeit args | 125 | timeit-specific |

All timeit-originated errors include a `timeit:` prefixed message on stderr to distinguish from child output. This is the only way to disambiguate when a child happens to exit with 126/127.

## Terminal Awareness

- Detect whether stderr (the summary output stream) is a terminal or pipe
- Terminal → colours enabled (dim labels, green/red exit code)
- Pipe → no colour, no ANSI sequences
- `--color` forces colour on (even when piped). Overrides `NO_COLOR` env var.
- `--no-color` forces colour off (even on terminal)
- `NO_COLOR` environment variable respected (see no-color.org) — treated as `--no-color` unless `--color` is explicitly passed
- Precedence: explicit flag (`--color` / `--no-color`) > `NO_COLOR` env var > auto-detection
- No OSC 8 hyperlinks in this tool — nothing meaningful to link to

Implementation: inline `ConsoleEnv` helper class (proto-Yort.ShellKit). Will be extracted to the shared library when the second tool is built.

## Project Structure

```
winix/
├── src/
│   ├── Winix.TimeIt/              ← class library (all logic)
│   │   ├── Winix.TimeIt.csproj
│   │   ├── CommandRunner.cs        ← spawn process, collect metrics
│   │   ├── NativeMetrics.cs        ← platform dispatch + shared types
│   │   ├── NativeMetrics.Windows.cs ← GetProcessTimes / GetProcessMemoryInfo
│   │   ├── NativeMetrics.Linux.cs  ← getrusage(RUSAGE_CHILDREN) via libc
│   │   ├── NativeMetrics.MacOS.cs  ← getrusage(RUSAGE_CHILDREN) via libSystem
│   │   ├── ResultFormatter.cs      ← format output (default/oneline/json)
│   │   ├── ConsoleEnv.cs           ← color/terminal detection (proto-Yort.ShellKit)
│   │   └── TimeItResult.cs         ← data record for timing results
│   └── timeit/                     ← thin console app
│       ├── timeit.csproj           ← PackAsTool, AOT, references Winix.TimeIt
│       └── Program.cs              ← arg parsing, call library, set exit code
├── tests/
│   └── Winix.TimeIt.Tests/
│       ├── Winix.TimeIt.Tests.csproj
│       ├── ResultFormatterTests.cs  ← formatting logic tests
│       └── CommandRunnerTests.cs    ← process spawning tests
├── Directory.Build.props           ← shared version, trimming, analyzers
└── Winix.sln
```

### Key .csproj settings for timeit console app

- `<PackAsTool>true</PackAsTool>` — publishable as dotnet tool from day one
- `<PublishAot>true</PublishAot>` — AOT native compilation
- `<IsTrimmable>true</IsTrimmable>` + `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>` on class library
- `<InvariantGlobalization>true</InvariantGlobalization>` — reduces AOT binary size, no culture-specific formatting needed
- Target: `net10.0` (LTS)

### Directory.Build.props

Shared across all projects in the solution:
- Version: single source of truth for all tools
- Nullable reference types: enabled
- Treat warnings as errors: enabled
- Trim analyzers: enabled

## Explicitly Not In v1

- **No `--compare` or benchmarking mode** — that's hyperfine's territory
- **No `--repeat N`** — same reasoning
- **No shell wrapping** — timeit spawns the process directly via `Process.Start()`, not via `cmd /c` or `bash -c`. Shell features (pipes, redirects) in the timed command require explicit shell invocation: `timeit bash -c "sort file | uniq"`
- **No Yort.ShellKit dependency** — first tool bootstraps inline, extract shared code when building tool #2

## Testing Strategy

- **ResultFormatter tests** — verify all three output formats, time/memory auto-scaling, edge cases (0 seconds, very large values, negative exit codes)
- **CommandRunner tests** — verify process spawning, exit code pass-through, error cases (command not found, permission denied). These are integration tests that spawn real (trivial) child processes
- **ConsoleEnv tests** — verify colour detection logic, `NO_COLOR` handling

Keep tests pragmatic — focus on formatting correctness and exit code pass-through, which are the most likely sources of bugs.
