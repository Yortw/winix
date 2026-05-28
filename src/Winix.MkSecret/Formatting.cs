using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Winix.MkSecret;

/// <summary>Renders user-facing output: the stderr entropy note and the stdout JSON envelope.
/// Plain output (the secret itself, one per line) is written directly by <see cref="Cli"/>.</summary>
public static class Formatting
{
    /// <summary>The stderr entropy note, e.g. <c>mksecret: ≈ 119 bits</c>. Whole bits.</summary>
    public static string EntropyNote(double bits)
        => $"mksecret: ≈ {(int)System.Math.Round(bits)} bits";

    /// <summary>JSON envelope: <c>{"mode":"password","bits":119.1,"values":[...]}</c>.</summary>
    public static string JsonEnvelope(MkSecretOptions options, IReadOnlyList<string> values, double bits)
    {
        var sb = new StringBuilder();
        sb.Append("{\"mode\":\"").Append(options.Mode.ToString().ToLowerInvariant()).Append("\",");
        sb.Append("\"bits\":").Append(System.Math.Round(bits, 1).ToString("0.0#", CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"values\":[");
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0) { sb.Append(','); }
            AppendJsonString(sb, values[i]);
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static void AppendJsonString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) { sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); }
                    else { sb.Append(c); }
                    break;
            }
        }
        sb.Append('"');
    }
}
