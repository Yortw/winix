#nullable enable
using System.Globalization;
using System.Text;

namespace Winix.QrCode.Renderers;

/// <summary>
/// Renders a <see cref="QrMatrix"/> as an SVG document. Uses a single <c>&lt;path&gt;</c> with one
/// subpath per dark module for compact output.
/// </summary>
public static class SvgRenderer
{
    private const int QuietZone = 4;

    /// <summary>
    /// Render <paramref name="matrix"/> as SVG text.
    /// </summary>
    /// <param name="matrix">The QR module matrix.</param>
    /// <param name="pixelsPerModule">Nominal pixels per module — sets width/height attributes.</param>
    /// <param name="drawQuietZone">When true, pad with a 4-module quiet zone on all sides.</param>
    public static string Render(QrMatrix matrix, int pixelsPerModule, bool drawQuietZone)
    {
        int margin = drawQuietZone ? QuietZone : 0;
        int size = matrix.Size;
        int total = size + 2 * margin;
        int pixels = total * pixelsPerModule;

        StringBuilder d = new();
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (matrix.Modules[r, c])
                {
                    int x = c + margin;
                    int y = r + margin;
                    d.Append('M');
                    d.Append(x.ToString(CultureInfo.InvariantCulture));
                    d.Append(',');
                    d.Append(y.ToString(CultureInfo.InvariantCulture));
                    d.Append("h1v1h-1z");
                }
            }
        }

        StringBuilder svg = new();
        svg.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" ");
        svg.Append("viewBox=\"0 0 ");
        svg.Append(total.ToString(CultureInfo.InvariantCulture));
        svg.Append(' ');
        svg.Append(total.ToString(CultureInfo.InvariantCulture));
        svg.Append("\" width=\"");
        svg.Append(pixels.ToString(CultureInfo.InvariantCulture));
        svg.Append("\" height=\"");
        svg.Append(pixels.ToString(CultureInfo.InvariantCulture));
        svg.Append("\" shape-rendering=\"crispEdges\">\n");
        svg.Append("  <path fill=\"#000\" d=\"");
        svg.Append(d);
        svg.Append("\"/>\n");
        svg.Append("</svg>\n");
        return svg.ToString();
    }
}
