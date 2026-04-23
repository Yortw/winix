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

    /// <summary>
    /// Exit code of the last child process run. When <see cref="Outcome"/> is
    /// <see cref="RetryOutcome.LaunchFailed"/> this is the tool's own exit code classification
    /// for the launch failure (e.g. <c>ExitCode.NotFound</c>), not a value returned by the child.
    /// </summary>
    public int ChildExitCode { get; }

    /// <summary>How the retry loop terminated.</summary>
    public RetryOutcome Outcome { get; }

    /// <summary>Total wall time including delays between attempts.</summary>
    public TimeSpan TotalTime { get; }

    /// <summary>Actual delay durations between attempts (for JSON reporting).</summary>
    public IReadOnlyList<TimeSpan> Delays { get; }

    /// <summary>
    /// Non-null only when <see cref="Outcome"/> is <see cref="RetryOutcome.LaunchFailed"/>. Carries
    /// the exception that aborted the retry loop. Preserves the original failure type so Program.cs
    /// can map it to the right POSIX exit code (127 for not-found, 126 for not-executable, etc.)
    /// without losing the partial history in <see cref="Attempts"/>, <see cref="Delays"/>, and
    /// <see cref="TotalTime"/>.
    /// </summary>
    public Exception? LaunchError { get; }

    /// <summary>
    /// Creates a new retry result.
    /// </summary>
    public RetryResult(int attempts, int maxAttempts, int childExitCode,
        RetryOutcome outcome, TimeSpan totalTime, IReadOnlyList<TimeSpan> delays,
        Exception? launchError = null)
    {
        Attempts = attempts;
        MaxAttempts = maxAttempts;
        ChildExitCode = childExitCode;
        Outcome = outcome;
        TotalTime = totalTime;
        Delays = delays;
        LaunchError = launchError;
    }
}
