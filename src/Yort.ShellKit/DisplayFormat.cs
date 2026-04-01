using System.Globalization;

namespace Yort.ShellKit;

/// <summary>
/// Human-friendly formatting for byte counts and durations.
/// </summary>
public static class DisplayFormat
{
    /// <summary>
    /// Formats a byte count as a human-friendly auto-scaling string.
    /// 0-1023: "500 B". 1 KB to 1 MB: "384.0 KB". 1 MB to 1 GB: "1.5 MB". 1 GB+: "2.3 GB".
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), bytes, "Byte count cannot be negative.");
        }

        const long KB = 1024;
        const long MB = 1024 * KB;
        const long GB = 1024 * MB;

        if (bytes < KB)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} B", bytes);
        }

        if (bytes < MB)
        {
            double kb = (double)bytes / KB;
            return string.Format(CultureInfo.InvariantCulture, "{0:F1} KB", kb);
        }

        if (bytes < GB)
        {
            double mb = (double)bytes / MB;
            return string.Format(CultureInfo.InvariantCulture, "{0:F1} MB", mb);
        }

        double gb = (double)bytes / GB;
        return string.Format(CultureInfo.InvariantCulture, "{0:F1} GB", gb);
    }

    /// <summary>
    /// Formats a duration as a human-friendly auto-scaling string.
    /// Under 1s: "0.842s". 1-60s: "12.4s". 1-60m: "3m 27.1s". Over 60m: "1h 12m 03s".
    /// </summary>
    public static string FormatDuration(TimeSpan duration)
    {
        double totalSeconds = duration.TotalSeconds;

        if (totalSeconds < 1.0)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F3}s", totalSeconds);
        }

        if (totalSeconds < 60.0)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F1}s", totalSeconds);
        }

        if (totalSeconds < 3600.0)
        {
            int minutes = (int)(totalSeconds / 60.0);
            double remainingSeconds = totalSeconds - (minutes * 60.0);
            return string.Format(CultureInfo.InvariantCulture, "{0}m {1:00.0}s", minutes, remainingSeconds);
        }

        {
            int hours = (int)(totalSeconds / 3600.0);
            int minutes = (int)((totalSeconds - (hours * 3600.0)) / 60.0);
            int secs = (int)(totalSeconds - (hours * 3600.0) - (minutes * 60.0));
            return string.Format(CultureInfo.InvariantCulture, "{0}h {1:00}m {2:00}s", hours, minutes, secs);
        }
    }
}
