using System.Runtime.InteropServices;

namespace Winix.TimeIt;

public static partial class NativeMetrics
{
    // RUSAGE_CHILDREN on macOS (same value as Linux)
    private const int RUSAGE_CHILDREN_MACOS = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct TimeValMacOS
    {
        public long tv_sec;
        public int tv_usec;   // int (4 bytes) on macOS, not long

        public readonly TimeSpan ToTimeSpan()
        {
            return TimeSpan.FromTicks((tv_sec * TimeSpan.TicksPerSecond) + (tv_usec * (TimeSpan.TicksPerSecond / 1_000_000)));
        }
    }

    // Full RUsage struct for macOS (arm64/x86_64).
    // tv_usec is int (not long), which changes the struct layout vs Linux.
    [StructLayout(LayoutKind.Sequential)]
    private struct RUsageMacOS
    {
        public TimeValMacOS ru_utime;
        public TimeValMacOS ru_stime;
        public long ru_maxrss;      // peak RSS in bytes on macOS
        public long ru_ixrss;
        public long ru_idrss;
        public long ru_isrss;
        public long ru_minflt;
        public long ru_majflt;
        public long ru_nswap;
        public long ru_inblock;
        public long ru_oublock;
        public long ru_msgsnd;
        public long ru_msgrcv;
        public long ru_nsignals;
        public long ru_nvcsw;
        public long ru_nivcsw;
    }

    [LibraryImport("libSystem", EntryPoint = "getrusage")]
    private static partial int GetRUsageMacOS(int who, out RUsageMacOS usage);

    private static partial MetricsBaseline CaptureBaselineMacOS()
    {
        if (GetRUsageMacOS(RUSAGE_CHILDREN_MACOS, out RUsageMacOS usage) != 0)
        {
            return default;
        }

        return new MetricsBaseline
        {
            UserSeconds = usage.ru_utime.tv_sec,
            UserMicroseconds = usage.ru_utime.tv_usec,
            SystemSeconds = usage.ru_stime.tv_sec,
            SystemMicroseconds = usage.ru_stime.tv_usec,
            PeakRssRaw = usage.ru_maxrss,
        };
    }

    private static partial ProcessMetrics GetMetricsMacOS(MetricsBaseline baseline)
    {
        if (GetRUsageMacOS(RUSAGE_CHILDREN_MACOS, out RUsageMacOS usage) != 0)
        {
            return default;
        }

        TimeSpan postUser = usage.ru_utime.ToTimeSpan();
        TimeSpan postSys = usage.ru_stime.ToTimeSpan();
        TimeSpan baseUser = new TimeValMacOS { tv_sec = baseline.UserSeconds, tv_usec = (int)baseline.UserMicroseconds }.ToTimeSpan();
        TimeSpan baseSys = new TimeValMacOS { tv_sec = baseline.SystemSeconds, tv_usec = (int)baseline.SystemMicroseconds }.ToTimeSpan();

        return new ProcessMetrics
        {
            UserCpuTime = postUser - baseUser,
            SystemCpuTime = postSys - baseSys,
            // ru_maxrss is in bytes on macOS (not KB like Linux)
            PeakMemoryBytes = usage.ru_maxrss,
        };
    }
}
