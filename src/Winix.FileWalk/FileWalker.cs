
using System.Text.RegularExpressions;

namespace Winix.FileWalk;

/// <summary>
/// Enumerates directory trees and yields <see cref="FileEntry"/> records matching configured predicates.
/// Uses lazy enumeration via <c>yield return</c> and skips subtrees early when possible.
/// </summary>
public sealed class FileWalker
{
    private readonly FileWalkerOptions _options;
    private readonly Func<string, bool>? _isIgnored;
    private readonly GlobMatcher _globMatcher;
    private readonly Regex[] _regexes;

    /// <summary>
    /// Initialises a new <see cref="FileWalker"/> with the given options and an optional ignore predicate.
    /// </summary>
    /// <param name="options">Walk configuration (filters, depth, flags).</param>
    /// <param name="isIgnored">
    /// Optional predicate that returns <see langword="true"/> for paths that should be skipped.
    /// Receives a relative path (forward-slash separated) from the search root. Called for
    /// directories before recursing (skipping entire subtrees) and for files individually.
    /// </param>
    public FileWalker(FileWalkerOptions options, Func<string, bool>? isIgnored = null)
    {
        _options = options;
        _isIgnored = isIgnored;
        _globMatcher = new GlobMatcher(options.GlobPatterns, options.CaseInsensitive);

        RegexOptions regexOptions = RegexOptions.CultureInvariant;
        if (options.CaseInsensitive)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        _regexes = options.RegexPatterns
            .Select(p => SafeRegex.Create(p, regexOptions))
            .ToArray();
    }

    /// <summary>
    /// Walks one or more root directories and yields matching <see cref="FileEntry"/> records.
    /// Each root is yielded as a depth-0 entry (subject to hidden / type-filter checks),
    /// followed by its contents at depth 1, 2, … up to <see cref="FileWalkerOptions.MaxDepth"/>.
    /// </summary>
    /// <remarks>
    /// Tier-2 baseline 2026-05-06 finding F1: pre-fix the search root was never yielded
    /// and immediate children were at depth 0. That diverged from GNU find
    /// (<c>find . -maxdepth 0</c> emits <c>.</c> itself; <c>-maxdepth N</c> includes up to
    /// N levels deep) and from treex's post-F1 depth model. README documents files as a
    /// "find replacement" so user muscle memory should carry across — that's the driving
    /// constraint. After this fix: search root has depth 0, immediate children depth 1,
    /// etc. Existing users running <c>files . --max-depth N</c> need to add 1 to their
    /// depth values (deliberate breaking change).
    /// </remarks>
    /// <param name="roots">The root directories to walk.</param>
    /// <returns>A lazy sequence of file entries matching the configured predicates.</returns>
    public IEnumerable<FileEntry> Walk(IReadOnlyList<string> roots)
    {
        foreach (string root in roots)
        {
            string fullRoot = Path.GetFullPath(root);

            // Track visited real paths for symlink cycle detection.
            // Case-insensitive on Windows (NTFS is always case-insensitive).
            // Case-sensitive on Linux and macOS — macOS can run case-sensitive APFS,
            // and using Ordinal here is safe on case-insensitive APFS too (it just
            // won't detect the rare scenario of two differently-cased paths pointing
            // to the same case-insensitive directory, which is not a real-world concern).
            HashSet<string>? visitedDirs = _options.FollowSymlinks
                ? new HashSet<string>(OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal)
                : null;

            // F1 fix: yield the search root as depth 0, then descend with depth=1.
            FileEntry? rootEntry = TryMakeFilteredRootEntry(fullRoot);
            if (rootEntry is not null)
            {
                yield return rootEntry;
            }

            // Skip recursion if the root is a symlink and --follow isn't set.
            FileAttributes rootAttrs;
            try { rootAttrs = File.GetAttributes(fullRoot); }
            catch { continue; }
            if ((rootAttrs & FileAttributes.ReparsePoint) != 0 && !_options.FollowSymlinks)
            {
                continue;
            }

            foreach (FileEntry entry in WalkDirectory(fullRoot, fullRoot, 1, visitedDirs))
            {
                yield return entry;
            }
        }
    }

    /// <summary>
    /// Builds the depth-0 search-root <see cref="FileEntry"/> if it passes the same
    /// hidden + type filters that would apply to any directory entry yielded inside
    /// <see cref="WalkDirectory"/>. Returns <see langword="null"/> when filtered out.
    /// </summary>
    /// <remarks>
    /// Glob/regex/size/date/text-binary filters are not applied to the root — consistent
    /// with how WalkDirectory treats interior directories (those filters are file-only).
    /// </remarks>
    private FileEntry? TryMakeFilteredRootEntry(string fullRoot)
    {
        FileAttributes attrs;
        try
        {
            attrs = File.GetAttributes(fullRoot);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        string name = Path.GetFileName(fullRoot);
        if (name.Length == 0)
        {
            // Drive roots (e.g. "C:\\") produce empty names from Path.GetFileName.
            // Use the full path so the user sees something meaningful.
            name = fullRoot;
        }

        bool isSymlink = (attrs & FileAttributes.ReparsePoint) != 0;
        FileEntryType rootType = isSymlink ? FileEntryType.Symlink : FileEntryType.Directory;

        if (!_options.IncludeHidden && FileSystemHelper.IsHidden(fullRoot, name, attrs))
        {
            return null;
        }

        if (!ShouldYieldDirectory(rootType))
        {
            return null;
        }

        // MaxDepth < 0 is defensive — Program.cs rejects negative values, but if one
        // slipped through the root is at depth 0 and we should still skip it.
        if (_options.MaxDepth is int max && max < 0)
        {
            return null;
        }

        // Relative path for the root is "." (matches the user's intuitive "the
        // directory you asked about" notion). Absolute output is handled by
        // MakeDirectoryEntry's AbsolutePaths branch.
        return MakeDirectoryEntry(fullRoot, ".", name, rootType, depth: 0);
    }

    private IEnumerable<FileEntry> WalkDirectory(
        string root,
        string currentDir,
        int depth,
        HashSet<string>? visitedDirs)
    {
        // Tier-2 baseline 2026-05-06 finding F1: skip entirely if entries that would
        // be yielded in this call exceed MaxDepth. Pre-fix the recursion guard (further
        // below) only prevented descending into the next level; entries at this level
        // were always yielded. Now gate the entry yielding too so that --max-depth N
        // filters depth ≤ N inclusive (matches GNU find's -maxdepth).
        if (_options.MaxDepth is int max && depth > max)
        {
            yield break;
        }

        // Track this directory for symlink cycle detection.
        // ResolveLinkTarget resolves symlinks to their real target so that a symlink
        // pointing to an ancestor directory is detected as a cycle. Path.GetFullPath
        // only normalises "." and ".." — it does NOT resolve symlinks.
        if (visitedDirs != null)
        {
            string realPath;
            try
            {
                var dirInfo = new DirectoryInfo(currentDir);
                string? resolved = dirInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
                realPath = resolved ?? Path.GetFullPath(currentDir);
            }
            catch
            {
                // Permission error or invalid path — fall back to canonical form
                realPath = Path.GetFullPath(currentDir);
            }

            if (!visitedDirs.Add(realPath))
            {
                // Already visited -- cycle detected, stop recursion
                yield break;
            }
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(currentDir);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        catch (DirectoryNotFoundException)
        {
            yield break;
        }

        foreach (string fullPath in entries)
        {
            string name = Path.GetFileName(fullPath);

            // Get attributes once -- used for hidden check, directory/symlink detection
            FileAttributes attrs;
            try
            {
                attrs = File.GetAttributes(fullPath);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (FileNotFoundException)
            {
                continue;
            }

            if (!_options.IncludeHidden && FileSystemHelper.IsHidden(fullPath, name, attrs))
            {
                continue;
            }

            bool isDirectory = (attrs & FileAttributes.Directory) != 0;
            bool isSymlink = (attrs & FileAttributes.ReparsePoint) != 0;

            string relativePath = FileSystemHelper.GetRelativePath(root, fullPath);

            if (isDirectory)
            {
                FileEntryType dirType = isSymlink ? FileEntryType.Symlink : FileEntryType.Directory;

                // Gitignore check for directories — skip entire subtree if ignored.
                // This is the key performance optimisation: one check per directory
                // eliminates all per-file checks for ignored subtrees like bin/ and obj/.
                // Trailing slash is required so git evaluates directory-specific patterns
                // (e.g. "bin/" in .gitignore only matches directories, not files named "bin").
                if (_isIgnored != null && _isIgnored(relativePath + "/"))
                {
                    continue;
                }

                // Skip symlink directories unless FollowSymlinks is set
                if (isSymlink && !_options.FollowSymlinks)
                {
                    // Yield symlink dir entry if type filter allows, but don't recurse
                    if (ShouldYieldDirectory(dirType))
                    {
                        yield return MakeDirectoryEntry(fullPath, relativePath, name, dirType, depth);
                    }
                    continue;
                }

                // Yield directory entry if type filter allows
                if (ShouldYieldDirectory(dirType))
                {
                    yield return MakeDirectoryEntry(fullPath, relativePath, name, dirType, depth);
                }

                // Recurse if depth allows
                if (_options.MaxDepth == null || depth < _options.MaxDepth)
                {
                    foreach (FileEntry child in WalkDirectory(root, fullPath, depth + 1, visitedDirs))
                    {
                        yield return child;
                    }
                }
            }
            else
            {
                // File (or file symlink)
                FileEntryType fileType = isSymlink ? FileEntryType.Symlink : FileEntryType.File;

                // Gitignore check for files (e.g. *.log patterns)
                if (_isIgnored != null && _isIgnored(relativePath))
                {
                    continue;
                }

                // Type filter: skip if we only want directories
                if (_options.TypeFilter != null && _options.TypeFilter != fileType)
                {
                    continue;
                }

                // Glob filter: file must match at least one glob (when globs are configured)
                if (_globMatcher.HasPatterns && !_globMatcher.IsMatch(name))
                {
                    continue;
                }

                // Regex filter: file must match at least one regex (when regexes are configured)
                if (_regexes.Length > 0 && !MatchesAnyRegex(name))
                {
                    continue;
                }

                // Size filters
                long fileSize;
                DateTimeOffset modified;
                try
                {
                    var info = new FileInfo(fullPath);
                    fileSize = info.Length;
                    modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (FileNotFoundException)
                {
                    continue;
                }

                if (_options.MinSize != null && fileSize < _options.MinSize)
                {
                    continue;
                }

                if (_options.MaxSize != null && fileSize > _options.MaxSize)
                {
                    continue;
                }

                // Date filters
                if (_options.NewerThan != null && modified <= _options.NewerThan)
                {
                    continue;
                }

                if (_options.OlderThan != null && modified >= _options.OlderThan)
                {
                    continue;
                }

                // Text/binary detection -- late filter, only after all other predicates pass
                bool? isText = null;
                if (_options.TextOnly != null)
                {
                    isText = ContentDetector.IsTextFile(fullPath);

                    // TextOnly == true: only text files; TextOnly == false: only binary files
                    if (_options.TextOnly.Value && !isText.Value)
                    {
                        continue;
                    }

                    if (!_options.TextOnly.Value && isText.Value)
                    {
                        continue;
                    }
                }

                string outputPath = _options.AbsolutePaths
                    ? fullPath.Replace('\\', '/')
                    : relativePath;

                yield return new FileEntry(
                    outputPath,
                    name,
                    fileType,
                    fileSize,
                    modified,
                    depth,
                    isText);
            }
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if the directory entry should be yielded based on type filter.
    /// Directories are yielded when there's no type filter, or the type filter matches.
    /// </summary>
    private bool ShouldYieldDirectory(FileEntryType dirType)
    {
        return _options.TypeFilter == null || _options.TypeFilter == dirType;
    }

    private FileEntry MakeDirectoryEntry(string fullPath, string relativePath, string name, FileEntryType type, int depth)
    {
        string outputPath = _options.AbsolutePaths
            ? fullPath.Replace('\\', '/')
            : relativePath;

        return new FileEntry(
            outputPath,
            name,
            type,
            -1,
            DateTimeOffset.MinValue,
            depth,
            null);
    }

    private bool MatchesAnyRegex(string fileName)
    {
        foreach (Regex regex in _regexes)
        {
            try
            {
                if (regex.IsMatch(fileName))
                {
                    return true;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Pattern timed out on this filename — treat as non-match rather than
                // hanging the process. Only fires for patterns that fell back to the
                // standard engine (NonBacktracking never times out).
            }
        }

        return false;
    }

}
