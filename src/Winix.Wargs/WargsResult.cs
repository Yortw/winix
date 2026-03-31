namespace Winix.Wargs;

/// <summary>
/// Summary of all job executions.
/// </summary>
/// <param name="TotalJobs">Number of invocations produced by the command builder.</param>
/// <param name="Succeeded">Jobs that exited 0.</param>
/// <param name="Failed">Jobs that exited non-zero.</param>
/// <param name="Skipped">Jobs not executed (confirm declined, fail-fast stopped, etc.).</param>
/// <param name="WallTime">Total wall-clock time for the entire run.</param>
/// <param name="Jobs">Per-job results in input order.</param>
public sealed record WargsResult(
    int TotalJobs,
    int Succeeded,
    int Failed,
    int Skipped,
    TimeSpan WallTime,
    List<JobResult> Jobs
);
