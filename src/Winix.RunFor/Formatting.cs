using System;
using Yort.ShellKit;

namespace Winix.RunFor;

/// <summary>JSON envelope and human stderr notice for runfor.</summary>
public static class Formatting
{
    /// <summary>
    /// The <c>--json</c> envelope: standard fields (tool/version/exit_code) then runfor-specific
    /// (outcome/timed_out/child_exit_code/signal/kill_failed/duration_ms).
    /// </summary>
    /// <param name="result">The completed runfor result.</param>
    /// <param name="toolName">The tool's executable name (e.g. "runfor").</param>
    /// <param name="version">The tool's version string from assembly metadata.</param>
    /// <param name="signalName">The signal name used to terminate the child (e.g. "TERM", "KILL").</param>
    public static string FormatJson(RunForResult result, string toolName, string version, string signalName)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", result.ExitCode);
            writer.WriteString("outcome", OutcomeToString(result.Outcome));
            writer.WriteBoolean("timed_out", result.Outcome == RunForOutcome.TimedOut);
            if (result.ChildExitCode.HasValue)
            {
                writer.WriteNumber("child_exit_code", result.ChildExitCode.Value);
            }
            else
            {
                writer.WriteNull("child_exit_code");
            }
            writer.WriteString("signal", signalName);
            writer.WriteBoolean("kill_failed", result.KillFailed);
            writer.WriteNumber("duration_ms", (long)result.Duration.TotalMilliseconds);
            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// A terse one-line stderr notice for non-JSON mode (timeout/interrupt only; returns
    /// <see cref="string.Empty"/> on clean completion or launch failure — those are handled by
    /// the caller via exit code and optional error text).
    /// </summary>
    /// <param name="result">The completed runfor result.</param>
    /// <param name="command">The child executable name, for context in the message.</param>
    /// <param name="deadline">The configured deadline, used to format the timeout duration.</param>
    /// <param name="useColor">Whether to include ANSI colour escapes.</param>
    public static string FormatNotice(RunForResult result, string command, TimeSpan deadline, bool useColor)
    {
        string yellow = AnsiColor.Yellow(useColor);
        string reset = AnsiColor.Reset(useColor);
        string warn = result.KillFailed
            ? $" {AnsiColor.Red(useColor)}(could not terminate child — it may still be running){reset}"
            : string.Empty;

        return result.Outcome switch
        {
            RunForOutcome.TimedOut =>
                $"runfor: {yellow}timed out{reset} after {DisplayFormat.FormatDuration(deadline)}: {command}{warn}",
            RunForOutcome.Interrupted =>
                $"runfor: {yellow}interrupted{reset}: {command}{warn}",
            // Completed and LaunchFailed produce no notice; the caller handles those via exit code.
            _ => string.Empty,
        };
    }

    private static string OutcomeToString(RunForOutcome outcome) => outcome switch
    {
        RunForOutcome.Completed => "completed",
        RunForOutcome.TimedOut => "timed_out",
        RunForOutcome.Interrupted => "interrupted",
        RunForOutcome.LaunchFailed => "launch_failed",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "unhandled runfor outcome"),
    };
}
