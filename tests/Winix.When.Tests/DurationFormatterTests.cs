using Xunit;
using Winix.When;

namespace Winix.When.Tests;

public class DurationFormatterHumanTests
{
    [Fact]
    public void FormatHuman_DaysHoursMinutes()
    {
        Assert.Equal("7 days, 4 hours, 12 minutes", DurationFormatter.FormatHuman(new TimeSpan(7, 4, 12, 0)));
    }

    [Fact]
    public void FormatHuman_HoursSeconds()
    {
        Assert.Equal("1 hour, 30 seconds", DurationFormatter.FormatHuman(new TimeSpan(0, 1, 0, 30)));
    }

    [Fact]
    public void FormatHuman_SecondsOnly()
    {
        Assert.Equal("45 seconds", DurationFormatter.FormatHuman(TimeSpan.FromSeconds(45)));
    }

    [Fact]
    public void FormatHuman_MillisecondsOnly()
    {
        Assert.Equal("250 milliseconds", DurationFormatter.FormatHuman(TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public void FormatHuman_Zero()
    {
        Assert.Equal("0 seconds", DurationFormatter.FormatHuman(TimeSpan.Zero));
    }

    [Fact]
    public void FormatHuman_OneDay()
    {
        Assert.Equal("1 day", DurationFormatter.FormatHuman(TimeSpan.FromDays(1)));
    }

    [Fact]
    public void FormatHuman_OneHour()
    {
        Assert.Equal("1 hour", DurationFormatter.FormatHuman(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void FormatHuman_OneMinute()
    {
        Assert.Equal("1 minute", DurationFormatter.FormatHuman(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void FormatHuman_OneSecond()
    {
        Assert.Equal("1 second", DurationFormatter.FormatHuman(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void FormatHuman_NegativeDuration_UsesAbsoluteValue()
    {
        Assert.Equal("7 days", DurationFormatter.FormatHuman(TimeSpan.FromDays(-7)));
    }

    [Fact]
    public void FormatHuman_DaysMinutes_NoHours()
    {
        Assert.Equal("2 days, 30 minutes", DurationFormatter.FormatHuman(new TimeSpan(2, 0, 30, 0)));
    }

    [Fact]
    public void FormatHuman_SecondsAndMilliseconds()
    {
        // When seconds present, milliseconds are dropped
        Assert.Equal("5 seconds", DurationFormatter.FormatHuman(TimeSpan.FromSeconds(5.5)));
    }
}

public class DurationFormatterIsoTests
{
    [Fact]
    public void FormatIso_DaysHoursMinutes()
    {
        Assert.Equal("P7DT4H12M", DurationFormatter.FormatIso(new TimeSpan(7, 4, 12, 0)));
    }

    [Fact]
    public void FormatIso_Zero()
    {
        Assert.Equal("PT0S", DurationFormatter.FormatIso(TimeSpan.Zero));
    }

    [Fact]
    public void FormatIso_Negative()
    {
        Assert.Equal("-P7DT0H0M", DurationFormatter.FormatIso(TimeSpan.FromDays(-7)));
    }
}
