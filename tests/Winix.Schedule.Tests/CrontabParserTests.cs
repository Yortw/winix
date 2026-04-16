#nullable enable

using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

public sealed class CrontabParserTests
{
    [Fact]
    public void ParseEntries_WinixTagged_ReturnsTask()
    {
        string crontab =
            "# winix:health-check\n" +
            "*/5 * * * * curl http://localhost:8080/health\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        Assert.Single(tasks);
        Assert.Equal("health-check", tasks[0].Name);
        Assert.Equal("*/5 * * * *", tasks[0].Schedule);
        Assert.Equal("curl http://localhost:8080/health", tasks[0].Command);
        Assert.Equal("Enabled", tasks[0].Status);
    }

    [Fact]
    public void ParseEntries_DisabledEntry_ReturnsDisabled()
    {
        string crontab =
            "# winix:my-task\n" +
            "# */5 * * * * curl http://localhost/health\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        Assert.Single(tasks);
        Assert.Equal("my-task", tasks[0].Name);
        Assert.Equal("Disabled", tasks[0].Status);
    }

    [Fact]
    public void ParseEntries_NonWinixEntries_Excluded_WhenWinixOnly()
    {
        string crontab =
            "0 2 * * * /usr/bin/backup.sh\n" +
            "# winix:health-check\n" +
            "*/5 * * * * curl http://localhost:8080/health\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        Assert.Single(tasks);
        Assert.Equal("health-check", tasks[0].Name);
    }

    [Fact]
    public void ParseEntries_All_IncludesNonWinix()
    {
        string crontab =
            "0 2 * * * /usr/bin/backup.sh\n" +
            "# winix:health-check\n" +
            "*/5 * * * * curl http://localhost:8080/health\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: false);

        Assert.Equal(2, tasks.Count);
    }

    [Fact]
    public void ParseEntries_Empty_ReturnsEmpty()
    {
        var tasks = CrontabParser.ParseEntries("", winixOnly: true);

        Assert.Empty(tasks);
    }

    [Fact]
    public void ParseEntries_OnlyComments_ReturnsEmpty()
    {
        string crontab =
            "# This is a regular comment\n" +
            "# Another comment\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        Assert.Empty(tasks);
    }

    [Fact]
    public void AddEntry_EmptyCrontab_AddsTagAndLine()
    {
        string result = CrontabParser.AddEntry("", "health-check", "*/5 * * * *", "curl http://localhost:8080/health");

        Assert.Contains("# winix:health-check", result);
        Assert.Contains("*/5 * * * * curl http://localhost:8080/health", result);
    }

    [Fact]
    public void AddEntry_ExistingCrontab_Appends()
    {
        string existing = "0 2 * * * /usr/bin/backup.sh\n";

        string result = CrontabParser.AddEntry(existing, "health-check", "*/5 * * * *", "curl http://localhost/health");

        Assert.StartsWith("0 2 * * * /usr/bin/backup.sh", result);
        Assert.Contains("# winix:health-check", result);
    }

    [Fact]
    public void RemoveEntry_RemovesTagAndCommandLine()
    {
        string crontab =
            "0 2 * * * /usr/bin/backup.sh\n" +
            "# winix:health-check\n" +
            "*/5 * * * * curl http://localhost:8080/health\n";

        string result = CrontabParser.RemoveEntry(crontab, "health-check");

        Assert.DoesNotContain("health-check", result);
        Assert.DoesNotContain("curl", result);
        Assert.Contains("backup.sh", result);
    }

    [Fact]
    public void DisableEntry_CommentsOutCommandLine()
    {
        string crontab =
            "# winix:health-check\n" +
            "*/5 * * * * curl http://localhost:8080/health\n";

        string result = CrontabParser.DisableEntry(crontab, "health-check");

        Assert.Contains("# winix:health-check", result);
        Assert.Contains("# */5 * * * * curl http://localhost:8080/health", result);
    }

    [Fact]
    public void EnableEntry_UncommentsCommandLine()
    {
        string crontab =
            "# winix:health-check\n" +
            "# */5 * * * * curl http://localhost:8080/health\n";

        string result = CrontabParser.EnableEntry(crontab, "health-check");

        Assert.Contains("# winix:health-check", result);
        Assert.Contains("*/5 * * * * curl http://localhost:8080/health", result);
        // Should NOT have a double-hash comment on the command line.
        Assert.DoesNotContain("# */5", result);
    }

    [Fact]
    public void ExtractCommand_ReturnsCommandPortion()
    {
        string command = CrontabParser.ExtractCommand("*/5 * * * * curl http://localhost:8080/health");

        Assert.Equal("curl http://localhost:8080/health", command);
    }

    [Fact]
    public void ExtractCronFields_ReturnsCronPortion()
    {
        string cron = CrontabParser.ExtractCronFields("*/5 * * * * curl http://localhost:8080/health");

        Assert.Equal("*/5 * * * *", cron);
    }
}
