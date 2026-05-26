namespace Winix.Retry;

/// <summary>
/// Calculates delay durations for retry attempts based on strategy and optional jitter.
/// </summary>
public static class BackoffCalculator
{
    /// <summary>
    /// Calculates the delay for a given retry attempt number.
    /// </summary>
    /// <param name="baseDelay">The base delay configured by the user.</param>
    /// <param name="attempt">The retry attempt number (1-indexed: 1 = first retry).</param>
    /// <param name="strategy">How the delay scales across attempts.</param>
    /// <param name="jitter">Whether to randomise the delay within [50%, 100%) of the calculated value.</param>
    /// <param name="random">Random instance for jitter. Required when <paramref name="jitter"/> is true.</param>
    /// <returns>The delay to wait before this attempt.</returns>
    /// <summary>
    /// Practical upper bound for a single delay. `TimeSpan.MaxValue` (~10,675,199 days) would cause
    /// `WaitHandle.WaitOne(TimeSpan)` to overflow its internal `Convert.ToInt32(ms)` (ceiling
    /// ~24.8 days). One hour is a sensible business cap: exponential backoff reaching it means the
    /// retry loop has effectively given up, and retrying that slowly almost always indicates the
    /// user's config is wrong.
    /// </summary>
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromHours(1);

    public static TimeSpan Calculate(TimeSpan baseDelay, int attempt,
        BackoffStrategy strategy, bool jitter, Random? random)
    {
        // Zero base delay always yields zero regardless of strategy/jitter/attempt. Without this
        // short-circuit, 0 * Math.Pow(2, attempt-1) for large attempts becomes 0 * Infinity = NaN,
        // and the NaN clamp below returns MaxDelay (1h) — a silent contract flip where "zero
        // delay" becomes "1-hour delay" at large attempt numbers. Round-4 M1 fix.
        if (baseDelay == TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        // Explicit enum exhaustiveness: throwing on unknown variants prevents a silent "fixed"
        // degradation if a new BackoffStrategy value is added without extending this switch.
        double multiplier = strategy switch
        {
            BackoffStrategy.Fixed => 1.0,
            BackoffStrategy.Linear => attempt,
            BackoffStrategy.Exponential => Math.Pow(2, attempt - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "unhandled backoff strategy")
        };

        double totalMs = baseDelay.TotalMilliseconds * multiplier;

        if (jitter && random != null && totalMs > 0)
        {
            // Scale to [0.5, 1.0) of calculated delay — reduces thundering herd without
            // allowing delay to exceed the computed backoff.
            double factor = 0.5 + (random.NextDouble() * 0.5);
            totalMs *= factor;
        }

        // Clamp before TimeSpan.FromMilliseconds to avoid OverflowException on extreme inputs.
        // Exponential backoff at attempt ~27 with a 1-day base delay overflows double's range for
        // TimeSpan.MaxValue (~9.22e15 ms); even reasonable inputs (1-minute base × exp × 35
        // attempts) hit the wall. Cap silently — the user who configured this got what they
        // asked for (eventually a large delay); crashing here would be strictly worse.
        if (double.IsNaN(totalMs) || double.IsInfinity(totalMs) || totalMs > MaxDelay.TotalMilliseconds)
        {
            return MaxDelay;
        }
        if (totalMs < 0)
        {
            // Defensive: baseDelay.TotalMilliseconds is non-negative by TimeSpan construction,
            // multiplier is non-negative, jitter factor is [0.5, 1.0). So totalMs < 0 is
            // unreachable in practice, but the check is cheap and makes the return-to-TimeSpan
            // contract explicit for future maintainers.
            return TimeSpan.Zero;
        }
        return TimeSpan.FromMilliseconds(totalMs);
    }
}
