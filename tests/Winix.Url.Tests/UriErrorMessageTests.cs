#nullable enable
using Xunit;

namespace Winix.Url.Tests;

/// <summary>
/// Pins the SR-resource-key → English mapping introduced after tier-2 re-verification 2026-05-06
/// found that <c>UrlParser</c> and <c>UrlJoiner</c> were leaking <c>net_uri_*</c> tokens to user
/// output under <c>InvariantGlobalization=true</c>. Each known mapping has a regression test so
/// renaming or dropping a key has to be a deliberate edit, not silent drift.
/// </summary>
public class UriErrorMessageTests
{
    [Theory]
    [InlineData("net_uri_EmptyUri", "URL is empty")]
    [InlineData("net_uri_BadFormat", "URL has no scheme or is malformed")]
    [InlineData("net_uri_BadHostName", "hostname is malformed or empty")]
    [InlineData("net_uri_BadPort", "port number is out of range (0–65535)")]
    [InlineData("net_uri_PortOutOfRange", "port number is out of range (0–65535)")]
    [InlineData("net_uri_BadScheme", "scheme contains invalid characters")]
    [InlineData("net_uri_SchemeLimit", "scheme is too long")]
    [InlineData("net_uri_BadAuthority", "authority component is malformed")]
    [InlineData("net_uri_BadAuthorityTerminator", "authority component is not terminated correctly")]
    [InlineData("net_uri_BadUserPassword", "userinfo component is malformed")]
    [InlineData("net_uri_BadString", "URL contains invalid characters")]
    [InlineData("net_uri_MustRootedPath", "path must start with '/'")]
    [InlineData("net_uri_NotAbsolute", "URL is not absolute")]
    [InlineData("net_uri_SizeLimit", "URL exceeds the maximum size")]
    public void ToEnglish_KnownSrKey_MapsToEnglish(string srKey, string expected)
    {
        Assert.Equal(expected, UriErrorMessage.ToEnglish(srKey));
    }

    [Fact]
    public void ToEnglish_UnknownKey_PassesThroughWithUnmappedPrefix()
    {
        // Unknown resource keys (e.g. a future .NET runtime adds a new one) must surface visibly
        // so we notice and add an explicit mapping, rather than silently regressing to a leak.
        string result = UriErrorMessage.ToEnglish("net_uri_BrandNewKey");
        Assert.Contains("unrecognised URL format error", result);
        Assert.Contains("net_uri_BrandNewKey", result);
    }

    [Fact]
    public void ToEnglish_Null_DoesNotThrow()
    {
        // Defensive: ex.Message on UriFormatException should always be non-null in practice,
        // but tolerating null avoids surfacing a NullReferenceException to the user.
        string result = UriErrorMessage.ToEnglish(null);
        Assert.Contains("unrecognised URL format error", result);
    }
}
