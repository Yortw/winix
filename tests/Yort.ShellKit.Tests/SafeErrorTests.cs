#nullable enable
using System;
using System.IO;
using System.Text.RegularExpressions;
using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class SafeErrorTests
{
    [Fact]
    public void DirectoryNotFound_MapsToReadableText()
    {
        string s = SafeError.Describe(new DirectoryNotFoundException("IO_PathNotFound_Path"));
        Assert.Equal("no such directory", s);
    }

    [Fact]
    public void FileNotFound_MapsToReadableText()
    {
        Assert.Equal("no such file", SafeError.Describe(new FileNotFoundException()));
    }

    [Fact]
    public void UnauthorizedAccess_MapsToReadableText()
    {
        Assert.Equal("access denied", SafeError.Describe(new UnauthorizedAccessException()));
    }

    [Fact]
    public void PathTooLong_MapsToReadableText()
    {
        Assert.Equal("path too long", SafeError.Describe(new PathTooLongException()));
    }

    [Fact]
    public void RegexParse_UsesErrorAndOffset_NotMessage()
    {
        RegexParseException rex;
        try { _ = new Regex("("); throw new InvalidOperationException("unreachable"); }
        catch (RegexParseException ex) { rex = ex; }

        string s = SafeError.Describe(rex);
        Assert.DoesNotContain("MakeException", s, StringComparison.Ordinal);
        Assert.Contains("offset", s, StringComparison.Ordinal);
        Assert.Contains(rex.Error.ToString(), s, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownException_FallsBackToTypeName_NotMessage()
    {
        string s = SafeError.Describe(new InvalidOperationException("some-internal-key"));
        Assert.Equal(nameof(InvalidOperationException), s);
    }

    [Fact]
    public void Null_ReturnsPlaceholder_DoesNotThrow()
    {
        Assert.Equal("unknown error", SafeError.Describe(null));
    }

    [Fact]
    public void Aggregate_UnwrapsToInnerCause()
    {
        var agg = new AggregateException(new DirectoryNotFoundException());
        Assert.Equal("no such directory", SafeError.Describe(agg));
    }

    [Fact]
    public void JsonException_KeepsLocation_NotMessage()
    {
        System.Text.Json.JsonException jex;
        // JsonDocument.Parse is trim-safe (no IL2026) and throws a JsonException with LineNumber,
        // so it provokes the same path as Deserialize without suppressing the trim analyzer.
        try { using var doc = System.Text.Json.JsonDocument.Parse("{bad"); throw new InvalidOperationException("unreachable"); }
        catch (System.Text.Json.JsonException ex) { jex = ex; }
        string s = SafeError.Describe(jex);
        Assert.Contains("JSON", s, StringComparison.Ordinal);
        Assert.Contains("line", s, StringComparison.Ordinal);
    }
}
