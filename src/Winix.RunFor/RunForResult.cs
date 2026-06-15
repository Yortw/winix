using System;
using Winix.ProcessSupervision;

namespace Winix.RunFor;

/// <summary>
/// The immutable outcome of a <c>runfor</c> invocation. Library-produced (the caller only observes
/// it), so all properties are get-only; construct via the static factories.
/// </summary>
public sealed class RunForResult
{
    /// <summary>How the invocation ended.</summary>
    public RunForOutcome Outcome { get; private init; }

    /// <summary>runfor's own exit code (forwarded child code, 124, 130, or 126/127).</summary>
    public int ExitCode { get; private init; }

    /// <summary>The child's exit code when it ran to completion; <c>null</c> for timeout/interrupt/launch-fail.</summary>
    public int? ChildExitCode { get; private init; }

    /// <summary>
    /// True when a kill was attempted at the deadline/interrupt but could not be confirmed
    /// (the child may still be running). Surfaced as a warning. Always false for the coreutils
    /// signal-only default and for a clean completion.
    /// </summary>
    public bool KillFailed { get; private init; }

    /// <summary>Wall-clock time from launch to resolution.</summary>
    public TimeSpan Duration { get; private init; }

    /// <summary>The child exited before the deadline; forward its code.</summary>
    public static RunForResult Completed(int childExitCode, TimeSpan duration) => new()
    {
        Outcome = RunForOutcome.Completed,
        ExitCode = childExitCode,
        ChildExitCode = childExitCode,
        Duration = duration,
    };

    /// <summary>The deadline fired; runfor returns 124.</summary>
    public static RunForResult TimedOut(TimeSpan duration, bool killFailed) => new()
    {
        Outcome = RunForOutcome.TimedOut,
        ExitCode = SupervisionExitCode.Timeout,
        ChildExitCode = null,
        KillFailed = killFailed,
        Duration = duration,
    };

    /// <summary>Ctrl+C; runfor returns 130.</summary>
    public static RunForResult Interrupted(TimeSpan duration, bool killFailed) => new()
    {
        Outcome = RunForOutcome.Interrupted,
        ExitCode = SupervisionExitCode.Interrupted,
        ChildExitCode = null,
        KillFailed = killFailed,
        Duration = duration,
    };

    /// <summary>The child never started; <paramref name="exitCode"/> is the classified 126/127.</summary>
    public static RunForResult LaunchFailed(int exitCode, TimeSpan duration) => new()
    {
        Outcome = RunForOutcome.LaunchFailed,
        ExitCode = exitCode,
        ChildExitCode = null,
        Duration = duration,
    };
}
