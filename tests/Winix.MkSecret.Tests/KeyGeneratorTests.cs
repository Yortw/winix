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
    public void Base64_and_Base32_match_known_vectors_proving_the_right_codec_is_wired()
    {
        // Wire-correctness pin: confirms KeyGenerator routes each encoding to the codec a real
        // counterpart would use. The vector is chosen to DISCRIMINATE std base64 from base64url —
        // 0xFB/0xFF/0xBF force '+'/'/' (std) vs '-'/'_' (url), and the 5-byte length forces a '='
        // pad on std — so a base64/base64url swap can never pass. Expected strings computed with an
        // independent oracle (Python base64 + a from-scratch Crockford base32 encoder), not from our
        // embedded codec. mksecret emits base64url UNPADDED (TrimEnd('=')), std base64 WITH padding.
        byte[] src = { 0xFB, 0xFF, 0xBF, 0x01, 0x02 };
        const string expectedBase64 = "+/+/AQI=";
        const string expectedBase64Url = "-_-_AQI";
        const string expectedBase32 = "ZFZVY082";

        // Guard: if the vector ever stops discriminating, fail at authoring time rather than passing silently.
        Assert.NotEqual(expectedBase64, expectedBase64Url);

        Assert.Equal(expectedBase64, new KeyGenerator(new SequenceRandom(src)).Generate(Opts(src.Length, KeyEncoding.Base64)));
        Assert.Equal(expectedBase64Url, new KeyGenerator(new SequenceRandom(src)).Generate(Opts(src.Length, KeyEncoding.Base64Url)));
        Assert.Equal(expectedBase32, new KeyGenerator(new SequenceRandom(src)).Generate(Opts(src.Length, KeyEncoding.Base32)));
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
