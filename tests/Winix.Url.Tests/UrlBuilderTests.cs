#nullable enable
using Xunit;
using Winix.Url;

namespace Winix.Url.Tests;

public class UrlBuilderTests
{
    [Fact]
    public void Build_HostOnly_DefaultsToHttpsWithTrailingSlash()
    {
        var r = UrlBuilder.Build(scheme: null, host: "example.com", port: null, path: null,
            query: System.Array.Empty<(string, string)>(), fragment: null, raw: false);
        Assert.True(r.Success);
        Assert.Equal("https://example.com/", r.Url);
    }

    [Fact]
    public void Build_WithPath_SlashesPreserved()
    {
        var r = UrlBuilder.Build("https", "example.com", null, "/v1/users",
            System.Array.Empty<(string, string)>(), null, false);
        Assert.Equal("https://example.com/v1/users", r.Url);
    }

    [Fact]
    public void Build_PathMissingLeadingSlash_SlashAdded()
    {
        var r = UrlBuilder.Build("https", "example.com", null, "v1/users",
            System.Array.Empty<(string, string)>(), null, false);
        Assert.Equal("https://example.com/v1/users", r.Url);
    }

    [Fact]
    public void Build_PathWithSpace_EncodedAsPath()
    {
        var r = UrlBuilder.Build("https", "example.com", null, "/a b/c",
            System.Array.Empty<(string, string)>(), null, false);
        Assert.Equal("https://example.com/a%20b/c", r.Url);
    }

    [Fact]
    public void Build_QueryPairs_FormEncoded()
    {
        var r = UrlBuilder.Build("https", "example.com", null, "/search",
            new (string, string)[] { ("q", "hello world"), ("limit", "10") }, null, false);
        Assert.Equal("https://example.com/search?q=hello+world&limit=10", r.Url);
    }

    [Fact]
    public void Build_NonDefaultPort_Included()
    {
        var r = UrlBuilder.Build("https", "example.com", 8443, "/",
            System.Array.Empty<(string, string)>(), null, false);
        Assert.Equal("https://example.com:8443/", r.Url);
    }

    [Fact]
    public void Build_DefaultPort_OmittedUnlessRaw()
    {
        var r = UrlBuilder.Build("https", "example.com", 443, "/",
            System.Array.Empty<(string, string)>(), null, false);
        Assert.Equal("https://example.com/", r.Url);
    }

    [Fact]
    public void Build_Fragment_Appended()
    {
        var r = UrlBuilder.Build("https", "example.com", null, "/",
            System.Array.Empty<(string, string)>(), "top", false);
        Assert.Equal("https://example.com/#top", r.Url);
    }

    [Fact]
    public void Build_MissingHost_Errors()
    {
        var r = UrlBuilder.Build("https", host: "", port: null, path: null,
            query: System.Array.Empty<(string, string)>(), fragment: null, raw: false);
        Assert.False(r.Success);
        Assert.Contains("host", r.Error);
    }

    [Fact]
    public void Build_InvalidPort_Errors()
    {
        var r = UrlBuilder.Build("https", "example.com", port: 99999, path: null,
            query: System.Array.Empty<(string, string)>(), fragment: null, raw: false);
        Assert.False(r.Success);
        Assert.Contains("port", r.Error);
    }

    [Fact]
    public void Build_Raw_PreservesDefaultPort()
    {
        var r = UrlBuilder.Build("https", "example.com", 443, "/",
            System.Array.Empty<(string, string)>(), null, raw: true);
        Assert.True(r.Success);
        Assert.Contains(":443", r.Url);
    }
}
