#nullable enable

using System;
using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R4 regression pins for DST handling in <see cref="CronExpression.GetNextOccurrence"/>.
///
/// Pre-fix, the search captured the input <c>after.Offset</c> once and reused it for every
/// iteration — so on the first occurrence after a DST boundary, the returned wall-clock
/// time was off by an hour for half the year (every DST zone: NZ, AU, EU, US, etc.). This
/// affected what users saw in <c>schedule next</c> and the "Next Run" column of
/// <c>schedule list</c>.
///
/// The fix does arithmetic on a wall-clock <see cref="DateTime"/> in a specified
/// <see cref="TimeZoneInfo"/>, then carries the correct UTC offset for each candidate's
/// own local date when constructing the returned <see cref="DateTimeOffset"/>.
///
/// Tests use NZ rules synthesised in-test so they're deterministic on UTC-only CI agents.
/// </summary>
public sealed class CronExpressionDstTests
{
    /// <summary>
    /// Synthesised NZ-style DST timezone: standard offset +12, daylight offset +13.
    /// Spring-forward at 02:00 local on the last Sunday of September.
    /// Fall-back at 03:00 local on the first Sunday of April (matches NZ rules).
    /// Self-contained so the test passes regardless of host machine TZ data.
    /// </summary>
    private static readonly TimeZoneInfo NzLikeZone = BuildNzLikeZone();

    private static TimeZoneInfo BuildNzLikeZone()
    {
        // NZ daylight rule: spring-forward last Sunday Sep at 02:00 → 03:00 (gap 02:00..02:59).
        // Fall-back first Sunday April at 03:00 → 02:00 (overlap 02:00..02:59 happens twice).
        var transitionStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 9, 5, DayOfWeek.Sunday);
        var transitionEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 3, 0, 0), 4, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date, DateTime.MaxValue.Date,
            TimeSpan.FromHours(1), transitionStart, transitionEnd);

        return TimeZoneInfo.CreateCustomTimeZone(
            "NZ-Like",
            TimeSpan.FromHours(12),
            "NZ-Like Standard",
            "NZ-Like Standard",
            "NZ-Like Daylight",
            new[] { rule });
    }

    [Fact]
    public void DailyAt0230_StraddlingFallBack_ReportsConsistentLocalTime()
    {
        // 2026-04-05 is the first Sunday of April → fall-back day. 02:30 happens twice
        // on this day (once at +13 NZDT, then again at +12 NZST after the clock falls
        // back at 03:00). Cron "30 2 * * *" must continue to READ as 02:30 local on
        // every day — pre-fix, the day after fall-back was reported as 01:30 because
        // the saved +13 offset was reapplied after the clock had moved to +12.
        var expr = CronExpression.Parse("30 2 * * *");
        var afterPreDst = new DateTimeOffset(2026, 4, 4, 12, 0, 0, TimeSpan.FromHours(13));

        var occ1 = expr.GetNextOccurrence(afterPreDst, NzLikeZone);
        var occ2 = expr.GetNextOccurrence(occ1, NzLikeZone);
        var occ3 = expr.GetNextOccurrence(occ2, NzLikeZone);

        // The first 02:30 strictly after 2026-04-04 12:00 lands on 2026-04-05 02:30.
        // That wall-clock is ambiguous; .NET's GetUtcOffset returns standard offset
        // (+12) for ambiguous times, so the impl picks the second occurrence by default.
        // Either +12 or +13 is a defensible cron semantic here — we pin wall-clock,
        // not offset, on the ambiguous day.
        Assert.Equal(new DateTime(2026, 4, 5, 2, 30, 0), occ1.DateTime);
        Assert.Contains(occ1.Offset, new[] { TimeSpan.FromHours(12), TimeSpan.FromHours(13) });

        // The day AFTER fall-back: 02:30 NZST. Pre-fix this was reported as 01:30
        // because the original input offset (+13) was reapplied to a wall-clock that
        // had already moved to +12 — the regression this whole task was about.
        Assert.Equal(new DateTime(2026, 4, 6, 2, 30, 0), occ2.DateTime);
        Assert.Equal(TimeSpan.FromHours(12), occ2.Offset);

        Assert.Equal(new DateTime(2026, 4, 7, 2, 30, 0), occ3.DateTime);
        Assert.Equal(TimeSpan.FromHours(12), occ3.Offset);
    }

    [Fact]
    public void DailyAt0230_StraddlingSpringForward_SkipsGapToNextValidWallClock()
    {
        // 2026-09-27 is the last Sunday of September → spring-forward at 02:00.
        // The local times 02:00..02:59 don't exist on that day. A cron firing at
        // "30 2 * * *" has no matching wall-clock on 2026-09-27, so the search
        // skips the gap and resumes at 02:30 on the next day (2026-09-28 NZDT).
        var expr = CronExpression.Parse("30 2 * * *");
        var afterPreSpring = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.FromHours(12));

        var occ1 = expr.GetNextOccurrence(afterPreSpring, NzLikeZone);
        var occ2 = expr.GetNextOccurrence(occ1, NzLikeZone);

        // The 02:30 on 09-27 is invalid (in the spring-forward gap), so the search
        // moves on. First valid 02:30 wall-clock is on 09-28, now NZDT (+13).
        Assert.Equal(new DateTime(2026, 9, 28, 2, 30, 0), occ1.DateTime);
        Assert.Equal(TimeSpan.FromHours(13), occ1.Offset);

        Assert.Equal(new DateTime(2026, 9, 29, 2, 30, 0), occ2.DateTime);
        Assert.Equal(TimeSpan.FromHours(13), occ2.Offset);
    }

    [Fact]
    public void EveryMinute_AcrossSpringForward_OffsetShiftsAtTransition()
    {
        // Cron "* * * * *" — fires every wall-clock minute that exists. Verify that
        // crossing the spring-forward boundary advances wall-clock past the gap and
        // the returned offset reflects DST.
        var expr = CronExpression.Parse("* * * * *");
        var atGapStart = new DateTimeOffset(2026, 9, 27, 1, 59, 0, TimeSpan.FromHours(12));

        // 01:59 NZST → next minute is 02:00 which doesn't exist → 03:00 NZDT.
        var occ = expr.GetNextOccurrence(atGapStart, NzLikeZone);

        Assert.Equal(new DateTime(2026, 9, 27, 3, 0, 0), occ.DateTime);
        Assert.Equal(TimeSpan.FromHours(13), occ.Offset);
    }

    [Fact]
    public void HourlyTopOfHour_AcrossFallBack_AdvancesMonotonically()
    {
        // 2026-04-05 fall-back at 03:00 → 02:00. The wall-clock hour 02:00 happens
        // twice. Cron "0 * * * *" fires every top-of-hour. The exact behaviour at the
        // ambiguous hour is impl-defined (fire-once vs fire-twice), but the contract is:
        //   1. Wall-clock advances monotonically — no infinite loop.
        //   2. The first occurrence after 01:30 NZDT has wall-clock 02:00.
        //   3. By the time wall-clock reaches 03:00 we're firmly in NZST (+12).
        var expr = CronExpression.Parse("0 * * * *");
        var atFallBackEdge = new DateTimeOffset(2026, 4, 5, 1, 30, 0, TimeSpan.FromHours(13));

        var occ1 = expr.GetNextOccurrence(atFallBackEdge, NzLikeZone);
        var occ2 = expr.GetNextOccurrence(occ1, NzLikeZone);

        Assert.Equal(new DateTime(2026, 4, 5, 2, 0, 0), occ1.DateTime);
        Assert.Contains(occ1.Offset, new[] { TimeSpan.FromHours(12), TimeSpan.FromHours(13) });

        // Whatever the impl picked for the ambiguous 02:00, the next occurrence must
        // strictly advance in absolute time (UTC instant) — pre-fix, frozen-offset
        // arithmetic could let two successive results report the same UTC instant.
        Assert.True(occ2.UtcTicks > occ1.UtcTicks,
            "Successive cron occurrences must advance in absolute time across DST fall-back.");

        // Continue past the ambiguous hour. The implementation may emit 02:00 once or
        // twice; either way, walking until wall-clock 03:00 must land us in NZST.
        var current = occ2;
        for (int i = 0; i < 4; i++)
        {
            if (current.DateTime == new DateTime(2026, 4, 5, 3, 0, 0))
            {
                Assert.Equal(TimeSpan.FromHours(12), current.Offset);
                return;
            }
            current = expr.GetNextOccurrence(current, NzLikeZone);
        }
        Assert.Fail($"Did not reach 03:00 NZST within 4 hourly steps; last={current:o}");
    }
}
