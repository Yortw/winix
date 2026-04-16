#nullable enable

using System.Globalization;
using System.Text;
using Yort.ShellKit;

namespace Winix.When;

/// <summary>
/// Top-level output composition. Produces complete output blocks for conversion mode,
/// diff mode, and JSON output.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Multi-line default output for conversion mode.
    /// Shows UTC, local (with timezone abbreviation), optional extra timezone,
    /// relative time, and Unix epoch.
    /// </summary>
    /// <param name="timestamp">The resolved timestamp.</param>
    /// <param name="localTz">The local (system) timezone.</param>
    /// <param name="extraTz">An additional timezone from <c>--tz</c>, or null.</param>
    /// <param name="now">The current time for relative formatting.</param>
    /// <param name="useColor">Whether to include ANSI colour escapes.</param>
    public static string FormatDefault(DateTimeOffset timestamp, TimeZoneInfo localTz,
        TimeZoneInfo? extraTz, DateTimeOffset now, bool useColor)
    {
        string dim = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);

        var sb = new StringBuilder();

        // UTC line
        string utcStr = timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        sb.AppendLine($"  {dim}UTC:{reset}       {utcStr}");

        // Local line
        DateTimeOffset localTime = TimeZoneInfo.ConvertTime(timestamp, localTz);
        string localAbbr = TimezoneResolver.GetAbbreviation(localTz, timestamp);
        string localOffsetStr = FormatOffset(localTz.GetUtcOffset(timestamp));
        string localStr = localTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        sb.AppendLine($"  {dim}Local:{reset}     {localStr} {localAbbr} ({localOffsetStr})");

        // Extra timezone line
        if (extraTz != null)
        {
            DateTimeOffset extraTime = TimeZoneInfo.ConvertTime(timestamp, extraTz);
            string extraAbbr = TimezoneResolver.GetAbbreviation(extraTz, timestamp);
            string extraOffsetStr = FormatOffset(extraTz.GetUtcOffset(timestamp));
            string extraStr = extraTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string label = TimezoneResolver.GetDisplayLabel(extraTz);
            string paddedLabel = (label + ":").PadRight(10);
            sb.AppendLine($"  {dim}{paddedLabel}{reset} {extraStr} {extraAbbr} ({extraOffsetStr})");
        }

        // Relative line
        string relative = RelativeFormatter.Format(timestamp, now);
        sb.AppendLine($"  {dim}Relative:{reset}  {relative}");

        // Unix line
        long unixSeconds = timestamp.ToUnixTimeSeconds();
        sb.Append($"  {dim}Unix:{reset}      {unixSeconds.ToString(CultureInfo.InvariantCulture)}");

        return sb.ToString();
    }

    /// <summary>
    /// Single UTC ISO 8601 string — pipe-friendly, no padding.
    /// </summary>
    public static string FormatUtc(DateTimeOffset timestamp)
    {
        return timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Single local (or <c>--tz</c>) ISO 8601 string — pipe-friendly, no padding.
    /// </summary>
    /// <param name="timestamp">The timestamp to format.</param>
    /// <param name="tz">The timezone to convert to.</param>
    public static string FormatLocal(DateTimeOffset timestamp, TimeZoneInfo tz)
    {
        DateTimeOffset converted = TimeZoneInfo.ConvertTime(timestamp, tz);
        if (converted.Offset == TimeSpan.Zero)
        {
            return converted.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }
        return converted.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a UTC offset as <c>+HH:MM</c> or <c>-HH:MM</c>.
    /// </summary>
    internal static string FormatOffset(TimeSpan offset)
    {
        string sign = offset < TimeSpan.Zero ? "-" : "+";
        TimeSpan abs = offset < TimeSpan.Zero ? offset.Negate() : offset;
        return $"{sign}{abs.Hours:D2}:{abs.Minutes:D2}";
    }
}
