using System;

namespace Winix.Ids;

/// <summary>
/// Generates time-ordered UUID v7 identifiers via <see cref="Guid.CreateVersion7"/>
/// (.NET 10) with an additional monotonicity guard.
/// </summary>
/// <remarks>
/// <see cref="Guid.CreateVersion7()"/> does not guarantee strict monotonic ordering
/// within the same millisecond — successive calls re-randomise the sub-timestamp bits
/// and can produce descending values. This generator compares each candidate against
/// the last issued value in canonical big-endian byte order; if the candidate would
/// sort ≤ the predecessor, the predecessor is incremented by 1 (as a 128-bit big-endian
/// integer) and returned instead. The version nibble (bit 52 of the 128-bit value) and
/// variant bits (bits 62–63) are preserved in practice because an intra-ms increment
/// burst of that magnitude is impossible (requires 2^76+ calls in one millisecond).
/// Thread-safe — a single lock serialises compare-and-increment and the shared state.
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

            // Guid.CompareTo orders by mixed-endian struct layout, not canonical UUID
            // byte order, so it does not match how UUID strings sort. Compare via
            // big-endian byte arrays instead — Guid.TryWriteBytes(..., bigEndian: true)
            // gives us the canonical order with zero heap allocations (stackalloc).
            Span<byte> candidateBytes = stackalloc byte[16];
            Span<byte> lastBytes = stackalloc byte[16];
            candidate.TryWriteBytes(candidateBytes, bigEndian: true, out _);
            _last.TryWriteBytes(lastBytes, bigEndian: true, out _);

            if (candidateBytes.SequenceCompareTo(lastBytes) <= 0)
            {
                candidate = Increment(_last);
            }

            _last = candidate;
            return candidate;
        }
    }

    /// <summary>
    /// Returns the UUID immediately following <paramref name="guid"/> in canonical
    /// big-endian byte order by adding 1 to its 128-bit representation. Allocation-free.
    /// </summary>
    private static Guid Increment(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes, bigEndian: true, out _);

        int carry = 1;
        for (int i = 15; i >= 0 && carry > 0; i--)
        {
            int sum = bytes[i] + carry;
            bytes[i] = (byte)(sum & 0xFF);
            carry = sum >> 8;
        }

        return new Guid(bytes, bigEndian: true);
    }
}
