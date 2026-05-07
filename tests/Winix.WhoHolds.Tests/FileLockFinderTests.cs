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
                IReadOnlyList<LockInfo> results = WaitForLockToBeVisible(filePath, currentPid);
                Assert.Contains(results, r => r.ProcessId == currentPid);
            }
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [SkippableFact]
    public void Find_UnlockedFile_ReturnsSuccessEmpty()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        if (!OperatingSystem.IsWindows()) { return; } // redundant, satisfies CA1416 analyzer

        string filePath = Path.GetTempFileName();
        try
        {
            FindResult result = FileLockFinder.Find(filePath);
            Assert.False(result.QueryFailed);
            Assert.Null(result.Reason);
            Assert.Empty(result.Results);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [SkippableFact]
    public void Find_NonExistentFile_ReturnsSuccessEmpty()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        if (!OperatingSystem.IsWindows()) { return; } // redundant, satisfies CA1416 analyzer

        string filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".tmp");
        FindResult result = FileLockFinder.Find(filePath);
        Assert.False(result.QueryFailed);
        Assert.Empty(result.Results);
    }

    /// <summary>
    /// On non-Windows platforms FileLockFinder has no backend; the contract is success-empty
    /// (not QueryFailed). The "no backend installed" case is a static, user-known condition
    /// distinct from a runtime backend error.
    /// </summary>
    [Fact]
    public void Find_OnNonWindows_ReturnsSuccessEmpty()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // The branch only applies off-Windows.
        }

        FindResult result = FileLockFinder.Find("/tmp/nonexistent");
        Assert.False(result.QueryFailed);
        Assert.Empty(result.Results);
    }

    // ── Tier-2 baseline 2026-05-06 finding F1 — process name truncation regression ──
    // Pre-fix: RM_UNIQUE_PROCESS used `long ProcessStartTime` which forced 8-byte
    // alignment on x64, inserting 4 bytes of padding after dwProcessId. This shifted
    // strAppName by 2 wide chars when the marshaller read it back. Post-fix: the struct
    // uses two uint fields matching FILETIME's actual layout, so no padding inserts.
    // This test fails (with a 2-char-shifted name) on the pre-fix struct layout.
    [SkippableFact]
    public void Find_LockedFile_ProcessNameIsNotTruncated()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        if (!OperatingSystem.IsWindows()) { return; } // redundant, satisfies CA1416 analyzer

        string filePath = Path.GetTempFileName();
        try
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                int currentPid = Process.GetCurrentProcess().Id;
                IReadOnlyList<LockInfo> results = WaitForLockToBeVisible(filePath, currentPid);
                LockInfo? ours = results.FirstOrDefault(r => r.ProcessId == currentPid);
                Assert.NotNull(ours);

                // The pre-fix bug was: every name lost its first 2 chars due to struct
                // padding misalignment. We don't pin the exact name (it depends on the
                // test runner host — testhost / dotnet / similar), but we assert that
                // (a) the name is non-empty, and (b) it does NOT have a 2-char-shifted
                // suffix relationship with the actual current-process name (which is
                // what the bug produced).
                Assert.False(string.IsNullOrEmpty(ours!.ProcessName), "Process name must not be empty");

                string expectedName = Process.GetCurrentProcess().ProcessName;
                if (expectedName.Length >= 3)
                {
                    // Reject the specific corruption shape: "Truncated" should never equal
                    // expectedName.Substring(2) (the post-shift form). If it does, the
                    // padding bug has regressed.
                    string corruptedShape = expectedName.Substring(2);
                    Assert.False(
                        ours.ProcessName.Equals(corruptedShape, StringComparison.OrdinalIgnoreCase),
                        $"ProcessName '{ours.ProcessName}' matches the 2-char-shifted form of " +
                        $"the current process name '{expectedName}' — indicates RM_UNIQUE_PROCESS " +
                        $"struct-layout regression (F1).");
                }
            }
        }
        finally
        {
            File.Delete(filePath);
        }
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
                IReadOnlyList<LockInfo> results = WaitForLockToBeVisible(filePath, currentPid);
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
    /// own diagnostic — no need for a custom timeout exception. Asserts that no poll
    /// surfaces a backend failure: the post-FindResult contract is that QueryFailed must
    /// fail the test rather than be hidden inside an empty-results retry.
    /// </summary>
    private static IReadOnlyList<LockInfo> WaitForLockToBeVisible(string filePath, int currentPid)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(LockVisibilityTimeoutMs);
        FindResult result;
        do
        {
            result = FileLockFinder.Find(filePath);
            Assert.False(result.QueryFailed, $"FileLockFinder.Find returned QueryFailed: {result.Reason}");
            if (result.Results.Any(r => r.ProcessId == currentPid))
            {
                return result.Results;
            }
            Thread.Sleep(LockVisibilityPollIntervalMs);
        } while (DateTimeOffset.UtcNow < deadline);

        // Final attempt for diagnostic — caller's assertion fires on this if nothing was
        // found, which produces a clearer failure than swallowing the empty list silently.
        FindResult finalAttempt = FileLockFinder.Find(filePath);
        Assert.False(finalAttempt.QueryFailed, $"FileLockFinder.Find returned QueryFailed: {finalAttempt.Reason}");
        return finalAttempt.Results;
    }
}
