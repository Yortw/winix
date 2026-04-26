using System.Text.Json;
using System.Text.RegularExpressions;
using Yort.ShellKit;

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
    /// <param name="historyRetained">Number of snapshots retained in history at session end, or null to omit.</param>
    public static string FormatJson(
        int exitCode,
        string exitReason,
        int runs,
        int? lastChildExitCode,
        double durationSeconds,
        string command,
        string? lastOutput,
        string toolName,
        string version,
        int? historyRetained = null)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            writer.WriteNumber("runs", runs);

            if (lastChildExitCode.HasValue)
            {
                writer.WriteNumber("last_child_exit_code", lastChildExitCode.Value);
            }
            else
            {
                writer.WriteNull("last_child_exit_code");
            }

            JsonHelper.WriteFixedDecimal(writer, "duration_seconds", durationSeconds, 3);
            writer.WriteString("command", command);

            if (lastOutput is not null)
            {
                writer.WriteString("last_output", StripAnsi(lastOutput));
            }

            if (historyRetained.HasValue)
            {
                writer.WriteNumber("history_retained", historyRetained.Value);
            }

            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
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
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
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
    /// Two alternatives:
    /// (a) CSI: <c>ESC [ ...params... letter</c> — colours, cursor moves, SGR.
    /// (b) OSC: <c>ESC ] ...payload... ST</c> — window titles (<c>\x1b]0;title\x07</c>),
    ///     OSC-8 hyperlinks (<c>\x1b]8;;url\x1b\\</c>) used by gcc, ripgrep, modern shells.
    /// String terminator (ST) is either <c>\x07</c> (BEL, common short form) or
    /// <c>\x1b\\</c> (ESC + backslash, the spec form).
    /// Without the OSC alternative, <c>--exit-on-match</c> patterns fail to match
    /// lines carrying a leading title-set escape, and <c>--json-output last_output</c>
    /// leaks raw escape bytes into the JSON envelope.
    /// </summary>
    [GeneratedRegex(@"\x1b\[[0-9;?]*[a-zA-Z]|\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)")]
    private static partial Regex AnsiEscapeRegex();

}
