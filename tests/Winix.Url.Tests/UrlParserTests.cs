#nullable enable
using Xunit;
using Winix.Url;

namespace Winix.Url.Tests;

public class UrlParserTests
{
    [Fact]
    public void Parse_FullAbsoluteUrl_AllFieldsExtracted()
    {
        var result = UrlParser.Parse("https://user:pw@api.example.com:8443/v1/users?q=hello&limit=10#top");
        Assert.True(result.Success);
        var p = result.Url!;
        Assert.Equal("https", p.Scheme);
        Assert.Equal("user:pw", p.UserInfo);
        Assert.Equal("api.example.com", p.Host);
        Assert.Equal(8443, p.Port);
        Assert.Equal("/v1/users", p.Path);
        Assert.Equal("top", p.Fragment);
        Assert.Equal(2, p.QueryPairs.Count);
        Assert.Equal(("q", "hello"), p.QueryPairs[0]);
        Assert.Equal(("limit", "10"), p.QueryPairs[1]);
    }

    [Fact]
    public void Parse_DefaultPortForHttps_NormalisedToNull()
    {
        var result = UrlParser.Parse("https://example.com:443/path");
        Assert.Null(result.Url!.Port);
    }

    [Fact]
    public void Parse_NoExplicitPort_NormalisedToNull()
    {
        var result = UrlParser.Parse("https://example.com/path");
        Assert.Null(result.Url!.Port);
    }

    [Fact]
    public void Parse_ExplicitNonDefaultPort_Preserved()
    {
        var result = UrlParser.Parse("https://example.com:8443/path");
        Assert.Equal(8443, result.Url!.Port);
    }

    [Fact]
    public void Parse_DuplicateQueryKeys_BothPreservedInOrder()
    {
        var result = UrlParser.Parse("https://x.io/?a=1&b=2&a=3");
        Assert.Equal(3, result.Url!.QueryPairs.Count);
        Assert.Equal(("a", "1"), result.Url.QueryPairs[0]);
        Assert.Equal(("b", "2"), result.Url.QueryPairs[1]);
        Assert.Equal(("a", "3"), result.Url.QueryPairs[2]);
    }

    [Fact]
    public void Parse_EmptyQuery_EmptyPairsList()
    {
        var result = UrlParser.Parse("https://x.io/path");
        Assert.Empty(result.Url!.QueryPairs);
    }

    [Fact]
    public void Parse_NoFragment_FragmentIsNull()
    {
        var result = UrlParser.Parse("https://x.io/path");
        Assert.Null(result.Url!.Fragment);
    }

    [Fact]
    public void Parse_NoUserInfo_UserInfoIsNull()
    {
        var result = UrlParser.Parse("https://x.io/path");
        Assert.Null(result.Url!.UserInfo);
    }

    [Fact]
    public void Parse_Invalid_ReturnsFailure()
    {
        var result = UrlParser.Parse("not a url");
        Assert.False(result.Success);
        Assert.Contains("invalid URL", result.Error);
    }

    [Fact]
    public void Parse_PercentDecodedQueryValues()
    {
        var result = UrlParser.Parse("https://x.io/p?q=hello%20world");
        Assert.Equal(("q", "hello world"), result.Url!.QueryPairs[0]);
    }

    [Fact]
    public void Parse_FragmentDecoded()
    {
        var result = UrlParser.Parse("https://x.io/#section%20one");
        Assert.Equal("section one", result.Url!.Fragment);
    }

    [Fact]
    public void Parse_AdjacentAmpersands_EmptyPairsSkipped()
    {
        // Third-review fix: "?a=1&&b=2" should NOT produce a spurious ("", "") pair.
        // Emitting empty-key pairs would round-trip as "a=1&=&b=2" — data distortion.
        var result = UrlParser.Parse("https://x.io/?a=1&&b=2");
        Assert.True(result.Success);
        Assert.Equal(2, result.Url!.QueryPairs.Count);
        Assert.Equal(("a", "1"), result.Url.QueryPairs[0]);
        Assert.Equal(("b", "2"), result.Url.QueryPairs[1]);
    }

    [Fact]
    public void Parse_PreservesRawQuery()
    {
        // Third-review fix: raw query string preserved so --field query round-trips faithfully.
        var result = UrlParser.Parse("https://x.io/?q=hello%20world&limit=10");
        Assert.True(result.Success);
        Assert.Equal("q=hello%20world&limit=10", result.Url!.RawQuery);
    }

    [Fact]
    public void Parse_EmptyQuery_RawQueryIsEmptyString()
    {
        var result = UrlParser.Parse("https://x.io/path");
        Assert.Equal("", result.Url!.RawQuery);
    }
}
