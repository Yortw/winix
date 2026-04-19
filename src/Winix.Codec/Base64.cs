#nullable enable

using System;

namespace Winix.Codec;

/// <summary>
/// Base64 encoding/decoding. Standard alphabet (RFC 4648 §4) or URL-safe variant
/// (RFC 4648 §5, <c>+</c>/<c>/</c> replaced with <c>-</c>/<c>_</c>). Both variants
/// use <c>=</c> padding. Decode auto-detects alphabet by normalising URL-safe chars
/// before delegating to <see cref="Convert.FromBase64String"/>.
/// </summary>
public static class Base64
{
    /// <summary>
    /// Encodes bytes as base64. Returns empty string for empty input.
    /// </summary>
    /// <param name="input">The bytes to encode.</param>
    /// <param name="urlSafe">When true, uses URL-safe alphabet (<c>-</c> and <c>_</c> instead of <c>+</c> and <c>/</c>).</param>
    public static string Encode(ReadOnlySpan<byte> input, bool urlSafe = false)
    {
        string standard = Convert.ToBase64String(input);
        return urlSafe ? standard.Replace('+', '-').Replace('/', '_') : standard;
    }

    /// <summary>
    /// Decodes a base64 string. Accepts both standard and URL-safe alphabets;
    /// the two are distinguished by whether <c>-</c>/<c>_</c> or <c>+</c>/<c>/</c>
    /// appear, so mixed-alphabet strings are normalised rather than rejected.
    /// Returns an empty array for null or empty input.
    /// </summary>
    /// <exception cref="FormatException">Thrown for invalid base64 content.</exception>
    public static byte[] Decode(string input)
    {
        if (string.IsNullOrEmpty(input)) return Array.Empty<byte>();
        // Normalise URL-safe chars so Convert.FromBase64String handles both alphabets.
        string normalised = input.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(normalised);
    }
}
