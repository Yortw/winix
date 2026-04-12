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

            // Parse next run time. schtasks uses locale-dependent date formats, but typically
            // "M/d/yyyy h:mm:ss tt" for en-US. "N/A" means no next run.
            DateTime? nextRun = null;
            string nextRunStr = fields[ColNextRunTime];
            if (!string.IsNullOrEmpty(nextRunStr) && nextRunStr != "N/A")
            {
                // schtasks uses the system locale for date formatting. Use CurrentCulture
                // (not InvariantCulture) so that dd/MM/yyyy locales parse correctly.
                if (DateTime.TryParse(nextRunStr, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out DateTime parsed))
                {
                    nextRun = parsed;
                }
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
            if (DateTime.TryParse(startTime, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out DateTime parsed))
            {
                startTime = parsed.ToString("HH:mm");
            }
            else
            {
                startTime = "";
            }
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
}
