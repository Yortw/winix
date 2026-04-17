using System;
using Xunit;
using Winix.Codec;

namespace Winix.Codec.Tests;

public class Base32CrockfordTests
{
    [Fact]
    public void Encode_EmptyInput_ReturnsEmptyString()
    {
        Assert.Equal("", Base32Crockford.Encode(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Encode_SingleByte_ProducesTwoChars()
    {
        // 0x00 in 8 bits, padded to 10 bits with trailing zeros = "00"
        Assert.Equal("00", Base32Crockford.Encode(new byte[] { 0x00 }));
    }

    [Fact]
    public void Encode_UlidPayload_Produces26Chars()
    {
        // ULID-sized input → standard 26-char output (the canonical ULID length).
        var payload = new byte[16];
        Assert.Equal(26, Base32Crockford.Encode(payload).Length);
    }

    [Fact]
    public void Encode_AllZeros_ProducesAllZeroChars()
    {
        var payload = new byte[16];
        Assert.Equal(new string('0', 26), Base32Crockford.Encode(payload));
    }

    [Fact]
    public void Encode_AllOnes_ProducesExpectedString()
    {
        // 16 bytes = 128 bits; 26 chars × 5 bits = 130 bits, so the last char
        // encodes only 3 real bits (all 1s) left-shifted 2 to fill 5 → 0b11100 = 28 = 'W'.
        // The first 25 chars are all 'Z' (31 = 0b11111).
        var payload = new byte[16];
        Array.Fill(payload, (byte)0xFF);
        Assert.Equal(new string('Z', 25) + "W", Base32Crockford.Encode(payload));
    }

    [Fact]
    public void Decode_UppercaseInput_RoundTripsThroughEncode()
    {
        byte[] original = new byte[16];
        new Random(42).NextBytes(original);
        var encoded = Base32Crockford.Encode(original);
        var decoded = Base32Crockford.Decode(encoded);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Decode_LowercaseInput_RoundTripsThroughEncode()
    {
        byte[] original = new byte[16];
        new Random(7).NextBytes(original);
        var encoded = Base32Crockford.Encode(original);
        var decoded = Base32Crockford.Decode(encoded.ToLowerInvariant());
        Assert.Equal(original, decoded);
    }

    [Theory]
    [InlineData("I", "1")]
    [InlineData("L", "1")]
    [InlineData("O", "0")]
    [InlineData("i", "1")]
    [InlineData("l", "1")]
    [InlineData("o", "0")]
    public void Decode_CrockfordAliases_AreAcceptedAsMappedDigit(string alias, string canonical)
    {
        // Crockford allows I/L → 1 and O → 0 on input for human-entry tolerance.
        Assert.Equal(Base32Crockford.Decode(canonical), Base32Crockford.Decode(alias));
    }

    [Theory]
    [InlineData("U")]
    [InlineData(" ")]
    [InlineData("-")]
    [InlineData("!")]
    public void Decode_InvalidChar_Throws(string invalid)
    {
        Assert.Throws<FormatException>(() => Base32Crockford.Decode(invalid));
    }
}
