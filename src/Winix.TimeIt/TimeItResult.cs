namespace Winix.TimeIt;

/// <summary>
/// Immutable result of timing a child process.
/// </summary>
/// <param name="WallTime">Elapsed wall-clock time.</param>
/// <param name="CpuTime">Total CPU time (user + kernel).</param>
/// <param name="PeakMemoryBytes">Peak working set of the child process in bytes.</param>
/// <param name="ExitCode">Exit code of the child process.</param>
public sealed record TimeItResult(
    TimeSpan WallTime,
    TimeSpan CpuTime,
    long PeakMemoryBytes,
    int ExitCode
);
