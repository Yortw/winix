namespace Winix.Wargs;

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
public sealed record JobResult(
    int JobIndex,
    int ChildExitCode,
    string? Output,
    TimeSpan Duration,
    string[] SourceItems,
    bool Skipped,
    string? FaultMessage = null
);
