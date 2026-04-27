#nullable enable

using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R2 regression pin for commit 490630e — when Map() returns a Degraded result, the
/// placeholder ScheduleType/Modifier fields are emptied so a future caller bypassing
/// the Degraded check produces visibly-broken output rather than plausible-looking
/// "every minute" mis-scheduling.
/// </summary>
public sealed class CronToSchtasksMapperDegradedSentinelTests
{
    [Fact]
    public void Map_Degraded_ScheduleTypeIsEmpty()
    {
        // 5,17,42 — known degraded case from R1 tests.
        var schedule = CronToSchtasksMapper.Map(CronExpression.Parse("5,17,42 * * * *"));
        Assert.True(schedule.Degraded);
        Assert.Equal("", schedule.ScheduleType);
    }

    [Fact]
    public void Map_Degraded_ModifierIsNull()
    {
        var schedule = CronToSchtasksMapper.Map(CronExpression.Parse("5/10 * * * *"));
        Assert.True(schedule.Degraded);
        Assert.Null(schedule.Modifier);
    }

    [Fact]
    public void Map_NotDegraded_StillReturnsValidScheduleType()
    {
        // Verify the sentinel cleanup didn't accidentally strip values from the happy path.
        var schedule = CronToSchtasksMapper.Map(CronExpression.Parse("*/5 * * * *"));
        Assert.False(schedule.Degraded);
        Assert.Equal("MINUTE", schedule.ScheduleType);
        Assert.Equal("5", schedule.Modifier);
    }
}
