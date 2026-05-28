using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class SamplingTests
{
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
