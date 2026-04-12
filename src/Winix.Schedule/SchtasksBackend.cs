#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Winix.Schedule;

/// <summary>
/// Windows scheduler backend that delegates to <c>schtasks.exe</c> for task management.
/// Guaranteed AOT-compatible (pure process spawning, no COM interop).
/// </summary>
public sealed class SchtasksBackend : ISchedulerBackend
{
    /// <inheritdoc />
    public ScheduleResult Add(string name, CronExpression cron, string command, string[] arguments, string folder)
    {
        string taskPath = BuildTaskPath(folder, name);

        // Build the command string for schtasks /TR.
        // schtasks /TR takes a single string. We must quote the command and arguments.
        string taskRun = BuildTaskRunString(command, arguments);

        // Map cron to schtasks schedule parameters.
        SchtasksSchedule schedule = CronToSchtasksMapper.Map(cron);

        var args = new List<string>
        {
            "/Create",
            "/TN", taskPath,
            "/TR", taskRun,
            "/SC", schedule.ScheduleType,
            "/F", // Force overwrite if exists.
        };

        if (schedule.Modifier != null)
        {
            args.Add("/MO");
            args.Add(schedule.Modifier);
        }

        if (schedule.StartTime != null)
        {
            args.Add("/ST");
            args.Add(schedule.StartTime);
        }

        if (schedule.Days != null)
        {
            args.Add("/D");
            args.Add(schedule.Days);
        }

        if (schedule.DayOfMonth != null)
        {
            args.Add("/D");
            args.Add(schedule.DayOfMonth);
        }

        // Run the task with limited privileges by default.
        args.Add("/RL");
        args.Add("LIMITED");

        var result = RunSchtasks(args.ToArray());

        if (result.ExitCode != 0)
        {
            return ScheduleResult.Fail($"schtasks failed: {result.Stderr}");
        }

        // schtasks /Create does not have a /Comment flag. Ideally we'd store the cron
        // expression by using /XML to embed it in the task description, but for v1 the
        // `list` command will display the cron from the schedule mapping.
        // TODO: Use schtasks /Create /XML to embed the cron expression in the task description.

        return ScheduleResult.Ok($"Created task '{name}'.");
    }

    /// <inheritdoc />
    public IReadOnlyList<ScheduledTask> List(string? folder, bool all)
    {
        string queryFolder = folder ?? @"\Winix";

        // schtasks /Query /TN requires the folder path without trailing backslash
        // to list all tasks in that folder. A trailing backslash causes "not found".
        var args = all
            ? new[] { "/Query", "/FO", "CSV", "/V", "/NH" }
            : new[] { "/Query", "/TN", queryFolder, "/FO", "CSV", "/V", "/NH" };

        var result = RunSchtasks(args);

        if (result.ExitCode != 0)
        {
            // "ERROR: The system cannot find the file specified." means the folder doesn't exist.
            // Return empty list rather than failing.
            return Array.Empty<ScheduledTask>();
        }

        return SchtasksCsvParser.Parse(result.Stdout, queryFolder);
    }

    /// <inheritdoc />
    public ScheduleResult Remove(string name, string folder)
    {
        string taskPath = BuildTaskPath(folder, name);

        var result = RunSchtasks(new[] { "/Delete", "/TN", taskPath, "/F" });

        if (result.ExitCode != 0)
        {
            return ScheduleResult.Fail($"Failed to remove task '{name}': {result.Stderr}");
        }

        return ScheduleResult.Ok($"Removed task '{name}'.");
    }

    /// <inheritdoc />
    public ScheduleResult Enable(string name, string folder)
    {
        string taskPath = BuildTaskPath(folder, name);

        var result = RunSchtasks(new[] { "/Change", "/TN", taskPath, "/ENABLE" });

        if (result.ExitCode != 0)
        {
            return ScheduleResult.Fail($"Failed to enable task '{name}': {result.Stderr}");
        }

        return ScheduleResult.Ok($"Enabled task '{name}'.");
    }

    /// <inheritdoc />
    public ScheduleResult Disable(string name, string folder)
    {
        string taskPath = BuildTaskPath(folder, name);

        var result = RunSchtasks(new[] { "/Change", "/TN", taskPath, "/DISABLE" });

        if (result.ExitCode != 0)
        {
            return ScheduleResult.Fail($"Failed to disable task '{name}': {result.Stderr}");
        }

        return ScheduleResult.Ok($"Disabled task '{name}'.");
    }

    /// <inheritdoc />
    public ScheduleResult Run(string name, string folder)
    {
        string taskPath = BuildTaskPath(folder, name);

        var result = RunSchtasks(new[] { "/Run", "/TN", taskPath });

        if (result.ExitCode != 0)
        {
            return ScheduleResult.Fail($"Failed to run task '{name}': {result.Stderr}");
        }

        return ScheduleResult.Ok($"Triggered task '{name}'.");
    }

    /// <inheritdoc />
    public IReadOnlyList<TaskRunRecord> GetHistory(string name, string folder)
    {
        // schtasks.exe does not have a direct "history" query. Task Scheduler history
        // is stored in the Windows Event Log (Microsoft-Windows-TaskScheduler/Operational).
        // Querying it requires wevtutil or COM -- both are complex for v1.
        // Return empty; the console app will display a note about this limitation.
        return Array.Empty<TaskRunRecord>();
    }

    /// <summary>Builds the full task path from folder and name.</summary>
    private static string BuildTaskPath(string folder, string name)
    {
        string cleanFolder = folder.TrimEnd('\\');
        return cleanFolder + "\\" + name;
    }

    /// <summary>
    /// Builds the /TR string for schtasks.exe. If there are arguments,
    /// wraps the command in quotes and appends arguments.
    /// </summary>
    private static string BuildTaskRunString(string command, string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return command;
        }

        // schtasks /TR expects a single string. We need to be careful with quoting.
        var sb = new StringBuilder();

        // If the command contains spaces, quote it.
        if (command.Contains(' '))
        {
            sb.Append('"');
            sb.Append(command);
            sb.Append('"');
        }
        else
        {
            sb.Append(command);
        }

        foreach (string arg in arguments)
        {
            sb.Append(' ');
            if (arg.Contains(' ') || arg.Contains('"'))
            {
                sb.Append('"');
                sb.Append(arg.Replace("\"", "\\\""));
                sb.Append('"');
            }
            else
            {
                sb.Append(arg);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Runs schtasks.exe with the given arguments and returns captured output.
    /// Uses <see cref="ProcessStartInfo.ArgumentList"/> for safe argument passing.
    /// </summary>
    private static ProcessRunResult RunSchtasks(string[] arguments)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null for schtasks.exe.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            return new ProcessRunResult(-1, "", "schtasks.exe not found");
        }

        using (process)
        {
            process.StandardInput.Close();

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new ProcessRunResult(process.ExitCode, stdout.Trim(), stderr.Trim());
        }
    }
}

/// <summary>Captured output from a child process.</summary>
internal sealed class ProcessRunResult
{
    /// <summary>The process exit code.</summary>
    public int ExitCode { get; }

    /// <summary>Captured standard output text (trimmed).</summary>
    public string Stdout { get; }

    /// <summary>Captured standard error text (trimmed).</summary>
    public string Stderr { get; }

    /// <summary>Creates a new <see cref="ProcessRunResult"/>.</summary>
    public ProcessRunResult(int exitCode, string stdout, string stderr)
    {
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
    }
}
