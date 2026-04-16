using Xunit;
using Winix.Clip;

namespace Winix.Clip.Tests;

public class NewlineStrippingTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("foo", "foo")]
    [InlineData("foo\n", "foo")]
    [InlineData("foo\r\n", "foo")]
    [InlineData("foo\r", "foo\r")]                // bare \r is not stripped
    [InlineData("foo\n\n", "foo\n")]              // only one stripped
    [InlineData("foo\r\n\r\n", "foo\r\n")]        // only the last CRLF stripped
    [InlineData("foo\nbar\n", "foo\nbar")]        // internal newline preserved
    [InlineData("\n", "")]                        // single newline → empty
    [InlineData("\r\n", "")]                      // single CRLF → empty
    [InlineData("a", "a")]
    [InlineData(null, null)]
    public void StripTrailingNewline_MatchesExpected(string? input, string? expected)
    {
        string? result = NewlineStripping.StripTrailingNewline(input);
        Assert.Equal(expected, result);
    }
}
