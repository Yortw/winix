#nullable enable

using Winix.FileWalk;
using Xunit;

namespace Winix.FileWalk.Tests;

public class DurationParserTests
{
    [Theory]
    [InlineData("30s", 30)]
    [InlineData("1s", 1)]
    [InlineData("0s", 0)]
    public void Parse_Seconds_ReturnsCorrectTimeSpan(string input, int expectedSeconds)
    {
        TimeSpan result = DurationParser.Parse(input);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Theory]
    [InlineData("5m", 5 * 60)]
    [InlineData("1m", 60)]
    [InlineData("90m", 90 * 60)]
    public void Parse_Minutes_ReturnsCorrectTimeSpan(string input, int expectedSeconds)
    {
        TimeSpan result = DurationParser.Parse(input);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Theory]
    [InlineData("1h", 3600)]
    [InlineData("24h", 86400)]
    public void Parse_Hours_ReturnsCorrectTimeSpan(string input, int expectedSeconds)
    {
        TimeSpan result = DurationParser.Parse(input);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Theory]
    [InlineData("1d", 86400)]
    [InlineData("7d", 604800)]
    public void Parse_Days_ReturnsCorrectTimeSpan(string input, int expectedSeconds)
    {
        TimeSpan result = DurationParser.Parse(input);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Theory]
    [InlineData("1w", 604800)]
    [InlineData("2w", 1209600)]
    public void Parse_Weeks_ReturnsCorrectTimeSpan(string input, int expectedSeconds)
    {
        TimeSpan result = DurationParser.Parse(input);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("s")]
    [InlineData("-1h")]
    [InlineData("1.5h")]
    [InlineData("1x")]
    [InlineData("100")]
    public void Parse_InvalidInput_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => DurationParser.Parse(input));
    }

    [Fact]
    public void TryParse_ValidInput_ReturnsTrueAndValue()
    {
        bool ok = DurationParser.TryParse("2h", out TimeSpan duration);
        Assert.True(ok);
        Assert.Equal(TimeSpan.FromHours(2), duration);
    }

    [Fact]
    public void TryParse_InvalidInput_ReturnsFalse()
    {
        bool ok = DurationParser.TryParse("bad", out TimeSpan duration);
        Assert.False(ok);
        Assert.Equal(TimeSpan.Zero, duration);
    }
}
