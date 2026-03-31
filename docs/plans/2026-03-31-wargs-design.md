# wargs — Cross-Platform xargs Replacement

**Date:** 2026-03-31
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)

---

## Overview

`wargs` (Winix args) is a cross-platform xargs replacement with sane defaults: line-delimited input, parallel execution, correct Windows path/quoting handling, and structured JSON output. It reads items from stdin, builds command invocations, and runs them — sequentially or in parallel.

**Why not just use xargs?** Windows has no native xargs. GNU xargs via MSYS/Git Bash works but has painful quoting issues with Windows paths. The whitespace-delimited default is a historical mistake that causes bugs with filenames containing spaces. `wargs` fixes these defaults while staying compatible enough for muscle memory.

---

## Project Structure

```
src/Winix.Wargs/           — class library (all logic)
src/wargs/                 — thin console app (arg parsing, call library, exit code)
tests/Winix.Wargs.Tests/   — xUnit tests
```

Follows standard Winix conventions: library does all work, console app is thin, ShellKit provides arg parsing and terminal detection.

---

## Data Flow

```
stdin → InputReader → IEnumerable<string> items
    → CommandBuilder → IEnumerable<CommandInvocation>
    → JobRunner → async stream of JobResult
    → Formatting → human/JSON/NDJSON output
```

---

## Components

### InputReader

Reads from a `TextReader` (stdin or test stream), yields items one at a time. Streaming — does not buffer entire input.

**Delimiter modes:**

| Mode | Trigger | Behaviour |
|------|---------|-----------|
| Line (default) | No flags | Split on `\n`, trim `\r\n`. Empty lines skipped. |
| Null | `-0` / `--null` | Split on `\0`. Empty items skipped. |
| Custom | `-d` / `--delimiter` | Split on single character. Empty items skipped. |
| Whitespace | `--compat` | Split on whitespace runs. POSIX single/double quote grouping and backslash escapes. |

Empty items and whitespace-only lines (in line mode) are always skipped.

```csharp
public sealed class InputReader
{
    public InputReader(TextReader source, DelimiterMode mode, char? customDelimiter = null);
    public IEnumerable<string> ReadItems();
}

public enum DelimiterMode { Line, Null, Custom, Whitespace }
```

### CommandBuilder

Takes the command template (trailing CLI args) and items, produces concrete invocations.

**Placeholder detection:** Scan template args for `{}` at construction time. If any arg contains `{}`, substitution mode; otherwise, append mode. No flag needed.

**Substitution mode:** For each item (or batch), replace every `{}` in every template arg with the item value.

**Append mode:** For each item (or batch), append items as additional arguments after the template.

**Batching (`-n N`):**
- Default 1 — one item per invocation.
- `-n 5` collects 5 items per invocation.
- Append mode: all items appended as separate args.
- Substitution mode with `-n > 1`: each `{}` replaced with all items space-joined.
- Last batch may be smaller than N.

**No command:** Default to `echo` (matches xargs convention).

```csharp
public sealed class CommandBuilder
{
    public CommandBuilder(string[] template, int batchSize = 1);
    public bool IsSubstitutionMode { get; }
    public IEnumerable<CommandInvocation> Build(IEnumerable<string> items);
}

public sealed record CommandInvocation(
    string Command,
    string[] Arguments,
    string DisplayString,
    string[] SourceItems
);
```

`DisplayString` is a human-readable, shell-quoted form for `--verbose` and `--dry-run`.

### JobRunner

Executes `CommandInvocation`s with parallelism, output buffering, confirm prompts, and fail-fast.

**Parallelism:**
- `-P N` controls max concurrent jobs. Default 1 (sequential).
- `-P 0` means unlimited (matches GNU xargs).
- Implemented with `SemaphoreSlim(N)`.

**Output buffering:**

| Strategy | Flag | Behaviour |
|----------|------|-----------|
| Job-buffered (default) | — | Capture stdout+stderr per job. Print atomically on completion. Completion order. |
| Line-buffered | `--line-buffered` | Child inherits stdio directly. Output interleaves. Fastest feedback. |
| Keep-order | `--keep-order` / `-k` | Capture per job. Print in input order. Hold completed jobs until their turn. |

**Confirm mode (`-p` / `--confirm`):**
- Before each job, print command to stderr, prompt `?...`.
- Read from `/dev/tty` (Unix) or `CON` (Windows) — not stdin (stdin is the input pipe).
- `y`/`Y`/`yes` proceeds, anything else skips.
- Incompatible with `-P > 1` (caught at arg validation).

**Fail-fast (`--fail-fast`):**
- On first non-zero child exit: set cancellation flag. No new jobs spawn. In-flight jobs finish.
- If command not found/not executable on first job, stop regardless of `--fail-fast` (no point retrying).

**Signal handling:**
- Ctrl+C / SIGINT: stop spawning new jobs, wait for in-flight to finish, then exit.

```csharp
public sealed class JobRunner
{
    public JobRunner(JobRunnerOptions options);
    public async Task<WargsResult> RunAsync(
        IEnumerable<CommandInvocation> invocations,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default);
}

public sealed record JobRunnerOptions(
    int Parallelism,
    BufferStrategy Strategy,
    bool FailFast,
    bool DryRun,
    bool Verbose,
    bool Confirm,
    Func<string, bool>? ConfirmPrompt
);

public enum BufferStrategy { JobBuffered, LineBuffered, KeepOrder }
```

`ConfirmPrompt` delegate injected for testability — production code reads from `/dev/tty` or `CON`.

### Result Types

```csharp
public sealed record JobResult(
    int JobIndex,
    int ChildExitCode,
    string? Output,
    TimeSpan Duration,
    string[] SourceItems,
    bool Skipped
);

public sealed record WargsResult(
    int TotalJobs,
    int Succeeded,
    int Failed,
    int Skipped,
    TimeSpan WallTime,
    List<JobResult> Jobs
);
```

### Formatting

Same pattern as timeit/squeeze — static methods on a `Formatting` class.

**Human output (default):**
- Each job's captured stdout printed to stdout as it completes.
- `--verbose`: print `wargs: <command>` to stderr before each job.
- `--dry-run`: print each command to stdout, don't execute. Exit 0.
- On completion with failures: `wargs: 3/10 jobs failed` to stderr.

**JSON (`--json`):**

```json
{
  "tool": "wargs",
  "version": "0.1.0",
  "exit_code": 0,
  "exit_reason": "success",
  "total_jobs": 10,
  "succeeded": 10,
  "failed": 0,
  "skipped": 0,
  "wall_seconds": 4.200
}
```

**NDJSON (`--ndjson`):**

```json
{"tool":"wargs","version":"0.1.0","job":1,"exit_code":0,"exit_reason":"success","child_exit_code":0,"input":"file1.cs","wall_seconds":0.340}
{"tool":"wargs","version":"0.1.0","job":2,"exit_code":0,"exit_reason":"success","child_exit_code":1,"input":"file2.cs","wall_seconds":0.510}
```

- `job` is 1-based input-order index.
- `exit_code` is wargs' own status for this job (0 = wargs spawned and reaped the child successfully; 126/127 if the command couldn't be spawned).
- `child_exit_code` is the child process's exit code.
- `input` is a string for `-n 1`, an array for `-n > 1`.
- With `--keep-order`, NDJSON lines emitted in order.

---

## Path Handling

wargs handles Windows paths (spaces, long names, `\\?\` prefix) and Unix paths correctly:

- **`Process.Start` with `ArgumentList`** — .NET handles all platform-specific quoting. No manual shell escaping.
- **No `cmd /c` or `sh -c` wrapper** — direct process spawn. Avoids the entire class of shell-escaping bugs.
- **No path length assumptions** — strings passed through as-is. .NET on modern Windows supports long paths natively.
- **Line-delimited default** — paths with spaces just work, one per line, no quoting needed from the user.
- **`DisplayString`** uses platform-aware quoting for readability (display-only, not execution).

---

## CLI Interface

```
Usage: wargs [options] [--] <command> [args...]
```

**Options:**

| Flag | Short | Value | Description |
|------|-------|-------|-------------|
| `--parallel` | `-P` | N | Max concurrent jobs (default 1, 0 = unlimited) |
| `--batch` | `-n` | N | Items per invocation (default 1) |
| `--null` | `-0` | — | Null-delimited input |
| `--delimiter` | `-d` | CHAR | Custom input delimiter |
| `--compat` | — | — | POSIX whitespace splitting with quote handling |
| `--fail-fast` | — | — | Stop spawning after first failure |
| `--keep-order` | `-k` | — | Print output in input order |
| `--line-buffered` | — | — | Children inherit stdio directly |
| `--confirm` | `-p` | — | Prompt before each job |
| `--dry-run` | — | — | Print commands without executing |
| `--verbose` | `-v` | — | Print each command to stderr before running |
| `--help` | `-h` | — | Show help |
| `--version` | — | — | Show version |
| `--json` | — | — | JSON summary to stderr |
| `--ndjson` | — | — | Streaming NDJSON per job to stderr |
| `--color` | — | — | Force colour on |
| `--no-color` | — | — | Force colour off |

**Compat aliases (GNU xargs muscle memory):**

| GNU xargs | wargs equivalent |
|-----------|-----------------|
| `-P N` | `--parallel N` |
| `-n N` | `--batch N` |
| `-0` | `--null` |
| `-d X` | `--delimiter X` |
| `-p` | `--confirm` |

**Validation rules:**

- `--confirm` + `-P > 1` → error
- `--line-buffered` + `--keep-order` → error
- `--null` / `--delimiter` / `--compat` mutually exclusive
- `-n < 1` or `-P < 0` → error
- `--ndjson` + `--line-buffered` → error

No command → default to `echo`.

---

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | All jobs succeeded |
| 123 | One or more child processes failed (GNU xargs convention) |
| 124 | Aborted due to `--fail-fast` |
| 125 | Usage error (bad arguments) |
| 126 | Command not executable (permission denied) |
| 127 | Command not found |

- Mixed failures → 123, regardless of individual child codes. Per-job detail in NDJSON.
- 126/127: command can't be spawned at all. Detected on first job — stops immediately regardless of `--fail-fast` (no point retrying a missing command).

---

## Deferred (not in v1)

| Feature | Why deferred |
|---------|-------------|
| `-I` flag (custom placeholder string) | `{}` is sufficient for v1 |
| `--max-args` (ARG_MAX auto-chunking) | Unix artefact, not relevant on Windows |
| Multi-placeholder (`{1}`, `{2}` for CSV) | Niche, adds parser complexity |
| Retry on failure | Can be added later without breaking changes |
| Rate limiting / throttle | No current use case |
