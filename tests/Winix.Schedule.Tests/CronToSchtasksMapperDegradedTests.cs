using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// Pins the contract that cron expressions with no clean schtasks mapping are flagged as
/// Degraded so the backend can reject the operation rather than silently registering a
/// task that runs every minute, 24/7. Without this the mapper's MINUTE/MO=1 fallback
/// would silently mis-schedule any expression schtasks doesn't natively support.
/// </summary>
public sealed class CronToSchtasksMapperDegradedTests
{
    [Fact]
    public void Map_EveryMinute_NotDegraded()
    {
        var schedule = CronToSchtasksMapper.Map(CronExpression.Parse("* * * * *"));
        Assert.False(schedule.Degraded);
        Assert.Null(schedule.DegradedReason);
        Assert.Equal("MINUTE", schedule.ScheduleType);
        Assert.Equal("1", schedule.Modifier);
    }

    [Fact]
    public void Map_EveryFiveMinutes_NotDegraded()
    {
        var schedule = CronToSchtasksMapper.Map(CronExpression.Parse("*/5 * * * *"));
        Assert.False(schedule.Degraded);
        Assert.Equal("MINUTE", schedule.ScheduleType);
        Assert.Equal("5", schedule.Modifier);
    }

    [Fact]
    public void Map_DailyAtFixedTime_NotDegraded()
    {
        var schedule = CronToSchtasksMapper.Map(CronExpression.Parse("0 2 * * *"));
        Assert.False(schedule.Degraded);
        Assert.Equal("DAILY", schedule.ScheduleType);
    }

    [Fact]
    public void Map_WeeklyWeekdays_NotDegraded()
    {
        var schedule = CronToSchtasksMapper.Map(CronExpression.Parse("0 9 * * 1-5"));
        Assert.False(schedule.Degraded);
        Assert.Equal("WEEKLY", schedule.ScheduleType);
    }

    [Fact]
    public void Map_MonthlyOnFirst_NotDegraded()
    {
        var schedule = CronToSchtasksMapper.Map(CronExpression.Parse("30 6 1 * *"));
        Assert.False(schedule.Degraded);
        Assert.Equal("MONTHLY", schedule.ScheduleType);
    }

    [Fact]
    public void Map_NonStepMinuteList_FlaggedDegraded()
    {
        // 5,17,42 cannot be expressed as a single MINUTE step.
        var schedule = CronToSchtasksMapper.Map(CronExpression.Parse("5,17,42 * * * *"));
        Assert.True(schedule.Degraded);
        Assert.NotNull(schedule.DegradedReason);
        Assert.Contains("5,17,42", schedule.DegradedReason);
    }

    [Fact]
    public void Map_StepNotStartingAtMin_FlaggedDegraded()
    {
        // 5/10 — step starts at 5 not 0; DetectStep returns null and mapper falls through.
        var schedule = CronToSchtasksMapper.Map(CronExpression.Parse("5/10 * * * *"));
        Assert.True(schedule.Degraded);
    }

    [Fact]
    public void Map_HourRangeWithStepAndDow_FlaggedDegraded()
    {
        // 0 9-17/2 * * 1-5 — every 2 hours during weekday business hours; schtasks has no
        // single-pattern expression for this combination.
        var schedule = CronToSchtasksMapper.Map(CronExpression.Parse("0 9-17/2 * * 1-5"));
        Assert.True(schedule.Degraded);
    }

    [Fact]
    public void Map_BothDomAndDowRestricted_FlaggedDegraded()
    {
        // 0 0 1 * 1 — fires on the 1st of the month OR Mondays. Standard cron OR semantics
        // has no clean schtasks equivalent.
        var schedule = CronToSchtasksMapper.Map(CronExpression.Parse("0 0 1 * 1"));
        Assert.True(schedule.Degraded);
    }

    [Fact]
    public void Map_DegradedReason_MentionsSupportedPatterns()
    {
        var schedule = CronToSchtasksMapper.Map(CronExpression.Parse("5,17,42 * * * *"));
        Assert.True(schedule.Degraded);
        Assert.Contains("Supported patterns", schedule.DegradedReason);
    }
}
