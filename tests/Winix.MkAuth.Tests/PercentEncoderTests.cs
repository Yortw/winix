using Winix.MkAuth;
using Xunit;

public class PercentEncoderTests
{
    [Theory]
    [InlineData("abcDEF123", "abcDEF123")]   // unreserved alnum untouched
    [InlineData("-._~", "-._~")]              // RFC 3986 unreserved symbols untouched
    [InlineData(" ", "%20")]                  // space is %20, NOT '+'
    [InlineData("!*'()", "%21%2A%27%28%29")] // sub-delims that OAuth1 requires encoded
    [InlineData("a+b=c&d", "a%2Bb%3Dc%26d")]
    [InlineData("å/ä", "%C3%A5%2F%C3%A4")]   // UTF-8 bytes, uppercase hex
    public void Encodes_per_rfc3986(string input, string expected)
    {
        Assert.Equal(expected, PercentEncoder.Encode(input));
    }
}
