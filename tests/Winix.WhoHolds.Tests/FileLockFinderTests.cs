#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Winix.WhoHolds;
using Xunit;

namespace Winix.WhoHolds.Tests;

public sealed class FileLockFinderTests
{
    /// <summary>
    /// Total time the test will wait for the freshly-opened OS file lock to become
    /// observable to the Restart Manager API. Production code already retries internally
    /// (see <see cref="FileLockFinder"/>'s <c>MaxFindAttempts</c> × <c>FindRetryDelayMs</c>),
    /// but its budget (~250 ms) is sized for real users investigating long-held locks where
    /// quick "no one holds this" feedback matters more than absorbing CI flakiness.
    /// Tests fire queries microseconds after the lock opens under heavy parallel load,
    /// where RM's kernel-handle snapshot can take noticeably longer to converge —
    /// observed mid-suite-run failures with a passing isolated rerun. Outer poll budget
    /// is intentionally generous (CI flake tolerance), inner poll interval keeps the
    /// success path fast.
    /// </summary>
    private const int LockVisibilityTimeoutMs = 5000;
    private const int LockVisibilityPollIntervalMs = 100;

    [SkippableFact]
    public void Find_LockedFile_ReturnsCurrentProcess()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        if (!OperatingSystem.IsWindows()) { return; } // redundant, satisfies CA1416 analyzer

        string filePath = Path.GetTempFileName();
        try
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                int currentPid = Process.GetCurrentProcess().Id;
                List<LockInfo> results = WaitForLockToBeVisible(filePath, currentPid);
                Assert.Contains(results, r => r.ProcessId == currentPid);
            }
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [SkippableFact]
    public void Find_UnlockedFile_ReturnsEmpty()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        if (!OperatingSystem.IsWindows()) { return; } // redundant, satisfies CA1416 analyzer

        string filePath = Path.GetTempFileName();
        try
        {
            var results = FileLockFinder.Find(filePath);
            Assert.Empty(results);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [SkippableFact]
    public void Find_NonExistentFile_ReturnsEmpty()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        if (!OperatingSystem.IsWindows()) { return; } // redundant, satisfies CA1416 analyzer

        string filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".tmp");
        var results = FileLockFinder.Find(filePath);
        Assert.Empty(results);
    }

    [SkippableFact]
    public void Find_LockedFile_ResourceContainsFilePath()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        if (!OperatingSystem.IsWindows()) { return; } // redundant, satisfies CA1416 analyzer

        string filePath = Path.GetTempFileName();
        try
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                int currentPid = Process.GetCurrentProcess().Id;
                List<LockInfo> results = WaitForLockToBeVisible(filePath, currentPid);
                var ours = results.FirstOrDefault(r => r.ProcessId == currentPid);
                Assert.NotNull(ours);
                Assert.Equal(filePath, ours!.Resource);
            }
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Polls <see cref="FileLockFinder.Find"/> until either the current process appears in
    /// the result set or <see cref="LockVisibilityTimeoutMs"/> elapses. Returns the most
    /// recent result list either way, so a still-empty result on timeout produces a
    /// natural <c>Assert.NotNull</c>/<c>Assert.Contains</c> failure with the assertion's
    /// own diagnostic — no need for a custom timeout exception.
    /// </summary>
    private static List<LockInfo> WaitForLockToBeVisible(string filePath, int currentPid)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(LockVisibilityTimeoutMs);
        List<LockInfo> results;
        do
        {
            results = FileLockFinder.Find(filePath);
            if (results.Any(r => r.ProcessId == currentPid))
            {
                return results;
            }
            Thread.Sleep(LockVisibilityPollIntervalMs);
        } while (DateTimeOffset.UtcNow < deadline);

        // Final attempt for diagnostic — caller's assertion fires on this if nothing was
        // found, which produces a clearer failure than swallowing the empty list silently.
        return FileLockFinder.Find(filePath);
    }
}
