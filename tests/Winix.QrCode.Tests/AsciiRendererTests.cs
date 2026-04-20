#nullable enable
using Xunit;
using Winix.QrCode;
using Winix.QrCode.Renderers;

namespace Winix.QrCode.Tests;

public class AsciiRendererTests
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
    public void Render_2x2_AllDark_FourHashHashRows()
    {
        QrMatrix m = MatrixOf("XX", "XX");
        string result = AsciiRenderer.Render(m, drawQuietZone: false);
        Assert.Equal("####\n####\n", result);
    }

    [Fact]
    public void Render_2x2_AllLight_FourSpaceRows()
    {
        QrMatrix m = MatrixOf("..", "..");
        string result = AsciiRenderer.Render(m, drawQuietZone: false);
        Assert.Equal("    \n    \n", result);
    }

    [Fact]
    public void Render_2x2_Checker_HashAndSpaceAlternating()
    {
        QrMatrix m = MatrixOf("X.", ".X");
        string result = AsciiRenderer.Render(m, drawQuietZone: false);
        Assert.Equal("##  \n  ##\n", result);
    }

    [Fact]
    public void Render_QuietZone_Adds4ModuleMarginOnAllSides()
    {
        QrMatrix m = MatrixOf("XX", "XX");
        string result = AsciiRenderer.Render(m, drawQuietZone: true);
        // Expected: 4 blank rows top + 2 data rows + 4 blank rows bottom. Each row is (4+2+4)*2 = 20 chars wide.
        string[] lines = result.Split('\n');
        Assert.Equal(10, lines.Length - 1);                                         // 10 content lines + 1 empty trailing
        // 4 blank top
        for (int i = 0; i < 4; i++) { Assert.Equal(new string(' ', 20), lines[i]); }
        Assert.Equal(new string(' ', 8) + "####" + new string(' ', 8), lines[4]);   // data row 0
        Assert.Equal(new string(' ', 8) + "####" + new string(' ', 8), lines[5]);   // data row 1
        // 4 blank bottom
        for (int i = 6; i < 10; i++) { Assert.Equal(new string(' ', 20), lines[i]); }
    }
}
