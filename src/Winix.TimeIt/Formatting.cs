using System.Globalization;
using System.Text.Json;
using Yort.ShellKit;

namespace Winix.TimeIt;

/// <summary>
/// Formatting helpers for human-readable time and memory values.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Formats a <see cref="TimeItResult"/> as a multi-line human-readable summary.
    /// </summary>
    /// <param name="result">The timing result to format.</param>
    /// <param name="useColor">
    /// Whether to include ANSI colour escapes. The caller must resolve this from
    /// <c>NO_COLOR</c>, terminal detection, and <c>--color</c>/<c>--no-color</c> flags
    /// before calling — the formatter does not check the environment.
    /// </param>
    public static string FormatDefault(TimeItResult result, bool useColor)
    {
        string dim = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);
        string exitColor = result.ExitCode == 0
            ? AnsiColor.Green(useColor)
            : AnsiColor.Red(useColor);

        string userDisplay = result.UserCpuTime.HasValue ? DisplayFormat.FormatDuration(result.UserCpuTime.Value) : "N/A";
        string sysDisplay = result.SystemCpuTime.HasValue ? DisplayFormat.FormatDuration(result.SystemCpuTime.Value) : "N/A";
        string peakDisplay = result.PeakMemoryBytes.HasValue ? DisplayFormat.FormatBytes(result.PeakMemoryBytes.Value) : "N/A";

        return $"  {dim}real{reset}  {DisplayFormat.FormatDuration(result.WallTime)}\n  {dim}user{reset}  {userDisplay}\n  {dim}sys{reset}   {sysDisplay}\n  {dim}peak{reset}  {peakDisplay}\n  {dim}exit{reset}  {exitColor}{result.ExitCode}{reset}";
    }

    /// <summary>
    /// Formats a <see cref="TimeItResult"/> as a single compact line.
    /// </summary>
    /// <param name="result">The timing result to format.</param>
    /// <param name="useColor">
    /// Whether to include ANSI colour escapes. The caller must resolve this from
    /// <c>NO_COLOR</c>, terminal detection, and <c>--color</c>/<c>--no-color</c> flags
    /// before calling — the formatter does not check the environment.
    /// </param>
    public static string FormatOneLine(TimeItResult result, bool useColor)
    {
        string exitColor = result.ExitCode == 0
            ? AnsiColor.Green(useColor)
            : AnsiColor.Red(useColor);
        string reset = AnsiColor.Reset(useColor);

        string userDisplay = result.UserCpuTime.HasValue ? DisplayFormat.FormatDuration(result.UserCpuTime.Value) : "N/A";
        string sysDisplay = result.SystemCpuTime.HasValue ? DisplayFormat.FormatDuration(result.SystemCpuTime.Value) : "N/A";
        string peakDisplay = result.PeakMemoryBytes.HasValue ? DisplayFormat.FormatBytes(result.PeakMemoryBytes.Value) : "N/A";

        return $"[timeit] {DisplayFormat.FormatDuration(result.WallTime)} wall | {userDisplay} user | {sysDisplay} sys | {peakDisplay} peak | exit {exitColor}{result.ExitCode}{reset}";
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
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", 0);
            writer.WriteString("exit_reason", "success");
            writer.WriteNumber("child_exit_code", result.ExitCode);
            JsonHelper.WriteFixedDecimal(writer, "wall_seconds", result.WallTime.TotalSeconds, 3);

            if (result.UserCpuTime.HasValue)
            {
                JsonHelper.WriteFixedDecimal(writer, "user_cpu_seconds", result.UserCpuTime.Value.TotalSeconds, 3);
            }
            else
            {
                writer.WriteNull("user_cpu_seconds");
            }

            if (result.SystemCpuTime.HasValue)
            {
                JsonHelper.WriteFixedDecimal(writer, "sys_cpu_seconds", result.SystemCpuTime.Value.TotalSeconds, 3);
            }
            else
            {
                writer.WriteNull("sys_cpu_seconds");
            }

            if (result.TotalCpuTime.HasValue)
            {
                JsonHelper.WriteFixedDecimal(writer, "cpu_seconds", result.TotalCpuTime.Value.TotalSeconds, 3);
            }
            else
            {
                writer.WriteNull("cpu_seconds");
            }

            if (result.PeakMemoryBytes.HasValue)
            {
                writer.WriteNumber("peak_memory_bytes", result.PeakMemoryBytes.Value);
            }
            else
            {
                writer.WriteNull("peak_memory_bytes");
            }

            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
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
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            writer.WriteNull("child_exit_code");
            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }
}
