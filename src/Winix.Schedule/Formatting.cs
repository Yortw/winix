#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Yort.ShellKit;

namespace Winix.Schedule;

/// <summary>
/// Output formatting for the schedule tool — tables, history, next-occurrence lists,
/// result messages, and JSON envelopes.
/// All methods are pure (no I/O); the console app writes the returned strings.
/// </summary>
public static class Formatting
{
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

    private const string HeaderName    = "Name";
    private const string HeaderCron    = "Cron";
    private const string HeaderNext    = "Next Run";
    private const string HeaderStatus  = "Status";
    private const string HeaderCommand = "Command";
    private const string HeaderFolder  = "Folder";
    private const string HeaderTime     = "Time";
    private const string HeaderExitCode = "Exit Code";
    private const string HeaderDuration = "Duration";

    /// <summary>
    /// Formats a list of scheduled tasks as a fixed-width table.
    /// Columns: Name, Cron, Next Run, Status, Command (plus optional Folder when
    /// <paramref name="showFolder"/> is <see langword="true"/>).
    /// The header row is dimmed when <paramref name="useColor"/> is <see langword="true"/>.
    /// </summary>
    /// <param name="tasks">Tasks to display. Must not be null.</param>
    /// <param name="showFolder">When <see langword="true"/>, includes a Folder column.</param>
    /// <param name="useColor">When <see langword="true"/>, ANSI colour codes are included.</param>
    public static string FormatTable(IReadOnlyList<ScheduledTask> tasks, bool showFolder, bool useColor)
    {
        int nameW    = HeaderName.Length;
        int cronW    = HeaderCron.Length;
        int nextW    = HeaderNext.Length;
        int statusW  = HeaderStatus.Length;
        int cmdW     = HeaderCommand.Length;
        int folderW  = HeaderFolder.Length;

        // Cap column widths to keep the table readable on typical terminals.
        const int maxNameW = 40;
        const int maxCronW = 25;
        const int maxStatusW = 10;
        const int maxFolderW = 20;
        const int maxCmdW = 60;

        foreach (ScheduledTask t in tasks)
        {
            if (t.Name.Length > nameW)    { nameW   = t.Name.Length; }
            if (t.Schedule.Length > cronW) { cronW   = t.Schedule.Length; }

            string nextStr = t.NextRun?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "";
            if (nextStr.Length > nextW)    { nextW   = nextStr.Length; }

            if (t.Status.Length > statusW) { statusW = t.Status.Length; }
            if (t.Command.Length > cmdW)   { cmdW    = t.Command.Length; }

            if (showFolder && t.Folder.Length > folderW) { folderW = t.Folder.Length; }
        }

        // Apply caps
        if (nameW > maxNameW) { nameW = maxNameW; }
        if (cronW > maxCronW) { cronW = maxCronW; }
        if (statusW > maxStatusW) { statusW = maxStatusW; }
        if (folderW > maxFolderW) { folderW = maxFolderW; }
        if (cmdW > maxCmdW) { cmdW = maxCmdW; }

        string dim   = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);

        var sb = new StringBuilder();

        // Header row
        sb.Append(dim);
        sb.Append(HeaderName.PadRight(nameW));
        sb.Append("  ");
        sb.Append(HeaderCron.PadRight(cronW));
        sb.Append("  ");
        sb.Append(HeaderNext.PadRight(nextW));
        sb.Append("  ");
        sb.Append(HeaderStatus.PadRight(statusW));
        sb.Append("  ");
        if (showFolder)
        {
            sb.Append(HeaderFolder.PadRight(folderW));
            sb.Append("  ");
        }
        sb.Append(HeaderCommand);
        sb.Append(reset);
        sb.AppendLine();

        // Data rows
        foreach (ScheduledTask t in tasks)
        {
            string nextStr = t.NextRun?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "";

            sb.Append(Truncate(t.Name, nameW).PadRight(nameW));
            sb.Append("  ");
            sb.Append(Truncate(t.Schedule, cronW).PadRight(cronW));
            sb.Append("  ");
            sb.Append(nextStr.PadRight(nextW));
            sb.Append("  ");
            sb.Append(Truncate(t.Status, statusW).PadRight(statusW));
            sb.Append("  ");
            if (showFolder)
            {
                sb.Append(Truncate(t.Folder, folderW).PadRight(folderW));
                sb.Append("  ");
            }
            sb.AppendLine(Truncate(t.Command, maxCmdW));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats a list of task run records as a fixed-width table.
    /// Columns: Time, Exit Code, Duration.
    /// The header row is dimmed when <paramref name="useColor"/> is <see langword="true"/>.
    /// </summary>
    /// <param name="records">Run records to display. Must not be null.</param>
    /// <param name="useColor">When <see langword="true"/>, ANSI colour codes are included.</param>
    public static string FormatHistory(IReadOnlyList<TaskRunRecord> records, bool useColor)
    {
        int timeW  = HeaderTime.Length;
        int exitW  = HeaderExitCode.Length;
        int durW   = HeaderDuration.Length;

        foreach (TaskRunRecord r in records)
        {
            string timeStr = r.StartTime.ToString(DateFormat, CultureInfo.InvariantCulture);
            if (timeStr.Length > timeW) { timeW = timeStr.Length; }

            string exitStr = r.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "running";
            if (exitStr.Length > exitW) { exitW = exitStr.Length; }

            string durStr = FormatDuration(r.Duration);
            if (durStr.Length > durW) { durW = durStr.Length; }
        }

        string dim   = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);

        var sb = new StringBuilder();

        // Header row
        sb.Append(dim);
        sb.Append(HeaderTime.PadRight(timeW));
        sb.Append("  ");
        sb.Append(HeaderExitCode.PadRight(exitW));
        sb.Append("  ");
        sb.Append(HeaderDuration);
        sb.Append(reset);
        sb.AppendLine();

        // Data rows
        foreach (TaskRunRecord r in records)
        {
            string exitStr = r.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "running";

            sb.Append(r.StartTime.ToString(DateFormat, CultureInfo.InvariantCulture).PadRight(timeW));
            sb.Append("  ");
            sb.Append(exitStr.PadRight(exitW));
            sb.Append("  ");
            sb.AppendLine(FormatDuration(r.Duration));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats next-occurrence times, one per line, using local time in
    /// <c>yyyy-MM-dd HH:mm:ss</c> format.
    /// </summary>
    /// <param name="times">Occurrence times to display. Must not be null.</param>
    public static string FormatNextOccurrences(string cronExpression, IReadOnlyList<DateTimeOffset> times)
    {
        var sb = new StringBuilder();
        sb.Append("Expression: ");
        sb.AppendLine(cronExpression);
        sb.AppendLine();
        foreach (DateTimeOffset t in times)
        {
            sb.AppendLine(t.ToString(DateFormat, CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats a <see cref="ScheduleResult"/> for human-readable display.
    /// Successful results with a next-run time append the formatted time.
    /// </summary>
    /// <param name="result">The result to format. Must not be null.</param>
    /// <param name="useColor">When <see langword="true"/>, ANSI colour codes are included.</param>
    public static string FormatResult(ScheduleResult result, bool useColor)
    {
        if (result.Success)
        {
            string green = AnsiColor.Green(useColor);
            string reset = AnsiColor.Reset(useColor);
            return $"{green}\u2713{reset} {result.Message}";
        }
        else
        {
            string red   = AnsiColor.Red(useColor);
            string reset = AnsiColor.Reset(useColor);
            return $"{red}\u2717{reset} {result.Message}";
        }
    }

    /// <summary>
    /// Formats a message indicating that run history is not available on the current platform.
    /// Used on Linux where cron does not expose a queryable history log.
    /// </summary>
    public static string FormatHistoryNotAvailable()
    {
        return "Run history not available on this platform. Check syslog for cron output.";
    }

    // --- JSON ---

    /// <summary>
    /// Returns a JSON object containing the standard Winix envelope and a <c>tasks</c> array
    /// with one object per <see cref="ScheduledTask"/>.
    /// </summary>
    /// <param name="tasks">Tasks to serialise. Must not be null.</param>
    /// <param name="exitCode">Process exit code written into the envelope.</param>
    /// <param name="exitReason">Machine-readable exit reason string.</param>
    /// <param name="version">Value for the <c>version</c> envelope field.</param>
    public static string FormatTaskListJson(
        IReadOnlyList<ScheduledTask> tasks,
        int exitCode,
        string exitReason,
        string version)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", "schedule");
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            writer.WriteStartArray("tasks");
            foreach (ScheduledTask t in tasks)
            {
                writer.WriteStartObject();
                writer.WriteString("name", t.Name);
                writer.WriteString("cron", t.Schedule);
                if (t.NextRun.HasValue)
                {
                    writer.WriteString("next_run", t.NextRun.Value.ToString("o", CultureInfo.InvariantCulture));
                }
                else
                {
                    writer.WriteNull("next_run");
                }
                writer.WriteString("status", t.Status);
                writer.WriteString("command", t.Command);
                writer.WriteString("folder", t.Folder);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Returns a JSON object for an action subcommand result (add/remove/enable/disable/run),
    /// containing the standard Winix envelope plus <c>action</c>, <c>name</c>,
    /// and optionally <c>cron</c> and <c>next_run</c>.
    /// </summary>
    /// <param name="action">The action performed (e.g. "add", "remove").</param>
    /// <param name="name">The task name that the action was applied to.</param>
    /// <param name="cronExpression">The cron expression, or <see langword="null"/> when not applicable.</param>
    /// <param name="nextRun">The next scheduled run time, or <see langword="null"/> when not available.</param>
    /// <param name="exitCode">Process exit code written into the envelope.</param>
    /// <param name="exitReason">Machine-readable exit reason string.</param>
    /// <param name="version">Value for the <c>version</c> envelope field.</param>
    public static string FormatActionJson(
        string action,
        string name,
        string? cronExpression,
        DateTimeOffset? nextRun,
        int exitCode,
        string exitReason,
        string version)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", "schedule");
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            writer.WriteString("action", action);
            writer.WriteString("name", name);
            if (cronExpression != null)
            {
                writer.WriteString("cron", cronExpression);
            }
            if (nextRun.HasValue)
            {
                writer.WriteString("next_run", nextRun.Value.ToString("o", CultureInfo.InvariantCulture));
            }
            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Returns a JSON object for the <c>next</c> subcommand, containing the standard Winix
    /// envelope, the queried <c>cron</c> expression, and an <c>occurrences</c> array of ISO 8601 strings.
    /// </summary>
    /// <param name="cronExpression">The cron expression that was evaluated.</param>
    /// <param name="occurrences">The computed next-occurrence times.</param>
    /// <param name="exitCode">Process exit code written into the envelope.</param>
    /// <param name="exitReason">Machine-readable exit reason string.</param>
    /// <param name="version">Value for the <c>version</c> envelope field.</param>
    public static string FormatNextJson(
        string cronExpression,
        IReadOnlyList<DateTimeOffset> occurrences,
        int exitCode,
        string exitReason,
        string version)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", "schedule");
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            writer.WriteString("cron", cronExpression);
            writer.WriteStartArray("occurrences");
            foreach (DateTimeOffset t in occurrences)
            {
                writer.WriteStringValue(t.ToString("o", CultureInfo.InvariantCulture));
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Returns a JSON object for the <c>history</c> subcommand, containing the standard Winix
    /// envelope, the task <c>name</c>, and a <c>runs</c> array of run record objects.
    /// </summary>
    /// <param name="name">The task name whose history is being reported.</param>
    /// <param name="records">The run records to serialise. Must not be null.</param>
    /// <param name="exitCode">Process exit code written into the envelope.</param>
    /// <param name="exitReason">Machine-readable exit reason string.</param>
    /// <param name="version">Value for the <c>version</c> envelope field.</param>
    public static string FormatHistoryJson(
        string name,
        IReadOnlyList<TaskRunRecord> records,
        int exitCode,
        string exitReason,
        string version)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", "schedule");
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            writer.WriteString("name", name);
            writer.WriteStartArray("runs");
            foreach (TaskRunRecord r in records)
            {
                writer.WriteStartObject();
                writer.WriteString("start_time", r.StartTime.ToString("o", CultureInfo.InvariantCulture));
                if (r.ExitCode.HasValue)
                {
                    writer.WriteNumber("exit_code", r.ExitCode.Value);
                }
                else
                {
                    writer.WriteNull("exit_code");
                }
                if (r.Duration.HasValue)
                {
                    JsonHelper.WriteFixedDecimal(writer, "duration_seconds", r.Duration.Value.TotalSeconds, 1);
                }
                else
                {
                    writer.WriteNull("duration_seconds");
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Formats a <see cref="TimeSpan"/> as a human-readable duration: seconds, minutes, or hours.
    /// Returns <c>"running"</c> when <paramref name="duration"/> is <see langword="null"/>.
    /// </summary>
    private static string FormatDuration(TimeSpan? duration)
    {
        if (!duration.HasValue)
        {
            return "running";
        }

        double seconds = duration.Value.TotalSeconds;
        if (seconds < 60)
        {
            return seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        if (seconds < 3600)
        {
            return (seconds / 60).ToString("0.0", CultureInfo.InvariantCulture) + "m";
        }

        return (seconds / 3600).ToString("0.0", CultureInfo.InvariantCulture) + "h";
    }

    /// <summary>
    /// Truncates a string to the given max length, appending "…" if truncated.
    /// </summary>
    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        if (maxLength <= 1)
        {
            return value.Substring(0, maxLength);
        }

        return value.Substring(0, maxLength - 1) + "\u2026";
    }
}
