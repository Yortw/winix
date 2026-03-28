using System.Globalization;

namespace Winix.TimeIt;

/// <summary>
/// Formatting helpers for human-readable time and memory values.
/// </summary>
public static class Formatting
{
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

    /// <summary>
    /// Formats a byte count as a human-friendly auto-scaling string (KB, MB, or GB).
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = 1024 * KB;
        const long GB = 1024 * MB;

        if (bytes < MB)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} KB", bytes / KB);
        }

        if (bytes < GB)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} MB", bytes / MB);
        }

        double gb = (double)bytes / GB;
        return string.Format(CultureInfo.InvariantCulture, "{0:F1} GB", gb);
    }

    /// <summary>
    /// Formats a <see cref="TimeItResult"/> as a multi-line human-readable summary.
    /// </summary>
    public static string FormatDefault(TimeItResult result, bool useColor)
    {
        string dim = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);
        string exitColor = result.ExitCode == 0
            ? AnsiColor.Green(useColor)
            : AnsiColor.Red(useColor);

        string userDisplay = result.UserCpuTime.HasValue ? FormatDuration(result.UserCpuTime.Value) : "N/A";
        string sysDisplay = result.SystemCpuTime.HasValue ? FormatDuration(result.SystemCpuTime.Value) : "N/A";
        string peakDisplay = result.PeakMemoryBytes.HasValue ? FormatBytes(result.PeakMemoryBytes.Value) : "N/A";

        return $"  {dim}real{reset}  {FormatDuration(result.WallTime)}\n  {dim}user{reset}  {userDisplay}\n  {dim}sys{reset}   {sysDisplay}\n  {dim}peak{reset}  {peakDisplay}\n  {dim}exit{reset}  {exitColor}{result.ExitCode}{reset}";
    }

    /// <summary>
    /// Formats a <see cref="TimeItResult"/> as a single compact line.
    /// </summary>
    public static string FormatOneLine(TimeItResult result, bool useColor)
    {
        string exitColor = result.ExitCode == 0
            ? AnsiColor.Green(useColor)
            : AnsiColor.Red(useColor);
        string reset = AnsiColor.Reset(useColor);

        string userDisplay = result.UserCpuTime.HasValue ? FormatDuration(result.UserCpuTime.Value) : "N/A";
        string sysDisplay = result.SystemCpuTime.HasValue ? FormatDuration(result.SystemCpuTime.Value) : "N/A";
        string peakDisplay = result.PeakMemoryBytes.HasValue ? FormatBytes(result.PeakMemoryBytes.Value) : "N/A";

        return $"[timeit] {FormatDuration(result.WallTime)} wall | {userDisplay} user | {sysDisplay} sys | {peakDisplay} peak | exit {exitColor}{result.ExitCode}{reset}";
    }

    /// <summary>
    /// Formats a <see cref="TimeItResult"/> as a JSON object. No colour, machine-parseable.
    /// Follows Winix CLI conventions: standard fields (tool, version, exit_code, exit_reason,
    /// child_exit_code) followed by tool-specific metrics.
    /// </summary>
    /// <param name="result">The timing result to format.</param>
    /// <param name="toolName">The tool's executable name (e.g. "timeit").</param>
    /// <param name="version">The tool's version string from assembly metadata.</param>
    public static string FormatJson(TimeItResult result, string toolName, string version)
    {
        string userJson = result.UserCpuTime.HasValue
            ? result.UserCpuTime.Value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)
            : "null";
        string sysJson = result.SystemCpuTime.HasValue
            ? result.SystemCpuTime.Value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)
            : "null";
        string cpuJson = result.TotalCpuTime.HasValue
            ? result.TotalCpuTime.Value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)
            : "null";
        string peakJson = result.PeakMemoryBytes.HasValue
            ? result.PeakMemoryBytes.Value.ToString(CultureInfo.InvariantCulture)
            : "null";

        return string.Format(
            CultureInfo.InvariantCulture,
            "{{\"tool\":\"{0}\",\"version\":\"{1}\",\"exit_code\":0,\"exit_reason\":\"success\",\"child_exit_code\":{2},\"wall_seconds\":{3:F3},\"user_cpu_seconds\":{4},\"sys_cpu_seconds\":{5},\"cpu_seconds\":{6},\"peak_memory_bytes\":{7}}}",
            toolName,
            version,
            result.ExitCode,
            result.WallTime.TotalSeconds,
            userJson,
            sysJson,
            cpuJson,
            peakJson
        );
    }

    /// <summary>
    /// Formats an error as a JSON object following Winix CLI conventions.
    /// Used when timeit itself fails (command not found, not executable, usage error).
    /// </summary>
    /// <param name="exitCode">The tool's exit code (125, 126, or 127).</param>
    /// <param name="exitReason">Machine-readable snake_case reason.</param>
    /// <param name="toolName">The tool's executable name.</param>
    /// <param name="version">The tool's version string.</param>
    public static string FormatJsonError(int exitCode, string exitReason, string toolName, string version)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{{\"tool\":\"{0}\",\"version\":\"{1}\",\"exit_code\":{2},\"exit_reason\":\"{3}\",\"child_exit_code\":null}}",
            toolName,
            version,
            exitCode,
            exitReason
        );
    }
}
