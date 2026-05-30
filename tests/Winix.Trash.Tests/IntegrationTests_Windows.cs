#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Winix.Trash;
using Xunit;

namespace Winix.Trash.Tests;

/// <summary>Real Recycle Bin integration tests for the Windows backend. These exercise the
/// SHFileOperationW marshalling end-to-end (the unit tests can't — they'd touch the real bin), and
/// critically assert the file is RECYCLED (recoverable), not permanently deleted. Each test
/// self-cleans by deleting only the specific <c>$I</c>/<c>$R</c> pair it created — never
/// <c>SHEmptyRecycleBin</c>, which would wipe the developer's real bin.</summary>
[Trait("Platform", "Windows")]
public class IntegrationTests_Windows
{
    [SkippableFact]
    public void Trash_recyclesFile_recoverableWithMatchingMetadata()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        if (!OperatingSystem.IsWindows()) { return; } // redundant, satisfies CA1416 analyzer
        Trash_recycles_impl();
    }

    [SkippableFact]
    public void List_includesRecycledItem_withOriginalPath()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        if (!OperatingSystem.IsWindows()) { return; }
        List_includes_impl();
    }

    // ── Implementations ─────────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static void Trash_recycles_impl()
    {
        string victim = Path.Combine(Path.GetTempPath(), "winix-trash-it-" + Guid.NewGuid().ToString("N") + ".txt");
        const string content = "recoverable-payload";
        File.WriteAllText(victim, content);
        string fullPath = Path.GetFullPath(victim);

        string? iFile = null;
        try
        {
            var backend = new WindowsRecycleBinBackend();
            TrashResult result = backend.Trash(new[] { victim });

            Assert.False(result.AnyFailed, ErrorsOf(result));
            Assert.False(File.Exists(victim), "origin file must be gone");

            // Find the $I metadata whose decoded original path is our file — proves it was recycled
            // with correct metadata, not destroyed.
            iFile = FindIFileFor(fullPath);
            Assert.NotNull(iFile);

            string rFile = DeriveRFile(iFile!);
            Assert.True(File.Exists(rFile), "$R payload must exist in the Recycle Bin (recoverable)");
            // The recycled payload must still hold the original bytes — i.e. it's genuinely
            // recoverable, which a permanent delete would not be.
            Assert.Equal(content, File.ReadAllText(rFile));
        }
        finally
        {
            CleanPair(iFile);
            if (File.Exists(victim)) { File.Delete(victim); }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void List_includes_impl()
    {
        string victim = Path.Combine(Path.GetTempPath(), "winix-trash-it-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(victim, "x");
        string fullPath = Path.GetFullPath(victim);

        string? iFile = null;
        try
        {
            var backend = new WindowsRecycleBinBackend();
            Assert.False(backend.Trash(new[] { victim }).AnyFailed);

            var items = backend.List();
            TrashedItem? entry = items.FirstOrDefault(i =>
                string.Equals(i.OriginalPath, fullPath, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(entry);
            Assert.Equal(fullPath, entry!.OriginalPath, ignoreCase: true);

            iFile = FindIFileFor(fullPath); // for cleanup
        }
        finally
        {
            CleanPair(iFile);
            if (File.Exists(victim)) { File.Delete(victim); }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Scans every fixed drive's <c>$Recycle.Bin\&lt;SID&gt;</c> for the <c>$I</c> file whose
    /// decoded original path matches <paramref name="originalFullPath"/>, returning its path or null.</summary>
    [SupportedOSPlatform("windows")]
    private static string? FindIFileFor(string originalFullPath)
    {
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady) { continue; }

            string recycleRoot = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
            if (!Directory.Exists(recycleRoot)) { continue; }

            string[] sidDirs;
            try { sidDirs = Directory.GetDirectories(recycleRoot); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (string sidDir in sidDirs)
            {
                string[] iFiles;
                try { iFiles = Directory.GetFiles(sidDir, "$I*"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

                foreach (string iFile in iFiles)
                {
                    RecycleEntry? entry;
                    try { entry = RecycleMetadata.TryParseIFile(File.ReadAllBytes(iFile)); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

                    if (entry is not null
                        && string.Equals(entry.OriginalPath, originalFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return iFile;
                    }
                }
            }
        }

        return null;
    }

    private static string DeriveRFile(string iFile)
    {
        string dir = Path.GetDirectoryName(iFile)!;
        string leaf = Path.GetFileName(iFile);
        string rLeaf = leaf.StartsWith("$I", StringComparison.Ordinal) ? "$R" + leaf.Substring(2) : leaf;
        return Path.Combine(dir, rLeaf);
    }

    /// <summary>Removes only the specific <c>$I</c>/<c>$R</c> pair this test created — never the whole
    /// bin. The <c>$R</c> may be a directory (recycled folder); handle both.</summary>
    private static void CleanPair(string? iFile)
    {
        if (iFile is null) { return; }
        try
        {
            string rFile = DeriveRFile(iFile);
            if (Directory.Exists(rFile)) { Directory.Delete(rFile, recursive: true); }
            else if (File.Exists(rFile)) { File.Delete(rFile); }
            if (File.Exists(iFile)) { File.Delete(iFile); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup; a leftover pair is recoverable manually and harmless.
        }
    }

    private static string ErrorsOf(TrashResult r)
        => string.Join("; ", r.Outcomes.Where(o => o.Error is not null).Select(o => $"{o.Path}: {o.Error}"));
}
