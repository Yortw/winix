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

    // ---------------------------------------------------------------------------
    // FormatListJson / FormatStatusJson
    // ---------------------------------------------------------------------------

    [Fact]
    public void FormatListJson_ProducesValidJsonWithStableShape()
    {
        var statuses = new List<ToolStatus>
        {
            new("timeit", isInstalled: true, version: "0.3.0", packageManager: "winget"),
            new("squeeze", isInstalled: false, version: null, packageManager: null),
        };

        string json = Formatting.FormatListJson(statuses, "0.3.0", PlatformId.Windows);

        // Round-trip via System.Text.Json to confirm the output is well-formed JSON and
        // pin the shape consumers will rely on.
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("winix", root.GetProperty("tool").GetString());
        Assert.Equal("list", root.GetProperty("command").GetString());
        Assert.Equal("0.3.0", root.GetProperty("version").GetString());
        Assert.Equal("windows", root.GetProperty("platform").GetString());

        var tools = root.GetProperty("tools");
        Assert.Equal(2, tools.GetArrayLength());

        var first = tools[0];
        Assert.Equal("timeit", first.GetProperty("name").GetString());
        Assert.True(first.GetProperty("installed").GetBoolean());
        Assert.Equal("0.3.0", first.GetProperty("version").GetString());
        Assert.Equal("winget", first.GetProperty("via").GetString());

        var second = tools[1];
        Assert.Equal("squeeze", second.GetProperty("name").GetString());
        Assert.False(second.GetProperty("installed").GetBoolean());
        // Not installed: version and via are JSON null, not omitted.
        Assert.Equal(System.Text.Json.JsonValueKind.Null, second.GetProperty("version").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, second.GetProperty("via").ValueKind);
    }

    [Fact]
    public void FormatStatusJson_AggregatesByPackageManager()
    {
        var statuses = new List<ToolStatus>
        {
            new("timeit", isInstalled: true, version: "0.3.0", packageManager: "winget"),
            new("squeeze", isInstalled: true, version: "0.3.0", packageManager: "winget"),
            new("peep", isInstalled: true, version: "0.3.0", packageManager: "scoop"),
            new("wargs", isInstalled: false, version: null, packageManager: null),
        };

        string json = Formatting.FormatStatusJson(statuses, totalTools: 4, "0.3.0", PlatformId.MacOS);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("winix", root.GetProperty("tool").GetString());
        Assert.Equal("status", root.GetProperty("command").GetString());
        Assert.Equal("macos", root.GetProperty("platform").GetString());
        Assert.Equal(3, root.GetProperty("installed").GetInt32());
        Assert.Equal(4, root.GetProperty("total").GetInt32());

        var byPm = root.GetProperty("by_pm");
        Assert.Equal(2, byPm.GetProperty("winget").GetInt32());
        Assert.Equal(1, byPm.GetProperty("scoop").GetInt32());
    }

    [Fact]
    public void FormatToolError_OmitsViaAnnotation()
    {
        // F10: when a tool isn't in the manifest at all, there's no PM to attribute the
        // error to — the previous shape "✗ X (via winget) — not in manifest" was
        // misleading. The new helper keeps the ✗ + reason pair without the (via X) part.
        string result = Formatting.FormatToolError("nonexistent-tool", "not in manifest", useColor: false);

        Assert.Contains("✗", result);
        Assert.Contains("nonexistent-tool", result);
        Assert.Contains("not in manifest", result);
        Assert.DoesNotContain("via", result);
    }

    [Fact]
    public void FormatListJson_PlatformLinux_EmitsLowercaseLiteral()
    {
        var statuses = new List<ToolStatus>();
        string json = Formatting.FormatListJson(statuses, "0.0.0", PlatformId.Linux);

        Assert.Contains("\"platform\":\"linux\"", json, StringComparison.Ordinal);
    }
}
