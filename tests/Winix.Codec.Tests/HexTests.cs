using System;
using Xunit;
using Winix.Codec;

namespace Winix.Codec.Tests;

public class HexTests
{
    [Theory]
    [InlineData(new byte[] { }, "")]
    [InlineData(new byte[] { 0x00 }, "00")]
    [InlineData(new byte[] { 0xff }, "ff")]
    [InlineData(new byte[] { 0x01, 0x23, 0x45, 0x67 }, "01234567")]
    [InlineData(new byte[] { 0xde, 0xad, 0xbe, 0xef }, "deadbeef")]
    public void Encode_KnownVectors_LowercaseByDefault(byte[] input, string expected)
    {
        Assert.Equal(expected, Hex.Encode(input));
    }

    [Fact]
    public void Encode_Uppercase_ProducesUppercaseHex()
    {
        Assert.Equal("DEADBEEF", Hex.Encode(new byte[] { 0xde, 0xad, 0xbe, 0xef }, upper: true));
    }

    [Fact]
    public void Decode_RoundTripsEncode()
    {
        byte[] original = new byte[32];
        new Random(42).NextBytes(original);
        Assert.Equal(original, Hex.Decode(Hex.Encode(original)));
    }

    [Theory]
    [InlineData("AbCdEf")]
    [InlineData("abcdef")]
    [InlineData("ABCDEF")]
    public void Decode_MixedCase_Accepted(string input)
    {
        Assert.Equal(new byte[] { 0xab, 0xcd, 0xef }, Hex.Decode(input));
    }

    [Theory]
    [InlineData("abc")]        // odd length
    [InlineData("zz")]         // non-hex chars
    [InlineData("ab cd")]      // whitespace
    public void Decode_Invalid_Throws(string input)
    {
        Assert.Throws<FormatException>(() => Hex.Decode(input));
    }
}
