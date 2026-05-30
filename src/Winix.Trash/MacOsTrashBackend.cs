#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;

namespace Winix.Trash;

/// <summary>macOS Trash backend. Routes deletions through <c>-[NSFileManager trashItemAtURL:…]</c>
/// (the same Foundation API Finder uses), so trashed items get the OS's correct Trash location,
/// volume routing, and name-collision handling for free. The Objective-C interop lives in the
/// <c>.Interop.cs</c> partial. All per-path failures are recorded as outcomes — the backend never
/// throws for an operational error.</summary>
[SupportedOSPlatform("macos")]
internal sealed partial class MacOsTrashBackend : ITrashBackend
{
    /// <inheritdoc/>
    public TrashResult Trash(IReadOnlyList<string> paths)
    {
        var outcomes = new List<PathOutcome>(paths.Count);
        foreach (string input in paths)
        {
            outcomes.Add(TrashOne(input));
        }

        return new TrashResult { Outcomes = outcomes };
    }

    private static PathOutcome TrashOne(string input)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(input);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new PathOutcome(input, "invalid path.");
        }

        // F1 trash-root guard: never trash the trash. Structural check — refuse any path segment
        // equal to ".Trash"/".Trashes", or anything at/under <home>/.Trash. trashItem would itself
        // error, but a clear up-front message beats whatever Foundation returns and matches the
        // Linux backend's guard wording.
        if (TrashGuards.IsMacTrashRoot(fullPath, HomeDir()))
        {
            return new PathOutcome(input, "refusing to trash the Trash itself.");
        }

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return new PathOutcome(input, "no such file or directory.");
        }

        (bool ok, string? error) = TrashViaFoundation(fullPath);
        if (!ok)
        {
            return new PathOutcome(input, error ?? "failed to move to Trash.");
        }

        return new PathOutcome(input, null);
    }

    /// <inheritdoc/>
    public IReadOnlyList<TrashedItem> List()
    {
        var items = new List<TrashedItem>();
        foreach ((string trashDir, string label) in TrashRoots())
        {
            string[] entries;
            try
            {
                if (!Directory.Exists(trashDir)) { continue; }
                entries = Directory.GetFileSystemEntries(trashDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string entry in entries)
            {
                TrashedItem? item = TryReadEntry(entry, label);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
        }

        return items;
    }

    /// <summary>Reads one top-level Trash entry into a <see cref="TrashedItem"/>, or null when it
    /// can't be stat'd. <see cref="TrashedItem.OriginalPath"/> is always null: macOS records the
    /// Put-Back source in a private binary store (the <c>.DS_Store</c>-adjacent trash metadata) that
    /// we do not parse in v1. Never throws — one bad entry must not crash the listing.</summary>
    private static TrashedItem? TryReadEntry(string entryPath, string trashLocation)
    {
        try
        {
            string name = Path.GetFileName(entryPath.TrimEnd('/'));
            if (name.Length == 0)
            {
                return null;
            }

            bool isDir = Directory.Exists(entryPath);
            DateTime deletedUtc;
            long? size;
            if (isDir)
            {
                deletedUtc = Directory.GetLastWriteTimeUtc(entryPath);
                size = null;
            }
            else
            {
                var info = new FileInfo(entryPath);
                deletedUtc = info.LastWriteTimeUtc;
                size = info.Length;
            }

            return new TrashedItem(name, OriginalPath: null, deletedUtc, size, trashLocation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public EmptyResult Empty()
    {
        int removed = 0;
        foreach ((string trashDir, string _) in TrashRoots())
        {
            removed += EmptyOne(trashDir);
        }

        return new EmptyResult(removed);
    }

    /// <summary>Deletes the contents of one Trash directory (never the directory itself), counting
    /// top-level items removed and continuing past per-item failures.</summary>
    private static int EmptyOne(string trashDir)
    {
        string[] entries;
        try
        {
            if (!Directory.Exists(trashDir)) { return 0; }
            entries = Directory.GetFileSystemEntries(trashDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        int removed = 0;
        foreach (string entry in entries)
        {
            if (TryDeleteAny(entry))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>Enumerates the Trash roots to scan for List/Empty: the user's <c>~/.Trash</c>
    /// (label "home"), plus any readable per-volume <c>/Volumes/&lt;vol&gt;/.Trashes/&lt;uid&gt;</c>
    /// (label = the volume path). Volume discovery is best-effort — a Trashes we can't read is
    /// silently skipped — and never throws.</summary>
    private static IEnumerable<(string TrashDir, string Label)> TrashRoots()
    {
        string home = HomeDir();
        if (home.Length > 0)
        {
            yield return (Path.Combine(home, ".Trash"), "home");
        }

        uint uid = GetUidNative();
        string uidName = uid.ToString(CultureInfo.InvariantCulture);
        string[] volumes;
        try
        {
            if (!Directory.Exists("/Volumes")) { yield break; }
            volumes = Directory.GetDirectories("/Volumes");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (string volume in volumes)
        {
            string candidate = Path.Combine(volume, ".Trashes", uidName);
            bool exists;
            try
            {
                exists = Directory.Exists(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                exists = false;
            }

            if (exists)
            {
                yield return (candidate, volume);
            }
        }
    }

    /// <summary>Resolves the user's home directory: <c>SpecialFolder.UserProfile</c>, falling back to
    /// <c>$HOME</c>. Returns empty string when neither resolves (then the home-trash root is skipped).</summary>
    private static string HomeDir()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        }

        return home;
    }

    private static bool TryDeleteAny(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                return true;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }

            // A dangling symlink reports neither File nor Directory Exists; remove it as a file.
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
