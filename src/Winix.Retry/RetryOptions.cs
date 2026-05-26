namespace Winix.Retry;

/// <summary>
/// Configuration for a retry sequence.
/// </summary>
public sealed class RetryOptions
{
    /// <summary>Maximum number of retry attempts (not counting the initial run).</summary>
    public int MaxRetries { get; }

    /// <summary>Base delay before retries.</summary>
    public TimeSpan Delay { get; }

    /// <summary>How the delay scales across attempts.</summary>
    public BackoffStrategy Backoff { get; }

    /// <summary>Whether to add random jitter to delay calculations.</summary>
    public bool Jitter { get; }

    /// <summary>
    /// Exit codes that trigger a retry (--on whitelist). Null means use default/StopOnCodes logic.
    /// </summary>
    public IReadOnlySet<int>? RetryOnCodes { get; }

    /// <summary>
    /// Exit codes that stop retrying (--until targets). Null means use default (stop on 0).
    /// </summary>
    public IReadOnlySet<int>? StopOnCodes { get; }

    /// <summary>
    /// Creates retry options with validation.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">MaxRetries is negative or Delay is negative.</exception>
    /// <exception cref="ArgumentException">Both retryOnCodes and stopOnCodes are specified.</exception>
    public RetryOptions(
        int maxRetries,
        TimeSpan delay,
        BackoffStrategy backoff = BackoffStrategy.Fixed,
        bool jitter = false,
        IReadOnlySet<int>? retryOnCodes = null,
        IReadOnlySet<int>? stopOnCodes = null)
    {
        if (maxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetries), maxRetries,
                "Max retries cannot be negative.");
        }
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay,
                "Delay cannot be negative.");
        }
        // --on and --until are contradictory: one whitelists codes to retry on, the other
        // whitelists codes to stop on. Allowing both would create ambiguous/conflicting rules.
        if (retryOnCodes != null && stopOnCodes != null)
        {
            throw new ArgumentException(
                "Cannot specify both --on and --until. They are contradictory.");
        }

        MaxRetries = maxRetries;
        Delay = delay;
        Backoff = backoff;
        Jitter = jitter;
        RetryOnCodes = retryOnCodes;
        StopOnCodes = stopOnCodes;
    }

    /// <summary>
    /// Determines whether the given exit code should trigger a retry.
    /// </summary>
    /// <param name="exitCode">The child process exit code.</param>
    /// <returns>True if the exit code warrants another attempt.</returns>
    public bool ShouldRetry(int exitCode)
    {
        if (RetryOnCodes != null)
        {
            // --on mode: retry only if code is in the whitelist. 0 always stops (success).
            return exitCode != 0 && RetryOnCodes.Contains(exitCode);
        }

        if (StopOnCodes != null)
        {
            // --until mode: stop if code is in the target set, retry everything else.
            return !StopOnCodes.Contains(exitCode);
        }

        // Default: retry on any non-zero (implicit --until 0).
        return exitCode != 0;
    }
}
