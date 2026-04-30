#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Winix.WhoHolds;
using Xunit;

namespace Winix.WhoHolds.Tests;

public sealed class FileLockFinderTests
{
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
                var results = FileLockFinder.Find(filePath);
                int currentPid = Process.GetCurrentProcess().Id;
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
                var results = FileLockFinder.Find(filePath);
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
}
