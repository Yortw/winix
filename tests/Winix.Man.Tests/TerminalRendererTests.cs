#nullable enable

using System.Collections.Generic;
using Winix.Man;
using Xunit;

namespace Winix.Man.Tests;

public sealed class TerminalRendererTests
{
    private static TerminalRenderer CreateRenderer(bool color = false, bool hyperlinks = false, int width = 80)
    {
        return new TerminalRenderer(new RendererOptions
        {
            WidthOverride = width,
            Color = color,
            Hyperlinks = hyperlinks
        });
    }

    [Fact]
    public void Render_TitleBlock_FormatsHeader()
    {
        var renderer = CreateRenderer();
        var blocks = new List<DocumentBlock>
        {
            new TitleBlock("TIMEIT", "1", "2026-03-31", "Winix", "User Commands")
        };

        var output = renderer.Render(blocks);

        Assert.Contains("TIMEIT(1)", output);
    }

    [Fact]
    public void Render_SectionHeading_IsBold()
    {
        var renderer = CreateRenderer(color: false);
        var blocks = new List<DocumentBlock>
        {
            new SectionHeading("NAME")
        };

        var output = renderer.Render(blocks);

        Assert.Contains("\x1b[1m", output);
        Assert.Contains("NAME", output);
    }

    [Fact]
    public void Render_SectionHeading_WithColour_IncludesColourCode()
    {
        var renderer = CreateRenderer(color: true);
        var blocks = new List<DocumentBlock>
        {
            new SectionHeading("NAME")
        };

        var output = renderer.Render(blocks);

        Assert.Contains("\x1b[36m", output);
        Assert.Contains("NAME", output);
    }

    [Fact]
    public void Render_Paragraph_WrapsToWidth()
    {
        var renderer = CreateRenderer(width: 40);
        var longText = "This is a very long paragraph that should be wrapped because it exceeds the specified rendering width of forty characters.";
        var blocks = new List<DocumentBlock>
        {
            new Paragraph(new[] { new StyledSpan(longText, FontStyle.Roman) })
        };

        var output = renderer.Render(blocks);

        // Each line should be at most 42 chars (40 + small indent tolerance)
        foreach (var line in output.Split('\n'))
        {
            Assert.True(line.TrimEnd('\r').Length <= 42, $"Line too long ({line.TrimEnd('\r').Length}): '{line.TrimEnd('\r')}'");
        }
    }

    [Fact]
    public void Render_PreformattedBlock_DoesNotWrap()
    {
        var renderer = CreateRenderer(width: 30);
        var longLine = "this-is-a-very-long-preformatted-line-that-must-not-be-wrapped-at-all";
        var blocks = new List<DocumentBlock>
        {
            new PreformattedBlock(longLine)
        };

        var output = renderer.Render(blocks);

        Assert.Contains(longLine, output);
    }

    [Fact]
    public void Render_TaggedParagraph_FormatsTagAndBody()
    {
        var renderer = CreateRenderer();
        var tag = new[] { new StyledSpan("-v, --verbose", FontStyle.Bold) };
        var body = new[] { new StyledSpan("Enable verbose output.", FontStyle.Roman) };
        var blocks = new List<DocumentBlock>
        {
            new TaggedParagraph(tag, body)
        };

        var output = renderer.Render(blocks);

        Assert.Contains("-v, --verbose", output);
        Assert.Contains("Enable verbose output.", output);
    }

    [Fact]
    public void Render_BoldSpan_EmitsAnsiCodes()
    {
        var renderer = CreateRenderer(color: false);
        var blocks = new List<DocumentBlock>
        {
            new Paragraph(new[] { new StyledSpan("bold", FontStyle.Bold) })
        };

        var output = renderer.Render(blocks);

        Assert.Contains("\x1b[1mbold\x1b[0m", output);
    }

    [Fact]
    public void Render_ItalicSpan_EmitsUnderline()
    {
        var renderer = CreateRenderer(color: false);
        var blocks = new List<DocumentBlock>
        {
            new Paragraph(new[] { new StyledSpan("italic", FontStyle.Italic) })
        };

        var output = renderer.Render(blocks);

        Assert.Contains("\x1b[4m", output);
    }

    [Fact]
    public void Render_VerticalSpace_EmitsBlankLines()
    {
        var renderer = CreateRenderer();
        var blocks = new List<DocumentBlock>
        {
            new Paragraph(new[] { new StyledSpan("before", FontStyle.Roman) }),
            new VerticalSpace(2),
            new Paragraph(new[] { new StyledSpan("after", FontStyle.Roman) })
        };

        var output = renderer.Render(blocks);

        Assert.Contains("before", output);
        Assert.Contains("after", output);
    }

    [Fact]
    public void Render_NoColour_OmitsColourCodes()
    {
        var renderer = CreateRenderer(color: false);
        var blocks = new List<DocumentBlock>
        {
            new SectionHeading("NAME")
        };

        var output = renderer.Render(blocks);

        Assert.DoesNotContain("\x1b[36m", output);
    }

    [Fact]
    public void Render_EmptyBlocks_ReturnsEmpty()
    {
        var renderer = CreateRenderer();

        var output = renderer.Render(new List<DocumentBlock>());

        Assert.Equal("", output);
    }
}
