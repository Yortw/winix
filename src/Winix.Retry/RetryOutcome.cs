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
    NotRetryable,

    /// <summary>
    /// Child process failed to launch during an attempt (command not found, not executable, or
    /// unexpected start failure). Unlike <see cref="NotRetryable"/>, the process never ran and no
    /// exit code was returned by the child — partial history (attempts completed, delays incurred)
    /// is still preserved in the <see cref="RetryResult"/>, but <see cref="RetryResult.LaunchError"/>
    /// carries the failure details.
    /// </summary>
    LaunchFailed,

    /// <summary>
    /// The retry loop was cancelled by the caller (Ctrl+C, CancellationToken signalled mid-run).
    /// Partial history (attempts completed, delays) is preserved. Distinguished from
    /// <see cref="RetriesExhausted"/> so dashboards and CI scripts can tell user-initiated
    /// cancellation apart from genuine exhaustion — the latter is a failure mode worth alerting
    /// on; the former is routine. <see cref="RetryResult.ChildExitCode"/> is the child's last
    /// observed exit code (usually a kill-signal value like 137 on Unix); consumers should treat
    /// it as diagnostic only, not a meaningful success/failure signal.
    /// </summary>
    Cancelled
}
