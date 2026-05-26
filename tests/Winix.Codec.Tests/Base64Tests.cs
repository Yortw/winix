using System;
using Xunit;
using Winix.Codec;

namespace Winix.Codec.Tests;

public class Base64Tests
{
    [Theory]
    [InlineData(new byte[] { }, "")]
    [InlineData(new byte[] { 0x66, 0x6f }, "Zm8=")]
    [InlineData(new byte[] { 0x66, 0x6f, 0x6f }, "Zm9v")]
    [InlineData(new byte[] { 0x66, 0x6f, 0x6f, 0x62 }, "Zm9vYg==")]
    public void Encode_StandardAlphabet_MatchesRfc4648(byte[] input, string expected)
    {
        Assert.Equal(expected, Base64.Encode(input, urlSafe: false));
    }

    [Theory]
    [InlineData(new byte[] { 0xfb }, "-w==", "+w==")]
    [InlineData(new byte[] { 0xff, 0xef }, "_-8=", "/+8=")]
    public void Encode_UrlSafe_UsesDashAndUnderscore(byte[] input, string urlSafe, string standard)
    {
        Assert.Equal(urlSafe, Base64.Encode(input, urlSafe: true));
        Assert.Equal(standard, Base64.Encode(input, urlSafe: false));
    }

    [Fact]
    public void RoundTrip_RandomBytes_MatchesOriginal()
    {
        byte[] original = new byte[64];
        new Random(7).NextBytes(original);
        Assert.Equal(original, Base64.Decode(Base64.Encode(original, urlSafe: false)));
        Assert.Equal(original, Base64.Decode(Base64.Encode(original, urlSafe: true)));
    }
}
