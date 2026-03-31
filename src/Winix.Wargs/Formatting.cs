using System.Globalization;
using System.Text;

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
        return string.Format(
            CultureInfo.InvariantCulture,
            "{{\"tool\":\"{0}\",\"version\":\"{1}\",\"exit_code\":{2},\"exit_reason\":\"{3}\",\"total_jobs\":{4},\"succeeded\":{5},\"failed\":{6},\"skipped\":{7},\"wall_seconds\":{8:F3}}}",
            EscapeJson(toolName),
            EscapeJson(version),
            exitCode,
            EscapeJson(exitReason),
            result.TotalJobs,
            result.Succeeded,
            result.Failed,
            result.Skipped,
            result.WallTime.TotalSeconds);
    }

    /// <summary>
    /// Formats a single job result as one NDJSON line (no trailing newline).
    /// When the job has one source item, <c>input</c> is a JSON string;
    /// when it has multiple (batched), <c>input</c> is a JSON array.
    /// </summary>
    public static string FormatNdjsonLine(JobResult job, int exitCode, string exitReason, string toolName, string version)
    {
        var sb = new StringBuilder();
        sb.AppendFormat(
            CultureInfo.InvariantCulture,
            "{{\"tool\":\"{0}\",\"version\":\"{1}\",\"job\":{2},\"exit_code\":{3},\"exit_reason\":\"{4}\",\"child_exit_code\":{5},",
            EscapeJson(toolName),
            EscapeJson(version),
            job.JobIndex,
            exitCode,
            EscapeJson(exitReason),
            job.ChildExitCode);

        if (job.SourceItems.Length == 1)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"input\":\"{0}\",", EscapeJson(job.SourceItems[0]));
        }
        else
        {
            sb.Append("\"input\":[");
            for (int i = 0; i < job.SourceItems.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.AppendFormat(CultureInfo.InvariantCulture, "\"{0}\"", EscapeJson(job.SourceItems[i]));
            }

            sb.Append("],");
        }

        sb.AppendFormat(CultureInfo.InvariantCulture, "\"wall_seconds\":{0:F3}}}", job.Duration.TotalSeconds);

        return sb.ToString();
    }

    /// <summary>
    /// Formats an error as a JSON object following Winix CLI conventions.
    /// Used when wargs itself fails before executing any jobs.
    /// </summary>
    public static string FormatJsonError(int exitCode, string exitReason, string toolName, string version)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{{\"tool\":\"{0}\",\"version\":\"{1}\",\"exit_code\":{2},\"exit_reason\":\"{3}\"}}",
            EscapeJson(toolName),
            EscapeJson(version),
            exitCode,
            EscapeJson(exitReason));
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

    /// <summary>Escapes backslashes and double-quotes for safe JSON string embedding.</summary>
    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
