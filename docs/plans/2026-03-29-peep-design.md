# peep — Watch + File Watcher

**Date:** 2026-03-29
**Status:** Design approved
**Project:** Winix (`D:\projects\winix`)
**Conventions:** `2026-03-29-winix-cli-conventions.md`

## Purpose

`peep` runs a command repeatedly — on a timer, when files change, or both — and displays the output on a refreshing terminal screen. It combines the functionality of Linux `watch` (interval polling) and `entr` (file-triggered execution) in a single cross-platform tool.

**Key differentiator:** The combined interval + file-watch mode doesn't exist in any other tool. `viddy` does interval-only. `entr` does file-watch-only. `peep` does both, in one tool, on all platforms.

## Usage

```
peep [options] [--] <command> [args...]

Run a command repeatedly and display output on a refreshing screen.

Options:
  -n, --interval N       Seconds between runs (default: 2)
  -w, --watch GLOB       Re-run on file changes matching glob (repeatable)
  --debounce N           Milliseconds to debounce file changes (default: 300)
  --exit-on-change, -g   Exit when output changes
  --exit-on-success      Exit when command returns exit code 0
  --exit-on-error        Exit when command returns non-zero
  --once                 Run once, display, and exit
  --no-header, -t        Hide the header lines
  --json                 JSON summary to stderr on exit
  --json-output          Include last captured output in JSON (implies --json)
  --no-color             Disable colored output
  --color                Force colored output
  --version              Show version
  -h, --help             Show help

Compatibility:
  These flags match watch for muscle memory:
  -n N                   Same as --interval
  -g                     Same as --exit-on-change
  -e                     Same as --exit-on-error
  -t                     Same as --no-header

Interactive:
  q / Ctrl+C             Quit
  Space                  Pause/unpause display
  r / Enter              Force immediate re-run
  Arrow keys / PgUp/Dn   Scroll while paused
  ?                      Show/hide help overlay

Exit Codes:
  0    Auto-exit condition met, or manual quit with last child exit 0
  <N>  Last child exit code (manual quit)
  125  Usage error
  126  Command not executable
  127  Command not found
```

## Modes

### Interval mode (default)

```bash
peep -- git status              # runs every 2 seconds
peep -n 5 -- kubectl get pods   # runs every 5 seconds
```

Runs the command on a fixed interval. Default 2 seconds, configurable with `-n`/`--interval`.

### File-watch mode

```bash
peep -w "src/**/*.cs" -- dotnet build
peep -w "src/**/*.cs" -w "tests/**/*.cs" -- dotnet test
```

Runs the command when files matching the glob pattern change. Multiple `-w` patterns can be specified. No interval polling — only triggers on file changes.

### Combined mode

```bash
peep -n 5 -w "src/**/*.cs" -- dotnet build
```

Runs on interval AND immediately on file change. The interval resets after a file-triggered run (don't double-run).

### Once mode

```bash
peep --once -- git log --oneline -20
```

Runs the command once, prints output to stdout (not alternate screen), exits with the child's exit code. Useful for testing and scripting. JSON output still works with `--once`.

## Display

### Alternate screen buffer

peep switches to the terminal's alternate screen buffer on start (`\x1b[?1049h`) and restores the original buffer on exit (`\x1b[?1049l`). This means:
- No flickering — the screen is cleanly redrawn each cycle
- No scroll history pollution — the user's terminal history is preserved
- Clean exit — when peep exits, the terminal looks exactly as it did before

### Header

Two-line header at the top of the screen:

```
Every 2.0s: dotnet build                  Sat Mar 29 14:32:01 [exit 0] [run #3]
Watching: src/**/*.cs
```

- Line 1: interval, command, timestamp, exit code (green for 0, red for non-zero), run counter
- Line 2: file watch patterns (only shown when `-w` is active)
- `--no-header`/`-t` hides both lines
- `[PAUSED]` indicator added to line 1 when paused

### Output rendering

- Command output (stdout + stderr combined) is captured and rendered below the header
- ANSI colour sequences are preserved — coloured command output displays correctly
- Output truncated to fit terminal height (height minus header lines) during live mode
- Terminal resize is handled — next render adapts to new dimensions
- Very small terminals (< 3 lines) show a minimal display without crashing

### Pause mode

- Spacebar toggles pause
- While paused: command keeps running on schedule, results captured but not displayed
- Header shows `[PAUSED]` indicator
- Arrow keys / PgUp / PgDn scroll through the frozen output (for content longer than terminal height)
- On unpause: immediately display the latest result, header updates to current timestamp and run count

## Output Capture

### How capture works

CommandExecutor spawns the child process with redirected stdout and stderr:

```csharp
startInfo.RedirectStandardOutput = true;
startInfo.RedirectStandardError = true;
```

Both streams are read concurrently (async tasks) to avoid deadlocks when the child writes to both. Output is merged into a single string preserving ANSI escape sequences.

### Process lifecycle

- Child process is spawned fresh for each run
- If a run is still in progress when the next trigger fires (interval, file change, or manual), the trigger is skipped (don't queue runs)
- On peep exit (q/Ctrl+C), if a child is running, it is killed before restoring the terminal

## File Watching

### Implementation

Uses `System.IO.FileSystemWatcher` — cross-platform, supports recursive watching natively on Windows (which is actually better than Linux inotify for this).

Each `-w` pattern creates a watcher on the appropriate root directory with the glob as a filter. Multiple patterns create multiple watchers.

### Debouncing

File change events are coalesced. After the first change event, wait `--debounce` milliseconds (default 300) for more events before triggering a re-run. This prevents rapid-fire triggers when build tools touch many files in quick succession.

### Interval reset

In combined mode, a file-triggered run resets the interval timer. This prevents a double-run scenario where a file change triggers a run and then the interval fires immediately after.

## Auto-Exit Modes

| Flag | Behaviour |
|------|-----------|
| `--exit-on-change` / `-g` | Exit when command output differs from previous run |
| `--exit-on-success` | Exit when command returns exit code 0 |
| `--exit-on-error` / `-e` | Exit when command returns non-zero |

Auto-exit conditions are checked after each command run. The final output is displayed before exiting (so the user sees what triggered the exit).

Use cases:
- `peep --exit-on-success -- ping -n 1 server` — wait for a server to come back
- `peep --exit-on-change -- git status` — wait for repo state to change
- `peep --exit-on-error -w "src/**/*.cs" -- dotnet build` — watch for build failures

## JSON Output

Written to stderr after peep exits (not during — the alternate screen is the live display).

### Standard session summary (`--json`)

```json
{"tool":"peep","version":"0.1.0","exit_code":0,"exit_reason":"exit_on_success","runs":12,"last_child_exit_code":0,"duration_seconds":24.500,"command":"dotnet build"}
```

Standard convention fields plus:
- `runs` — total command executions during the session
- `last_child_exit_code` — exit code of the final run
- `duration_seconds` — total wall time of the peep session
- `command` — the watched command (joined string)

`exit_reason` values: `"manual"`, `"interrupted"`, `"exit_on_change"`, `"exit_on_success"`, `"exit_on_error"`, `"once"`.

### With last output (`--json-output`)

Implies `--json`. Adds `last_output` field containing the captured text from the final run, with ANSI escape sequences stripped for clean consumption:

```json
{"tool":"peep","version":"0.1.0","exit_code":0,"exit_reason":"exit_on_success","runs":12,"last_child_exit_code":0,"duration_seconds":24.500,"command":"dotnet build","last_output":"Build succeeded.\n    0 Warning(s)\n    0 Error(s)\n"}
```

`last_output` is `null` if the command never ran (e.g. usage error before first execution).

## Error Handling

| Situation | Exit code | Exit reason |
|-----------|-----------|-------------|
| Manual quit (`q`) | Last child exit code | `manual` |
| Ctrl+C | Last child exit code | `interrupted` |
| `--exit-on-change` triggered | 0 | `exit_on_change` |
| `--exit-on-success` triggered | 0 | `exit_on_success` |
| `--exit-on-error` triggered | Last child exit code | `exit_on_error` |
| `--once` mode | Child exit code | `once` |
| Command not found | 127 | `command_not_found` |
| Command not executable | 126 | `command_not_executable` |
| Usage error | 125 | `usage_error` |

Errors before the first run (command not found, usage error) skip the alternate screen — write error to stderr and exit immediately. If `--json` is set, error JSON is written instead.

## Project Structure

```
src/
  Winix.Peep/
    Winix.Peep.csproj
    CommandExecutor.cs          ← spawn child, capture stdout+stderr, return result
    PeepResult.cs               ← result record (output text, exit code, duration, trigger source)
    IntervalScheduler.cs        ← PeriodicTimer-based scheduling with reset support
    FileWatcher.cs              ← FileSystemWatcher wrapper, glob filtering, debounce
    ScreenRenderer.cs           ← alternate buffer, header, output rendering, viewport scroll
    Formatting.cs               ← JSON output per conventions, ANSI stripping
  peep/
    peep.csproj
    Program.cs                  ← arg parsing, main event loop, key handling
tests/
  Winix.Peep.Tests/
    Winix.Peep.Tests.csproj
```

### Dependencies

- `Yort.ShellKit` — ConsoleEnv, AnsiColor, DisplayFormat
- `System.IO.FileSystemGlobbing` — glob pattern matching for file watchers (built-in NuGet, part of .NET extensions)
- No other external dependencies

## Testing Strategy

### Unit tests

- **CommandExecutor** — capture output from a trivial command, verify exit code and output text, verify ANSI sequences preserved in captured output
- **PeepResult** — record construction, computed properties
- **IntervalScheduler** — fires callbacks, can be cancelled, resets on demand
- **FileWatcher** — triggers on file change in watched path, respects glob filter, debounces rapid changes, multiple patterns work independently
- **ScreenRenderer** — header formatting (with/without file watch line, pause indicator, exit code colours), output truncation to given height, viewport offset scroll logic
- **Formatting** — JSON with convention fields, `last_output` with ANSI stripping, error JSON

### Integration tests

- `--once` mode: run a command, verify captured output and exit code
- File watcher trigger: modify a watched file, verify command re-runs
- Auto-exit: `--exit-on-success` with a succeeding command, verify peep exits with 0

### Not testing

- Actual alternate screen buffer rendering (terminal state not assertable in test harness)
- Real keyboard input handling (console app concern)
- Exact interval timing (flaky in CI)

## Explicitly Not In v1

- **No diff highlighting** (`-d`) — requires line-level diff algorithm. v2.
- **No time machine / history navigation** — snapshot storage and navigation UI. v2.
- **No exit on regex match** (`--exit-on-match`) — v2.
- **No `.gitignore`-aware file watching** — needs glob parsing infrastructure. v2.
- **No text search** (`/`) — terminal-level search (CLIo, Windows Terminal) covers this. v2+.
- **No SQLite persistence / lookback** — v3+.
- **No shell wrapping** — direct exec, consistent with Winix convention. Use `bash -c "..."` for shell features.

## Version Roadmap

**v2:** time machine (history navigation), diff highlighting (`-d`), exit on regex match (`--exit-on-match`), `.gitignore`-aware file watching, history memory limits

**v3+:** SQLite persistence/lookback, text search (`/`), word-level diff
