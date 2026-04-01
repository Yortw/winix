using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Winix.FileWalk;

namespace Winix.TreeX;

/// <summary>
/// Recursively walks a directory tree and builds an in-memory <see cref="TreeNode"/> hierarchy
/// with filtering, sorting, pruning of empty branches, and optional size rollup.
/// </summary>
public sealed class TreeBuilder
{
    private readonly TreeBuilderOptions _options;
    private readonly Func<string, bool>? _isIgnored;
    private readonly GlobMatcher _globMatcher;
    private readonly Regex[] _regexes;
    private readonly bool _hasFileFilters;

    private static readonly HashSet<string> WindowsExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".cmd", ".bat", ".ps1", ".com"
    };

    /// <summary>
    /// Initialises a new <see cref="TreeBuilder"/> with the given options and an optional ignore predicate.
    /// </summary>
    /// <param name="options">Build configuration (filters, depth, sorting, sizes).</param>
    /// <param name="isIgnored">
    /// Optional predicate returning <see langword="true"/> for relative paths (forward-slash separated)
    /// that should be skipped. Called for both directories (skipping entire subtrees) and files.
    /// </param>
    public TreeBuilder(TreeBuilderOptions options, Func<string, bool>? isIgnored = null)
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

        _hasFileFilters = _globMatcher.HasPatterns
            || _regexes.Length > 0
            || options.TypeFilter != null
            || options.MinSize != null
            || options.MaxSize != null
            || options.NewerThan != null
            || options.OlderThan != null;
    }

    /// <summary>
    /// Builds an in-memory tree rooted at <paramref name="rootPath"/>. Applies filtering, sorting,
    /// pruning, and optional size rollup according to the configured <see cref="TreeBuilderOptions"/>.
    /// </summary>
    /// <param name="rootPath">Absolute path to the root directory to walk.</param>
    /// <returns>The root <see cref="TreeNode"/> with populated children.</returns>
    public TreeNode Build(string rootPath)
    {
        string fullRoot = Path.GetFullPath(rootPath);

        // Track visited real paths for symlink cycle detection. Without this,
        // a symlink pointing to an ancestor directory causes infinite recursion
        // and a StackOverflowException. Case-insensitive on Windows/macOS
        // (NTFS/APFS), case-sensitive on Linux (ext4/xfs).
        var visitedDirs = new HashSet<string>(OperatingSystem.IsLinux()
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase);
        visitedDirs.Add(fullRoot);

        TreeNode root = new()
        {
            Name = Path.GetFileName(fullRoot),
            FullPath = fullRoot,
            Type = FileEntryType.Directory,
            SizeBytes = -1,
            Modified = GetDirectoryModified(fullRoot),
            IsExecutable = false,
            IsMatch = !_hasFileFilters
        };

        BuildChildren(root, fullRoot, fullRoot, 0, visitedDirs);

        if (_hasFileFilters)
        {
            PruneEmpty(root);
        }

        if (_options.ComputeSizes)
        {
            RollUpSizes(root);
        }

        return root;
    }

    private void BuildChildren(TreeNode parentNode, string rootPath, string dirPath, int depth, HashSet<string> visitedDirs)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(dirPath);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
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

            string relativePath = FileSystemHelper.GetRelativePath(rootPath, fullPath);

            // Gitignore check — skip ignored entries.
            // Directories need a trailing slash so git evaluates directory-specific
            // patterns (e.g. "bin/" in .gitignore only matches directories).
            string ignorePath = isDirectory ? relativePath + "/" : relativePath;
            if (_isIgnored != null && _isIgnored(ignorePath))
            {
                continue;
            }

            if (isDirectory)
            {
                FileEntryType dirType = isSymlink ? FileEntryType.Symlink : FileEntryType.Directory;

                // When type filter is File, skip directories entirely from output
                // but we still need to check --type d below
                bool typeFilterExcludesThisDir = _options.TypeFilter != null
                    && _options.TypeFilter != dirType;

                TreeNode dirNode = new()
                {
                    Name = name,
                    FullPath = fullPath,
                    Type = dirType,
                    SizeBytes = -1,
                    Modified = GetDirectoryModified(fullPath),
                    IsExecutable = false,
                    // Directories are structural when file filters are active.
                    // When --type d, directories ARE the match targets.
                    IsMatch = _options.TypeFilter == FileEntryType.Directory
                        ? !typeFilterExcludesThisDir
                        : !_hasFileFilters
                };

                // Recurse if depth allows and we haven't visited this directory
                // (symlink cycle detection)
                if (_options.MaxDepth == null || depth < _options.MaxDepth)
                {
                    string realPath = fullPath;
                    try
                    {
                        realPath = Path.GetFullPath(fullPath);
                    }
                    catch
                    {
                        // Fall back to the path as-is
                    }

                    if (visitedDirs.Add(realPath))
                    {
                        BuildChildren(dirNode, rootPath, fullPath, depth + 1, visitedDirs);
                    }
                }

                parentNode.Children.Add(dirNode);
            }
            else
            {
                // File or file symlink
                FileEntryType fileType = isSymlink ? FileEntryType.Symlink : FileEntryType.File;

                // Type filter: skip files if we only want directories
                if (_options.TypeFilter != null && _options.TypeFilter != fileType)
                {
                    continue;
                }

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

                bool matchesFilters = true;

                // Glob filter: file must match at least one glob (OR) when globs are configured
                if (_globMatcher.HasPatterns && !_globMatcher.IsMatch(name))
                {
                    matchesFilters = false;
                }

                // Regex filter: file must match at least one regex (OR) when regexes are configured
                if (matchesFilters && _regexes.Length > 0 && !MatchesAnyRegex(name))
                {
                    matchesFilters = false;
                }

                // Size filters
                if (matchesFilters && _options.MinSize != null && fileSize < _options.MinSize)
                {
                    matchesFilters = false;
                }

                if (matchesFilters && _options.MaxSize != null && fileSize > _options.MaxSize)
                {
                    matchesFilters = false;
                }

                // Date filters
                if (matchesFilters && _options.NewerThan != null && modified <= _options.NewerThan)
                {
                    matchesFilters = false;
                }

                if (matchesFilters && _options.OlderThan != null && modified >= _options.OlderThan)
                {
                    matchesFilters = false;
                }

                TreeNode fileNode = new()
                {
                    Name = name,
                    FullPath = fullPath,
                    Type = fileType,
                    SizeBytes = fileSize,
                    Modified = modified,
                    IsExecutable = IsExecutable(fullPath),
                    IsMatch = matchesFilters
                };

                parentNode.Children.Add(fileNode);
            }
        }

        SortChildren(parentNode);
    }

    /// <summary>
    /// Sorts children: directories first, then files. Within each group, sorted by
    /// the configured <see cref="SortMode"/>.
    /// </summary>
    private void SortChildren(TreeNode node)
    {
        node.Children.Sort((a, b) =>
        {
            bool aIsDir = a.Type == FileEntryType.Directory;
            bool bIsDir = b.Type == FileEntryType.Directory;

            // Directories first
            if (aIsDir != bIsDir)
            {
                return aIsDir ? -1 : 1;
            }

            return _options.Sort switch
            {
                SortMode.Size => b.SizeBytes.CompareTo(a.SizeBytes),    // Largest first
                SortMode.Modified => b.Modified.CompareTo(a.Modified),  // Newest first
                _ => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)  // Alphabetical
            };
        });
    }

    /// <summary>
    /// Recursively removes directory children that have no matching descendants.
    /// A directory survives pruning if any descendant has <see cref="TreeNode.IsMatch"/> = true.
    /// </summary>
    private static void PruneEmpty(TreeNode node)
    {
        // Process children depth-first so leaf directories are pruned before parents
        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            TreeNode child = node.Children[i];

            if (child.Type == FileEntryType.Directory)
            {
                PruneEmpty(child);

                // Remove if no matching descendants remain
                if (!HasMatchingDescendant(child))
                {
                    node.Children.RemoveAt(i);
                }
            }
            else if (!child.IsMatch)
            {
                // Remove non-matching files
                node.Children.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Returns true if the node or any of its descendants has IsMatch = true.
    /// </summary>
    private static bool HasMatchingDescendant(TreeNode node)
    {
        if (node.IsMatch)
        {
            return true;
        }

        foreach (TreeNode child in node.Children)
        {
            if (HasMatchingDescendant(child))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Bottom-up size rollup: each directory's <see cref="TreeNode.SizeBytes"/> becomes the
    /// sum of its children's sizes (treating -1 as 0).
    /// </summary>
    private static void RollUpSizes(TreeNode node)
    {
        if (node.Type != FileEntryType.Directory)
        {
            return;
        }

        long total = 0;
        foreach (TreeNode child in node.Children)
        {
            RollUpSizes(child);
            total += child.SizeBytes > 0 ? child.SizeBytes : 0;
        }

        node.SizeBytes = total;
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

    /// <summary>
    /// Determines whether a file is executable. On Windows, checks the extension against
    /// a known set. On Unix, checks <see cref="System.IO.UnixFileMode.UserExecute"/>.
    /// </summary>
    private static bool IsExecutable(string fullPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string ext = Path.GetExtension(fullPath);
            return WindowsExecutableExtensions.Contains(ext);
        }

        // Unix/macOS: check user-execute permission
        try
        {
            var mode = File.GetUnixFileMode(fullPath);
            return (mode & UnixFileMode.UserExecute) != 0;
        }
        catch
        {
            // Not supported on this platform/runtime — fall back to false
            return false;
        }
    }

    /// <summary>
    /// Gets the last-modified time for a directory, or <see cref="DateTimeOffset.MinValue"/> on failure.
    /// </summary>
    private static DateTimeOffset GetDirectoryModified(string dirPath)
    {
        try
        {
            return new DateTimeOffset(Directory.GetLastWriteTimeUtc(dirPath), TimeSpan.Zero);
        }
        catch
        {
            return DateTimeOffset.MinValue;
        }
    }
}
