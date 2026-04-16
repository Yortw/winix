// tests/Winix.When.Tests/IsoDurationParserTests.cs
using Xunit;
using Winix.When;

namespace Winix.When.Tests;

public class IsoDurationParserTryParseTests
{
    [Fact]
    public void TryParse_DaysHoursMinutes_Parses()
    {
        bool ok = IsoDurationParser.TryParse("P3DT4H12M", out TimeSpan result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new TimeSpan(3, 4, 12, 0), result);
    }

    [Fact]
    public void TryParse_HoursMinutesOnly_Parses()
    {
        bool ok = IsoDurationParser.TryParse("PT1H30M", out TimeSpan result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new TimeSpan(1, 30, 0), result);
    }

    [Fact]
    public void TryParse_DaysOnly_Parses()
    {
        bool ok = IsoDurationParser.TryParse("P7D", out TimeSpan result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(TimeSpan.FromDays(7), result);
    }

    [Fact]
    public void TryParse_ZeroDuration_Parses()
    {
        bool ok = IsoDurationParser.TryParse("PT0S", out TimeSpan result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(TimeSpan.Zero, result);
    }

    [Fact]
    public void TryParse_SecondsOnly_Parses()
    {
        bool ok = IsoDurationParser.TryParse("PT45S", out TimeSpan result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(TimeSpan.FromSeconds(45), result);
    }

    [Fact]
    public void TryParse_AllComponents_Parses()
    {
        bool ok = IsoDurationParser.TryParse("P1DT2H3M4S", out TimeSpan result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new TimeSpan(1, 2, 3, 4), result);
    }

    [Fact]
    public void TryParse_FractionalSeconds_Parses()
    {
        bool ok = IsoDurationParser.TryParse("PT1.5S", out TimeSpan result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(TimeSpan.FromSeconds(1.5), result);
    }

    [Fact]
    public void TryParse_DaysHoursMinutesSeconds_P1DT12H30M15S()
    {
        bool ok = IsoDurationParser.TryParse("P1DT12H30M15S", out TimeSpan result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new TimeSpan(1, 12, 30, 15), result);
    }

    [Fact]
    public void TryParse_Years_Rejected()
    {
        bool ok = IsoDurationParser.TryParse("P1Y", out _, out string? error);
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("year", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParse_Months_Rejected()
    {
        bool ok = IsoDurationParser.TryParse("P1M", out _, out string? error);
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("month", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParse_Empty_Rejected()
    {
        bool ok = IsoDurationParser.TryParse("", out _, out string? error);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_BareP_Rejected()
    {
        bool ok = IsoDurationParser.TryParse("P", out _, out string? error);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_BarePT_Rejected()
    {
        bool ok = IsoDurationParser.TryParse("PT", out _, out string? error);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_NoPPrefix_Rejected()
    {
        bool ok = IsoDurationParser.TryParse("3DT4H", out _, out string? error);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_LowercaseP_Rejected()
    {
        bool ok = IsoDurationParser.TryParse("p3d", out _, out string? error);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_WeeksNotSupported()
    {
        bool ok = IsoDurationParser.TryParse("P2W", out _, out string? error);
        Assert.False(ok);
        Assert.NotNull(error);
    }
}

public class IsoDurationParserFormatTests
{
    [Fact]
    public void Format_DaysHoursMinutes()
    {
        string result = IsoDurationParser.Format(new TimeSpan(3, 4, 12, 0));
        Assert.Equal("P3DT4H12M", result);
    }

    [Fact]
    public void Format_Zero()
    {
        string result = IsoDurationParser.Format(TimeSpan.Zero);
        Assert.Equal("PT0S", result);
    }

    [Fact]
    public void Format_SecondsOnly()
    {
        string result = IsoDurationParser.Format(TimeSpan.FromSeconds(45));
        Assert.Equal("PT45S", result);
    }

    [Fact]
    public void Format_DaysOnly()
    {
        string result = IsoDurationParser.Format(TimeSpan.FromDays(7));
        Assert.Equal("P7DT0H0M", result);
    }

    [Fact]
    public void Format_HoursMinutesSeconds()
    {
        string result = IsoDurationParser.Format(new TimeSpan(0, 2, 30, 15));
        Assert.Equal("PT2H30M15S", result);
    }

    [Fact]
    public void Format_NegativeDuration_PrefixedWithMinus()
    {
        string result = IsoDurationParser.Format(TimeSpan.FromDays(-7));
        Assert.Equal("-P7DT0H0M", result);
    }

    [Fact]
    public void Format_NegativeHoursMinutes()
    {
        string result = IsoDurationParser.Format(new TimeSpan(0, -2, -30, 0));
        Assert.Equal("-PT2H30M", result);
    }

    [Fact]
    public void Format_Roundtrip_P3DT4H12M()
    {
        bool ok = IsoDurationParser.TryParse("P3DT4H12M", out TimeSpan parsed, out _);
        Assert.True(ok);
        string formatted = IsoDurationParser.Format(parsed);
        Assert.Equal("P3DT4H12M", formatted);
    }

    [Fact]
    public void Format_Roundtrip_PT0S()
    {
        bool ok = IsoDurationParser.TryParse("PT0S", out TimeSpan parsed, out _);
        Assert.True(ok);
        string formatted = IsoDurationParser.Format(parsed);
        Assert.Equal("PT0S", formatted);
    }
}
