using Xunit;
using Winix.When;

namespace Winix.When.Tests;

public class RelativeFormatterTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Format_JustNow_Past()
    {
        Assert.Equal("just now", RelativeFormatter.Format(Now.AddSeconds(-30), Now));
    }

    [Fact]
    public void Format_MinutesAgo()
    {
        Assert.Equal("12 minutes ago", RelativeFormatter.Format(Now.AddMinutes(-12), Now));
    }

    [Fact]
    public void Format_OneMinuteAgo()
    {
        Assert.Equal("1 minute ago", RelativeFormatter.Format(Now.AddMinutes(-1), Now));
    }

    [Fact]
    public void Format_HoursAgo()
    {
        Assert.Equal("3 hours ago", RelativeFormatter.Format(Now.AddHours(-3), Now));
    }

    [Fact]
    public void Format_OneHourAgo()
    {
        Assert.Equal("1 hour ago", RelativeFormatter.Format(Now.AddHours(-1), Now));
    }

    [Fact]
    public void Format_DaysAgo()
    {
        Assert.Equal("7 days ago", RelativeFormatter.Format(Now.AddDays(-7), Now));
    }

    [Fact]
    public void Format_OneDayAgo()
    {
        Assert.Equal("1 day ago", RelativeFormatter.Format(Now.AddDays(-1), Now));
    }

    [Fact]
    public void Format_MonthsAgo()
    {
        Assert.Equal("3 months ago", RelativeFormatter.Format(Now.AddDays(-90), Now));
    }

    [Fact]
    public void Format_OneMonthAgo()
    {
        Assert.Equal("1 month ago", RelativeFormatter.Format(Now.AddDays(-35), Now));
    }

    [Fact]
    public void Format_YearsAgo()
    {
        Assert.Equal("2 years ago", RelativeFormatter.Format(Now.AddDays(-730), Now));
    }

    [Fact]
    public void Format_OneYearAgo()
    {
        Assert.Equal("1 year ago", RelativeFormatter.Format(Now.AddDays(-400), Now));
    }

    [Fact]
    public void Format_JustNow_Future()
    {
        Assert.Equal("just now", RelativeFormatter.Format(Now.AddSeconds(30), Now));
    }

    [Fact]
    public void Format_InMinutes()
    {
        Assert.Equal("in 12 minutes", RelativeFormatter.Format(Now.AddMinutes(12), Now));
    }

    [Fact]
    public void Format_InOneMinute()
    {
        Assert.Equal("in 1 minute", RelativeFormatter.Format(Now.AddMinutes(1), Now));
    }

    [Fact]
    public void Format_InHours()
    {
        Assert.Equal("in 3 hours", RelativeFormatter.Format(Now.AddHours(3), Now));
    }

    [Fact]
    public void Format_InDays()
    {
        Assert.Equal("in 7 days", RelativeFormatter.Format(Now.AddDays(7), Now));
    }

    [Fact]
    public void Format_InMonths()
    {
        Assert.Equal("in 3 months", RelativeFormatter.Format(Now.AddDays(90), Now));
    }

    [Fact]
    public void Format_InYears()
    {
        Assert.Equal("in 2 years", RelativeFormatter.Format(Now.AddDays(730), Now));
    }

    [Fact]
    public void Format_Exactly59Seconds_JustNow()
    {
        Assert.Equal("just now", RelativeFormatter.Format(Now.AddSeconds(-59), Now));
    }

    [Fact]
    public void Format_Exactly60Seconds_OneMinuteAgo()
    {
        Assert.Equal("1 minute ago", RelativeFormatter.Format(Now.AddSeconds(-60), Now));
    }

    [Fact]
    public void Format_SameTime_JustNow()
    {
        Assert.Equal("just now", RelativeFormatter.Format(Now, Now));
    }
}
