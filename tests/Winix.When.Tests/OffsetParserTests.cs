using Xunit;
using Winix.When;

namespace Winix.When.Tests;

public class OffsetParserTests
{
    [Fact]
    public void TryParse_SimpleDays_Parses()
    {
        bool ok = OffsetParser.TryParse("7d", out TimeSpan result, out string? error);
        Assert.True(ok); Assert.Null(error);
        Assert.Equal(TimeSpan.FromDays(7), result);
    }

    [Fact]
    public void TryParse_SimpleHours_Parses()
    {
        bool ok = OffsetParser.TryParse("3h", out TimeSpan result, out string? error);
        Assert.True(ok); Assert.Null(error);
        Assert.Equal(TimeSpan.FromHours(3), result);
    }

    [Fact]
    public void TryParse_SimpleMilliseconds_Parses()
    {
        bool ok = OffsetParser.TryParse("500ms", out TimeSpan result, out string? error);
        Assert.True(ok); Assert.Null(error);
        Assert.Equal(TimeSpan.FromMilliseconds(500), result);
    }

    [Fact]
    public void TryParse_SimpleWeeks_Parses()
    {
        bool ok = OffsetParser.TryParse("2w", out TimeSpan result, out string? error);
        Assert.True(ok); Assert.Null(error);
        Assert.Equal(TimeSpan.FromDays(14), result);
    }

    [Fact]
    public void TryParse_IsoDuration_Parses()
    {
        bool ok = OffsetParser.TryParse("P3DT4H12M", out TimeSpan result, out string? error);
        Assert.True(ok); Assert.Null(error);
        Assert.Equal(new TimeSpan(3, 4, 12, 0), result);
    }

    [Fact]
    public void TryParse_IsoHoursMinutes_Parses()
    {
        bool ok = OffsetParser.TryParse("PT1H30M", out TimeSpan result, out string? error);
        Assert.True(ok); Assert.Null(error);
        Assert.Equal(new TimeSpan(1, 30, 0), result);
    }

    [Fact]
    public void TryParse_DotNetTimeSpan_Parses()
    {
        bool ok = OffsetParser.TryParse("1.02:30:00", out TimeSpan result, out string? error);
        Assert.True(ok); Assert.Null(error);
        Assert.Equal(new TimeSpan(1, 2, 30, 0), result);
    }

    [Fact]
    public void TryParse_HhMmSs_Parses()
    {
        bool ok = OffsetParser.TryParse("01:30:00", out TimeSpan result, out string? error);
        Assert.True(ok); Assert.Null(error);
        Assert.Equal(new TimeSpan(1, 30, 0), result);
    }

    [Fact]
    public void TryParse_ShortHhMmSs_Parses()
    {
        bool ok = OffsetParser.TryParse("00:05:30", out TimeSpan result, out string? error);
        Assert.True(ok); Assert.Null(error);
        Assert.Equal(new TimeSpan(0, 5, 30), result);
    }

    [Fact]
    public void TryParse_Empty_Fails()
    {
        bool ok = OffsetParser.TryParse("", out _, out string? error);
        Assert.False(ok); Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_Garbage_Fails()
    {
        bool ok = OffsetParser.TryParse("abc", out _, out string? error);
        Assert.False(ok); Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_BareNumber_Fails()
    {
        bool ok = OffsetParser.TryParse("42", out _, out string? error);
        Assert.False(ok); Assert.NotNull(error);
    }
}
