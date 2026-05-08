#nullable enable

using System;
using System.Globalization;
using System.Text;

namespace Winix.Man;

/// <summary>
/// Pure-function helpers extracted from <c>man/Program.cs</c> so the contracts they
/// implement can be unit-tested directly without going through the orchestration layer.
/// </summary>
public static class Helpers
{
    /// <summary>
    /// Resolves the rendering width using the priority chain
    /// <c>--width</c> &gt; <c>$MANWIDTH</c> &gt; <c>min(terminal, 80)</c>.
    /// </summary>
    /// <param name="widthFlag">The value passed via <c>--width</c>, or <see langword="null"/> when omitted.</param>
    /// <param name="manWidthEnv">The value of the <c>MANWIDTH</c> environment variable, or <see langword="null"/>.</param>
    /// <param name="terminalWidth">The detected terminal width in columns.</param>
    /// <returns>
    /// The resolved width. The MANWIDTH branch ignores values below 10 (matches the
    /// <c>--width</c> validator) and any non-integer string, falling through to the
    /// terminal-width-with-80-cap branch.
    /// </returns>
    /// <remarks>
    /// The 80-column cap matches the effective behaviour of GNU man-db (which delegates
    /// rendering to groff, whose default width is 80). This is documented in man.1.md /
    /// README.md / docs/ai/man.md.
    /// </remarks>
    public static int ResolveWidth(int? widthFlag, string? manWidthEnv, int terminalWidth)
    {
        if (widthFlag.HasValue)
        {
            return widthFlag.Value;
        }

        if (!string.IsNullOrWhiteSpace(manWidthEnv)
            && int.TryParse(manWidthEnv, NumberStyles.Integer, CultureInfo.InvariantCulture, out int envWidth)
            && envWidth >= 10)
        {
            return envWidth;
        }

        return Math.Min(terminalWidth, 80);
    }

    /// <summary>
    /// Escapes a string value for embedding in a JSON document per RFC 8259 §7.
    /// </summary>
    /// <param name="value">The string to escape, or <see langword="null"/>.</param>
    /// <returns>
    /// The escaped value enclosed in quotation marks, or the literal <c>null</c> when
    /// <paramref name="value"/> is <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// Handles the standard short escapes (<c>\"</c>, <c>\\</c>, <c>\b</c>, <c>\f</c>,
    /// <c>\n</c>, <c>\r</c>, <c>\t</c>) and emits <c>\uXXXX</c> for any other character below
    /// 0x20. RFC 8259 §7 forbids unescaped control characters in JSON string content; without
    /// this, a NAME-section description containing a stray control byte (e.g. 0x07 BEL) would
    /// produce invalid JSON output (Tier-2 baseline 2026-05-07 finding F4).
    /// </remarks>
    public static string EscapeJsonString(string? value)
    {
        if (value is null)
        {
            return "null";
        }

        var sb = new StringBuilder();
        sb.Append('"');
        foreach (char ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                    {
                        // Any other C0 control byte must be \uXXXX-escaped per RFC 8259 §7.
                        sb.Append("\\u");
                        sb.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
