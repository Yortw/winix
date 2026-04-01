#nullable enable

using System.Text.RegularExpressions;
using Winix.FileWalk;
using Xunit;

namespace Winix.FileWalk.Tests;

public class SafeRegexTests
{
    [Fact]
    public void Create_SimplePattern_ReturnsWorkingRegex()
    {
        Regex regex = SafeRegex.Create(@"\.cs$", RegexOptions.CultureInvariant);

        bool matchesCsFile = regex.IsMatch("Program.cs");
        bool matchesMdFile = regex.IsMatch("readme.md");

        Assert.True(matchesCsFile);
        Assert.False(matchesMdFile);
    }

    [Fact]
    public void Create_SimplePattern_UsesNonBacktracking()
    {
        Regex regex = SafeRegex.Create(@"\.cs$", RegexOptions.None);

        // NonBacktracking engine reports Infinite match timeout since it
        // guarantees linear time and does not need a timeout.
        Assert.Equal(Timeout.InfiniteTimeSpan, regex.MatchTimeout);
    }

    [Fact]
    public void Create_PatternWithBackreference_FallsBackToTimeoutEngine()
    {
        // Backreferences are not supported by NonBacktracking — SafeRegex
        // should fall back to the standard engine with a finite timeout.
        Regex regex = SafeRegex.Create(@"(.)\1", RegexOptions.None);

        Assert.NotEqual(Timeout.InfiniteTimeSpan, regex.MatchTimeout);

        bool matchesRepeated = regex.IsMatch("aa");
        bool matchesDistinct = regex.IsMatch("ab");

        Assert.True(matchesRepeated);
        Assert.False(matchesDistinct);
    }

    [Fact]
    public void Create_PatternWithLookahead_FallsBackToTimeoutEngine()
    {
        // Lookahead is not supported by NonBacktracking.
        Regex regex = SafeRegex.Create(@"foo(?=bar)", RegexOptions.None);

        Assert.NotEqual(Timeout.InfiniteTimeSpan, regex.MatchTimeout);

        bool matchesFoobar = regex.IsMatch("foobar");
        bool matchesFoobaz = regex.IsMatch("foobaz");

        Assert.True(matchesFoobar);
        Assert.False(matchesFoobaz);
    }

    [Fact]
    public void Create_CaseInsensitive_HonoursOption()
    {
        Regex regex = SafeRegex.Create(@"\.CS$", RegexOptions.IgnoreCase);

        bool matchesLowerCase = regex.IsMatch("program.cs");

        Assert.True(matchesLowerCase);
    }

    [Fact]
    public void Create_InvalidPattern_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => SafeRegex.Create(@"[invalid", RegexOptions.None));
    }
}
