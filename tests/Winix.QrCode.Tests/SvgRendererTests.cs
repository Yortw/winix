#nullable enable
using Xunit;
using Winix.QrCode;
using Winix.QrCode.Renderers;

namespace Winix.QrCode.Tests;

public class SvgRendererTests
{
    private static QrMatrix MatrixOf(params string[] rows)
    {
        int size = rows.Length;
        bool[,] m = new bool[size, size];
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                m[r, c] = rows[r][c] == 'X';
            }
        }
        return new QrMatrix(m);
    }

    [Fact]
    public void Render_ContainsSvgRoot_WithXmlns()
    {
        QrMatrix m = MatrixOf("X.", ".X");
        string svg = SvgRenderer.Render(m, pixelsPerModule: 10, drawQuietZone: false);
        Assert.Contains("<svg ", svg);
        Assert.Contains("xmlns=\"http://www.w3.org/2000/svg\"", svg);
        Assert.Contains("</svg>", svg);
    }

    [Fact]
    public void Render_NoQuietZone_ViewBoxMatchesMatrixSize()
    {
        QrMatrix m = MatrixOf("XX", "XX");
        string svg = SvgRenderer.Render(m, pixelsPerModule: 10, drawQuietZone: false);
        Assert.Contains("viewBox=\"0 0 2 2\"", svg);
    }

    [Fact]
    public void Render_QuietZone_ViewBoxIncludesMargin()
    {
        QrMatrix m = MatrixOf("XX", "XX");
        string svg = SvgRenderer.Render(m, pixelsPerModule: 10, drawQuietZone: true);
        // 2 modules + 2*4 quiet zone = 10.
        Assert.Contains("viewBox=\"0 0 10 10\"", svg);
    }

    [Fact]
    public void Render_SizeAttributes_ComputedFromPixelsPerModule()
    {
        QrMatrix m = MatrixOf("XX", "XX");
        string svg = SvgRenderer.Render(m, pixelsPerModule: 10, drawQuietZone: false);
        // 2 modules × 10 px/module = 20 px.
        Assert.Contains("width=\"20\"", svg);
        Assert.Contains("height=\"20\"", svg);
    }

    [Fact]
    public void Render_DarkModule_HasPathSegment()
    {
        QrMatrix m = MatrixOf("X.", "..");
        string svg = SvgRenderer.Render(m, pixelsPerModule: 10, drawQuietZone: false);
        // Path segment for (0,0): "M0,0h1v1h-1z"
        Assert.Contains("M0,0h1v1h-1z", svg);
    }

    [Fact]
    public void Render_LightModule_NoPathSegment()
    {
        QrMatrix m = MatrixOf("..", "..");
        string svg = SvgRenderer.Render(m, pixelsPerModule: 10, drawQuietZone: false);
        // Path should be empty (d="").
        Assert.Contains("d=\"\"", svg);
    }

    [Fact]
    public void Render_QuietZone_ShiftsPathOriginByMargin()
    {
        QrMatrix m = MatrixOf("X.", "..");
        string svg = SvgRenderer.Render(m, pixelsPerModule: 10, drawQuietZone: true);
        // With quiet zone, (0,0) module is at viewBox (4,4).
        Assert.Contains("M4,4h1v1h-1z", svg);
    }

    [Fact]
    public void Render_FillBlack_FillRuleEvenOdd()
    {
        QrMatrix m = MatrixOf("XX", "XX");
        string svg = SvgRenderer.Render(m, pixelsPerModule: 10, drawQuietZone: false);
        Assert.Contains("fill=\"#000\"", svg);
    }
}
