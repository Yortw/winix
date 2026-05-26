#nullable enable
using System;
using System.Text;

namespace Winix.Url;

/// <summary>Percent-encodes strings per <see cref="EncodeMode"/>. Pure — no I/O.</summary>
public static class Encoder
{
    /// <summary>Encode <paramref name="input"/> per the given mode.</summary>
    /// <param name="input">The raw string.</param>
    /// <param name="mode">Encoding variant.</param>
    /// <param name="form">When true, overrides <paramref name="mode"/> with form-encoding (space → +).</param>
    /// <returns>The percent-encoded string.</returns>
    public static string Encode(string input, EncodeMode mode, bool form)
    {
        if (form || mode == EncodeMode.Form)
        {
            // Component-encode, then swap %20 for +.
            return Uri.EscapeDataString(input).Replace("%20", "+");
        }

        if (mode == EncodeMode.Path)
        {
            // Preserve valid "%XX" triplets as-is (case-normalised to uppercase per RFC 3986
            // §6.2.2.1) so that pre-encoded paths round-trip correctly: url parse | url build
            // stays idempotent. Input chars not in existing escapes get segment-wise
            // EscapeDataString'd, with '/' preserved between segments.
            return EncodePathPreservingEscapes(input);
        }

        // Component or Query — same alphabet, %20 for space.
        return Uri.EscapeDataString(input);
    }

    private static string EncodePathPreservingEscapes(string input)
    {
        var sb = new StringBuilder(input.Length + 16);
        int chunkStart = 0;
        int i = 0;
        while (i < input.Length)
        {
            if (input[i] == '%' && i + 2 < input.Length && IsHex(input[i + 1]) && IsHex(input[i + 2]))
            {
                if (i > chunkStart)
                {
                    AppendEncodedChunk(sb, input, chunkStart, i - chunkStart);
                }
                sb.Append('%');
                sb.Append(char.ToUpperInvariant(input[i + 1]));
                sb.Append(char.ToUpperInvariant(input[i + 2]));
                i += 3;
                chunkStart = i;
            }
            else
            {
                i++;
            }
        }
        if (chunkStart < input.Length)
        {
            AppendEncodedChunk(sb, input, chunkStart, input.Length - chunkStart);
        }
        return sb.ToString();
    }

    // Encode a plain-text substring, preserving '/' as path-segment separator.
    private static void AppendEncodedChunk(StringBuilder sb, string input, int start, int length)
    {
        string chunk = input.Substring(start, length);
        string[] segments = chunk.Split('/');
        for (int j = 0; j < segments.Length; j++)
        {
            if (j > 0) sb.Append('/');
            sb.Append(Uri.EscapeDataString(segments[j]));
        }
    }

    private static bool IsHex(char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
}
