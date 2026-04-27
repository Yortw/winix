#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R3 regression pins for Formatting.FormatHistoryJson — was identified by R1 test analysis
/// as having zero coverage and remained uncovered through R2. The 'running' record case
/// (null ExitCode and null Duration emitted as JSON null rather than the literal string
/// "null" or omitted entirely) is the riskiest path: a regression that wrote them as 0
/// would silently corrupt downstream consumers parsing 'duration_seconds' as a number.
/// </summary>
public sealed class FormatHistoryJsonTests
{
    [Fact]
    public void FormatHistoryJson_EmptyRecords_EmitsRunsArray()
    {
        string json = Formatting.FormatHistoryJson(
            "my-task", Array.Empty<TaskRunRecord>(), 0, "success", "0.4.0");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("schedule", doc.RootElement.GetProperty("tool").GetString());
        Assert.Equal("0.4.0", doc.RootElement.GetProperty("version").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("exit_code").GetInt32());
        Assert.Equal("success", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal("my-task", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("runs").ValueKind);
        Assert.Equal(0, doc.RootElement.GetProperty("runs").GetArrayLength());
    }

    [Fact]
    public void FormatHistoryJson_CompletedRecord_EmitsExitCodeAndDuration()
    {
        var records = new List<TaskRunRecord>
        {
            new TaskRunRecord(
                new DateTime(2026, 4, 27, 14, 0, 0),
                exitCode: 0,
                duration: TimeSpan.FromSeconds(1.5)),
        };

        string json = Formatting.FormatHistoryJson("t", records, 0, "success", "0.4.0");
        using var doc = JsonDocument.Parse(json);
        var record = doc.RootElement.GetProperty("runs")[0];

        Assert.Equal(JsonValueKind.Number, record.GetProperty("exit_code").ValueKind);
        Assert.Equal(0, record.GetProperty("exit_code").GetInt32());
        Assert.Equal(JsonValueKind.Number, record.GetProperty("duration_seconds").ValueKind);
        Assert.Equal(1.5, record.GetProperty("duration_seconds").GetDouble(), precision: 1);
    }

    [Fact]
    public void FormatHistoryJson_RunningRecord_EmitsJsonNullsNotZero()
    {
        // The "still running" case: ExitCode and Duration are both null. Pin that the JSON
        // emits actual 'null' (JsonValueKind.Null) rather than the literal string "null" or
        // a default 0 — downstream consumers MUST be able to distinguish.
        var records = new List<TaskRunRecord>
        {
            new TaskRunRecord(
                new DateTime(2026, 4, 27, 14, 0, 0),
                exitCode: null,
                duration: null),
        };

        string json = Formatting.FormatHistoryJson("t", records, 0, "success", "0.4.0");
        using var doc = JsonDocument.Parse(json);
        var record = doc.RootElement.GetProperty("runs")[0];

        Assert.Equal(JsonValueKind.Null, record.GetProperty("exit_code").ValueKind);
        Assert.Equal(JsonValueKind.Null, record.GetProperty("duration_seconds").ValueKind);
    }

    [Fact]
    public void FormatHistoryJson_StartTime_IsIso8601WithOffset()
    {
        var records = new List<TaskRunRecord>
        {
            new TaskRunRecord(
                new DateTime(2026, 4, 27, 14, 0, 0, DateTimeKind.Utc),
                exitCode: 0,
                duration: TimeSpan.FromSeconds(2)),
        };

        string json = Formatting.FormatHistoryJson("t", records, 0, "success", "0.4.0");
        using var doc = JsonDocument.Parse(json);
        var record = doc.RootElement.GetProperty("runs")[0];

        string startTime = record.GetProperty("start_time").GetString()!;
        // ISO 8601 round-trip format ('o') for a Utc-kind DateTime emits '+00:00' as the
        // explicit offset (DateTime.ToString('o') uses '+00:00', not 'Z' — that's only
        // DateTimeOffset's behaviour). Either way the date/time portion is invariant.
        Assert.Contains("2026-04-27T14:00:00", startTime);
        Assert.True(
            startTime.EndsWith("Z") || startTime.EndsWith("+00:00"),
            $"Expected ISO-8601 with Z or +00:00 suffix, got: {startTime}");
    }
}
