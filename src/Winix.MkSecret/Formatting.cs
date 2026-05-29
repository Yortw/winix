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

    /// <summary>JSON envelope: <c>{"mode":"password","bits":119.1,"values":[...]}</c>.
    /// Composed from <see cref="JsonOpen"/>/<see cref="JsonValue"/>/<see cref="JsonClose"/> so that the
    /// streamed output <see cref="Winix.MkSecret.Cli"/> emits is byte-identical to this string form.</summary>
    public static string JsonEnvelope(MkSecretOptions options, IReadOnlyList<string> values, double bits)
    {
        var sb = new StringBuilder();
        sb.Append(JsonOpen(options, bits));
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0) { sb.Append(','); }
            sb.Append(JsonValue(values[i]));
        }
        sb.Append(JsonClose());
        return sb.ToString();
    }

    /// <summary>The envelope prefix up to and including the opening <c>"values":[</c>, so callers can
    /// stream values one at a time instead of buffering them all. Caller writes commas between values.</summary>
    public static string JsonOpen(MkSecretOptions options, double bits)
    {
        var sb = new StringBuilder();
        sb.Append("{\"mode\":\"").Append(options.Mode.ToString().ToLowerInvariant()).Append("\",");
        sb.Append("\"bits\":").Append(System.Math.Round(bits, 1).ToString("0.0#", CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"values\":[");
        return sb.ToString();
    }

    /// <summary>A single value rendered as an escaped JSON string (no surrounding comma).</summary>
    public static string JsonValue(string value)
    {
        var sb = new StringBuilder();
        AppendJsonString(sb, value);
        return sb.ToString();
    }

    /// <summary>The envelope suffix closing the values array and object: <c>]}</c>.</summary>
    public static string JsonClose() => "]}";

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
