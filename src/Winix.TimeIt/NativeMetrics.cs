using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Winix.TimeIt;

/// <summary>
/// Raw process metrics collected via platform-native APIs.
/// Null fields indicate the OS did not provide the metric.
/// </summary>
public readonly struct ProcessMetrics
{
    /// <summary>User-mode CPU time, or null if unavailable.</summary>
    public TimeSpan? UserCpuTime { get; init; }

    /// <summary>Kernel-mode CPU time, or null if unavailable.</summary>
    public TimeSpan? SystemCpuTime { get; init; }

    /// <summary>Peak working set in bytes, or null if unavailable.</summary>
    public long? PeakMemoryBytes { get; init; }
}

/// <summary>
/// Opaque baseline snapshot for delta-based metric collection.
/// On Windows this is empty — metrics come from the process handle.
/// On Unix this holds a pre-spawn <c>getrusage(RUSAGE_CHILDREN)</c> snapshot.
/// </summary>
public readonly struct MetricsBaseline
{
    internal long UserSeconds { get; init; }
    internal long UserMicroseconds { get; init; }
    internal long SystemSeconds { get; init; }
    internal long SystemMicroseconds { get; init; }
    internal long PeakRssRaw { get; init; }
}

/// <summary>
/// Collects process metrics via platform-native APIs.
/// Never throws — returns null metrics on failure.
/// </summary>
public static partial class NativeMetrics
{
    /// <summary>
    /// Captures a pre-spawn baseline. On Unix, snapshots <c>getrusage(RUSAGE_CHILDREN)</c>.
    /// On Windows, returns a no-op baseline.
    /// Call immediately before <see cref="Process.Start()"/>.
    /// </summary>
    public static MetricsBaseline CaptureBaseline()
    {
        if (OperatingSystem.IsLinux())
        {
            return CaptureBaselineLinux();
        }

        if (OperatingSystem.IsMacOS())
        {
            return CaptureBaselineMacOS();
        }

        // Windows: no baseline needed
        return default;
    }

    /// <summary>
    /// Reads final metrics for the exited child process.
    /// Call after <see cref="Process.WaitForExit()"/>, before <see cref="Process.Dispose()"/>.
    /// </summary>
    public static ProcessMetrics GetMetrics(Process process, MetricsBaseline baseline)
    {
        if (OperatingSystem.IsWindows())
        {
            return GetMetricsWindows(process);
        }

        if (OperatingSystem.IsLinux())
        {
            return GetMetricsLinux(baseline);
        }

        if (OperatingSystem.IsMacOS())
        {
            return GetMetricsMacOS(baseline);
        }

        // Unsupported platform: all metrics unavailable
        return default;
    }

    /// <summary>Converts a Linux <c>RUSAGE_CHILDREN.ru_maxrss</c> delta (kilobytes) to peak bytes.
    /// ru_maxrss is a high-water mark across all waited children, so the caller passes the delta
    /// against a pre-spawn baseline to attribute only the just-waited child. A non-positive delta
    /// means a prior child had an equal/greater peak (can't attribute) → null. Linux reports KB → ×1024.</summary>
    internal static long? LinuxPeakBytesFromDeltaKb(long deltaKb)
        => deltaKb > 0 ? deltaKb * 1024 : null;

    /// <summary>macOS variant of <see cref="LinuxPeakBytesFromDeltaKb"/>: <c>ru_maxrss</c> is already
    /// in bytes (BSD-derived), so the conversion is identity — no ×1024.</summary>
    internal static long? MacOsPeakBytesFromDelta(long deltaBytes)
        => deltaBytes > 0 ? deltaBytes : null;

    // Platform-specific partial methods — implemented in per-platform files
    private static partial MetricsBaseline CaptureBaselineLinux();
    private static partial MetricsBaseline CaptureBaselineMacOS();
    private static partial ProcessMetrics GetMetricsWindows(Process process);
    private static partial ProcessMetrics GetMetricsLinux(MetricsBaseline baseline);
    private static partial ProcessMetrics GetMetricsMacOS(MetricsBaseline baseline);
}
