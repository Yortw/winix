#nullable enable
using System;

namespace Winix.Url;

/// <summary>Percent-decodes strings. Pure — no I/O.</summary>
public static class Decoder
{
    /// <summary>Strict-mode result: either the decoded value or a typed error explaining the malformed escape.</summary>
    public sealed record StrictResult(string? Value, string? Error)
    {
        /// <summary>True if the input was valid percent-encoding.</summary>
        public bool Success => Error is null;
    }

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

    /// <summary>
    /// Decode <paramref name="input"/> in strict mode: reject any malformed percent-escape
    /// (lone <c>%</c>, <c>%X</c> at end-of-input, <c>%XY</c> where X or Y is not a hex digit).
    /// Round-1 review SFH-I3 — Uri.UnescapeDataString silently passes these through, so the
    /// default (lenient) Decode method can't distinguish "input was plain text" from "input
    /// was corrupted percent-encoding." Strict mode is opt-in via <c>--strict</c>.
    /// </summary>
    public static StrictResult DecodeStrict(string input, bool form)
    {
        // Walk the string and validate every '%' is followed by two hex digits. If any are
        // malformed, return the offending position and the rest of the input from there.
        // Note: form decoding (+ → space) is independent of the validity check — '+' is
        // always a valid character; we replace it after validation.
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] != '%') continue;
            if (i + 2 >= input.Length)
            {
                return new StrictResult(null,
                    $"malformed percent-escape at position {i}: incomplete '%' sequence at end of input");
            }
            if (!IsHex(input[i + 1]) || !IsHex(input[i + 2]))
            {
                return new StrictResult(null,
                    $"malformed percent-escape at position {i}: '%{input[i + 1]}{input[i + 2]}' is not valid hex");
            }
            i += 2; // skip the two hex digits we just validated
        }
        // All percent sequences valid — delegate to the standard decoder.
        return new StrictResult(Decode(input, form), null);
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') ||
        (c >= 'a' && c <= 'f') ||
        (c >= 'A' && c <= 'F');
}
