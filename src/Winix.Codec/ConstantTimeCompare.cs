#nullable enable

using System;

namespace Winix.Codec;

/// <summary>
/// Timing-safe equality comparisons for cryptographic values such as HMAC digests
/// and auth tokens. All overloads OR each difference bit into an accumulator and
/// check the accumulator once at the end, so timing does not reveal which position
/// first differed.
/// </summary>
public static class ConstantTimeCompare
{
    /// <summary>
    /// Compares two byte sequences for equality in constant time with respect to
    /// the common length. Returns false immediately (without scanning) if lengths differ,
    /// which leaks only length — length is typically non-secret (e.g., fixed-size HMAC output).
    /// </summary>
    public static bool BytesEqual(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }

    /// <summary>
    /// Compares two strings for equality in constant time using ASCII-only case
    /// folding when <paramref name="caseInsensitive"/> is true. Intended for
    /// hex/Base64-shaped digest outputs — <strong>not</strong> safe for general
    /// Unicode case comparison. Returns false for null inputs without throwing.
    /// </summary>
    /// <remarks>
    /// The <paramref name="caseInsensitive"/> path uses a small ASCII-range check
    /// (<c>if (c &gt;= 'A' &amp;&amp; c &lt;= 'Z')</c>) which is not strictly branchless —
    /// on hostile hardware the branch could in principle leak whether each char is
    /// in A–Z. For <c>digest</c>'s use case (local CLI comparing hex HMAC strings)
    /// this is not a practical attack vector. If used in a context where a
    /// nanosecond-precision local adversary is a concern, fold to lowercase at the
    /// call site before invoking with <c>caseInsensitive: false</c>.
    /// </remarks>
    public static bool StringEqualsAscii(string? a, string? b, bool caseInsensitive)
    {
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            char ca = a[i];
            char cb = b[i];
            if (caseInsensitive)
            {
                if (ca >= 'A' && ca <= 'Z') ca = (char)(ca + 32);
                if (cb >= 'A' && cb <= 'Z') cb = (char)(cb + 32);
            }
            diff |= ca ^ cb;
        }
        return diff == 0;
    }
}
