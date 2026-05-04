// tests/Winix.When.Tests/InputParserTests.cs
using Xunit;
using Winix.When;

namespace Winix.When.Tests;

public class InputParserTests
{
    [Fact]
    public void TryParse_Now_ReturnsTrue()
    {
        bool ok = InputParser.TryParse("now", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(DateTimeOffset.MinValue, result);
    }

    [Fact]
    public void TryParse_NowCaseInsensitive_ReturnsTrue()
    {
        bool ok = InputParser.TryParse("NOW", out _, out string? error);
        Assert.True(ok);
        Assert.Null(error);
    }

    [Fact]
    public void IsNow_Now_ReturnsTrue()
    {
        Assert.True(InputParser.IsNow("now"));
    }

    [Fact]
    public void IsNow_NotNow_ReturnsFalse()
    {
        Assert.False(InputParser.IsNow("2024-06-18"));
    }

    [Fact]
    public void TryParse_EpochSeconds_Parses()
    {
        bool ok = InputParser.TryParse("1718740800", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new DateTimeOffset(2024, 6, 18, 20, 0, 0, TimeSpan.Zero), result);
    }

    // -- Round-1 review SFH-I1 — CONTRACT CHANGE.
    //    The original contract pinned by `TryParse_SmallEpoch_TreatedAsEpochNotYear`
    //    was "small numerics are always epoch seconds, never years". SFH found a real
    //    silent-wrong-output bug under that contract: `when 2025 --utc` produced
    //    `1970-01-01T00:33:45Z` with no signal to the user that they probably meant the
    //    year. In pipe-friendly modes (--utc / --local / --json) the misparse is
    //    invisible to downstream consumers.
    //    New contract: bare positive integers in [1900, 2200] AND length ≤ 4 are rejected
    //    as ambiguous with an error directing the user to `2024-01-01` (year) or `0000002024`
    //    (epoch escape hatch). Numerics outside that range still go through the epoch
    //    path unchanged. --
    [Fact]
    public void TryParse_BareYearLikeInteger_RejectedAsAmbiguous()
    {
        bool ok = InputParser.TryParse("2024", out DateTimeOffset result, out string? error);
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("ambiguous", error, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2024-01-01", error, System.StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_SmallEpoch_OutsideYearRange_StillTreatedAsEpoch()
    {
        // 1234 = epoch second 1234 = 1970-01-01 00:20:34 UTC. Outside [1900, 2200].
        bool ok = InputParser.TryParse("1234", out DateTimeOffset result, out string? error);
        Assert.True(ok, error);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(1234), result);
    }

    [Fact]
    public void TryParse_LeadingZerosForceEpoch_ParsesEvenInYearRange()
    {
        // `0000002025` (5+ digits with leading zeros) is the documented escape hatch for
        // forcing epoch interpretation when the value would otherwise be ambiguous.
        bool ok = InputParser.TryParse("0000002025", out DateTimeOffset result, out string? error);
        Assert.True(ok, error);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(2025), result);
    }

    [Fact]
    public void TryParse_EpochZero_Parses()
    {
        bool ok = InputParser.TryParse("0", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(DateTimeOffset.UnixEpoch, result);
    }

    [Fact]
    public void TryParse_NegativeEpoch_Parses()
    {
        bool ok = InputParser.TryParse("-86400", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(-86400), result);
    }

    [Fact]
    public void TryParse_EpochMilliseconds_Parses()
    {
        bool ok = InputParser.TryParse("1718740800000", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new DateTimeOffset(2024, 6, 18, 20, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void TryParse_EpochMilliseconds_11Digits()
    {
        bool ok = InputParser.TryParse("10000000000", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(10000000000), result);
    }

    [Fact]
    public void TryParse_DecimalEpoch_Parses()
    {
        bool ok = InputParser.TryParse("1718745600.123", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(1718745600.123), result);
    }

    [Fact]
    public void TryParse_Iso8601WithZ_Parses()
    {
        bool ok = InputParser.TryParse("2024-06-18T20:00:00Z", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new DateTimeOffset(2024, 6, 18, 20, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void TryParse_Iso8601WithOffset_Parses()
    {
        bool ok = InputParser.TryParse("2024-06-18T20:00:00+12:00", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new DateTimeOffset(2024, 6, 18, 20, 0, 0, TimeSpan.FromHours(12)), result);
    }

    [Fact]
    public void TryParse_Iso8601DateOnly_ParsesAsMidnightUtc()
    {
        bool ok = InputParser.TryParse("2024-06-18", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new DateTimeOffset(2024, 6, 18, 0, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void TryParse_Iso8601NoTimezone_ParsesAsUtc()
    {
        bool ok = InputParser.TryParse("2024-06-18T20:00:00", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new DateTimeOffset(2024, 6, 18, 20, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void TryParse_SpaceSeparated_Parses()
    {
        bool ok = InputParser.TryParse("2024-06-18 20:00:00", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new DateTimeOffset(2024, 6, 18, 20, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void TryParse_SpaceSeparatedWithOffset_Parses()
    {
        bool ok = InputParser.TryParse("2024-06-18 20:00:00+12:00", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new DateTimeOffset(2024, 6, 18, 20, 0, 0, TimeSpan.FromHours(12)), result);
    }

    [Fact]
    public void TryParse_NamedMonth_MonthDayYear()
    {
        bool ok = InputParser.TryParse("Jun 18 2024", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new DateTimeOffset(2024, 6, 18, 0, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void TryParse_NamedMonth_DayMonthYear()
    {
        bool ok = InputParser.TryParse("18 Jun 2024", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new DateTimeOffset(2024, 6, 18, 0, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void TryParse_NamedMonth_WithComma()
    {
        bool ok = InputParser.TryParse("Jun 18, 2024", out DateTimeOffset result, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new DateTimeOffset(2024, 6, 18, 0, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void TryParse_SlashDate_Rejected()
    {
        bool ok = InputParser.TryParse("06/12/2024", out _, out string? error);
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("ambiguous", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParse_DashNumericOnly_Rejected()
    {
        bool ok = InputParser.TryParse("12-06-2024", out _, out string? error);
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("ambiguous", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParse_Empty_Fails()
    {
        bool ok = InputParser.TryParse("", out _, out string? error);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_Garbage_Fails()
    {
        bool ok = InputParser.TryParse("not-a-date", out _, out string? error);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_EpochTooLarge_Fails()
    {
        bool ok = InputParser.TryParse("99999999999999", out _, out string? error);
        Assert.False(ok);
        Assert.NotNull(error);
    }
}
