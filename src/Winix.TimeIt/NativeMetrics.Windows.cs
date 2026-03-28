using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Winix.TimeIt;

public static partial class NativeMetrics
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MEMORY_COUNTERS
    {
        public uint cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
    }

    [LibraryImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(
        SafeProcessHandle hProcess,
        out long lpCreationTime,
        out long lpExitTime,
        out long lpKernelTime,
        out long lpUserTime);

    [LibraryImport("psapi")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessMemoryInfo(
        SafeProcessHandle hProcess,
        out PROCESS_MEMORY_COUNTERS ppsmemCounters,
        uint cb);

    private static ProcessMetrics GetMetricsWindows(Process process)
    {
        TimeSpan? userCpu = null;
        TimeSpan? sysCpu = null;
        long? peakMemory = null;

        if (GetProcessTimes(
                process.SafeHandle,
                out _,
                out _,
                out long kernelTime,
                out long userTime))
        {
            // FILETIME ticks are 100-nanosecond intervals, same as TimeSpan.Ticks
            userCpu = TimeSpan.FromTicks(userTime);
            sysCpu = TimeSpan.FromTicks(kernelTime);
        }

        var counters = new PROCESS_MEMORY_COUNTERS();
        counters.cb = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS>();
        if (GetProcessMemoryInfo(process.SafeHandle, out counters, counters.cb))
        {
            peakMemory = (long)counters.PeakWorkingSetSize;
        }

        return new ProcessMetrics
        {
            UserCpuTime = userCpu,
            SystemCpuTime = sysCpu,
            PeakMemoryBytes = peakMemory,
        };
    }
}
