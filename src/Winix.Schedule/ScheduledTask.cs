#nullable enable

using System;

namespace Winix.Schedule;

/// <summary>
/// Represents a scheduled task as reported by the OS task scheduler.
/// </summary>
public sealed class ScheduledTask
{
    /// <summary>The task name (leaf name, not full path).</summary>
    public string Name { get; }

    /// <summary>
    /// The schedule expression (e.g. cron string or human-readable trigger description).
    /// Empty when not available.
    /// </summary>
    public string Schedule { get; }

    /// <summary>
    /// The next scheduled run time. Null when no future run is scheduled or the value is unavailable.
    /// </summary>
    public DateTime? NextRun { get; }

    /// <summary>
    /// Last-known status string (e.g. "Ready", "Running", "Disabled").
    /// Empty when not available.
    /// </summary>
    public string Status { get; }

    /// <summary>
    /// The command (executable path or action) the task runs.
    /// Empty when not available or when access is denied.
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// The task folder path (e.g. "\" for the root folder on Windows Task Scheduler).
    /// Empty when not applicable or unavailable.
    /// </summary>
    public string Folder { get; }

    /// <summary>Creates a new ScheduledTask.</summary>
    /// <param name="name">Task name; null is treated as empty.</param>
    /// <param name="schedule">Schedule expression; null is treated as empty.</param>
    /// <param name="nextRun">Next scheduled run time; null when unavailable.</param>
    /// <param name="status">Status string; null is treated as empty.</param>
    /// <param name="command">Command/action; null is treated as empty.</param>
    /// <param name="folder">Folder path; null is treated as empty.</param>
    public ScheduledTask(
        string name,
        string schedule = "",
        DateTime? nextRun = null,
        string status = "",
        string command = "",
        string folder = "")
    {
        Name = name ?? "";
        Schedule = schedule ?? "";
        NextRun = nextRun;
        Status = status ?? "";
        Command = command ?? "";
        Folder = folder ?? "";
    }
}
