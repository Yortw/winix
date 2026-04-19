#nullable enable
using Xunit;
using Winix.Url;

namespace Winix.Url.Tests;

public class EncoderTests
{
    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("hello world", "hello%20world")]
    [InlineData("a/b", "a%2Fb")]
    [InlineData("key=value", "key%3Dvalue")]
    [InlineData("100%", "100%25")]
    [InlineData("a~b-c._d", "a~b-c._d")]
    public void Encode_ComponentMode(string input, string expected)
    {
        Assert.Equal(expected, Encoder.Encode(input, EncodeMode.Component, form: false));
    }

    [Fact]
    public void Encode_ComponentMode_UnicodeIsUtf8PercentEncoded()
    {
        // UTF-8 bytes for "日" are E6 97 A5
        Assert.Equal("%E6%97%A5", Encoder.Encode("日", EncodeMode.Component, form: false));
    }

    [Theory]
    [InlineData("a/b", "a/b")]
    [InlineData("foo bar/baz", "foo%20bar/baz")]
    [InlineData("/leading/trailing/", "/leading/trailing/")]
    [InlineData("segment with=equals", "segment%20with%3Dequals")]
    public void Encode_PathMode_SlashPreserved(string input, string expected)
    {
        Assert.Equal(expected, Encoder.Encode(input, EncodeMode.Path, form: false));
    }

    [Theory]
    [InlineData("hello world", "hello+world")]
    [InlineData("a+b", "a%2Bb")]
    [InlineData("a/b", "a%2Fb")]
    public void Encode_FormMode_SpaceToPlus(string input, string expected)
    {
        Assert.Equal(expected, Encoder.Encode(input, EncodeMode.Form, form: true));
    }

    [Fact]
    public void Encode_FormFlagEquivalentToFormMode()
    {
        Assert.Equal(
            Encoder.Encode("hello world", EncodeMode.Form, form: true),
            Encoder.Encode("hello world", EncodeMode.Component, form: true));
    }

    [Theory]
    [InlineData("hello world", "hello%20world")]
    [InlineData("a&b", "a%26b")]
    public void Encode_QueryMode(string input, string expected)
    {
        Assert.Equal(expected, Encoder.Encode(input, EncodeMode.Query, form: false));
    }

    [Fact]
    public void Encode_EmptyString_Empty()
    {
        Assert.Equal("", Encoder.Encode("", EncodeMode.Component, form: false));
    }

    // Regression: pre-encoded path input must round-trip without double-encoding.
    // Without this, `url parse | url build` produces corrupted output because
    // ParsedUrl.Path comes from uri.AbsolutePath (already %XX-encoded).
    [Theory]
    [InlineData("/a%20b", "/a%20b")]                  // pre-encoded space preserved
    [InlineData("/path/with%2Fslash", "/path/with%2Fslash")] // literal-slash-in-segment preserved
    [InlineData("/100%", "/100%25")]                  // bare % (not a valid escape) → encoded
    [InlineData("/a%20b%", "/a%20b%25")]              // mix: valid escape kept, bare % encoded
    [InlineData("/a b/c", "/a%20b/c")]                // plain input encoded
    [InlineData("/a%2zb", "/a%252zb")]                // %2z is not a valid escape → encoded
    public void Encode_PathMode_PreservesExistingEscapes(string input, string expected)
    {
        Assert.Equal(expected, Encoder.Encode(input, EncodeMode.Path, form: false));
    }

    [Fact]
    public void Encode_PathMode_NormalisesExistingEscapeCaseToUppercase()
    {
        // RFC 3986 §6.2.2.1: hex digits in percent-encoded triplets should be uppercase.
        Assert.Equal("/a%A0b", Encoder.Encode("/a%a0b", EncodeMode.Path, form: false));
        Assert.Equal("/a%A0b", Encoder.Encode("/a%A0b", EncodeMode.Path, form: false));
    }
}
