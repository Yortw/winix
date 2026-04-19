#nullable enable
using System;

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
            // Preserve '/' across segments; each segment component-encoded.
            string[] segments = input.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = Uri.EscapeDataString(segments[i]);
            }
            return string.Join('/', segments);
        }

        // Component or Query — same alphabet, %20 for space.
        return Uri.EscapeDataString(input);
    }
}
