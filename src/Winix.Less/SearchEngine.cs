#nullable enable

using System.Collections.Generic;

namespace Winix.Less;

/// <summary>
/// Performs forward and backward text searches across a list of lines,
/// automatically stripping ANSI escape sequences before matching so that
/// coloured content is searched by its visible text.
/// </summary>
/// <remarks>
/// Search direction wraps around: <see cref="FindNext"/> continues from the
/// beginning when it reaches the end; <see cref="FindPrevious"/> continues from
/// the end when it reaches the beginning.
/// </remarks>
public sealed class SearchEngine
{
    /// <summary>
    /// Gets the pattern used in the most recent call to <see cref="FindNext"/> or
    /// <see cref="FindPrevious"/>, or <see langword="null"/> if no search has been
    /// performed yet.
    /// </summary>
    public string? CurrentPattern { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether all searches should be
    /// case-insensitive regardless of the pattern content.
    /// When both <see cref="IgnoreCase"/> and <see cref="SmartCase"/> are set,
    /// <see cref="IgnoreCase"/> takes precedence.
    /// </summary>
    public bool IgnoreCase { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether smart-case logic is active.
    /// When <see langword="true"/> and the pattern contains no uppercase letters,
    /// matching is case-insensitive; if the pattern contains at least one uppercase
    /// letter the search is case-sensitive.
    /// Ignored when <see cref="IgnoreCase"/> is <see langword="true"/>.
    /// </summary>
    public bool SmartCase { get; set; }

    /// <summary>
    /// Searches forward through <paramref name="lines"/> for <paramref name="pattern"/>,
    /// starting at <paramref name="startLine"/> and wrapping around to the beginning
    /// if the end is reached without a match.
    /// </summary>
    /// <param name="lines">The lines to search. Must not be <see langword="null"/>.</param>
    /// <param name="pattern">The text to search for. Must not be <see langword="null"/>.</param>
    /// <param name="startLine">The zero-based index of the first line to check.</param>
    /// <returns>
    /// The zero-based index of the first matching line, or <see langword="null"/> if
    /// <paramref name="pattern"/> is not found in any line.
    /// </returns>
    public int? FindNext(IReadOnlyList<string> lines, string pattern, int startLine)
    {
        CurrentPattern = pattern;

        var comparison = ResolveComparison(pattern);
        int count = lines.Count;

        // Search from startLine to end, then wrap from 0 back to startLine.
        for (int offset = 0; offset < count; offset++)
        {
            int index = (startLine + offset) % count;
            if (LineMatches(lines[index], pattern, comparison))
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>
    /// Searches backward through <paramref name="lines"/> for <paramref name="pattern"/>,
    /// starting at the line immediately before <paramref name="startLine"/> and wrapping
    /// around to the end if the beginning is reached without a match.
    /// </summary>
    /// <param name="lines">The lines to search. Must not be <see langword="null"/>.</param>
    /// <param name="pattern">The text to search for. Must not be <see langword="null"/>.</param>
    /// <param name="startLine">
    /// The zero-based index used as the search origin; the search begins on the line
    /// immediately before this position.
    /// </param>
    /// <returns>
    /// The zero-based index of the first matching line found in the backward direction,
    /// or <see langword="null"/> if <paramref name="pattern"/> is not found in any line.
    /// </returns>
    public int? FindPrevious(IReadOnlyList<string> lines, string pattern, int startLine)
    {
        CurrentPattern = pattern;

        var comparison = ResolveComparison(pattern);
        int count = lines.Count;

        // Search backward from (startLine - 1), wrapping around through the entire list.
        for (int offset = 1; offset <= count; offset++)
        {
            // Adding count before modulo avoids negative remainders.
            int index = (startLine - offset + count) % count;
            if (LineMatches(lines[index], pattern, comparison))
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>
    /// Determines the <see cref="StringComparison"/> to use for the given
    /// <paramref name="pattern"/> based on the current <see cref="IgnoreCase"/>
    /// and <see cref="SmartCase"/> settings.
    /// </summary>
    private StringComparison ResolveComparison(string pattern)
    {
        if (IgnoreCase)
        {
            return StringComparison.OrdinalIgnoreCase;
        }

        if (SmartCase)
        {
            // If the pattern is all-lowercase, treat the search as case-insensitive.
            return pattern == pattern.ToLowerInvariant()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        return StringComparison.Ordinal;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the visible text of <paramref name="line"/>
    /// (after ANSI stripping) contains <paramref name="pattern"/> using the specified
    /// <paramref name="comparison"/>.
    /// </summary>
    private static bool LineMatches(string line, string pattern, StringComparison comparison)
    {
        return AnsiText.StripAnsi(line).IndexOf(pattern, comparison) >= 0;
    }
}
