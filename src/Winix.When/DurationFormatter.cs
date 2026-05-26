using System.Globalization;

namespace Winix.When;

/// <summary>
/// Formats a <see cref="TimeSpan"/> as human-friendly text or ISO 8601 duration.
/// </summary>
public static class DurationFormatter
{
    /// <summary>
    /// Formats a duration as a human-friendly comma-separated string.
    /// Milliseconds shown only when the total duration is sub-second.
    /// Negative durations use the absolute value.
    /// </summary>
    public static string FormatHuman(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = duration.Negate();
        }

        int days = duration.Days;
        int hours = duration.Hours;
        int minutes = duration.Minutes;
        int seconds = duration.Seconds;
        int milliseconds = duration.Milliseconds;

        var parts = new List<string>();

        if (days > 0) { parts.Add($"{days.ToString(CultureInfo.InvariantCulture)} {Pluralize("day", days)}"); }
        if (hours > 0) { parts.Add($"{hours.ToString(CultureInfo.InvariantCulture)} {Pluralize("hour", hours)}"); }
        if (minutes > 0) { parts.Add($"{minutes.ToString(CultureInfo.InvariantCulture)} {Pluralize("minute", minutes)}"); }
        if (seconds > 0) { parts.Add($"{seconds.ToString(CultureInfo.InvariantCulture)} {Pluralize("second", seconds)}"); }

        // Milliseconds only when no larger components present
        if (parts.Count == 0 && milliseconds > 0)
        {
            parts.Add($"{milliseconds.ToString(CultureInfo.InvariantCulture)} {Pluralize("millisecond", milliseconds)}");
        }

        if (parts.Count == 0) { return "0 seconds"; }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Formats a duration as ISO 8601 (PnDTnHnMnS). Delegates to <see cref="IsoDurationParser.Format"/>.
    /// </summary>
    public static string FormatIso(TimeSpan duration)
    {
        return IsoDurationParser.Format(duration);
    }

    private static string Pluralize(string singular, int count)
    {
        return count == 1 ? singular : singular + "s";
    }
}
