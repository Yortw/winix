# schedule

Cross-platform task scheduler with cron expressions.

Unified interface for creating, listing, and managing scheduled tasks using standard cron syntax. On **Windows**, delegates to `schtasks.exe` (Windows Task Scheduler). On **Linux/macOS**, manages `crontab` entries using tagged comments to track Winix-owned lines non-destructively.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/schedule
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Schedule
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Schedule
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
schedule <subcommand> [options] [args...]
```

All output goes to **stderr**. Exit codes, JSON, and composability are described below.

## Subcommands

### add

Register a new scheduled task.

```bash
# Run dotnet build daily at 2am
schedule add --cron "0 2 * * *" -- dotnet build

# Create a named task running every 5 minutes
schedule add --cron "*/5 * * * *" --name health-check -- curl http://localhost:8080/health

# Run a script with a specific folder
schedule add --cron "0 9 * * 1-5" --folder "\MyApp\" -- powershell -File backup.ps1
```

### list

List scheduled tasks. By default shows only tasks in the `\Winix\` folder (Windows) or Winix-tagged crontab entries (Linux/macOS).

```bash
# List Winix-managed tasks
schedule list

# List all tasks (including system tasks and other users' tasks)
schedule list --all
```

### remove

Remove a task by name.

```bash
schedule remove health-check
```

### enable

Re-enable a disabled task.

```bash
schedule enable health-check
```

### disable

Disable a task without removing it. The task definition is preserved; it will not fire until re-enabled.

```bash
schedule disable health-check
```

### run

Trigger an immediate (on-demand) execution of a task, independent of its cron schedule.

```bash
schedule run health-check
```

### history

Show run history for a task (Windows only; returns a note on Linux/macOS where history is not available via crontab).

```bash
schedule history health-check
```

### next

Compute and display upcoming fire times for a cron expression. No backend interaction — pure cron calculation.

```bash
# Show next 5 fire times for a cron expression
schedule next "0 2 * * *"

# Show next 10 fire times
schedule next "*/5 * * * *" --count 10
```

## Cron Expression Syntax

Five space-separated fields: `minute hour day-of-month month day-of-week`

| Field | Range | Special characters |
|-------|-------|--------------------|
| Minute | 0–59 | `*` `,` `-` `/` |
| Hour | 0–23 | `*` `,` `-` `/` |
| Day of month | 1–31 | `*` `,` `-` `/` |
| Month | 1–12 | `*` `,` `-` `/` |
| Day of week | 0–6 (0=Sun) | `*` `,` `-` `/` |

### Special values

| Expression | Meaning |
|------------|---------|
| `*` | Every value |
| `*/N` | Every N-th value (step) |
| `1,3,5` | Specific values (list) |
| `1-5` | Inclusive range |

### Examples

| Expression | Fires |
|------------|-------|
| `0 2 * * *` | Daily at 2:00am |
| `*/5 * * * *` | Every 5 minutes |
| `0 9 * * 1-5` | Weekdays at 9:00am |
| `30 6 1 * *` | 1st of every month at 6:30am |
| `0 0 * * 0` | Every Sunday at midnight |

## Folder Scoping

On **Windows**, tasks are created in the `\Winix\` folder by default. This prevents accidental modification of system tasks or tasks created by other tools. Use `--folder` to specify a different folder.

On **Linux/macOS**, Winix-managed crontab entries are tagged with a `# winix:<name>` comment. `list` shows only tagged entries by default; `--all` shows the full crontab.

| Option | Description |
|--------|-------------|
| (default) | Windows: `\Winix\` folder. Linux/macOS: Winix-tagged entries only. |
| `--folder PATH` | Use the specified Task Scheduler folder (Windows only). |
| `--all` | Include all tasks, not just Winix-managed ones. |

## Options

| Option | Description |
|--------|-------------|
| `--cron EXPR` | Cron expression (required for `add`). |
| `--name NAME` | Task name. Auto-generated from the command if omitted. |
| `--folder PATH` | Task Scheduler folder (Windows). Default: `\Winix\`. |
| `--count N` | Number of fire times to show for `next` (default: 5). |
| `--all` | Show all tasks, not just Winix-managed. Used with `list`. |
| `--json` | Output results as JSON to stderr. |
| `--color` | Force coloured output (overrides `NO_COLOR`). |
| `--no-color` | Disable coloured output. |
| `--help` | Show help and exit. |
| `--version` | Show version and exit. |

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success. |
| 1 | Error — task not found, scheduler failure, or invalid cron expression. |
| 125 | Usage error (bad arguments). |

## Platform Notes

| Platform | Implementation |
|----------|---------------|
| Windows | Delegates to `schtasks.exe`, which is present on all Windows versions since XP. Creates tasks in `\Winix\` by default. Run history via Task Scheduler event log. |
| Linux / macOS | Manages `crontab -e` entries. Winix-owned lines are tagged with `# winix:<name>` to enable non-destructive add/remove. History is not available via crontab. |

## Colour

- Task names are highlighted for quick scanning.
- Enabled/disabled state is colour-coded.
- `--no-color` suppresses all ANSI colour output.
- Respects the `NO_COLOR` environment variable ([no-color.org](https://no-color.org)).

## Part of Winix

`schedule` is part of the [Winix](../../README.md) CLI toolkit.
