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

                int next = FindCronLineIndex(lines, i);
                if (next < 0)
                {
                    // Orphan tag — either ran off EOF with nothing but blanks, or the next
                    // non-blank line is another winix tag. Either way: emit a placeholder
                    // so the listing surfaces the corruption rather than silently dropping
                    // the user's task name.
                    tasks.Add(new ScheduledTask(name, "", null, "Unknown (no cron line)", "", ""));
                    continue;
                }

                string cronLine = lines[next].TrimEnd('\r');
                {
                    bool disabled = cronLine.StartsWith("# ", StringComparison.Ordinal);
                    string activeLine = disabled ? cronLine.Substring(2) : cronLine;

                    string cronFields = ExtractCronFields(activeLine);
                    string command = ExtractCommand(activeLine);
                    string status = disabled ? "Disabled" : "Enabled";

                    DateTime? nextRun = null;
                    if (!disabled)
                    {
                        // Catch only the exceptions CronExpression.Parse and GetNextOccurrence
                        // can produce (FormatException for bad syntax, InvalidOperationException
                        // when no occurrence is found within 8 years). A bare catch here would
                        // also swallow OutOfMemoryException / StackOverflowException / runtime
                        // errors elsewhere and leave the user with no diagnostic at all.
                        try
                        {
                            var expr = CronExpression.Parse(cronFields);
                            nextRun = expr.GetNextOccurrence(DateTimeOffset.Now).LocalDateTime;
                        }
                        catch (FormatException) { /* unparseable cron — leave nextRun null */ }
                        catch (InvalidOperationException) { /* no occurrence within 8 years */ }
                    }

                    tasks.Add(new ScheduledTask(name, cronFields, nextRun, status, command, ""));
                    // Advance the index PAST the cron line we just consumed (which lived at
                    // 'next', possibly several lines ahead of i+1 due to blank-line spacing).
                    // The for-loop's i++ then moves us to next+1.
                    i = next;
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
                catch (FormatException) { /* unparseable cron — leave nextRun null */ }
                catch (InvalidOperationException) { /* no occurrence within 8 years */ }

                // Use the command as the display name since there is no winix name tag.
                tasks.Add(new ScheduledTask(command, cronFields, nextRun, "Enabled", command, ""));
            }
        }

        return tasks;
    }

    /// <summary>Appends a winix-tagged cron entry to the crontab content.</summary>
    /// <param name="crontabContent">The existing crontab text.</param>
    /// <param name="name">The winix task name. Must not contain newlines, carriage returns, or the <c># winix:</c> tag prefix.</param>
    /// <param name="cronExpression">The 5-field cron expression (e.g. <c>*/5 * * * *</c>). Must not contain newlines.</param>
    /// <param name="command">The shell command to run. Must not contain newlines.</param>
    /// <returns>The updated crontab text with the new entry appended.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/>, <paramref name="cronExpression"/>, or
    /// <paramref name="command"/> contains a newline / carriage-return character, or when
    /// <paramref name="name"/> would forge an additional <c># winix:</c> tag. Without this
    /// check, a user-supplied newline in any of the three could inject a second cron entry
    /// into the user's crontab — the literal task is registered, AND a hidden second task
    /// runs alongside it.
    /// </exception>
    public static string AddEntry(string crontabContent, string name, string cronExpression, string command)
    {
        ValidateNoNewlines(name, nameof(name));
        ValidateNoNewlines(cronExpression, nameof(cronExpression));
        ValidateNoNewlines(command, nameof(command));
        if (name.Contains(WinixTagPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Task name must not contain '{WinixTagPrefix}' — would forge an additional crontab tag.",
                nameof(name));
        }

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
                // Skip the tag line, any blank lines between tag and cron line (legitimate
                // readability spacing), and the cron line itself. Without the blank-line
                // skip, RemoveEntry would leave the user's actual cron line orphaned in
                // the crontab — the user would see 'Removed task X.' but the task would
                // continue to fire from the un-tagged cron line.
                int next = FindCronLineIndex(lines, i);
                i = next >= 0 ? next : i;
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

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when <paramref name="value"/> contains a newline
    /// or carriage-return character. Crontab lines are newline-delimited, so any unfiltered
    /// newline in name/cron/command would split a single entry into multiple lines —
    /// silently registering an extra winix-or-not task in the user's crontab.
    /// </summary>
    private static void ValidateNoNewlines(string value, string paramName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }

        if (value.IndexOfAny(NewlineChars) >= 0)
        {
            throw new ArgumentException(
                $"{paramName} must not contain newline or carriage-return characters " +
                "(would inject additional crontab entries).",
                paramName);
        }
    }

    private static readonly char[] NewlineChars = { '\n', '\r' };

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

            if (!line.Equals(tag, StringComparison.Ordinal))
            {
                continue;
            }

            int cronIndex = FindCronLineIndex(lines, i);
            if (cronIndex < 0)
            {
                // Orphan tag (no cron line, or next non-blank is another tag). Nothing to
                // toggle — leave the surrounding content untouched and let the next loop
                // iteration handle subsequent lines.
                continue;
            }

            // Emit any intervening blank lines verbatim so they survive the round-trip.
            for (int j = i + 1; j < cronIndex; j++)
            {
                sb.Append(lines[j].TrimEnd('\r'));
                sb.Append('\n');
            }

            // Now toggle the actual cron line at cronIndex.
            string cronLine = lines[cronIndex].TrimEnd('\r');
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

            if (cronIndex < lines.Length - 1)
            {
                sb.Append('\n');
            }

            // Advance past the cron line so the for-loop's i++ moves to the line after.
            i = cronIndex;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Given the index of a <see cref="WinixTagPrefix"/> line, returns the index of the cron
    /// line that belongs to it — skipping any intervening blank/whitespace-only lines, which
    /// users frequently insert as visual separators when hand-editing crontabs. Returns
    /// <c>-1</c> for an orphan tag (next non-blank is another winix tag, or end of file).
    /// </summary>
    /// <remarks>
    /// Centralising this logic ensures all four call sites (<see cref="ParseEntries"/>,
    /// <see cref="RemoveEntry"/>, <see cref="DisableEntry"/>, <see cref="EnableEntry"/>)
    /// agree on which line is "the cron line" for a given tag. Without this, R3's blank-
    /// line tolerance in <see cref="ParseEntries"/> diverged from <c>RemoveEntry</c> and
    /// <c>ToggleEntry</c> — which still hard-coded strict adjacency — producing visible
    /// task lifecycle corruption (a 'list' that showed the task healthy paired with a
    /// 'disable' that left it firing).
    /// </remarks>
    private static int FindCronLineIndex(string[] lines, int tagIndex)
    {
        for (int i = tagIndex + 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            if (line.StartsWith(WinixTagPrefix, StringComparison.Ordinal))
            {
                // Adjacent winix tag — current tag is orphan, return so the caller can
                // emit/skip-as-orphan and the for-loop can pick up the next tag fresh.
                return -1;
            }
            return i;
        }
        return -1;
    }
}
