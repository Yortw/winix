namespace Winix.Wargs;

/// <summary>
/// Why a job was skipped. Round-12 SFH+TA I4: the prior `Skipped: bool` field couldn't
/// distinguish a confirm-declined skip from a fail-fast-aborted skip from an external-cancel
/// skip. The fail_fast_abort exit_reason classifier in Program.cs needs to fire ONLY when at
/// least one job was skipped due to fail-fast, not when a confirm-declined skip happened to
/// coexist with an unrelated child failure.
/// </summary>
public enum SkipReason
{
    /// <summary>The job was not skipped — Skipped is false.</summary>
    NotSkipped = 0,
    /// <summary>The user declined the --confirm prompt for this job.</summary>
    ConfirmDeclined = 1,
    /// <summary>--fail-fast triggered before this job ran (an earlier job failed).</summary>
    FailFastAbort = 2,
    /// <summary>External cancellation (Ctrl+C) signalled before/during this job.</summary>
    ExternalCancel = 3,
}

/// <summary>
/// Result of a single job execution.
/// </summary>
/// <param name="JobIndex">1-based input-order index.</param>
/// <param name="ChildExitCode">The child process exit code. -1 if the process could not be spawned.</param>
/// <param name="Output">Captured stdout+stderr. Null in line-buffered mode.</param>
/// <param name="Duration">How long the job took.</param>
/// <param name="SourceItems">The input items for this job.</param>
/// <param name="Skipped">True if the job was skipped (e.g. confirm declined, fail-fast, not spawnable).</param>
/// <param name="FaultMessage">
/// Diagnostic message captured when the job could not be spawned or a task body threw an
/// unexpected exception. Null on normal success/failure paths. Surfaces the original error
/// (e.g. "Win32Exception: No such file or directory") so the user isn't left with a bare
/// `child_failed` exit reason and no clue why the process didn't run.
/// </param>
/// <param name="SkipReason">
/// When <paramref name="Skipped"/> is true, identifies WHY the job was skipped so the
/// classifier in Program.cs can distinguish fail_fast_abort from confirm-declined-coincident-
/// with-failure. Default <see cref="Winix.Wargs.SkipReason.NotSkipped"/> for normal jobs.
/// </param>
public sealed record JobResult(
    int JobIndex,
    int ChildExitCode,
    string? Output,
    TimeSpan Duration,
    string[] SourceItems,
    bool Skipped,
    string? FaultMessage = null,
    SkipReason SkipReason = SkipReason.NotSkipped
);
