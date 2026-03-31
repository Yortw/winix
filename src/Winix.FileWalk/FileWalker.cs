#nullable enable

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
            .Select(p => new Regex(p, regexOptions))
            .ToArray();
    }

    /// <summary>
    /// Walks one or more root directories and yields matching <see cref="FileEntry"/> records.
    /// Roots are walked in the order provided. Each root is an independent walk with depth starting at 0.
    /// </summary>
    /// <param name="roots">The root directories to walk.</param>
    /// <returns>A lazy sequence of file entries matching the configured predicates.</returns>
    public IEnumerable<FileEntry> Walk(IReadOnlyList<string> roots)
    {
        foreach (string root in roots)
        {
            string fullRoot = Path.GetFullPath(root);

            // Track visited real paths for symlink cycle detection
            HashSet<string>? visitedDirs = _options.FollowSymlinks
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : null;

            foreach (FileEntry entry in WalkDirectory(fullRoot, fullRoot, 0, visitedDirs))
            {
                yield return entry;
            }
        }
    }

    private IEnumerable<FileEntry> WalkDirectory(
        string root,
        string currentDir,
        int depth,
        HashSet<string>? visitedDirs)
    {
        // Track this directory for symlink cycle detection
        if (visitedDirs != null)
        {
            string realPath = currentDir;
            try
            {
                realPath = Path.GetFullPath(currentDir);
            }
            catch
            {
                // Fall back to the path as-is
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
                if (_isIgnored != null && _isIgnored(relativePath))
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
            if (regex.IsMatch(fileName))
            {
                return true;
            }
        }

        return false;
    }

}
