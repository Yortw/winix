using System;
using Winix.Codec;

namespace Winix.Ids;

/// <summary>
/// Monotonic ULID generator. Produces 26-char Crockford base32 identifiers where
/// the first 10 chars encode the Unix ms timestamp and the remaining 16 chars
/// encode 80 bits of state, matching the canonical ULID specification.
/// </summary>
/// <remarks>
/// Within a single millisecond, the 80-bit random portion is incremented as a
/// big-endian integer on each call (rather than re-drawn), guaranteeing that
/// generation order equals sort order for intra-ms batches. Overflow within a
/// single ms is mathematically impossible (80 bits of headroom).
/// <para/>
/// If the system clock appears to go backward (NTP correction, VM snapshot
/// restore, DST bug), the timestamp is clamped to the last-issued value and
/// the call is routed through the increment branch — monotonicity is preserved
/// even across clock skew.
/// <para/>
/// Encoding uses <strong>leading</strong> zero padding (2 zero pad bits + 128
/// data bits = 130 bits → 26 chars), matching the ULID spec. The generic
/// <see cref="Base32Crockford.Encode"/> uses trailing padding which would
/// produce correctly-sortable but non-standard output.
/// <para/>
/// Thread-safe. The per-call lock is uncontended in CLI single-process usage.
/// </remarks>
public sealed class UlidGenerator : IIdGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private readonly ISecureRandom _random;
    private readonly ISystemClock _clock;
    private readonly object _lock = new();
    private long _lastMs = -1;
    private readonly byte[] _lastRandom = new byte[10];

    /// <summary>Constructs a new generator with injectable random and clock sources.</summary>
    public UlidGenerator(ISecureRandom random, ISystemClock clock)
    {
        _random = random;
        _clock = clock;
    }

    /// <inheritdoc />
    public string Generate(IdsOptions options)
    {
        Span<byte> payload = stackalloc byte[16];

        lock (_lock)
        {
            long ms = _clock.UnixMsNow();

            // Clock skew guard: if the clock appears to go backward, clamp the
            // timestamp to the last-issued value. The next branch will hit the
            // increment path and emit an ID strictly greater than the previous.
            if (ms < _lastMs)
            {
                ms = _lastMs;
            }

            if (ms == _lastMs)
            {
                IncrementBigEndian(_lastRandom);
            }
            else
            {
                _lastMs = ms;
                _random.Fill(_lastRandom);
            }

            // 48-bit ms timestamp, big-endian, into payload[0..5].
            payload[0] = (byte)((ms >> 40) & 0xFF);
            payload[1] = (byte)((ms >> 32) & 0xFF);
            payload[2] = (byte)((ms >> 24) & 0xFF);
            payload[3] = (byte)((ms >> 16) & 0xFF);
            payload[4] = (byte)((ms >> 8)  & 0xFF);
            payload[5] = (byte)(ms & 0xFF);

            _lastRandom.AsSpan().CopyTo(payload[6..]);
        }

        return EncodeUlidPayload(payload);
    }

    private static void IncrementBigEndian(byte[] buffer)
    {
        for (int i = buffer.Length - 1; i >= 0; i--)
        {
            if (++buffer[i] != 0)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Encodes a 128-bit payload as 26 Crockford base32 characters using ULID's
    /// leading-pad rule (2 zero bits prepended → 130-bit stream → 26×5-bit groups).
    /// </summary>
    private static string EncodeUlidPayload(ReadOnlySpan<byte> payload16)
    {
        Span<char> chars = stackalloc char[26];

        int buffer = 0;
        int bitsLeft = 2; // the 2 leading zero pad bits are already implied in buffer=0.
        int charIndex = 0;

        foreach (byte b in payload16)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                int index = (buffer >> bitsLeft) & 0x1F;
                chars[charIndex++] = Alphabet[index];
            }
        }
        // 130 / 5 = 26 exactly; no trailing remainder.
        return new string(chars);
    }
}
