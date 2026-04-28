#nullable enable

using System;
using System.Collections.Generic;

namespace Winix.Schedule;

/// <summary>
/// The outcome of <see cref="ISchedulerBackend.List"/> — carries the enumerated tasks plus
/// a diagnostic channel so a backend failure is not silently mistaken for an empty
/// scheduler. Pre-R4 the interface returned <see cref="IReadOnlyList{ScheduledTask}"/>
/// directly, and every non-zero schtasks exit / unreachable crontab collapsed to an empty
/// list with no signal — users on a wedged Task Scheduler service or a denied crontab
/// saw a clean "no tasks" output and concluded everything was fine.
/// </summary>
public sealed class ScheduleListResult
{
    /// <summary>
    /// Gets a value indicating whether the backend successfully enumerated tasks.
    /// When <c>false</c>, <see cref="Tasks"/> is empty and <see cref="FailureReason"/> carries the diagnostic.
    /// </summary>
    public bool Available { get; }

    /// <summary>
    /// Gets the enumerated tasks. Empty when <see cref="Available"/> is <c>false</c>.
    /// </summary>
    public IReadOnlyList<ScheduledTask> Tasks { get; }

    /// <summary>
    /// Gets a non-empty warning surfaced from a successful enumeration, or <c>null</c>
    /// when none. Mirrors <see cref="ScheduleResult.Warning"/> semantics: the underlying
    /// scheduler tool emitted stderr while still reporting a working enumeration. Callers
    /// surface this so partial-success can't hide a dropped task line or a permission notice.
    /// </summary>
    public string? Warning { get; }

    /// <summary>
    /// Gets the diagnostic explaining why <see cref="Available"/> is <c>false</c>. <c>null</c>
    /// when the enumeration succeeded.
    /// </summary>
    public string? FailureReason { get; }

    private ScheduleListResult(bool available, IReadOnlyList<ScheduledTask> tasks, string? warning, string? failureReason)
    {
        Available = available;
        Tasks = tasks;
        Warning = warning;
        FailureReason = failureReason;
    }

    /// <summary>
    /// Creates a successful enumeration result with no warning.
    /// </summary>
    public static ScheduleListResult Ok(IReadOnlyList<ScheduledTask> tasks) =>
        new ScheduleListResult(available: true, tasks, warning: null, failureReason: null);

    /// <summary>
    /// Creates a successful enumeration result that also carries a warning surfaced from
    /// the underlying tool. Whitespace-only warnings are normalised to no warning.
    /// </summary>
    public static ScheduleListResult OkWithWarning(IReadOnlyList<ScheduledTask> tasks, string? warning)
    {
        string? trimmed = string.IsNullOrWhiteSpace(warning) ? null : warning.Trim();
        return new ScheduleListResult(available: true, tasks, trimmed, failureReason: null);
    }

    /// <summary>
    /// Creates a failed enumeration result. <see cref="Tasks"/> is set to an empty list and
    /// <paramref name="reason"/> is captured verbatim into <see cref="FailureReason"/>.
    /// </summary>
    public static ScheduleListResult Unavailable(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Failure reason must not be empty.", nameof(reason));
        }
        return new ScheduleListResult(available: false, Array.Empty<ScheduledTask>(), warning: null, failureReason: reason.Trim());
    }
}
