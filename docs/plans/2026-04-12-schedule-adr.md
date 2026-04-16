# ADR: Winix Schedule

**Date:** 2026-04-12
**Status:** Proposed
**Related design:** `2026-04-12-schedule-design.md`

---

## 1. schtasks.exe delegation (not COM/Task Scheduler API)

**Context:** Windows exposes scheduled tasks through two interfaces: the COM-based Task Scheduler API (`taskschd.dll`, `ITaskService`) and the `schtasks.exe` command-line tool. The COM API offers richer control over task properties (triggers, conditions, run-as settings) but requires COM interop, which is incompatible with AOT compilation. `schtasks.exe` ships on every Windows version since XP and covers the operations needed (create, list, delete, enable, disable, run).

**Decision:** Delegate all Windows Task Scheduler operations to `schtasks.exe` via `ProcessStartInfo.ArgumentList`.

**Rationale:** AOT-safe, zero interop surface, and follows the same pattern already established by the `winix` installer. `schtasks.exe` is guaranteed to be present; no version or feature detection is needed. The tool's scope (cron-style recurring tasks) maps cleanly onto the `/SC`, `/MO`, `/D`, `/ST`, and `/ET` options that `schtasks.exe` exposes.

**Trade-offs Accepted:** Some advanced task properties (idle conditions, power management, detailed run-as configuration) are not exposed. This is acceptable: `schedule` targets developer and automation workflows, not full Windows Task Scheduler administration.

**Options Considered:**
- *COM Task Scheduler API via P/Invoke or COM interop:* rejected — not AOT-compatible; requires unconstrained reflection or manual COM vtable wrappers.
- *Windows Management Instrumentation (WMI):* rejected — high-overhead, deprecated for task management in favour of the COM API, and similarly incompatible with AOT.

---

## 2. crontab management with `# winix:<name>` tags

**Context:** On Linux/macOS, the standard mechanism for scheduled tasks is the user's `crontab`. Editing crontab programmatically risks destroying unrelated entries if the tool reads, modifies, and rewrites the file carelessly. A tagging convention lets `schedule` identify its own entries without assuming ownership of the entire crontab.

**Decision:** Winix-owned crontab entries are written as a pair: a `# winix:<name>` comment line immediately followed by the cron line. Add/remove/enable/disable operations locate entries by tag and mutate only those lines.

**Rationale:** Non-destructive — unrelated entries are never touched. The tag is human-readable and survives manual crontab edits as long as the comment line is preserved. The convention is self-documenting: anyone reading the crontab can see which lines are Winix-managed.

**Trade-offs Accepted:** A user who manually deletes the `# winix:<name>` comment line (but keeps the cron line) will produce an orphaned entry that `schedule remove` cannot find. This is an acceptable edge case: the user has explicitly intervened in Winix's managed state.

**Options Considered:**
- *Separate crontab file via `cron.d`:* considered — cleaner isolation, but `/etc/cron.d` requires root on most systems; user-level `crontab -e` is the only non-root option.
- *Sentinel block (`# BEGIN winix` / `# END winix`):* rejected — a mismatched block due to manual editing could corrupt the entire managed section; the per-entry tag is more resilient to partial edits.

---

## 3. Cron as universal scheduling syntax

**Context:** Windows Task Scheduler uses a proprietary trigger model (daily/weekly/monthly with specific UI-oriented fields). Unix `crontab` uses the 5-field cron syntax. A cross-platform tool needs a single input format that works on both platforms.

**Decision:** Accept only 5-field cron expressions as input and map them to the appropriate native format at write time. Windows mapping is handled by `CronToSchtasksMapper`; Unix crontab uses cron syntax directly.

**Rationale:** Cron syntax is the de facto standard for scheduled task expressions — familiar to any developer who has used Linux, CI/CD pipelines, Kubernetes CronJobs, or serverless schedulers. Exposing the native Windows trigger model to users would add cognitive overhead and break cross-platform portability of task definitions.

**Trade-offs Accepted:** Not all cron expressions map perfectly to `schtasks.exe` options. Complex combinations (e.g., `1,3,5` in the day-of-week field alongside a specific day-of-month) may require multiple `schtasks.exe` invocations or result in an error with a clear message. This is acceptable for the tool's primary use case of simple recurring schedules.

**Options Considered:**
- *Expose schtasks trigger types natively on Windows:* rejected — would require a different CLI shape per platform, breaking scripting portability.
- *ISO 8601 duration / RFC 5545 RRULE:* rejected — less widely known than cron, would add a parser for a format that most users don't already have in muscle memory.

---

## 4. Folder scoping (`\Winix\` default on Windows)

**Context:** Windows Task Scheduler organises tasks in a folder hierarchy. Without scoping, `schedule list` would return hundreds of system-managed tasks, and `schedule remove` could accidentally target a task created by another tool or Windows itself.

**Decision:** All Winix-created tasks are placed in `\Winix\` by default. `list` shows only that folder unless `--all` is passed. `remove`, `enable`, `disable`, `run`, and `history` operate only within the scoped folder unless overridden with `--folder`.

**Rationale:** Prevents accidental modification of system tasks. Keeps `schedule list` usable — showing only the user's Winix-managed tasks by default rather than hundreds of Windows-internal tasks. Mirrors the `# winix:<name>` tagging convention used on Linux/macOS for the same reason.

**Trade-offs Accepted:** Users who want to manage tasks outside `\Winix\` must pass `--folder`. This is a minor friction increase for the uncommon case. `--all` provides an escape hatch for listing across all folders.

**Options Considered:**
- *Root folder (`\`):* rejected — `schedule list` would expose all system tasks by default, making the output unusable without filtering.
- *Per-user folder (`\Winix\<username>\`):* rejected — adds complexity for no benefit in single-user developer scenarios; folder naming with special characters can be problematic with `schtasks.exe`.

---

## 5. Custom cron parser (not a NuGet dependency)

**Context:** Several NuGet packages provide cron expression parsing (e.g., Cronos, NCrontab, HangfireIO). Using one would reduce implementation effort but adds a dependency — one that must be AOT-compatible, actively maintained, and acceptable under its license.

**Decision:** Implement a custom 5-field cron parser (`CronExpression`, `CronField`) in `Winix.Schedule`.

**Rationale:** The required subset is well-defined: 5 fields, `*`, `*/N`, ranges, and lists. A custom parser is small (< 200 lines), requires no NuGet dependency, is guaranteed AOT-safe, and gives full control over error messages and mapping to `schtasks.exe` options. Adding a dependency for this scope would be over-engineering.

**Trade-offs Accepted:** Non-standard cron extensions (e.g., `@reboot`, `@daily` aliases, 6-field expressions with seconds, Quartz-style `?` and `L` fields) are not supported. These are out of scope for the tool's cross-platform cron use case.

**Options Considered:**
- *Cronos NuGet package:* evaluated — AOT-compatible and well-maintained, but adds an external dependency for functionality that fits in one file; rejected on simplicity grounds.
- *NCrontab:* rejected — not verified AOT-safe without additional trim analysis work.

---

## Decisions Explicitly Deferred

| Topic | Why Deferred |
|-------|-------------|
| Second-level cron (6-field with seconds) | Not supported by standard crontab; complex to map to schtasks.exe; unusual in developer scheduling scenarios |
| Task dependencies (run B after A succeeds) | Requires orchestration logic beyond scheduler delegation; out of scope for a cross-platform scheduler wrapper |
| Email/webhook notifications on task failure | Requires credentials and network configuration; better handled at the script level or via a dedicated alerting tool |
| Event-based triggers (on file change, system event) | Windows-specific concept with no crontab analogue; out of scope for the cross-platform cron model |
| Persistent run history on Linux/macOS | `crontab` has no history mechanism; would require a sidecar file or system log parsing; deferred pending user demand |
