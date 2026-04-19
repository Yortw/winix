#nullable enable
using System;

namespace Winix.Url;

/// <summary>Percent-decodes strings. Pure — no I/O.</summary>
public static class Decoder
{
    /// <summary>Decode <paramref name="input"/>.</summary>
    /// <param name="input">The percent-encoded string.</param>
    /// <param name="form">When true, apply form-decoding (+ → space). Default is RFC 3986 literal +.</param>
    public static string Decode(string input, bool form)
    {
        if (form)
        {
            // Swap + for space first, then decode %xx. Matches application/x-www-form-urlencoded.
            return Uri.UnescapeDataString(input.Replace('+', ' '));
        }
        return Uri.UnescapeDataString(input);
    }
}
