#nullable enable

using System;
using System.Collections.Generic;
using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class FormattingTests
{
    private static WaitResult Ready() => new(
        Ready: true, TimedOut: false, Attempts: 3, Elapsed: TimeSpan.FromMilliseconds(1234),
        LastChecks: new List<CheckResult>
        {
            new("internet", null, true, "204 via https://www.gstatic.com/generate_204"),
            new("url", "https://api/health", true, "200"),
        });

    [Fact]
    public void Json_contains_top_level_fields()
    {
        string json = Formatting.FormatJson(Ready(), "1.2.3");
        Assert.Contains("\"ready\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"timed_out\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"attempts\":3", json, StringComparison.Ordinal);
        Assert.Contains("\"elapsed_ms\":1234", json, StringComparison.Ordinal);
        Assert.Contains("\"tool\":\"online\"", json, StringComparison.Ordinal);
        Assert.Contains("\"version\":\"1.2.3\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_contains_per_check_objects_with_target()
    {
        string json = Formatting.FormatJson(Ready(), "1.2.3");
        Assert.Contains("\"kind\":\"internet\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"url\"", json, StringComparison.Ordinal);
        Assert.Contains("\"target\":\"https://api/health\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ok\":true", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_ready_mentions_ready()
    {
        string summary = Formatting.FormatSummary(Ready(), useColor: false);
        Assert.Contains("ready", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Summary_timeout_mentions_timed_out()
    {
        var timedOut = new WaitResult(false, true, 300, TimeSpan.FromMinutes(10),
            new List<CheckResult> { new("internet", null, false, "no network route") });
        string summary = Formatting.FormatSummary(timedOut, useColor: false);
        Assert.Contains("timed out", summary, StringComparison.OrdinalIgnoreCase);
    }
}
