using System.Globalization;
using System.Text;
using Yort.ShellKit;

namespace Winix.Peep;

/// <summary>
/// Manages the alternate screen buffer and renders peep's header, output, and help overlay.
/// All formatting methods take a <see cref="TextWriter"/> so they can be tested with
/// <see cref="StringWriter"/> without a real terminal.
/// </summary>
public static class ScreenRenderer
{
    // Alternate screen buffer
    private const string EnterAlternateBufferSeq = "\x1b[?1049h";
    private const string ExitAlternateBufferSeq = "\x1b[?1049l";

    // Cursor and screen control
    private const string CursorHomeSeq = "\x1b[H";
    private const string ClearScreenSeq = "\x1b[2J";
    private const string HideCursorSeq = "\x1b[?25l";
    private const string ShowCursorSeq = "\x1b[?25h";

    /// <summary>
    /// Enters the alternate screen buffer. This preserves the user's terminal history
    /// and provides a clean canvas for peep's output.
    /// </summary>
    public static void EnterAlternateBuffer(TextWriter writer)
    {
        writer.Write(EnterAlternateBufferSeq);
        writer.Write(HideCursorSeq);
        writer.Flush();
    }

    /// <summary>
    /// Exits the alternate screen buffer and shows the cursor, restoring the terminal
    /// to its state before peep started.
    /// </summary>
    public static void ExitAlternateBuffer(TextWriter writer)
    {
        writer.Write(ShowCursorSeq);
        writer.Write(ExitAlternateBufferSeq);
        writer.Flush();
    }

    /// <summary>
    /// Clears the alternate screen and moves the cursor to the top-left corner.
    /// </summary>
    public static void ClearScreen(TextWriter writer)
    {
        writer.Write(CursorHomeSeq);
        writer.Write(ClearScreenSeq);
    }

    /// <summary>
    /// Dark navy blue background for highlighting changed lines in diff mode.
    /// Visible on dark terminals without being garish. 256-colour palette index 17.
    /// </summary>
    private const string DiffHighlightOn = "\x1b[48;5;17m";
    private const string DiffHighlightOff = "\x1b[49m";

    /// <summary>
    /// Renders the full screen: header, optional watch line, and command output.
    /// </summary>
    /// <param name="writer">Output writer (typically <see cref="Console.Out"/>).</param>
    /// <param name="header">Formatted header string (from <see cref="FormatHeader"/>).</param>
    /// <param name="watchLine">Formatted watch line (from <see cref="FormatWatchLine"/>) or null.</param>
    /// <param name="output">The command's captured output text.</param>
    /// <param name="terminalHeight">Current terminal height in rows.</param>
    /// <param name="scrollOffset">Number of output lines to skip from the top (scroll position).</param>
    /// <param name="showHeader">Whether to show the header lines.</param>
    /// <param name="previousOutput">Previous command output for diff highlighting, or null.</param>
    /// <param name="diffEnabled">Whether diff highlighting is active.</param>
    public static void Render(
        TextWriter writer,
        string? header,
        string? watchLine,
        string output,
        int terminalHeight,
        int scrollOffset,
        bool showHeader,
        string? previousOutput = null,
        bool diffEnabled = false)
    {
        ClearScreen(writer);

        int linesUsed = 0;

        if (showHeader && header is not null)
        {
            writer.WriteLine(header);
            linesUsed++;

            if (watchLine is not null)
            {
                writer.WriteLine(watchLine);
                linesUsed++;
            }
        }

        int availableHeight = GetAvailableHeight(terminalHeight, watchLine is not null, showHeader);

        if (diffEnabled && previousOutput is not null)
        {
            RenderOutputWithDiff(writer, output, previousOutput, availableHeight, scrollOffset);
        }
        else
        {
            RenderOutput(writer, output, availableHeight, scrollOffset);
        }

        writer.Flush();
    }

    /// <summary>
    /// Renders the command output, truncated to the available height and shifted
    /// by the scroll offset.
    /// </summary>
    /// <param name="writer">Output writer.</param>
    /// <param name="output">Full captured command output.</param>
    /// <param name="availableHeight">Number of terminal rows available for output.</param>
    /// <param name="scrollOffset">Number of lines to skip from the top.</param>
    public static void RenderOutput(TextWriter writer, string output, int availableHeight, int scrollOffset)
    {
        if (availableHeight <= 0 || string.IsNullOrEmpty(output))
        {
            return;
        }

        string[] lines = output.Split('\n');

        // Apply scroll offset
        int startLine = Math.Min(scrollOffset, Math.Max(0, lines.Length - 1));
        int endLine = Math.Min(startLine + availableHeight, lines.Length);

        for (int i = startLine; i < endLine; i++)
        {
            // Don't add a trailing newline after the last line to avoid unnecessary blank lines
            if (i < endLine - 1)
            {
                writer.WriteLine(lines[i]);
            }
            else
            {
                writer.Write(lines[i]);
            }
        }
    }

    /// <summary>
    /// Renders command output with diff highlighting. Lines that differ from the previous
    /// output (or are new) are rendered with a dark grey background.
    /// Comparison uses ANSI-stripped text so colour changes don't trigger false diffs.
    /// </summary>
    public static void RenderOutputWithDiff(
        TextWriter writer, string output, string previousOutput,
        int availableHeight, int scrollOffset)
    {
        if (availableHeight <= 0 || string.IsNullOrEmpty(output))
        {
            return;
        }

        string[] currentLines = output.Split('\n');
        string[] previousLines = previousOutput.Split('\n');
        bool[] changed = ComputeChangedLines(currentLines, previousLines);

        int startLine = Math.Min(scrollOffset, Math.Max(0, currentLines.Length - 1));
        int endLine = Math.Min(startLine + availableHeight, currentLines.Length);

        for (int i = startLine; i < endLine; i++)
        {
            bool isChanged = i < changed.Length && changed[i];
            bool isLast = (i == endLine - 1);

            if (isChanged)
            {
                writer.Write(DiffHighlightOn);
            }

            if (isLast)
            {
                writer.Write(currentLines[i]);
            }
            else
            {
                writer.WriteLine(currentLines[i]);
            }

            if (isChanged)
            {
                writer.Write(DiffHighlightOff);
            }
        }
    }

    /// <summary>
    /// Compares current and previous output lines (ANSI-stripped) and returns a boolean array
    /// indicating which current lines have changed or are new.
    /// </summary>
    internal static bool[] ComputeChangedLines(string[] currentLines, string[] previousLines)
    {
        bool[] changed = new bool[currentLines.Length];

        for (int i = 0; i < currentLines.Length; i++)
        {
            if (i >= previousLines.Length)
            {
                // New line (current output is longer than previous)
                changed[i] = true;
            }
            else
            {
                string currentStripped = Formatting.StripAnsi(currentLines[i]);
                string previousStripped = Formatting.StripAnsi(previousLines[i]);
                changed[i] = !string.Equals(currentStripped, previousStripped, StringComparison.Ordinal);
            }
        }

        return changed;
    }

    /// <summary>
    /// Renders the help overlay centred on the screen.
    /// </summary>
    public static void RenderHelpOverlay(TextWriter writer, int width, int height)
    {
        string[] helpLines = new[]
        {
            "",
            "  Keyboard shortcuts:",
            "",
            "  q / Ctrl+C       Quit",
            "  Space            Pause/unpause",
            "  r / Enter        Force re-run",
            "  Up/Down          Scroll (when paused)",
            "  PgUp/PgDn        Scroll page (when paused)",
            "  Left/Right       Time travel (older/newer)",
            "  t                History overlay",
            "  d                Toggle diff highlighting",
            "  ? / Esc          Toggle this help",
            "",
        };

        ClearScreen(writer);

        // Centre vertically
        int topPadding = Math.Max(0, (height - helpLines.Length) / 2);
        for (int i = 0; i < topPadding; i++)
        {
            writer.WriteLine();
        }

        foreach (string line in helpLines)
        {
            writer.WriteLine(line);
        }

        writer.Flush();
    }

    /// <summary>
    /// Renders the history overlay showing a scrollable list of snapshots.
    /// Newest snapshots appear at the top. The selected entry is marked with &gt;.
    /// </summary>
    /// <param name="writer">Output writer (typically <see cref="Console.Out"/>).</param>
    /// <param name="history">The snapshot history to display.</param>
    /// <param name="selectedIndex">Zero-based index of the currently selected snapshot (0 = oldest).</param>
    /// <param name="width">Terminal width in columns.</param>
    /// <param name="height">Terminal height in rows.</param>
    public static void RenderHistoryOverlay(
        TextWriter writer, SnapshotHistory history, int selectedIndex,
        int width, int height)
    {
        selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, history.Count - 1));

        ClearScreen(writer);

        // Title bar
        const string title = " History ";
        string titleBar = title.PadLeft((width + title.Length) / 2).PadRight(width);
        writer.WriteLine(titleBar);

        if (history.Count == 0)
        {
            writer.WriteLine();
            writer.Write("  No history yet");
            writer.WriteLine();
            writer.WriteLine();
            writer.Write("  Up/Dn navigate  Enter select  t/Esc close");
            writer.Flush();
            return;
        }

        // Reserve rows: 1 title + 1 hint line at bottom
        const int reservedRows = 2;
        int listHeight = Math.Max(0, height - reservedRows);

        // Build the display list newest-first (reverses the oldest-first internal order).
        int count = history.Count;

        // Scroll so that the selected entry stays visible.
        // selectedIndex is oldest-first; in the display it maps to display index (count - 1 - selectedIndex).
        int displaySelected = count > 0 ? count - 1 - selectedIndex : 0;
        int scrollOffset = 0;
        if (listHeight > 0 && displaySelected >= listHeight)
        {
            scrollOffset = displaySelected - listHeight + 1;
        }

        int rendered = 0;
        for (int displayIdx = scrollOffset; displayIdx < count && rendered < listHeight; displayIdx++, rendered++)
        {
            // Convert display index (newest-first) to storage index (oldest-first).
            int storageIdx = count - 1 - displayIdx;
            Snapshot snapshot = history[storageIdx];

            bool isSelected = (storageIdx == selectedIndex);
            string marker = isSelected ? ">" : " ";

            string timestamp = snapshot.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0} #{1,-4} {2}  exit:{3}  +{4} -{5}",
                marker,
                snapshot.RunNumber,
                timestamp,
                snapshot.Result.ExitCode,
                snapshot.LinesAdded,
                snapshot.LinesRemoved);

            writer.WriteLine(line);
        }

        // Fill remaining rows so the hint line is always at the bottom.
        for (int i = rendered; i < listHeight; i++)
        {
            writer.WriteLine();
        }

        // Key hints at bottom.
        writer.Write("  Up/Dn navigate  Enter select  t/Esc close");
        writer.Flush();
    }

    /// <summary>
    /// Formats the first header line showing interval, command, timestamp, exit code, and run count.
    /// </summary>
    /// <param name="intervalSeconds">The configured interval in seconds.</param>
    /// <param name="command">The command string being executed.</param>
    /// <param name="timestamp">Timestamp of the last run.</param>
    /// <param name="exitCode">Exit code of the last run, or null if no run yet.</param>
    /// <param name="runCount">Total number of runs so far.</param>
    /// <param name="isPaused">Whether the display is currently paused.</param>
    /// <param name="useColor">Whether to apply ANSI colour to the exit code.</param>
    /// <param name="isDiffEnabled">Whether diff highlighting is active.</param>
    /// <param name="isTimeMachine">Whether the user is browsing historical snapshots.</param>
    /// <param name="timeMachinePosition">1-based position within the history (cursor index + 1). Only used when <paramref name="isTimeMachine"/> is true.</param>
    /// <param name="timeMachineTotal">Total number of snapshots in the history. Only used when <paramref name="isTimeMachine"/> is true.</param>
    /// <returns>Formatted header line string.</returns>
    public static string FormatHeader(
        double intervalSeconds,
        string command,
        DateTime timestamp,
        int? exitCode,
        int runCount,
        bool isPaused,
        bool useColor,
        bool isDiffEnabled = false,
        bool isTimeMachine = false,
        int timeMachinePosition = 0,
        int timeMachineTotal = 0)
    {
        var sb = new StringBuilder();

        sb.AppendFormat(CultureInfo.InvariantCulture, "Every {0:F1}s: {1}", intervalSeconds, command);

        // Right-align: timestamp, exit code, run count
        sb.Append("  ");
        sb.Append(timestamp.ToString("ddd MMM dd HH:mm:ss", CultureInfo.InvariantCulture));

        if (exitCode.HasValue)
        {
            string exitColor = exitCode.Value == 0
                ? AnsiColor.Green(useColor)
                : AnsiColor.Red(useColor);
            string reset = AnsiColor.Reset(useColor);

            sb.AppendFormat(CultureInfo.InvariantCulture,
                " [{0}exit {1}{2}]", exitColor, exitCode.Value, reset);
        }

        sb.AppendFormat(CultureInfo.InvariantCulture, " [run #{0}]", runCount);

        if (isPaused)
        {
            sb.Append(" [PAUSED]");
        }

        if (isDiffEnabled)
        {
            sb.Append(" [DIFF]");
        }

        if (isTimeMachine)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture,
                " [TIME {0}/{1}]", timeMachinePosition, timeMachineTotal);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats the second header line showing watched file patterns.
    /// Returns null if no patterns are being watched.
    /// </summary>
    /// <param name="patterns">The file glob patterns being watched.</param>
    /// <param name="useColor">Whether to apply ANSI styling.</param>
    /// <returns>Formatted watch line, or null if <paramref name="patterns"/> is empty.</returns>
    public static string? FormatWatchLine(string[] patterns, bool useColor)
    {
        if (patterns.Length == 0)
        {
            return null;
        }

        string dim = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);

        return $"{dim}Watching:{reset} {string.Join(", ", patterns)}";
    }

    /// <summary>
    /// Calculates how many terminal rows are available for command output after
    /// subtracting header lines.
    /// </summary>
    /// <param name="terminalHeight">Total terminal height in rows.</param>
    /// <param name="hasWatchLine">Whether the watch patterns line is shown.</param>
    /// <param name="showHeader">Whether the header is shown at all.</param>
    /// <returns>Number of rows available for output. Minimum 0.</returns>
    public static int GetAvailableHeight(int terminalHeight, bool hasWatchLine, bool showHeader)
    {
        if (!showHeader)
        {
            return Math.Max(0, terminalHeight);
        }

        int headerLines = 1; // main header line
        if (hasWatchLine)
        {
            headerLines++;
        }

        return Math.Max(0, terminalHeight - headerLines);
    }
}
