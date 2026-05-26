#nullable enable
using System;
using Winix.Codec;

namespace Winix.Digest;

/// <summary>
/// Compares a computed hash against an expected encoded string in constant time.
/// Hex and Crockford base32 comparisons are case-insensitive (per their respective specs);
/// base64 (standard and URL-safe) is case-sensitive because the base64 alphabet includes
/// mixed case that carries meaning.
/// </summary>
public static class Verifier
{
    /// <summary>
    /// Returns true if the computed bytes match the expected encoded value after
    /// re-encoding the computed bytes in <paramref name="format"/> and comparing.
    /// </summary>
    /// <remarks>
    /// The comparison is constant-time with respect to the encoded string — a timing
    /// attacker cannot learn anything about where the strings diverge. This matters
    /// when verifying user-supplied HMAC tags.
    /// </remarks>
    public static bool Verify(byte[] computed, string expected, OutputFormat format)
    {
        if (expected is null) return false;
        string computedStr = format switch
        {
            OutputFormat.Hex       => Hex.Encode(computed),
            OutputFormat.Base64    => Base64.Encode(computed, urlSafe: false),
            OutputFormat.Base64Url => Base64.Encode(computed, urlSafe: true),
            OutputFormat.Base32    => Base32Crockford.Encode(computed),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
        // Round-2 review CR-I1 — Crockford base32 is defined as case-insensitive on
        // decode (the encoder emits uppercase, but a user-supplied --verify value in
        // lowercase must still match). The previous implementation only made hex
        // case-insensitive, so `digest --base32 --verify <lowercase> file` rejected
        // valid hashes as mismatches.
        bool caseInsensitive = format == OutputFormat.Hex || format == OutputFormat.Base32;
        return ConstantTimeCompare.StringEqualsAscii(computedStr, expected, caseInsensitive);
    }
}
