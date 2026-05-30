#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Winix.Trash;
using Xunit;

namespace Winix.Trash.Tests;

/// <summary>Real-filesystem integration tests for the Linux FreeDesktop backend. These are the
/// only place the statx/move/rollback interop is exercised against a real kernel — the unit tests
/// cover the pure <see cref="MountResolver"/> policy. Each test sandboxes the home trash to a temp
/// dir (via the seam constructor) and self-cleans.</summary>
[Trait("Platform", "Linux")]
public class IntegrationTests_Linux
{
    [SkippableFact]
    public void Trash_homeVolume_movesFileAndWritesDecodableTrashInfo()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux-only integration test");
        if (!OperatingSystem.IsLinux()) { return; } // redundant, satisfies CA1416 analyzer
        Trash_homeVolume_impl();
    }

    [SkippableFact]
    public void List_includesTrashedItem_withOriginalPath()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux-only integration test");
        if (!OperatingSystem.IsLinux()) { return; }
        List_includes_impl();
    }

    [SkippableFact]
    public void Trash_otherVolume_usesTopDirTrash()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux-only integration test");
        if (!OperatingSystem.IsLinux()) { return; }
        Trash_multiVolume_impl();
    }

    [SkippableFact]
    public void Trash_symlinkToOtherVolume_goesToTheLinkNodesVolume()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux-only integration test");
        if (!OperatingSystem.IsLinux()) { return; }
        Trash_crossVolumeSymlink_impl();
    }

    [SkippableFact]
    public void List_skipsCorruptAndOrphanedEntries_withoutThrowing()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux-only integration test");
        if (!OperatingSystem.IsLinux()) { return; }
        List_skipsCorrupt_impl();
    }

    [SkippableFact]
    public void Trash_sameBaseName_reservesDistinctSuffixedEntries()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux-only integration test");
        if (!OperatingSystem.IsLinux()) { return; }
        Trash_collision_impl();
    }

    [SkippableFact]
    public void Trash_trashRootItself_isRefused_andStoreIntact()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux-only integration test");
        if (!OperatingSystem.IsLinux()) { return; }
        Trash_f1Guard_impl();
    }

    // ── Implementations (gated so CA1416 is satisfied) ──────────────────────────

    [SupportedOSPlatform("linux")]
    private static void Trash_homeVolume_impl()
    {
        string work = MakeWorkDir();
        try
        {
            string homeTrash = Path.Combine(work, "Trash");
            string origin = Path.Combine(work, "origin");
            Directory.CreateDirectory(origin);
            string victim = Path.Combine(origin, "doc.txt");
            File.WriteAllText(victim, "payload");

            var backend = new LinuxFreeDesktopBackend(homeTrash);
            TrashResult result = backend.Trash(new[] { victim });

            Assert.False(result.AnyFailed, ErrorsOf(result));
            Assert.False(File.Exists(victim), "origin file must be gone");

            string moved = Path.Combine(homeTrash, "files", "doc.txt");
            Assert.True(File.Exists(moved), "file must be present in the trash files/ dir");
            Assert.Equal("payload", File.ReadAllText(moved));

            string info = Path.Combine(homeTrash, "info", "doc.txt.trashinfo");
            Assert.True(File.Exists(info), ".trashinfo metadata must exist");
            TrashInfoRecord? record = TrashInfo.Parse(File.ReadAllText(info));
            Assert.NotNull(record);
            // The decoded original path must be the absolute pre-trash path.
            Assert.Equal(Path.GetFullPath(victim), record!.OriginalPath);
        }
        finally
        {
            TryRmTree(work);
        }
    }

    [SupportedOSPlatform("linux")]
    private static void List_includes_impl()
    {
        string work = MakeWorkDir();
        try
        {
            string homeTrash = Path.Combine(work, "Trash");
            string origin = Path.Combine(work, "origin");
            Directory.CreateDirectory(origin);
            string victim = Path.Combine(origin, "note.md");
            File.WriteAllText(victim, "x");

            var backend = new LinuxFreeDesktopBackend(homeTrash);
            Assert.False(backend.Trash(new[] { victim }).AnyFailed);

            var items = backend.List();
            TrashedItem? entry = items.FirstOrDefault(i => i.Name == "note.md");
            Assert.NotNull(entry);
            Assert.Equal(Path.GetFullPath(victim), entry!.OriginalPath);
            Assert.Equal("home", entry.TrashLocation);
        }
        finally
        {
            TryRmTree(work);
        }
    }

    [SupportedOSPlatform("linux")]
    private static void Trash_multiVolume_impl()
    {
        string work = MakeWorkDir();
        string mnt = Path.Combine(work, "mnt");
        Directory.CreateDirectory(mnt);

        // A separate filesystem is required to get a distinct st_dev — a bind mount would keep the
        // source device id and would NOT exercise the cross-volume branch. tmpfs needs root/CAP_SYS_ADMIN.
        if (Run("mount", "-t", "tmpfs", "tmpfs", mnt) != 0)
        {
            TryRmTree(work);
            Skip.If(true, "cannot mount tmpfs (need root/CAP_SYS_ADMIN) — skipping multi-volume test");
            return;
        }

        try
        {
            string victim = Path.Combine(mnt, "big.bin");
            File.WriteAllText(victim, "data");

            // Home trash lives on the work dir's volume — a DIFFERENT device from the tmpfs mount,
            // so the file must route to <mnt>/.Trash-<uid> rather than the home trash.
            string homeTrash = Path.Combine(work, "Trash");
            var backend = new LinuxFreeDesktopBackend(homeTrash);
            TrashResult result = backend.Trash(new[] { victim });

            Assert.False(result.AnyFailed, ErrorsOf(result));
            Assert.False(File.Exists(victim), "origin file must be gone from the tmpfs");

            // It must NOT have landed in the home trash (that's the offset-bug failure mode).
            Assert.False(File.Exists(Path.Combine(homeTrash, "files", "big.bin")),
                "file must NOT route to the home trash when on another volume");

            string[] topDirTrashes = Directory.GetDirectories(mnt, ".Trash-*");
            Assert.Single(topDirTrashes);
            Assert.True(File.Exists(Path.Combine(topDirTrashes[0], "files", "big.bin")),
                "file must land in <mount>/.Trash-<uid>/files");
        }
        finally
        {
            Run("umount", mnt);
            TryRmTree(work);
        }
    }

    /// <summary>Reproducer (review finding #8): a symlink whose NODE lives on the home volume but whose
    /// TARGET is on another volume must trash to the home volume — the move operates on the link node,
    /// not the target. If device identity is resolved by following the symlink (statx flags 0), the
    /// backend keys on the target's volume and the same-device move fails spuriously.</summary>
    [SupportedOSPlatform("linux")]
    private static void Trash_crossVolumeSymlink_impl()
    {
        string work = MakeWorkDir();
        string mnt = Path.Combine(work, "mnt");
        Directory.CreateDirectory(mnt);

        if (Run("mount", "-t", "tmpfs", "tmpfs", mnt) != 0)
        {
            TryRmTree(work);
            Skip.If(true, "cannot mount tmpfs (need root/CAP_SYS_ADMIN) — skipping cross-volume symlink test");
            return;
        }

        try
        {
            // Target on the tmpfs (volume B); symlink NODE on the work dir (volume A).
            string target = Path.Combine(mnt, "target.txt");
            File.WriteAllText(target, "data");
            string link = Path.Combine(work, "link.txt");
            File.CreateSymbolicLink(link, target);

            // Home trash on volume A — same device as the link node, so trashing the link should be a
            // same-device rename into the home trash, NOT a cross-volume failure based on the target.
            string homeTrash = Path.Combine(work, "Trash");
            var backend = new LinuxFreeDesktopBackend(homeTrash);
            TrashResult result = backend.Trash(new[] { link });

            Assert.False(result.AnyFailed, ErrorsOf(result));
            // Origin link node must be gone (File.Exists on the now-absent link returns false).
            Assert.False(File.Exists(link), "the symlink must be gone from origin");
            // The moved link lands in the home trash; File.Exists follows it to the still-present target.
            Assert.True(File.Exists(Path.Combine(homeTrash, "files", "link.txt")),
                "the symlink must land in the home trash (its own volume)");
        }
        finally
        {
            Run("umount", mnt);
            TryRmTree(work);
        }
    }

    [SupportedOSPlatform("linux")]
    private static void List_skipsCorrupt_impl()
    {
        string work = MakeWorkDir();
        try
        {
            string homeTrash = Path.Combine(work, "Trash");
            string origin = Path.Combine(work, "origin");
            Directory.CreateDirectory(origin);
            string victim = Path.Combine(origin, "good.txt");
            File.WriteAllText(victim, "ok");

            var backend = new LinuxFreeDesktopBackend(homeTrash);
            Assert.False(backend.Trash(new[] { victim }).AnyFailed);

            string infoDir = Path.Combine(homeTrash, "info");
            string filesDir = Path.Combine(homeTrash, "files");

            // (a) corrupt metadata: a .trashinfo with no Path= line → TrashInfo.Parse returns null.
            File.WriteAllText(Path.Combine(infoDir, "corrupt.trashinfo"),
                "[Trash Info]\nDeletionDate=2026-05-30T12:00:00\n");
            // (b) orphaned metadata: valid .trashinfo with no sibling files/ payload.
            File.WriteAllText(Path.Combine(infoDir, "orphan.trashinfo"),
                "[Trash Info]\nPath=/tmp/orphan\nDeletionDate=2026-05-30T12:00:00\n");

            var items = backend.List(); // must not throw
            Assert.Single(items);
            Assert.Equal("good.txt", items[0].Name);
            Assert.True(File.Exists(Path.Combine(filesDir, "good.txt")));
        }
        finally
        {
            TryRmTree(work);
        }
    }

    [SupportedOSPlatform("linux")]
    private static void Trash_collision_impl()
    {
        string work = MakeWorkDir();
        try
        {
            string homeTrash = Path.Combine(work, "Trash");
            string a = Path.Combine(work, "a");
            string b = Path.Combine(work, "b");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(b);
            string v1 = Path.Combine(a, "dup.txt");
            string v2 = Path.Combine(b, "dup.txt");
            File.WriteAllText(v1, "first");
            File.WriteAllText(v2, "second");

            var backend = new LinuxFreeDesktopBackend(homeTrash);
            Assert.False(backend.Trash(new[] { v1 }).AnyFailed);
            Assert.False(backend.Trash(new[] { v2 }).AnyFailed);

            string filesDir = Path.Combine(homeTrash, "files");
            string infoDir = Path.Combine(homeTrash, "info");

            // Two distinct entries — numeric-suffix reservation must have prevented an overwrite.
            Assert.True(File.Exists(Path.Combine(filesDir, "dup.txt")));
            Assert.True(File.Exists(Path.Combine(filesDir, "dup.txt.2")));
            Assert.True(File.Exists(Path.Combine(infoDir, "dup.txt.trashinfo")));
            Assert.True(File.Exists(Path.Combine(infoDir, "dup.txt.2.trashinfo")));

            // Contents preserved distinctly (no clobber).
            string c1 = File.ReadAllText(Path.Combine(filesDir, "dup.txt"));
            string c2 = File.ReadAllText(Path.Combine(filesDir, "dup.txt.2"));
            Assert.Equal(new[] { "first", "second" }, new[] { c1, c2 }.OrderBy(s => s).ToArray());

            // The two .trashinfo records point at the two distinct original paths.
            TrashInfoRecord? r1 = TrashInfo.Parse(File.ReadAllText(Path.Combine(infoDir, "dup.txt.trashinfo")));
            TrashInfoRecord? r2 = TrashInfo.Parse(File.ReadAllText(Path.Combine(infoDir, "dup.txt.2.trashinfo")));
            Assert.NotNull(r1);
            Assert.NotNull(r2);
            Assert.Equal(
                new[] { Path.GetFullPath(v1), Path.GetFullPath(v2) }.OrderBy(s => s).ToArray(),
                new[] { r1!.OriginalPath, r2!.OriginalPath }.OrderBy(s => s).ToArray());
        }
        finally
        {
            TryRmTree(work);
        }
    }

    [SupportedOSPlatform("linux")]
    private static void Trash_f1Guard_impl()
    {
        string work = MakeWorkDir();
        try
        {
            string homeTrash = Path.Combine(work, "Trash");
            string origin = Path.Combine(work, "origin");
            Directory.CreateDirectory(origin);
            string victim = Path.Combine(origin, "keep.txt");
            File.WriteAllText(victim, "y");

            var backend = new LinuxFreeDesktopBackend(homeTrash);
            // Trash a real file first so the trash root exists (otherwise the guard test would only
            // hit the "no such file" path).
            Assert.False(backend.Trash(new[] { victim }).AnyFailed);

            // Now attempt to trash the trash root itself — must be refused.
            TrashResult result = backend.Trash(new[] { homeTrash });
            Assert.True(result.AnyFailed, "trashing the trash root must fail");
            Assert.Contains("refusing", result.Outcomes[0].Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            // The store must be intact — the previously-trashed item is still listed.
            Assert.Contains(backend.List(), i => i.Name == "keep.txt");
        }
        finally
        {
            TryRmTree(work);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static string MakeWorkDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "winix-trash-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string ErrorsOf(TrashResult r)
        => string.Join("; ", r.Outcomes.Where(o => o.Error is not null).Select(o => $"{o.Path}: {o.Error}"));

    private static void TryRmTree(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    /// <summary>Runs a process to completion, returning its exit code (or a non-zero sentinel on
    /// launch failure). Args go through ArgumentList — never string concatenation.</summary>
    private static int Run(string fileName, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string a in args) { psi.ArgumentList.Add(a); }

            using Process? p = Process.Start(psi);
            if (p is null) { return -1; }
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return -1;
        }
    }
}
