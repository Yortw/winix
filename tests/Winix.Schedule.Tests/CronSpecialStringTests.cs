#nullable enable

using System;
using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

public sealed class CronSpecialStringTests
{
    // Frozen reference time: 2026-04-12 14:30:00 +12:00 (Sunday in NZST).
    private static readonly DateTimeOffset Reference = new DateTimeOffset(2026, 4, 12, 14, 30, 0, TimeSpan.FromHours(12));

    [Fact]
    public void Hourly_ReturnsNextHour()
    {
        // @hourly = "0 * * * *" — next occurrence is the top of the next hour.
        var expr = CronExpression.Parse("@hourly");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 4, 12, 15, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Daily_ReturnsTomorrowMidnight()
    {
        // @daily = "0 0 * * *" — 14:30 has passed midnight, so next is tomorrow 00:00.
        var expr = CronExpression.Parse("@daily");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 4, 13, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Midnight_SameAsDaily()
    {
        // @midnight is an alias for @daily ("0 0 * * *").
        var daily = CronExpression.Parse("@daily");
        var midnight = CronExpression.Parse("@midnight");

        DateTimeOffset dailyNext = daily.GetNextOccurrence(Reference);
        DateTimeOffset midnightNext = midnight.GetNextOccurrence(Reference);

        Assert.Equal(dailyNext, midnightNext);
    }

    [Fact]
    public void Weekly_ReturnsNextSundayMidnight()
    {
        // @weekly = "0 0 * * 0" — fires on Sunday at 00:00.
        // Reference is Sunday 14:30, so 00:00 has passed; next is the following Sunday (7 days later).
        var expr = CronExpression.Parse("@weekly");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 4, 19, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Monthly_ReturnsFirstOfNextMonth()
    {
        // @monthly = "0 0 1 * *" — fires on the 1st at 00:00.
        // Reference is April 12, so day 1 has passed; next is May 1.
        var expr = CronExpression.Parse("@monthly");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Yearly_ReturnsJan1NextYear()
    {
        // @yearly = "0 0 1 1 *" — fires on January 1 at 00:00.
        // Reference is April 2026, so Jan 1 2026 has passed; next is Jan 1 2027.
        var expr = CronExpression.Parse("@yearly");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Annually_SameAsYearly()
    {
        // @annually is an alias for @yearly ("0 0 1 1 *").
        var yearly = CronExpression.Parse("@yearly");
        var annually = CronExpression.Parse("@annually");

        DateTimeOffset yearlyNext = yearly.GetNextOccurrence(Reference);
        DateTimeOffset annuallyNext = annually.GetNextOccurrence(Reference);

        Assert.Equal(yearlyNext, annuallyNext);
    }

    [Fact]
    public void UnknownSpecialString_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("@invalid"));
    }

    [Fact]
    public void SpecialString_CaseInsensitive()
    {
        // @DAILY should expand the same as @daily.
        var lower = CronExpression.Parse("@daily");
        var upper = CronExpression.Parse("@DAILY");

        DateTimeOffset lowerNext = lower.GetNextOccurrence(Reference);
        DateTimeOffset upperNext = upper.GetNextOccurrence(Reference);

        Assert.Equal(lowerNext, upperNext);
    }
}
