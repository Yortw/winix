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

        if (info.StopReason == RetryOutcome.LaunchFailed)
        {
            // Launch-failure mid-loop (e.g. binary removed between attempts). ExitCode here is the
            // classified code Program.cs will return (127/126), not a real child exit — the child
            // never ran. Wording mirrors NotRetryable so the user knows the loop terminated.
            return $"{prefix} {red}launch failed{reset}, stopping";
        }

        if (info.StopReason == RetryOutcome.Cancelled)
        {
            // Ctrl+C during an attempt. Matters that this isn't the "no retries remaining"
            // fallthrough — a user who just pressed Ctrl+C should see "cancelled", not a
            // retries-exhausted message. Round-4 I1 fix.
            return $"{prefix} {yellow}cancelled{reset} (exit {info.ExitCode}), stopping";
        }

        if (info.StopReason == RetryOutcome.RetriesExhausted)
        {
            return $"{prefix} {red}failed{reset} (exit {info.ExitCode}), no retries remaining";
        }

        // Unreachable in practice — every `!willRetry` path above assigns a stopReason. Throw so a
        // future enum addition that forgets to extend this switch fails loudly in testing rather
        // than silently emitting a "failed, no retries remaining" line for an unrelated outcome.
        throw new ArgumentOutOfRangeException(
            nameof(info), info.StopReason, "unhandled stop reason in FormatAttempt");
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
            // On LaunchFailed the child never ran, so child_exit_code is null (not 127/126 —
            // those are retry's own classification, already carried by exit_code). Emitting
            // the classification code as child_exit_code would mislead consumers into
            // thinking the child exited with that value.
            if (result.Outcome == RetryOutcome.LaunchFailed)
            {
                writer.WriteNull("child_exit_code");
            }
            else
            {
                writer.WriteNumber("child_exit_code", result.ChildExitCode);
            }
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
    /// Maps a <see cref="RetryOutcome"/> value to its JSON <c>exit_reason</c> string. Throws on
    /// unknown variants so a future enum addition that forgets to extend this switch fails loudly
    /// rather than silently emitting "unknown" — which downstream parsers cannot distinguish from
    /// "retry has a bug" vs "retry has been updated and this is a new outcome I should handle".
    /// </summary>
    internal static string OutcomeToReason(RetryOutcome outcome) => outcome switch
    {
        RetryOutcome.Succeeded => "succeeded",
        RetryOutcome.RetriesExhausted => "retries_exhausted",
        RetryOutcome.NotRetryable => "not_retryable",
        RetryOutcome.LaunchFailed => "launch_failed",
        RetryOutcome.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "unhandled retry outcome")
    };
}
