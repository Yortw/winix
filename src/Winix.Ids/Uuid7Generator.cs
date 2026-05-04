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
/// integer) and returned instead.
/// <para/>
/// Per RFC 9562 §4 the version nibble lives at bits 48–51 (high nibble of the 7th byte
/// in big-endian) and the variant bits live at bits 64–65 (high two bits of the 9th byte).
/// In normal operation:
/// <list type="bullet">
///   <item>The version nibble requires roughly 2^76 carries from the 80-bit random
///         tail to overflow into byte 6 — well beyond any realistic intra-ms generation rate.</item>
///   <item>The variant bits live at byte 8, so a carry chain through the lower 7 bytes
///         (~2^62 calls) would propagate into them. Still vanishingly improbable —
///         a ~4 GHz core can't approach 2^62 generations per millisecond — but the safety
///         margin is 2^62, not 2^76. Round-1 review CR-Minor — comment was over-stating.</item>
/// </list>
/// Thread-safe — a single lock serialises compare-and-increment and the shared state.
/// <para/>
/// Round-1 review TA-I2 — accepts an optional candidate-source delegate (default
/// <see cref="Guid.CreateVersion7"/>) so tests can force same-ms collisions and pin the
/// Increment branch deterministically. Production callers leave the constructor argument
/// as default and get the BCL's CSPRNG-backed v7 implementation.
/// </remarks>
public sealed class Uuid7Generator : IIdGenerator
{
    private readonly Func<Guid> _candidateSource;
    private readonly object _lock = new();
    private Guid _last = Guid.Empty;

    /// <summary>Constructs a generator using the BCL's <see cref="Guid.CreateVersion7"/>.</summary>
    public Uuid7Generator() : this(Guid.CreateVersion7)
    {
    }

    /// <summary>Constructs a generator with an injected candidate source. Test seam.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidateSource"/> is null.</exception>
    public Uuid7Generator(Func<Guid> candidateSource)
    {
        ArgumentNullException.ThrowIfNull(candidateSource);
        _candidateSource = candidateSource;
    }

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
            Guid candidate = _candidateSource();

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
    /// Internal so tests can pin known-input → known-output (carry propagation, all-FF
    /// wrap, byte-7 boundary) without going through the full <see cref="MakeMonotonic"/>
    /// state machine.
    /// </summary>
    internal static Guid Increment(Guid guid)
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
