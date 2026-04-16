#nullable enable

using System.Globalization;
using System.Text;
using System.Text.Json;
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
    /// Multi-line default output for diff mode.
    /// Duration is always displayed as absolute (positive). From/To lines are shown only
    /// when <paramref name="displayTz"/> is specified, and are ordered earliest-first.
    /// </summary>
    /// <param name="duration">The raw duration (may be negative if time1 &gt; time2).</param>
    /// <param name="from">The first timestamp (time1).</param>
    /// <param name="to">The second timestamp (time2).</param>
    /// <param name="displayTz">Optional timezone for From/To display lines.</param>
    /// <param name="useColor">Whether to include ANSI colour escapes.</param>
    public static string FormatDiff(TimeSpan duration, DateTimeOffset from, DateTimeOffset to,
        TimeZoneInfo? displayTz, bool useColor)
    {
        string dim = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);

        var sb = new StringBuilder();

        // For human output, reorder so "from" is the earlier timestamp
        DateTimeOffset displayFrom = from < to ? from : to;
        DateTimeOffset displayTo = from < to ? to : from;
        TimeSpan absDuration = duration < TimeSpan.Zero ? duration.Negate() : duration;

        // From/To lines only shown when --tz is specified
        if (displayTz != null)
        {
            string fromStr = FormatWithTimezone(displayFrom, displayTz);
            string toStr = FormatWithTimezone(displayTo, displayTz);
            sb.AppendLine($"  {dim}From:{reset}      {fromStr}");
            sb.AppendLine($"  {dim}To:{reset}        {toStr}");
        }

        // Duration line (human-friendly, absolute)
        string humanDuration = DurationFormatter.FormatHuman(absDuration);
        sb.AppendLine($"  {dim}Duration:{reset}  {humanDuration}");

        // ISO 8601 line (absolute for human display)
        string isoDuration = IsoDurationParser.Format(absDuration);
        sb.AppendLine($"  {dim}ISO 8601:{reset}  {isoDuration}");

        // Seconds line (absolute for human display)
        long totalSeconds = (long)absDuration.TotalSeconds;
        sb.Append($"  {dim}Seconds:{reset}   {totalSeconds.ToString(CultureInfo.InvariantCulture)}");

        return sb.ToString();
    }

    /// <summary>
    /// Single ISO 8601 duration string for <c>--iso</c> flag.
    /// Uses signed duration (negative when time1 &gt; time2).
    /// </summary>
    public static string FormatDiffIso(TimeSpan duration)
    {
        return IsoDurationParser.Format(duration);
    }

    /// <summary>
    /// JSON output for conversion mode. Follows the standard Winix JSON envelope.
    /// </summary>
    /// <param name="timestamp">The resolved timestamp.</param>
    /// <param name="localTz">The local (system) timezone.</param>
    /// <param name="extraTz">An additional timezone from <c>--tz</c>, or null.</param>
    /// <param name="now">The current time for relative formatting.</param>
    /// <param name="inputStr">The raw input string provided by the user.</param>
    /// <param name="offsetStr">The raw offset string from <c>--offset</c>, or null.</param>
    /// <param name="toolName">The tool's executable name.</param>
    /// <param name="version">The tool's version string.</param>
    public static string FormatJson(DateTimeOffset timestamp, TimeZoneInfo localTz,
        TimeZoneInfo? extraTz, DateTimeOffset now, string? inputStr, string? offsetStr,
        string toolName, string version)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", 0);
            writer.WriteString("exit_reason", "success");
            writer.WriteString("input", inputStr);
            if (offsetStr != null)
            {
                writer.WriteString("offset", offsetStr);
            }
            else
            {
                writer.WriteNull("offset");
            }

            string utcStr = timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            writer.WriteString("utc", utcStr);

            DateTimeOffset localTime = TimeZoneInfo.ConvertTime(timestamp, localTz);
            string localStr;
            if (localTime.Offset == TimeSpan.Zero)
            {
                localStr = localTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            }
            else
            {
                localStr = localTime.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
            }
            writer.WriteString("local", localStr);

            string localAbbr = TimezoneResolver.GetAbbreviation(localTz, timestamp);
            writer.WriteString("local_timezone", localAbbr);

            writer.WriteNumber("unix_seconds", timestamp.ToUnixTimeSeconds());
            writer.WriteNumber("unix_milliseconds", timestamp.ToUnixTimeMilliseconds());

            string relative = RelativeFormatter.Format(timestamp, now);
            writer.WriteString("relative", relative);

            if (extraTz != null)
            {
                string extraAbbr = TimezoneResolver.GetAbbreviation(extraTz, timestamp);
                writer.WriteString("target_timezone", extraAbbr);
                DateTimeOffset extraTime = TimeZoneInfo.ConvertTime(timestamp, extraTz);
                string targetStr;
                if (extraTime.Offset == TimeSpan.Zero)
                {
                    targetStr = extraTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                }
                else
                {
                    targetStr = extraTime.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
                }
                writer.WriteString("target", targetStr);
            }

            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// JSON output for diff mode. Values are signed (negative when time1 &gt; time2).
    /// From/to preserve argument order (not reordered).
    /// </summary>
    /// <param name="duration">The raw duration (signed).</param>
    /// <param name="from">The first timestamp (time1), as provided by the user.</param>
    /// <param name="to">The second timestamp (time2), as provided by the user.</param>
    /// <param name="toolName">The tool's executable name.</param>
    /// <param name="version">The tool's version string.</param>
    public static string FormatDiffJson(TimeSpan duration, DateTimeOffset from, DateTimeOffset to,
        string toolName, string version)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", 0);
            writer.WriteString("exit_reason", "success");

            string fromStr = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            writer.WriteString("from", fromStr);

            string toStr = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            writer.WriteString("to", toStr);

            string isoStr = IsoDurationParser.Format(duration);
            writer.WriteString("duration_iso", isoStr);

            long totalSeconds = (long)duration.TotalSeconds;
            writer.WriteNumber("total_seconds", totalSeconds);

            // Signed component breakdown
            bool negative = duration < TimeSpan.Zero;
            TimeSpan absDuration = negative ? duration.Negate() : duration;
            int sign = negative ? -1 : 1;

            writer.WriteNumber("days", absDuration.Days * sign);
            writer.WriteNumber("hours", absDuration.Hours);
            writer.WriteNumber("minutes", absDuration.Minutes);
            writer.WriteNumber("seconds", absDuration.Seconds);

            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// JSON error output with standard Winix envelope.
    /// </summary>
    /// <param name="exitCode">The tool's exit code (125, 126, or 127 per POSIX convention).</param>
    /// <param name="exitReason">Machine-readable snake_case reason (e.g. "parse_error").</param>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="toolName">The tool's executable name.</param>
    /// <param name="version">The tool's version string.</param>
    public static string FormatJsonError(int exitCode, string exitReason, string message,
        string toolName, string version)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            writer.WriteString("message", message);
            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }

    private static string FormatWithTimezone(DateTimeOffset timestamp, TimeZoneInfo tz)
    {
        DateTimeOffset converted = TimeZoneInfo.ConvertTime(timestamp, tz);
        string abbr = TimezoneResolver.GetAbbreviation(tz, timestamp);
        string offsetStr = FormatOffset(tz.GetUtcOffset(timestamp));
        return $"{converted.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {abbr} ({offsetStr})";
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
