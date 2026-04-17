using System;

namespace Winix.Ids;

/// <summary>
/// Generates time-ordered UUID v7 identifiers via <see cref="Guid.CreateVersion7"/>
/// (.NET 10) with an additional monotonicity guard.
/// </summary>
/// <remarks>
/// <see cref="Guid.CreateVersion7()"/> does not guarantee strict monotonic ordering
/// within the same millisecond — successive calls can produce descending random bits.
/// This class tracks the last-issued GUID and, when a newly generated value would
/// compare ≤ the previous one, increments the 128-bit representation by 1 (carrying
/// through bytes in little-endian Guid memory layout) so the output is always ≥ prev.
/// Version (nibble at offset 7 hi-bits) and variant (nibble at offset 8 hi-bits) are
/// preserved by the increment since regressions only occur within the same millisecond
/// and the counter overflow into those nibbles is astronomically unlikely in practice.
/// Thread safety: a lock serialises access so the generator is safe to share.
/// </remarks>
public sealed class Uuid7Generator : IIdGenerator
{
    private readonly object _lock = new();
    private Guid _last = Guid.Empty;

    /// <inheritdoc />
    public string Generate(IdsOptions options)
    {
        Guid guid = MakeMonotonic();
        return Formatting.FormatGuid(guid, options.Format, options.Uppercase);
    }

    private Guid MakeMonotonic()
    {
        lock (_lock)
        {
            Guid candidate = Guid.CreateVersion7();

            // If the new candidate doesn't sort strictly after the last issued GUID,
            // increment the last value by 1 to preserve monotonic order.
            if (string.CompareOrdinal(candidate.ToString("D"), _last.ToString("D")) <= 0)
            {
                candidate = Increment(_last);
            }

            _last = candidate;
            return candidate;
        }
    }

    /// <summary>
    /// Returns the UUID immediately following <paramref name="guid"/> in lexicographic
    /// (ordinal string / byte-value) order by adding 1 to the big-endian byte sequence.
    /// </summary>
    private static Guid Increment(Guid guid)
    {
        // Guid bytes in memory are stored in a mixed-endian layout that differs from
        // the canonical string order. We round-trip through the "D" string format so
        // the arithmetic is on the canonical big-endian hex representation, then parse
        // the result back. This avoids manual byte-swap arithmetic on the struct layout.
        string hex = guid.ToString("N"); // 32 lowercase hex chars, no hyphens
        Span<char> chars = stackalloc char[32];
        hex.AsSpan().CopyTo(chars);

        // Add 1 to the 128-bit big-endian value represented as 32 hex digits.
        int carry = 1;
        for (int i = 31; i >= 0 && carry > 0; i--)
        {
            int digit = HexVal(chars[i]) + carry;
            chars[i] = HexChar(digit & 0xF);
            carry = digit >> 4;
        }

        return Guid.Parse(chars);
    }

    private static int HexVal(char c) =>
        c >= '0' && c <= '9' ? c - '0' : c - 'a' + 10;

    private static char HexChar(int v) =>
        (char)(v < 10 ? '0' + v : 'a' + v - 10);
}
