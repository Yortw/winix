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

    [Fact]
    public void Build_Raw_StillValidatesSyntax()
    {
        // --raw must not skip syntactic validation. A host with a literal space is not a legal URI.
        // Round-1 review CR-I2 / Round-2 review SFH-I2 — host validation now fires earlier with
        // a specific error. Round 1 introduced "URL-component separator" for the structural
        // chars (/ ? # @); round 2 moved whitespace to a separate "control or whitespace
        // character" branch with position + hex code. The contract being pinned (raw doesn't
        // skip validation, bad host rejected) is unchanged across both rounds; the error just
        // got more actionable.
        var r = UrlBuilder.Build("https", "foo bar.com", null, "/",
            System.Array.Empty<(string, string)>(), null, raw: true);
        Assert.False(r.Success);
        Assert.NotNull(r.Error);
        // Any of the rejection wordings satisfies the contract; pin breadth, not specifics.
        Assert.True(
            r.Error!.Contains("URL-component separator", System.StringComparison.Ordinal)
            || r.Error.Contains("control or whitespace character", System.StringComparison.Ordinal)
            || r.Error.Contains("invalid URL", System.StringComparison.Ordinal),
            $"expected one of the host-rejection wordings in error, got: {r.Error}");
    }

    [Fact]
    public void Build_WithUserInfo_InjectedBetweenSchemeAndHost()
    {
        var r = UrlBuilder.Build("https", "x.io", null, "/api",
            System.Array.Empty<(string, string)>(), null, raw: false,
            userInfo: "user:pw");
        Assert.True(r.Success);
        Assert.Contains("user:pw@", r.Url);
        Assert.Contains("x.io", r.Url);
    }

    [Fact]
    public void Build_PreEncodedPath_NotDoubleEncoded()
    {
        // Regression: feeding back a parsed path (already %XX-encoded) should not produce %25XX.
        var r = UrlBuilder.Build("https", "x.io", null, "/a%20b",
            System.Array.Empty<(string, string)>(), null, raw: false);
        Assert.True(r.Success);
        Assert.Equal("https://x.io/a%20b", r.Url);
    }

    // -- Round-2 review TA-I2 — pin the Uri.CheckHostName secondary-rejection branch.
    //    The char-loop catches obvious separators and control chars, but a host like
    //    "foo..bar" (empty label, valid chars otherwise) passes the loop and must be
    //    rejected by CheckHostName. Without a test, removing/inverting that branch
    //    would silently allow malformed-but-printable hosts. --
    [Fact]
    public void Build_HostFailsCheckHostName_RejectsAsNotWellFormed()
    {
        // "foo..bar" has an empty DNS label between the dots — valid chars throughout but
        // not a well-formed hostname per Uri.CheckHostName.
        var r = UrlBuilder.Build("https", "foo..bar", null, "/",
            System.Array.Empty<(string, string)>(), null, raw: false);
        Assert.False(r.Success);
        Assert.NotNull(r.Error);
        Assert.Contains("not a well-formed hostname", r.Error, System.StringComparison.Ordinal);
    }

    // -- Round-2 review SFH-I2 — pin the control-char rejection path so a host with an
    //    invisible \v or \0 surfaces with position + hex code, NOT silently stripped by
    //    Uri.CheckHostName which would echo back the WRONG host string in any error. --
    [Fact]
    public void Build_HostWithVerticalTab_RejectsWithPositionAndHex()
    {
        var r = UrlBuilder.Build("https", "evil\vcom", null, "/",
            System.Array.Empty<(string, string)>(), null, raw: false);
        Assert.False(r.Success);
        Assert.NotNull(r.Error);
        Assert.Contains("control or whitespace character", r.Error, System.StringComparison.Ordinal);
        // Hex code 0x0B for vertical tab — the user must see WHICH invisible char they typed.
        Assert.Contains("0x0B", r.Error, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HostWithNullByte_Rejected()
    {
        var r = UrlBuilder.Build("https", "evil\0com", null, "/",
            System.Array.Empty<(string, string)>(), null, raw: false);
        Assert.False(r.Success);
        Assert.Contains("0x00", r.Error, System.StringComparison.Ordinal);
    }
}
