namespace Winix.TimeIt;

/// <summary>
/// Immutable result of timing a child process.
/// Null metrics indicate the OS could not provide the value.
/// </summary>
/// <param name="WallTime">Elapsed wall-clock time.</param>
/// <param name="UserCpuTime">User-mode CPU time of the child process, or null if unavailable.</param>
/// <param name="SystemCpuTime">Kernel-mode CPU time of the child process, or null if unavailable.</param>
/// <param name="PeakMemoryBytes">Peak working set of the child process in bytes, or null if unavailable.</param>
/// <param name="ExitCode">Exit code of the child process.</param>
public sealed record TimeItResult(
    TimeSpan WallTime,
    TimeSpan? UserCpuTime,
    TimeSpan? SystemCpuTime,
    long? PeakMemoryBytes,
    int ExitCode
)
{
    /// <summary>
    /// Total CPU time (user + system). Null if either component is unavailable.
    /// </summary>
    public TimeSpan? TotalCpuTime => (UserCpuTime != null && SystemCpuTime != null)
        ? UserCpuTime.Value + SystemCpuTime.Value
        : null;
}
