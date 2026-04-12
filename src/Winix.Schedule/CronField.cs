#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Winix.Schedule;

/// <summary>
/// Represents a single parsed cron field (minute, hour, day-of-month, month, or day-of-week).
/// Holds the sorted set of integer values that satisfy the field expression.
/// </summary>
public sealed class CronField
{
    /// <summary>
    /// Name-to-number mappings for month fields (jan=1 .. dec=12).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> MonthNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "jan", 1 }, { "feb", 2 }, { "mar", 3 }, { "apr", 4 },
        { "may", 5 }, { "jun", 6 }, { "jul", 7 }, { "aug", 8 },
        { "sep", 9 }, { "oct", 10 }, { "nov", 11 }, { "dec", 12 },
    };

    /// <summary>
    /// Name-to-number mappings for day-of-week fields (sun=0, mon=1 .. sat=6).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> DayOfWeekNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "sun", 0 }, { "mon", 1 }, { "tue", 2 }, { "wed", 3 },
        { "thu", 4 }, { "fri", 5 }, { "sat", 6 },
    };

    private readonly SortedSet<int> _values;

    private CronField(SortedSet<int> values)
    {
        _values = values;
    }

    /// <summary>
    /// The sorted set of values this field matches.
    /// </summary>
    public IReadOnlySet<int> Values => _values;

    /// <summary>
    /// Returns true if this field matches <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The value to test.</param>
    public bool Contains(int value)
    {
        return _values.Contains(value);
    }

    /// <summary>
    /// Parses a single cron field expression into a set of matching integer values.
    /// Supports wildcards (<c>*</c>), ranges (<c>1-5</c>), steps (<c>*/5</c>, <c>1-10/3</c>, <c>5/10</c>),
    /// comma-separated lists, and optional named values (months, days of week).
    /// </summary>
    /// <param name="expression">The field expression (e.g. "*/5", "1-5", "1,3,5").</param>
    /// <param name="min">Minimum allowed value for this field (inclusive).</param>
    /// <param name="max">Maximum allowed value for this field (inclusive).</param>
    /// <param name="names">Optional name-to-number map for named values (e.g. month or day-of-week names).</param>
    /// <returns>A parsed <see cref="CronField"/> containing the set of allowed values.</returns>
    /// <exception cref="FormatException">The expression is empty, contains invalid tokens, or values fall outside the allowed range.</exception>
    public static CronField Parse(string expression, int min, int max, IReadOnlyDictionary<string, int>? names = null)
    {
        if (string.IsNullOrEmpty(expression))
        {
            throw new FormatException("Cron field expression is empty.");
        }

        // DOW field treats 7 as an alias for 0 (both mean Sunday).
        bool isDayOfWeek = (names == DayOfWeekNames);
        var values = new SortedSet<int>();

        // Split on commas for list support.
        string[] parts = expression.Split(',');
        foreach (string part in parts)
        {
            if (part.Length == 0)
            {
                throw new FormatException($"Invalid cron field: trailing or empty element in '{expression}'.");
            }

            ParseElement(part, min, max, names, isDayOfWeek, values);
        }

        return new CronField(values);
    }

    /// <summary>
    /// Parses a single element of a cron field (a comma-delimited segment with no commas).
    /// Handles wildcards, ranges, steps, named values, and plain numbers.
    /// </summary>
    /// <param name="element">The segment to parse.</param>
    /// <param name="min">Minimum allowed value.</param>
    /// <param name="max">Maximum allowed value.</param>
    /// <param name="names">Optional name-to-number map.</param>
    /// <param name="isDayOfWeek">True when parsing a DOW field; enables 7→0 normalisation.</param>
    /// <param name="values">Accumulates parsed values.</param>
    private static void ParseElement(string element, int min, int max, IReadOnlyDictionary<string, int>? names, bool isDayOfWeek, SortedSet<int> values)
    {
        // Check for step: e.g. "*/5" or "1-10/3" or "5/10"
        int slashIndex = element.IndexOf('/');
        if (slashIndex >= 0)
        {
            string basePart = element.Substring(0, slashIndex);
            string stepPart = element.Substring(slashIndex + 1);

            if (!int.TryParse(stepPart, NumberStyles.None, CultureInfo.InvariantCulture, out int step) || step <= 0)
            {
                throw new FormatException($"Invalid step value '{stepPart}' in cron field '{element}'.");
            }

            int rangeStart;
            int rangeEnd;

            if (basePart == "*")
            {
                rangeStart = min;
                rangeEnd = max;
            }
            else if (basePart.Contains('-'))
            {
                (rangeStart, rangeEnd) = ParseRange(basePart, min, max, names, isDayOfWeek);
            }
            else
            {
                // "5/10" means start at 5, step up to max.
                rangeStart = ResolveValue(basePart, min, max, names, isDayOfWeek);
                rangeEnd = max;
            }

            for (int v = rangeStart; v <= rangeEnd; v += step)
            {
                int normalized = isDayOfWeek && v == 7 ? 0 : v;
                values.Add(normalized);
            }

            return;
        }

        // Wildcard: all values in range.
        if (element == "*")
        {
            for (int v = min; v <= max; v++)
            {
                values.Add(v);
            }

            return;
        }

        // Range: e.g. "1-5" or named range "jan-mar"
        if (element.Contains('-'))
        {
            (int start, int end) = ParseRange(element, min, max, names, isDayOfWeek);
            for (int v = start; v <= end; v++)
            {
                int normalized = isDayOfWeek && v == 7 ? 0 : v;
                values.Add(normalized);
            }

            return;
        }

        // Single value (number or named).
        int val = ResolveValue(element, min, max, names, isDayOfWeek);
        int normalizedVal = isDayOfWeek && val == 7 ? 0 : val;
        values.Add(normalizedVal);
    }

    /// <summary>
    /// Parses a range expression (e.g. "1-5" or "jan-mar") and returns (start, end) inclusive.
    /// </summary>
    /// <param name="rangeExpr">The range expression without a step component.</param>
    /// <param name="min">Minimum allowed value.</param>
    /// <param name="max">Maximum allowed value.</param>
    /// <param name="names">Optional name-to-number map.</param>
    /// <param name="isDayOfWeek">True when parsing a DOW field.</param>
    /// <returns>A tuple of (start, end) inclusive bounds.</returns>
    /// <exception cref="FormatException">Range start is greater than range end, or either bound is out of range.</exception>
    private static (int Start, int End) ParseRange(string rangeExpr, int min, int max, IReadOnlyDictionary<string, int>? names, bool isDayOfWeek)
    {
        int dashIndex = rangeExpr.IndexOf('-');
        string startStr = rangeExpr.Substring(0, dashIndex);
        string endStr = rangeExpr.Substring(dashIndex + 1);

        int start = ResolveValue(startStr, min, max, names, isDayOfWeek);
        int end = ResolveValue(endStr, min, max, names, isDayOfWeek);

        if (start > end)
        {
            throw new FormatException($"Invalid range '{rangeExpr}': start ({start}) is greater than end ({end}).");
        }

        return (start, end);
    }

    /// <summary>
    /// Resolves a single value token (name or number) to an integer, validating it against [min, max].
    /// For DOW fields, 7 is accepted and returned as-is so callers can normalise it to 0.
    /// </summary>
    /// <param name="token">The raw token string.</param>
    /// <param name="min">Minimum allowed value.</param>
    /// <param name="max">Maximum allowed value.</param>
    /// <param name="names">Optional name-to-number map.</param>
    /// <param name="isDayOfWeek">True when parsing a DOW field; allows the extended value 7.</param>
    /// <returns>The resolved integer value.</returns>
    /// <exception cref="FormatException">The token is not a recognised name or number, or is outside [min, max].</exception>
    private static int ResolveValue(string token, int min, int max, IReadOnlyDictionary<string, int>? names, bool isDayOfWeek)
    {
        // Try named lookup first (case-insensitive via dictionary comparer).
        if (names != null && names.TryGetValue(token, out int namedValue))
        {
            return namedValue;
        }

        if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            throw new FormatException($"Invalid cron field value '{token}': not a number or recognised name.");
        }

        // DOW: value 7 is an alias for Sunday (0) — accept it even though max is 6 in the names dict;
        // when max == 7 the range check below will pass naturally.
        if (isDayOfWeek && value == 7)
        {
            return 7; // Caller normalises to 0.
        }

        if (value < min || value > max)
        {
            throw new FormatException($"Cron field value {value} is outside the allowed range {min}-{max}.");
        }

        return value;
    }
}
