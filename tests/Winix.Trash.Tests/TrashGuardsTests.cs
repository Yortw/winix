#nullable enable
using Winix.Trash;
using Xunit;

namespace Winix.Trash.Tests;

/// <summary>Unit tests for the F1 "never trash the trash" guards. These are the data-safety guards
/// whose false-negative means handing a drive root / recycle bin to the OS delete API. Extracting them
/// into the non-platform-gated <see cref="TrashGuards"/> lets us test them on any OS. Assertions here
/// use only platform-independent inputs (drive-root-by-length, segment matches, home-trash
/// containment) — the Windows UNC-root path relies on <c>Path.GetPathRoot</c>'s Windows-runtime
/// behaviour and is exercised by the Windows integration tier, not here.</summary>
public class TrashGuardsTests
{
    // ── Windows ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\")]                                  // drive root
    [InlineData(@"C:")]                                   // drive root, no trailing slash
    [InlineData(@"D:\")]
    [InlineData(@"C:\$Recycle.Bin")]                      // the recycle bin root
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21-1\$RABCDEF")]  // an entry inside the recycle bin
    [InlineData(@"E:\$RECYCLE.BIN\x")]                    // case-insensitive segment match
    public void Windows_refuses_roots_and_recycle_bin(string path)
    {
        Assert.True(TrashGuards.IsWindowsRefusedRoot(path));
    }

    [Theory]
    [InlineData(@"C:\Users\me\file.txt")]
    [InlineData(@"C:\Users\me\$Recycle.Bin.txt")]         // a file merely NAMED like the bin — not refused
    [InlineData(@"C:\projects\winix\readme.md")]
    public void Windows_allows_normal_paths(string path)
    {
        Assert.False(TrashGuards.IsWindowsRefusedRoot(path));
    }

    // ── macOS ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/Users/me/.Trash")]                      // the home Trash itself
    [InlineData("/Users/me/.Trash/old.txt")]              // under the home Trash
    [InlineData("/Volumes/Data/.Trashes/501")]            // a per-volume Trashes (segment match)
    [InlineData("/some/dir/.Trash/x")]                    // any .Trash segment
    public void Mac_refuses_trash_roots(string path)
    {
        Assert.True(TrashGuards.IsMacTrashRoot(path, "/Users/me"));
    }

    [Theory]
    [InlineData("/Users/me/Documents/report.pdf")]
    [InlineData("/Users/me/.Trashcan/x")]                 // not a .Trash/.Trashes segment
    public void Mac_allows_normal_paths(string path)
    {
        Assert.False(TrashGuards.IsMacTrashRoot(path, "/Users/me"));
    }

    // ── Linux ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/home/me/.local/share/Trash")]           // the home trash dir itself
    [InlineData("/home/me/.local/share/Trash/files/x")]   // under it
    [InlineData("/mnt/data/.Trash-1000")]                 // a top-dir .Trash-<uid> (uid 1000)
    [InlineData("/mnt/data/.Trash-1000/files/y")]
    [InlineData("/srv/.Trash/admin")]                     // the admin .Trash form
    public void Linux_refuses_trash_roots(string path)
    {
        Assert.True(TrashGuards.IsLinuxTrashRoot(path, "/home/me/.local/share/Trash", uid: 1000));
    }

    [Theory]
    [InlineData("/home/me/projects/file.c")]
    [InlineData("/mnt/data/.Trash-2000/x")]               // a DIFFERENT user's top-dir trash — not ours
    public void Linux_allows_normal_and_other_uid_paths(string path)
    {
        Assert.False(TrashGuards.IsLinuxTrashRoot(path, "/home/me/.local/share/Trash", uid: 1000));
    }

    // ── Shared helper ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/a/b", "/a/b", true)]
    [InlineData("/a/b/", "/a/b", true)]                   // trailing slash on the path
    [InlineData("/a/b/c", "/a/b", true)]                  // strictly under
    [InlineData("/a/bc", "/a/b", false)]                  // sibling-prefix, NOT under (the bug PathEqualsOrUnder guards)
    [InlineData("/a", "/a/b", false)]
    public void PathEqualsOrUnder_matches_only_real_containment(string path, string root, bool expected)
    {
        Assert.Equal(expected, TrashGuards.PathEqualsOrUnder(path, root));
    }
}
