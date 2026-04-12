#nullable enable

using System;
using System.Collections.Generic;
using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

public sealed class CronExpressionNextTests
{
    // Frozen reference time: 2026-04-12 14:30:00 +12:00 (Sunday in NZST).
    private static readonly DateTimeOffset Reference = new DateTimeOffset(2026, 4, 12, 14, 30, 0, TimeSpan.FromHours(12));

    [Fact]
    public void EveryMinute_ReturnsNextMinute()
    {
        var expr = CronExpression.Parse("* * * * *");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 4, 12, 14, 31, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void DailyAt2am_AfterTime_ReturnsTomorrow()
    {
        // At 14:30 the 02:00 window has passed, so next is tomorrow.
        var expr = CronExpression.Parse("0 2 * * *");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 4, 13, 2, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void DailyAt2am_BeforeTime_ReturnsToday()
    {
        var at0100 = new DateTimeOffset(2026, 4, 12, 1, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 2 * * *");

        DateTimeOffset next = expr.GetNextOccurrence(at0100);

        Assert.Equal(new DateTimeOffset(2026, 4, 12, 2, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Every5Minutes_ReturnsNext5MinuteMark()
    {
        var expr = CronExpression.Parse("*/5 * * * *");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        // 14:30 is a match for */5 but "strictly after" semantics means next is 14:35.
        Assert.Equal(new DateTimeOffset(2026, 4, 12, 14, 35, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Weekdays_OnSunday_ReturnsMonday()
    {
        var expr = CronExpression.Parse("0 9 * * 1-5");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        // Sunday 14:30 → Monday 09:00.
        Assert.Equal(new DateTimeOffset(2026, 4, 13, 9, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Weekdays_OnFriday_ReturnsSameDay()
    {
        // 2026-04-10 is a Friday. At 08:00 the 09:00 slot hasn't passed.
        var friday0800 = new DateTimeOffset(2026, 4, 10, 8, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 9 * * 1-5");

        DateTimeOffset next = expr.GetNextOccurrence(friday0800);

        Assert.Equal(new DateTimeOffset(2026, 4, 10, 9, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Weekdays_FridayAfterTime_ReturnsNextMonday()
    {
        // 2026-04-10 is a Friday. At 10:00 the 09:00 slot has passed.
        var friday1000 = new DateTimeOffset(2026, 4, 10, 10, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 9 * * 1-5");

        DateTimeOffset next = expr.GetNextOccurrence(friday1000);

        // Next Monday is April 13.
        Assert.Equal(new DateTimeOffset(2026, 4, 13, 9, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Monthly_FirstOfMonth_BeforeDay_ReturnsThisMonth()
    {
        var april1_0100 = new DateTimeOffset(2026, 4, 1, 1, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 2 1 * *");

        DateTimeOffset next = expr.GetNextOccurrence(april1_0100);

        Assert.Equal(new DateTimeOffset(2026, 4, 1, 2, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Monthly_FirstOfMonth_AfterDay_ReturnsNextMonth()
    {
        // April 12 is past day 1, so next is May 1.
        var expr = CronExpression.Parse("0 2 1 * *");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 5, 1, 2, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void DecemberRollover_ReturnsNextYear()
    {
        var dec31 = new DateTimeOffset(2026, 12, 31, 23, 59, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("* * * * *");

        DateTimeOffset next = expr.GetNextOccurrence(dec31);

        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Feb29_LeapYear_Matches()
    {
        // 2028 is a leap year. From Feb 28 → Feb 29.
        var feb28_2028 = new DateTimeOffset(2028, 2, 28, 0, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 0 29 2 *");

        DateTimeOffset next = expr.GetNextOccurrence(feb28_2028);

        Assert.Equal(new DateTimeOffset(2028, 2, 29, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Feb29_NonLeapYear_SkipsToNextLeapYear()
    {
        // 2026 is not a leap year. Next Feb 29 is in 2028.
        var jan1_2026 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 0 29 2 *");

        DateTimeOffset next = expr.GetNextOccurrence(jan1_2026);

        Assert.Equal(new DateTimeOffset(2028, 2, 29, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Day31_AprilHas30Days_SkipsToMay()
    {
        // April has only 30 days, so day 31 in April skips to May 31.
        var april1 = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 0 31 * *");

        DateTimeOffset next = expr.GetNextOccurrence(april1);

        Assert.Equal(new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void ExactMatch_WithSeconds_AdvancesToNextOccurrence()
    {
        // "30 14 * * *" at 14:30:30 — we're in the 14:30 minute but seconds > 0,
        // so advancing to next whole minute is 14:31 which doesn't match minute=30,
        // so the result is tomorrow 14:30.
        var at1430_30s = new DateTimeOffset(2026, 4, 12, 14, 30, 30, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("30 14 * * *");

        DateTimeOffset next = expr.GetNextOccurrence(at1430_30s);

        Assert.Equal(new DateTimeOffset(2026, 4, 13, 14, 30, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void ExactMatch_WithZeroSeconds_AdvancesToNextOccurrence()
    {
        // "30 14 * * *" at exactly 14:30:00 — strictly "after" semantics,
        // so we advance to next minute (14:31) which doesn't match, result is tomorrow 14:30.
        var at1430 = new DateTimeOffset(2026, 4, 12, 14, 30, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("30 14 * * *");

        DateTimeOffset next = expr.GetNextOccurrence(at1430);

        Assert.Equal(new DateTimeOffset(2026, 4, 13, 14, 30, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void GetNextOccurrences_ReturnsRequestedCount()
    {
        var expr = CronExpression.Parse("0 2 * * *");

        IReadOnlyList<DateTimeOffset> results = expr.GetNextOccurrences(Reference, 5);

        Assert.Equal(5, results.Count);
        // Should be 5 consecutive days at 02:00.
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(new DateTimeOffset(2026, 4, 13 + i, 2, 0, 0, TimeSpan.FromHours(12)), results[i]);
        }
    }

    [Fact]
    public void Sunday_Zero_MatchesSunday()
    {
        // 2026-04-11 is a Saturday. Next Sunday is April 12.
        var saturday = new DateTimeOffset(2026, 4, 11, 10, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 9 * * 0");

        DateTimeOffset next = expr.GetNextOccurrence(saturday);

        Assert.Equal(new DateTimeOffset(2026, 4, 12, 9, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Sunday_Seven_MatchesSunday()
    {
        // 7 is an alias for Sunday. Same expected result as Sunday_Zero.
        var saturday = new DateTimeOffset(2026, 4, 11, 10, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 9 * * 7");

        DateTimeOffset next = expr.GetNextOccurrence(saturday);

        Assert.Equal(new DateTimeOffset(2026, 4, 12, 9, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void PreservesOffset_FromInput()
    {
        var utcRef = new DateTimeOffset(2026, 4, 12, 2, 30, 0, TimeSpan.Zero);
        var expr = CronExpression.Parse("* * * * *");

        DateTimeOffset next = expr.GetNextOccurrence(utcRef);

        Assert.Equal(TimeSpan.Zero, next.Offset);
        Assert.Equal(new DateTimeOffset(2026, 4, 12, 2, 31, 0, TimeSpan.Zero), next);
    }
}
