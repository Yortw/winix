#nullable enable

using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Winix.Man;

/// <summary>
/// A minimal interactive pager for displaying ANSI-formatted content in a terminal.
/// Used as a fallback when no external pager (less, $PAGER) is available.
/// </summary>
/// <remarks>
/// Supports scrolling, searching, and a status bar. Search operates on plain text
/// (ANSI codes stripped), but output lines are shown with their original ANSI formatting.
/// </remarks>
internal sealed class BuiltInPager
{
    // ANSI codes used in the status bar.
    private const string AnsiReverseVideo = "\x1b[7m";
    private const string AnsiReset = "\x1b[0m";
    private const string AnsiHideCursor = "\x1b[?25l";
    private const string AnsiShowCursor = "\x1b[?25h";

    /// <summary>
    /// Displays <paramref name="content"/> interactively, allowing the user to scroll
    /// and search. Blocks until the user presses 'q' or 'Q' to quit.
    /// </summary>
    /// <param name="content">The ANSI-formatted text to display.</param>
    public void Display(string content)
    {
        var lines = content.Split('\n');
        var topLine = 0;
        var searchTerm = "";
        var searchMatchLine = -1;

        int height = GetTerminalHeight();
        int width = GetTerminalWidth();
        // Reserve one line at the bottom for the status bar.
        int viewLines = height - 1;

        Console.Write(AnsiHideCursor);
        try
        {
            bool redraw = true;
            while (true)
            {
                height = GetTerminalHeight();
                width = GetTerminalWidth();
                viewLines = height - 1;

                if (redraw)
                {
                    DrawPage(lines, topLine, viewLines, width, searchTerm);
                    DrawStatusBar(topLine, lines.Length, viewLines, width, searchTerm);
                    redraw = false;
                }

                var key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.Q)
                {
                    break;
                }
                else if (key.Key == ConsoleKey.J || key.Key == ConsoleKey.DownArrow)
                {
                    if (topLine < lines.Length - viewLines)
                    {
                        topLine++;
                        redraw = true;
                    }
                }
                else if (key.Key == ConsoleKey.K || key.Key == ConsoleKey.UpArrow)
                {
                    if (topLine > 0)
                    {
                        topLine--;
                        redraw = true;
                    }
                }
                else if (key.Key == ConsoleKey.Spacebar || key.Key == ConsoleKey.PageDown)
                {
                    int next = topLine + viewLines;
                    topLine = Math.Min(next, Math.Max(0, lines.Length - viewLines));
                    redraw = true;
                }
                else if (key.Key == ConsoleKey.PageUp)
                {
                    topLine = Math.Max(0, topLine - viewLines);
                    redraw = true;
                }
                else if (key.Key == ConsoleKey.Home)
                {
                    topLine = 0;
                    redraw = true;
                }
                else if (key.Key == ConsoleKey.End)
                {
                    topLine = Math.Max(0, lines.Length - viewLines);
                    redraw = true;
                }
                else if (key.KeyChar == '/')
                {
                    searchTerm = ReadSearchTerm(width);
                    if (searchTerm.Length > 0)
                    {
                        searchMatchLine = FindNext(lines, searchTerm, topLine + 1);
                        if (searchMatchLine >= 0)
                        {
                            topLine = searchMatchLine;
                        }
                    }
                    redraw = true;
                }
                else if (key.KeyChar == 'n')
                {
                    if (searchTerm.Length > 0)
                    {
                        // Search forward from one line after current position; wrap if needed.
                        int start = topLine + 1;
                        searchMatchLine = FindNext(lines, searchTerm, start);
                        if (searchMatchLine < 0)
                        {
                            // Wrap around from beginning.
                            searchMatchLine = FindNext(lines, searchTerm, 0);
                        }

                        if (searchMatchLine >= 0)
                        {
                            topLine = searchMatchLine;
                        }

                        redraw = true;
                    }
                }
                else if (key.KeyChar == 'N')
                {
                    if (searchTerm.Length > 0)
                    {
                        // Search backward from one line before current position; wrap if needed.
                        int start = topLine - 1;
                        searchMatchLine = FindPrevious(lines, searchTerm, start);
                        if (searchMatchLine < 0)
                        {
                            // Wrap around from end.
                            searchMatchLine = FindPrevious(lines, searchTerm, lines.Length - 1);
                        }

                        if (searchMatchLine >= 0)
                        {
                            topLine = searchMatchLine;
                        }

                        redraw = true;
                    }
                }
            }
        }
        finally
        {
            // Always restore cursor, even on exception, to avoid leaving the terminal broken.
            Console.Write(AnsiShowCursor);
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Draws the visible portion of the content to the terminal.
    /// </summary>
    /// <param name="lines">All content lines.</param>
    /// <param name="topLine">Index of the first visible line.</param>
    /// <param name="viewLines">Number of lines available for content.</param>
    /// <param name="width">Terminal width in columns.</param>
    /// <param name="searchTerm">Current search term; matching lines will be highlighted.</param>
    private static void DrawPage(string[] lines, int topLine, int viewLines, int width, string searchTerm)
    {
        Console.SetCursorPosition(0, 0);

        int end = Math.Min(topLine + viewLines, lines.Length);
        for (int i = topLine; i < end; i++)
        {
            string line = lines[i];
            int visible = VisibleLength(line);
            if (visible > width)
            {
                // Truncate to terminal width — use Substring, not range syntax.
                line = TruncateToVisibleWidth(line, width);
            }

            // Pad short lines so they fully overwrite any previous content.
            int paddingNeeded = width - VisibleLength(line);
            if (paddingNeeded > 0)
            {
                Console.Write(line + new string(' ', paddingNeeded));
            }
            else
            {
                Console.Write(line);
            }

            Console.WriteLine();
        }

        // Blank any remaining lines below the content.
        for (int i = end; i < topLine + viewLines; i++)
        {
            Console.WriteLine(new string(' ', width));
        }
    }

    /// <summary>
    /// Draws the status bar at the bottom of the terminal in reverse video.
    /// Shows position as a line range and percentage through the content.
    /// </summary>
    /// <param name="topLine">Index of the first visible line.</param>
    /// <param name="totalLines">Total number of lines in the content.</param>
    /// <param name="viewLines">Number of lines available for content display.</param>
    /// <param name="width">Terminal width in columns.</param>
    /// <param name="searchTerm">Current search term (shown in status bar if non-empty).</param>
    private static void DrawStatusBar(int topLine, int totalLines, int viewLines, int width, string searchTerm)
    {
        int bottomLine = Math.Min(topLine + viewLines, totalLines);
        int pct = totalLines > 0 ? (bottomLine * 100 / totalLines) : 100;

        string searchInfo = searchTerm.Length > 0 ? $"  /{searchTerm}" : "";
        string position = $" Lines {topLine + 1}-{bottomLine}/{totalLines} ({pct}%){searchInfo} ";

        // Truncate if status is wider than the terminal.
        if (position.Length > width)
        {
            position = position.Substring(0, width);
        }

        // Pad to fill the full width in reverse video.
        string bar = position + new string(' ', width - position.Length);

        Console.SetCursorPosition(0, viewLines);
        Console.Write(AnsiReverseVideo + bar + AnsiReset);
    }

    /// <summary>
    /// Reads a search term from the status bar area, displaying a '/' prompt.
    /// Returns the entered term, or an empty string if the user cancelled (Escape).
    /// </summary>
    /// <param name="width">Terminal width in columns, used to size the prompt area.</param>
    /// <returns>The search string entered by the user.</returns>
    private static string ReadSearchTerm(int width)
    {
        Console.Write(AnsiShowCursor);
        try
        {
            // Move to the last row and display the search prompt.
            int promptRow = GetTerminalHeight() - 1;
            Console.SetCursorPosition(0, promptRow);
            Console.Write(new string(' ', width));
            Console.SetCursorPosition(0, promptRow);
            Console.Write('/');

            var sb = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    break;
                }
                else if (key.Key == ConsoleKey.Escape)
                {
                    return "";
                }
                else if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0)
                    {
                        sb.Remove(sb.Length - 1, 1);
                        // Erase last character from display.
                        int col = 1 + sb.Length;
                        Console.SetCursorPosition(col, promptRow);
                        Console.Write(' ');
                        Console.SetCursorPosition(col, promptRow);
                    }
                }
                else if (key.KeyChar != '\0')
                {
                    sb.Append(key.KeyChar);
                    Console.Write(key.KeyChar);
                }
            }

            return sb.ToString();
        }
        finally
        {
            Console.Write(AnsiHideCursor);
        }
    }

    /// <summary>
    /// Searches forward from <paramref name="startLine"/> for the first line containing
    /// <paramref name="term"/> (case-insensitive, ANSI codes stripped).
    /// </summary>
    /// <param name="lines">All content lines.</param>
    /// <param name="term">The search term.</param>
    /// <param name="startLine">Index to start searching from (inclusive).</param>
    /// <returns>Index of the first matching line, or -1 if not found.</returns>
    private static int FindNext(string[] lines, string term, int startLine)
    {
        for (int i = startLine; i < lines.Length; i++)
        {
            if (StripAnsiForSearch(lines[i]).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Searches backward from <paramref name="startLine"/> for the first line containing
    /// <paramref name="term"/> (case-insensitive, ANSI codes stripped).
    /// </summary>
    /// <param name="lines">All content lines.</param>
    /// <param name="term">The search term.</param>
    /// <param name="startLine">Index to start searching from (inclusive), searching toward 0.</param>
    /// <returns>Index of the first matching line (closest to startLine), or -1 if not found.</returns>
    private static int FindPrevious(string[] lines, string term, int startLine)
    {
        for (int i = startLine; i >= 0; i--)
        {
            if (StripAnsiForSearch(lines[i]).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Truncates <paramref name="line"/> so that its visible (non-ANSI) character count
    /// does not exceed <paramref name="maxWidth"/>, preserving any ANSI escape sequences
    /// that fall within the visible range.
    /// </summary>
    /// <param name="line">The ANSI-formatted line to truncate.</param>
    /// <param name="maxWidth">Maximum visible width in columns.</param>
    /// <returns>The truncated line string.</returns>
    private static string TruncateToVisibleWidth(string line, int maxWidth)
    {
        var sb = new StringBuilder();
        int visibleCount = 0;
        int pos = 0;

        while (pos < line.Length && visibleCount < maxWidth)
        {
            if (line[pos] == '\x1b')
            {
                // Copy the ANSI escape sequence verbatim; it has no visible width.
                int escEnd = pos + 1;
                if (escEnd < line.Length && line[escEnd] == '[')
                {
                    escEnd++;
                    while (escEnd < line.Length && line[escEnd] != 'm')
                    {
                        escEnd++;
                    }

                    if (escEnd < line.Length)
                    {
                        escEnd++; // include the 'm'
                    }
                }

                sb.Append(line.Substring(pos, escEnd - pos));
                pos = escEnd;
            }
            else
            {
                sb.Append(line[pos]);
                pos++;
                visibleCount++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Strips ANSI escape sequences from <paramref name="line"/> so that text-only
    /// comparisons (e.g. search) operate on visible characters only.
    /// </summary>
    /// <param name="line">The line that may contain ANSI codes.</param>
    /// <returns>The plain text content of the line.</returns>
    internal static string StripAnsiForSearch(string line)
    {
        // Matches ESC [ ... m sequences.
        return Regex.Replace(line, @"\x1b\[[^m]*m", "");
    }

    /// <summary>
    /// Returns the number of visible (non-ANSI) characters in <paramref name="line"/>.
    /// </summary>
    /// <param name="line">The line that may contain ANSI escape codes.</param>
    /// <returns>The visible column width of the line.</returns>
    internal static int VisibleLength(string line)
    {
        return StripAnsiForSearch(line).Length;
    }

    /// <summary>
    /// Returns the terminal height in lines, defaulting to 24 if the value cannot be determined.
    /// </summary>
    private static int GetTerminalHeight()
    {
        try
        {
            int h = Console.WindowHeight;
            return h > 0 ? h : 24;
        }
        catch
        {
            return 24;
        }
    }

    /// <summary>
    /// Returns the terminal width in columns, defaulting to 80 if the value cannot be determined.
    /// </summary>
    private static int GetTerminalWidth()
    {
        try
        {
            int w = Console.WindowWidth;
            return w > 0 ? w : 80;
        }
        catch
        {
            return 80;
        }
    }
}
