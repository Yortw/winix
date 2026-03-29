using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Winix.Peep;

/// <summary>
/// JSON output formatting per Winix CLI conventions, plus ANSI escape sequence stripping
/// for clean machine-parseable output.
/// </summary>
public static partial class Formatting
{
    /// <summary>
    /// Formats a peep session summary as a JSON string following Winix CLI conventions.
    /// Includes standard fields (tool, version, exit_code, exit_reason) plus peep-specific
    /// session metrics.
    /// </summary>
    /// <param name="exitCode">The tool's exit code (0, 125, 126, or 127).</param>
    /// <param name="exitReason">Machine-readable snake_case reason (e.g. "manual", "exit_on_success").</param>
    /// <param name="runs">Total number of command executions during the session.</param>
    /// <param name="lastChildExitCode">Exit code of the final child run, or null if no run occurred.</param>
    /// <param name="durationSeconds">Total wall time of the peep session in seconds.</param>
    /// <param name="command">The watched command (joined string).</param>
    /// <param name="lastOutput">Captured output from the final run (ANSI-stripped), or null to omit.</param>
    /// <param name="toolName">The tool's executable name ("peep").</param>
    /// <param name="version">The tool's version string from assembly metadata.</param>
    public static string FormatJson(
        int exitCode,
        string exitReason,
        int runs,
        int? lastChildExitCode,
        double durationSeconds,
        string command,
        string? lastOutput,
        string toolName,
        string version)
    {
        var sb = new StringBuilder();
        sb.Append('{');

        sb.AppendFormat(
            CultureInfo.InvariantCulture,
            "\"tool\":\"{0}\",\"version\":\"{1}\",\"exit_code\":{2},\"exit_reason\":\"{3}\"",
            EscapeJsonString(toolName),
            EscapeJsonString(version),
            exitCode,
            EscapeJsonString(exitReason));

        sb.AppendFormat(CultureInfo.InvariantCulture, ",\"runs\":{0}", runs);

        if (lastChildExitCode.HasValue)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture,
                ",\"last_child_exit_code\":{0}", lastChildExitCode.Value);
        }
        else
        {
            sb.Append(",\"last_child_exit_code\":null");
        }

        sb.AppendFormat(CultureInfo.InvariantCulture,
            ",\"duration_seconds\":{0:F3}", durationSeconds);

        sb.AppendFormat(CultureInfo.InvariantCulture,
            ",\"command\":\"{0}\"", EscapeJsonString(command));

        if (lastOutput is not null)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture,
                ",\"last_output\":\"{0}\"", EscapeJsonString(StripAnsi(lastOutput)));
        }

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Formats an error as a JSON string following Winix CLI conventions.
    /// Used when peep itself fails before entering the main loop (command not found, usage error).
    /// </summary>
    /// <param name="exitCode">The tool's exit code (125, 126, or 127).</param>
    /// <param name="exitReason">Machine-readable snake_case reason.</param>
    /// <param name="toolName">The tool's executable name.</param>
    /// <param name="version">The tool's version string.</param>
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
    /// Removes ANSI escape sequences from text for clean machine-parseable output.
    /// Handles standard SGR (Select Graphic Rendition) sequences used for colours,
    /// bold, dim, etc.
    /// </summary>
    /// <param name="input">Text potentially containing ANSI escape sequences.</param>
    /// <returns>The text with all ANSI escape sequences removed.</returns>
    public static string StripAnsi(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return AnsiEscapeRegex().Replace(input, "");
    }

    /// <summary>
    /// Source-generated regex for matching ANSI escape sequences.
    /// Matches ESC[ followed by any number of parameter bytes (digits, semicolons)
    /// and a final letter.
    /// </summary>
    [GeneratedRegex(@"\x1b\[[0-9;]*[a-zA-Z]")]
    private static partial Regex AnsiEscapeRegex();

    /// <summary>
    /// Escapes backslashes, double-quotes, and control characters for safe JSON string embedding.
    /// </summary>
    private static string EscapeJsonString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
