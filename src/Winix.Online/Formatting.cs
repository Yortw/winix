#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Yort.ShellKit;

namespace Winix.Online;

/// <summary>
/// Output formatting for the online tool. The JSON envelope goes to stdout (own-data tool); the
/// human summary and per-attempt verbose lines go to stderr. All methods are pure.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Builds the <c>--json</c> envelope. Field names: <c>tool, version, ready, timed_out,
    /// elapsed_ms, attempts, checks[]</c> where each check is <c>{ kind, target, ok, detail }</c>.
    /// </summary>
    public static string FormatJson(WaitResult result, string version)
    {
        (System.Text.Json.Utf8JsonWriter writer, System.Buffers.ArrayBufferWriter<byte> buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", "online");
            writer.WriteString("version", version);
            writer.WriteBoolean("ready", result.Ready);
            writer.WriteBoolean("timed_out", result.TimedOut);
            writer.WriteNumber("elapsed_ms", (long)result.Elapsed.TotalMilliseconds);
            writer.WriteNumber("attempts", result.Attempts);
            writer.WriteStartArray("checks");
            foreach (CheckResult check in result.LastChecks)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", check.Kind);
                if (check.Target is null)
                {
                    writer.WriteNull("target");
                }
                else
                {
                    writer.WriteString("target", check.Target);
                }
                writer.WriteBoolean("ok", check.Ok);
                writer.WriteString("detail", check.Detail);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }

    /// <summary>Builds the one-line human summary written to stderr after the wait completes.</summary>
    public static string FormatSummary(WaitResult result, bool useColor)
    {
        string green = AnsiColor.Green(useColor);
        string red = AnsiColor.Red(useColor);
        string reset = AnsiColor.Reset(useColor);
        long ms = (long)result.Elapsed.TotalMilliseconds;

        if (result.Ready)
        {
            return $"{green}online{reset}: ready after {result.Attempts} attempt(s), {ms}ms";
        }
        if (result.TimedOut)
        {
            string lastFail = FirstFailureDetail(result.LastChecks);
            return $"{red}online{reset}: timed out after {ms}ms ({result.Attempts} attempts) — {lastFail}";
        }
        // --once miss.
        string detail = FirstFailureDetail(result.LastChecks);
        return $"{red}online{reset}: not ready — {detail}";
    }

    /// <summary>Builds a per-attempt verbose line (one per cycle) for <c>-v</c>.</summary>
    public static string FormatAttempt(int attempt, IReadOnlyList<CheckResult> results, bool useColor)
    {
        string dim = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);
        var sb = new StringBuilder();
        sb.Append(dim);
        sb.Append("attempt ");
        sb.Append(attempt.ToString(CultureInfo.InvariantCulture));
        sb.Append(reset);
        foreach (CheckResult check in results)
        {
            sb.Append("  ");
            sb.Append(check.Ok ? "ok" : "wait");
            sb.Append('(');
            sb.Append(check.Kind);
            if (check.Target is not null)
            {
                sb.Append(' ');
                sb.Append(check.Target);
            }
            sb.Append(": ");
            sb.Append(check.Detail);
            sb.Append(')');
        }
        return sb.ToString();
    }

    private static string FirstFailureDetail(IReadOnlyList<CheckResult> results)
    {
        foreach (CheckResult check in results)
        {
            if (!check.Ok)
            {
                return check.Detail;
            }
        }
        return "unknown";
    }
}
