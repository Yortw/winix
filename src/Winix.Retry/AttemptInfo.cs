namespace Winix.Retry;

/// <summary>
/// Information about a single retry attempt, passed to the progress callback.
/// </summary>
public sealed class AttemptInfo
{
    /// <summary>Which attempt just completed (1-indexed).</summary>
    public int Attempt { get; }

    /// <summary>Total max attempts allowed (initial + retries).</summary>
    public int MaxAttempts { get; }

    /// <summary>Exit code from this attempt's child process.</summary>
    public int ExitCode { get; }

    /// <summary>Delay before the next attempt, or null if this is the final attempt or loop is stopping.</summary>
    public TimeSpan? NextDelay { get; }

    /// <summary>Whether this attempt will be followed by another retry.</summary>
    public bool WillRetry { get; }

    /// <summary>Why the loop is stopping (null if <see cref="WillRetry"/> is true).</summary>
    public RetryOutcome? StopReason { get; }

    /// <summary>
    /// Creates a new attempt info record.
    /// </summary>
    public AttemptInfo(int attempt, int maxAttempts, int exitCode,
        TimeSpan? nextDelay, bool willRetry, RetryOutcome? stopReason)
    {
        Attempt = attempt;
        MaxAttempts = maxAttempts;
        ExitCode = exitCode;
        NextDelay = nextDelay;
        WillRetry = willRetry;
        StopReason = stopReason;
    }
}
