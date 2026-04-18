using System;
using Winix.Codec;

namespace Winix.Ids;

/// <summary>
/// Monotonic ULID generator. Produces 26-char Crockford base32 identifiers where
/// the first 10 chars encode the Unix ms timestamp and the remaining 16 chars
/// encode 80 bits of state.
///
/// Within a single millisecond, the 80-bit random portion is incremented as a
/// big-endian integer on each call (rather than re-drawn), guaranteeing that
/// generation order equals sort order for intra-ms batches. Overflow within a
/// single ms is mathematically impossible (80 bits of headroom).
///
/// Thread-safe. The per-call lock is uncontended in CLI single-process usage.
/// </summary>
public sealed class UlidGenerator : IIdGenerator
{
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
        long ms = _clock.UnixMsNow();
        byte[] payload = new byte[16];

        lock (_lock)
        {
            if (ms == _lastMs)
            {
                IncrementBigEndian(_lastRandom);
            }
            else
            {
                _lastMs = ms;
                _random.Fill(_lastRandom);
            }

            // ULID ms timestamp is the top 48 bits (6 bytes), big-endian.
            payload[0] = (byte)((ms >> 40) & 0xFF);
            payload[1] = (byte)((ms >> 32) & 0xFF);
            payload[2] = (byte)((ms >> 24) & 0xFF);
            payload[3] = (byte)((ms >> 16) & 0xFF);
            payload[4] = (byte)((ms >> 8)  & 0xFF);
            payload[5] = (byte)(ms & 0xFF);

            Buffer.BlockCopy(_lastRandom, 0, payload, 6, 10);
        }

        return Base32Crockford.Encode(payload);
    }

    private static void IncrementBigEndian(byte[] buffer)
    {
        for (int i = buffer.Length - 1; i >= 0; i--)
        {
            if (++buffer[i] != 0)
            {
                return;
            }
            // If we wrapped to 0, carry to the next byte. 80 bits of headroom
            // means this loop will never overflow the whole buffer within one ms.
        }
    }
}
