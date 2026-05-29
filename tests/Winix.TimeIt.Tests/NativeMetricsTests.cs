#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using Winix.TimeIt;
using Xunit;

namespace Winix.TimeIt.Tests;

// Round-1 review TA-I4 — per-platform NativeMetrics tests pinning the unit-conversion
// contract. Per `feedback_platform_branch_test_every_path`, each platform branch needs
// dedicated coverage. These complement the existing CommandRunnerTests by:
//
// 1. Calling NativeMetrics.CaptureBaseline + GetMetrics directly (rather than via
//    CommandRunner), so the platform-specific conversion is the load-bearing logic.
// 2. Pinning the ru_maxrss→bytes conversion contract deterministically via the pure
//    LinuxPeakBytes_*/MacOsPeakBytes_* tests. (The live *_PeakMemoryInPlausibleByteRange
//    tests are now only no-throw/sanity smokes — a magnitude floor on the RUSAGE_CHILDREN
//    delta flaked, because the cross-child high-water-mark delta can be a legitimately tiny
//    positive in a multi-child test process. See those tests for the full explanation.)
// 3. Asserting CPU times are non-negative for a process that did real work.
//
// Each platform's branch is gated via SkippableFact + Skip.IfNot per CLAUDE.md (not the
// silent-early-return pattern, which would CI-pass on the wrong OS).
public class NativeMetricsTests
{
    private static Process StartTestProcess()
    {
        // dotnet --version is portable, fast, and exercises real CPU + memory.
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--version");
        var process = Process.Start(psi)!;
        return process;
    }

    [SkippableFact]
    public void Linux_GetMetrics_PeakMemoryInPlausibleByteRange()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux-only — pins the ru_maxrss KB→bytes conversion.");
        if (!OperatingSystem.IsLinux()) return; // satisfies CA1416 alongside Skip.IfNot

        var baseline = NativeMetrics.CaptureBaseline();
        var process = StartTestProcess();
        process.WaitForExit();
        var metrics = NativeMetrics.GetMetrics(process, baseline);
        process.Dispose();

        // Live smoke: the getrusage P/Invoke + struct layout must run without throwing and return
        // a sane value. The KB→bytes conversion contract is pinned deterministically by
        // LinuxPeakBytes_* above — NOT by a magnitude floor here, which flaked because the
        // RUSAGE_CHILDREN delta can be a legitimately tiny positive in a multi-child test process.
        if (metrics.PeakMemoryBytes is long peak)
        {
            Assert.True(peak > 0, "a non-null delta is positive by construction");
        }
    }

    [SkippableFact]
    public void MacOS_GetMetrics_PeakMemoryInPlausibleByteRange()
    {
        Skip.IfNot(OperatingSystem.IsMacOS(), "macOS-only — pins the ru_maxrss bytes-as-bytes contract.");
        if (!OperatingSystem.IsMacOS()) return;

        var baseline = NativeMetrics.CaptureBaseline();
        var process = StartTestProcess();
        process.WaitForExit();
        var metrics = NativeMetrics.GetMetrics(process, baseline);
        process.Dispose();

        // Live smoke only (see Linux twin): the bytes-identity conversion contract is pinned
        // deterministically by MacOsPeakBytes_IsIdentityNotKilobytes above. The old magnitude
        // floor flaked on the RUSAGE_CHILDREN cross-child delta in a multi-child test process.
        if (metrics.PeakMemoryBytes is long peak)
        {
            Assert.True(peak > 0, "a non-null delta is positive by construction");
        }
    }

    [SkippableFact]
    public void Windows_GetMetrics_PeakMemoryInPlausibleByteRange()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only — pins GetProcessMemoryInfo.PeakWorkingSetSize handling.");
        if (!OperatingSystem.IsWindows()) return;

        var baseline = NativeMetrics.CaptureBaseline();
        var process = StartTestProcess();
        process.WaitForExit();
        var metrics = NativeMetrics.GetMetrics(process, baseline);
        process.Dispose();

        // Windows uses GetProcessMemoryInfo which returns PeakWorkingSetSize in bytes
        // (no conversion). A regression dividing by 1024 would report < 100 KB for any
        // real process; multiplying would report > 1 TB.
        Assert.NotNull(metrics.PeakMemoryBytes);
        Assert.InRange(metrics.PeakMemoryBytes!.Value, 1_000_000L, 10_000_000_000L);
    }

    [SkippableFact]
    public void Linux_GetMetrics_CpuTimes_NonNegative()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux-only — getrusage-based CPU time pin.");
        if (!OperatingSystem.IsLinux()) return;

        var baseline = NativeMetrics.CaptureBaseline();
        var process = StartTestProcess();
        process.WaitForExit();
        var metrics = NativeMetrics.GetMetrics(process, baseline);
        process.Dispose();

        Assert.NotNull(metrics.UserCpuTime);
        Assert.NotNull(metrics.SystemCpuTime);
        Assert.True(metrics.UserCpuTime!.Value >= TimeSpan.Zero);
        Assert.True(metrics.SystemCpuTime!.Value >= TimeSpan.Zero);
    }

    [SkippableFact]
    public void MacOS_GetMetrics_CpuTimes_NonNegative()
    {
        Skip.IfNot(OperatingSystem.IsMacOS(), "macOS-only — getrusage-based CPU time pin.");
        if (!OperatingSystem.IsMacOS()) return;

        var baseline = NativeMetrics.CaptureBaseline();
        var process = StartTestProcess();
        process.WaitForExit();
        var metrics = NativeMetrics.GetMetrics(process, baseline);
        process.Dispose();

        Assert.NotNull(metrics.UserCpuTime);
        Assert.NotNull(metrics.SystemCpuTime);
        Assert.True(metrics.UserCpuTime!.Value >= TimeSpan.Zero);
        Assert.True(metrics.SystemCpuTime!.Value >= TimeSpan.Zero);
    }

    [SkippableFact]
    public void Windows_GetMetrics_CpuTimes_NonNegative()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only — GetProcessTimes-based CPU time pin.");
        if (!OperatingSystem.IsWindows()) return;

        var baseline = NativeMetrics.CaptureBaseline();
        var process = StartTestProcess();
        process.WaitForExit();
        var metrics = NativeMetrics.GetMetrics(process, baseline);
        process.Dispose();

        Assert.NotNull(metrics.UserCpuTime);
        Assert.NotNull(metrics.SystemCpuTime);
        Assert.True(metrics.UserCpuTime!.Value >= TimeSpan.Zero);
        Assert.True(metrics.SystemCpuTime!.Value >= TimeSpan.Zero);
    }

    // Deterministic, cross-platform pins for the ru_maxrss→bytes conversion contract. These
    // replace the old reliance on a live-process magnitude floor, which was flaky: ru_maxrss
    // (RUSAGE_CHILDREN) is a high-water mark across ALL waited children, so in a multi-child
    // test process the per-child delta can be a legitimately tiny positive value (a CI run
    // produced 896 KB → 917504 bytes, below the old 1 MB floor). The conversion itself is what
    // matters, and it's now pinned here without depending on live process memory.
    [Fact]
    public void LinuxPeakBytes_ConvertsKilobyteDeltaToBytes()
    {
        // Linux ru_maxrss is in KB → ×1024. 896 KB is the exact delta the flaky CI run saw.
        Assert.Equal(917504L, NativeMetrics.LinuxPeakBytesFromDeltaKb(896));
    }

    [Fact]
    public void LinuxPeakBytes_NonPositiveDeltaIsNull()
    {
        // Delta ≤ 0 means a previously-waited child had an equal/greater peak — can't attribute.
        Assert.Null(NativeMetrics.LinuxPeakBytesFromDeltaKb(0));
        Assert.Null(NativeMetrics.LinuxPeakBytesFromDeltaKb(-5));
    }

    [Fact]
    public void MacOsPeakBytes_IsIdentityNotKilobytes()
    {
        // macOS ru_maxrss is already in BYTES (BSD-derived) — identity, NOT ×1024. Pins the
        // platform difference a "treat-like-Linux" regression would break.
        Assert.Equal(917504L, NativeMetrics.MacOsPeakBytesFromDelta(917504));
        Assert.Null(NativeMetrics.MacOsPeakBytesFromDelta(0));
    }

    [Fact]
    public void CaptureBaseline_DoesNotThrow_OnAnyPlatform()
    {
        // Sanity: baseline capture is a no-op on Windows and a getrusage call on Unix.
        // Should never throw, regardless of platform.
        var baseline = NativeMetrics.CaptureBaseline();
        // No assertion — the contract is "doesn't throw". Calling default(MetricsBaseline)
        // would also satisfy this; the test passes if we got here without throwing.
        _ = baseline;
    }
}
