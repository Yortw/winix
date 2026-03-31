using System.Globalization;

namespace Winix.TreeX;

/// <summary>
/// Formats byte counts as human-readable sizes: plain bytes below 1024,
/// then K, M, G with one decimal place. Binary units (1K = 1024).
/// </summary>
public static class HumanSize
{
    private const long KB = 1024L;
    private const long MB = KB * 1024;
    private const long GB = MB * 1024;

    /// <summary>
    /// Formats a byte count as a human-readable string.
    /// Returns "-" for negative values (used to indicate size is unavailable,
    /// e.g. for directory entries on filesystems that don't report directory sizes).
    /// Bytes below 1024 are shown as plain integers with thousands separators.
    /// 1024 and above are shown with one decimal place and a K/M/G suffix.
    /// </summary>
    public static string Format(long bytes)
    {
        if (bytes < 0) { return "-"; }
        if (bytes < KB) { return bytes.ToString("N0", CultureInfo.InvariantCulture); }
        if (bytes < MB) { return string.Format(CultureInfo.InvariantCulture, "{0:F1}K", (double)bytes / KB); }
        if (bytes < GB) { return string.Format(CultureInfo.InvariantCulture, "{0:F1}M", (double)bytes / MB); }
        return string.Format(CultureInfo.InvariantCulture, "{0:F1}G", (double)bytes / GB);
    }

    /// <summary>
    /// Formats a byte count right-aligned within the given character width.
    /// See <see cref="Format"/> for formatting rules.
    /// </summary>
    public static string FormatPadded(long bytes, int width)
    {
        return Format(bytes).PadLeft(width);
    }
}
