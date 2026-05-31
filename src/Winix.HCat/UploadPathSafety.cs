#nullable enable
using System;
using System.Globalization;
using System.IO;

namespace Winix.HCat;

/// <summary>Pure path-safety for the upload receiver. A non-null result is a fully-resolved target
/// guaranteed to sit under <c>uploadRoot</c>; null means the upload is rejected.</summary>
public static class UploadPathSafety
{
    /// <summary>Resolves a safe target path for an uploaded filename, or null if rejected. The name is
    /// reduced to its base name (directory components, <c>..</c>, and absolute paths cannot escape the
    /// root); on collision a numeric suffix is inserted before the extension. <paramref name="exists"/>
    /// is injected so the collision walk is testable without touching disk.</summary>
    public static string? ResolveTarget(string uploadRoot, string uploadedFileName, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(uploadedFileName)) { return null; }

        // Strip any directory components (defeats ../ and absolute paths) by keeping only the leaf.
        string leaf = Path.GetFileName(uploadedFileName.Replace('\\', '/'));
        if (string.IsNullOrEmpty(leaf) || leaf == "." || leaf == "..") { return null; }

        string rootFull = Path.GetFullPath(uploadRoot);
        string candidate = Path.GetFullPath(Path.Combine(rootFull, leaf));

        // Defence in depth: the candidate must equal the root or sit *below a path boundary* under it.
        // A bare prefix check (StartsWith(rootFull)) is UNSOUND — "/x/uploads" prefixes "/x/uploads-evil".
        // Use OrdinalIgnoreCase on Windows (case-insensitive FS), Ordinal elsewhere (trash precedent).
        StringComparison cmp = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        bool underRoot = candidate.Equals(rootFull, cmp)
            || candidate.StartsWith(rootFull + Path.DirectorySeparatorChar, cmp);
        if (!underRoot) { return null; }

        if (!exists(candidate)) { return candidate; }

        string stem = Path.GetFileNameWithoutExtension(leaf);
        string ext = Path.GetExtension(leaf);
        for (int n = 2; n < 10000; n++)
        {
            string suffixed = Path.GetFullPath(Path.Combine(rootFull,
                stem + "." + n.ToString(CultureInfo.InvariantCulture) + ext));
            if (!exists(suffixed)) { return suffixed; }
        }
        return null;
    }

    /// <summary>True when <paramref name="uploadDir"/> equals or sits under <paramref name="servedRoot"/>
    /// — i.e. uploads written there sit inside the served tree. Drives the "exclude from serving"
    /// decision: a within-tree-but-not-root dir is hidden; the served root itself cannot be hidden.</summary>
    public static bool IsWithinServedTree(string servedRoot, string uploadDir)
    {
        StringComparison cmp = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string root = Path.GetFullPath(servedRoot).TrimEnd(Path.DirectorySeparatorChar, '/');
        string dir = Path.GetFullPath(uploadDir).TrimEnd(Path.DirectorySeparatorChar, '/');
        if (string.Equals(root, dir, cmp)) { return true; }
        return dir.StartsWith(root + Path.DirectorySeparatorChar, cmp)
            || dir.StartsWith(root + "/", cmp);
    }

    /// <summary>True when <paramref name="uploadDir"/> resolves to the served root itself. This is the single
    /// "uploads are downloadable" condition: only the served root cannot be excluded from serving, so the
    /// <c>--upload-dir .</c> escape hatch is the one case where uploaded files are reachable as downloads.
    /// Both the serve-mode download-exclusion guard and the bind-banner warning derive from this one predicate
    /// so they can never disagree about whether an upload dir is downloadable.</summary>
    public static bool IsServedRoot(string servedRoot, string uploadDir)
    {
        StringComparison cmp = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string root = Path.GetFullPath(servedRoot).TrimEnd(Path.DirectorySeparatorChar, '/');
        string dir = Path.GetFullPath(uploadDir).TrimEnd(Path.DirectorySeparatorChar, '/');
        return string.Equals(root, dir, cmp);
    }
}
