namespace Yort.ShellKit;

/// <summary>Classifies the outcome of expanding one argument.</summary>
internal enum GlobExpansionKind
{
    /// <summary>The argument contains no <c>*</c> or <c>?</c> — not a pattern; leave untouched.</summary>
    NotAPattern,
    /// <summary>The pattern matched at least one entry; <see cref="GlobExpansionResult.Matches"/> holds them.</summary>
    Expanded,
    /// <summary>A pattern, but nothing matched — pass the literal through (bash nullglob-off).</summary>
    NoMatch,
    /// <summary>The argument contains <c>**</c>, which argument expansion does not support.</summary>
    UnsupportedRecursive,
}

/// <summary>Result of expanding one argument: the outcome kind plus matches when expanded.</summary>
internal readonly record struct GlobExpansionResult(GlobExpansionKind Kind, IReadOnlyList<string> Matches)
{
    /// <summary>Singleton for non-pattern arguments.</summary>
    public static GlobExpansionResult NotAPattern { get; } = new(GlobExpansionKind.NotAPattern, Array.Empty<string>());
    /// <summary>Singleton for patterns with no matches.</summary>
    public static GlobExpansionResult NoMatch { get; } = new(GlobExpansionKind.NoMatch, Array.Empty<string>());
    /// <summary>Singleton for unsupported <c>**</c> patterns.</summary>
    public static GlobExpansionResult UnsupportedRecursive { get; } = new(GlobExpansionKind.UnsupportedRecursive, Array.Empty<string>());
}

/// <summary>
/// Expands a single command-line argument containing <c>*</c>/<c>?</c> wildcards against the
/// filesystem, with bash-compatible semantics (dotfile rule, no-match passthrough, files and
/// directories both match, trailing separator restricts to directories). Used by
/// <see cref="CommandLineParser"/> on Windows for tools that opt in via
/// <c>ExpandGlobPositionals()</c>.
/// </summary>
/// <remarks>
/// Matching is done in-process via <see cref="GlobMatcher"/> against enumerated entry names —
/// never via <c>Directory.GetFiles(dir, pattern)</c>, whose OS-level matching also matches 8.3
/// short names (so <c>*.log</c> would match <c>*.log2</c>). The literal prefix before the first
/// wildcard segment is kept verbatim as typed; matched components use on-disk casing.
/// </remarks>
internal sealed class GlobArgExpander
{
    /// <summary>One enumerated directory entry: leaf name plus whether it is a directory.</summary>
    internal readonly record struct FsEntry(string Name, bool IsDirectory);

    private static readonly char[] Metachars = { '*', '?' };

    private readonly Func<string, List<FsEntry>> _enumerate;

    /// <summary>Creates an expander over the real filesystem.</summary>
    public GlobArgExpander()
        : this(DefaultEnumerate)
    {
    }

    /// <summary>Test seam: creates an expander over a custom directory enumerator.</summary>
    /// <param name="enumerate">Maps a directory path ("." for the current directory) to its entries.</param>
    internal GlobArgExpander(Func<string, List<FsEntry>> enumerate)
    {
        _enumerate = enumerate;
    }

    /// <summary>Expands one argument. See <see cref="GlobExpansionKind"/> for outcomes.</summary>
    public GlobExpansionResult Expand(string arg)
    {
        if (arg.IndexOfAny(Metachars) < 0)
        {
            return GlobExpansionResult.NotAPattern;
        }
        if (arg.Contains("**", StringComparison.Ordinal))
        {
            return GlobExpansionResult.UnsupportedRecursive;
        }

        // Trailing separator(s) → directories only; preserved verbatim on output.
        int end = arg.Length;
        while (end > 0 && (arg[end - 1] == '\\' || arg[end - 1] == '/'))
        {
            end--;
        }
        string trailing = arg[end..];
        string body = arg[..end];
        bool dirsOnly = trailing.Length > 0;

        // Split into segments, remembering each segment's start offset so we can recover
        // both the verbatim literal prefix and the typed separator preceding each segment.
        var segments = new List<(string Text, int Start)>();
        int segStart = 0;
        for (int i = 0; i <= body.Length; i++)
        {
            if (i == body.Length || body[i] == '\\' || body[i] == '/')
            {
                segments.Add((body[segStart..i], segStart));
                segStart = i + 1;
            }
        }

        int firstWild = segments.FindIndex(s => s.Text.IndexOfAny(Metachars) >= 0);
        // firstWild >= 0 is guaranteed: metachars exist and separators are not metachars.

        // Verbatim text before the first wildcard segment, INCLUDING its trailing separator
        // ("C:\" must stay a root, not become drive-relative "C:").
        string prefix = firstWild == 0 ? "" : body[..segments[firstWild].Start];

        var candidates = new List<string> { prefix };
        for (int s = firstWild; s < segments.Count; s++)
        {
            (string segText, int start) = segments[s];
            bool isLast = s == segments.Count - 1;
            bool needDir = !isLast || dirsOnly;
            bool segHasWild = segText.IndexOfAny(Metachars) >= 0;
            GlobMatcher? matcher = segHasWild ? new GlobMatcher(new[] { segText }, caseInsensitive: true) : null;
            bool matchesDotEntries = segText.StartsWith('.');
            char sep = start > 0 ? body[start - 1] : '\\';

            var next = new List<string>();
            foreach (string candidate in candidates)
            {
                string dir = ResolveDir(candidate);
                List<FsEntry> entries;
                try
                {
                    entries = _enumerate(dir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                    or System.Security.SecurityException or ArgumentException or NotSupportedException)
                {
                    // bash parity: an unreadable/invalid directory contributes no matches;
                    // the pattern falls through as a literal. Deliberately not an error.
                    continue;
                }

                foreach (FsEntry entry in entries)
                {
                    if (needDir && !entry.IsDirectory)
                    {
                        continue;
                    }

                    bool isMatch;
                    if (matcher is not null)
                    {
                        if (!matchesDotEntries && entry.Name.StartsWith('.'))
                        {
                            continue; // bash dotfile rule
                        }
                        isMatch = matcher.IsMatch(entry.Name);
                    }
                    else
                    {
                        // Literal segment after a wildcard: case-insensitive equality,
                        // emitting on-disk casing.
                        // Note: '.'/'..' literal segments never match here (no dot entries in enumeration) — documented known limitation.
                        isMatch = string.Equals(entry.Name, segText, StringComparison.OrdinalIgnoreCase);
                    }

                    if (isMatch)
                    {
                        if (candidate.Length == 0)
                        {
                            next.Add(entry.Name);
                        }
                        else if (candidate[^1] is '\\' or '/')
                        {
                            next.Add(candidate + entry.Name);
                        }
                        else
                        {
                            next.Add(candidate + sep + entry.Name);
                        }
                    }
                }
            }

            candidates = next;
            if (candidates.Count == 0)
            {
                break;
            }
        }

        if (candidates.Count == 0)
        {
            return GlobExpansionResult.NoMatch;
        }

        candidates.Sort(StringComparer.OrdinalIgnoreCase);
        if (trailing.Length > 0)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                candidates[i] += trailing;
            }
        }
        return new GlobExpansionResult(GlobExpansionKind.Expanded, candidates);
    }

    /// <summary>
    /// Converts a candidate path to a directory key for the enumerator.
    /// Strips a trailing path separator for ordinary paths (so "src\" becomes "src"),
    /// but preserves it for drive roots ("C:\") and UNC roots ("\\server\share\") where
    /// removing the separator would produce an ambiguous or invalid path form.
    /// </summary>
    private static string ResolveDir(string candidate)
    {
        if (candidate.Length == 0)
        {
            return ".";
        }

        char last = candidate[^1];
        if (last != '\\' && last != '/')
        {
            // No trailing separator — use verbatim.
            return candidate;
        }

        // UNC paths (\\server\share\) — keep the trailing separator; the root form requires it.
        if (candidate.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return candidate;
        }

        string stripped = candidate.TrimEnd('\\', '/');

        // Drive root: stripping "C:\" leaves "C:" which is drive-relative, not a root.
        // Keep the original so the enumerator receives the valid root form.
        if (stripped.Length == 2 && stripped[1] == ':')
        {
            return candidate;
        }

        return stripped;
    }

    private static List<FsEntry> DefaultEnumerate(string dir)
    {
        var result = new List<FsEntry>();
        // Hidden/system attributes deliberately NOT filtered: bash/Git Bash on Windows match
        // them, and attribute filtering would be a silent data-dependent divergence (see ADR).
        foreach (string path in Directory.EnumerateFileSystemEntries(dir))
        {
            result.Add(new FsEntry(Path.GetFileName(path), Directory.Exists(path)));
        }
        return result;
    }
}
