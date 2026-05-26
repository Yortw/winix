#nullable enable

using System.Linq;
using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R4 regression pins for the lifecycle methods (RemoveEntry / DisableEntry / EnableEntry)
/// — R3 ce3f434 made ParseEntries blank-line tolerant but did NOT update the lifecycle
/// methods, producing silent task-lifecycle corruption: 'schedule list' showed the task
/// correctly while 'schedule disable foo' wrote '# ' before the blank line and left the
/// actual cron line firing. R4 c1ef0... extracted the shared FindCronLineIndex helper to
/// keep all four code paths in agreement.
/// </summary>
public sealed class CrontabParserBlankLineLifecycleTests
{
    [Fact]
    public void RemoveEntry_BlankLineBetweenTagAndCron_RemovesAllThreeLines()
    {
        // Without the R4 fix, RemoveEntry skipped only the tag + the immediately-next blank
        // line, leaving the actual cron line orphaned in the crontab. The user would see
        // 'Removed task foo.' but the task would continue to fire.
        string crontab =
            "# winix:foo\n" +
            "\n" +
            "0 2 * * * /usr/bin/run\n" +
            "# winix:bar\n" +
            "*/5 * * * * /other\n";

        string result = CrontabParser.RemoveEntry(crontab, "foo");

        Assert.DoesNotContain("# winix:foo", result);
        Assert.DoesNotContain("/usr/bin/run", result);
        // bar's entry must be unaffected.
        Assert.Contains("# winix:bar", result);
        Assert.Contains("*/5 * * * * /other", result);
    }

    [Fact]
    public void DisableEntry_BlankLineBetweenTagAndCron_PrefixesActualCronLine()
    {
        // Pre-fix bug: DisableEntry advanced i++ from the tag, landed on the blank line,
        // prepended '# ' to the empty string. The actual cron line further down kept
        // firing.
        string crontab =
            "# winix:nightly\n" +
            "\n" +
            "0 2 * * * /backup\n";

        string result = CrontabParser.DisableEntry(crontab, "nightly");

        // The actual cron line must now be commented.
        Assert.Contains("# 0 2 * * * /backup", result);
        // The blank-line spacing should survive the round-trip.
        Assert.Contains("# winix:nightly\n\n# 0 2 * * * /backup", result);
    }

    [Fact]
    public void EnableEntry_BlankLineBetweenTagAndDisabledCron_UncommentsActualLine()
    {
        // Inverse of the disable case: the disabled cron lives below a blank line. Pre-fix,
        // EnableEntry tried to enable the blank line (no-op), leaving the actual disabled
        // line still commented.
        string crontab =
            "# winix:nightly\n" +
            "\n" +
            "# 0 2 * * * /backup\n";

        string result = CrontabParser.EnableEntry(crontab, "nightly");

        // The disabled cron line must now be active.
        Assert.Contains("\n0 2 * * * /backup", result);
        Assert.DoesNotContain("# 0 2 * * * /backup", result);
    }

    [Fact]
    public void RemoveEntry_OrphanTag_RemovesOnlyTheTagLine()
    {
        // An orphan tag (no cron line, EOF or another tag follows) — RemoveEntry should
        // strip the tag without touching any blank lines that follow.
        string crontab =
            "# winix:dangling\n" +
            "\n" +
            "# winix:other\n" +
            "*/5 * * * * /run\n";

        string result = CrontabParser.RemoveEntry(crontab, "dangling");

        Assert.DoesNotContain("# winix:dangling", result);
        // The other tag's entry must still be intact.
        Assert.Contains("# winix:other", result);
        Assert.Contains("*/5 * * * * /run", result);
    }

    [Fact]
    public void DisableEntry_OrphanTag_NoOp()
    {
        // Orphan tag — no cron line to toggle. The crontab content should be unchanged.
        string crontab = "# winix:dangling\n";
        string result = CrontabParser.DisableEntry(crontab, "dangling");
        Assert.Equal(crontab, result);
    }

    [Fact]
    public void RemoveEntry_TaskWithMultipleBlankLinesBetweenTagAndCron_RemovesEverything()
    {
        // Stress test: 3 blank lines between tag and cron — should still remove cleanly.
        string crontab =
            "# winix:spaced\n" +
            "\n" +
            "  \n" +
            "\t\n" +
            "0 4 * * * /run\n" +
            "# winix:other\n" +
            "0 5 * * * /other\n";

        string result = CrontabParser.RemoveEntry(crontab, "spaced");

        Assert.DoesNotContain("# winix:spaced", result);
        Assert.DoesNotContain("/run", result);
        Assert.Contains("# winix:other", result);
        Assert.Contains("/other", result);
    }
}
