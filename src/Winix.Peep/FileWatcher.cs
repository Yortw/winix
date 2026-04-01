using Microsoft.Extensions.FileSystemGlobbing;

namespace Winix.Peep;

/// <summary>
/// Watches the filesystem for changes matching glob patterns. Fires a debounced event
/// after changes settle, preventing rapid-fire triggers when build tools touch many files.
/// </summary>
/// <remarks>
/// Each glob pattern (e.g. <c>"src/**/*.cs"</c>) is decomposed into a root directory and
/// a filter pattern. <see cref="FileSystemWatcher"/> watches the root recursively, and
/// <see cref="Matcher"/> tests whether changed paths match the glob.
/// </remarks>
public sealed class FileWatcher : IDisposable
{
    private readonly string[] _patterns;
    private readonly int _debounceMs;
    private readonly Func<string, bool>? _excludeFilter;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Matcher _matcher;
    private Timer? _debounceTimer;
    private readonly object _lock = new();
    private string? _basePath;
    private bool _disposed;

    /// <summary>
    /// Raised after file changes matching the glob patterns have settled (debounce period
    /// elapsed with no further changes).
    /// </summary>
    public event Action? FileChanged;

    /// <summary>
    /// Creates a new file watcher for the specified glob patterns.
    /// </summary>
    /// <param name="patterns">
    /// Glob patterns to watch (e.g. <c>"src/**/*.cs"</c>, <c>"tests/**/*.cs"</c>).
    /// Each pattern is relative to the current working directory.
    /// </param>
    /// <param name="debounceMs">
    /// Milliseconds to wait after the last file event before raising <see cref="FileChanged"/>.
    /// Default 300ms. Prevents rapid-fire triggers during multi-file operations (e.g. git checkout).
    /// </param>
    /// <param name="excludeFilter">
    /// Optional filter that returns true for paths that should be excluded (e.g. gitignore check).
    /// Called with the full file path after glob matching succeeds.
    /// </param>
    public FileWatcher(string[] patterns, int debounceMs = 300, Func<string, bool>? excludeFilter = null)
    {
        _patterns = patterns;
        _debounceMs = debounceMs;
        _excludeFilter = excludeFilter;

        _matcher = new Matcher();
        foreach (string pattern in patterns)
        {
            _matcher.AddInclude(pattern);
        }
    }

    /// <summary>
    /// The glob patterns being watched.
    /// </summary>
    public IReadOnlyList<string> Patterns => _patterns;

    /// <summary>
    /// Begins watching for file changes. Creates a <see cref="FileSystemWatcher"/> for each
    /// unique root directory derived from the glob patterns.
    /// </summary>
    public void Start()
    {
        string basePath = Directory.GetCurrentDirectory();
        _basePath = basePath;

        // Determine root directories to watch. For each pattern, extract the leading
        // literal path segments before any wildcard. If the pattern starts with a wildcard,
        // watch the current directory.
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string pattern in _patterns)
        {
            string root = ExtractRoot(pattern);
            string fullRoot = Path.GetFullPath(Path.Combine(basePath, root));

            if (Directory.Exists(fullRoot))
            {
                roots.Add(fullRoot);
            }
            else
            {
                // If the root doesn't exist, watch the base path and let glob filtering
                // handle it. This avoids a crash if a watched directory doesn't exist yet.
                roots.Add(basePath);
            }
        }

        foreach (string root in roots)
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size
                             | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };

            watcher.Changed += OnFileEvent;
            watcher.Created += OnFileEvent;
            watcher.Deleted += OnFileEvent;
            watcher.Renamed += OnRenameEvent;

            _watchers.Add(watcher);
        }
    }

    /// <summary>
    /// Stops watching for file changes.
    /// </summary>
    public void Stop()
    {
        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }

        _debounceTimer?.Dispose();

        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    /// <summary>
    /// Handles file system events (Created, Changed, Deleted). Tests the changed file
    /// against the glob matcher and starts/resets the debounce timer if it matches.
    /// </summary>
    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (IsMatch(e.FullPath))
        {
            ResetDebounce();
        }
    }

    /// <summary>
    /// Handles file rename events. Tests both old and new paths against the glob matcher.
    /// </summary>
    private void OnRenameEvent(object sender, RenamedEventArgs e)
    {
        if (IsMatch(e.FullPath) || IsMatch(e.OldFullPath))
        {
            ResetDebounce();
        }
    }

    /// <summary>
    /// Tests whether a full file path matches any of the configured glob patterns.
    /// The path is converted to a relative path (from the base directory captured at Start) before matching.
    /// </summary>
    private bool IsMatch(string fullPath)
    {
        string relativePath = Path.GetRelativePath(_basePath!, fullPath);

        // Normalise separators for the matcher (it expects forward slashes)
        relativePath = relativePath.Replace('\\', '/');

        if (!_matcher.Match(relativePath).HasMatches)
        {
            return false;
        }

        // Check exclude filter (e.g. gitignore) — if the file is excluded, don't trigger
        if (_excludeFilter is not null && _excludeFilter(fullPath))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resets (or starts) the debounce timer. Each file event restarts the countdown.
    /// When the timer fires without being reset, the <see cref="FileChanged"/> event is raised.
    /// </summary>
    private void ResetDebounce()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(
                _ => FileChanged?.Invoke(),
                null,
                _debounceMs,
                Timeout.Infinite);
        }
    }

    /// <summary>
    /// Extracts the literal root directory from a glob pattern. Returns everything before
    /// the first segment containing a wildcard character (<c>*</c>, <c>?</c>, <c>[</c>).
    /// If the pattern starts with a wildcard, returns <c>"."</c>.
    /// </summary>
    internal static string ExtractRoot(string pattern)
    {
        // Normalise to forward slashes for splitting
        string normalised = pattern.Replace('\\', '/');
        string[] segments = normalised.Split('/');

        var rootParts = new List<string>();

        foreach (string segment in segments)
        {
            if (segment.Contains('*') || segment.Contains('?') || segment.Contains('['))
            {
                break;
            }
            rootParts.Add(segment);
        }

        if (rootParts.Count == 0)
        {
            return ".";
        }

        return string.Join(Path.DirectorySeparatorChar.ToString(), rootParts);
    }
}
