#nullable enable
using System;
using System.Globalization;
using System.IO;

namespace Winix.Trash;

/// <summary>Pure, platform-agnostic "never trash the trash itself" guard predicates (F1). Extracted
/// from the three backends so this safety logic is unit-testable on any OS — the backends are
/// <c>[SupportedOSPlatform]</c>-gated, which would otherwise make a cross-OS unit test trip the CA1416
/// analyzer. A false negative here means handing a drive root or the recycle bin to the OS delete
/// API — i.e. data loss — so these checks are deliberately conservative (a false refusal is just a
/// clear error). Dependencies that would need a syscall (home dir, uid) are passed in, mirroring the
/// injected-delegate pattern in <see cref="MountResolver"/>.</summary>
internal static class TrashGuards
{
    /// <summary>Windows: refuse a drive or UNC-share root (e.g. <c>C:\</c>) or any path containing a
    /// <c>$Recycle.Bin</c> segment. Case-insensitive, matching Windows path semantics.</summary>
    public static bool IsWindowsRefusedRoot(string fullPath)
    {
        // A drive root canonicalises to "C:\" which trims to "C:" (2 chars). Anything that short is a root.
        string trimmed = fullPath.TrimEnd('\\', '/');
        if (trimmed.Length <= 2)
        {
            return true;
        }

        // Also catch a framework-reported path root (covers UNC share roots etc.).
        string? root = Path.GetPathRoot(fullPath);
        if (root is not null
            && string.Equals(trimmed, root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string segment in fullPath.Split('\\', '/'))
        {
            if (string.Equals(segment, "$Recycle.Bin", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>macOS: refuse <paramref name="home"/><c>/.Trash</c> (or anything beneath it) and any
    /// path containing a <c>.Trash</c>/<c>.Trashes</c> segment (covers per-volume
    /// <c>/Volumes/*/.Trashes</c>). Case-sensitive. Pass the resolved home dir in.</summary>
    public static bool IsMacTrashRoot(string fullPath, string home)
    {
        // Join with an explicit '/', NOT Path.Combine — macOS paths are always '/'-separated, and
        // Path.Combine would emit '\' when this pure logic is exercised on a Windows test host.
        if (home.Length > 0 && PathEqualsOrUnder(fullPath, home.TrimEnd('/') + "/.Trash"))
        {
            return true;
        }

        return HasSegment(fullPath, ".Trash") || HasSegment(fullPath, ".Trashes");
    }

    /// <summary>Linux: refuse the home trash dir (or anything beneath it) and any path containing this
    /// user's <c>.Trash-&lt;uid&gt;</c> segment or the admin <c>.Trash</c> form. Case-sensitive.</summary>
    public static bool IsLinuxTrashRoot(string canonical, string homeTrashDir, int uid)
    {
        if (PathEqualsOrUnder(canonical, homeTrashDir))
        {
            return true;
        }

        string topDirName = ".Trash-" + uid.ToString(CultureInfo.InvariantCulture);
        return HasSegment(canonical, topDirName) || HasSegment(canonical, ".Trash");
    }

    /// <summary>True when <paramref name="path"/> equals <paramref name="root"/> or sits beneath it.
    /// Ordinal comparison; both are trimmed of a trailing slash first.</summary>
    public static bool PathEqualsOrUnder(string path, string root)
    {
        string p = path.TrimEnd('/');
        string r = root.TrimEnd('/');
        if (string.Equals(p, r, StringComparison.Ordinal))
        {
            return true;
        }

        return p.StartsWith(r + "/", StringComparison.Ordinal);
    }

    // Ordinal segment match on '/'-split components (the *nix path separator).
    private static bool HasSegment(string path, string segment)
    {
        foreach (string s in path.Split('/'))
        {
            if (string.Equals(s, segment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
