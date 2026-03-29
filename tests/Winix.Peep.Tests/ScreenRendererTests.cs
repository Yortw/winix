using Xunit;
using Winix.Peep;

namespace Winix.Peep.Tests;

public class FormatHeaderTests
{
    [Fact]
    public void FormatHeader_BasicFormat()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet build",
            timestamp: new DateTime(2026, 3, 29, 14, 32, 1),
            exitCode: 0,
            runCount: 3,
            isPaused: false,
            useColor: false);

        Assert.Contains("Every 2.0s:", header);
        Assert.Contains("dotnet build", header);
        Assert.Contains("exit 0", header);
        Assert.Contains("[run #3]", header);
    }

    [Fact]
    public void FormatHeader_WithPauseIndicator()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet build",
            timestamp: new DateTime(2026, 3, 29, 14, 32, 1),
            exitCode: 0,
            runCount: 1,
            isPaused: true,
            useColor: false);

        Assert.Contains("[PAUSED]", header);
    }

    [Fact]
    public void FormatHeader_WithoutPauseIndicator()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet build",
            timestamp: new DateTime(2026, 3, 29, 14, 32, 1),
            exitCode: 0,
            runCount: 1,
            isPaused: false,
            useColor: false);

        Assert.DoesNotContain("[PAUSED]", header);
    }

    [Fact]
    public void FormatHeader_ExitCodeZero_GreenWithColor()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet build",
            timestamp: new DateTime(2026, 3, 29, 14, 32, 1),
            exitCode: 0,
            runCount: 1,
            isPaused: false,
            useColor: true);

        // Green ANSI: \x1b[32m
        Assert.Contains("\x1b[32m", header);
    }

    [Fact]
    public void FormatHeader_ExitCodeNonZero_RedWithColor()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet build",
            timestamp: new DateTime(2026, 3, 29, 14, 32, 1),
            exitCode: 1,
            runCount: 1,
            isPaused: false,
            useColor: true);

        // Red ANSI: \x1b[31m
        Assert.Contains("\x1b[31m", header);
    }

    [Fact]
    public void FormatHeader_NullExitCode_NoExitSection()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet build",
            timestamp: new DateTime(2026, 3, 29, 14, 32, 1),
            exitCode: null,
            runCount: 0,
            isPaused: false,
            useColor: false);

        Assert.DoesNotContain("exit", header);
    }
}

public class FormatWatchLineTests
{
    [Fact]
    public void FormatWatchLine_WithPatterns_ShowsPatterns()
    {
        string? line = ScreenRenderer.FormatWatchLine(
            new[] { "src/**/*.cs", "tests/**/*.cs" }, useColor: false);

        Assert.NotNull(line);
        Assert.Contains("Watching:", line);
        Assert.Contains("src/**/*.cs", line);
        Assert.Contains("tests/**/*.cs", line);
    }

    [Fact]
    public void FormatWatchLine_EmptyPatterns_ReturnsNull()
    {
        string? line = ScreenRenderer.FormatWatchLine(Array.Empty<string>(), useColor: false);

        Assert.Null(line);
    }
}

public class GetAvailableHeightTests
{
    [Fact]
    public void GetAvailableHeight_WithHeaderNoWatch()
    {
        int height = ScreenRenderer.GetAvailableHeight(
            terminalHeight: 24, hasWatchLine: false, showHeader: true);

        Assert.Equal(23, height); // 24 - 1 header line
    }

    [Fact]
    public void GetAvailableHeight_WithHeaderAndWatch()
    {
        int height = ScreenRenderer.GetAvailableHeight(
            terminalHeight: 24, hasWatchLine: true, showHeader: true);

        Assert.Equal(22, height); // 24 - 2 header lines
    }

    [Fact]
    public void GetAvailableHeight_NoHeader()
    {
        int height = ScreenRenderer.GetAvailableHeight(
            terminalHeight: 24, hasWatchLine: true, showHeader: false);

        Assert.Equal(24, height); // No header subtracted
    }

    [Fact]
    public void GetAvailableHeight_VerySmallTerminal_DoesNotGoNegative()
    {
        int height = ScreenRenderer.GetAvailableHeight(
            terminalHeight: 1, hasWatchLine: true, showHeader: true);

        Assert.Equal(0, height);
    }

    [Fact]
    public void GetAvailableHeight_ZeroHeight()
    {
        int height = ScreenRenderer.GetAvailableHeight(
            terminalHeight: 0, hasWatchLine: false, showHeader: true);

        Assert.Equal(0, height);
    }
}

public class RenderOutputTests
{
    [Fact]
    public void RenderOutput_TruncatesToAvailableHeight()
    {
        var writer = new StringWriter();
        string output = "line1\nline2\nline3\nline4\nline5";

        ScreenRenderer.RenderOutput(writer, output, availableHeight: 3, scrollOffset: 0);

        string rendered = writer.ToString();
        Assert.Contains("line1", rendered);
        Assert.Contains("line2", rendered);
        Assert.Contains("line3", rendered);
        Assert.DoesNotContain("line4", rendered);
        Assert.DoesNotContain("line5", rendered);
    }

    [Fact]
    public void RenderOutput_RespectsScrollOffset()
    {
        var writer = new StringWriter();
        string output = "line1\nline2\nline3\nline4\nline5";

        ScreenRenderer.RenderOutput(writer, output, availableHeight: 3, scrollOffset: 2);

        string rendered = writer.ToString();
        Assert.DoesNotContain("line1", rendered);
        Assert.DoesNotContain("line2", rendered);
        Assert.Contains("line3", rendered);
        Assert.Contains("line4", rendered);
        Assert.Contains("line5", rendered);
    }

    [Fact]
    public void RenderOutput_EmptyOutput_WritesNothing()
    {
        var writer = new StringWriter();

        ScreenRenderer.RenderOutput(writer, "", availableHeight: 10, scrollOffset: 0);

        Assert.Equal("", writer.ToString());
    }

    [Fact]
    public void RenderOutput_ZeroAvailableHeight_WritesNothing()
    {
        var writer = new StringWriter();

        ScreenRenderer.RenderOutput(writer, "some output", availableHeight: 0, scrollOffset: 0);

        Assert.Equal("", writer.ToString());
    }

    [Fact]
    public void RenderOutput_SingleLineAvailableHeight()
    {
        var writer = new StringWriter();
        string output = "line1\nline2\nline3";

        ScreenRenderer.RenderOutput(writer, output, availableHeight: 1, scrollOffset: 0);

        string rendered = writer.ToString();
        Assert.Contains("line1", rendered);
        Assert.DoesNotContain("line2", rendered);
    }

    [Fact]
    public void RenderOutput_ScrollOffsetBeyondContent_WritesLastLine()
    {
        var writer = new StringWriter();
        string output = "line1\nline2\nline3";

        ScreenRenderer.RenderOutput(writer, output, availableHeight: 5, scrollOffset: 100);

        string rendered = writer.ToString();
        // Should show at least the last line when scrolled past
        Assert.Contains("line3", rendered);
    }
}

public class AlternateBufferTests
{
    [Fact]
    public void EnterAlternateBuffer_WritesEscapeSequence()
    {
        var writer = new StringWriter();

        ScreenRenderer.EnterAlternateBuffer(writer);

        string output = writer.ToString();
        Assert.Contains("\x1b[?1049h", output);
        Assert.Contains("\x1b[?25l", output); // hide cursor
    }

    [Fact]
    public void ExitAlternateBuffer_WritesEscapeSequence()
    {
        var writer = new StringWriter();

        ScreenRenderer.ExitAlternateBuffer(writer);

        string output = writer.ToString();
        Assert.Contains("\x1b[?1049l", output);
        Assert.Contains("\x1b[?25h", output); // show cursor
    }

    [Fact]
    public void ClearScreen_WritesCursorHomeAndClear()
    {
        var writer = new StringWriter();

        ScreenRenderer.ClearScreen(writer);

        string output = writer.ToString();
        Assert.Contains("\x1b[H", output);
        Assert.Contains("\x1b[2J", output);
    }
}

public class TimeMachineHeaderTests
{
    [Fact]
    public void FormatHeader_WithTimeMachine_ShowsTimeIndicator()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet build",
            timestamp: new DateTime(2026, 3, 30, 14, 32, 5),
            exitCode: 0,
            runCount: 3,
            isPaused: true,
            useColor: false,
            isDiffEnabled: false,
            isTimeMachine: true,
            timeMachinePosition: 3,
            timeMachineTotal: 17);

        Assert.Contains("[TIME", header);
        Assert.Contains("3/17", header);
    }

    [Fact]
    public void FormatHeader_WithTimeMachine_ShowsSnapshotRunCount()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet build",
            timestamp: new DateTime(2026, 3, 30, 14, 32, 5),
            exitCode: 0,
            runCount: 3,
            isPaused: true,
            useColor: false,
            isTimeMachine: true,
            timeMachinePosition: 5,
            timeMachineTotal: 10);

        Assert.Contains("[run #3]", header);
        Assert.Contains("[TIME 5/10]", header);
    }

    [Fact]
    public void FormatHeader_NoTimeMachine_NoTimeIndicator()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet build",
            timestamp: DateTime.Now,
            exitCode: 0,
            runCount: 1,
            isPaused: false,
            useColor: false);

        Assert.DoesNotContain("[TIME", header);
    }
}

public class HistoryOverlayTests
{
    private static PeepResult MakeResult(string output = "output", int exitCode = 0)
    {
        return new PeepResult(output, exitCode, TimeSpan.FromSeconds(1), TriggerSource.Interval);
    }

    [Fact]
    public void RenderHistoryOverlay_ShowsRunNumbers()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), new DateTime(2026, 3, 30, 14, 0, 0), runNumber: 1);
        history.Add(MakeResult("b"), new DateTime(2026, 3, 30, 14, 0, 2), runNumber: 2);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 1, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains("#1", output);
        Assert.Contains("#2", output);
    }

    [Fact]
    public void RenderHistoryOverlay_ShowsTimestamps()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), new DateTime(2026, 3, 30, 14, 32, 5), runNumber: 1);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 0, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains("14:32:05", output);
    }

    [Fact]
    public void RenderHistoryOverlay_ShowsSelectionMarker()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("b"), DateTime.Now, runNumber: 2);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 0, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains(">", output);
    }

    [Fact]
    public void RenderHistoryOverlay_ShowsExitCode()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a", exitCode: 1), DateTime.Now, runNumber: 1);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 0, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains("exit:1", output);
    }

    [Fact]
    public void RenderHistoryOverlay_ShowsDiffStats()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("line1\nline2"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("line1\nchanged"), DateTime.Now, runNumber: 2);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 1, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains("+1", output);
        Assert.Contains("-1", output);
    }

    [Fact]
    public void RenderHistoryOverlay_ShowsHistoryTitle()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 0, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains("History", output);
    }

    [Fact]
    public void RenderHistoryOverlay_ShowsKeyHints()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 0, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains("Enter", output);
        Assert.Contains("navigate", output);
    }

    [Fact]
    public void RenderHistoryOverlay_NewestAtTop()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), new DateTime(2026, 3, 30, 14, 0, 0), runNumber: 1);
        history.Add(MakeResult("b"), new DateTime(2026, 3, 30, 14, 0, 2), runNumber: 2);
        history.Add(MakeResult("c"), new DateTime(2026, 3, 30, 14, 0, 4), runNumber: 3);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 2, width: 80, height: 24);

        string output = writer.ToString();
        int pos3 = output.IndexOf("#3");
        int pos1 = output.IndexOf("#1");
        Assert.True(pos3 < pos1, "Newest (#3) should appear before oldest (#1)");
    }

    [Fact]
    public void RenderHistoryOverlay_EmptyHistory_DoesNotThrow()
    {
        var history = new SnapshotHistory(capacity: 10);
        var writer = new StringWriter();

        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: -1, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains("History", output);
        Assert.DoesNotContain("#", output); // No run entries
    }

    [Fact]
    public void RenderHistoryOverlay_VerySmallTerminal_DoesNotThrow()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);

        var writer = new StringWriter();

        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 0, width: 40, height: 2);

        // Should not throw, should still produce some output
        Assert.NotEmpty(writer.ToString());
    }
}

public class HelpOverlayTimeMachineTests
{
    [Fact]
    public void RenderHelpOverlay_ShowsTimeMachineKeys()
    {
        var writer = new StringWriter();
        ScreenRenderer.RenderHelpOverlay(writer, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains("Left/Right", output);
        Assert.Contains("time travel", output.ToLowerInvariant());
        Assert.Contains("istory", output); // "History" or "history"
    }
}

public class DiffHighlightingTests
{
    [Fact]
    public void ComputeChangedLines_IdenticalOutput_NoChanges()
    {
        string[] current = { "line 1", "line 2", "line 3" };
        string[] previous = { "line 1", "line 2", "line 3" };

        bool[] changed = ScreenRenderer.ComputeChangedLines(current, previous);

        Assert.Equal(3, changed.Length);
        Assert.False(changed[0]);
        Assert.False(changed[1]);
        Assert.False(changed[2]);
    }

    [Fact]
    public void ComputeChangedLines_ModifiedLine_MarkedChanged()
    {
        string[] current = { "line 1", "MODIFIED", "line 3" };
        string[] previous = { "line 1", "line 2", "line 3" };

        bool[] changed = ScreenRenderer.ComputeChangedLines(current, previous);

        Assert.False(changed[0]);
        Assert.True(changed[1]);
        Assert.False(changed[2]);
    }

    [Fact]
    public void ComputeChangedLines_NewLines_MarkedChanged()
    {
        string[] current = { "line 1", "line 2", "line 3", "new line" };
        string[] previous = { "line 1", "line 2", "line 3" };

        bool[] changed = ScreenRenderer.ComputeChangedLines(current, previous);

        Assert.False(changed[0]);
        Assert.False(changed[1]);
        Assert.False(changed[2]);
        Assert.True(changed[3]);
    }

    [Fact]
    public void ComputeChangedLines_IgnoresAnsiDifferences()
    {
        // Same text but different ANSI colouring — should NOT be marked as changed
        string[] current = { "\x1b[32mgreen text\x1b[0m" };
        string[] previous = { "\x1b[31mgreen text\x1b[0m" };

        bool[] changed = ScreenRenderer.ComputeChangedLines(current, previous);

        Assert.False(changed[0]);
    }

    [Fact]
    public void ComputeChangedLines_EmptyPrevious_AllChanged()
    {
        string[] current = { "line 1", "line 2" };
        string[] previous = Array.Empty<string>();

        bool[] changed = ScreenRenderer.ComputeChangedLines(current, previous);

        Assert.True(changed[0]);
        Assert.True(changed[1]);
    }

    [Fact]
    public void RenderOutputWithDiff_HighlightsChangedLines()
    {
        using var writer = new StringWriter();
        string current = "same\nchanged\nsame";
        string previous = "same\noriginal\nsame";

        ScreenRenderer.RenderOutputWithDiff(writer, current, previous, 10, 0);

        string output = writer.ToString();
        // The changed line should have the diff highlight escape sequence
        Assert.Contains("\x1b[48;5;17m", output);
        // The unchanged lines should not
        Assert.Contains("same", output);
    }

    [Fact]
    public void FormatHeader_ShowsDiffIndicator()
    {
        string header = ScreenRenderer.FormatHeader(
            2.0, "git status", DateTime.Now, 0, 1, false, false, isDiffEnabled: true);

        Assert.Contains("[DIFF]", header);
    }

    [Fact]
    public void FormatHeader_NoDiffIndicatorWhenDisabled()
    {
        string header = ScreenRenderer.FormatHeader(
            2.0, "git status", DateTime.Now, 0, 1, false, false, isDiffEnabled: false);

        Assert.DoesNotContain("[DIFF]", header);
    }
}
