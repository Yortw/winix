using System.Globalization;
using System.Text;

namespace Winix.Squeeze;

/// <summary>
/// Formatting helpers for squeeze results — human-readable stats, JSON output, and byte/duration display.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Formats a byte count as a human-friendly auto-scaling string.
    /// Uses "B" for less than 1024, "X.X KB" for less than 1 MB, "X.X MB" for less than 1 GB,
    /// and "X.X GB" for larger values.
    /// </summary>
    public static string FormatBytes(long bytes)
    {
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
    /// Under 1s: "0.120s". 1-60s: "12.4s". Over 60s: "1m 27.1s".
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

        int minutes = (int)(totalSeconds / 60.0);
        double remainingSeconds = totalSeconds - (minutes * 60.0);
        return string.Format(CultureInfo.InvariantCulture, "{0}m {1:00.0}s", minutes, remainingSeconds);
    }

    /// <summary>
    /// Formats a single <see cref="SqueezeResult"/> as a one-line human-readable summary.
    /// Example: <c>filename: 1.0 MB → 512 KB (50.0%, gz, 0.120s)</c>.
    /// Applies green colour to the ratio when reduction exceeds 50%, and dims the filename.
    /// </summary>
    public static string FormatHuman(SqueezeResult result, bool useColor)
    {
        string dim = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);

        string filename = Path.GetFileName(result.InputPath);
        double ratioPercent = result.Ratio * 100.0;
        string ratioColor = ratioPercent > 50.0 ? AnsiColor.Green(useColor) : "";
        string ratioReset = ratioPercent > 50.0 ? reset : "";

        string shortName = CompressionFormatInfo.GetShortName(result.Format);

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}{1}{2}: {3} \u2192 {4} ({5}{6:F1}%{7}, {8}, {9})",
            dim,
            filename,
            reset,
            FormatBytes(result.InputBytes),
            FormatBytes(result.OutputBytes),
            ratioColor,
            ratioPercent,
            ratioReset,
            shortName,
            FormatDuration(result.Elapsed)
        );
    }

    /// <summary>
    /// Formats one or more <see cref="SqueezeResult"/>s as a JSON object following Winix CLI conventions.
    /// Includes tool/version/exit_code/exit_reason envelope and a files array.
    /// </summary>
    public static string FormatJson(
        IReadOnlyList<SqueezeResult> results,
        int exitCode,
        string exitReason,
        string toolName,
        string version)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.AppendFormat(
            CultureInfo.InvariantCulture,
            "\"tool\":\"{0}\",\"version\":\"{1}\",\"exit_code\":{2},\"exit_reason\":\"{3}\",\"files\":[",
            EscapeJsonString(toolName),
            EscapeJsonString(version),
            exitCode,
            EscapeJsonString(exitReason));

        for (int i = 0; i < results.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            SqueezeResult r = results[i];
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                "{{\"input\":\"{0}\",\"output\":\"{1}\",\"input_bytes\":{2},\"output_bytes\":{3},\"ratio\":{4:F3},\"format\":\"{5}\",\"seconds\":{6:F3}}}",
                EscapeJsonString(r.InputPath),
                EscapeJsonString(r.OutputPath),
                r.InputBytes,
                r.OutputBytes,
                r.Ratio,
                CompressionFormatInfo.GetShortName(r.Format),
                r.Elapsed.TotalSeconds);
        }

        sb.Append("]}");
        return sb.ToString();
    }

    /// <summary>
    /// Formats an error as a JSON object following Winix CLI conventions.
    /// Used when squeeze itself fails before processing any files.
    /// </summary>
    public static string FormatJsonError(int exitCode, string exitReason, string toolName, string version)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{{\"tool\":\"{0}\",\"version\":\"{1}\",\"exit_code\":{2},\"exit_reason\":\"{3}\"}}",
            EscapeJsonString(toolName),
            EscapeJsonString(version),
            exitCode,
            EscapeJsonString(exitReason));
    }

    /// <summary>
    /// Escapes backslashes and double-quotes for safe JSON string embedding.
    /// </summary>
    private static string EscapeJsonString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
