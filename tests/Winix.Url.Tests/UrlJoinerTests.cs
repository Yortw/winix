#nullable enable
using Xunit;
using Winix.Url;

namespace Winix.Url.Tests;

public class UrlJoinerTests
{
    // RFC 3986 §5.4 "normal examples" — subset exercising each resolution category.
    [Theory]
    [InlineData("http://a/b/c/d;p?q", "g", "http://a/b/c/g")]
    [InlineData("http://a/b/c/d;p?q", "./g", "http://a/b/c/g")]
    [InlineData("http://a/b/c/d;p?q", "g/", "http://a/b/c/g/")]
    [InlineData("http://a/b/c/d;p?q", "/g", "http://a/g")]
    [InlineData("http://a/b/c/d;p?q", "//g", "http://g/")]
    [InlineData("http://a/b/c/d;p?q", "?y", "http://a/b/c/d;p?y")]
    [InlineData("http://a/b/c/d;p?q", "#s", "http://a/b/c/d;p?q#s")]
    [InlineData("http://a/b/c/d;p?q", "", "http://a/b/c/d;p?q")]
    [InlineData("http://a/b/c/d;p?q", ".", "http://a/b/c/")]
    [InlineData("http://a/b/c/d;p?q", "..", "http://a/b/")]
    [InlineData("http://a/b/c/d;p?q", "../", "http://a/b/")]
    [InlineData("http://a/b/c/d;p?q", "../g", "http://a/b/g")]
    [InlineData("http://a/b/c/d;p?q", "../..", "http://a/")]
    [InlineData("http://a/b/c/d;p?q", "../../", "http://a/")]
    public void Join_Rfc3986Examples(string baseUrl, string relative, string expected)
    {
        var r = UrlJoiner.Join(baseUrl, relative);
        Assert.True(r.Success, $"expected success, got error: {r.Error}");
        Assert.Equal(expected, r.Url);
    }

    [Fact]
    public void Join_AbsoluteRelativeOverridesBase()
    {
        var r = UrlJoiner.Join("https://example.com/api/", "https://other.com/path");
        Assert.True(r.Success);
        Assert.Equal("https://other.com/path", r.Url);
    }

    [Fact]
    public void Join_BaseNotAbsolute_Errors()
    {
        var r = UrlJoiner.Join("/relative/base", "foo");
        Assert.False(r.Success);
        Assert.Contains("base URL must be absolute", r.Error);
    }

    [Fact]
    public void Join_BothInvalid_Errors()
    {
        var r = UrlJoiner.Join("not a url", "also not a url");
        Assert.False(r.Success);
    }
}
