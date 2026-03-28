using System.Runtime.InteropServices;

namespace Winix.TimeIt;

public static partial class NativeMetrics
{
    // RUSAGE_CHILDREN on Linux
    private const int RUSAGE_CHILDREN_LINUX = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct TimeValLinux
    {
        public long tv_sec;
        public long tv_usec;

        public readonly TimeSpan ToTimeSpan()
        {
            return TimeSpan.FromTicks((tv_sec * TimeSpan.TicksPerSecond) + (tv_usec * (TimeSpan.TicksPerSecond / 1_000_000)));
        }
    }

    // Full RUsage struct for x86_64 Linux.
    // We only use ru_utime, ru_stime, and ru_maxrss but must declare all preceding
    // fields to get the correct offsets.
    [StructLayout(LayoutKind.Sequential)]
    private struct RUsageLinux
    {
        public TimeValLinux ru_utime;
        public TimeValLinux ru_stime;
        public long ru_maxrss;      // peak RSS in kilobytes on Linux
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

    [LibraryImport("libc", EntryPoint = "getrusage")]
    private static partial int GetRUsageLinux(int who, out RUsageLinux usage);

    private static partial MetricsBaseline CaptureBaselineLinux()
    {
        if (GetRUsageLinux(RUSAGE_CHILDREN_LINUX, out RUsageLinux usage) != 0)
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

    private static partial ProcessMetrics GetMetricsLinux(MetricsBaseline baseline)
    {
        if (GetRUsageLinux(RUSAGE_CHILDREN_LINUX, out RUsageLinux usage) != 0)
        {
            return default;
        }

        TimeSpan postUser = usage.ru_utime.ToTimeSpan();
        TimeSpan postSys = usage.ru_stime.ToTimeSpan();
        TimeSpan baseUser = new TimeValLinux { tv_sec = baseline.UserSeconds, tv_usec = baseline.UserMicroseconds }.ToTimeSpan();
        TimeSpan baseSys = new TimeValLinux { tv_sec = baseline.SystemSeconds, tv_usec = baseline.SystemMicroseconds }.ToTimeSpan();

        return new ProcessMetrics
        {
            UserCpuTime = postUser - baseUser,
            SystemCpuTime = postSys - baseSys,
            // ru_maxrss is in kilobytes on Linux
            PeakMemoryBytes = usage.ru_maxrss * 1024,
        };
    }
}
