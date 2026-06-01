#nullable enable

using System.Collections.Generic;
using Winix.Man;
using Xunit;

namespace Winix.Man.Tests;

/// <summary>
/// Colour regression tests for <see cref="TerminalRenderer"/>: locks ESC emission on
/// the colour-gated code path so a future unwired-colour regression is caught immediately.
/// These tests target the renderer directly — the interactive pager is not driven
/// end-to-end because it is a blocking terminal loop.
///
/// Key distinction in man's colour model:
///   - Bold (<c>\x1b[1m</c>) and italic/underline (<c>\x1b[4m</c>) are always emitted
///     regardless of <see cref="RendererOptions.Color"/> — they are structural, not cosmetic.
///   - Cyan (<c>\x1b[36m</c>) on section headings is the <em>only</em> code gated by
///     <see cref="RendererOptions.Color"/>. The regression tests focus on that gate.
/// </summary>
public sealed class ColourRegressionTests
{
    private static readonly string Esc = ((char)27).ToString();

    private static TerminalRenderer CreateRenderer(bool color, int width = 80)
    {
        return new TerminalRenderer(new RendererOptions
        {
            WidthOverride = width,
            Color = color,
        });
    }

    // ── SectionHeading — the primary Color-gated element ─────────────────────────

    /// <summary>
    /// When Color=true, a SectionHeading must emit the cyan ANSI escape in addition
    /// to the always-present bold code.
    /// </summary>
    [Fact]
    public void Render_SectionHeading_ColorTrue_EmitsCyanEsc()
    {
        var renderer = CreateRenderer(color: true);
        var blocks = new List<DocumentBlock>
        {
            new SectionHeading("SYNOPSIS")
        };

        string output = renderer.Render(blocks);

        // Cyan: ESC[36m — only emitted when Color=true
        Assert.Contains(Esc + "[36m", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// When Color=false, a SectionHeading must NOT emit the cyan ANSI escape.
    /// Bold is still emitted (structural) — that is expected and does not violate this contract.
    /// </summary>
    [Fact]
    public void Render_SectionHeading_ColorFalse_NoCyanEsc()
    {
        var renderer = CreateRenderer(color: false);
        var blocks = new List<DocumentBlock>
        {
            new SectionHeading("SYNOPSIS")
        };

        string output = renderer.Render(blocks);

        Assert.DoesNotContain(Esc + "[36m", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Color flag must not suppress bold/italic (structural formatting).
    /// Bold is always emitted regardless of Color — this ensures the gate is
    /// narrow (cyan only), not overreaching.
    /// </summary>
    [Fact]
    public void Render_BoldSpan_ColorFalse_StillEmitsBoldEsc()
    {
        var renderer = CreateRenderer(color: false);
        var blocks = new List<DocumentBlock>
        {
            new Paragraph(new[] { new StyledSpan("option", FontStyle.Bold) })
        };

        string output = renderer.Render(blocks);

        // Bold ANSI is structural — emitted even when Color=false
        Assert.Contains(Esc + "[1m", output, StringComparison.Ordinal);
    }

    // ── Multiple sections — verify Color gate fires per heading ──────────────────

    /// <summary>
    /// With Color=true and multiple section headings, every heading emits cyan.
    /// </summary>
    [Fact]
    public void Render_MultipleSectionHeadings_ColorTrue_AllEmitCyan()
    {
        var renderer = CreateRenderer(color: true);
        var blocks = new List<DocumentBlock>
        {
            new SectionHeading("NAME"),
            new SectionHeading("DESCRIPTION"),
            new SectionHeading("OPTIONS"),
        };

        string output = renderer.Render(blocks);

        // Count occurrences: one per section heading
        int count = 0;
        int pos = 0;
        string target = Esc + "[36m";
        while ((pos = output.IndexOf(target, pos, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            pos += target.Length;
        }

        Assert.Equal(3, count);
    }

    /// <summary>
    /// With Color=false and multiple section headings, no cyan escape is emitted at all.
    /// </summary>
    [Fact]
    public void Render_MultipleSectionHeadings_ColorFalse_NoCyanAnywhere()
    {
        var renderer = CreateRenderer(color: false);
        var blocks = new List<DocumentBlock>
        {
            new SectionHeading("NAME"),
            new SectionHeading("DESCRIPTION"),
            new SectionHeading("OPTIONS"),
        };

        string output = renderer.Render(blocks);

        Assert.DoesNotContain(Esc + "[36m", output, StringComparison.Ordinal);
    }
}
