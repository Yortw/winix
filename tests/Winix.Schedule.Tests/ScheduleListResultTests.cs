#nullable enable

using System;
using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R4 contract pins for <see cref="ScheduleListResult"/> — the dedicated result type
/// introduced so <see cref="ISchedulerBackend.List"/> can distinguish "no tasks" from
/// "backend unreachable" without collapsing both to the same empty list.
/// </summary>
public sealed class ScheduleListResultTests
{
    [Fact]
    public void Ok_AvailableTrue_NoWarning_NoFailureReason()
    {
        var task = new ScheduledTask("t", "* * * * *", null, "Enabled", "echo hi", "");
        var result = ScheduleListResult.Ok(new[] { task });

        Assert.True(result.Available);
        Assert.Single(result.Tasks);
        Assert.Null(result.Warning);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Ok_EmptyList_StillAvailable()
    {
        // Genuinely-empty scheduler vs unreachable scheduler are different states; both
        // can have empty Tasks, but only one has Available=true.
        var result = ScheduleListResult.Ok(Array.Empty<ScheduledTask>());

        Assert.True(result.Available);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public void OkWithWarning_TrimsWhitespace()
    {
        var result = ScheduleListResult.OkWithWarning(
            Array.Empty<ScheduledTask>(),
            "  PAM stack noisy  \n");

        Assert.True(result.Available);
        Assert.Equal("PAM stack noisy", result.Warning);
    }

    [Fact]
    public void OkWithWarning_NullOrWhitespace_NormalisesToNoWarning()
    {
        var nullCase = ScheduleListResult.OkWithWarning(Array.Empty<ScheduledTask>(), null);
        var emptyCase = ScheduleListResult.OkWithWarning(Array.Empty<ScheduledTask>(), "");
        var whitespaceCase = ScheduleListResult.OkWithWarning(Array.Empty<ScheduledTask>(), "   \n  ");

        Assert.Null(nullCase.Warning);
        Assert.Null(emptyCase.Warning);
        Assert.Null(whitespaceCase.Warning);
    }

    [Fact]
    public void Unavailable_AvailableFalse_TasksEmpty_FailureReasonTrimmed()
    {
        var result = ScheduleListResult.Unavailable("  Task Scheduler service is stopped  ");

        Assert.False(result.Available);
        Assert.Empty(result.Tasks);
        Assert.Equal("Task Scheduler service is stopped", result.FailureReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Unavailable_RejectsBlankReason(string? reason)
    {
        // A blank reason on Unavailable would be a worse signal than the original bug —
        // it'd render an empty-but-failed result with no diagnostic to surface to the user.
        Assert.Throws<ArgumentException>(() => ScheduleListResult.Unavailable(reason!));
    }
}
