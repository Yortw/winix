#nullable enable

using System.Collections.Generic;

namespace Winix.Less;

/// <summary>
/// Provides methods for wrapping, chopping, and annotating lines of text for display
/// in a terminal pager. All width calculations are based on visible character counts,
/// so ANSI SGR escape sequences are treated as zero-width throughout.
/// </summary>
public static class LineWrapper
{
    /// <summary>
    /// Splits <paramref name="line"/> into display rows, each containing at most
    /// <paramref name="width"/> visible characters. ANSI escape sequences are preserved
    /// within each row and do not count toward the visible width.
    /// </summary>
    /// <param name="line">The source line to wrap. Must not be <see langword="null"/>.</param>
    /// <param name="width">The maximum number of visible characters per display row. Must be &gt; 0.</param>
    /// <returns>
    /// A list of rows. An empty <paramref name="line"/> always produces exactly one empty row.
    /// A line that fits within <paramref name="width"/> produces exactly one row equal to
    /// <paramref name="line"/>. Longer lines are split left-to-right at each <paramref name="width"/>
    /// boundary.
    /// </returns>
    public static IReadOnlyList<string> WrapLine(string line, int width)
    {
        int visibleLen = AnsiText.VisibleLength(line);

        // An empty line must still occupy one display row so the cursor advances.
        if (visibleLen == 0)
        {
            return new[] { string.Empty };
        }

        if (visibleLen <= width)
        {
            return new[] { line };
        }

        var rows = new List<string>();
        int offset = 0;

        while (offset < visibleLen)
        {
            string shifted = AnsiText.SubstringByVisibleOffset(line, offset);
            string row = AnsiText.TruncateToWidth(shifted, width);
            rows.Add(row);
            offset += width;
        }

        return rows;
    }

    /// <summary>
    /// Returns the portion of <paramref name="line"/> visible in a terminal window that starts
    /// at <paramref name="leftColumn"/> and is <paramref name="width"/> columns wide.
    /// </summary>
    /// <param name="line">The source line. Must not be <see langword="null"/>.</param>
    /// <param name="width">The number of visible columns in the viewport. Must be &gt; 0.</param>
    /// <param name="leftColumn">
    /// The zero-based visible-character index of the leftmost column in the viewport. Pass 0
    /// to start from the beginning of the line.
    /// </param>
    /// <returns>
    /// The slice of <paramref name="line"/> that falls within the viewport, with at most
    /// <paramref name="width"/> visible characters, including any ANSI escape sequences that
    /// are present within that region.
    /// </returns>
    public static string ChopLine(string line, int width, int leftColumn)
    {
        string shifted = AnsiText.SubstringByVisibleOffset(line, leftColumn);
        return AnsiText.TruncateToWidth(shifted, width);
    }

    /// <summary>
    /// Formats a line-number gutter cell for display. The result is always
    /// <paramref name="gutterWidth"/> + 1 characters wide (the number right-aligned in
    /// <paramref name="gutterWidth"/> columns, followed by a single trailing space).
    /// </summary>
    /// <param name="lineNumber">
    /// The 1-based line number to display, or <see langword="null"/> for a soft-wrap
    /// continuation row, which renders as an all-spaces blank gutter.
    /// </param>
    /// <param name="gutterWidth">
    /// The number of columns reserved for the numeric portion of the gutter. The total
    /// returned string width is <paramref name="gutterWidth"/> + 1.
    /// </param>
    /// <returns>A fixed-width gutter string ready to prepend to a display row.</returns>
    public static string FormatLineNumber(int? lineNumber, int gutterWidth)
    {
        if (lineNumber == null)
        {
            return new string(' ', gutterWidth + 1);
        }

        return lineNumber.Value.ToString().PadLeft(gutterWidth) + " ";
    }

    /// <summary>
    /// Calculates the total number of display rows that <paramref name="lines"/> would occupy
    /// when rendered in a terminal <paramref name="width"/> columns wide, using soft wrapping.
    /// Each source line occupies at least one row; lines longer than <paramref name="width"/>
    /// occupy <c>ceil(visibleLength / width)</c> rows.
    /// </summary>
    /// <param name="lines">The source lines to measure. Must not be <see langword="null"/>.</param>
    /// <param name="width">The terminal width in visible columns. Must be &gt; 0.</param>
    /// <returns>The total display row count across all <paramref name="lines"/>.</returns>
    public static int CalculateDisplayRows(IReadOnlyList<string> lines, int width)
    {
        int total = 0;

        foreach (string line in lines)
        {
            int visibleLen = AnsiText.VisibleLength(line);

            if (visibleLen == 0)
            {
                total += 1;
            }
            else
            {
                // Ceiling division without floating point.
                total += (visibleLen + width - 1) / width;
            }
        }

        return total;
    }
}
