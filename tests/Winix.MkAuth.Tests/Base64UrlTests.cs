using System.Text;
using Winix.MkAuth;
using Xunit;

public class Base64UrlTests
{
    [Theory]
    // base64url of ASCII, no padding, '-'/'_' instead of '+'/'/'
    [InlineData("", "")]
    [InlineData("f", "Zg")]
    [InlineData("fo", "Zm8")]
    [InlineData("foo", "Zm9v")]
    [InlineData("foob", "Zm9vYg")]
    public void Encodes_no_padding(string ascii, string expected)
    {
        Assert.Equal(expected, Base64Url.EncodeNoPad(Encoding.ASCII.GetBytes(ascii)));
    }

    [Fact]
    public void Uses_url_alphabet()
    {
        // 0xFF 0xFE 0xFD -> standard base64 "//79" -> url "__79"
        Assert.Equal("__79", Base64Url.EncodeNoPad(new byte[] { 0xFF, 0xFE, 0xFD }));
    }
}
