namespace Winix.Retry;

/// <summary>
/// Final result of a retry sequence.
/// </summary>
public sealed class RetryResult
{
    /// <summary>Total number of attempts made (initial run + retries).</summary>
    public int Attempts { get; }

    /// <summary>Maximum attempts that were allowed (MaxRetries + 1).</summary>
    public int MaxAttempts { get; }

    /// <summary>Exit code of the last child process run.</summary>
    public int ChildExitCode { get; }

    /// <summary>How the retry loop terminated.</summary>
    public RetryOutcome Outcome { get; }

    /// <summary>Total wall time including delays between attempts.</summary>
    public TimeSpan TotalTime { get; }

    /// <summary>Actual delay durations between attempts (for JSON reporting).</summary>
    public IReadOnlyList<TimeSpan> Delays { get; }

    /// <summary>
    /// Creates a new retry result.
    /// </summary>
    public RetryResult(int attempts, int maxAttempts, int childExitCode,
        RetryOutcome outcome, TimeSpan totalTime, IReadOnlyList<TimeSpan> delays)
    {
        Attempts = attempts;
        MaxAttempts = maxAttempts;
        ChildExitCode = childExitCode;
        Outcome = outcome;
        TotalTime = totalTime;
        Delays = delays;
    }
}
