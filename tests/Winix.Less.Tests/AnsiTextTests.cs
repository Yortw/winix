#nullable enable

using Winix.Less;
using Xunit;

namespace Winix.Less.Tests;

public class AnsiTextTests
{
    // 1. Plain text passes through unchanged
    [Fact]
    public void StripAnsi_PlainText_Unchanged()
    {
        Assert.Equal("hello world", AnsiText.StripAnsi("hello world"));
    }

    // 2. Bold + reset stripped, visible text preserved
    [Fact]
    public void StripAnsi_WithBoldReset_StripsEscapes()
    {
        Assert.Equal("bold text", AnsiText.StripAnsi("\x1b[1mbold text\x1b[0m"));
    }

    // 3. Multiple colour codes all stripped
    [Fact]
    public void StripAnsi_WithColourCodes_StripsAll()
    {
        Assert.Equal("red green", AnsiText.StripAnsi("\x1b[31mred\x1b[0m \x1b[32mgreen\x1b[0m"));
    }

    // 4. Semicolon-delimited (combined) codes stripped correctly
    [Fact]
    public void StripAnsi_MultipleCodeSequence_StripsCorrectly()
    {
        Assert.Equal("text", AnsiText.StripAnsi("\x1b[1;31mtext\x1b[0m"));
    }

    // 5. Visible length of plain text equals character count
    [Fact]
    public void VisibleLength_PlainText_ReturnsLength()
    {
        Assert.Equal(5, AnsiText.VisibleLength("hello"));
    }

    // 6. Escape sequences do not contribute to visible length
    [Fact]
    public void VisibleLength_WithAnsi_ExcludesEscapes()
    {
        Assert.Equal(4, AnsiText.VisibleLength("\x1b[1mbold\x1b[0m"));
    }

    // 7. Plain text is truncated at maxWidth
    [Fact]
    public void TruncateToWidth_PlainText_Truncates()
    {
        Assert.Equal("hel", AnsiText.TruncateToWidth("hello", 3));
    }

    // 8. Text shorter than maxWidth is returned in full
    [Fact]
    public void TruncateToWidth_ShortText_ReturnsFull()
    {
        Assert.Equal("hi", AnsiText.TruncateToWidth("hi", 10));
    }

    // 9. ANSI sequences inside the visible width are kept; content beyond is dropped
    [Fact]
    public void TruncateToWidth_WithAnsi_PreservesEscapesWithinWidth()
    {
        var result = AnsiText.TruncateToWidth("\x1b[1mbold\x1b[0m rest", 4);
        Assert.Contains("bold", result);
        Assert.DoesNotContain("rest", result);
    }

    // 10. Zero width produces an empty string
    [Fact]
    public void TruncateToWidth_ZeroWidth_ReturnsEmpty()
    {
        Assert.Equal("", AnsiText.TruncateToWidth("hello", 0));
    }

    // 11. SubstringByVisibleOffset extracts from the correct offset in plain text
    [Fact]
    public void SubstringByVisibleOffset_ExtractsFromMiddle()
    {
        Assert.Equal("world", AnsiText.SubstringByVisibleOffset("hello world", 6));
    }

    // 12. ANSI sequences before the offset are counted as zero visible width
    [Fact]
    public void SubstringByVisibleOffset_WithAnsi_SkipsCorrectly()
    {
        // "\x1b[1mAB\x1b[0mCD" — visible chars: A(0) B(1) C(2) D(3)
        // offset 2 → skip A and B (and the surrounding escapes), return "CD"
        Assert.Equal("CD", AnsiText.SubstringByVisibleOffset("\x1b[1mAB\x1b[0mCD", 2));
    }
}
