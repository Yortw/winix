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
