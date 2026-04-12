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

            string taskName = fields[ColTaskName];

            // Strip folder prefix from name.
            if (taskName.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                taskName = taskName.Substring(folderPrefix.Length);
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

            // Try the Comment field first (we store cron there if possible),
            // fall back to the native Schedule Type description.
            string schedule = fields[ColComment];
            if (string.IsNullOrEmpty(schedule) || schedule == "N/A")
            {
                // Use native schedule description from schtasks
                if (fields.Length > ColScheduleType)
                {
                    string schedType = fields[ColScheduleType].Trim();
                    if (!string.IsNullOrEmpty(schedType) && schedType != "N/A")
                    {
                        schedule = schedType;
                    }
                }
            }
            string status = fields[ColState];     // "Enabled" or "Disabled"
            string command = fields[ColTaskToRun];

            tasks.Add(new ScheduledTask(taskName, schedule, nextRun, status, command, folder));
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
}
