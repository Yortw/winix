#nullable enable
using System.Text;

namespace Winix.QrCode.Renderers;

/// <summary>
/// Renders a <see cref="QrMatrix"/> as terminal-unicode art using half-block characters.
/// Each terminal cell represents two vertical QR modules.
/// </summary>
public static class UnicodeRenderer
{
    private const int QuietZone = 4;

    // Half-block glyphs indexed by (top, bottom) booleans.
    // [false, false] = light,light → space
    // [true,  false] = dark,light  → upper-half
    // [false, true ] = light,dark  → lower-half
    // [true,  true ] = dark,dark   → full block
    private const char Space = ' ';
    private const char UpperHalf = '\u2580';    // ▀
    private const char LowerHalf = '\u2584';    // ▄
    private const char FullBlock = '\u2588';    // █

    /// <summary>
    /// Render <paramref name="matrix"/> to a string of half-block Unicode glyphs.
    /// </summary>
    /// <param name="matrix">The QR module matrix.</param>
    /// <param name="drawQuietZone">When true, pad with a 4-module quiet zone on all sides.</param>
    public static string Render(QrMatrix matrix, bool drawQuietZone)
    {
        int margin = drawQuietZone ? QuietZone : 0;
        int size = matrix.Size;
        int total = size + 2 * margin;

        // Determine module (dark/light) at (row, col) accounting for quiet zone.
        bool ModuleAt(int r, int c)
        {
            int mr = r - margin;
            int mc = c - margin;
            if (mr < 0 || mc < 0 || mr >= size || mc >= size)
            {
                return false;       // quiet zone is always light
            }
            return matrix.Modules[mr, mc];
        }

        StringBuilder sb = new();
        for (int r = 0; r < total; r += 2)
        {
            for (int c = 0; c < total; c++)
            {
                bool top = ModuleAt(r, c);
                bool bottom = r + 1 < total && ModuleAt(r + 1, c);
                char glyph = (top, bottom) switch
                {
                    (false, false) => Space,
                    (true,  false) => UpperHalf,
                    (false, true)  => LowerHalf,
                    (true,  true)  => FullBlock,
                };
                sb.Append(glyph);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
