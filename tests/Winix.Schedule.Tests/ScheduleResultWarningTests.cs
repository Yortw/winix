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
    public void FormatResult_Success_WithWarning_UseColorTrue_EmitsGreenTickAndYellowWarning()
    {
        var r = ScheduleResult.OkWithWarning("Created task 'foo'.", "PAM notice");
        string output = Formatting.FormatResult(r, useColor: true);

        // Pin specifically: success tick uses green (\x1b[32m), warning prefix uses yellow
        // (\x1b[33m), and both are followed by reset (\x1b[0m). Without the colour-specific
        // assertions, a regression that dropped the yellow OR swapped warning to red would
        // still pass a generic "contains \x1b[" check.
        Assert.Contains("\x1b[32m", output);  // green for success tick
        Assert.Contains("\x1b[33m", output);  // yellow for warning prefix
        Assert.Contains("\x1b[0m", output);   // reset
    }

    [Fact]
    public void FormatResult_Success_NoWarning_UseColorTrue_DoesNotEmitYellow()
    {
        var r = ScheduleResult.Ok("Created task 'foo'.");
        string output = Formatting.FormatResult(r, useColor: true);

        // The yellow code is the warning-line marker. If a regression added an unconditional
        // yellow segment it would slip past the warning-present test above; this counter-test
        // pins that yellow appears ONLY when there's a warning to surface.
        Assert.Contains("\x1b[32m", output);   // green tick still present
        Assert.DoesNotContain("\x1b[33m", output);
    }

    [Fact]
    public void FormatResult_Failure_UseColorTrue_EmitsRedCross()
    {
        var r = ScheduleResult.Fail("backend failure");
        string output = Formatting.FormatResult(r, useColor: true);

        Assert.Contains("\x1b[31m", output);  // red for failure cross
        Assert.Contains("\x1b[0m", output);   // reset
        Assert.DoesNotContain("\x1b[32m", output);  // not green
        Assert.DoesNotContain("\x1b[33m", output);  // failure has no warning channel
    }

    [Fact]
    public void FormatActionJson_NoWarning_OmitsWarningField()
    {
        string json = Formatting.FormatActionJson(
            "add", "foo", "0 2 * * *", null, 0, "success", "0.3.0", warning: null);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("warning", out _));
    }

    [Fact]
    public void FormatActionJson_WithWarning_IncludesWarningField()
    {
        string json = Formatting.FormatActionJson(
            "add", "foo", "0 2 * * *", null, 0, "success", "0.3.0",
            warning: "Skipping line 2: bad day-of-month");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Skipping line 2: bad day-of-month", doc.RootElement.GetProperty("warning").GetString());
    }
}
