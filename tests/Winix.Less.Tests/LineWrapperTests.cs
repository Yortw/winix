#nullable enable

using Winix.Less;
using Xunit;

namespace Winix.Less.Tests;

public class LineWrapperTests
{
    // 1. A line shorter than the terminal width is returned as-is in one row.
    [Fact]
    public void WrapLine_ShortLine_ReturnsOneLine()
    {
        var rows = LineWrapper.WrapLine("hello", 80);
        Assert.Single(rows);
        Assert.Equal("hello", rows[0]);
    }

    // 2. A line whose visible length exactly equals the width fits in one row.
    [Fact]
    public void WrapLine_ExactWidth_ReturnsOneLine()
    {
        var rows = LineWrapper.WrapLine("12345", 5);
        Assert.Single(rows);
        Assert.Equal("12345", rows[0]);
    }

    // 3. A line longer than the width is split into multiple rows.
    [Fact]
    public void WrapLine_LongLine_WrapsAtWidth()
    {
        var rows = LineWrapper.WrapLine("1234567890", 5);
        Assert.Equal(2, rows.Count);
        Assert.Equal("12345", rows[0]);
        Assert.Equal("67890", rows[1]);
    }

    // 4. ANSI escape sequences are zero-width — wrapping is based on visible characters only.
    [Fact]
    public void WrapLine_WithAnsi_WrapsOnVisibleWidth()
    {
        // Visible: ABCDE (bold) + FGHIJ — 10 visible chars, width 5 → 2 rows
        var rows = LineWrapper.WrapLine("\x1b[1mABCDE\x1b[0mFGHIJ", 5);
        Assert.Equal(2, rows.Count);
        Assert.Equal(5, AnsiText.VisibleLength(rows[0]));
    }

    // 5. An empty line produces exactly one empty row (so the display advances one line).
    [Fact]
    public void WrapLine_EmptyLine_ReturnsOneEmptyRow()
    {
        var rows = LineWrapper.WrapLine(string.Empty, 80);
        Assert.Single(rows);
        Assert.Equal(string.Empty, rows[0]);
    }

    // 6. ChopLine returns the full line when it is shorter than the width with offset 0.
    [Fact]
    public void ChopLine_ShortLine_ReturnsFull()
    {
        Assert.Equal("hello", LineWrapper.ChopLine("hello", 80, 0));
    }

    // 7. ChopLine truncates a line that is longer than the width.
    [Fact]
    public void ChopLine_LongLine_TruncatesToWidth()
    {
        Assert.Equal("hello", LineWrapper.ChopLine("hello world", 5, 0));
    }

    // 8. ChopLine with a non-zero leftColumn pans the viewport right.
    [Fact]
    public void ChopLine_WithOffset_PansRight()
    {
        Assert.Equal("world", LineWrapper.ChopLine("hello world", 5, 6));
    }

    // 9. FormatLineNumber right-aligns a single digit in the gutter with a trailing space.
    [Fact]
    public void FormatLineNumber_SingleDigit_RightAligned()
    {
        // gutterWidth 6: "     3 " = 5 spaces + "3" + " " = 7 characters total
        var result = LineWrapper.FormatLineNumber(3, 6);
        Assert.Equal("     3 ", result);
        Assert.Equal(7, result.Length);
    }

    // 10. FormatLineNumber with null produces an all-spaces continuation gutter.
    [Fact]
    public void FormatLineNumber_Continuation_Blank()
    {
        // gutterWidth 6: 7 spaces (gutterWidth + 1)
        var result = LineWrapper.FormatLineNumber(null, 6);
        Assert.Equal("       ", result);
        Assert.Equal(7, result.Length);
    }

    // 11. CalculateDisplayRows sums rows correctly, giving at least 1 row per line.
    [Fact]
    public void CalculateDisplayRows_MixedLines_CountsCorrectly()
    {
        var lines = new[] { "short", "this is a long line that needs wrapping" };
        // "short" = 5 visible chars, width 20 → 1 row
        // "this is a long line that needs wrapping" = 39 visible chars, width 20 → ceil(39/20) = 2 rows
        // Total = 3
        int rows = LineWrapper.CalculateDisplayRows(lines, 20);
        Assert.Equal(3, rows);
    }
}
