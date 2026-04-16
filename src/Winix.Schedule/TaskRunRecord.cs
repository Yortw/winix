#nullable enable

using System;

namespace Winix.Schedule;

/// <summary>
/// An immutable record of a single execution of a scheduled task.
/// </summary>
public sealed class TaskRunRecord
{
    /// <summary>
    /// Gets the time at which this task execution started.
    /// </summary>
    public DateTimeOffset StartTime { get; }

    /// <summary>
    /// Gets the exit code returned by the task process, or <c>null</c> if the task is still running
    /// or the exit code was unavailable.
    /// </summary>
    public int? ExitCode { get; }

    /// <summary>
    /// Gets the elapsed time of the task execution, or <c>null</c> if the task has not yet finished
    /// or the duration was unavailable.
    /// </summary>
    public TimeSpan? Duration { get; }

    /// <summary>
    /// Initialises a new <see cref="TaskRunRecord"/>.
    /// </summary>
    /// <param name="startTime">The time the task execution started.</param>
    /// <param name="exitCode">The process exit code, or <c>null</c> if unavailable.</param>
    /// <param name="duration">The total run duration, or <c>null</c> if unavailable.</param>
    public TaskRunRecord(DateTimeOffset startTime, int? exitCode, TimeSpan? duration)
    {
        StartTime = startTime;
        ExitCode = exitCode;
        Duration = duration;
    }
}
