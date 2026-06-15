using System;

namespace Winix.RunFor;

/// <summary>Validated configuration for one <c>runfor</c> invocation.</summary>
public sealed class RunForOptions
{
    /// <summary>The time budget before the deadline fires. Must be positive.</summary>
    public TimeSpan Deadline { get; }

    /// <summary>The Unix signal number sent at the deadline (default SIGTERM). Ignored on Windows.</summary>
    public int Signal { get; }

    /// <summary>
    /// The <c>--kill-after</c> grace window. <c>null</c> means signal-only (no SIGKILL backstop).
    /// <see cref="TimeSpan.Zero"/> means escalate to SIGKILL immediately after the signal.
    /// A positive value escalates after the specified grace period.
    /// </summary>
    public TimeSpan? KillAfter { get; }

    /// <param name="deadline">Positive time budget. Must be greater than <see cref="TimeSpan.Zero"/>.</param>
    /// <param name="signal">Signal number (see <see cref="Winix.ProcessSupervision.UnixSignal"/>).</param>
    /// <param name="killAfter">Grace window, or <c>null</c> for the signal-only default.
    /// Must be non-negative when provided.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="deadline"/> is zero or negative, or <paramref name="killAfter"/> is negative.
    /// </exception>
    public RunForOptions(TimeSpan deadline, int signal, TimeSpan? killAfter)
    {
        if (deadline <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(deadline), deadline, "Deadline must be positive.");
        }
        if (killAfter.HasValue && killAfter.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(killAfter), killAfter, "Kill-after grace cannot be negative.");
        }

        Deadline = deadline;
        Signal = signal;
        KillAfter = killAfter;
    }
}
