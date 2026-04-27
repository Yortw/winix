#nullable enable

using System.Text.Json;
using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R2 regression pins for the stderr-on-success warning surface added by commit c3cde71.
/// Verifies both the ScheduleResult.OkWithWarning factory contract and the human/JSON
/// formatting paths.
/// </summary>
public sealed class ScheduleResultWarningTests
{
    [Fact]
    public void Ok_WithoutWarning_HasNullWarning()
    {
        var r = ScheduleResult.Ok("done");
        Assert.True(r.Success);
        Assert.Null(r.Warning);
    }

    [Fact]
    public void OkWithWarning_NonEmpty_StoresTrimmed()
    {
        var r = ScheduleResult.OkWithWarning("done", "  PAM warning  ");
        Assert.True(r.Success);
        Assert.Equal("PAM warning", r.Warning);
    }

    [Fact]
    public void OkWithWarning_NullOrWhitespace_StoresNull()
    {
        Assert.Null(ScheduleResult.OkWithWarning("done", null).Warning);
        Assert.Null(ScheduleResult.OkWithWarning("done", "").Warning);
        Assert.Null(ScheduleResult.OkWithWarning("done", "   ").Warning);
        Assert.Null(ScheduleResult.OkWithWarning("done", "\n\t").Warning);
    }

    [Fact]
    public void Fail_HasNullWarning()
    {
        var r = ScheduleResult.Fail("nope");
        Assert.False(r.Success);
        Assert.Null(r.Warning);
    }

    [Fact]
    public void FormatResult_Success_NoWarning_DoesNotEmitWarningLine()
    {
        var r = ScheduleResult.Ok("Created task 'foo'.");
        string output = Formatting.FormatResult(r, useColor: false);

        Assert.Contains("✓", output);
        Assert.Contains("Created task 'foo'.", output);
        Assert.DoesNotContain("warning:", output);
    }

    [Fact]
    public void FormatResult_Success_WithWarning_AppendsIndentedWarningLine()
    {
        var r = ScheduleResult.OkWithWarning("Created task 'foo'.", "Skipping line 2: bad day-of-month");
        string output = Formatting.FormatResult(r, useColor: false);

        Assert.Contains("Created task 'foo'.", output);
        Assert.Contains("warning:", output);
        Assert.Contains("Skipping line 2", output);
    }

    [Fact]
    public void FormatResult_Success_WithWarning_UseColorTrue_BracketsBothSuccessAndWarning()
    {
        var r = ScheduleResult.OkWithWarning("Created task 'foo'.", "PAM notice");
        string output = Formatting.FormatResult(r, useColor: true);

        // Pin both the success-tick green and the warning yellow are bracketed by reset.
        Assert.Contains("\x1b[", output);
        Assert.Contains("\x1b[0m", output);
    }

    [Fact]
    public void FormatActionJson_NoWarning_OmitsWarningField()
    {
        string json = Formatting.FormatActionJson(
            "add", "foo", "0 2 * * *", null, 0, "success", "0.4.0", warning: null);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("warning", out _));
    }

    [Fact]
    public void FormatActionJson_WithWarning_IncludesWarningField()
    {
        string json = Formatting.FormatActionJson(
            "add", "foo", "0 2 * * *", null, 0, "success", "0.4.0",
            warning: "Skipping line 2: bad day-of-month");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Skipping line 2: bad day-of-month", doc.RootElement.GetProperty("warning").GetString());
    }
}
