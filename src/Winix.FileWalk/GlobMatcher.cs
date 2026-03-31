using System.Text.RegularExpressions;

namespace Winix.FileWalk;

/// <summary>
/// Matches filenames against one or more glob patterns. Any pattern matching is a hit (OR logic).
/// Supports case-insensitive matching for Windows/macOS.
/// </summary>
/// <remarks>
/// <para>
/// Patterns use glob syntax: <c>*</c> matches any sequence of characters within a path segment,
/// <c>**</c> matches across directory separators, and <c>?</c> matches exactly one character.
/// For filename-only matching (no path components), use simple patterns such as <c>*.cs</c> or <c>test?.txt</c>.
/// </para>
/// <para>
/// Patterns are compiled to regular expressions at construction time for efficient repeated matching.
/// <c>Microsoft.Extensions.FileSystemGlobbing.Matcher</c>'s in-memory <c>Match</c> overloads do not
/// support the <c>?</c> wildcard reliably; this class uses its own regex-based conversion instead.
/// </para>
/// </remarks>
public sealed class GlobMatcher
{
    private readonly Regex[] _regexes;

    /// <summary>
    /// Initialises a new <see cref="GlobMatcher"/> with the given patterns.
    /// </summary>
    /// <param name="patterns">
    /// The glob patterns to match against. Each pattern is an include rule; an empty collection
    /// results in a matcher that never matches.
    /// </param>
    /// <param name="caseInsensitive">
    /// <see langword="true"/> to match filenames case-insensitively (appropriate for Windows and macOS);
    /// <see langword="false"/> for case-sensitive matching (appropriate for Linux).
    /// </param>
    public GlobMatcher(IEnumerable<string> patterns, bool caseInsensitive)
    {
        RegexOptions options = RegexOptions.CultureInvariant;
        if (caseInsensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        _regexes = patterns
            .Select(p => new Regex(GlobToRegex(p.Replace('\\', '/')), options))
            .ToArray();
    }

    /// <summary>
    /// Gets a value indicating whether any patterns were provided. When <see langword="false"/>,
    /// <see cref="IsMatch"/> will always return <see langword="false"/>.
    /// </summary>
    public bool HasPatterns => _regexes.Length > 0;

    /// <summary>
    /// Tests whether the given filename matches any of the configured glob patterns.
    /// </summary>
    /// <param name="fileName">
    /// The filename (without directory path) to test, e.g. <c>"Program.cs"</c>.
    /// Backslashes are normalised to forward slashes before matching.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if at least one pattern matches; <see langword="false"/> if no
    /// patterns match, or if no patterns were configured.
    /// </returns>
    public bool IsMatch(string fileName)
    {
        if (_regexes.Length == 0)
        {
            return false;
        }

        // Normalise separators — patterns are compiled with forward slashes
        string normalised = fileName.Replace('\\', '/');

        foreach (Regex regex in _regexes)
        {
            if (regex.IsMatch(normalised))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Converts a glob pattern to an anchored regular expression string.
    /// Supports <c>*</c> (any chars within a segment), <c>**</c> (any chars including separators),
    /// and <c>?</c> (exactly one character). All other regex metacharacters are escaped.
    /// </summary>
    /// <param name="glob">The glob pattern, using forward slashes as path separators.</param>
    /// <returns>A regex pattern string anchored at both ends.</returns>
    internal static string GlobToRegex(string glob)
    {
        var sb = new System.Text.StringBuilder("^");
        int i = 0;

        while (i < glob.Length)
        {
            char c = glob[i];

            if (c == '*')
            {
                // Check for ** (matches anything including path separators)
                if (i + 1 < glob.Length && glob[i + 1] == '*')
                {
                    sb.Append(".*");
                    i += 2;
                }
                else
                {
                    // Single * — matches anything except a path separator
                    sb.Append("[^/]*");
                    i++;
                }
            }
            else if (c == '?')
            {
                // Matches exactly one character that is not a path separator
                sb.Append("[^/]");
                i++;
            }
            else
            {
                // Escape all other regex metacharacters
                sb.Append(Regex.Escape(c.ToString()));
                i++;
            }
        }

        sb.Append('$');
        return sb.ToString();
    }
}
