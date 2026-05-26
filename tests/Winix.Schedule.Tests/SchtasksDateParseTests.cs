using System;
using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// Pins the locale-aware date parsing used by SchtasksCsvParser. Schtasks emits dates in
/// the system locale; under InvariantGlobalization=true the .NET process can't load named
/// cultures, so the parser uses a fixed format list with InvariantCulture instead. Each
/// supported format gets a regression test here.
/// </summary>
public sealed class SchtasksDateParseTests
{
    [Fact]
    public void TryParseScheduleDate_EnUs_TwelveHourWithSeconds_Parses()
    {
        DateTime? result = SchtasksCsvParser.TryParseScheduleDate("4/13/2026 2:00:00 AM");
        Assert.NotNull(result);
        Assert.Equal(new DateTime(2026, 4, 13, 2, 0, 0), result!.Value);
    }

    [Fact]
    public void TryParseScheduleDate_EnUs_PaddedHour_Parses()
    {
        DateTime? result = SchtasksCsvParser.TryParseScheduleDate("04/13/2026 02:00:00 AM");
        Assert.NotNull(result);
        Assert.Equal(new DateTime(2026, 4, 13, 2, 0, 0), result!.Value);
    }

    [Fact]
    public void TryParseScheduleDate_EnGb_DayMonthYear_Parses()
    {
        DateTime? result = SchtasksCsvParser.TryParseScheduleDate("13/04/2026 14:00:00");
        Assert.NotNull(result);
        Assert.Equal(new DateTime(2026, 4, 13, 14, 0, 0), result!.Value);
    }

    [Fact]
    public void TryParseScheduleDate_EnNz_SingleDigitHour_Parses()
    {
        DateTime? result = SchtasksCsvParser.TryParseScheduleDate("3/4/2026 9:00:00");
        Assert.NotNull(result);
        Assert.Equal(new DateTime(2026, 4, 3, 9, 0, 0), result!.Value);
    }

    [Fact]
    public void TryParseScheduleDate_German_DotSeparated_Parses()
    {
        DateTime? result = SchtasksCsvParser.TryParseScheduleDate("13.04.2026 14:00:00");
        Assert.NotNull(result);
        Assert.Equal(new DateTime(2026, 4, 13, 14, 0, 0), result!.Value);
    }

    [Fact]
    public void TryParseScheduleDate_Japanese_YearFirstSlashed_Parses()
    {
        DateTime? result = SchtasksCsvParser.TryParseScheduleDate("2026/04/13 14:00:00");
        Assert.NotNull(result);
        Assert.Equal(new DateTime(2026, 4, 13, 14, 0, 0), result!.Value);
    }

    [Fact]
    public void TryParseScheduleDate_Iso_YearFirstHyphenated_Parses()
    {
        DateTime? result = SchtasksCsvParser.TryParseScheduleDate("2026-04-13 14:00:00");
        Assert.NotNull(result);
        Assert.Equal(new DateTime(2026, 4, 13, 14, 0, 0), result!.Value);
    }

    [Fact]
    public void TryParseScheduleDate_TimeOnly_TwentyFourHour_Parses()
    {
        // Used for /ST start-time columns in the verbose CSV.
        DateTime? result = SchtasksCsvParser.TryParseScheduleDate("14:00:00");
        Assert.NotNull(result);
        Assert.Equal(14, result!.Value.Hour);
        Assert.Equal(0, result.Value.Minute);
    }

    [Fact]
    public void TryParseScheduleDate_TimeOnly_TwelveHour_Parses()
    {
        DateTime? result = SchtasksCsvParser.TryParseScheduleDate("2:00:00 AM");
        Assert.NotNull(result);
        Assert.Equal(2, result!.Value.Hour);
    }

    [Fact]
    public void TryParseScheduleDate_NotADate_ReturnsNull()
    {
        Assert.Null(SchtasksCsvParser.TryParseScheduleDate("Disabled"));
        Assert.Null(SchtasksCsvParser.TryParseScheduleDate("N/A"));
        Assert.Null(SchtasksCsvParser.TryParseScheduleDate(""));
    }

    [Fact]
    public void TryParseScheduleDate_AssumesLocalKind()
    {
        // AssumeLocal style — no zone info in the input string should produce Local kind so that
        // round-tripping through DateTime.ToString("o") emits an offset (Unspecified would emit
        // bare local time with no offset, which is what the previous CurrentCulture path did
        // when InvariantGlobalization stripped the named-culture data).
        DateTime? result = SchtasksCsvParser.TryParseScheduleDate("4/13/2026 2:00:00 AM");
        Assert.NotNull(result);
        Assert.Equal(DateTimeKind.Local, result!.Value.Kind);
    }
}
