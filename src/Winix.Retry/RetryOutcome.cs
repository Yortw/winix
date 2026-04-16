namespace Winix.Retry;

/// <summary>
/// How the retry loop terminated.
/// </summary>
public enum RetryOutcome
{
    /// <summary>A target exit code was reached (default: exit 0).</summary>
    Succeeded,

    /// <summary>All retry attempts exhausted without reaching target.</summary>
    RetriesExhausted,

    /// <summary>Exit code was not in the --on set — stopped early.</summary>
    NotRetryable
}
