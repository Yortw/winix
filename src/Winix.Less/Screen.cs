#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace Winix.Less;

/// <summary>
/// Terminal rendering component for the pager. Manages the cursor, status bar, and line display.
/// Hides the cursor on construction and shows it again on disposal.
/// </summary>
/// <remarks>
/// A single-row status bar is permanently reserved at the bottom of the terminal window.
/// The remaining <see cref="ViewHeight"/> rows are used for content.
/// </remarks>
internal sealed class Screen : IDisposable
{
    private readonly LessOptions _options;

    // Hide cursor on entry; restored in Dispose.
    private const string HideCursor = "\x1b[?25l";
    private const string ShowCursorSeq = "\x1b[?25h";
    private const string ReverseVideo = "\x1b[7m";
    private const string ResetVideo = "\x1b[0m";

    /// <summary>
    /// Initialises the screen, hides the cursor, and reads the initial terminal dimensions.
    /// </summary>
    /// <param name="options">The current pager options. Must not be <see langword="null"/>.</param>
    internal Screen(LessOptions options)
    {
        _options = options;
        Console.Write(HideCursor);
        RefreshDimensions();
    }

    /// <summary>Gets the current terminal height in rows.</summary>
    internal int Height { get; private set; }

    /// <summary>Gets the current terminal width in columns.</summary>
    internal int Width { get; private set; }

    /// <summary>
    /// Gets the number of rows available for content — <see cref="Height"/> minus one row
    /// reserved for the status bar.
    /// </summary>
    internal int ViewHeight => Height - 1;

    /// <summary>
    /// Re-reads <see cref="Console.WindowHeight"/> and <see cref="Console.WindowWidth"/>,
    /// falling back to 24 rows / 80 columns when the values are zero or unavailable
    /// (e.g. when output is redirected).
    /// </summary>
    internal void RefreshDimensions()
    {
        int h = 0;
        int w = 0;

        try
        {
            h = Console.WindowHeight;
            w = Console.WindowWidth;
        }
        catch (Exception)
        {
            // Console dimensions are unavailable (e.g. redirected output) — use safe defaults.
        }

        Height = h > 0 ? h : 24;
        Width = w > 0 ? w : 80;
    }

    /// <summary>
    /// Renders the visible page of content followed by the status bar.
    /// </summary>
    /// <param name="lines">All source lines in the document.</param>
    /// <param name="topLine">Zero-based index of the first source line to display.</param>
    /// <param name="leftColumn">Horizontal scroll offset in visible characters (only relevant when chopping).</param>
    /// <param name="sourceName">Display name of the source (shown in the status bar).</param>
    /// <param name="totalLines">Total number of source lines (for the position indicator).</param>
    /// <param name="searchPattern">Current search pattern, or <see langword="null"/> if none.</param>
    /// <param name="isFollowing">When <see langword="true"/>, the pager is in follow mode.</param>
    /// <param name="isAtEnd">When <see langword="true"/>, the viewport is at the last line.</param>
    internal void Render(
        IReadOnlyList<string> lines,
        int topLine,
        int leftColumn,
        string sourceName,
        int totalLines,
        string? searchPattern,
        bool isFollowing,
        bool isAtEnd)
    {
        // Calculate gutter width when line numbers are enabled.
        // Reserve 6 digits + 1 space = 7 columns for numbers up to 999 999.
        int gutterWidth = _options.ShowLineNumbers ? 6 : 0;
        int contentWidth = Width - gutterWidth - (_options.ShowLineNumbers ? 1 : 0);
        if (contentWidth < 1)
        {
            contentWidth = 1;
        }

        // Move to top-left of terminal before re-drawing to avoid flicker.
        Console.SetCursorPosition(0, 0);

        int rowsWritten = 0;

        for (int sourceIndex = topLine; sourceIndex < lines.Count && rowsWritten < ViewHeight; sourceIndex++)
        {
            string line = lines[sourceIndex];
            IReadOnlyList<string> displayRows;

            if (_options.ChopLongLines)
            {
                displayRows = new[] { LineWrapper.ChopLine(line, contentWidth, leftColumn) };
            }
            else
            {
                displayRows = LineWrapper.WrapLine(line, contentWidth);
            }

            bool firstRow = true;
            foreach (string row in displayRows)
            {
                if (rowsWritten >= ViewHeight)
                {
                    break;
                }

                string gutter = string.Empty;
                if (_options.ShowLineNumbers)
                {
                    // Only the first physical row of a wrapped source line gets the line number;
                    // continuation rows get a blank gutter.
                    int? lineNumber = firstRow ? (int?)(sourceIndex + 1) : null;
                    gutter = LineWrapper.FormatLineNumber(lineNumber, gutterWidth);
                }

                WritePaddedLine(gutter + row);
                rowsWritten++;
                firstRow = false;
            }
        }

        // Fill remaining content rows with "~" (vim/less convention for lines beyond the file end).
        while (rowsWritten < ViewHeight)
        {
            WritePaddedLine("~");
            rowsWritten++;
        }

        // Draw the status bar in reverse video on the last row.
        string statusText = BuildStatusBar(sourceName, topLine, totalLines, searchPattern, isFollowing, isAtEnd);
        string paddedStatus = PadToWidth(statusText);

        Console.Write(ReverseVideo);
        Console.Write(paddedStatus);
        Console.Write(ResetVideo);
    }

    /// <summary>
    /// Reads a single keypress from the terminal without echoing it.
    /// </summary>
    /// <returns>The <see cref="ConsoleKeyInfo"/> describing the key that was pressed.</returns>
    internal ConsoleKeyInfo ReadKey()
    {
        return Console.ReadKey(intercept: true);
    }

    /// <summary>
    /// Displays a prompt character at the bottom of the screen and reads a line of input
    /// until Enter or Escape is pressed.
    /// </summary>
    /// <param name="promptChar">The single-character prompt to display (e.g. '/' or '?').</param>
    /// <returns>
    /// The entered string (possibly empty) if Enter was pressed, or <see langword="null"/>
    /// if Escape was pressed.
    /// </returns>
    /// <remarks>
    /// The cursor is shown for the duration of prompt input and re-hidden before returning.
    /// Backspace is supported; other control characters are ignored.
    /// </remarks>
    internal string? ReadPrompt(char promptChar)
    {
        // Show cursor while user is typing.
        Console.Write(ShowCursorSeq);

        // Position at the start of the status bar row and clear it.
        Console.SetCursorPosition(0, Height - 1);
        Console.Write(new string(' ', Width));
        Console.SetCursorPosition(0, Height - 1);
        Console.Write(promptChar);

        var input = new StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Escape)
            {
                Console.Write(HideCursor);
                return null;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (input.Length > 0)
                {
                    input.Remove(input.Length - 1, 1);

                    // Move cursor back, erase the character, move back again.
                    int col = 1 + input.Length;
                    Console.SetCursorPosition(col, Height - 1);
                    Console.Write(' ');
                    Console.SetCursorPosition(col, Height - 1);
                }
            }
            else if (key.KeyChar >= ' ')
            {
                // Only printable characters are accepted.
                input.Append(key.KeyChar);
                Console.Write(key.KeyChar);
            }
        }

        Console.Write(HideCursor);
        return input.ToString();
    }

    /// <summary>
    /// Releases terminal resources: shows the cursor and optionally clears the screen.
    /// </summary>
    public void Dispose()
    {
        Console.Write(ShowCursorSeq);

        if (!_options.NoClearOnExit)
        {
            Console.Clear();
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes <paramref name="content"/> to stdout, padding with spaces to fill the full
    /// terminal width, then advances to the next row via <see cref="Console.WriteLine()"/>.
    /// This ensures previous content in the row is fully overwritten when the new content
    /// is shorter than what was rendered before.
    /// </summary>
    private void WritePaddedLine(string content)
    {
        Console.Write(PadToWidth(content));
        Console.WriteLine();
    }

    /// <summary>
    /// Returns <paramref name="text"/> padded with trailing spaces to exactly <see cref="Width"/>
    /// visible characters. Uses <see cref="AnsiText.VisibleLength"/> so that ANSI escape sequences
    /// in <paramref name="text"/> do not count toward the padding calculation.
    /// </summary>
    private string PadToWidth(string text)
    {
        int visible = AnsiText.VisibleLength(text);
        int padding = Width - visible;

        if (padding <= 0)
        {
            return text;
        }

        return text + new string(' ', padding);
    }

    /// <summary>
    /// Builds the status bar text for the current pager state.
    /// </summary>
    private static string BuildStatusBar(
        string sourceName,
        int topLine,
        int totalLines,
        string? searchPattern,
        bool isFollowing,
        bool isAtEnd)
    {
        if (isFollowing)
        {
            return " Waiting for data... (press any key to stop)";
        }

        if (isAtEnd && totalLines > 0)
        {
            return " (END)";
        }

        // Format: " file.txt  [/pattern  ]Lines 42-84/1200 (7%)"
        var sb = new StringBuilder();
        sb.Append(' ');
        sb.Append(sourceName);
        sb.Append("  ");

        if (!string.IsNullOrEmpty(searchPattern))
        {
            sb.Append('/');
            sb.Append(searchPattern);
            sb.Append("  ");
        }

        int bottomLine = topLine + 1; // 1-based display for first visible line
        int percentDone = totalLines > 0 ? (int)((topLine * 100.0) / totalLines) : 0;

        sb.Append("Lines ");
        sb.Append(bottomLine);
        sb.Append('/');
        sb.Append(totalLines);
        sb.Append(" (");
        sb.Append(percentDone);
        sb.Append("%)");

        return sb.ToString();
    }
}
