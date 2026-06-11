#nullable enable

using System;
using System.Collections.Generic;

namespace Winix.Online;

/// <summary>The three mutually-exclusive outcomes of a wait.</summary>
public enum WaitOutcome
{
    /// <summary>Every requested check passed — exit 0.</summary>
    Ready,
    /// <summary>The wait budget was exhausted before ready (wait mode) — exit 124.</summary>
    TimedOut,
    /// <summary><c>--once</c> single-cycle miss: not ready right now — exit 1.</summary>
    NotReady,
}

/// <summary>
/// Outcome of a wait. The single <see cref="Outcome"/> makes the illegal "ready and timed-out at once"
/// state unrepresentable; <see cref="Ready"/>/<see cref="TimedOut"/> are derived read-only projections.
/// </summary>
/// <param name="Outcome">The mutually-exclusive result.</param>
/// <param name="Attempts">Number of poll cycles run.</param>
/// <param name="Elapsed">Wall time as measured by the injected clock.</param>
/// <param name="LastChecks">Per-check results from the final cycle (for the JSON envelope / summary).</param>
public sealed record WaitResult(
    WaitOutcome Outcome,
    int Attempts,
    TimeSpan Elapsed,
    IReadOnlyList<CheckResult> LastChecks)
{
    /// <summary>True when the gate opened (exit 0).</summary>
    public bool Ready => Outcome == WaitOutcome.Ready;

    /// <summary>True when the wait timed out (exit 124).</summary>
    public bool TimedOut => Outcome == WaitOutcome.TimedOut;
}
