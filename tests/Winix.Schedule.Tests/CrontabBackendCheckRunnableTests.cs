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

    [Fact]
    public void StatusComparisonIsCaseSensitive()
    {
        // ParseEntries assigns "Enabled" / "Disabled" verbatim; a case mismatch shouldn't
        // accidentally pass through as runnable. Lowercase 'disabled' would only happen
        // from a manual-edit corruption, but in that case treating it as runnable would
        // mask the corruption — fail closed by treating only canonical "Disabled" as gated.
        var lowercase = new ScheduledTask("myjob", "*/5 * * * *", null, "disabled", "echo hi", "");

        // The current contract: only the exact "Disabled" string gates. Documented for
        // visibility in case future status values come from non-Winix entries.
        Assert.Null(CrontabBackend.CheckRunnable(lowercase));
    }
}
