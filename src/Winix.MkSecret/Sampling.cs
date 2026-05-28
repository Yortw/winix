using System;
using Winix.Codec;

namespace Winix.MkSecret;

/// <summary>Unbiased uniform selection of an index in <c>[0, count)</c> from a CSPRNG.
/// Generalises the rejection-sampling-against-a-power-of-two-mask technique to ranges larger
/// than one byte (the EFF wordlist needs 13 bits). Rejection — not modulo — is what removes bias.</summary>
public static class Sampling
{
    /// <summary>Returns a uniformly-random index in <c>[0, count)</c>. <paramref name="count"/> must be ≥ 1.</summary>
    public static int UniformIndex(ISecureRandom random, int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 1) return 0;

        int mask = NextPowerOfTwo(count) - 1;
        int bytesNeeded = mask < 0x100 ? 1 : (mask < 0x10000 ? 2 : 4);
        Span<byte> buf = stackalloc byte[4];

        while (true)
        {
            random.Fill(buf.Slice(0, bytesNeeded));
            int v = 0;
            for (int i = 0; i < bytesNeeded; i++) { v = (v << 8) | buf[i]; }
            v &= mask;
            if (v < count) { return v; }
            // else reject: masked value landed in [count, 2^k) — draw again to avoid modulo bias.
        }
    }

    private static int NextPowerOfTwo(int n)
    {
        int p = 1;
        while (p < n) { p <<= 1; }
        return p;
    }
}
