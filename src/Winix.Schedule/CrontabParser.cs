#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace Winix.Schedule;

/// <summary>
/// Parses and manipulates crontab file contents. Winix-managed entries are identified
/// by <c># winix:&lt;name&gt;</c> comment tags on the line preceding the cron entry.
/// </summary>
public static class CrontabParser
{
    private const string WinixTagPrefix = "# winix:";

    /// <summary>
    /// Parses crontab content into a list of <see cref="ScheduledTask"/> objects.
    /// </summary>
    /// <param name="crontabContent">Full text of the crontab.</param>
    /// <param name="winixOnly">When <c>true</c>, only return entries tagged with <c># winix:</c>.</param>
    /// <returns>A read-only list of tasks; never null.</returns>
    public static IReadOnlyList<ScheduledTask> ParseEntries(string crontabContent, bool winixOnly)
    {
        var tasks = new List<ScheduledTask>();
        if (string.IsNullOrEmpty(crontabContent))
        {
            return tasks;
        }

        string[] lines = crontabContent.Split(new[] { '\n' }, StringSplitOptions.None);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            // Check for a winix tag comment.
            if (line.StartsWith(WinixTagPrefix, StringComparison.Ordinal))
            {
                string name = line.Substring(WinixTagPrefix.Length).Trim();

                // The next line should be the cron entry (possibly commented-out if disabled).
                if (i + 1 < lines.Length)
                {
                    string cronLine = lines[i + 1].TrimEnd('\r');
                    bool disabled = cronLine.StartsWith("# ", StringComparison.Ordinal);
                    string activeLine = disabled ? cronLine.Substring(2) : cronLine;

                    string cronFields = ExtractCronFields(activeLine);
                    string command = ExtractCommand(activeLine);
                    string status = disabled ? "Disabled" : "Enabled";

                    DateTime? nextRun = null;
                    if (!disabled)
                    {
                        try
                        {
                            var expr = CronExpression.Parse(cronFields);
                            // Convert to local DateTime for ScheduledTask.
                            nextRun = expr.GetNextOccurrence(DateTimeOffset.Now).LocalDateTime;
                        }
                        catch
                        {
                            // Unparseable cron — leave nextRun as null.
                        }
                    }

                    tasks.Add(new ScheduledTask(name, cronFields, nextRun, status, command, ""));
                    i++; // Skip the cron line — we already consumed it.
                }

                continue;
            }

            // Non-winix cron entry (no preceding winix tag). Only included when winixOnly is false.
            if (!winixOnly && line.Length > 0 && !line.StartsWith('#'))
            {
                string cronFields = ExtractCronFields(line);
                string command = ExtractCommand(line);

                DateTime? nextRun = null;
                try
                {
                    var expr = CronExpression.Parse(cronFields);
                    nextRun = expr.GetNextOccurrence(DateTimeOffset.Now).LocalDateTime;
                }
                catch
                {
                    // Unparseable cron — leave nextRun as null.
                }

                // Use the command as the display name since there is no winix name tag.
                tasks.Add(new ScheduledTask(command, cronFields, nextRun, "Enabled", command, ""));
            }
        }

        return tasks;
    }

    /// <summary>Appends a winix-tagged cron entry to the crontab content.</summary>
    /// <param name="crontabContent">The existing crontab text.</param>
    /// <param name="name">The winix task name.</param>
    /// <param name="cronExpression">The 5-field cron expression (e.g. <c>*/5 * * * *</c>).</param>
    /// <param name="command">The shell command to run.</param>
    /// <returns>The updated crontab text with the new entry appended.</returns>
    public static string AddEntry(string crontabContent, string name, string cronExpression, string command)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(crontabContent))
        {
            sb.Append(crontabContent);
            if (!crontabContent.EndsWith('\n'))
            {
                sb.Append('\n');
            }
        }

        sb.Append(WinixTagPrefix);
        sb.Append(name);
        sb.Append('\n');
        sb.Append(cronExpression);
        sb.Append(' ');
        sb.Append(command);
        sb.Append('\n');

        return sb.ToString();
    }

    /// <summary>
    /// Removes a winix-tagged entry (tag line + cron line) from the crontab.
    /// If the named entry does not exist, the original content is returned unchanged.
    /// </summary>
    /// <param name="crontabContent">The existing crontab text.</param>
    /// <param name="name">The winix task name to remove.</param>
    /// <returns>The updated crontab text with the named entry removed.</returns>
    public static string RemoveEntry(string crontabContent, string name)
    {
        string tag = WinixTagPrefix + name;
        string[] lines = crontabContent.Split(new[] { '\n' }, StringSplitOptions.None);
        var sb = new StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (line.Equals(tag, StringComparison.Ordinal))
            {
                // Skip this tag line and the following cron line.
                if (i + 1 < lines.Length)
                {
                    i++; // Skip cron line.
                }

                continue;
            }

            sb.Append(line);
            if (i < lines.Length - 1)
            {
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Disables a winix entry by commenting out its cron line with a <c># </c> prefix.
    /// If the entry is already disabled or not found, the content is returned unchanged.
    /// </summary>
    /// <param name="crontabContent">The existing crontab text.</param>
    /// <param name="name">The winix task name to disable.</param>
    /// <returns>The updated crontab text.</returns>
    public static string DisableEntry(string crontabContent, string name)
    {
        return ToggleEntry(crontabContent, name, disable: true);
    }

    /// <summary>
    /// Enables a winix entry by removing the <c># </c> prefix from its cron line.
    /// If the entry is already enabled or not found, the content is returned unchanged.
    /// </summary>
    /// <param name="crontabContent">The existing crontab text.</param>
    /// <param name="name">The winix task name to enable.</param>
    /// <returns>The updated crontab text.</returns>
    public static string EnableEntry(string crontabContent, string name)
    {
        return ToggleEntry(crontabContent, name, disable: false);
    }

    /// <summary>
    /// Extracts the command portion from a crontab line (everything after the 5 cron fields).
    /// Returns an empty string if the line has fewer than 5 fields.
    /// </summary>
    /// <param name="cronLine">A single crontab line (no newlines).</param>
    public static string ExtractCommand(string cronLine)
    {
        // Skip 5 space-separated fields, then return the rest.
        int fieldCount = 0;
        int i = 0;

        // Skip leading whitespace.
        while (i < cronLine.Length && char.IsWhiteSpace(cronLine[i])) { i++; }

        while (fieldCount < 5 && i < cronLine.Length)
        {
            // Skip field token.
            while (i < cronLine.Length && !char.IsWhiteSpace(cronLine[i])) { i++; }
            fieldCount++;

            // Skip whitespace separator.
            while (i < cronLine.Length && char.IsWhiteSpace(cronLine[i])) { i++; }
        }

        return i < cronLine.Length ? cronLine.Substring(i) : "";
    }

    /// <summary>
    /// Extracts the first 5 cron fields from a crontab line, preserving the original spacing
    /// between fields but stripping leading whitespace.
    /// Returns an empty string if the line has fewer than 5 fields.
    /// </summary>
    /// <param name="cronLine">A single crontab line (no newlines).</param>
    public static string ExtractCronFields(string cronLine)
    {
        int fieldCount = 0;
        int i = 0;

        while (i < cronLine.Length && char.IsWhiteSpace(cronLine[i])) { i++; }

        int start = i;
        int endOfFields = i;

        while (fieldCount < 5 && i < cronLine.Length)
        {
            // Skip field token.
            while (i < cronLine.Length && !char.IsWhiteSpace(cronLine[i])) { i++; }
            fieldCount++;
            endOfFields = i;

            // Skip whitespace separator.
            while (i < cronLine.Length && char.IsWhiteSpace(cronLine[i])) { i++; }
        }

        return cronLine.Substring(start, endOfFields - start);
    }

    /// <summary>Comments out or uncomments the cron line following a winix tag.</summary>
    private static string ToggleEntry(string crontabContent, string name, bool disable)
    {
        string tag = WinixTagPrefix + name;
        string[] lines = crontabContent.Split(new[] { '\n' }, StringSplitOptions.None);
        var sb = new StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            sb.Append(line);
            if (i < lines.Length - 1)
            {
                sb.Append('\n');
            }

            if (line.Equals(tag, StringComparison.Ordinal) && i + 1 < lines.Length)
            {
                i++;
                string cronLine = lines[i].TrimEnd('\r');
                sb.Append('\n');

                if (disable && !cronLine.StartsWith("# ", StringComparison.Ordinal))
                {
                    sb.Append("# ");
                    sb.Append(cronLine);
                }
                else if (!disable && cronLine.StartsWith("# ", StringComparison.Ordinal))
                {
                    sb.Append(cronLine.Substring(2));
                }
                else
                {
                    // Already in the desired state — leave unchanged.
                    sb.Append(cronLine);
                }

                if (i < lines.Length - 1)
                {
                    sb.Append('\n');
                }
            }
        }

        return sb.ToString();
    }
}
