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
    private readonly Yort.ShellKit.GlobMatcher _globMatcher;
    private readonly Regex[] _regexes;
    private readonly bool _hasFileFilters;
    private readonly List<WalkError> _walkErrors = new();

    /// <summary>
    /// Errors encountered during the most recent <see cref="Build"/> call: directories
    /// that could not be enumerated (typically <see cref="UnauthorizedAccessException"/>
    /// or <see cref="DirectoryNotFoundException"/> from a vanishing path) and individual
    /// files that disappeared between enumeration and stat.
    /// </summary>
    /// <remarks>
    /// Round-1 fresh-eyes 2026-05-09 silent-failure-hunter C1: pre-fix, the catch sites
    /// silently <c>return</c>'d / <c>continue</c>'d, producing a partial tree with no
    /// indication. The README documents exit code 1 as "Runtime error (permission
    /// denied, invalid path)" but the binary never returned 1 mid-walk. Real
    /// <c>tree(1)</c> prints <c>[error opening dir]</c> per inaccessible node. We collect
    /// here and let the CLI layer surface to stderr + set exit 1.
    /// </remarks>
    public IReadOnlyList<WalkError> WalkErrors => _walkErrors;

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
        _globMatcher = new Yort.ShellKit.GlobMatcher(options.GlobPatterns, options.CaseInsensitive);

        RegexOptions regexOptions = RegexOptions.CultureInvariant;
        if (options.CaseInsensitive)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        _regexes = options.RegexPatterns
            .Select(p => SafeRegex.Create(p, regexOptions))
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
        // Reset walk-error log so successive Build calls don't accumulate. Each Build
        // exposes the errors hit during ITS walk only.
        _walkErrors.Clear();

        string fullRoot = Path.GetFullPath(rootPath);

        // Track visited real paths for symlink cycle detection. Without this,
        // a symlink pointing to an ancestor directory causes infinite recursion
        // and a StackOverflowException. Case-insensitive on Windows (NTFS).
        // Case-sensitive on Linux and macOS (macOS can run case-sensitive APFS).
        var visitedDirs = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
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
        // Tier-2 baseline 2026-05-06 finding F1: README documents `--max-depth N` as
        // "include nodes with depth ≤ N" (specifically "0 = root only"). Pre-fix, the
        // depth filter only gated RECURSION (line below: `depth < MaxDepth` for descending
        // into a child dir), but children were ALWAYS added before the recursion check.
        // That made `--max-depth 0` produce root + its immediate children — not "root
        // only" as documented. The early return here fixes the semantics: when called
        // with parent.depth = depth, the children we'd add live at depth+1, so skip
        // entirely if depth >= MaxDepth. The inner recursion guard below now becomes
        // a redundant defense-in-depth check (BuildChildren returns early on the next
        // call anyway) but it's kept for clarity.
        if (_options.MaxDepth != null && depth >= _options.MaxDepth)
        {
            return;
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(dirPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            // SFH C1 round-1 2026-05-09: surface as walk error so CLI exits 1 + diagnoses.
            _walkErrors.Add(new WalkError(dirPath, "permission denied: " + ex.Message));
            parentNode.IsUnreadable = true;
            return;
        }
        catch (DirectoryNotFoundException ex)
        {
            // Race: directory existed at the parent's enumeration moment but vanished
            // before we descended. Surface as a non-permission walk error.
            _walkErrors.Add(new WalkError(dirPath, "directory not found (removed during walk?): " + ex.Message));
            return;
        }
        catch (IOException ex)
        {
            // Catch-all for transient filesystem failures (network share dropped,
            // file-system reset). Surface; do not pretend the directory was empty.
            _walkErrors.Add(new WalkError(dirPath, "I/O error: " + ex.Message));
            parentNode.IsUnreadable = true;
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
            catch (UnauthorizedAccessException ex)
            {
                _walkErrors.Add(new WalkError(fullPath, "permission denied (attributes): " + ex.Message));
                continue;
            }
            catch (FileNotFoundException)
            {
                // File was enumerated but vanished before GetAttributes — common during
                // active builds. Mid-walk volatility is not a contract violation; do not
                // surface as a walk error.
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
                    // ResolveLinkTarget resolves symlinks to their real target so that
                    // a symlink pointing to an ancestor directory is detected as a cycle.
                    // Path.GetFullPath only normalises "." and ".." — it does NOT resolve symlinks.
                    string realPath;
                    try
                    {
                        var dirInfo = new DirectoryInfo(fullPath);
                        string? resolved = dirInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
                        realPath = resolved ?? Path.GetFullPath(fullPath);
                    }
                    catch
                    {
                        realPath = Path.GetFullPath(fullPath);
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
                catch (UnauthorizedAccessException ex)
                {
                    _walkErrors.Add(new WalkError(fullPath, "permission denied (stat): " + ex.Message));
                    continue;
                }
                catch (FileNotFoundException)
                {
                    // File vanished between enumeration and stat; same race rationale as
                    // GetAttributes above. Do not surface.
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
                _ => string.Compare(a.Name, b.Name,
                    _options.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
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
        return IsMatchSafe(_regexes, fileName);
    }

    /// <summary>
    /// Returns <c>true</c> when at least one of <paramref name="regexes"/> matches
    /// <paramref name="input"/>. Swallows <see cref="RegexMatchTimeoutException"/> per
    /// pattern so a single pathological pattern cannot wedge the walk; treats timeout
    /// as a non-match for that pattern. Only standard-engine patterns can time out;
    /// <see cref="RegexOptions.NonBacktracking"/> patterns have linear-time guarantees.
    /// </summary>
    /// <remarks>
    /// Internal so tests can call directly with a regex configured with a 1-tick timeout
    /// to deterministically exercise the timeout-catch path. Round-2 fresh-eyes 2026-05-09
    /// test-analyzer I7: prior to this extraction, the swallow path was unreachable from
    /// any unit test because <see cref="MatchesAnyRegex"/> was instance-private and the
    /// regexes were SafeRegex-created with a 2-second timeout.
    /// </remarks>
    internal static bool IsMatchSafe(Regex[] regexes, string input)
    {
        foreach (Regex regex in regexes)
        {
            try
            {
                if (regex.IsMatch(input))
                {
                    return true;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Pattern timed out on this input — treat as non-match for this pattern.
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a file is executable. On Windows, checks the extension against
    /// a known set (<c>.exe .cmd .bat .ps1 .com</c>, case-insensitive). On Unix, checks
    /// <see cref="System.IO.UnixFileMode.UserExecute"/> on the file's mode.
    /// </summary>
    /// <remarks>
    /// Round-2 fresh-eyes 2026-05-09 silent-failure-hunter F1: pre-fix the Unix branch
    /// caught all exceptions and silently returned <c>false</c>, hiding permission denials
    /// and I/O failures the same way the C1 main-walk catch sites did before being closed.
    /// Now narrowed to <see cref="PlatformNotSupportedException"/> (the documented intent);
    /// other exceptions surface as a <see cref="WalkError"/> while still degrading exec
    /// detection to <c>false</c> rather than crashing the walk.
    /// Promoted from <c>static</c> to instance so it can append to <c>_walkErrors</c>.
    /// </remarks>
    private bool IsExecutable(string fullPath)
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
        catch (PlatformNotSupportedException)
        {
            // The runtime does not implement Unix file modes (e.g. AOT runtime stubs on
            // an unsupported platform). Documented intent for this catch.
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            // The user can list this directory but not stat the file. Surface so the CLI
            // can report it; degrade exec detection to false rather than crash the walk.
            _walkErrors.Add(new WalkError(fullPath, "permission denied (mode): " + ex.Message));
            return false;
        }
        catch (IOException ex)
        {
            // Transient FS failure (network share dropped, file vanished mid-stat).
            _walkErrors.Add(new WalkError(fullPath, "I/O error reading mode: " + ex.Message));
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
