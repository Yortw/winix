# schedule — Cross-Platform Task Scheduler CLI

**Date:** 2026-04-12
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)

---

## Overview

`schedule` is a cross-platform CLI for creating, listing, and managing scheduled tasks. It uses cron expressions as the universal scheduling syntax and delegates to the platform's native scheduler: Windows Task Scheduler on Windows, crontab on Linux/macOS.

**Why it's needed:** Windows has `schtasks.exe` but its syntax is arcane and incompatible with cron. Linux has `crontab -e` but it's an editor-based flow that doesn't compose well. A unified CLI with cron syntax means the same scheduling knowledge works everywhere.

**Platform backends:**
- **Windows:** COM interop with Task Scheduler 2.0 (`ITaskService`, `ITaskFolder`, `ITaskDefinition`). Tasks are created as native scheduled tasks, visible in Task Scheduler GUI.
- **Linux/macOS:** Manages the user's crontab programmatically. Winix-managed entries are tagged with `# winix:<name>` comments for identification and safe modification.

---

## Project Structure

```
src/Winix.Schedule/          — class library (cron parser, scheduler backends, formatting)
src/schedule/                — thin console app (subcommand parsing, call library, exit code)
tests/Winix.Schedule.Tests/  — xUnit tests
```

Standard Winix conventions: library does all work, console app is thin.

---

## Subcommands

### add

Creates a new scheduled task.

```
schedule add --cron "0 2 * * *" -- dotnet build /path/to/project
schedule add --cron "*/5 * * * *" --name "health-check" -- curl http://localhost:8080/health
schedule add --cron "0 9 * * 1-5" --name "weekday-report" --folder "\Reports" -- generate-report.bat
```

**Flags:**
- `--cron "expr"` — **required**. Standard 5-field cron expression (minute hour day-of-month month day-of-week).
- `--name <name>` — optional. If omitted, auto-generated from the command (e.g. `dotnet-build`).
- `--folder <path>` — optional. Task Scheduler folder on Windows (default: `\Winix\`). Ignored on Linux.
- `--` — separator between schedule flags and the command to run.

**Auto-naming:** Derives name from the first token of the command: `schedule add --cron "..." -- dotnet build` → name `dotnet-build`. If a task with that name already exists, appends a number (`dotnet-build-2`).

**Command handling:** Everything after `--` is the command and its arguments. On Windows, stored as the task action's executable + arguments. On Linux, stored as the crontab command line.

### list

Lists scheduled tasks.

```
schedule list                              # Winix-managed tasks only (default folder)
schedule list --all                        # All tasks across all folders
schedule list --folder "\Microsoft\Office" # Tasks in a specific folder
```

**Output (table):**

```
Name             Cron            Next Run              Status    Command
health-check     */5 * * * *     2026-04-12 14:35:00   Enabled   curl http://localhost:8080/health
weekday-report   0 9 * * 1-5    2026-04-14 09:00:00   Disabled  generate-report.bat
```

**Flags:**
- `--all` — show all tasks, not just Winix-managed ones. On Windows, walks all Task Scheduler folders recursively. On Linux, shows all crontab entries.
- `--folder <path>` — show tasks in a specific folder (Windows only).
- `--json` — JSON output to stderr.

**Non-Winix tasks:** When listing tasks from other folders (via `--all` or `--folder`), the cron column may show the native schedule format if it can't be reverse-mapped to a cron expression. For `--all`, an additional "Folder" column is shown.

### remove

Deletes a scheduled task.

```
schedule remove health-check
schedule remove weekday-report --folder "\Reports"
```

Prints confirmation to stderr. Exits 1 if the task doesn't exist.

### enable / disable

Enables or disables a task without deleting it.

```
schedule enable health-check
schedule disable health-check
```

On Windows: sets the task's Enabled property. On Linux: comments out / uncomments the crontab line (preserving the `# winix:<name>` tag).

### run

Triggers immediate execution of a task.

```
schedule run health-check
```

On Windows: uses `IRegisteredTask.Run()`. On Linux: extracts the command from the crontab entry and executes it in a subshell.

Does NOT wait for completion — fires and returns immediately.

### history

Shows recent run history for a task.

```
schedule history health-check
```

**Output:**

```
Time                   Exit Code  Duration
2026-04-12 14:30:00    0          1.2s
2026-04-12 14:25:00    0          0.9s
2026-04-12 14:20:00    1          0.3s
```

On Windows: queries Task Scheduler run history (built-in). On Linux: not natively available — prints "Run history not available on this platform. Check syslog for cron output."

### next

Pure cron expression evaluator — no scheduler interaction.

```
schedule next "0 2 * * *"
schedule next "*/5 * * * *" --count 10
```

Shows the next N fire times (default 5) for a cron expression. Useful for testing expressions before creating tasks.

**Output:**

```
2026-04-13 02:00:00
2026-04-14 02:00:00
2026-04-15 02:00:00
2026-04-16 02:00:00
2026-04-17 02:00:00
```

---

## Components

### CronExpression

Parses and evaluates standard 5-field cron expressions.

```csharp
public sealed class CronExpression
{
    /// <summary>
    /// Parses a cron expression string.
    /// </summary>
    /// <exception cref="FormatException">The expression is invalid.</exception>
    public static CronExpression Parse(string expression);

    /// <summary>
    /// Returns the next occurrence after the given time.
    /// </summary>
    public DateTimeOffset GetNextOccurrence(DateTimeOffset after);

    /// <summary>
    /// Returns the next N occurrences after the given time.
    /// </summary>
    public IReadOnlyList<DateTimeOffset> GetNextOccurrences(DateTimeOffset after, int count);

    /// <summary>The original expression string.</summary>
    public string Expression { get; }
}
```

**Supported syntax:**
- 5 fields: minute (0-59), hour (0-23), day-of-month (1-31), month (1-12), day-of-week (0-7, 0 and 7 = Sunday)
- Values: `*`, specific numbers, ranges (`1-5`), lists (`1,3,5`), steps (`*/5`, `1-10/2`)
- Month names: `jan`-`dec` (case-insensitive)
- Day names: `sun`-`sat` (case-insensitive)
- Special strings: `@hourly`, `@daily`, `@weekly`, `@monthly`, `@yearly`, `@annually`

### ISchedulerBackend

Platform-specific scheduler interface.

```csharp
public interface ISchedulerBackend
{
    /// <summary>Creates a new scheduled task.</summary>
    ScheduleResult Add(string name, CronExpression cron, string command, string[] arguments, string folder);

    /// <summary>Lists tasks in the specified scope.</summary>
    IReadOnlyList<ScheduledTask> List(string? folder, bool all);

    /// <summary>Removes a task by name.</summary>
    ScheduleResult Remove(string name, string folder);

    /// <summary>Enables a disabled task.</summary>
    ScheduleResult Enable(string name, string folder);

    /// <summary>Disables a task.</summary>
    ScheduleResult Disable(string name, string folder);

    /// <summary>Triggers immediate execution.</summary>
    ScheduleResult Run(string name, string folder);

    /// <summary>Returns recent run history.</summary>
    IReadOnlyList<TaskRunRecord> GetHistory(string name, string folder);
}
```

### ScheduledTask

Data model for a listed task.

```csharp
public sealed class ScheduledTask
{
    /// <summary>Task name.</summary>
    public string Name { get; }

    /// <summary>Cron expression (if reverse-mapped) or native schedule description.</summary>
    public string Schedule { get; }

    /// <summary>Next scheduled run time, or null if disabled/unknown.</summary>
    public DateTimeOffset? NextRun { get; }

    /// <summary>Enabled or Disabled.</summary>
    public string Status { get; }

    /// <summary>The command that runs.</summary>
    public string Command { get; }

    /// <summary>Folder path (Windows) or empty (Linux).</summary>
    public string Folder { get; }
}
```

### TaskRunRecord

Data model for a history entry.

```csharp
public sealed class TaskRunRecord
{
    /// <summary>When the task ran.</summary>
    public DateTimeOffset StartTime { get; }

    /// <summary>Exit code, or null if still running.</summary>
    public int? ExitCode { get; }

    /// <summary>Duration, or null if still running.</summary>
    public TimeSpan? Duration { get; }
}
```

### WindowsSchedulerBackend

Implements `ISchedulerBackend` using Windows Task Scheduler 2.0 COM interop.

**Key operations:**
- `Add`: Creates `ITaskDefinition` with a `TimeTrigger` or `CalendarTrigger` derived from the cron expression. Stores the original cron expression in the task's `Description` field for round-trip display.
- `List`: Walks `ITaskFolder.GetTasks()`, optionally recursive via `ITaskFolder.GetFolders()`.
- `Remove/Enable/Disable`: `ITaskFolder.DeleteTask()`, `IRegisteredTask.Enabled = true/false`.
- `Run`: `IRegisteredTask.Run()`.
- `History`: `ITaskDefinition` → query event log via Task Scheduler's built-in history.

**Cron-to-trigger mapping:** The cron expression is evaluated to determine the trigger type:
- Simple interval (`*/N * * * *`) → `TimeTrigger` with repetition interval
- Daily at fixed time (`0 2 * * *`) → `DailyTrigger` at 02:00
- Complex expressions → `CalendarTrigger` with explicit days/months, or multiple triggers

The original cron expression is stored in the task description so `list` can display it.

**COM interop approach:** Use `Type.GetTypeFromProgID("Schedule.Service")` and `Activator.CreateInstance()` for AOT-compatible late-bound COM. This avoids generating COM interop assemblies. The `dynamic` keyword is NOT AOT-safe — use explicit `object` + `InvokeMember` reflection, or use the `TaskScheduler` NuGet package if it's AOT-compatible.

**AOT concern:** COM interop via `dynamic` or late-bound reflection may not work with AOT. Alternative: P/Invoke the Task Scheduler C API directly, or use `Microsoft.Win32.TaskScheduler` NuGet (check AOT compatibility). If neither works, fall back to shelling out to `schtasks.exe` with argument construction (like winix shells out to winget/scoop). This is the safest AOT approach.

**Recommended approach for v1:** Shell out to `schtasks.exe` for task management (add, remove, enable, disable, run, list, history). This is:
- Guaranteed AOT-compatible (just process spawning)
- Same pattern as winix installer (delegates to native tool)
- Simpler than COM interop
- `schtasks.exe` is always available on Windows

### CrontabBackend

Implements `ISchedulerBackend` using the user's crontab on Linux/macOS.

**Key operations:**
- `Add`: Reads current crontab (`crontab -l`), appends `# winix:<name>\n<cron> <command>`, writes back (`crontab -`).
- `List`: Reads crontab, filters for `# winix:` tagged entries (or all entries with `--all`).
- `Remove`: Reads crontab, removes the `# winix:<name>` line and following command line, writes back.
- `Enable/Disable`: Comments out (`#`) or uncomments the command line, preserving the tag.
- `Run`: Extracts command from crontab entry, runs in subshell.
- `History`: Not available. Returns empty list with a message.

**Crontab format for Winix entries:**
```
# winix:health-check
*/5 * * * * curl http://localhost:8080/health
# winix:daily-build
0 2 * * * cd /projects/myapp && dotnet build
```

### Formatting

Output formatting following Winix conventions.

```csharp
public static class Formatting
{
    /// <summary>Formats the task list as a table.</summary>
    public static string FormatTable(IReadOnlyList<ScheduledTask> tasks, bool showFolder, bool useColor);

    /// <summary>Formats run history as a table.</summary>
    public static string FormatHistory(IReadOnlyList<TaskRunRecord> records, bool useColor);

    /// <summary>Formats next-occurrence output.</summary>
    public static string FormatNextOccurrences(IReadOnlyList<DateTimeOffset> times);

    /// <summary>Formats a success/error result message.</summary>
    public static string FormatResult(ScheduleResult result, bool useColor);

    /// <summary>JSON output following Winix conventions.</summary>
    public static string FormatJson(/* ... */);
}
```

---

## CLI Flags

| Flag | Subcommand | Description |
|------|-----------|-------------|
| `--cron "expr"` | add | Cron expression (required) |
| `--name <name>` | add | Task name (auto-generated if omitted) |
| `--folder <path>` | all except next | Task Scheduler folder (default: `\Winix\`) |
| `--all` | list | Show all tasks, not just Winix-managed |
| `--count N` | next | Number of occurrences to show (default: 5) |
| `--json` | all | Structured JSON output to stderr |
| `--color` | all | Force coloured output |
| `--no-color` | all | Disable coloured output |
| `--describe` | — | AI agent metadata |
| `-h`, `--help` | all | Show help |
| `--version` | — | Show version |

---

## JSON Output

All subcommands support `--json` for structured output to stderr, following the standard Winix envelope pattern. This enables scripting and AI agent consumption.

### list --json

```json
{
  "tool": "schedule",
  "version": "0.2.0",
  "exit_code": 0,
  "exit_reason": "success",
  "tasks": [
    {
      "name": "health-check",
      "cron": "*/5 * * * *",
      "next_run": "2026-04-12T14:35:00+12:00",
      "status": "Enabled",
      "command": "curl http://localhost:8080/health",
      "folder": "\\Winix"
    }
  ]
}
```

### add/remove/enable/disable/run --json

```json
{
  "tool": "schedule",
  "version": "0.2.0",
  "exit_code": 0,
  "exit_reason": "success",
  "action": "add",
  "name": "health-check",
  "cron": "*/5 * * * *",
  "next_run": "2026-04-12T14:35:00+12:00"
}
```

### history --json

```json
{
  "tool": "schedule",
  "version": "0.2.0",
  "exit_code": 0,
  "exit_reason": "success",
  "name": "health-check",
  "runs": [
    {
      "start_time": "2026-04-12T14:30:00+12:00",
      "exit_code": 0,
      "duration_seconds": 1.2
    }
  ]
}
```

### next --json

```json
{
  "tool": "schedule",
  "version": "0.2.0",
  "exit_code": 0,
  "exit_reason": "success",
  "cron": "0 2 * * *",
  "occurrences": [
    "2026-04-13T02:00:00+12:00",
    "2026-04-14T02:00:00+12:00"
  ]
}
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Error (task not found, scheduler API failure, invalid cron) |
| 125 | Usage error (bad arguments, missing required flags) |

---

## Folder Scoping

**Windows:**
- Default folder: `\Winix\` (auto-created on first `add`)
- `--folder "\Reports"` creates/lists in `\Reports\`
- `--all` on `list` walks all folders recursively
- Folder paths use backslash (`\Winix\SubFolder`)

**Linux/macOS:**
- No folder concept — crontab is flat
- `--folder` is silently ignored
- Winix entries identified by `# winix:<name>` comment tags
- `--all` shows all crontab entries (not just Winix-tagged)

---

## Testing Strategy

**Unit-testable:**
- `CronExpression.Parse` — valid expressions, invalid expressions, special strings (@daily etc.)
- `CronExpression.GetNextOccurrence` — various patterns, edge cases (month boundaries, leap years, day-of-week)
- Auto-name generation from command strings
- `Formatting` — table, history, next-occurrences, result messages
- Crontab line parsing and generation (tag format)

**Integration-testable (carefully):**
- `WindowsSchedulerBackend` — create task in `\Winix\Test\`, verify it exists, remove it. Windows-only.
- `CrontabBackend` — harder to test safely (modifies real crontab). Could use a mock crontab file.

**Manual testing:**
- End-to-end: add → list → run → history → disable → enable → remove
- Verify tasks appear in Windows Task Scheduler GUI
- Verify crontab entries on Linux

---

## Scope Boundaries

**In scope (v1):**
- Subcommands: add, list, remove, enable, disable, run, history, next
- Cron expression parsing and evaluation (5-field standard + @shortcuts)
- Windows: schtasks.exe delegation
- Linux/macOS: crontab management with tag-based identification
- Folder scoping with `\Winix\` default
- Auto-naming from command
- `--all` for full task visibility
- `next` subcommand for cron expression testing

**Out of scope (v1):**
- Second-level cron fields (6-field or 7-field expressions)
- Task dependencies (run A after B completes)
- Email/notification on failure
- Web dashboard or GUI
- System-level crontab (requires root) — user crontab only
- Windows event triggers (only time-based triggers)
- Persistent run history on Linux (would need a log file)
