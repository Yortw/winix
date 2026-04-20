#nullable enable
using Xunit;
using Winix.QrCode;
using Winix.QrCode.Renderers;

namespace Winix.QrCode.Tests;

public class UnicodeRendererTests
{
    // Test helper: construct a matrix from a string grid. 'X' = dark, '.' = light.
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
    public void Render_2x2_AllDark_ProducesFullBlock()
    {
        QrMatrix m = MatrixOf("XX", "XX");
        string result = UnicodeRenderer.Render(m, drawQuietZone: false);
        Assert.Equal("██\n", result);
    }

    [Fact]
    public void Render_2x2_TopDarkBottomLight_ProducesUpperHalf()
    {
        QrMatrix m = MatrixOf("XX", "..");
        string result = UnicodeRenderer.Render(m, drawQuietZone: false);
        Assert.Equal("▀▀\n", result);
    }

    [Fact]
    public void Render_2x2_TopLightBottomDark_ProducesLowerHalf()
    {
        QrMatrix m = MatrixOf("..", "XX");
        string result = UnicodeRenderer.Render(m, drawQuietZone: false);
        Assert.Equal("▄▄\n", result);
    }

    [Fact]
    public void Render_2x2_AllLight_ProducesSpace()
    {
        QrMatrix m = MatrixOf("..", "..");
        string result = UnicodeRenderer.Render(m, drawQuietZone: false);
        Assert.Equal("  \n", result);
    }

    [Fact]
    public void Render_OddRowCount_PadsWithBlankRow()
    {
        // 3 rows → 2 terminal lines (pair 1-2, pair 3-blank).
        QrMatrix m = MatrixOf("XXX", "XXX", "XXX");
        string result = UnicodeRenderer.Render(m, drawQuietZone: false);
        // Line 1: rows 0+1 → full block. Line 2: row 2 + blank pad → upper half only.
        Assert.Equal("███\n▀▀▀\n", result);
    }

    [Fact]
    public void Render_Mixed_2x2_FourCornerModulesProduceFourGlyphs()
    {
        QrMatrix m = MatrixOf("X.", ".X");
        string result = UnicodeRenderer.Render(m, drawQuietZone: false);
        // (0,0)=X top, (1,0)=. bottom → upper half "▀"
        // (0,1)=. top, (1,1)=X bottom → lower half "▄"
        Assert.Equal("▀▄\n", result);
    }

    [Fact]
    public void Render_QuietZone_Adds4ModuleMarginOnAllSides()
    {
        QrMatrix m = MatrixOf("XX", "XX");
        string result = UnicodeRenderer.Render(m, drawQuietZone: true);
        // Expected: 4 blank rows top, 2 rows of data (full blocks with 4 light modules padding each side), 4 blank rows bottom.
        // Unicode half-block pairs rows, so: 2 blank lines (8 blank-blank rows), then data line (4 space + 2 full-block + 4 space), then 2 blank lines.
        string[] lines = result.Split('\n');
        // Last entry is empty (trailing newline).
        // Total grid = 2 data + 2*4 quiet zone = 10 rows; half-block pairs → 5 terminal lines.
        Assert.Equal(5, lines.Length - 1);                                           // 5 content lines + 1 empty trailing
        // 2 blank top
        Assert.Equal(new string(' ', 10), lines[0]);
        Assert.Equal(new string(' ', 10), lines[1]);
        Assert.Equal(new string(' ', 4) + "██" + new string(' ', 4), lines[2]);     // data row
        // 2 blank bottom
        Assert.Equal(new string(' ', 10), lines[3]);
        Assert.Equal(new string(' ', 10), lines[4]);
    }
}
