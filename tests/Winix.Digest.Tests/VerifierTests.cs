#nullable enable
using Xunit;
using Winix.Digest;

namespace Winix.Digest.Tests;

public class VerifierTests
{
    [Fact]
    public void Verify_HexMatch_ReturnsTrue()
    {
        string expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        byte[] computed = Winix.Codec.Hex.Decode(expected);
        Assert.True(Verifier.Verify(computed, expected, OutputFormat.Hex));
    }

    [Fact]
    public void Verify_HexMismatch_ReturnsFalse()
    {
        string expected = "0000000000000000000000000000000000000000000000000000000000000000";
        byte[] computed = Winix.Codec.Hex.Decode("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        Assert.False(Verifier.Verify(computed, expected, OutputFormat.Hex));
    }

    [Fact]
    public void Verify_HexCaseInsensitive()
    {
        byte[] computed = new byte[] { 0xab, 0xcd, 0xef };
        Assert.True(Verifier.Verify(computed, "abcdef", OutputFormat.Hex));
        Assert.True(Verifier.Verify(computed, "ABCDEF", OutputFormat.Hex));
        Assert.True(Verifier.Verify(computed, "AbCdEf", OutputFormat.Hex));
    }

    [Fact]
    public void Verify_Base64_CaseSensitive()
    {
        byte[] computed = new byte[] { 0x66, 0x6f, 0x6f }; // "foo"
        Assert.True(Verifier.Verify(computed, "Zm9v", OutputFormat.Base64));
        // Base64 alphabet is case-sensitive — lower-case "zm9v" is a different value entirely.
        Assert.False(Verifier.Verify(computed, "zm9v", OutputFormat.Base64));
    }

    [Fact]
    public void Verify_Base64Url_Matches_And_RejectsStandardWhenSlashOrPlusAppears()
    {
        // Choose bytes that produce a + or / in standard base64 so URL-safe encoding differs.
        byte[] computed = new byte[] { 0xff, 0xfe, 0xfd, 0xfc };
        string urlSafe = Winix.Codec.Base64.Encode(computed, urlSafe: true);
        string standard = Winix.Codec.Base64.Encode(computed, urlSafe: false);
        Assert.NotEqual(urlSafe, standard);
        Assert.True(Verifier.Verify(computed, urlSafe, OutputFormat.Base64Url));
        Assert.False(Verifier.Verify(computed, standard, OutputFormat.Base64Url));
    }

    [Fact]
    public void Verify_Base32_MatchesCrockfordUpperCase()
    {
        byte[] computed = new byte[] { 0x12, 0x34, 0x56, 0x78 };
        string expected = Winix.Codec.Base32Crockford.Encode(computed);
        Assert.True(Verifier.Verify(computed, expected, OutputFormat.Base32));
    }

    [Fact]
    public void Verify_NullExpected_ReturnsFalse()
    {
        Assert.False(Verifier.Verify(new byte[] { 0x00 }, null!, OutputFormat.Hex));
    }
}
