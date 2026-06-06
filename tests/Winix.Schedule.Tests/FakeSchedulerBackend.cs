#nullable enable

using System;
using System.Collections.Generic;

namespace Winix.Schedule.Tests;

/// <summary>
/// Configurable in-memory <see cref="ISchedulerBackend"/> for Cli.Run seam tests.
/// Records calls; returns the pre-set result objects. Never touches the OS scheduler.
/// </summary>
internal sealed class FakeSchedulerBackend : ISchedulerBackend
{
    public ScheduleResult AddResult { get; set; } = ScheduleResult.Ok("created");
    public ScheduleListResult ListResult { get; set; } = ScheduleListResult.Ok(Array.Empty<ScheduledTask>());
    public ScheduleResult RemoveResult { get; set; } = ScheduleResult.Ok("removed");
    public ScheduleResult EnableResult { get; set; } = ScheduleResult.Ok("enabled");
    public ScheduleResult DisableResult { get; set; } = ScheduleResult.Ok("disabled");
    public ScheduleResult RunResult { get; set; } = ScheduleResult.Ok("ran");
    public IReadOnlyList<TaskRunRecord> HistoryResult { get; set; } = Array.Empty<TaskRunRecord>();

    /// <summary>Call log, e.g. "add:name:command:folder".</summary>
    public List<string> Calls { get; } = new();

    public ScheduleResult Add(string name, CronExpression cron, string command, string[] arguments, string folder)
    {
        Calls.Add($"add:{name}:{command}:{folder}");
        return AddResult;
    }

    public ScheduleListResult List(string? folder, bool all)
    {
        Calls.Add($"list:{folder ?? "(null)"}:{all}");
        return ListResult;
    }

    public ScheduleResult Remove(string name, string folder) { Calls.Add($"remove:{name}:{folder}"); return RemoveResult; }
    public ScheduleResult Enable(string name, string folder) { Calls.Add($"enable:{name}:{folder}"); return EnableResult; }
    public ScheduleResult Disable(string name, string folder) { Calls.Add($"disable:{name}:{folder}"); return DisableResult; }
    public ScheduleResult Run(string name, string folder) { Calls.Add($"run:{name}:{folder}"); return RunResult; }
    public IReadOnlyList<TaskRunRecord> GetHistory(string name, string folder) { Calls.Add($"history:{name}:{folder}"); return HistoryResult; }
}
