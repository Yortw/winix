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
    public static TimeSpan Calculate(TimeSpan baseDelay, int attempt,
        BackoffStrategy strategy, bool jitter, Random? random)
    {
        double multiplier = strategy switch
        {
            BackoffStrategy.Fixed => 1.0,
            BackoffStrategy.Linear => attempt,
            BackoffStrategy.Exponential => Math.Pow(2, attempt - 1),
            _ => 1.0
        };

        double totalMs = baseDelay.TotalMilliseconds * multiplier;

        if (jitter && random != null && totalMs > 0)
        {
            // Scale to [0.5, 1.0) of calculated delay — reduces thundering herd without
            // allowing delay to exceed the computed backoff.
            double factor = 0.5 + (random.NextDouble() * 0.5);
            totalMs *= factor;
        }

        return TimeSpan.FromMilliseconds(totalMs);
    }
}
