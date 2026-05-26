#nullable enable

using System.Linq;
using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R2 regression pins for CrontabParser adjacency and narrow-catch behaviour.
/// </summary>
public sealed class CrontabParserAdjacencyTests
{
    [Fact]
    public void ParseEntries_TwoConsecutiveWinixTagsWithNoCronLine_BothPreservedAsUnknown()
    {
        // Hand-edited crontab can leave two consecutive winix tags with the cron line in
        // between deleted. R2 fix: don't consume the second tag as the first task's cron
        // line — emit an "Unknown" entry for the first and continue parsing.
        string crontab =
            "# winix:foo\n" +
            "# winix:bar\n" +
            "*/5 * * * * curl http://localhost:8080/health\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        Assert.Equal(2, tasks.Count);
        Assert.Equal("foo", tasks[0].Name);
        Assert.Contains("Unknown", tasks[0].Status);
        Assert.Equal("bar", tasks[1].Name);
        Assert.NotNull(tasks[1].NextRun);
    }

    [Fact]
    public void ParseEntries_OrphanWinixTagAtEndOfFile_TaskMarkedUnknown()
    {
        // R2 adjacency fix extended to whitespace-only cron lines: a tag with no following
        // cron line (trailing newline produces a blank "next line") would previously emit
        // a task with empty Schedule/Command but Status="Enabled" — a misleading row in
        // the user's listing. Now surfaces as Unknown so the corruption is visible.
        string crontab = "# winix:dangling\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        Assert.Single(tasks);
        Assert.Equal("dangling", tasks[0].Name);
        Assert.Contains("Unknown", tasks[0].Status);
        Assert.Equal("", tasks[0].Schedule);
        Assert.Equal("", tasks[0].Command);
    }

    [Fact]
    public void ParseEntries_MalformedCronAdjacentToValidCron_ValidEntryStillGetsNextRun()
    {
        // R1 narrow-catch (FormatException + InvalidOperationException only). A malformed
        // cron line in one entry must NOT cascade to swallow exceptions in the next entry's
        // computation. Pins the contract: bad next_run = null, good next_run != null,
        // BOTH tasks produced.
        string crontab =
            "# winix:bad\n" +
            "totally not a cron expression curl x\n" +
            "# winix:good\n" +
            "0 2 * * * dotnet build\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        Assert.Equal(2, tasks.Count);
        var bad = tasks.Single(t => t.Name == "bad");
        var good = tasks.Single(t => t.Name == "good");

        Assert.Null(bad.NextRun);
        Assert.NotNull(good.NextRun);
    }

    [Fact]
    public void ParseEntries_BlankLineBetweenTagAndCronLine_TaskStillExtracted()
    {
        // Regression test for the R2-introduced over-aggressive orphan-at-EOF check
        // (silent-failure-hunter R3 finding F3): a user-typed crontab with a readability
        // blank line between the tag and the cron line was misattributed — the tag
        // emitted an Unknown placeholder and the actual cron line was either silently
        // dropped (winixOnly=true) or shown as an untagged entry with the user's tag
        // appearing destroyed (winixOnly=false). Now the parser skips blank lines
        // between tag and cron entry and treats them as a single task.
        string crontab =
            "# winix:foo\n" +
            "\n" +
            "0 2 * * * /usr/bin/run.sh\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        Assert.Single(tasks);
        Assert.Equal("foo", tasks[0].Name);
        Assert.Equal("Enabled", tasks[0].Status);
        Assert.Equal("0 2 * * *", tasks[0].Schedule);
        Assert.Equal("/usr/bin/run.sh", tasks[0].Command);
    }

    [Fact]
    public void ParseEntries_MultipleBlankLinesBetweenTagAndCron_StillExtractsCorrectly()
    {
        // Two or three blank lines between tag and cron line should still resolve to
        // a single task — defensive against unusual editor configurations or merge
        // artifacts that introduce extra whitespace.
        string crontab =
            "# winix:multi-blank\n" +
            "\n" +
            "  \n" +   // line with only whitespace
            "\t\n" +   // tab-only line
            "*/15 * * * * /opt/poller\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        Assert.Single(tasks);
        Assert.Equal("multi-blank", tasks[0].Name);
        Assert.Equal("*/15 * * * *", tasks[0].Schedule);
    }

    [Fact]
    public void ParseEntries_BlankLinesBetweenAdjacentWinixTags_BothMarkedUnknown()
    {
        // Blank lines between two adjacent winix tags don't somehow rescue the first —
        // the second tag is still a distinct entry.
        string crontab =
            "# winix:first\n" +
            "\n" +
            "# winix:second\n" +
            "*/5 * * * * /run\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        Assert.Equal(2, tasks.Count);
        Assert.Equal("first", tasks[0].Name);
        Assert.Contains("Unknown", tasks[0].Status);
        Assert.Equal("second", tasks[1].Name);
        Assert.Equal("Enabled", tasks[1].Status);
    }

    [Fact]
    public void ParseEntries_DisabledMalformedCron_StillReportsDisabledStatus()
    {
        string crontab =
            "# winix:broken\n" +
            "# bogus stuff\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        Assert.Single(tasks);
        Assert.Equal("broken", tasks[0].Name);
        Assert.Equal("Disabled", tasks[0].Status);
        Assert.Null(tasks[0].NextRun);
    }
}
