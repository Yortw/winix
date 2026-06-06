#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Yort.ShellKit;

namespace Winix.Schedule;

/// <summary>
/// Library entry point for the schedule tool: parses arguments, dispatches subcommands,
/// and routes all output through the supplied writers. <c>Program.Main</c> is a thin shell
/// over this method so the full parse→orchestrate→format→route path is testable in-process.
/// </summary>
public static class Cli
{
    /// <summary>
    /// Runs the schedule CLI.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <param name="stdout">Receives JSON envelopes (<c>--json</c> output). JSON goes to stdout
    /// per the suite-wide convention — pipelines like <c>schedule next --json X | jq</c> only
    /// work when the JSON is on stdout. Tier-1 smoke verification 2026-05-09 found the pre-fix
    /// code routed JSON to stderr, breaking that pipeline shape; same suite-convention defect
    /// class as man F12, treex r2, whoholds, files, less, winix.</param>
    /// <param name="stderr">Receives plain-text tables, status messages, human diagnostics,
    /// and usage errors.</param>
    /// <param name="backend">Scheduler backend override, or <see langword="null"/> for the
    /// platform default (<see cref="SchtasksBackend"/> on Windows, <see cref="CrontabBackend"/>
    /// elsewhere). Supplied by tests to avoid touching the real OS scheduler.</param>
    /// <returns>Process exit code: 0 success, 125 usage error, 126 backend failure.</returns>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, ISchedulerBackend? backend = null)
    {
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
            .StdoutDescription("JSON envelope when --json is set (success/error envelope on stdout per suite convention); otherwise unused")
            .StderrDescription("Plain-text tables, status messages, and human diagnostics. Usage-error JSON envelopes also land here.")
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
                (ExitCode.UsageError, "Usage error (bad arguments, invalid cron expression)"),
                (ExitCode.NotExecutable, "Backend failure (task not found, scheduler error)"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(stderr); }

        if (result.Positionals.Length == 0)
        {
            return result.WriteError(
                "missing subcommand (expected add, list, remove, enable, disable, run, history, or next)",
                stderr);
        }

        string subcommand = result.Positionals[0];
        bool jsonOutput = result.Has("--json");
        bool useColor = result.ResolveColor(checkStdErr: true);

        // Default folder is \Winix\ on Windows, empty on Linux/macOS. An explicit empty
        // --folder ("" passed by the user) is treated the same as "flag absent" — without
        // this fall-back the empty string would propagate to schtasks as /TN "" which it
        // rejects with a confusing error.
        string folderArg = result.Has("--folder") ? result.GetString("--folder") : "";
        string folder = !string.IsNullOrEmpty(folderArg)
            ? folderArg
            : (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\Winix\" : string.Empty);

        ISchedulerBackend resolvedBackend = backend ?? CreateDefaultBackend();

        switch (subcommand)
        {
            case "add":
                return RunAdd(result, version, jsonOutput, useColor, folder, resolvedBackend, stdout, stderr);
            case "list":
                return RunList(result, version, jsonOutput, useColor, folder, resolvedBackend, stdout, stderr);
            case "remove":
                return RunRemove(result, version, jsonOutput, useColor, folder, resolvedBackend, stdout, stderr);
            case "enable":
                return RunEnable(result, version, jsonOutput, useColor, folder, resolvedBackend, stdout, stderr);
            case "disable":
                return RunDisable(result, version, jsonOutput, useColor, folder, resolvedBackend, stdout, stderr);
            case "run":
                return RunRun(result, version, jsonOutput, useColor, folder, resolvedBackend, stdout, stderr);
            case "history":
                return RunHistory(result, version, jsonOutput, useColor, folder, resolvedBackend, stdout, stderr);
            case "next":
                return RunNext(result, version, jsonOutput, stdout, stderr);
            default:
                return result.WriteError(
                    $"unknown subcommand '{subcommand}' (expected add, list, remove, enable, disable, run, history, or next)",
                    stderr);
        }
    }

    /// <summary>
    /// Handles the <c>add</c> subcommand: registers a new scheduled task.
    /// Requires <c>--cron</c>. The command to schedule is taken from positionals[1..],
    /// i.e. everything after the "add" token (use <c>--</c> to separate flags from command args).
    /// Task name is taken from <c>--name</c> or auto-generated from the command.
    /// </summary>
    private static int RunAdd(ParseResult result, string version, bool json, bool useColor, string folder, ISchedulerBackend backend, TextWriter stdout, TextWriter stderr)
    {
        if (!result.Has("--cron"))
        {
            return result.WriteError("--cron is required for add", stderr);
        }

        string cronStr = result.GetString("--cron");

        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(cronStr);
        }
        catch (FormatException ex)
        {
            return result.WriteError($"invalid cron expression: {ex.Message}", stderr);
        }

        // Positionals[0] is "add"; everything after is the command to schedule.
        // Invoke as: schedule add --cron "..." [--name X] -- dotnet build arg1
        string[] positionals = result.Positionals;
        if (positionals.Length < 2)
        {
            return result.WriteError(
                "missing command to schedule (use -- to separate flags from the command)",
                stderr);
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

        // Reject newlines in user-supplied identifiers BEFORE handing them to the backend.
        // crontab is newline-delimited; an unfiltered '\n' in name/command/argument injects
        // additional entries into the user's crontab, registering hidden tasks alongside the
        // legitimate one. schtasks rejects them too but with a less actionable error.
        // Defence in depth: CrontabParser.AddEntry also validates, but the cleaner UX is to
        // surface the usage error here rather than let an ArgumentException escape.
        if (RejectIfMultiline("--name", name, out string? nameError))
        {
            return result.WriteError(nameError!, stderr);
        }
        if (RejectIfMultiline("command", command, out string? cmdError))
        {
            return result.WriteError(cmdError!, stderr);
        }
        for (int ai = 0; ai < arguments.Length; ai++)
        {
            if (RejectIfMultiline($"argument {ai + 1}", arguments[ai], out string? argError))
            {
                return result.WriteError(argError!, stderr);
            }
        }

        ScheduleResult scheduleResult;
        try
        {
            scheduleResult = backend.Add(name, cron, command, arguments, folder);
        }
        catch (ArgumentException ex)
        {
            // CrontabParser.AddEntry throws ArgumentException for inputs that survived the
            // RejectIfMultiline gate above but still violate the parser's tighter contract
            // (notably a name containing the literal '# winix:' tag prefix). Catching here
            // turns the validation failure into a clean usage-error exit (125) rather than
            // letting the exception escape as an unhandled stack trace. See SafeWriteLine
            // rationale — diagnostic must never crash the tool.
            return result.WriteError(ex.Message, stderr);
        }

        int exitCode = scheduleResult.Success ? 0 : ExitCode.NotExecutable;
        if (json)
        {
            SafeWriteLine(stdout, Formatting.FormatActionJson(
                "add", name, cronStr, null,
                exitCode, scheduleResult.Success ? "success" : "error", version,
                scheduleResult.Warning));
        }
        else
        {
            SafeWriteLine(stderr, Formatting.FormatResult(scheduleResult, useColor));
        }

        return exitCode;
    }

    /// <summary>
    /// Handles the <c>list</c> subcommand: lists scheduled tasks.
    /// Pass <c>--all</c> to include tasks outside the default Winix folder.
    /// </summary>
    private static int RunList(ParseResult result, string version, bool json, bool useColor, string folder, ISchedulerBackend backend, TextWriter stdout, TextWriter stderr)
    {
        bool all = result.Has("--all");

        ScheduleListResult listResult = backend.List(all ? null : folder, all);

        if (!listResult.Available)
        {
            // Backend reported a real failure (Task Scheduler service stopped, crontab
            // denied, etc.). Surface diagnostic via stderr and exit non-zero rather than
            // silently presenting an empty table that the user mistakes for "no tasks."
            int failExit = (int)ExitCode.NotExecutable;
            string reason = listResult.FailureReason ?? "unknown";
            if (json)
            {
                SafeWriteLine(stdout, Formatting.FormatTaskListJson(listResult.Tasks, failExit, "error", version, reason));
            }
            else
            {
                SafeWriteLine(stderr, $"schedule: {reason}");
            }
            return failExit;
        }

        if (json)
        {
            SafeWriteLine(stdout, Formatting.FormatTaskListJson(listResult.Tasks, 0, "success", version, listResult.Warning));
        }
        else
        {
            SafeWrite(stderr, Formatting.FormatTable(listResult.Tasks, showFolder: all, useColor: useColor));
            if (!string.IsNullOrEmpty(listResult.Warning))
            {
                SafeWriteLine(stderr, $"warning: {listResult.Warning}");
            }
        }

        return 0;
    }

    /// <summary>
    /// Handles the <c>remove</c> subcommand: removes a named task.
    /// </summary>
    private static int RunRemove(ParseResult result, string version, bool json, bool useColor, string folder, ISchedulerBackend backend, TextWriter stdout, TextWriter stderr)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing task name for remove", stderr);
        }

        string name = result.Positionals[1];

        ScheduleResult scheduleResult = backend.Remove(name, folder);

        return WriteActionResult(scheduleResult, "remove", name, null, version, json, useColor, stdout, stderr);
    }

    /// <summary>
    /// Handles the <c>enable</c> subcommand: re-enables a disabled task.
    /// </summary>
    private static int RunEnable(ParseResult result, string version, bool json, bool useColor, string folder, ISchedulerBackend backend, TextWriter stdout, TextWriter stderr)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing task name for enable", stderr);
        }

        string name = result.Positionals[1];

        ScheduleResult scheduleResult = backend.Enable(name, folder);

        return WriteActionResult(scheduleResult, "enable", name, null, version, json, useColor, stdout, stderr);
    }

    /// <summary>
    /// Handles the <c>disable</c> subcommand: disables a task without removing it.
    /// </summary>
    private static int RunDisable(ParseResult result, string version, bool json, bool useColor, string folder, ISchedulerBackend backend, TextWriter stdout, TextWriter stderr)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing task name for disable", stderr);
        }

        string name = result.Positionals[1];

        ScheduleResult scheduleResult = backend.Disable(name, folder);

        return WriteActionResult(scheduleResult, "disable", name, null, version, json, useColor, stdout, stderr);
    }

    /// <summary>
    /// Handles the <c>run</c> subcommand: triggers an immediate on-demand execution of a task.
    /// </summary>
    private static int RunRun(ParseResult result, string version, bool json, bool useColor, string folder, ISchedulerBackend backend, TextWriter stdout, TextWriter stderr)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing task name for run", stderr);
        }

        string name = result.Positionals[1];

        ScheduleResult scheduleResult = backend.Run(name, folder);

        return WriteActionResult(scheduleResult, "run", name, null, version, json, useColor, stdout, stderr);
    }

    /// <summary>
    /// Common output path for action-style subcommands (add/remove/enable/disable/run).
    /// Returns 0 on success, <see cref="ExitCode.NotExecutable"/> (126) on backend failure.
    /// </summary>
    private static int WriteActionResult(
        ScheduleResult scheduleResult, string action, string name, string? cronStr,
        string version, bool json, bool useColor, TextWriter stdout, TextWriter stderr)
    {
        int exitCode = scheduleResult.Success ? 0 : ExitCode.NotExecutable;
        if (json)
        {
            SafeWriteLine(stdout, Formatting.FormatActionJson(
                action, name, cronStr, null,
                exitCode, scheduleResult.Success ? "success" : "error", version,
                scheduleResult.Warning));
        }
        else
        {
            SafeWriteLine(stderr, Formatting.FormatResult(scheduleResult, useColor));
        }

        return exitCode;
    }

    /// <summary>
    /// Handles the <c>history</c> subcommand: shows execution history for a named task.
    /// On non-Windows platforms the backend may return an empty list when history is unavailable.
    /// </summary>
    private static int RunHistory(ParseResult result, string version, bool json, bool useColor, string folder, ISchedulerBackend backend, TextWriter stdout, TextWriter stderr)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing task name for history", stderr);
        }

        string name = result.Positionals[1];

        IReadOnlyList<TaskRunRecord> records = backend.GetHistory(name, folder);

        if (json)
        {
            SafeWriteLine(stdout, Formatting.FormatHistoryJson(name, records, 0, "success", version));
        }
        else
        {
            if (records.Count == 0)
            {
                SafeWriteLine(stderr, Formatting.FormatHistoryNotAvailable());
            }
            else
            {
                SafeWrite(stderr, Formatting.FormatHistory(records, useColor));
            }
        }

        return 0;
    }

    /// <summary>
    /// Handles the <c>next</c> subcommand: computes and displays upcoming fire times for a cron expression.
    /// No backend interaction — purely a cron calculation utility.
    /// Defaults to 5 occurrences; override with <c>--count</c>.
    /// </summary>
    private static int RunNext(ParseResult result, string version, bool json, TextWriter stdout, TextWriter stderr)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing cron expression for next", stderr);
        }

        string cronStr = result.Positionals[1];

        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(cronStr);
        }
        catch (FormatException ex)
        {
            return result.WriteError($"invalid cron expression: {ex.Message}", stderr);
        }

        int count = 5;
        if (result.Has("--count"))
        {
            string countStr = result.GetString("--count");
            if (!int.TryParse(countStr, out count) || count < 1)
            {
                return result.WriteError(
                    $"invalid --count value '{countStr}' (must be a positive integer)",
                    stderr);
            }
        }

        IReadOnlyList<DateTimeOffset> occurrences;
        try
        {
            occurrences = cron.GetNextOccurrences(DateTimeOffset.Now, count);
        }
        catch (InvalidOperationException ex)
        {
            // Parseable expressions can still be unsatisfiable — e.g. '0 0 30 2 *' (Feb 30).
            // GetNextOccurrence throws after exhausting an 8-year search horizon. Without this
            // catch the exception escapes RunNext as an unhandled CLR error with a stack trace.
            // CrontabParser already learned this lesson; mirror it here. Treat as a usage error
            // since the cron is technically valid syntax but logically impossible.
            return result.WriteError(ex.Message, stderr);
        }

        if (json)
        {
            SafeWriteLine(stdout, Formatting.FormatNextJson(cron.Expression, occurrences, 0, "success", version));
        }
        else
        {
            SafeWrite(stderr, Formatting.FormatNextOccurrences(cron.Expression, occurrences));
        }

        return 0;
    }

    /// <summary>
    /// Returns <see langword="true"/> and emits an error message into <paramref name="error"/>
    /// when <paramref name="value"/> contains a newline or carriage return — used as a
    /// usage-error gate for user-supplied identifiers (task name, command, arguments) before
    /// they reach the backend. Without this check, '\n' in any of those fields would inject
    /// additional crontab entries.
    /// </summary>
    private static bool RejectIfMultiline(string label, string value, out string? error)
    {
        if (value.IndexOfAny(MultilineChars) >= 0)
        {
            error = $"{label} must not contain newline or carriage-return characters.";
            return true;
        }
        error = null;
        return false;
    }

    private static readonly char[] MultilineChars = { '\n', '\r' };

    /// <summary>
    /// Writes <paramref name="message"/> followed by a newline to <paramref name="writer"/>,
    /// swallowing any write exception (<see cref="System.IO.IOException"/> from a broken pipe or
    /// full disk; <see cref="ObjectDisposedException"/> from a host that has closed the
    /// underlying stream during teardown). Either would otherwise convert a clean
    /// exit-code path into a CLR unhandled-exception crash with stack trace. Diagnostic
    /// output must be strictly weaker than the production path it reports on.
    /// </summary>
    private static void SafeWriteLine(TextWriter writer, string message)
    {
        try { writer.WriteLine(message); }
        catch (System.IO.IOException) { /* writer unwritable — accept loss; do not mask exit code */ }
        catch (ObjectDisposedException) { /* host tore down the stream; same rationale as IOException */ }
    }

    /// <summary>Writes <paramref name="message"/> verbatim to <paramref name="writer"/> with the same swallow behaviour as <see cref="SafeWriteLine"/>.</summary>
    private static void SafeWrite(TextWriter writer, string message)
    {
        try { writer.Write(message); }
        catch (System.IO.IOException) { /* see SafeWriteLine */ }
        catch (ObjectDisposedException) { /* see SafeWriteLine */ }
    }

    /// <summary>
    /// Returns the platform-appropriate scheduler backend used when no backend override is
    /// supplied to <see cref="Run"/> (the null-backend default).
    /// Uses <see cref="SchtasksBackend"/> on Windows and <see cref="CrontabBackend"/> elsewhere.
    /// </summary>
    private static ISchedulerBackend CreateDefaultBackend()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new SchtasksBackend();
        }

        return new CrontabBackend();
    }

    /// <summary>
    /// Returns the informational version from the Winix.Schedule library assembly. The SDK
    /// appends a SourceLink "+gitsha" suffix to <c>AssemblyInformationalVersion</c> by default
    /// (e.g. "0.3.0+abc123…"); we strip it so users see "0.3.0" — matching the convention
    /// adopted across clip / ids / digest / envvault / peep and the rest of the suite.
    /// </summary>
    private static string GetVersion()
    {
        string raw = typeof(ScheduledTask).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
