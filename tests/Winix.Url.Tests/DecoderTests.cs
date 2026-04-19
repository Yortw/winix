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
}
