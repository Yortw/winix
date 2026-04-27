#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Winix.Schedule;

/// <summary>
/// Parses the CSV output from <c>schtasks /Query /FO CSV /V /NH</c> into
/// <see cref="ScheduledTask"/> objects.
/// </summary>
public static class SchtasksCsvParser
{
    // Column indices in the verbose CSV output (with /V /NH):
    // 0: HostName
    // 1: TaskName
    // 2: Next Run Time
    // 3: Status
    // 8: Task To Run
    // 10: Comment
    // 11: Scheduled Task State (Enabled/Disabled)
    // 17: Schedule (native schedule description)
    // 18: Schedule Type (e.g. "Daily", "Weekly")
    private const int ColTaskName = 1;
    private const int ColNextRunTime = 2;
    private const int ColTaskToRun = 8;
    private const int ColComment = 10;
    private const int ColState = 11;
    private const int ColScheduleDesc = 17;
    private const int ColScheduleType = 18;
    private const int MinColumns = 12;

    /// <summary>
    /// Parses schtasks CSV output into a list of <see cref="ScheduledTask"/> objects.
    /// </summary>
    /// <param name="csvOutput">Raw CSV text from schtasks.exe.</param>
    /// <param name="folder">The folder prefix to strip from task names (e.g. "\Winix").</param>
    public static IReadOnlyList<ScheduledTask> Parse(string csvOutput, string folder)
    {
        var tasks = new List<ScheduledTask>();
        if (string.IsNullOrWhiteSpace(csvOutput))
        {
            return tasks;
        }

        string folderPrefix = folder.TrimEnd('\\') + "\\";

        string[] lines = csvOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();
            if (trimmedLine.Length == 0)
            {
                continue;
            }

            string[] fields = ParseCsvLine(trimmedLine);
            if (fields.Length < MinColumns)
            {
                continue;
            }

            string fullTaskName = fields[ColTaskName];

            // Extract the folder and short name from the full task path.
            // e.g. "\Winix\health-check" → folder="\Winix", name="health-check"
            // e.g. "\Apple\AppleSoftwareUpdate" → folder="\Apple", name="AppleSoftwareUpdate"
            string taskName;
            string taskFolder;
            int lastSlash = fullTaskName.LastIndexOf('\\');
            if (lastSlash > 0)
            {
                taskFolder = fullTaskName.Substring(0, lastSlash);
                taskName = fullTaskName.Substring(lastSlash + 1);
            }
            else
            {
                taskFolder = "";
                taskName = fullTaskName.TrimStart('\\');
            }

            // Parse next run time. schtasks uses locale-dependent date formats. The
            // schedule console app sets <InvariantGlobalization>true</InvariantGlobalization>
            // for AOT size, which collapses CurrentCulture to InvariantCulture and prevents
            // loading additional named cultures at runtime. We therefore attempt a fixed
            // list of explicit format strings (en-US, dd/MM/yyyy locales like en-GB/AU/NZ,
            // German, Japanese, ISO) using InvariantCulture for the parse — covering the
            // schtasks output shapes most users will encounter without depending on
            // CultureInfo lookups that aren't available under InvariantGlobalization.
            DateTime? nextRun = null;
            string nextRunStr = fields[ColNextRunTime];
            if (!string.IsNullOrEmpty(nextRunStr) && nextRunStr != "N/A")
            {
                nextRun = TryParseScheduleDate(nextRunStr);
            }

            // Determine the schedule description.
            // 1. If Comment looks like a cron expression (starts with * or digit or @), use it.
            // 2. Otherwise use the native Schedule Type (col 18) like "Daily", "Weekly".
            // 3. Fall back to empty.
            string comment = fields[ColComment];
            string schedule;
            if (!string.IsNullOrEmpty(comment) && comment != "N/A" && LooksLikeCron(comment))
            {
                schedule = comment;
            }
            else
            {
                // Build a description from native schedule columns.
                schedule = BuildNativeScheduleDescription(fields);
            }
            string status = fields[ColState];     // "Enabled" or "Disabled"
            string command = fields[ColTaskToRun];

            tasks.Add(new ScheduledTask(taskName, schedule, nextRun, status, command, taskFolder));
        }

        return tasks;
    }

    /// <summary>
    /// Parses a single CSV line respecting quoted fields.
    /// Handles commas inside quotes and escaped double-quotes ("").
    /// </summary>
    public static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        int i = 0;

        while (i < line.Length)
        {
            if (line[i] == '"')
            {
                // Quoted field.
                i++; // Skip opening quote.
                var sb = new StringBuilder();
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            // Escaped quote.
                            sb.Append('"');
                            i += 2;
                        }
                        else
                        {
                            // End of quoted field.
                            i++; // Skip closing quote.
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(line[i]);
                        i++;
                    }
                }

                fields.Add(sb.ToString());

                // Skip comma separator.
                if (i < line.Length && line[i] == ',')
                {
                    i++;
                }
            }
            else if (line[i] == ',')
            {
                // Empty field.
                fields.Add("");
                i++;
            }
            else
            {
                // Unquoted field.
                int start = i;
                while (i < line.Length && line[i] != ',')
                {
                    i++;
                }

                fields.Add(line.Substring(start, i - start));

                // Skip comma separator.
                if (i < line.Length && line[i] == ',')
                {
                    i++;
                }
            }
        }

        return fields.ToArray();
    }

    // Additional column indices for building schedule descriptions.
    private const int ColStartTime = 19;
    private const int ColDays = 22;

    /// <summary>
    /// Builds a concise schedule description from the native schtasks CSV columns.
    /// E.g. "Daily 02:00", "Weekly Mon-Fri 09:00", "Every 5 min".
    /// </summary>
    private static string BuildNativeScheduleDescription(string[] fields)
    {
        string schedType = fields.Length > ColScheduleType
            ? fields[ColScheduleType].Trim()
            : "";

        if (string.IsNullOrEmpty(schedType) || schedType == "N/A"
            || schedType.StartsWith("Scheduling data", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        // Try to append the start time for daily/weekly/monthly
        string startTime = fields.Length > ColStartTime
            ? fields[ColStartTime].Trim()
            : "";

        // Clean up time: "02:00:00 AM" → "02:00"
        if (!string.IsNullOrEmpty(startTime) && startTime != "N/A")
        {
            DateTime? parsed = TryParseScheduleDate(startTime);
            startTime = parsed.HasValue
                ? parsed.Value.ToString("HH:mm", CultureInfo.InvariantCulture)
                : "";
        }
        else
        {
            startTime = "";
        }

        // Clean up schedule type: remove trailing whitespace, "One Time Only, " prefix
        schedType = schedType.TrimEnd();

        if (!string.IsNullOrEmpty(startTime))
        {
            return schedType + " " + startTime;
        }

        return schedType;
    }

    /// <summary>
    /// Returns true if the string looks like a cron expression rather than a
    /// human-readable description. Cron expressions start with *, a digit, or @.
    /// </summary>
    private static bool LooksLikeCron(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        char first = value[0];
        return first == '*' || first == '@' || (first >= '0' && first <= '9');
    }

    /// <summary>
    /// Schtasks date formats encountered in the wild. Order matters — earlier matches win.
    /// Note these are tried with <see cref="CultureInfo.InvariantCulture"/> only, because
    /// the schedule console app sets &lt;InvariantGlobalization&gt;true&lt;/InvariantGlobalization&gt;
    /// for AOT binary size. That switch strips the named-culture data needed to parse
    /// dd/MM/yyyy or yyyy/MM/dd via <see cref="CultureInfo.CurrentCulture"/>, so we use
    /// explicit format strings to recognise locale-formatted output instead.
    /// </summary>
    private static readonly string[] ScheduleDateFormats =
    {
        // en-US (also the InvariantCulture default).
        "M/d/yyyy h:mm:ss tt",
        "M/d/yyyy hh:mm:ss tt",
        "MM/dd/yyyy h:mm:ss tt",
        "MM/dd/yyyy hh:mm:ss tt",
        // en-GB / en-AU / en-NZ / many EU locales (24-hour).
        "d/M/yyyy H:mm:ss",
        "dd/MM/yyyy HH:mm:ss",
        "d/MM/yyyy HH:mm:ss",
        // German / Italian / Russian / many EU.
        "dd.MM.yyyy HH:mm:ss",
        "d.M.yyyy H:mm:ss",
        // Japanese / Chinese / Korean / Hungarian.
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/M/d H:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        // Time-only (used for /ST start-time columns).
        "HH:mm:ss",
        "h:mm:ss tt",
        "hh:mm:ss tt",
        "HH:mm",
    };

    /// <summary>
    /// Tries to parse a schtasks-emitted timestamp string against the known format set.
    /// Returns null when no format matches — callers should treat that as an unparsed
    /// rather than missing value. Always assumes local time when no zone is specified.
    /// </summary>
    private static DateTime? TryParseScheduleDate(string value)
    {
        if (DateTime.TryParseExact(
                value, ScheduleDateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out DateTime parsed))
        {
            return parsed;
        }

        // Last-resort generic parse; covers ISO 8601 with offset and a few extras the
        // exact-format list doesn't enumerate. Still InvariantCulture for AOT safety.
        if (DateTime.TryParse(
                value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out parsed))
        {
            return parsed;
        }

        return null;
    }
}
