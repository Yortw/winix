using System.Text.Json;
using Yort.ShellKit;

namespace Winix.Wargs;

/// <summary>
/// Output formatting for wargs — human-readable summaries, JSON, and NDJSON.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Replaces lone UTF-16 surrogate halves with the Unicode replacement character (U+FFFD)
    /// so the result is a well-formed UTF-16 string that <see cref="Utf8JsonWriter"/> can
    /// encode without throwing. Round-9 SFH M2 / TA I1: a single SourceItem or FaultMessage
    /// containing a lone high or low surrogate (e.g. malformed-path text from a
    /// Win32Exception) made WriteString throw, escaping the per-job NDJSON loop and silently
    /// dropping every subsequent job's row from the stream — breaking the documented
    /// "one line per job actually run" contract. Sanitising at the formatter is preferable
    /// to per-call-site try/catch because well-formed JSON output is a formatter contract,
    /// not a caller responsibility.
    /// </summary>
    private static string SanitiseForJson(string value)
    {
        // Fast path: most inputs are valid. Scan first; allocate only when needed.
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                {
                    return SanitiseSlow(value);
                }
                i++; // skip valid pair
            }
            else if (char.IsLowSurrogate(c))
            {
                // Lone low surrogate — preceding char wasn't a high surrogate.
                return SanitiseSlow(value);
            }
        }
        return value;
    }

    private static string SanitiseSlow(string value)
    {
        var buffer = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    buffer.Append(c);
                    buffer.Append(value[i + 1]);
                    i++;
                }
                else
                {
                    buffer.Append('�');
                }
            }
            else if (char.IsLowSurrogate(c))
            {
                buffer.Append('�');
            }
            else
            {
                buffer.Append(c);
            }
        }
        return buffer.ToString();
    }

    /// <summary>
    /// Formats the overall result as a JSON object following Winix CLI conventions.
    /// Standard envelope fields: tool, version, exit_code, exit_reason.
    /// wargs-specific fields: total_jobs, succeeded, failed, skipped, wall_seconds.
    /// When any job carries a <see cref="JobResult.FaultMessage"/>, a <c>faults</c> array is
    /// appended with one <c>{job, message}</c> entry per faulted job — without this, structured
    /// consumers cannot distinguish a "command not found" from "permission denied" from a
    /// task-body fault, all of which surface as exit_reason=child_failed.
    /// </summary>
    public static string FormatJson(WargsResult result, int exitCode, string exitReason, string toolName, string version)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            writer.WriteNumber("total_jobs", result.TotalJobs);
            writer.WriteNumber("succeeded", result.Succeeded);
            writer.WriteNumber("failed", result.Failed);
            writer.WriteNumber("skipped", result.Skipped);
            JsonHelper.WriteFixedDecimal(writer, "wall_seconds", result.WallTime.TotalSeconds, 3);

            bool anyFaults = false;
            foreach (JobResult job in result.Jobs)
            {
                if (job.FaultMessage is not null) { anyFaults = true; break; }
            }
            if (anyFaults)
            {
                writer.WriteStartArray("faults");
                foreach (JobResult job in result.Jobs)
                {
                    if (job.FaultMessage is not null)
                    {
                        writer.WriteStartObject();
                        writer.WriteNumber("job", job.JobIndex);
                        writer.WriteString("message", SanitiseForJson(job.FaultMessage));
                        writer.WriteEndObject();
                    }
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Formats a single job result as one NDJSON line (no trailing newline).
    /// When the job has one source item, <c>input</c> is a JSON string;
    /// when it has multiple (batched), <c>input</c> is a JSON array.
    /// When the job carries a <see cref="JobResult.FaultMessage"/>, a <c>fault_message</c>
    /// string field is included so streaming consumers can see the diagnostic without
    /// having to wait for the JSON summary.
    /// </summary>
    public static string FormatNdjsonLine(JobResult job, int exitCode, string exitReason, string toolName, string version)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("job", job.JobIndex);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            writer.WriteNumber("child_exit_code", job.ChildExitCode);

            if (job.SourceItems.Length == 1)
            {
                writer.WriteString("input", SanitiseForJson(job.SourceItems[0]));
            }
            else
            {
                writer.WriteStartArray("input");
                foreach (string item in job.SourceItems)
                {
                    writer.WriteStringValue(SanitiseForJson(item));
                }
                writer.WriteEndArray();
            }

            if (job.FaultMessage is not null)
            {
                writer.WriteString("fault_message", SanitiseForJson(job.FaultMessage));
            }

            JsonHelper.WriteFixedDecimal(writer, "wall_seconds", job.Duration.TotalSeconds, 3);
            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Formats an error as a JSON object following Winix CLI conventions.
    /// Used when wargs itself fails before executing any jobs.
    /// </summary>
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
    /// Returns a human-readable failure summary, or <see langword="null"/> if there were no failures.
    /// Written to stderr so it doesn't pollute piped output.
    /// When <paramref name="useColor"/> is <see langword="true"/>, the failure count is rendered in red.
    /// </summary>
    public static string? FormatHumanSummary(WargsResult result, bool useColor)
    {
        if (result.Failed == 0)
        {
            return null;
        }

        string red = Yort.ShellKit.AnsiColor.Red(useColor);
        string reset = Yort.ShellKit.AnsiColor.Reset(useColor);
        return $"wargs: {red}{result.Failed}/{result.TotalJobs} jobs failed{reset}";
    }

}
