#nullable enable

using Winix.FileWalk;
using Xunit;

namespace Winix.FileWalk.Tests;

public class GlobMatcherTests
{
    [Fact]
    public void IsMatch_SinglePattern_MatchesCorrectly()
    {
        var matcher = new GlobMatcher(new[] { "*.cs" }, caseInsensitive: false);
        Assert.True(matcher.IsMatch("Program.cs"));
        Assert.False(matcher.IsMatch("readme.md"));
    }

    [Fact]
    public void IsMatch_MultiplePatterns_MatchesAny()
    {
        var matcher = new GlobMatcher(new[] { "*.cs", "*.fs" }, caseInsensitive: false);
        Assert.True(matcher.IsMatch("Program.cs"));
        Assert.True(matcher.IsMatch("Module.fs"));
        Assert.False(matcher.IsMatch("readme.md"));
    }

    [Fact]
    public void IsMatch_CaseInsensitive_MatchesRegardlessOfCase()
    {
        var matcher = new GlobMatcher(new[] { "*.cs" }, caseInsensitive: true);
        Assert.True(matcher.IsMatch("Program.cs"));
        Assert.True(matcher.IsMatch("Program.CS"));
        Assert.True(matcher.IsMatch("PROGRAM.Cs"));
    }

    [Fact]
    public void IsMatch_CaseSensitive_RequiresExactCase()
    {
        var matcher = new GlobMatcher(new[] { "*.cs" }, caseInsensitive: false);
        Assert.True(matcher.IsMatch("Program.cs"));
        Assert.False(matcher.IsMatch("Program.CS"));
    }

    [Fact]
    public void IsMatch_QuestionMark_MatchesSingleChar()
    {
        var matcher = new GlobMatcher(new[] { "test?.cs" }, caseInsensitive: false);
        Assert.True(matcher.IsMatch("test1.cs"));
        Assert.True(matcher.IsMatch("testA.cs"));
        Assert.False(matcher.IsMatch("test12.cs"));
        Assert.False(matcher.IsMatch("test.cs"));
    }

    [Fact]
    public void IsMatch_EmptyPatterns_MatchesNothing()
    {
        var matcher = new GlobMatcher(Array.Empty<string>(), caseInsensitive: false);
        Assert.False(matcher.IsMatch("anything.txt"));
    }

    [Fact]
    public void HasPatterns_WithPatterns_ReturnsTrue()
    {
        var matcher = new GlobMatcher(new[] { "*.cs" }, caseInsensitive: false);
        Assert.True(matcher.HasPatterns);
    }

    [Fact]
    public void HasPatterns_NoPatterns_ReturnsFalse()
    {
        var matcher = new GlobMatcher(Array.Empty<string>(), caseInsensitive: false);
        Assert.False(matcher.HasPatterns);
    }
}
