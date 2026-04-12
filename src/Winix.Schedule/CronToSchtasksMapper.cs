#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Winix.Schedule;

/// <summary>
/// Maps a <see cref="CronExpression"/> to schtasks.exe scheduling parameters.
/// Handles common patterns; complex expressions fall back to MINUTE/1 with the
/// actual cron expression stored in the task's comment field for round-tripping.
/// </summary>
public static class CronToSchtasksMapper
{
    private static readonly string[] DayNames = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };

    /// <summary>
    /// Maps a parsed cron expression to the closest schtasks.exe schedule parameters.
    /// </summary>
    /// <param name="cron">The parsed cron expression to map.</param>
    /// <returns>
    /// A <see cref="SchtasksSchedule"/> with the appropriate /SC, /MO, /ST, and /D values.
    /// When no clean mapping exists, falls back to MINUTE/1 so that the task still runs;
    /// the original cron expression is preserved in the task comment for display.
    /// </returns>
    public static SchtasksSchedule Map(CronExpression cron)
    {
        bool allMinutes = cron.Minute.Values.Count == 60;
        bool allHours = cron.Hour.Values.Count == 24;
        bool allDom = cron.DayOfMonth.Values.Count == 31;
        bool allMonths = cron.Month.Values.Count == 12;
        bool allDow = cron.DayOfWeek.Values.Count >= 7; // 0-6 = 7 values

        // Pattern: */N * * * * -> MINUTE /MO N
        if (allHours && allDom && allMonths && allDow)
        {
            if (allMinutes)
            {
                return new SchtasksSchedule { ScheduleType = "MINUTE", Modifier = "1" };
            }

            int? minuteStep = DetectStep(cron.Minute, 0, 59);
            if (minuteStep.HasValue)
            {
                return new SchtasksSchedule { ScheduleType = "MINUTE", Modifier = minuteStep.Value.ToString(CultureInfo.InvariantCulture) };
            }
        }

        // Pattern: 0 */N * * * -> HOURLY /MO N
        if (cron.Minute.Values.Count == 1 && cron.Minute.Contains(0) && allDom && allMonths && allDow)
        {
            if (allHours)
            {
                return new SchtasksSchedule { ScheduleType = "HOURLY", Modifier = "1" };
            }

            int? hourStep = DetectStep(cron.Hour, 0, 23);
            if (hourStep.HasValue)
            {
                return new SchtasksSchedule { ScheduleType = "HOURLY", Modifier = hourStep.Value.ToString(CultureInfo.InvariantCulture) };
            }
        }

        // Extract single minute and hour for time-based patterns.
        int? singleMinute = cron.Minute.Values.Count == 1 ? GetSingle(cron.Minute) : null;
        int? singleHour = cron.Hour.Values.Count == 1 ? GetSingle(cron.Hour) : null;
        string? startTime = (singleMinute.HasValue && singleHour.HasValue)
            ? $"{singleHour.Value:D2}:{singleMinute.Value:D2}"
            : null;

        // Pattern: M H * * DOW -> WEEKLY /D days /ST time
        if (startTime != null && allDom && allMonths && !allDow)
        {
            var days = new StringBuilder();
            // Sort days for consistent output.
            var sortedDays = new List<int>(cron.DayOfWeek.Values);
            sortedDays.Sort();
            foreach (int d in sortedDays)
            {
                if (d >= 0 && d <= 6)
                {
                    if (days.Length > 0) { days.Append(','); }
                    days.Append(DayNames[d]);
                }
            }

            return new SchtasksSchedule
            {
                ScheduleType = "WEEKLY",
                StartTime = startTime,
                Days = days.ToString(),
            };
        }

        // Pattern: M H DOM * * -> MONTHLY /D dom /ST time
        if (startTime != null && !allDom && cron.DayOfMonth.Values.Count == 1 && allMonths && allDow)
        {
            int dom = GetSingle(cron.DayOfMonth);
            return new SchtasksSchedule
            {
                ScheduleType = "MONTHLY",
                StartTime = startTime,
                DayOfMonth = dom.ToString(CultureInfo.InvariantCulture),
            };
        }

        // Pattern: M H * * * -> DAILY /ST time
        if (startTime != null && allDom && allMonths && allDow)
        {
            return new SchtasksSchedule
            {
                ScheduleType = "DAILY",
                StartTime = startTime,
            };
        }

        // Fallback: MINUTE /MO 1 (run every minute). The cron expression stored in the
        // task's comment field will be the source of truth for `list`.
        return new SchtasksSchedule { ScheduleType = "MINUTE", Modifier = "1" };
    }

    /// <summary>
    /// Detects whether a field represents a simple step pattern starting from <paramref name="min"/>.
    /// Returns the step value if so, null otherwise.
    /// </summary>
    private static int? DetectStep(CronField field, int min, int max)
    {
        var sorted = new List<int>(field.Values);
        sorted.Sort();
        if (sorted.Count < 2 || sorted[0] != min)
        {
            return null;
        }

        int step = sorted[1] - sorted[0];
        if (step <= 0)
        {
            return null;
        }

        // Verify all values match the step pattern.
        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] != min + (i * step))
            {
                return null;
            }
        }

        // Verify the step covers the full range.
        int expectedCount = ((max - min) / step) + 1;
        if (sorted.Count != expectedCount)
        {
            return null;
        }

        return step;
    }

    /// <summary>Returns the single value from a field known to contain exactly one value.</summary>
    private static int GetSingle(CronField field)
    {
        foreach (int v in field.Values)
        {
            return v;
        }

        throw new InvalidOperationException("Field has no values.");
    }
}
