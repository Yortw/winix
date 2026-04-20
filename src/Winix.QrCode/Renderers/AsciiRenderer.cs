#nullable enable
using System.Text;

namespace Winix.QrCode.Renderers;

/// <summary>
/// Renders a <see cref="QrMatrix"/> as ASCII art: <c>##</c> per dark module, two spaces per light module.
/// One terminal row per QR module; two chars wide per module for a roughly square aspect ratio.
/// </summary>
public static class AsciiRenderer
{
    private const int QuietZone = 4;
    private const string Dark = "##";
    private const string Light = "  ";

    /// <summary>
    /// Render <paramref name="matrix"/> as ASCII.
    /// </summary>
    /// <param name="matrix">The QR module matrix.</param>
    /// <param name="drawQuietZone">When true, pad with a 4-module quiet zone on all sides.</param>
    public static string Render(QrMatrix matrix, bool drawQuietZone)
    {
        int margin = drawQuietZone ? QuietZone : 0;
        int size = matrix.Size;
        int total = size + 2 * margin;

        bool ModuleAt(int r, int c)
        {
            int mr = r - margin;
            int mc = c - margin;
            if (mr < 0 || mc < 0 || mr >= size || mc >= size)
            {
                return false;
            }
            return matrix.Modules[mr, mc];
        }

        StringBuilder sb = new();
        for (int r = 0; r < total; r++)
        {
            for (int c = 0; c < total; c++)
            {
                sb.Append(ModuleAt(r, c) ? Dark : Light);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
