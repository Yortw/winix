#nullable enable

using System.Text;
using System.Text.RegularExpressions;

namespace Winix.Less;

/// <summary>
/// Utility methods for working with strings that may contain ANSI SGR escape sequences
/// (e.g. colour, bold). All operations treat escape sequences as zero-width so that
/// visible-character counts and offsets remain accurate even when formatting is present.
/// </summary>
public static partial class AnsiText
{
    // Matches SGR sequences: ESC [ <digits and semicolons> m
    // Compiled once as a source-generated regex for AOT compatibility.
    [GeneratedRegex(@"\x1b\[[0-9;]*m")]
    private static partial Regex AnsiEscapeRegex();

    /// <summary>
    /// Removes all ANSI SGR escape sequences from <paramref name="text"/>, returning only
    /// the visible characters.
    /// </summary>
    /// <param name="text">The text to strip. Must not be <see langword="null"/>.</param>
    /// <returns>The input with all ANSI escape sequences removed.</returns>
    public static string StripAnsi(string text)
    {
        return AnsiEscapeRegex().Replace(text, string.Empty);
    }

    /// <summary>
    /// Returns the number of visible characters in <paramref name="text"/>, excluding any
    /// ANSI escape sequences.
    /// </summary>
    /// <param name="text">The text to measure. Must not be <see langword="null"/>.</param>
    /// <returns>The count of visible characters.</returns>
    public static int VisibleLength(string text)
    {
        return StripAnsi(text).Length;
    }

    /// <summary>
    /// Truncates <paramref name="text"/> so that no more than <paramref name="maxWidth"/>
    /// visible characters are included. ANSI escape sequences that fall within the visible
    /// width are preserved; everything beyond the cutoff (including trailing sequences) is
    /// dropped.
    /// </summary>
    /// <param name="text">The text to truncate. Must not be <see langword="null"/>.</param>
    /// <param name="maxWidth">The maximum number of visible characters to include. Values
    /// &lt;= 0 produce an empty string.</param>
    /// <returns>
    /// A string containing at most <paramref name="maxWidth"/> visible characters, with any
    /// ANSI sequences that started before the cutoff preserved verbatim.
    /// </returns>
    public static string TruncateToWidth(string text, int maxWidth)
    {
        if (maxWidth <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        int visibleCount = 0;
        int pos = 0;

        while (pos < text.Length && visibleCount < maxWidth)
        {
            // If we're at the start of an ANSI escape sequence, copy it verbatim (zero width).
            if (text[pos] == '\x1b' && pos + 1 < text.Length && text[pos + 1] == '[')
            {
                int seqStart = pos;
                pos += 2; // skip ESC [

                while (pos < text.Length && text[pos] != 'm')
                {
                    pos++;
                }

                if (pos < text.Length)
                {
                    pos++; // skip the trailing 'm'
                }

                // Append the whole sequence — it contributes no visible width.
                sb.Append(text.Substring(seqStart, pos - seqStart));
            }
            else
            {
                sb.Append(text[pos]);
                visibleCount++;
                pos++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns the portion of <paramref name="text"/> starting after <paramref name="visibleOffset"/>
    /// visible characters have been skipped. ANSI escape sequences encountered during the skip
    /// are treated as zero width and do not consume any of the offset budget.
    /// </summary>
    /// <param name="text">The text to operate on. Must not be <see langword="null"/>.</param>
    /// <param name="visibleOffset">
    /// The number of visible characters to skip before the result begins. Pass 0 to return
    /// the full string.
    /// </param>
    /// <returns>
    /// The remainder of <paramref name="text"/> after skipping the requested number of visible
    /// characters, including any ANSI sequences that follow the skip point.
    /// </returns>
    public static string SubstringByVisibleOffset(string text, int visibleOffset)
    {
        if (visibleOffset <= 0)
        {
            return text;
        }

        int visibleCount = 0;
        int pos = 0;

        while (pos < text.Length && visibleCount < visibleOffset)
        {
            // Skip an ANSI escape sequence — it counts as zero visible width.
            if (text[pos] == '\x1b' && pos + 1 < text.Length && text[pos + 1] == '[')
            {
                pos += 2; // skip ESC [

                while (pos < text.Length && text[pos] != 'm')
                {
                    pos++;
                }

                if (pos < text.Length)
                {
                    pos++; // skip the trailing 'm'
                }
            }
            else
            {
                visibleCount++;
                pos++;
            }
        }

        // Skip any ANSI sequences sitting at the current position so the caller
        // receives the first visible character (or end of string), not a dangling
        // escape that belonged to the skipped region.
        while (pos < text.Length && text[pos] == '\x1b' && pos + 1 < text.Length && text[pos + 1] == '[')
        {
            pos += 2; // skip ESC [

            while (pos < text.Length && text[pos] != 'm')
            {
                pos++;
            }

            if (pos < text.Length)
            {
                pos++; // skip the trailing 'm'
            }
        }

        return text.Substring(pos);
    }
}
