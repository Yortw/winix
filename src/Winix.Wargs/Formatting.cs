using System.Text.Json;
using Yort.ShellKit;

namespace Winix.Wargs;

/// <summary>
/// Output formatting for wargs — human-readable summaries, JSON, and NDJSON.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Formats the overall result as a JSON object following Winix CLI conventions.
    /// Standard envelope fields: tool, version, exit_code, exit_reason.
    /// wargs-specific fields: total_jobs, succeeded, failed, skipped, wall_seconds.
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
            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Formats a single job result as one NDJSON line (no trailing newline).
    /// When the job has one source item, <c>input</c> is a JSON string;
    /// when it has multiple (batched), <c>input</c> is a JSON array.
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
                writer.WriteString("input", job.SourceItems[0]);
            }
            else
            {
                writer.WriteStartArray("input");
                foreach (string item in job.SourceItems)
                {
                    writer.WriteStringValue(item);
                }
                writer.WriteEndArray();
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
    /// </summary>
    public static string? FormatHumanSummary(WargsResult result)
    {
        if (result.Failed == 0)
        {
            return null;
        }

        return $"wargs: {result.Failed}/{result.TotalJobs} jobs failed";
    }

}
