using Xunit;
using Winix.When;

namespace Winix.When.Tests;

public class TimezoneResolverTests
{
    [Fact]
    public void TryResolve_IanaId_Resolves()
    {
        bool ok = TimezoneResolver.TryResolve("Asia/Tokyo", out TimeZoneInfo? zone, out string? error);
        Assert.True(ok);
        Assert.NotNull(zone);
        Assert.Null(error);
        Assert.Equal(TimeSpan.FromHours(9), zone!.BaseUtcOffset);
    }

    [Fact]
    public void TryResolve_WindowsId_Resolves()
    {
        bool ok = TimezoneResolver.TryResolve("Tokyo Standard Time", out TimeZoneInfo? zone, out string? error);
        Assert.True(ok);
        Assert.NotNull(zone);
        Assert.Null(error);
        Assert.Equal(TimeSpan.FromHours(9), zone!.BaseUtcOffset);
    }

    [Fact]
    public void TryResolve_Utc_Resolves()
    {
        bool ok = TimezoneResolver.TryResolve("UTC", out TimeZoneInfo? zone, out string? error);
        Assert.True(ok);
        Assert.NotNull(zone);
        Assert.Null(error);
        Assert.Equal(TimeSpan.Zero, zone!.BaseUtcOffset);
    }

    [Fact]
    public void TryResolve_UnknownId_Fails()
    {
        bool ok = TimezoneResolver.TryResolve("Not/A/Timezone", out TimeZoneInfo? zone, out string? error);
        Assert.False(ok);
        Assert.Null(zone);
        Assert.NotNull(error);
        Assert.Contains("Not/A/Timezone", error);
    }

    [Fact]
    public void TryResolve_Empty_Fails()
    {
        bool ok = TimezoneResolver.TryResolve("", out TimeZoneInfo? zone, out string? error);
        Assert.False(ok);
        Assert.Null(zone);
        Assert.NotNull(error);
    }

    [Fact]
    public void GetAbbreviation_Tokyo_ReturnsJST()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var dto = new DateTimeOffset(2024, 6, 18, 20, 0, 0, TimeSpan.Zero);
        string abbr = TimezoneResolver.GetAbbreviation(tz, dto);
        Assert.Equal("JST", abbr);
    }

    [Fact]
    public void GetDisplayLabel_IanaId_ReturnsCity()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        string label = TimezoneResolver.GetDisplayLabel(tz);
        Assert.Equal("Tokyo", label);
    }

    [Fact]
    public void GetDisplayLabel_Utc_ReturnsUTC()
    {
        string label = TimezoneResolver.GetDisplayLabel(TimeZoneInfo.Utc);
        Assert.Equal("UTC", label);
    }
}
