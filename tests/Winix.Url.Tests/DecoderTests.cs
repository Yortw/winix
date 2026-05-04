#nullable enable
using Xunit;
using Winix.Url;

namespace Winix.Url.Tests;

public class DecoderTests
{
    [Theory]
    [InlineData("hello%20world", "hello world")]
    [InlineData("a+b", "a+b")]
    [InlineData("%E6%97%A5", "日")]
    [InlineData("100%25", "100%")]
    [InlineData("", "")]
    public void Decode_DefaultMode_PlusIsLiteral(string input, string expected)
    {
        Assert.Equal(expected, Decoder.Decode(input, form: false));
    }

    [Theory]
    [InlineData("hello+world", "hello world")]
    [InlineData("hello%20world", "hello world")]
    [InlineData("a%2Bb", "a+b")]
    [InlineData("a%2Bb+c", "a+b c")]
    public void Decode_FormMode_PlusIsSpace(string input, string expected)
    {
        Assert.Equal(expected, Decoder.Decode(input, form: true));
    }

    [Fact]
    public void Decode_MalformedEscape_PreservesLiteralPercent()
    {
        // Uri.UnescapeDataString tolerates invalid escape sequences by leaving them literal.
        // Lock this behaviour in — users may rely on "garbage passes through".
        Assert.Equal("a%ZZb", Decoder.Decode("a%ZZb", form: false));
    }

    // -- Round-2 review TA-I1 — DecodeStrict library-level pins. The CliTests cover
    //    the exit-code routing but the boundary logic (i+2 >= length, IsHex range,
    //    consecutive %XX advancement, first-malformed-position reporting) belongs at
    //    the library tier so a future Span<char> refactor can't silently move
    //    detection to the LAST malformed position and pass every existing test. --

    [Fact]
    public void DecodeStrict_TrailingPercent_ReportsAtPosition()
    {
        var r = Decoder.DecodeStrict("abc%", form: false);
        Assert.False(r.Success);
        Assert.NotNull(r.Error);
        Assert.Contains("position 3", r.Error, System.StringComparison.Ordinal);
        Assert.Contains("incomplete", r.Error, System.StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeStrict_PartialPercentAtEnd_ReportsAsIncomplete()
    {
        // %X with one hex digit at end of input — same "incomplete" path as the trailing %.
        var r = Decoder.DecodeStrict("abc%5", form: false);
        Assert.False(r.Success);
        Assert.Contains("incomplete", r.Error, System.StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeStrict_MidStringNonHex_ReportsAtFirstOffender()
    {
        // %ZZ in the middle followed by a valid %20 — the FIRST malformed position must be
        // reported, not the last. A regression that walks past the first error and reports
        // the last would fail this pin.
        var r = Decoder.DecodeStrict("abc%ZZ def%20ghi", form: false);
        Assert.False(r.Success);
        Assert.Contains("position 3", r.Error, System.StringComparison.Ordinal);
        Assert.Contains("not valid hex", r.Error, System.StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeStrict_BackToBackValidEscapes_AcceptedAndDecoded()
    {
        // Verifies the i += 2 advance correctly skips both hex digits without re-checking
        // the second hex char as a percent. Pins the success path through consecutive %XX.
        var r = Decoder.DecodeStrict("%20%21%22", form: false);
        Assert.True(r.Success);
        Assert.Equal(" !\"", r.Value);
    }

    [Fact]
    public void DecodeStrict_FormMode_PlusBecomesSpaceAfterValidation()
    {
        // form: true should still validate percent-escapes BEFORE replacing + with space.
        var r = Decoder.DecodeStrict("a+b%20c", form: true);
        Assert.True(r.Success);
        Assert.Equal("a b c", r.Value);
    }

    [Fact]
    public void DecodeStrict_HexCaseInsensitive()
    {
        // Lowercase + uppercase hex digits are both valid per RFC 3986.
        Assert.True(Decoder.DecodeStrict("%aF", form: false).Success);
        Assert.True(Decoder.DecodeStrict("%Af", form: false).Success);
        Assert.True(Decoder.DecodeStrict("%af", form: false).Success);
        Assert.True(Decoder.DecodeStrict("%AF", form: false).Success);
    }

    [Fact]
    public void DecodeStrict_EmptyInput_Succeeds()
    {
        var r = Decoder.DecodeStrict("", form: false);
        Assert.True(r.Success);
        Assert.Equal("", r.Value);
    }

    [Fact]
    public void DecodeStrict_NoPercentSequences_PassesThrough()
    {
        var r = Decoder.DecodeStrict("plain text no escapes", form: false);
        Assert.True(r.Success);
        Assert.Equal("plain text no escapes", r.Value);
    }
}
