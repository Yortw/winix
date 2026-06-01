#nullable enable

using System;
using System.Collections.Generic;
using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// Regression tests locking schedule's --color emission paths at the formatter layer.
/// Guards against a future regression where colour is silently unwired.
/// </summary>
/// <remarks>
/// Seam note: schedule has no Cli.Run library seam — all orchestration lives in
/// Program.cs with direct Console.* references. The colour decision is wired at:
///   Program.Main: bool useColor = result.ResolveColor(checkStdErr: true)
///   RunList: Formatting.FormatTable(tasks, showFolder, useColor: useColor)
///   WriteActionResult: Formatting.FormatResult(scheduleResult, useColor)
///   RunHistory: Formatting.FormatHistory(records, useColor)
/// This test suite covers the formatter layer directly — confirming that each
/// formatted method emits ESC when useColor=true and suppresses it when false.
/// Production wiring (that Program.Main's useColor bool is actually forwarded here)
/// is verified by code inspection: Program.cs line "bool useColor = result.ResolveColor..."
/// is the single assignment that flows to all three formatters via their respective
/// SafeWrite calls. A process-spawn colour regression test would require invoking a
/// system scheduler backend (schtasks/crontab), which is unsuitable for unit tests.
/// </remarks>
public sealed class ColorTests
{
    private static readonly string Esc = ((char)27).ToString();

    // ── FormatTable (list subcommand) ──────────────────────────────────────────────

    [Fact]
    public void FormatTable_ColorTrue_HeaderContainsEscape()
    {
        // FormatTable wraps the header row in AnsiColor.Dim + AnsiColor.Reset.
        var tasks = new List<ScheduledTask>
        {
            new ScheduledTask("health-check", "*/5 * * * *",
                new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc), "Enabled",
                "curl http://localhost/health", @"\Winix"),
        };

        string output = Formatting.FormatTable(tasks, showFolder: false, useColor: true);

        Assert.Contains(Esc, output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatTable_ColorFalse_NoEscape()
    {
        var tasks = new List<ScheduledTask>
        {
            new ScheduledTask("health-check", "*/5 * * * *",
                new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc), "Enabled",
                "curl http://localhost/health", @"\Winix"),
        };

        string output = Formatting.FormatTable(tasks, showFolder: false, useColor: false);

        Assert.DoesNotContain(Esc, output, StringComparison.Ordinal);
        // Table content must still be present with colour off.
        Assert.Contains("health-check", output, StringComparison.Ordinal);
    }

    // ── FormatResult (add/remove/enable/disable/run subcommands) ──────────────────

    [Fact]
    public void FormatResult_Success_ColorTrue_ContainsEscape()
    {
        // FormatResult emits AnsiColor.Green for a successful result.
        var result = ScheduleResult.Ok("Task created.");

        string output = Formatting.FormatResult(result, useColor: true);

        Assert.Contains(Esc, output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatResult_Success_ColorFalse_NoEscape()
    {
        var result = ScheduleResult.Ok("Task created.");

        string output = Formatting.FormatResult(result, useColor: false);

        Assert.DoesNotContain(Esc, output, StringComparison.Ordinal);
        Assert.Contains("Task created.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatResult_Failure_ColorTrue_ContainsEscape()
    {
        // FormatResult emits AnsiColor.Red for a failed result.
        var result = ScheduleResult.Fail("Task not found.");

        string output = Formatting.FormatResult(result, useColor: true);

        Assert.Contains(Esc, output, StringComparison.Ordinal);
    }

    // ── FormatHistory (history subcommand) ────────────────────────────────────────

    [Fact]
    public void FormatHistory_ColorTrue_HeaderContainsEscape()
    {
        // FormatHistory wraps the header row in AnsiColor.Dim + AnsiColor.Reset.
        var records = new List<TaskRunRecord>
        {
            new TaskRunRecord(new DateTimeOffset(2026, 6, 1, 2, 0, 0, TimeSpan.Zero), 0, TimeSpan.FromSeconds(1.5)),
        };

        string output = Formatting.FormatHistory(records, useColor: true);

        Assert.Contains(Esc, output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatHistory_ColorFalse_NoEscape()
    {
        var records = new List<TaskRunRecord>
        {
            new TaskRunRecord(new DateTimeOffset(2026, 6, 1, 2, 0, 0, TimeSpan.Zero), 0, TimeSpan.FromSeconds(1.5)),
        };

        string output = Formatting.FormatHistory(records, useColor: false);

        Assert.DoesNotContain(Esc, output, StringComparison.Ordinal);
        Assert.Contains("Time", output, StringComparison.Ordinal);
    }
}
