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

    [Fact]
    public void Join_WindowsDrivePath_Errors()
    {
        // Pin: a Windows drive-letter path must not be silently accepted as a base URL.
        // Without the "scheme appeared explicitly in input" guard, .NET's Uri.TryCreate
        // would parse "C:\Windows" as file:///C:/Windows on Windows hosts (drive letters
        // look like 1-character schemes), and the join would silently produce a file://
        // URL. Cross-platform consistency requires rejecting this regardless of host OS.
        var r = UrlJoiner.Join(@"C:\Windows", "foo");
        Assert.False(r.Success);
        Assert.Contains("base URL must be absolute", r.Error);
    }

    [Fact]
    public void Join_ExplicitFileUri_StillAccepted()
    {
        // Counterpart to the path-rejection cases: an EXPLICIT file:// scheme is still
        // a valid absolute URI per RFC 3986 and must continue to work. The guard rejects
        // Unix-path / drive-path auto-conversions; it does not reject file:// schemes.
        var r = UrlJoiner.Join("file:///tmp/base/", "child.txt");
        Assert.True(r.Success, $"expected success, got error: {r.Error}");
        Assert.Equal("file:///tmp/base/child.txt", r.Url);
    }

    // -- Round-3 review TA-I1 — pin the round-2 ws/wss allowlist addition. Without these
    //    tests, removing the line silently passes the suite. WebSocket URLs are
    //    hierarchical with authority semantics — `retry -- websocat "$(url join $WS_BASE chat)"`
    //    is a documented compose pattern. --
    [Fact]
    public void Join_WsBase_Accepted()
    {
        var r = UrlJoiner.Join("ws://ws.example.com/", "chat");
        Assert.True(r.Success, $"expected success, got error: {r.Error}");
        Assert.Equal("ws://ws.example.com/chat", r.Url);
    }

    [Fact]
    public void Join_WssBase_Accepted()
    {
        var r = UrlJoiner.Join("wss://ws.example.com/v1/", "chat");
        Assert.True(r.Success, $"expected success, got error: {r.Error}");
        Assert.Equal("wss://ws.example.com/v1/chat", r.Url);
    }

    [Fact]
    public void Join_AllowlistMessage_NamesAllAllowedSchemes()
    {
        // Pin the user-facing diagnostic — if a future refactor changes the allowlist,
        // the error message MUST stay in sync so users hitting an excluded scheme see
        // the actual current set rather than a stale one.
        var r = UrlJoiner.Join("urn:isbn:0451450523", "x");
        Assert.False(r.Success);
        Assert.NotNull(r.Error);
        Assert.Contains("scheme not allowed", r.Error, System.StringComparison.Ordinal);
        Assert.Contains("ws", r.Error, System.StringComparison.Ordinal);
        Assert.Contains("wss", r.Error, System.StringComparison.Ordinal);
        Assert.Contains("http", r.Error, System.StringComparison.Ordinal);
        Assert.Contains("https", r.Error, System.StringComparison.Ordinal);
    }
}
