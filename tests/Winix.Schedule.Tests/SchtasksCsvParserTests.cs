#nullable enable

using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

public sealed class SchtasksCsvParserTests
{
    [Fact]
    public void Parse_SingleRow_ReturnsTask()
    {
        // Simplified CSV with the key columns. Real schtasks output has 29 columns.
        string csv = "\"MYPC\",\"\\Winix\\health-check\",\"4/13/2026 2:00:00 PM\",\"Ready\",\"Interactive only\",\"4/12/2026 2:00:00 PM\",\"0\",\"troy\",\"curl http://localhost:8080/health\",\"N/A\",\"*/5 * * * *\",\"Enabled\",\"Disabled\",\"Stop On Battery Mode, No Start On Batteries\",\"troy\",\"Disabled\",\"72:00:00\",\"Scheduling data is not available in this format.\",\"One Time Only, Minute\",\"2:00:00 PM\",\"4/12/2026\",\"N/A\",\"N/A\",\"N/A\",\"0 Hour(s), 5 Minute(s)\",\"N/A\",\"N/A\",\"Disabled\"";

        var tasks = SchtasksCsvParser.Parse(csv, @"\Winix");

        Assert.Single(tasks);
        Assert.Equal("health-check", tasks[0].Name);
        Assert.Equal("Enabled", tasks[0].Status);
        Assert.Equal("curl http://localhost:8080/health", tasks[0].Command);
        Assert.Equal("*/5 * * * *", tasks[0].Schedule);
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsEmpty()
    {
        var tasks = SchtasksCsvParser.Parse("", @"\Winix");

        Assert.Empty(tasks);
    }

    [Fact]
    public void Parse_MultipleRows_ReturnsAll()
    {
        string csv =
            "\"MYPC\",\"\\Winix\\task-a\",\"N/A\",\"Ready\",\"Interactive only\",\"N/A\",\"0\",\"troy\",\"cmd /c echo a\",\"N/A\",\"0 0 * * *\",\"Enabled\",\"Disabled\",\"N/A\",\"troy\",\"Disabled\",\"72:00:00\",\"N/A\",\"Daily\",\"12:00:00 AM\",\"4/12/2026\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"Disabled\"\n" +
            "\"MYPC\",\"\\Winix\\task-b\",\"N/A\",\"Ready\",\"Interactive only\",\"N/A\",\"0\",\"troy\",\"cmd /c echo b\",\"N/A\",\"0 2 * * *\",\"Disabled\",\"Disabled\",\"N/A\",\"troy\",\"Disabled\",\"72:00:00\",\"N/A\",\"Daily\",\"2:00:00 AM\",\"4/12/2026\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"Disabled\"";

        var tasks = SchtasksCsvParser.Parse(csv, @"\Winix");

        Assert.Equal(2, tasks.Count);
        Assert.Equal("task-a", tasks[0].Name);
        Assert.Equal("task-b", tasks[1].Name);
    }

    [Fact]
    public void Parse_StripsFolderPrefix_FromTaskName()
    {
        string csv = "\"MYPC\",\"\\Winix\\my-task\",\"N/A\",\"Ready\",\"Interactive only\",\"N/A\",\"0\",\"troy\",\"cmd\",\"N/A\",\"comment\",\"Enabled\",\"Disabled\",\"N/A\",\"troy\",\"Disabled\",\"72:00:00\",\"N/A\",\"Daily\",\"12:00:00 AM\",\"4/12/2026\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"Disabled\"";

        var tasks = SchtasksCsvParser.Parse(csv, @"\Winix");

        Assert.Equal("my-task", tasks[0].Name);
    }

    [Fact]
    public void ParseCsvLine_HandlesQuotedCommas()
    {
        string line = "\"value,with,commas\",\"simple\"";

        string[] fields = SchtasksCsvParser.ParseCsvLine(line);

        Assert.Equal(2, fields.Length);
        Assert.Equal("value,with,commas", fields[0]);
        Assert.Equal("simple", fields[1]);
    }

    [Fact]
    public void ParseCsvLine_HandlesEscapedQuotes()
    {
        string line = "\"value \"\"with\"\" quotes\",\"simple\"";

        string[] fields = SchtasksCsvParser.ParseCsvLine(line);

        Assert.Equal("value \"with\" quotes", fields[0]);
    }
}
