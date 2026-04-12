#nullable enable

using System.Collections.Generic;

namespace Winix.Schedule;

/// <summary>
/// Abstraction over an OS task-scheduler backend (e.g. Windows Task Scheduler, cron, launchd).
/// Implementations handle the platform-specific mechanics of registering, querying, and
/// managing scheduled tasks; callers interact solely through this interface.
/// </summary>
public interface ISchedulerBackend
{
    /// <summary>
    /// Registers a new scheduled task with the backend.
    /// </summary>
    /// <param name="name">
    /// The unique task name within <paramref name="folder"/>.
    /// Use <see cref="NameGenerator.FromCommand"/> to derive one automatically when not supplied by the user.
    /// </param>
    /// <param name="cron">The cron schedule that governs when the task fires.</param>
    /// <param name="command">The executable or script to run.</param>
    /// <param name="arguments">
    /// Zero or more arguments passed to <paramref name="command"/>. Must not be folded into
    /// <paramref name="command"/> as a string to avoid quoting/injection bugs.
    /// </param>
    /// <param name="folder">
    /// The scheduler folder (namespace) in which to create the task.
    /// Pass <c>"\"</c> or an empty string for the root/default location.
    /// </param>
    /// <returns>
    /// A <see cref="ScheduleResult"/> indicating success or describing the failure reason
    /// (e.g. duplicate name, permission denied).
    /// </returns>
    ScheduleResult Add(string name, CronExpression cron, string command, string[] arguments, string folder);

    /// <summary>
    /// Returns a snapshot list of scheduled tasks visible to the current user.
    /// </summary>
    /// <param name="folder">
    /// Restricts results to a specific scheduler folder, or <c>null</c> to include all folders.
    /// </param>
    /// <param name="all">
    /// When <c>true</c>, includes tasks owned by other users or system tasks that the current
    /// user may not have permission to modify. When <c>false</c>, returns only the current
    /// user's tasks.
    /// </param>
    /// <returns>A read-only list of <see cref="ScheduledTask"/> objects; may be empty but never null.</returns>
    IReadOnlyList<ScheduledTask> List(string? folder, bool all);

    /// <summary>
    /// Removes a scheduled task from the backend.
    /// </summary>
    /// <param name="name">The name of the task to remove.</param>
    /// <param name="folder">The scheduler folder containing the task.</param>
    /// <returns>
    /// A <see cref="ScheduleResult"/> indicating success, or describing why removal failed
    /// (e.g. task not found, permission denied).
    /// </returns>
    ScheduleResult Remove(string name, string folder);

    /// <summary>
    /// Enables a previously disabled scheduled task so it will fire on its next scheduled time.
    /// </summary>
    /// <param name="name">The name of the task to enable.</param>
    /// <param name="folder">The scheduler folder containing the task.</param>
    /// <returns>
    /// A <see cref="ScheduleResult"/> indicating success, or describing why the operation failed
    /// (e.g. task not found, already enabled, permission denied).
    /// </returns>
    ScheduleResult Enable(string name, string folder);

    /// <summary>
    /// Disables a scheduled task so it will not fire until re-enabled.
    /// The task definition is preserved; only the enabled state changes.
    /// </summary>
    /// <param name="name">The name of the task to disable.</param>
    /// <param name="folder">The scheduler folder containing the task.</param>
    /// <returns>
    /// A <see cref="ScheduleResult"/> indicating success, or describing why the operation failed
    /// (e.g. task not found, already disabled, permission denied).
    /// </returns>
    ScheduleResult Disable(string name, string folder);

    /// <summary>
    /// Triggers an immediate (on-demand) run of a scheduled task, independent of its cron schedule.
    /// </summary>
    /// <param name="name">The name of the task to run.</param>
    /// <param name="folder">The scheduler folder containing the task.</param>
    /// <returns>
    /// A <see cref="ScheduleResult"/> indicating whether the run was triggered successfully.
    /// Note: success means the task was started, not that it completed without error.
    /// </returns>
    ScheduleResult Run(string name, string folder);

    /// <summary>
    /// Retrieves the execution history for a named task, most-recent-first where supported by the backend.
    /// </summary>
    /// <param name="name">The name of the task whose history to retrieve.</param>
    /// <param name="folder">The scheduler folder containing the task.</param>
    /// <returns>
    /// A read-only list of <see cref="TaskRunRecord"/> entries. Returns an empty list when no
    /// history is available or the backend does not support history. Never returns null.
    /// </returns>
    IReadOnlyList<TaskRunRecord> GetHistory(string name, string folder);
}
