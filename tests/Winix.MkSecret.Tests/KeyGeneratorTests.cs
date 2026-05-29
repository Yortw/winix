using Winix.Codec;
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class KeyGeneratorTests
{
    private static MkSecretOptions Opts(int bytes, KeyEncoding enc) =>
        MkSecretOptions.Defaults with { Mode = SecretMode.Key, Bytes = bytes, Encoding = enc };

    [Fact]
    public void Hex_encodes_the_drawn_bytes()
    {
        var rng = new SequenceRandom(0xDE, 0xAD, 0xBE, 0xEF);
        var gen = new KeyGenerator(rng);
        Assert.Equal("deadbeef", gen.Generate(Opts(4, KeyEncoding.Hex)));
    }

    [Fact]
    public void Base64Url_matches_a_known_vector()
    {
        // Defence-in-depth wire pin. Only ONE literal-pinned encoding test lives here: the byte-level
        // correctness of every encoder is already pinned in Winix.Codec.Tests (Base64Tests /
        // Base32CrockfordTests / HexTests). These KeyGenerator tests need only confirm the correct
        // encoder is wired and that base64url is emitted unpadded. Vector: bytes 0x01..0x10 ->
        // unpadded base64url, computed with Python base64.urlsafe_b64encode (independent oracle).
        byte[] src = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var gen = new KeyGenerator(new SequenceRandom(src));
        Assert.Equal("AQIDBAUGBwgJCgsMDQ4PEA", gen.Generate(Opts(src.Length, KeyEncoding.Base64Url)));
    }

    [Fact]
    public void Base64Url_has_no_padding()
    {
        var rng = new SequenceRandom(new byte[32]); // 32 zero bytes
        var gen = new KeyGenerator(rng);
        string s = gen.Generate(Opts(32, KeyEncoding.Base64Url));
        Assert.DoesNotContain('=', s);
        Assert.DoesNotContain('+', s);
        Assert.DoesNotContain('/', s);
    }

    [Theory]
    [InlineData(KeyEncoding.Hex)]
    [InlineData(KeyEncoding.Base64)]
    [InlineData(KeyEncoding.Base64Url)]
    [InlineData(KeyEncoding.Base32)]
    public void Round_trips_back_to_the_original_bytes(KeyEncoding enc)
    {
        byte[] src = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var gen = new KeyGenerator(new SequenceRandom(src));
        string s = gen.Generate(Opts(src.Length, enc));
        byte[] back = enc switch
        {
            KeyEncoding.Hex => Hex.Decode(s),
            KeyEncoding.Base64 => Base64.Decode(s),
            // Re-add stripped padding before decoding — Base64.Decode requires it.
            KeyEncoding.Base64Url => Base64.Decode(s.PadRight((s.Length + 3) / 4 * 4, '=')),
            KeyEncoding.Base32 => Base32Crockford.Decode(s),
            _ => throw new System.NotSupportedException(),
        };
        Assert.Equal(src, back);
    }
}
