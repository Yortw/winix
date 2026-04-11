#nullable enable

using Winix.Man;
using Xunit;

namespace Winix.Man.Tests;

public sealed class ManMacroExpanderTests
{
    private readonly GroffLexer _lexer = new();
    private readonly ManMacroExpander _expander = new();

    private IReadOnlyList<DocumentBlock> Expand(string source)
    {
        return _expander.Expand(_lexer.Tokenise(source));
    }

    [Fact]
    public void TH_ProducesTitleBlock()
    {
        var blocks = Expand(".TH TIMEIT 1 \"2026-03-31\" Winix \"User Commands\"");

        var title = Assert.Single(blocks);
        var tb = Assert.IsType<TitleBlock>(title);
        Assert.Equal("TIMEIT", tb.Name);
        Assert.Equal("1", tb.Section);
        Assert.Equal("2026-03-31", tb.Date);
        Assert.Equal("Winix", tb.Source);
        Assert.Equal("User Commands", tb.Manual);
    }

    [Fact]
    public void SH_ProducesSectionHeading()
    {
        var blocks = Expand(".SH NAME");

        var heading = Assert.Single(blocks);
        var sh = Assert.IsType<SectionHeading>(heading);
        Assert.Equal("NAME", sh.Text);
    }

    [Fact]
    public void SS_ProducesSubsectionHeading()
    {
        var blocks = Expand(".SS \"Watch Mode\"");

        var heading = Assert.Single(blocks);
        var ss = Assert.IsType<SubsectionHeading>(heading);
        Assert.Equal("Watch Mode", ss.Text);
    }

    [Fact]
    public void PP_StartsParagraph_TextFollows()
    {
        var blocks = Expand(".PP\nThis is paragraph text.");

        var para = Assert.Single(blocks);
        var p = Assert.IsType<Paragraph>(para);
        Assert.Single(p.Content);
        Assert.Contains("This is paragraph text.", p.Content[0].Text);
    }

    [Fact]
    public void ConsecutiveTextLines_MergedIntoParagraph()
    {
        var blocks = Expand(".PP\nLine one.\nLine two.");

        var para = Assert.Single(blocks);
        var p = Assert.IsType<Paragraph>(para);
        var fullText = string.Join("", p.Content.Select(s => s.Text));
        Assert.Contains("Line one.", fullText);
        Assert.Contains("Line two.", fullText);
    }

    [Fact]
    public void TP_ProducesTaggedParagraph()
    {
        var blocks = Expand(".TP\n\\fB\\-v\\fR, \\fB\\-\\-verbose\\fR\nEnable verbose output.");

        Assert.Single(blocks);
        var tp = Assert.IsType<TaggedParagraph>(blocks[0]);
        Assert.NotEmpty(tp.Tag);
        Assert.NotEmpty(tp.Body);
    }

    [Fact]
    public void IP_ProducesIndentedParagraph()
    {
        var blocks = Expand(".IP \\(bu 2\nBullet item text.");

        Assert.Single(blocks);
        var ip = Assert.IsType<IndentedParagraph>(blocks[0]);
        Assert.NotEmpty(ip.Content);
    }

    [Fact]
    public void B_ProducesBoldSpan()
    {
        var blocks = Expand(".B timeit");

        var para = Assert.Single(blocks);
        var p = Assert.IsType<Paragraph>(para);
        Assert.Single(p.Content);
        Assert.Equal("timeit", p.Content[0].Text);
        Assert.Equal(FontStyle.Bold, p.Content[0].Style);
    }

    [Fact]
    public void I_ProducesItalicSpan()
    {
        var blocks = Expand(".I command");

        var para = Assert.Single(blocks);
        var p = Assert.IsType<Paragraph>(para);
        Assert.Single(p.Content);
        Assert.Equal("command", p.Content[0].Text);
        Assert.Equal(FontStyle.Italic, p.Content[0].Style);
    }

    [Fact]
    public void BR_ProducesAlternatingBoldRomanSpans()
    {
        var blocks = Expand(".BR timeit (1)");

        var para = Assert.Single(blocks);
        var p = Assert.IsType<Paragraph>(para);
        Assert.Equal(2, p.Content.Count);
        Assert.Equal(FontStyle.Bold, p.Content[0].Style);
        Assert.Equal(FontStyle.Roman, p.Content[1].Style);
    }

    [Fact]
    public void NfFi_ProducesPreformattedBlock()
    {
        var blocks = Expand(".nf\ntimeit dotnet build\n  real 12.4s\n.fi");

        var pre = Assert.Single(blocks);
        var pb = Assert.IsType<PreformattedBlock>(pre);
        Assert.Contains("timeit dotnet build", pb.Text);
        Assert.Contains("real 12.4s", pb.Text);
    }

    [Fact]
    public void Sp_ProducesVerticalSpace()
    {
        var blocks = Expand(".sp");

        var vs = Assert.Single(blocks);
        var space = Assert.IsType<VerticalSpace>(vs);
        Assert.Equal(1, space.Lines);
    }

    [Fact]
    public void RS_RE_AffectsIndentLevel()
    {
        var blocks = Expand(".RS\n.IP \\(bu 2\nNested item.\n.RE");

        Assert.Single(blocks);
        var ip = Assert.IsType<IndentedParagraph>(blocks[0]);
        Assert.True(ip.Indent > 0);
    }

    [Fact]
    public void InlineEscapes_BoldItalicRoman()
    {
        var blocks = Expand("This is \\fBbold\\fR and \\fIitalic\\fR text.");

        var para = Assert.Single(blocks);
        var p = Assert.IsType<Paragraph>(para);
        Assert.True(p.Content.Count >= 4);
        Assert.Contains(p.Content, s => s.Style == FontStyle.Bold && s.Text == "bold");
        Assert.Contains(p.Content, s => s.Style == FontStyle.Italic && s.Text == "italic");
    }

    [Fact]
    public void InlineEscape_HyphenMinus()
    {
        var blocks = Expand("Use \\-v for verbose.");

        var para = Assert.Single(blocks);
        var p = Assert.IsType<Paragraph>(para);
        var fullText = string.Join("", p.Content.Select(s => s.Text));
        Assert.Contains("-v", fullText);
    }

    [Fact]
    public void Comment_IsIgnored()
    {
        var blocks = Expand(".\\\" This is a comment\n.SH NAME");

        var heading = Assert.Single(blocks);
        Assert.IsType<SectionHeading>(heading);
    }

    [Fact]
    public void EmptyInput_ProducesNoBlocks()
    {
        var blocks = Expand("");

        Assert.Empty(blocks);
    }

    [Fact]
    public void FullManPage_ProducesExpectedBlockSequence()
    {
        var source = @".TH TIMEIT 1 ""2026-03-31"" Winix ""User Commands""
.SH NAME
timeit \- time command execution
.SH SYNOPSIS
.B timeit
.RI [ options ]
.I command
.SH DESCRIPTION
.PP
Times the execution of a command and displays a summary.
.SH OPTIONS
.TP
\fB\-v\fR, \fB\-\-verbose\fR
Enable verbose output.
.TP
\fB\-q\fR, \fB\-\-quiet\fR
Suppress summary output.";

        var blocks = Expand(source);

        // First block is TitleBlock
        Assert.IsType<TitleBlock>(blocks[0]);

        // Count section headings — should have NAME, SYNOPSIS, DESCRIPTION, OPTIONS
        var sections = blocks.OfType<SectionHeading>().ToList();
        Assert.Equal(4, sections.Count);

        // Count tagged paragraphs — should have 2 options
        var tagged = blocks.OfType<TaggedParagraph>().ToList();
        Assert.Equal(2, tagged.Count);
    }
}
