#nullable enable
using System.Globalization;

namespace Winix.When;

/// <summary>
/// Parses and formats ISO 8601 duration strings in the <c>PnDTnHnMnS</c> format.
/// Years and months are not supported because they are calendar-dependent and cannot
/// be converted to a fixed <see cref="TimeSpan"/> without a reference date.
/// </summary>
public static class IsoDurationParser
{
    /// <summary>
    /// Attempts to parse an ISO 8601 duration string into a <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="input">The ISO 8601 duration string to parse (e.g. <c>P3DT4H12M</c>).</param>
    /// <param name="result">The parsed duration on success; <see cref="TimeSpan.Zero"/> on failure.</param>
    /// <param name="error">A human-readable error message on failure; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Supports days (D), hours (H), minutes (M), and seconds (S), including fractional seconds.
    /// Rejects years (Y), months (M in date part), and weeks (W) as calendar-dependent or unsupported.
    /// The string must start with uppercase <c>P</c>; lowercase is rejected per ISO 8601.
    /// </remarks>
    public static bool TryParse(string input, out TimeSpan result, out string? error)
    {
        result = TimeSpan.Zero;
        error = null;

        if (string.IsNullOrEmpty(input) || input[0] != 'P')
        {
            error = "ISO 8601 duration must start with 'P'.";
            return false;
        }

        ReadOnlySpan<char> span = input.AsSpan(1);

        // Bare "P" with nothing following is empty.
        if (span.Length == 0)
        {
            error = "Empty ISO 8601 duration.";
            return false;
        }

        bool inTimePart = false;
        int days = 0;
        int hours = 0;
        int minutes = 0;
        double seconds = 0;
        int numStart = -1;

        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];

            if (c == 'T')
            {
                inTimePart = true;
                numStart = -1;
                continue;
            }

            if ((c >= '0' && c <= '9') || c == '.')
            {
                if (numStart < 0) { numStart = i; }
                continue;
            }

            if (numStart < 0)
            {
                error = $"Expected a number before '{c}' in ISO 8601 duration.";
                return false;
            }

            ReadOnlySpan<char> numSpan = span.Slice(numStart, i - numStart);
            numStart = -1;

            if (c == 'Y')
            {
                error = "Years (Y) are not supported — they are calendar-dependent. Use days instead.";
                return false;
            }

            if (c == 'W')
            {
                error = "Weeks (W) are not supported. Use days instead (e.g. P14D instead of P2W).";
                return false;
            }

            if (!inTimePart && c == 'M')
            {
                error = "Months (M) are not supported — they are calendar-dependent. Use days instead.";
                return false;
            }

            if (!inTimePart && c == 'D')
            {
                if (!int.TryParse(numSpan, NumberStyles.None, CultureInfo.InvariantCulture, out days))
                {
                    error = "Invalid day value in ISO 8601 duration.";
                    return false;
                }
                continue;
            }

            if (inTimePart)
            {
                if (c == 'H')
                {
                    if (!int.TryParse(numSpan, NumberStyles.None, CultureInfo.InvariantCulture, out hours))
                    {
                        error = "Invalid hour value in ISO 8601 duration.";
                        return false;
                    }
                }
                else if (c == 'M')
                {
                    if (!int.TryParse(numSpan, NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
                    {
                        error = "Invalid minute value in ISO 8601 duration.";
                        return false;
                    }
                }
                else if (c == 'S')
                {
                    if (!double.TryParse(numSpan, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out seconds))
                    {
                        error = "Invalid second value in ISO 8601 duration.";
                        return false;
                    }
                }
                else
                {
                    error = $"Unexpected designator '{c}' in time part of ISO 8601 duration.";
                    return false;
                }
                continue;
            }

            error = $"Unexpected designator '{c}' in date part of ISO 8601 duration.";
            return false;
        }

        if (numStart >= 0)
        {
            error = "ISO 8601 duration has trailing digits with no designator (D, H, M, or S).";
            return false;
        }

        try
        {
            result = new TimeSpan(days, hours, minutes, 0) + TimeSpan.FromSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            error = "ISO 8601 duration value is out of range for TimeSpan.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Formats a <see cref="TimeSpan"/> as an ISO 8601 duration string.
    /// </summary>
    /// <param name="duration">The duration to format.</param>
    /// <returns>
    /// An ISO 8601 duration string such as <c>P3DT4H12M</c>.
    /// Negative durations are prefixed with <c>-</c> (e.g. <c>-P7DT0H0M</c>).
    /// When days are present, hours and minutes are always included for round-trip clarity.
    /// Zero duration is rendered as <c>PT0S</c>.
    /// </returns>
    public static string Format(TimeSpan duration)
    {
        string prefix = "";
        if (duration < TimeSpan.Zero)
        {
            prefix = "-";
            duration = duration.Negate();
        }

        int days = duration.Days;
        int hours = duration.Hours;
        int minutes = duration.Minutes;
        int seconds = duration.Seconds;
        int milliseconds = duration.Milliseconds;

        if (days == 0 && hours == 0 && minutes == 0 && seconds == 0 && milliseconds == 0)
        {
            return "PT0S";
        }

        var sb = new System.Text.StringBuilder(20);
        sb.Append(prefix);
        sb.Append('P');

        if (days > 0)
        {
            sb.Append(days.ToString(CultureInfo.InvariantCulture));
            sb.Append('D');
        }

        bool hasTimeComponents = hours > 0 || minutes > 0 || seconds > 0 || milliseconds > 0;
        if (hasTimeComponents || days > 0)
        {
            sb.Append('T');
        }

        if (days > 0)
        {
            // Always emit H and M alongside D so the output round-trips cleanly.
            sb.Append(hours.ToString(CultureInfo.InvariantCulture));
            sb.Append('H');
            sb.Append(minutes.ToString(CultureInfo.InvariantCulture));
            sb.Append('M');
            if (seconds > 0 || milliseconds > 0)
            {
                AppendSeconds(sb, seconds, milliseconds);
            }
        }
        else
        {
            if (hours > 0)
            {
                sb.Append(hours.ToString(CultureInfo.InvariantCulture));
                sb.Append('H');
            }
            if (minutes > 0)
            {
                sb.Append(minutes.ToString(CultureInfo.InvariantCulture));
                sb.Append('M');
            }
            if (seconds > 0 || milliseconds > 0)
            {
                AppendSeconds(sb, seconds, milliseconds);
            }
        }

        return sb.ToString();
    }

    private static void AppendSeconds(System.Text.StringBuilder sb, int seconds, int milliseconds)
    {
        if (milliseconds > 0)
        {
            double totalSeconds = seconds + (milliseconds / 1000.0);
            sb.Append(totalSeconds.ToString("G", CultureInfo.InvariantCulture));
        }
        else
        {
            sb.Append(seconds.ToString(CultureInfo.InvariantCulture));
        }
        sb.Append('S');
    }
}
