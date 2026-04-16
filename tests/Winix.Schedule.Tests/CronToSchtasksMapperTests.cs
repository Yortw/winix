#nullable enable

using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

public sealed class CronToSchtasksMapperTests
{
    [Fact]
    public void Map_EveryNMinutes_ReturnsMinuteSchedule()
    {
        var cron = CronExpression.Parse("*/5 * * * *");

        SchtasksSchedule result = CronToSchtasksMapper.Map(cron);

        Assert.Equal("MINUTE", result.ScheduleType);
        Assert.Equal("5", result.Modifier);
        Assert.Null(result.StartTime);
        Assert.Null(result.Days);
    }

    [Fact]
    public void Map_EveryNHours_ReturnsHourlySchedule()
    {
        var cron = CronExpression.Parse("0 */2 * * *");

        SchtasksSchedule result = CronToSchtasksMapper.Map(cron);

        Assert.Equal("HOURLY", result.ScheduleType);
        Assert.Equal("2", result.Modifier);
    }

    [Fact]
    public void Map_DailyAtTime_ReturnsDailyWithStartTime()
    {
        var cron = CronExpression.Parse("0 2 * * *");

        SchtasksSchedule result = CronToSchtasksMapper.Map(cron);

        Assert.Equal("DAILY", result.ScheduleType);
        Assert.Equal("02:00", result.StartTime);
    }

    [Fact]
    public void Map_WeekdaysAtTime_ReturnsWeeklyWithDays()
    {
        var cron = CronExpression.Parse("0 9 * * 1-5");

        SchtasksSchedule result = CronToSchtasksMapper.Map(cron);

        Assert.Equal("WEEKLY", result.ScheduleType);
        Assert.Equal("09:00", result.StartTime);
        Assert.Equal("MON,TUE,WED,THU,FRI", result.Days);
    }

    [Fact]
    public void Map_MonthlyFirstDay_ReturnsMonthlyWithDay()
    {
        var cron = CronExpression.Parse("0 2 1 * *");

        SchtasksSchedule result = CronToSchtasksMapper.Map(cron);

        Assert.Equal("MONTHLY", result.ScheduleType);
        Assert.Equal("1", result.DayOfMonth);
        Assert.Equal("02:00", result.StartTime);
    }

    [Fact]
    public void Map_EveryMinute_ReturnsMinute1()
    {
        var cron = CronExpression.Parse("* * * * *");

        SchtasksSchedule result = CronToSchtasksMapper.Map(cron);

        Assert.Equal("MINUTE", result.ScheduleType);
        Assert.Equal("1", result.Modifier);
    }

    [Fact]
    public void Map_SpecificMinuteAndHour_ReturnsDailyWithTime()
    {
        var cron = CronExpression.Parse("30 14 * * *");

        SchtasksSchedule result = CronToSchtasksMapper.Map(cron);

        Assert.Equal("DAILY", result.ScheduleType);
        Assert.Equal("14:30", result.StartTime);
    }
}
