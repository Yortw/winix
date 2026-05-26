# retry — Run a Command With Automatic Retries

**Date:** 2026-04-16
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)

---

## Overview

`retry` runs a command and automatically retries it on failure, with configurable retry count, delay, backoff strategy, and exit-code filtering.

**Why it's needed:** Every platform has ad-hoc retry loops: bash `until`/`while` with `sleep`, PowerShell `do/while`, or one-off wrapper scripts. None are portable, composable, or handle backoff properly. `retry` replaces all of them with a single cross-platform tool.

**Primary use cases:**
- Flaky tests: `retry --times 3 dotnet test`
- Hardware probes: `retry --times 10 --delay 2s adb devices`
- Service polling: `retry --until 0 --delay 5s docker ps`
- API retries: `retry --times 5 --delay 1s --backoff exp --jitter curl -f https://api.example.com/health`

**Platform:** Cross-platform (Windows, Linux, macOS). No platform-specific code — pure process spawning.

---

## Project Structure

```
src/Winix.Retry/        — class library (retry loop, backoff, formatting)
src/retry/              — thin console app (arg parsing via ShellKit, call library, exit code)
tests/Winix.Retry.Tests/ — xUnit tests
```

Standard Winix conventions: library does all work, console app is thin.

---

## CLI Interface

```
retry [options] [--] <command> [args...]
```

### Options

| Flag | Short | Default | Description |
|------|-------|---------|-------------|
| `--times N` | `-n N` | `3` | Max retry attempts (not counting the initial run). Total attempts = N + 1. |
| `--delay D` | `-d D` | `1s` | Delay before first retry. Duration string via `DurationParser` (`500ms`, `2s`, `1m30s`). |
| `--backoff S` | `-b S` | `fixed` | Backoff strategy: `fixed`, `linear`, `exp`. |
| `--jitter` | | off | Add random jitter to delay (multiply by random factor in [0.5, 1.0)). Works with any backoff strategy. |
| `--on X,Y` | | (none) | Retry **only** on these exit codes. Any other non-zero code stops immediately. |
| `--until X,Y` | | (none) | Stop when exit code matches one of these. Retry on everything else. |
| `--json` | | off | JSON summary to output stream after completion. |
| `--color`/`--no-color` | | auto | Colour control. Respects `NO_COLOR` env var. |
| `--stdout` | | off | Write summary to stdout instead of stderr. |
| `--help` | `-h` | | Help text. |
| `--version` | `-v` | | Version. |
| `--describe` | | | AI-readable tool description. |

### Exit-Code Filtering Rules

- **No flags (default):** Retry on any non-zero exit code. Equivalent to `--until 0`.
- **`--on X,Y`:** Retry only when exit code is in the set. Exit 0 always stops (success). Any non-zero code not in the set also stops immediately (not retryable).
- **`--until X,Y`:** Stop when exit code is in the set. Everything else (including 0, if 0 is not listed) triggers a retry.
- **`--on` and `--until` together:** Usage error — they are contradictory.

The `--until` behaviour with 0 is deliberate: `--until 1` means "keep retrying until exit code 1" — exit 0 would trigger another attempt. Users who want to stop on both 0 and 1 write `--until 0,1`. The default (no flags) is the common case of "retry until success."

### Backoff Calculation

Given base delay `D` and attempt number `a` (1-indexed, where 1 = first retry):

- **`fixed`:** delay = D
- **`linear`:** delay = D × a
- **`exp`:** delay = D × 2^(a−1)

With `--jitter`: delay = calculated_delay × random([0.5, 1.0))

No maximum delay cap in v1. If needed, `--max-delay` can be added later.

---

## Output

Child stdout and stderr pass through unmodified. Retry status and summary go to stderr by default (`--stdout` redirects to stdout).

### Progress Lines (stderr, human mode)

Printed between attempts:

```
retry: attempt 1/4 failed (exit 1), retrying in 2s...
retry: attempt 2/4 failed (exit 1), retrying in 2s...
retry: attempt 3/4 succeeded (exit 0) after 3 attempts
```

On exhaustion:
```
retry: attempt 4/4 failed (exit 1), no retries remaining
```

On non-retryable code (with `--on`):
```
retry: attempt 1/4 failed (exit 137), not retryable — stopping
```

On `--until` target hit:
```
retry: attempt 2/4 matched target (exit 1) after 2 attempts
```

The "N/M" format counts total attempts (initial + retries), so `--times 3` gives attempts 1/4 through 4/4.

### JSON Output

With `--json`, a JSON object is written to the output stream after the final attempt:

```json
{
  "tool": "retry",
  "version": "0.3.0",
  "exit_code": 1,
  "exit_reason": "retries_exhausted",
  "child_exit_code": 1,
  "attempts": 4,
  "max_attempts": 4,
  "total_seconds": 12.34,
  "delays_seconds": [2.0, 2.0, 2.0]
}
```

**`exit_reason` values:**

| Value | Meaning |
|-------|---------|
| `succeeded` | Command exited 0 (default mode) or matched `--until` target |
| `retries_exhausted` | All attempts used, command still failing |
| `not_retryable` | Exit code not in `--on` set — stopped early |
| `command_not_found` | Command not found on PATH |
| `command_not_executable` | Permission denied |
| `usage_error` | Bad arguments to retry itself |

**Error JSON** (for retry's own errors):

```json
{
  "tool": "retry",
  "version": "0.3.0",
  "exit_code": 127,
  "exit_reason": "command_not_found",
  "child_exit_code": null,
  "attempts": 0,
  "max_attempts": 4,
  "total_seconds": 0.0,
  "delays_seconds": []
}
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| (child) | Child exit code on success, exhaustion, or non-retryable stop. Retry is transparent. |
| 125 | Usage error (bad retry arguments, `--on` + `--until` together) |
| 126 | Command not executable (permission denied) |
| 127 | Command not found |

On success: child exit code (usually 0, but could be non-zero with `--until`).
On exhaustion: last child exit code.
On non-retryable: the non-retryable child exit code.
On `--until` match: the matched exit code.

This makes retry transparent in pipelines — callers see the child's exit code.

---

## Components

### RetryOptions

Configuration record consumed by the runner.

```csharp
public sealed class RetryOptions
{
    /// <summary>Maximum number of retry attempts (not counting the initial run).</summary>
    public int MaxRetries { get; }

    /// <summary>Base delay before retries.</summary>
    public TimeSpan Delay { get; }

    /// <summary>Backoff strategy (fixed, linear, exponential).</summary>
    public BackoffStrategy Backoff { get; }

    /// <summary>Whether to add jitter to delay calculations.</summary>
    public bool Jitter { get; }

    /// <summary>
    /// Exit codes that trigger a retry (--on). Null means retry on any non-zero.
    /// </summary>
    public IReadOnlySet<int>? RetryOnCodes { get; }

    /// <summary>
    /// Exit codes that stop retrying (--until). Null means use default (stop on 0).
    /// </summary>
    public IReadOnlySet<int>? StopOnCodes { get; }
}
```

### BackoffStrategy

```csharp
public enum BackoffStrategy
{
    Fixed,
    Linear,
    Exponential
}
```

### BackoffCalculator

Static helper. Pure function, easily testable.

```csharp
public static class BackoffCalculator
{
    /// <summary>
    /// Calculates the delay for a given retry attempt.
    /// </summary>
    /// <param name="baseDelay">The base delay from options.</param>
    /// <param name="attempt">The retry attempt number (1-indexed).</param>
    /// <param name="strategy">The backoff strategy.</param>
    /// <param name="jitter">Whether to add random jitter.</param>
    /// <param name="random">Random instance for jitter. Null if jitter is disabled.</param>
    public static TimeSpan Calculate(TimeSpan baseDelay, int attempt,
        BackoffStrategy strategy, bool jitter, Random? random);
}
```

### RetryResult

Returned by the runner after all attempts complete.

```csharp
public sealed class RetryResult
{
    /// <summary>Total number of attempts made (initial + retries).</summary>
    public int Attempts { get; }

    /// <summary>Maximum attempts that were allowed (MaxRetries + 1).</summary>
    public int MaxAttempts { get; }

    /// <summary>Exit code of the last child process run.</summary>
    public int ChildExitCode { get; }

    /// <summary>How the retry loop terminated.</summary>
    public RetryOutcome Outcome { get; }

    /// <summary>Total wall time including delays.</summary>
    public TimeSpan TotalTime { get; }

    /// <summary>Actual delays between attempts (for JSON output).</summary>
    public IReadOnlyList<TimeSpan> Delays { get; }
}
```

### RetryOutcome

```csharp
public enum RetryOutcome
{
    Succeeded,
    RetriesExhausted,
    NotRetryable
}
```

### RetryRunner

Orchestrates the retry loop. Reports progress via a callback so the console app can print status lines in real time.

```csharp
public sealed class RetryRunner
{
    /// <summary>
    /// Runs the command with retries according to the specified options.
    /// </summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="arguments">Arguments for the command.</param>
    /// <param name="options">Retry configuration.</param>
    /// <param name="onAttempt">
    /// Callback invoked after each attempt with (attemptNumber, maxAttempts, exitCode, nextDelay).
    /// nextDelay is null on the final attempt.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for aborting the retry loop.</param>
    public RetryResult Run(string command, string[] arguments,
        RetryOptions options, Action<AttemptInfo>? onAttempt = null,
        CancellationToken cancellationToken = default);
}
```

### AttemptInfo

```csharp
public sealed class AttemptInfo
{
    /// <summary>Which attempt just completed (1-indexed).</summary>
    public int Attempt { get; }

    /// <summary>Total max attempts.</summary>
    public int MaxAttempts { get; }

    /// <summary>Exit code from this attempt.</summary>
    public int ExitCode { get; }

    /// <summary>Delay before next attempt, or null if this was the final attempt.</summary>
    public TimeSpan? NextDelay { get; }

    /// <summary>Whether this attempt will trigger a retry.</summary>
    public bool WillRetry { get; }

    /// <summary>Why the loop is stopping (if not retrying).</summary>
    public RetryOutcome? StopReason { get; }
}
```

### Formatting

Static class producing human-readable and JSON output strings. Same pattern as `Winix.TimeIt.Formatting`.

```csharp
public static class Formatting
{
    /// <summary>Format a progress line for a single attempt (printed between attempts).</summary>
    public static string FormatAttempt(AttemptInfo info, bool useColor);

    /// <summary>Format the final JSON summary.</summary>
    public static string FormatJson(RetryResult result, string toolName, string version);

    /// <summary>Format a JSON error for retry's own failures.</summary>
    public static string FormatJsonError(int exitCode, string exitReason,
        string toolName, string version);
}
```

---

## Process Execution

Retry spawns child processes the same way timeit does: `ProcessStartInfo` with `ArgumentList`, inherited stdin/stdout/stderr. No output redirection — the child talks directly to the terminal.

The `RetryRunner` uses `Thread.Sleep` (not `Task.Delay`) for delays since there's no async work to overlap — the tool blocks on one child process at a time. This keeps the implementation synchronous and avoids async colouring the library API.

CancellationToken support enables clean Ctrl+C handling: the console app hooks `Console.CancelKeyPress`, signals the token, and the runner breaks out of the loop after the current attempt finishes.

---

## Composability

| Composition | Example | Effect |
|-------------|---------|--------|
| timeit + retry | `timeit retry make test` | Time the entire retry sequence |
| retry + backoff | `retry --times 5 --delay 1s --backoff exp --jitter curl -f ...` | API health check with exponential backoff + jitter |
| peep + retry | `peep -- retry --times 2 make test` | File-watch with auto-retry on failure |
| retry as poller | `retry --until 0 --delay 5s docker ps` | Poll until Docker daemon is ready |
| wargs + retry | `cat hosts.txt \| wargs retry --times 2 ping -c 1` | Retry pings across a host list |

---

## Testing Strategy

Unit tests in `Winix.Retry.Tests`:

- **BackoffCalculator:** fixed/linear/exp produce correct delays, jitter stays within [0.5, 1.0) range, attempt indexing is correct
- **RetryOptions validation:** `--on` + `--until` together is rejected, zero times is rejected, negative delay is rejected, invalid backoff name is rejected
- **Exit-code filtering:** default retries on non-zero, `--on` whitelist works, `--until` target stops, `--until` without 0 retries on 0
- **Formatting:** human progress lines match expected output, JSON structure is correct, colour/no-colour variants, error JSON
- **RetryRunner:** mock process execution (inject exit code sequence), verify attempt counts, verify delays are requested, verify outcomes (succeeded, exhausted, not-retryable), verify cancellation breaks the loop
- **Duration parsing:** delegate to existing `DurationParser` — no new parsing code

Integration tests (process spawning):

- Retry a command that fails N times then succeeds (script/helper that counts invocations)
- Retry exhaustion
- Command not found / not executable → correct exit codes
- `--on` with non-matching code stops immediately
