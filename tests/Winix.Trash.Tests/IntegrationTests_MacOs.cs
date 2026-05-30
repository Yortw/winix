#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Winix.Trash;
using Xunit;

namespace Winix.Trash.Tests;

/// <summary>Real-Trash integration tests for the macOS backend. These are the ONLY runtime proof the
/// Objective-C interop (objc_msgSend → NSFileManager.trashItem) works — there is no fake for it — so
/// they are the verification gate for that risk surface and run on macOS CI only. Each test
/// self-cleans. Files are created under <c>$HOME</c> so they trash to <c>~/.Trash</c>.</summary>
[Trait("Platform", "macOS")]
public class IntegrationTests_MacOs
{
    [SkippableFact]
    public void Trash_movesFileToHomeTrash()
    {
        Skip.IfNot(OperatingSystem.IsMacOS(), "macOS-only integration test");
        if (!OperatingSystem.IsMacOS()) { return; } // redundant, satisfies CA1416 analyzer
        Trash_toHomeTrash_impl();
    }

    [SkippableFact]
    public void List_includesTrashedItem_withNullOriginalPath()
    {
        Skip.IfNot(OperatingSystem.IsMacOS(), "macOS-only integration test");
        if (!OperatingSystem.IsMacOS()) { return; }
        List_includes_impl();
    }

    [SkippableFact]
    public void Trash_nonexistentPath_reportsErrorAndFails()
    {
        Skip.IfNot(OperatingSystem.IsMacOS(), "macOS-only integration test");
        if (!OperatingSystem.IsMacOS()) { return; }
        Trash_nonexistent_impl();
    }

    [SkippableFact]
    public void Trash_unremovableItem_unwrapsNsErrorIntoMessage()
    {
        Skip.IfNot(OperatingSystem.IsMacOS(), "macOS-only integration test");
        if (!OperatingSystem.IsMacOS()) { return; }
        Trash_nsErrorArm_impl();
    }

    // ── Implementations ─────────────────────────────────────────────────────────

    [SupportedOSPlatform("macos")]
    private static void Trash_toHomeTrash_impl()
    {
        string home = HomeDir();
        string name = "winix-trash-it-" + Guid.NewGuid().ToString("N") + ".txt";
        string victim = Path.Combine(home, name);
        File.WriteAllText(victim, "payload");
        string trashedCopy = Path.Combine(home, ".Trash", name);

        try
        {
            var backend = new MacOsTrashBackend();
            TrashResult result = backend.Trash(new[] { victim });

            Assert.False(result.AnyFailed, ErrorsOf(result));
            Assert.False(File.Exists(victim), "origin file must be gone");
            // The GUID-unique name means no collision suffix; it lands at ~/.Trash/<name>.
            Assert.True(File.Exists(trashedCopy), "file must appear in ~/.Trash");
        }
        finally
        {
            TryDelete(trashedCopy);
            TryDelete(victim);
        }
    }

    [SupportedOSPlatform("macos")]
    private static void List_includes_impl()
    {
        string home = HomeDir();
        string name = "winix-trash-it-" + Guid.NewGuid().ToString("N") + ".txt";
        string victim = Path.Combine(home, name);
        File.WriteAllText(victim, "x");
        string trashedCopy = Path.Combine(home, ".Trash", name);

        try
        {
            var backend = new MacOsTrashBackend();
            Assert.False(backend.Trash(new[] { victim }).AnyFailed);

            TrashedItem? entry = backend.List().FirstOrDefault(i => i.Name == name);
            Assert.NotNull(entry);
            // Documented v1 limitation: macOS keeps the Put-Back source in a private store we don't read.
            Assert.Null(entry!.OriginalPath);
            Assert.Equal("home", entry.TrashLocation);
        }
        finally
        {
            TryDelete(trashedCopy);
            TryDelete(victim);
        }
    }

    [SupportedOSPlatform("macos")]
    private static void Trash_nonexistent_impl()
    {
        string missing = Path.Combine(HomeDir(), "winix-trash-missing-" + Guid.NewGuid().ToString("N"));
        var backend = new MacOsTrashBackend();
        TrashResult result = backend.Trash(new[] { missing });

        Assert.True(result.AnyFailed, "trashing a nonexistent path must fail");
        Assert.False(string.IsNullOrEmpty(result.Outcomes[0].Error), "a per-path error message must be produced");
    }

    /// <summary>Exercises the NSError-unwrap arm (the ADR's top-risk interop path): a real file whose
    /// parent dir is read-only stats fine (backend proceeds to Foundation) but trashItem cannot remove
    /// it, so Foundation returns an NSError that must be unwrapped into a non-empty message — not
    /// swallowed. Lenient on the exact (localized) text; asserts only that a message survived.</summary>
    [SupportedOSPlatform("macos")]
    private static void Trash_nsErrorArm_impl()
    {
        string roDir = Path.Combine(HomeDir(), "winix-trash-ro-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(roDir);
        string victim = Path.Combine(roDir, "locked.txt");
        File.WriteAllText(victim, "y");

        try
        {
            // r-x for owner: the file is still stat-able (execute lets us traverse) but it cannot be
            // removed from the dir (no write), so trashItem fails. CI runs non-root, so owner perm
            // bits are enforced.
            File.SetUnixFileMode(roDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            var backend = new MacOsTrashBackend();
            TrashResult result = backend.Trash(new[] { victim });

            Assert.True(result.AnyFailed, "trashItem on a read-only parent must fail");
            Assert.False(string.IsNullOrEmpty(result.Outcomes[0].Error),
                "the NSError must be unwrapped into a non-empty message, not swallowed");
        }
        finally
        {
            // Restore write so cleanup can remove the tree.
            try
            {
                File.SetUnixFileMode(roDir,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // best-effort
            }

            TryRmTree(roDir);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static string HomeDir()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        }

        return home;
    }

    private static string ErrorsOf(TrashResult r)
        => string.Join("; ", r.Outcomes.Where(o => o.Error is not null).Select(o => $"{o.Path}: {o.Error}"));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) { File.Delete(path); }
            else if (Directory.Exists(path)) { Directory.Delete(path, recursive: true); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }

    private static void TryRmTree(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }
}
