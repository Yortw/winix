#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Winix.Schedule;
using Yort.ShellKit;

namespace Schedule;

internal sealed class Program
{
    /// <summary>
    /// Entry point. Parses arguments, selects a subcommand, and dispatches to the appropriate handler.
    /// </summary>
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        string version = GetVersion();

        var parser = new CommandLineParser("schedule", version)
            .Description("Cross-platform task scheduler with cron expressions.")
            .StandardFlags()
            .Option("--cron", null, "EXPR", "Cron expression (required for add)")
            .Option("--name", null, "NAME", "Task name (auto-generated if omitted)")
            .Option("--folder", null, "PATH", @"Task Scheduler folder (default: \Winix\ on Windows)")
            .Option("--count", null, "N", "Number of occurrences to show (default: 5)")
            .Flag("--all", "Show all tasks, not just Winix-managed")
            .Positional("command [args...]")
            .Platform("cross-platform",
                new[] { "schtasks.exe", "crontab" },
                "No cross-platform scheduler CLI with cron syntax exists",
                "Unified cron syntax for Windows Task Scheduler and crontab")
            .StdinDescription("Not used")
            .StdoutDescription("Not used (all output goes to stderr)")
            .StderrDescription("Tables, messages, JSON output")
            .Example("schedule add --cron \"0 2 * * *\" -- dotnet build", "Create a task that runs daily at 2am")
            .Example("schedule add --cron \"*/5 * * * *\" --name health-check -- curl http://localhost:8080/health", "Create a named task")
            .Example("schedule list", "List Winix-managed tasks")
            .Example("schedule list --all", "List all scheduled tasks")
            .Example("schedule remove health-check", "Remove a task")
            .Example("schedule enable health-check", "Enable a disabled task")
            .Example("schedule disable health-check", "Disable a task")
            .Example("schedule run health-check", "Trigger immediate execution")
            .Example("schedule history health-check", "Show run history")
            .Example("schedule next \"0 2 * * *\"", "Show next 5 fire times for a cron expression")
            .Example("schedule next \"*/5 * * * *\" --count 10", "Show next 10 fire times")
            .ExitCodes(
                (0, "Success"),
                (1, "Error (task not found, scheduler failure, invalid cron)"),
                (ExitCode.UsageError, "Usage error (bad arguments)"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        if (result.Positionals.Length == 0)
        {
            return result.WriteError(
                "missing subcommand (expected add, list, remove, enable, disable, run, history, or next)",
                Console.Error);
        }

        string subcommand = result.Positionals[0];
        bool jsonOutput = result.Has("--json");
        bool useColor = result.ResolveColor(checkStdErr: true);

        // Default folder is \Winix\ on Windows, empty on Linux/macOS.
        string folder = result.Has("--folder")
            ? result.GetString("--folder")
            : (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\Winix\" : string.Empty);

        switch (subcommand)
        {
            case "add":
                return RunAdd(result, version, jsonOutput, useColor, folder);
            case "list":
                return RunList(result, version, jsonOutput, useColor, folder);
            case "remove":
                return RunRemove(result, version, jsonOutput, useColor, folder);
            case "enable":
                return RunEnable(result, version, jsonOutput, useColor, folder);
            case "disable":
                return RunDisable(result, version, jsonOutput, useColor, folder);
            case "run":
                return RunRun(result, version, jsonOutput, useColor, folder);
            case "history":
                return RunHistory(result, version, jsonOutput, useColor, folder);
            case "next":
                return RunNext(result, version, jsonOutput);
            default:
                return result.WriteError(
                    $"unknown subcommand '{subcommand}' (expected add, list, remove, enable, disable, run, history, or next)",
                    Console.Error);
        }
    }

    /// <summary>
    /// Handles the <c>add</c> subcommand: registers a new scheduled task.
    /// Requires <c>--cron</c>. The command to schedule is taken from positionals[1..],
    /// i.e. everything after the "add" token (use <c>--</c> to separate flags from command args).
    /// Task name is taken from <c>--name</c> or auto-generated from the command.
    /// </summary>
    private static int RunAdd(ParseResult result, string version, bool json, bool useColor, string folder)
    {
        if (!result.Has("--cron"))
        {
            return result.WriteError("--cron is required for add", Console.Error);
        }

        string cronStr = result.GetString("--cron");

        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(cronStr);
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine($"schedule: invalid cron expression: {ex.Message}");
            return 1;
        }

        // Positionals[0] is "add"; everything after is the command to schedule.
        // Invoke as: schedule add --cron "..." [--name X] -- dotnet build arg1
        string[] positionals = result.Positionals;
        if (positionals.Length < 2)
        {
            return result.WriteError(
                "missing command to schedule (use -- to separate flags from the command)",
                Console.Error);
        }

        string command = positionals[1];
        string[] arguments = positionals.Skip(2).ToArray();

        string name;
        if (result.Has("--name"))
        {
            name = result.GetString("--name");
        }
        else
        {
            // Build a single string so NameGenerator can apply its two-token heuristic.
            string fullCommand = arguments.Length > 0
                ? command + " " + string.Join(" ", arguments)
                : command;
            name = NameGenerator.FromCommand(fullCommand);
        }

        ISchedulerBackend backend = GetBackend();
        ScheduleResult scheduleResult = backend.Add(name, cron, command, arguments, folder);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatActionJson(
                "add", name, cronStr, null,
                scheduleResult.Success ? 0 : 1, scheduleResult.Success ? "success" : "error", version));
        }
        else
        {
            Console.Error.WriteLine(Formatting.FormatResult(scheduleResult, useColor));
        }

        return scheduleResult.Success ? 0 : 1;
    }

    /// <summary>
    /// Handles the <c>list</c> subcommand: lists scheduled tasks.
    /// Pass <c>--all</c> to include tasks outside the default Winix folder.
    /// </summary>
    private static int RunList(ParseResult result, string version, bool json, bool useColor, string folder)
    {
        bool all = result.Has("--all");

        ISchedulerBackend backend = GetBackend();
        IReadOnlyList<ScheduledTask> tasks = backend.List(all ? null : folder, all);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatTaskListJson(tasks, 0, "success", version));
        }
        else
        {
            Console.Error.Write(Formatting.FormatTable(tasks, showFolder: all, useColor: useColor));
        }

        return 0;
    }

    /// <summary>
    /// Handles the <c>remove</c> subcommand: removes a named task.
    /// </summary>
    private static int RunRemove(ParseResult result, string version, bool json, bool useColor, string folder)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing task name for remove", Console.Error);
        }

        string name = result.Positionals[1];

        ISchedulerBackend backend = GetBackend();
        ScheduleResult scheduleResult = backend.Remove(name, folder);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatActionJson(
                "remove", name, null, null,
                scheduleResult.Success ? 0 : 1, scheduleResult.Success ? "success" : "error", version));
        }
        else
        {
            Console.Error.WriteLine(Formatting.FormatResult(scheduleResult, useColor));
        }

        return scheduleResult.Success ? 0 : 1;
    }

    /// <summary>
    /// Handles the <c>enable</c> subcommand: re-enables a disabled task.
    /// </summary>
    private static int RunEnable(ParseResult result, string version, bool json, bool useColor, string folder)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing task name for enable", Console.Error);
        }

        string name = result.Positionals[1];

        ISchedulerBackend backend = GetBackend();
        ScheduleResult scheduleResult = backend.Enable(name, folder);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatActionJson(
                "enable", name, null, null,
                scheduleResult.Success ? 0 : 1, scheduleResult.Success ? "success" : "error", version));
        }
        else
        {
            Console.Error.WriteLine(Formatting.FormatResult(scheduleResult, useColor));
        }

        return scheduleResult.Success ? 0 : 1;
    }

    /// <summary>
    /// Handles the <c>disable</c> subcommand: disables a task without removing it.
    /// </summary>
    private static int RunDisable(ParseResult result, string version, bool json, bool useColor, string folder)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing task name for disable", Console.Error);
        }

        string name = result.Positionals[1];

        ISchedulerBackend backend = GetBackend();
        ScheduleResult scheduleResult = backend.Disable(name, folder);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatActionJson(
                "disable", name, null, null,
                scheduleResult.Success ? 0 : 1, scheduleResult.Success ? "success" : "error", version));
        }
        else
        {
            Console.Error.WriteLine(Formatting.FormatResult(scheduleResult, useColor));
        }

        return scheduleResult.Success ? 0 : 1;
    }

    /// <summary>
    /// Handles the <c>run</c> subcommand: triggers an immediate on-demand execution of a task.
    /// </summary>
    private static int RunRun(ParseResult result, string version, bool json, bool useColor, string folder)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing task name for run", Console.Error);
        }

        string name = result.Positionals[1];

        ISchedulerBackend backend = GetBackend();
        ScheduleResult scheduleResult = backend.Run(name, folder);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatActionJson(
                "run", name, null, null,
                scheduleResult.Success ? 0 : 1, scheduleResult.Success ? "success" : "error", version));
        }
        else
        {
            Console.Error.WriteLine(Formatting.FormatResult(scheduleResult, useColor));
        }

        return scheduleResult.Success ? 0 : 1;
    }

    /// <summary>
    /// Handles the <c>history</c> subcommand: shows execution history for a named task.
    /// On non-Windows platforms the backend may return an empty list when history is unavailable.
    /// </summary>
    private static int RunHistory(ParseResult result, string version, bool json, bool useColor, string folder)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing task name for history", Console.Error);
        }

        string name = result.Positionals[1];

        ISchedulerBackend backend = GetBackend();
        IReadOnlyList<TaskRunRecord> records = backend.GetHistory(name, folder);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatHistoryJson(name, records, 0, "success", version));
        }
        else
        {
            if (records.Count == 0 && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.Error.WriteLine(Formatting.FormatHistoryNotAvailable());
            }
            else
            {
                Console.Error.Write(Formatting.FormatHistory(records, useColor));
            }
        }

        return 0;
    }

    /// <summary>
    /// Handles the <c>next</c> subcommand: computes and displays upcoming fire times for a cron expression.
    /// No backend interaction — purely a cron calculation utility.
    /// Defaults to 5 occurrences; override with <c>--count</c>.
    /// </summary>
    private static int RunNext(ParseResult result, string version, bool json)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing cron expression for next", Console.Error);
        }

        string cronStr = result.Positionals[1];

        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(cronStr);
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine($"schedule: invalid cron expression: {ex.Message}");
            return 1;
        }

        int count = 5;
        if (result.Has("--count"))
        {
            string countStr = result.GetString("--count");
            if (!int.TryParse(countStr, out count) || count < 1)
            {
                return result.WriteError(
                    $"invalid --count value '{countStr}' (must be a positive integer)",
                    Console.Error);
            }
        }

        IReadOnlyList<DateTimeOffset> occurrences = cron.GetNextOccurrences(DateTimeOffset.Now, count);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatNextJson(cron.Expression, occurrences, 0, "success", version));
        }
        else
        {
            Console.Error.Write(Formatting.FormatNextOccurrences(occurrences));
        }

        return 0;
    }

    /// <summary>
    /// Returns the platform-appropriate scheduler backend.
    /// Uses <see cref="SchtasksBackend"/> on Windows and <see cref="CrontabBackend"/> elsewhere.
    /// </summary>
    private static ISchedulerBackend GetBackend()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new SchtasksBackend();
        }

        return new CrontabBackend();
    }

    /// <summary>
    /// Returns the informational version from the Winix.Schedule library assembly.
    /// </summary>
    private static string GetVersion()
    {
        return typeof(ScheduledTask).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
