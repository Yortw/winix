# schedule — AI Agent Guide

## What This Tool Does

`schedule` creates, lists, and manages scheduled tasks using standard 5-field cron expressions. It unifies Windows Task Scheduler (`schtasks.exe`) and Unix `crontab` behind a single interface so the same commands work cross-platform. Use it when you need to register a recurring task, query what tasks are scheduled, trigger an immediate run, or calculate future fire times for a cron expression.

## Platform Story

On **Windows**, `schedule` delegates to `schtasks.exe`, which ships with every Windows version since XP and requires no installation. Tasks are created in the `\Winix\` folder by default, isolating Winix-managed tasks from system tasks and preventing accidental modification. On **Linux/macOS**, `schedule` edits the user's `crontab` in-place, tagging Winix-owned entries with `# winix:<name>` comments so they can be found and removed without disturbing unrelated entries.

## When to Use This

- Register a recurring CI or maintenance task: `schedule add --cron "0 2 * * *" -- dotnet build`
- Check what tasks are currently scheduled: `schedule list`
- Temporarily disable a task without removing it: `schedule disable my-task`
- Trigger an immediate test run outside the cron schedule: `schedule run my-task`
- Calculate when a cron expression will next fire: `schedule next "0 2 * * *"`
- Remove a task by name: `schedule remove my-task`

## Common Patterns

**Create a task that runs daily at 2am:**
```bash
schedule add --cron "0 2 * * *" -- dotnet build
```

**Create a named task running every 5 minutes:**
```bash
schedule add --cron "*/5 * * * *" --name health-check -- curl http://localhost:8080/health
```

**List Winix-managed tasks:**
```bash
schedule list
```

**List all tasks (including system tasks):**
```bash
schedule list --all
```

**Remove a task:**
```bash
schedule remove health-check
```

**Disable a task:**
```bash
schedule disable health-check
```

**Enable a disabled task:**
```bash
schedule enable health-check
```

**Trigger immediate execution:**
```bash
schedule run health-check
```

**Show run history:**
```bash
schedule history health-check
```

**Show next 5 fire times:**
```bash
schedule next "0 2 * * *"
```

**Show next 10 fire times:**
```bash
schedule next "*/5 * * * *" --count 10
```

**Machine-readable output:**
```bash
schedule list --json
schedule add --cron "0 2 * * *" --json -- dotnet build
```

## Cron Expression Syntax

Five fields: `minute hour day-of-month month day-of-week`

| Field | Range | Supports |
|-------|-------|---------|
| Minute | 0–59 | `*`, `*/N`, `1,3,5`, `1-5` |
| Hour | 0–23 | `*`, `*/N`, `1,3,5`, `1-5` |
| Day of month | 1–31 | `*`, `*/N`, `1,3,5`, `1-5` |
| Month | 1–12 | `*`, `*/N`, `1,3,5`, `1-5` |
| Day of week | 0–6 (0=Sun) | `*`, `*/N`, `1,3,5`, `1-5` |

Common expressions:

| Expression | Fires |
|------------|-------|
| `0 2 * * *` | Daily at 2:00am |
| `*/5 * * * *` | Every 5 minutes |
| `0 9 * * 1-5` | Weekdays at 9:00am |
| `30 6 1 * *` | 1st of each month at 6:30am |

## Folder Scoping

By default, `schedule` only operates on Winix-managed tasks:

- **Windows**: tasks in the `\Winix\` Task Scheduler folder
- **Linux/macOS**: crontab entries tagged with `# winix:<name>`

Use `--folder PATH` to target a different Windows Task Scheduler folder. Use `--all` with `list` to see all tasks regardless of ownership.

This scoping prevents accidental modification of system tasks or tasks registered by other tools.

## Name Generation

If `--name` is omitted from `add`, the task name is auto-generated from the command. For `dotnet build`, this produces something like `dotnet-build`. For `curl http://localhost:8080/health`, it uses a two-token heuristic (tool + first path segment or argument). Names must be unique within the folder.

## Composing with Other Tools

**Pipe list output to inspect task details:**
```bash
schedule list --json | jq '.tasks[] | select(.enabled == false)'
```

**Use with timeit to measure how long a task takes:**
```bash
timeit -- schedule run my-task
```

## Output Format

All output goes to **stderr** — this keeps stdout clean for piping. Use `--json` for machine-parseable output.

**Default `list`:**
```
Name            Cron           Next Run             Status
health-check    */5 * * * *    2026-04-12 14:25     Enabled
daily-build     0 2 * * *      2026-04-13 02:00     Enabled
```

**`--json` for list:**
```json
{
  "tasks": [
    { "name": "health-check", "cron": "*/5 * * * *", "command": "curl http://localhost:8080/health", "enabled": true, "nextRun": "2026-04-12T14:25:00+12:00" }
  ],
  "exitCode": 0,
  "status": "success",
  "version": "0.1.0"
}
```

**`next` output:**
```
2026-04-12 14:25:00 +12:00
2026-04-12 14:30:00 +12:00
2026-04-12 14:35:00 +12:00
2026-04-12 14:40:00 +12:00
2026-04-12 14:45:00 +12:00
```

## Gotchas

**`-- ` separator is required when the scheduled command takes flags.** Without `--`, flags like `--json` or `--all` after `add` are interpreted as `schedule` flags, not passed to the command. Always use `schedule add --cron "..." -- command --flag` when the scheduled command has its own flags.

**Windows: tasks run as the current user's credentials.** The task will be created under the current logged-in user. If the machine restarts and the user is not logged in, the task may not run unless configured to do so. `schtasks.exe` supports `/ru` and `/rp` for service account credentials, but this is outside `schedule`'s scope.

**Linux/macOS: history is not available.** The `crontab` backend has no run history. `schedule history` will return a note explaining this. If you need history on Linux, check your system log (`/var/log/syslog`, `journalctl`).

**Cron does not support sub-minute intervals.** The minimum granularity is one minute. For faster polling, use `peep`.

**`--all` may show many tasks on Windows.** Windows ships with hundreds of built-in scheduled tasks. `schedule list --all` will return all of them, which can be noisy. Pipe to `jq` or `grep` if you need to filter.
