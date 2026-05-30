#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Winix.Trash;

/// <summary>FreeDesktop.org Trash-spec backend (Linux). Implements the home-volume trash
/// (<c>$XDG_DATA_HOME/Trash</c> or <c>~/.local/share/Trash</c>); cross-volume top-dir trashes are
/// added in Task 10. All per-path failures are recorded as outcomes — the backend never throws for
/// an operational error.</summary>
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxFreeDesktopBackend : ITrashBackend
{
    private readonly string _homeTrashDir;

    /// <summary>Creates a backend rooted at the resolved home trash directory.</summary>
    public LinuxFreeDesktopBackend()
        : this(MountResolver.HomeTrashDir())
    {
    }

    /// <summary>Test/seam constructor allowing the home trash root to be overridden.</summary>
    internal LinuxFreeDesktopBackend(string homeTrashDir)
    {
        _homeTrashDir = homeTrashDir;
    }

    // Lazily-resolved home volume device id (cached — the home trash never moves volumes mid-run).
    private ulong? _homeDeviceId;

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

    private PathOutcome TrashOne(string input)
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

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return new PathOutcome(input, "no such file or directory.");
        }

        // F1 trash-root guard: never trash the trash. Resolve a final symlink target so a link
        // pointing into a trash root cannot smuggle the trash dir back into itself.
        string canonical = ResolveFinalTarget(fullPath);
        if (IsUnderTrashRoot(canonical))
        {
            return new PathOutcome(input, "refusing to trash the trash directory itself.");
        }

        // Resolve the spec-correct trash dir for this file's volume: home trash for same-device
        // files, else <topdir>/.Trash-<uid> on the file's own mount (so the move stays a rename).
        string trashDir;
        try
        {
            trashDir = MountResolver.ResolveTrashDir(
                fullPath,
                DeviceIdOf,
                _homeTrashDir,
                HomeDeviceId(),
                CurrentUid(),
                MountPointOf);
        }
        catch (IOException)
        {
            return new PathOutcome(input, "could not determine the volume for this path.");
        }

        string infoDir = trashDir + "/info";
        string filesDir = trashDir + "/files";

        try
        {
            EnsureTrashDirs(trashDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PathOutcome(input, "could not create trash directory.");
        }

        string leaf = Path.GetFileName(fullPath.TrimEnd('/'));
        if (string.IsNullOrEmpty(leaf))
        {
            return new PathOutcome(input, "cannot determine a name to trash.");
        }

        // Step 2: atomically reserve info/<name>.trashinfo via O_EXCL (FileMode.CreateNew). On a
        // name collision we bump a numeric suffix and retry — this reservation is TOCTOU-safe and
        // safe against a GUI file manager concurrently writing the same trash dir.
        FileStream? infoStream = null;
        string reservedName = leaf;
        string infoPath;
        for (int suffix = 2; ; suffix++)
        {
            infoPath = infoDir + "/" + reservedName + ".trashinfo";
            try
            {
                infoStream = new FileStream(infoPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                break;
            }
            catch (IOException) when (File.Exists(infoPath))
            {
                // Name taken — bump suffix and retry. (IOException without an existing file is a
                // real failure and propagates to the outer handler below.)
                reservedName = leaf + "." + suffix.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new PathOutcome(input, "could not reserve a trash entry.");
            }
        }

        // Step 3: write the .trashinfo body into the reserved stream.
        try
        {
            string body = TrashInfo.Write(fullPath, DateTime.Now);
            using (var writer = new StreamWriter(infoStream))
            {
                writer.Write(body);
                writer.Flush();
            }
            infoStream = null; // disposed by the StreamWriter
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            infoStream?.Dispose();
            TryDelete(infoPath);
            return new PathOutcome(input, "could not write trash metadata.");
        }

        // Step 4: move the file/dir into files/<name> using the SAME reserved name. The resolved
        // trash dir is on the file's own volume, so this is a same-device rename.
        string destPath = filesDir + "/" + reservedName;
        try
        {
            if (Directory.Exists(fullPath))
            {
                Directory.Move(fullPath, destPath);
            }
            else
            {
                File.Move(fullPath, destPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Step 5 rollback: never leave an orphaned .trashinfo.
            TryDelete(infoPath);
            return new PathOutcome(input, "could not move item to trash.");
        }

        return new PathOutcome(input, null);
    }

    /// <inheritdoc/>
    public IReadOnlyList<TrashedItem> List()
    {
        var items = new List<TrashedItem>();
        foreach ((string trashDir, string label) in TrashRoots())
        {
            string infoDir = trashDir + "/info";
            string[] infoFiles;
            try
            {
                if (!Directory.Exists(infoDir)) { continue; }
                infoFiles = Directory.GetFiles(infoDir, "*.trashinfo");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string infoFile in infoFiles)
            {
                TrashedItem? item = TryReadEntry(infoFile, label);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
        }

        return items;
    }

    /// <summary>Enumerates the trash roots to scan for List/Empty: the home trash dir (label
    /// "home"), plus any top-dir <c>.Trash-&lt;uid&gt;</c> discoverable from <c>/proc/mounts</c>
    /// (labelled with the mount path). Top-dir discovery is best-effort — a mount we can't read is
    /// silently skipped — and intentionally does not chase trashes on mounts not listed there.</summary>
    private IEnumerable<(string TrashDir, string Label)> TrashRoots()
    {
        yield return (_homeTrashDir, "home");

        int uid = CurrentUid();
        string topDirName = ".Trash-" + uid.ToString(CultureInfo.InvariantCulture);
        foreach (string mountPoint in EnumerateMountPoints())
        {
            string candidate = (mountPoint == "/" ? string.Empty : mountPoint) + "/" + topDirName;
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
                yield return (candidate, mountPoint);
            }
        }
    }

    /// <summary>Reads mount-point paths from <c>/proc/mounts</c>. Returns empty on any failure —
    /// top-dir discovery is best-effort and must never crash List/Empty.</summary>
    private static IEnumerable<string> EnumerateMountPoints()
    {
        string[] lines;
        try
        {
            if (!File.Exists("/proc/mounts")) { return Array.Empty<string>(); }
            lines = File.ReadAllLines("/proc/mounts");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (string line in lines)
        {
            // Format: "device mountpoint fstype options dump pass". The mount point is field 2 and
            // uses octal escapes (\040 etc.) for spaces; we only need a path to probe, so decode them.
            string[] fields = line.Split(' ');
            if (fields.Length < 2) { continue; }

            string mountPoint = DecodeMountEscapes(fields[1]);
            if (mountPoint.Length > 0 && seen.Add(mountPoint))
            {
                result.Add(mountPoint);
            }
        }

        return result;
    }

    /// <summary>Decodes the octal <c>\NNN</c> escapes used in <c>/proc/mounts</c> mount-point fields.</summary>
    private static string DecodeMountEscapes(string field)
    {
        if (field.IndexOf('\\') < 0)
        {
            return field;
        }

        var sb = new System.Text.StringBuilder(field.Length);
        for (int i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && i + 3 < field.Length
                && field[i + 1] is >= '0' and <= '7'
                && field[i + 2] is >= '0' and <= '7'
                && field[i + 3] is >= '0' and <= '7')
            {
                int code = ((field[i + 1] - '0') << 6) | ((field[i + 2] - '0') << 3) | (field[i + 3] - '0');
                sb.Append((char)code);
                i += 3;
            }
            else
            {
                sb.Append(field[i]);
            }
        }

        return sb.ToString();
    }

    /// <summary>Reads one <c>.trashinfo</c> entry, or null when it should be skipped (F9: corrupt
    /// metadata or a missing sibling <c>files/&lt;name&gt;</c>). Never throws — one unreadable entry
    /// must not crash the listing.</summary>
    private TrashedItem? TryReadEntry(string infoFile, string trashLocation)
    {
        try
        {
            string name = Path.GetFileName(infoFile);
            if (name.EndsWith(".trashinfo", StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - ".trashinfo".Length);
            }

            TrashInfoRecord? record = TrashInfo.Parse(File.ReadAllText(infoFile));
            if (record is null)
            {
                return null; // F9: corrupt metadata
            }

            string filesPath = Path.GetDirectoryName(infoFile) is string infoDir
                ? Path.Combine(Path.GetDirectoryName(infoDir) ?? infoDir, "files", name)
                : name;
            bool isFile = File.Exists(filesPath);
            bool isDir = Directory.Exists(filesPath);
            if (!isFile && !isDir)
            {
                return null; // F9: orphaned metadata, no sibling payload
            }

            long? size = null;
            if (isFile)
            {
                try
                {
                    size = new FileInfo(filesPath).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    size = null;
                }
            }

            // DeletionDate is stored as local wall-clock with no timezone; mark it Local and
            // convert to UTC for the surfaced DeletedUtc.
            DateTime deletedUtc = DateTime.SpecifyKind(record.DeletionLocal, DateTimeKind.Local).ToUniversalTime();

            return new TrashedItem(name, record.OriginalPath, deletedUtc, size, trashLocation);
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

    private int EmptyOne(string trashDir)
    {
        int removed = 0;
        string infoDir = trashDir + "/info";
        string filesDir = trashDir + "/files";
        string[] infoFiles;
        try
        {
            if (!Directory.Exists(infoDir)) { return 0; }
            infoFiles = Directory.GetFiles(infoDir, "*.trashinfo");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        foreach (string infoFile in infoFiles)
        {
            // Count an item as removed when its metadata file is gone; continue past per-item errors.
            string name = Path.GetFileName(infoFile);
            if (name.EndsWith(".trashinfo", StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - ".trashinfo".Length);
            }

            string payload = filesDir + "/" + name;
            TryDeleteAny(payload);
            if (TryDelete(infoFile))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>True when <paramref name="canonical"/> equals or sits under any of this backend's
    /// trash roots: the home trash dir, or any top-dir <c>.Trash-&lt;uid&gt;</c> on any volume.</summary>
    private bool IsUnderTrashRoot(string canonical)
    {
        if (PathEqualsOrUnder(canonical, _homeTrashDir))
        {
            return true;
        }

        // Top-dir trashes live at arbitrary mount points, so we can't enumerate them up front.
        // Guard structurally instead: refuse any path whose components include this user's
        // ".Trash-<uid>" dir (or the spec's admin ".Trash" form).
        int uid = CurrentUid();
        string topDirName = ".Trash-" + uid.ToString(CultureInfo.InvariantCulture);
        foreach (string segment in canonical.Split('/'))
        {
            if (string.Equals(segment, topDirName, StringComparison.Ordinal)
                || string.Equals(segment, ".Trash", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Device id of the home trash volume, resolved once and cached. Falls back to a
    /// sentinel that never matches a real device when statx fails, so resolution degrades to the
    /// cross-volume (top-dir) branch rather than mis-routing into the home trash.</summary>
    private ulong HomeDeviceId()
    {
        if (_homeDeviceId is ulong cached)
        {
            return cached;
        }

        ulong dev;
        try
        {
            // The home trash dir may not exist yet; statx its existing ancestor (the home dir or /).
            dev = DeviceIdOf(NearestExistingAncestor(_homeTrashDir));
        }
        catch (IOException)
        {
            dev = ulong.MaxValue;
        }

        _homeDeviceId = dev;
        return dev;
    }

    /// <summary>Walks up until it finds a directory that exists, for statx of a not-yet-created path.</summary>
    private static string NearestExistingAncestor(string path)
    {
        string current = path;
        while (!Directory.Exists(current))
        {
            string? parent = Path.GetDirectoryName(current.TrimEnd('/'));
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
            {
                return "/";
            }

            current = parent;
        }

        return current;
    }

    private static bool PathEqualsOrUnder(string path, string root)
    {
        string p = path.TrimEnd('/');
        string r = root.TrimEnd('/');
        if (string.Equals(p, r, StringComparison.Ordinal))
        {
            return true;
        }

        return p.StartsWith(r + "/", StringComparison.Ordinal);
    }

    private void EnsureTrashDirs(string trashDir)
    {
        CreateDir0700(trashDir);
        CreateDir0700(trashDir + "/files");
        CreateDir0700(trashDir + "/info");
    }

    private static void CreateDir0700(string dir)
    {
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            // Spec §2: trash dirs are owner-only (mode 0700). SetUnixFileMode is AOT-safe and needs
            // no interop. Harmless no-op if the dir already existed with looser perms (we only set
            // on create to avoid clobbering an existing GUI-created trash's permissions).
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>Resolves a final symlink target so the trash-root guard cannot be bypassed by a link.</summary>
    private static string ResolveFinalTarget(string fullPath)
    {
        try
        {
            FileSystemInfo? resolved = Directory.Exists(fullPath)
                ? new DirectoryInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true)
                : new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true);
            if (resolved is not null)
            {
                return Path.GetFullPath(resolved.FullName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall through to the un-resolved path; the guard still compares the literal path.
        }

        return fullPath;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDeleteAny(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort empty: skip what we can't remove.
        }
    }
}
