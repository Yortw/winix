#nullable enable

using System;
using System.Collections.Generic;
using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

public sealed class FormattingTests
{
    // ScheduledTask.NextRun is DateTime? so we use .DateTime when constructing from a DateTimeOffset.
    private static readonly DateTimeOffset SampleOffset = new DateTimeOffset(2026, 4, 13, 2, 0, 0, TimeSpan.FromHours(12));
    private static readonly DateTime SampleTime = SampleOffset.DateTime;

    // --- Task table ---

    [Fact]
    public void FormatTable_SingleTask_ContainsNameAndSchedule()
    {
        var tasks = new List<ScheduledTask>
        {
            new ScheduledTask("health-check", "*/5 * * * *", SampleTime, "Enabled", "curl http://localhost:8080/health", @"\Winix"),
        };

        string output = Formatting.FormatTable(tasks, showFolder: false, useColor: false);

        Assert.Contains("health-check", output);
        Assert.Contains("*/5 * * * *", output);
        Assert.Contains("Enabled", output);
    }

    [Fact]
    public void FormatTable_WithFolder_ShowsFolderColumn()
    {
        var tasks = new List<ScheduledTask>
        {
            new ScheduledTask("task1", "0 2 * * *", SampleTime, "Enabled", "cmd", @"\Winix"),
        };

        string output = Formatting.FormatTable(tasks, showFolder: true, useColor: false);

        Assert.Contains("Folder", output);
        Assert.Contains(@"\Winix", output);
    }

    [Fact]
    public void FormatTable_WithColor_ContainsAnsi()
    {
        var tasks = new List<ScheduledTask>
        {
            new ScheduledTask("task1", "0 2 * * *", SampleTime, "Ready", "cmd", @"\"),
        };

        string output = Formatting.FormatTable(tasks, showFolder: false, useColor: true);

        // Header is dimmed — any ANSI escape sequence present.
        Assert.Contains("\x1b[", output);
    }

    [Fact]
    public void FormatTable_NoColor_NoAnsi()
    {
        var tasks = new List<ScheduledTask>
        {
            new ScheduledTask("task1", "0 2 * * *", SampleTime, "Ready", "cmd", @"\"),
        };

        string output = Formatting.FormatTable(tasks, showFolder: false, useColor: false);

        Assert.DoesNotContain("\x1b[", output);
    }

    [Fact]
    public void FormatTable_Empty_ReturnsHeader()
    {
        string output = Formatting.FormatTable(new List<ScheduledTask>(), showFolder: false, useColor: false);

        Assert.Contains("Name", output);
    }

    // --- History ---

    [Fact]
    public void FormatHistory_ContainsExitCode()
    {
        var records = new List<TaskRunRecord>
        {
            new TaskRunRecord(SampleOffset, 0, TimeSpan.FromSeconds(1.2)),
        };

        string output = Formatting.FormatHistory(records, useColor: false);

        Assert.Contains("0", output);
        Assert.Contains("1.2s", output);
    }

    [Fact]
    public void FormatHistory_Empty_ReturnsMessage()
    {
        string output = Formatting.FormatHistory(new List<TaskRunRecord>(), useColor: false);

        // Empty list still renders the header row.
        Assert.Contains("Time", output);
    }

    // --- Next occurrences ---

    [Fact]
    public void FormatNextOccurrences_ShowsFormattedTimes()
    {
        var times = new List<DateTimeOffset>
        {
            new DateTimeOffset(2026, 4, 13, 2, 0, 0, TimeSpan.FromHours(12)),
            new DateTimeOffset(2026, 4, 14, 2, 0, 0, TimeSpan.FromHours(12)),
        };

        string output = Formatting.FormatNextOccurrences("0 2 * * *", times);

        Assert.Contains("Next 2 occurrences of: 0 2 * * *", output);
        Assert.Contains("2026-04-13", output);
        Assert.Contains("2026-04-14", output);
    }

    // --- Result messages ---

    [Fact]
    public void FormatResult_Success_ContainsMessage()
    {
        var result = ScheduleResult.Ok("Created task 'test'.");

        string output = Formatting.FormatResult(result, useColor: false);

        Assert.Contains("Created task 'test'.", output);
    }

    [Fact]
    public void FormatResult_Failure_ContainsMessage()
    {
        var result = ScheduleResult.Fail("Task not found.");

        string output = Formatting.FormatResult(result, useColor: false);

        Assert.Contains("Task not found.", output);
    }

    // --- JSON ---

    [Fact]
    public void FormatListJson_ContainsTaskArray()
    {
        var tasks = new List<ScheduledTask>
        {
            new ScheduledTask("test", "0 2 * * *", SampleTime, "Ready", "cmd", @"\Winix"),
        };

        string json = Formatting.FormatTaskListJson(tasks, 0, "success", "0.1.0");

        Assert.Contains("\"tool\"", json);
        Assert.Contains("\"schedule\"", json);
        Assert.Contains("\"tasks\"", json);
    }

    [Fact]
    public void FormatActionJson_ContainsAction()
    {
        string json = Formatting.FormatActionJson("add", "test", "0 2 * * *", SampleOffset, 0, "success", "0.1.0");

        Assert.Contains("\"action\"", json);
        Assert.Contains("\"add\"", json);
        Assert.Contains("\"test\"", json);
    }

    [Fact]
    public void FormatNextJson_ContainsOccurrences()
    {
        var times = new List<DateTimeOffset>
        {
            new DateTimeOffset(2026, 4, 13, 2, 0, 0, TimeSpan.FromHours(12)),
        };

        string json = Formatting.FormatNextJson("0 2 * * *", times, 0, "success", "0.1.0");

        Assert.Contains("\"occurrences\"", json);
        Assert.Contains("\"cron\"", json);
    }

    // --- Round-12 verification follow-up: pin FormatHistoryNotAvailable ---
    //
    // Adding the man page in commit 9a919c0 created a contract on this function's
    // platform-branched output: schedule.1's `history` section claims that on
    // Linux/macOS this returns "a note that history is not available via crontab",
    // and the production text additionally references syslog. Without these pins,
    // a future refactor that condenses both branches to a single string would
    // silently break the man-page guarantee on whichever platform loses its hint.
    // pr-test-analyzer round-7 finding I-1 (6/10).

    [Fact]
    public void FormatHistoryNotAvailable_ReturnsNonEmptyString()
    {
        // Existence + non-empty guard: Program.RunHistory writes this verbatim and
        // a regression that returned null/empty would render as a blank line plus
        // exit 0, which is indistinguishable from "no history yet" — the man page
        // promises a specific message.
        string result = Formatting.FormatHistoryNotAvailable();
        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void FormatHistoryNotAvailable_PlatformBranchedHints_AreActionable()
    {
        // Each platform's message must include an actionable next-step the man page
        // implicitly commits to:
        //   - Windows: taskschd.msc + "Tasks History" guidance
        //   - Unix: syslog hint (where cron output actually goes)
        // A regression that condensed both branches to a single string would lose the
        // actionable text on whichever platform's branch was dropped.
        // SkippableFact would let us split this into two named tests but the
        // Winix.Schedule.Tests project doesn't currently reference Xunit.SkippableFact;
        // single-Fact-with-branching is the lower-friction option for a 5-LOC pin.
        string result = Formatting.FormatHistoryNotAvailable();

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("taskschd.msc", result);
            Assert.Contains("Tasks History", result);
        }
        else
        {
            Assert.Contains("not available", result);
            Assert.Contains("syslog", result);
        }
    }
}
