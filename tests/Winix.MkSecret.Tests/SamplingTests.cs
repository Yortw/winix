using Winix.Codec;
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class SamplingTests
{
    [Fact]
    public void UniformIndex_distributes_evenly_over_a_non_power_of_two_range()
    {
        // Witnesses unbiased distribution on the WORST-CASE non-power-of-two charset: 94 (full ASCII).
        // mask=127, so 34 of 128 masked values get rejected — exactly the path a modulo fold (% 94)
        // would corrupt by double-counting buckets 0..33. Band rationale: at N=100k over 94 buckets the
        // expected count is ~1064 with σ≈√(N·p·(1-p))≈33, so ±25% (~266) is ~8σ — a correct CSPRNG never
        // flakes, but a modulo fold doubles the low third and blows straight through the band.
        // DEFERRED (adversarial review F5): distribution is witnessed only at N=94 by design. 7776 (phrase)
        // and the other charsets share the identical UniformIndex code path and rely on the existing
        // scripted boundary/rejection tests below; per-charset distribution coverage is deliberately omitted.
        const int count = 94;
        const int draws = 100_000;
        double expected = (double)draws / count;
        double tolerance = expected * 0.25;

        int[] buckets = new int[count];
        for (int i = 0; i < draws; i++)
        {
            int idx = Sampling.UniformIndex(SecureRandom.Default, count);
            Assert.InRange(idx, 0, count - 1);
            buckets[idx]++;
        }

        for (int b = 0; b < count; b++)
        {
            Assert.InRange(buckets[b], expected - tolerance, expected + tolerance);
        }
    }

    [Fact]
    public void UniformIndex_returns_masked_value_in_range()
    {
        // count=62 -> mask=63 -> 1 byte. Byte 5 is in [0,62) -> index 5.
        var rng = new SequenceRandom(5);
        Assert.Equal(5, Sampling.UniformIndex(rng, 62));
    }

    [Fact]
    public void UniformIndex_rejects_out_of_range_draws_no_modulo_bias()
    {
        // count=62 -> mask=63. Bytes 62 and 63 survive the mask but are >= 62, so they MUST be
        // rejected (a modulo-folding bug would map them to 0 and 1). Then 7 -> index 7.
        var rng = new SequenceRandom(62, 63, 7);
        Assert.Equal(7, Sampling.UniformIndex(rng, 62));
    }

    [Fact]
    public void UniformIndex_handles_ranges_above_one_byte()
    {
        // count=7776 -> mask=8191 -> 2 bytes, big-endian. 0x00,0x05 -> 5.
        var rng = new SequenceRandom(0x00, 0x05);
        Assert.Equal(5, Sampling.UniformIndex(rng, 7776));
    }

    [Fact]
    public void UniformIndex_rejects_at_the_multibyte_boundary()
    {
        // SECURITY-CRITICAL: this is the path the phrase word-selection uses. count=7776 -> mask=8191.
        // 0x1F,0xFF = 8191 survives the mask but is >= 7776, so it MUST be rejected (a modulo-folding
        // bug would map it into range). Then 0x00,0x05 -> 5.
        var rng = new SequenceRandom(0x1F, 0xFF, 0x00, 0x05);
        Assert.Equal(5, Sampling.UniformIndex(rng, 7776));
    }
}
