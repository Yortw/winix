using System.Globalization;

namespace Winix.When;

/// <summary>
/// Formats the duration between a timestamp and a reference time as a human-friendly
/// relative string such as "3 hours ago" or "in 7 days".
/// </summary>
public static class RelativeFormatter
{
    /// <summary>
    /// Formats <paramref name="timestamp"/> relative to <paramref name="now"/> as a
    /// natural-language string. Differences under 60 seconds return "just now".
    /// Larger differences are expressed in minutes, hours, days, months, or years,
    /// using singular/plural forms as appropriate.
    /// </summary>
    /// <param name="timestamp">The point in time to describe.</param>
    /// <param name="now">The reference point treated as "now".</param>
    /// <returns>A relative time string such as "3 hours ago" or "in 7 days".</returns>
    public static string Format(DateTimeOffset timestamp, DateTimeOffset now)
    {
        TimeSpan diff = timestamp - now;
        bool isFuture = diff > TimeSpan.Zero;
        double totalSeconds = Math.Abs(diff.TotalSeconds);
        double totalMinutes = Math.Abs(diff.TotalMinutes);
        double totalHours = Math.Abs(diff.TotalHours);
        double totalDays = Math.Abs(diff.TotalDays);

        if (totalSeconds < 60)
        {
            return "just now";
        }

        if (totalMinutes < 60)
        {
            int minutes = (int)totalMinutes;
            return FormatRelative(minutes, minutes == 1 ? "minute" : "minutes", isFuture);
        }

        if (totalHours < 24)
        {
            int hours = (int)totalHours;
            return FormatRelative(hours, hours == 1 ? "hour" : "hours", isFuture);
        }

        if (totalDays < 30)
        {
            int days = (int)totalDays;
            return FormatRelative(days, days == 1 ? "day" : "days", isFuture);
        }

        if (totalDays < 365)
        {
            int months = (int)(totalDays / 30);
            return FormatRelative(months, months == 1 ? "month" : "months", isFuture);
        }

        int years = (int)(totalDays / 365);
        return FormatRelative(years, years == 1 ? "year" : "years", isFuture);
    }

    private static string FormatRelative(int value, string unit, bool isFuture)
    {
        string v = value.ToString(CultureInfo.InvariantCulture);
        return isFuture ? $"in {v} {unit}" : $"{v} {unit} ago";
    }
}
