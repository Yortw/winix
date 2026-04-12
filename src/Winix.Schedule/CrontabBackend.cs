#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Winix.Schedule;

/// <summary>
/// Linux/macOS scheduler backend that manages the user's crontab.
/// Winix-managed entries are identified by <c># winix:&lt;name&gt;</c> comment tags
/// on the line preceding each cron entry.
/// </summary>
public sealed class CrontabBackend : ISchedulerBackend
{
    /// <inheritdoc />
    public ScheduleResult Add(string name, CronExpression cron, string command, string[] arguments, string folder)
    {
        string fullCommand = BuildCommandString(command, arguments);

        string currentCrontab = ReadCrontab();
        string newCrontab = CrontabParser.AddEntry(currentCrontab, name, cron.Expression, fullCommand);
        WriteCrontab(newCrontab);

        return ScheduleResult.Ok($"Created task '{name}'.");
    }

    /// <inheritdoc />
    public IReadOnlyList<ScheduledTask> List(string? folder, bool all)
    {
        string crontab = ReadCrontab();
        return CrontabParser.ParseEntries(crontab, winixOnly: !all);
    }

    /// <inheritdoc />
    public ScheduleResult Remove(string name, string folder)
    {
        string crontab = ReadCrontab();
        string newCrontab = CrontabParser.RemoveEntry(crontab, name);

        if (crontab == newCrontab)
        {
            return ScheduleResult.Fail($"Task '{name}' not found.");
        }

        WriteCrontab(newCrontab);
        return ScheduleResult.Ok($"Removed task '{name}'.");
    }

    /// <inheritdoc />
    public ScheduleResult Enable(string name, string folder)
    {
        string crontab = ReadCrontab();
        string newCrontab = CrontabParser.EnableEntry(crontab, name);
        WriteCrontab(newCrontab);

        return ScheduleResult.Ok($"Enabled task '{name}'.");
    }

    /// <inheritdoc />
    public ScheduleResult Disable(string name, string folder)
    {
        string crontab = ReadCrontab();
        string newCrontab = CrontabParser.DisableEntry(crontab, name);
        WriteCrontab(newCrontab);

        return ScheduleResult.Ok($"Disabled task '{name}'.");
    }

    /// <inheritdoc />
    public ScheduleResult Run(string name, string folder)
    {
        string crontab = ReadCrontab();
        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        ScheduledTask? target = null;
        foreach (var task in tasks)
        {
            if (string.Equals(task.Name, name, StringComparison.Ordinal))
            {
                target = task;
                break;
            }
        }

        if (target is null)
        {
            return ScheduleResult.Fail($"Task '{name}' not found.");
        }

        // Run the command in a background subshell (fire and forget).
        try
        {
            var psi = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(target.Command);

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            return ScheduleResult.Fail($"Failed to run task '{name}': {ex.Message}");
        }

        return ScheduleResult.Ok($"Triggered task '{name}'.");
    }

    /// <inheritdoc />
    public IReadOnlyList<TaskRunRecord> GetHistory(string name, string folder)
    {
        // Crontab has no built-in run history. The console app displays a note about this.
        return Array.Empty<TaskRunRecord>();
    }

    /// <summary>
    /// Reads the current user crontab via <c>crontab -l</c>.
    /// Returns an empty string when the crontab is empty or <c>crontab</c> is not found.
    /// </summary>
    private static string ReadCrontab()
    {
        var psi = new ProcessStartInfo("crontab")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-l");

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return "";
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Exit code 1 with "no crontab for ..." is normal on some systems — treat as empty.
            return process.ExitCode == 0 ? output : "";
        }
        catch (Win32Exception)
        {
            // crontab binary not found on this system.
            return "";
        }
    }

    /// <summary>
    /// Writes new content to the user crontab by piping to <c>crontab -</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the crontab process fails to start or exits with a non-zero code.
    /// </exception>
    private static void WriteCrontab(string content)
    {
        var psi = new ProcessStartInfo("crontab")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start crontab process.");

        process.StandardInput.Write(content);
        process.StandardInput.Close();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            string stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"crontab failed (exit {process.ExitCode}): {stderr}");
        }
    }

    /// <summary>
    /// Builds a shell command string from a command and its arguments.
    /// Arguments containing spaces or shell-special characters are single-quote escaped.
    /// </summary>
    private static string BuildCommandString(string command, string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return command;
        }

        var sb = new StringBuilder();
        sb.Append(command);
        foreach (string arg in arguments)
        {
            sb.Append(' ');
            // Single-quote escape any argument that contains characters the shell would interpret.
            if (arg.Contains(' ') || arg.Contains('\'') || arg.Contains('"') || arg.Contains('$') || arg.Contains('\\'))
            {
                sb.Append('\'');
                sb.Append(arg.Replace("'", "'\\''")); // Terminate quote, escaped apostrophe, re-open quote.
                sb.Append('\'');
            }
            else
            {
                sb.Append(arg);
            }
        }

        return sb.ToString();
    }
}
