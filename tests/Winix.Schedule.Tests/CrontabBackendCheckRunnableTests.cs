#nullable enable

using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R4 contract pins for <see cref="CrontabBackend.CheckRunnable"/> — the gate that decides
/// whether an on-demand run should proceed. Pre-fix the crontab Run path silently launched
/// disabled tasks (cross-platform divergence with schtasks /Run, which rejects them).
/// </summary>
public sealed class CrontabBackendCheckRunnableTests
{
    [Fact]
    public void EnabledTask_ReturnsNull()
    {
        var task = new ScheduledTask("myjob", "*/5 * * * *", null, "Enabled", "echo hi", "");

        Assert.Null(CrontabBackend.CheckRunnable(task));
    }

    [Fact]
    public void DisabledTask_ReturnsFailWithUsefulMessage()
    {
        var task = new ScheduledTask("myjob", "*/5 * * * *", null, "Disabled", "echo hi", "");

        var result = CrontabBackend.CheckRunnable(task);

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Contains("disabled", result.Message);
        Assert.Contains("myjob", result.Message);
        Assert.Contains("enable", result.Message);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("DISABLED")]
    [InlineData("Disabled")]
    [InlineData("DiSaBlEd")]
    public void StatusComparisonIsCaseInsensitive_FailClosed(string statusValue)
    {
        // R5 contract: any casing of "disabled" gates the run. Pre-R5 the compare was
        // Ordinal — fail-OPEN to non-canonical casings, which would silently launch a
        // task that some future ScheduledTask source (different parser, JSON deserialise,
        // localised Windows SKU yielding upper-case state) had marked disabled. The
        // cost of fail-closed is zero; the benefit is no foot-gun for future code paths.
        var task = new ScheduledTask("myjob", "*/5 * * * *", null, statusValue, "echo hi", "");

        var result = CrontabBackend.CheckRunnable(task);
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Contains("disabled", result.Message);
    }

    [Theory]
    [InlineData("Enabled")]
    [InlineData("enabled")]
    [InlineData("Pending")]
    [InlineData("Unknown (no cron line)")]
    [InlineData("")]
    public void NonDisabledStatuses_AllPassThrough(string statusValue)
    {
        // Anything that isn't a casing of "disabled" runs. The orphan-tag and other
        // non-standard status values pass through (they're surfaced by parser-level tests,
        // not by the runtime gate).
        var task = new ScheduledTask("myjob", "*/5 * * * *", null, statusValue, "echo hi", "");
        Assert.Null(CrontabBackend.CheckRunnable(task));
    }
}
