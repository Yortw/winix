#nullable enable

using System.Collections.Generic;
using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class FormattingTests
{
    // ---------------------------------------------------------------------------
    // FormatToolResult
    // ---------------------------------------------------------------------------

    [Fact]
    public void FormatToolResult_Success_ShowsCheckmark()
    {
        var result = Formatting.FormatToolResult("timeit", "winget", success: true, error: null, useColor: false);

        Assert.Contains("timeit", result);
        Assert.Contains("winget", result);
        Assert.Contains("✓", result);
    }

    [Fact]
    public void FormatToolResult_Failure_ShowsCross()
    {
        var result = Formatting.FormatToolResult("squeeze", "scoop", success: false, error: "package not found", useColor: false);

        Assert.Contains("✗", result);
        Assert.Contains("package not found", result);
    }

    [Fact]
    public void FormatToolResult_Success_WithColor_ContainsGreenEscape()
    {
        var result = Formatting.FormatToolResult("timeit", "winget", success: true, error: null, useColor: true);

        Assert.Contains("\x1b[32m", result);
        Assert.Contains("\x1b[0m", result);
    }

    [Fact]
    public void FormatToolResult_Failure_WithColor_ContainsRedEscape()
    {
        var result = Formatting.FormatToolResult("timeit", "winget", success: false, error: "oops", useColor: true);

        Assert.Contains("\x1b[31m", result);
    }

    // ---------------------------------------------------------------------------
    // FormatStatusSummary
    // ---------------------------------------------------------------------------

    [Fact]
    public void FormatStatusSummary_AllInstalled()
    {
        var statuses = new List<ToolStatus>
        {
            new ToolStatus("timeit", isInstalled: true, version: "1.0.0", packageManager: "winget"),
            new ToolStatus("squeeze", isInstalled: true, version: "1.0.0", packageManager: "winget"),
        };

        var result = Formatting.FormatStatusSummary(statuses, totalTools: 2);

        Assert.Contains("2 of 2", result);
        Assert.Contains("winget", result);
    }

    [Fact]
    public void FormatStatusSummary_PartialInstall_ShowsMixed()
    {
        var statuses = new List<ToolStatus>
        {
            new ToolStatus("timeit", isInstalled: true, version: "1.0.0", packageManager: "winget"),
            new ToolStatus("squeeze", isInstalled: true, version: "1.0.0", packageManager: "winget"),
            new ToolStatus("peep", isInstalled: false, version: null, packageManager: null),
        };

        var result = Formatting.FormatStatusSummary(statuses, totalTools: 3);

        Assert.Contains("2 of 3", result);
    }

    [Fact]
    public void FormatStatusSummary_NoneInstalled()
    {
        var statuses = new List<ToolStatus>
        {
            new ToolStatus("timeit", isInstalled: false, version: null, packageManager: null),
        };

        var result = Formatting.FormatStatusSummary(statuses, totalTools: 1);

        Assert.Contains("0 of 1", result);
    }

    // ---------------------------------------------------------------------------
    // FormatListTable
    // ---------------------------------------------------------------------------

    [Fact]
    public void FormatListTable_ShowsToolInfo()
    {
        var statuses = new List<ToolStatus>
        {
            new ToolStatus("timeit", isInstalled: true, version: "1.2.3", packageManager: "winget"),
        };
        var descriptions = new Dictionary<string, string>
        {
            { "timeit", "Time a command." },
        };

        var result = Formatting.FormatListTable(statuses, descriptions, useColor: false);

        Assert.Contains("timeit", result);
        Assert.Contains("1.2.3", result);
        Assert.Contains("winget", result);
    }

    // ---------------------------------------------------------------------------
    // FormatDryRun
    // ---------------------------------------------------------------------------

    [Fact]
    public void FormatDryRun_ShowsCommandThatWouldRun()
    {
        var result = Formatting.FormatDryRun("winget", new[] { "install", "--id", "Winix.TimeIt" });

        Assert.Contains("winget", result);
        Assert.Contains("install", result);
        Assert.Contains("--id", result);
        Assert.Contains("Winix.TimeIt", result);
    }
}
