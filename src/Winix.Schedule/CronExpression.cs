#nullable enable

using System;
using System.Collections.Generic;

namespace Winix.Schedule;

/// <summary>
/// Represents a parsed 5-field cron expression (minute hour day-of-month month day-of-week).
/// Supports standard numeric fields, wildcards, ranges, steps, comma-separated lists, and
/// named month/day-of-week values. Also accepts <c>@special</c> strings as shortcuts.
/// </summary>
public sealed class CronExpression
{
    private static readonly Dictionary<string, string> SpecialStrings = new(StringComparer.OrdinalIgnoreCase)
    {
        { "@hourly",    "0 * * * *"   },
        { "@daily",     "0 0 * * *"   },
        { "@midnight",  "0 0 * * *"   },
        { "@weekly",    "0 0 * * 0"   },
        { "@monthly",   "0 0 1 * *"   },
        { "@yearly",    "0 0 1 1 *"   },
        { "@annually",  "0 0 1 1 *"   },
    };

    /// <summary>
    /// The original (trimmed) cron expression string as supplied to <see cref="Parse"/>.
    /// </summary>
    public string Expression { get; }

    /// <summary>The minute field (0–59).</summary>
    internal CronField Minute { get; }

    /// <summary>The hour field (0–23).</summary>
    internal CronField Hour { get; }

    /// <summary>The day-of-month field (1–31).</summary>
    internal CronField DayOfMonth { get; }

    /// <summary>The month field (1–12).</summary>
    internal CronField Month { get; }

    /// <summary>The day-of-week field (0–6, where both 0 and 7 represent Sunday).</summary>
    internal CronField DayOfWeek { get; }

    private CronExpression(string expression, CronField minute, CronField hour, CronField dayOfMonth, CronField month, CronField dayOfWeek)
    {
        Expression = expression;
        Minute = minute;
        Hour = hour;
        DayOfMonth = dayOfMonth;
        Month = month;
        DayOfWeek = dayOfWeek;
    }

    /// <summary>
    /// Parses a cron expression string into a <see cref="CronExpression"/>.
    /// Accepts standard 5-field cron syntax and <c>@special</c> shortcut strings
    /// (<c>@hourly</c>, <c>@daily</c>, <c>@weekly</c>, <c>@monthly</c>, <c>@yearly</c>, <c>@annually</c>, <c>@midnight</c>).
    /// </summary>
    /// <param name="expression">The cron expression to parse.</param>
    /// <returns>A parsed <see cref="CronExpression"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
    /// <exception cref="FormatException">
    /// The expression is empty, does not contain exactly five whitespace-separated fields,
    /// or any field contains an invalid or out-of-range value.
    /// </exception>
    public static CronExpression Parse(string expression)
    {
        if (expression == null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        string trimmed = expression.Trim();

        if (trimmed.Length == 0)
        {
            throw new FormatException("Cron expression must not be empty.");
        }

        // Expand @special shortcuts to their equivalent 5-field form before further parsing.
        if (trimmed.StartsWith("@", StringComparison.Ordinal))
        {
            if (!SpecialStrings.TryGetValue(trimmed, out string? expanded))
            {
                throw new FormatException($"Unknown cron special string '{trimmed}'.");
            }

            trimmed = expanded;
        }

        string[] fields = trimmed.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (fields.Length != 5)
        {
            throw new FormatException(
                $"Cron expression must have exactly 5 fields; got {fields.Length} in '{trimmed}'.");
        }

        CronField minute     = CronField.Parse(fields[0], 0,  59);
        CronField hour       = CronField.Parse(fields[1], 0,  23);
        CronField dayOfMonth = CronField.Parse(fields[2], 1,  31);
        CronField month      = CronField.Parse(fields[3], 1,  12, CronField.MonthNames);
        CronField dayOfWeek  = CronField.Parse(fields[4], 0,   7, CronField.DayOfWeekNames);

        return new CronExpression(trimmed, minute, hour, dayOfMonth, month, dayOfWeek);
    }

    /// <summary>
    /// Returns the next occurrence of this cron schedule strictly after <paramref name="after"/>,
    /// interpreted in the local timezone. Searches up to 8 years ahead; throws
    /// <see cref="InvalidOperationException"/> if no match is found within that horizon
    /// (e.g. Feb 29 with no leap year in range).
    /// </summary>
    /// <param name="after">The reference point; the returned time will be strictly after this value.</param>
    /// <returns>
    /// The next matching <see cref="DateTimeOffset"/>, carrying the UTC offset that applies
    /// to its own local wall-clock date — i.e. correctly reflecting DST transitions between
    /// <paramref name="after"/> and the returned occurrence.
    /// </returns>
    /// <exception cref="InvalidOperationException">No matching occurrence found within 8 years.</exception>
    public DateTimeOffset GetNextOccurrence(DateTimeOffset after) =>
        GetNextOccurrence(after, TimeZoneInfo.Local);

    /// <summary>
    /// Returns the next occurrence in a specified timezone. Internal so tests can pin
    /// behaviour deterministically without depending on the host machine's timezone.
    /// </summary>
    /// <param name="after">The reference point; the returned time will be strictly after this value.</param>
    /// <param name="timeZone">The timezone whose wall-clock the cron expression is interpreted in.</param>
    /// <returns>The next matching <see cref="DateTimeOffset"/>.</returns>
    /// <exception cref="InvalidOperationException">No matching occurrence found within 8 years.</exception>
    /// <remarks>
    /// Arithmetic is performed on a wall-clock <see cref="DateTime"/> (Kind = Unspecified)
    /// in <paramref name="timeZone"/>. Each candidate's UTC offset is recomputed via
    /// <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/> so that occurrences crossing a
    /// DST boundary correctly carry the post-transition offset. Local times that fall
    /// inside a spring-forward gap are skipped (advanced minute-by-minute) until the
    /// next valid wall-clock time.
    /// </remarks>
    internal DateTimeOffset GetNextOccurrence(DateTimeOffset after, TimeZoneInfo timeZone)
    {
        // Convert 'after' to wall-clock in the chosen timezone, drop sub-minute precision,
        // and advance one minute (strictly-after semantics).
        DateTime afterLocal = TimeZoneInfo.ConvertTime(after, timeZone).DateTime;
        DateTime candidate = new DateTime(
            afterLocal.Year, afterLocal.Month, afterLocal.Day,
            afterLocal.Hour, afterLocal.Minute, 0, DateTimeKind.Unspecified)
            .AddMinutes(1);

        // Safety limit: 8 years' worth of wall-clock minutes.
        DateTime limit = afterLocal.AddYears(8);

        bool domRestricted = DayOfMonth.Values.Count < 31;
        bool dowRestricted = DayOfWeek.Values.Count < 7;

        while (candidate <= limit)
        {
            // Skip wall-clock times that don't exist (DST spring-forward gap). Advance
            // minute-by-minute so the search resumes at the next valid local time —
            // this matches the conservative "skip the gap" semantic; vixie cron fires
            // once at the start or end of the gap depending on impl, but for display
            // purposes consistency-with-wall-clock is what matters.
            if (timeZone.IsInvalidTime(candidate))
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }

            // Check month first (coarsest).
            if (!Month.Contains(candidate.Month))
            {
                candidate = NextMonth(candidate);
                continue;
            }

            // Check day: standard cron OR logic for DOM/DOW.
            bool domMatch = DayOfMonth.Contains(candidate.Day);
            bool dowMatch = DayOfWeek.Contains((int)candidate.DayOfWeek);

            bool dayMatch;
            if (domRestricted && dowRestricted)
            {
                dayMatch = domMatch || dowMatch;
            }
            else if (domRestricted)
            {
                dayMatch = domMatch;
            }
            else if (dowRestricted)
            {
                dayMatch = dowMatch;
            }
            else
            {
                dayMatch = true;
            }

            if (!dayMatch)
            {
                candidate = NextDay(candidate);
                continue;
            }

            if (!Hour.Contains(candidate.Hour))
            {
                candidate = NextHour(candidate);
                continue;
            }

            if (!Minute.Contains(candidate.Minute))
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }

            // Found a match. Carry the UTC offset that applies to this candidate's
            // own local date; for ambiguous times (fall-back overlap) .NET returns
            // the standard offset by default — fire-once semantic, acceptable for
            // a display tool.
            TimeSpan offset = timeZone.GetUtcOffset(candidate);
            return new DateTimeOffset(candidate, offset);
        }

        throw new InvalidOperationException(
            $"No matching occurrence found within 8 years for cron expression '{Expression}'.");
    }

    /// <summary>
    /// Advances to midnight on the 1st of the next month (wall-clock).
    /// </summary>
    private static DateTime NextMonth(DateTime dt)
    {
        int year = dt.Year;
        int month = dt.Month + 1;
        if (month > 12)
        {
            month = 1;
            year++;
        }

        return new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// Advances to midnight of the next day (wall-clock).
    /// </summary>
    private static DateTime NextDay(DateTime dt)
    {
        return new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Unspecified).AddDays(1);
    }

    /// <summary>
    /// Advances to the start of the next hour (wall-clock).
    /// </summary>
    private static DateTime NextHour(DateTime dt)
    {
        return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, DateTimeKind.Unspecified).AddHours(1);
    }

    /// <summary>
    /// Returns the next <paramref name="count"/> occurrences of this cron schedule after <paramref name="after"/>.
    /// </summary>
    /// <param name="after">The reference point; returned times are strictly after this value.</param>
    /// <param name="count">The number of occurrences to return.</param>
    /// <returns>A list of the next <paramref name="count"/> matching <see cref="DateTimeOffset"/> values.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// Propagated from <see cref="GetNextOccurrence"/> when no matching occurrence is found
    /// within the 8-year search horizon.
    /// </exception>
    public IReadOnlyList<DateTimeOffset> GetNextOccurrences(DateTimeOffset after, int count) =>
        GetNextOccurrences(after, count, TimeZoneInfo.Local);

    /// <summary>
    /// Returns the next <paramref name="count"/> occurrences in a specified timezone.
    /// Internal so tests can pin behaviour deterministically without depending on the host
    /// machine's timezone.
    /// </summary>
    /// <param name="after">The reference point; returned times are strictly after this value.</param>
    /// <param name="count">The number of occurrences to return.</param>
    /// <param name="timeZone">The timezone whose wall-clock the cron expression is interpreted in.</param>
    internal IReadOnlyList<DateTimeOffset> GetNextOccurrences(DateTimeOffset after, int count, TimeZoneInfo timeZone)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be non-negative.");
        }

        var results = new List<DateTimeOffset>(count);
        DateTimeOffset current = after;

        for (int i = 0; i < count; i++)
        {
            current = GetNextOccurrence(current, timeZone);
            results.Add(current);
        }

        return results;
    }
}
