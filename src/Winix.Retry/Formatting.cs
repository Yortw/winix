using System.Globalization;
using Yort.ShellKit;

namespace Winix.Retry;

/// <summary>
/// Formatting helpers for retry progress lines and JSON output.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Formats a progress line for a single attempt.
    /// </summary>
    /// <param name="info">The attempt info.</param>
    /// <param name="useColor">Whether to include ANSI colour escapes.</param>
    public static string FormatAttempt(AttemptInfo info, bool useColor)
    {
        string dim = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);
        string red = AnsiColor.Red(useColor);
        string green = AnsiColor.Green(useColor);
        string yellow = AnsiColor.Yellow(useColor);

        string prefix = $"retry: {dim}attempt {info.Attempt}/{info.MaxAttempts}{reset}";

        if (info.WillRetry)
        {
            string delayStr = FormatDelay(info.NextDelay!.Value);
            return $"{prefix} {red}failed{reset} (exit {info.ExitCode}), retrying in {yellow}{delayStr}{reset}...";
        }

        if (info.StopReason == RetryOutcome.Succeeded)
        {
            if (info.ExitCode == 0)
            {
                return $"{prefix} {green}succeeded{reset} (exit 0) after {info.Attempt} attempts";
            }

            // Non-zero exit code with Succeeded outcome = --until target was matched.
            return $"{prefix} {green}matched target{reset} (exit {info.ExitCode}) after {info.Attempt} attempts";
        }

        if (info.StopReason == RetryOutcome.NotRetryable)
        {
            return $"{prefix} {red}failed{reset} (exit {info.ExitCode}), not retryable — stopping";
        }

        // RetriesExhausted (or any unrecognised stop reason)
        return $"{prefix} {red}failed{reset} (exit {info.ExitCode}), no retries remaining";
    }

    /// <summary>
    /// Formats a delay duration as a human-friendly string (e.g. "2s", "500ms", "1m 30s").
    /// Sub-second delays use milliseconds (e.g. "500ms") to avoid confusing "0.500s" output.
    /// </summary>
    internal static string FormatDelay(TimeSpan delay)
    {
        if (delay.TotalMilliseconds < 1000)
        {
            return $"{(int)delay.TotalMilliseconds}ms";
        }

        return DisplayFormat.FormatDuration(delay);
    }

    /// <summary>
    /// Formats the final JSON summary after all retry attempts complete.
    /// Follows Winix CLI JSON conventions: standard envelope fields (tool, version,
    /// exit_code, exit_reason, child_exit_code) followed by tool-specific metrics.
    /// </summary>
    /// <param name="result">The completed retry result.</param>
    /// <param name="toolName">The tool's executable name (e.g. "retry").</param>
    /// <param name="version">The tool's version string from assembly metadata.</param>
    public static string FormatJson(RetryResult result, string toolName, string version)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", result.ChildExitCode);
            writer.WriteString("exit_reason", OutcomeToReason(result.Outcome));
            writer.WriteNumber("child_exit_code", result.ChildExitCode);
            writer.WriteNumber("attempts", result.Attempts);
            writer.WriteNumber("max_attempts", result.MaxAttempts);
            JsonHelper.WriteFixedDecimal(writer, "total_seconds", result.TotalTime.TotalSeconds, 3);

            writer.WriteStartArray("delays_seconds");
            foreach (var delay in result.Delays)
            {
                writer.WriteRawValue(delay.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture));
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Formats a JSON error for retry's own failures (command not found, usage error, etc.).
    /// Uses the standard Winix envelope with null child_exit_code and zero attempts,
    /// since the child process was never started (or its result is irrelevant to the error).
    /// </summary>
    /// <param name="exitCode">The tool's exit code (125, 126, or 127 per POSIX convention).</param>
    /// <param name="exitReason">Machine-readable snake_case reason (e.g. "command_not_found").</param>
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
            writer.WriteNumber("attempts", 0);
            writer.WriteNumber("max_attempts", 0);
            JsonHelper.WriteFixedDecimal(writer, "total_seconds", 0.0, 3);
            writer.WriteStartArray("delays_seconds");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }

    private static string OutcomeToReason(RetryOutcome outcome)
    {
        if (outcome == RetryOutcome.Succeeded)
        {
            return "succeeded";
        }

        if (outcome == RetryOutcome.RetriesExhausted)
        {
            return "retries_exhausted";
        }

        if (outcome == RetryOutcome.NotRetryable)
        {
            return "not_retryable";
        }

        return "unknown";
    }
}
