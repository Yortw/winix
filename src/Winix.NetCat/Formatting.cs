#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Yort.ShellKit;

namespace Winix.NetCat;

/// <summary>
/// Pure formatting helpers. No I/O — the console app writes the returned strings.
/// </summary>
public static class Formatting
{
    /// <summary>Returns a single line "PORT open", optionally ANSI-coloured green.</summary>
    public static string FormatOpenPortLine(int port, bool useColor)
        => Wrap($"{port.ToString(CultureInfo.InvariantCulture)} open", AnsiColor.Green(useColor), useColor);

    /// <summary>Returns a single line "PORT closed", optionally ANSI-coloured red.</summary>
    public static string FormatClosedPortLine(int port, bool useColor)
        => Wrap($"{port.ToString(CultureInfo.InvariantCulture)} closed", AnsiColor.Red(useColor), useColor);

    /// <summary>Returns a single line "PORT timeout", optionally ANSI-coloured yellow.</summary>
    public static string FormatTimeoutPortLine(int port, bool useColor)
        => Wrap($"{port.ToString(CultureInfo.InvariantCulture)} timeout", AnsiColor.Yellow(useColor), useColor);

    /// <summary>
    /// Computes the check-mode <c>exit_reason</c> string from a results set. Pulled out of
    /// Program.cs so it's independently unit-testable — the <c>some_failed</c> branch (mixed
    /// errors + successes) is hard to trigger via a real process-spawn scan (one host = one
    /// DNS outcome), so this helper is the authoritative pin for that branch.
    /// </summary>
    /// <param name="results">Per-port results in scan order.</param>
    /// <returns>
    /// One of: <c>all_open</c>, <c>some_closed</c>, <c>some_timeout</c>, <c>all_failed</c>,
    /// <c>some_failed</c>. Mirrors the worst-status precedence used by the console app:
    /// Error &gt; Timeout &gt; Closed &gt; Open.
    /// </returns>
    public static string ComputeCheckExitReason(IReadOnlyList<PortCheckResult> results)
    {
        int worstStatus = 0; // 0=open, 1=closed, 2=timeout, 3=error
        int errorCount = 0;
        foreach (PortCheckResult r in results)
        {
            switch (r.Status)
            {
                case PortCheckStatus.Closed:
                    if (worstStatus < 1) { worstStatus = 1; }
                    break;
                case PortCheckStatus.Timeout:
                    if (worstStatus < 2) { worstStatus = 2; }
                    break;
                case PortCheckStatus.Error:
                    if (worstStatus < 3) { worstStatus = 3; }
                    errorCount++;
                    break;
            }
        }
        return worstStatus switch
        {
            0 => "all_open",
            1 => "some_closed",
            2 => "some_timeout",
            _ => errorCount == results.Count ? "all_failed" : "some_failed",
        };
    }

    /// <summary>
    /// Returns a JSON summary for a Check-mode run. Schema:
    /// <code>
    /// { "tool":"nc", "version":"...", "mode":"check", "host":"...",
    ///   "ports":[ { "port":int, "status":"open|closed|timeout|error",
    ///               "latency_ms":number?, "error":string? }, ... ],
    ///   "exit_code":int, "exit_reason":"all_open|some_closed|some_timeout|all_failed|some_failed" }
    /// </code>
    /// </summary>
    public static string FormatCheckJson(string version, string host, IReadOnlyList<PortCheckResult> results,
                                         int exitCode, string exitReason)
    {
        (var writer, var buffer) = JsonHelper.CreateWriter();
        writer.WriteStartObject();
        writer.WriteString("tool", "nc");
        writer.WriteString("version", version);
        writer.WriteString("mode", "check");
        writer.WriteString("host", host);
        writer.WritePropertyName("ports");
        writer.WriteStartArray();
        foreach (PortCheckResult r in results)
        {
            writer.WriteStartObject();
            writer.WriteNumber("port", r.Port);
            writer.WriteString("status", StatusName(r.Status));
            if (r.Status == PortCheckStatus.Open)
            {
                JsonHelper.WriteFixedDecimal(writer, "latency_ms", r.LatencyMilliseconds, decimals: 2);
            }
            if (r.Status == PortCheckStatus.Error && r.ErrorMessage is not null)
            {
                writer.WriteString("error", r.ErrorMessage);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteNumber("exit_code", exitCode);
        writer.WriteString("exit_reason", exitReason);
        writer.WriteEndObject();
        writer.Flush();
        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Returns a JSON summary for a Connect or Listen run.
    /// </summary>
    public static string FormatRunJson(string version, NetCatOptions options, RunResult result)
    {
        (var writer, var buffer) = JsonHelper.CreateWriter();
        writer.WriteStartObject();
        writer.WriteString("tool", "nc");
        writer.WriteString("version", version);
        writer.WriteString("mode", options.Mode == NetCatMode.Listen ? "listen" : "connect");
        if (options.Host is not null)
        {
            writer.WriteString("host", options.Host);
        }
        writer.WriteNumber("port", options.Ports[0].Low);
        writer.WriteString("protocol", options.Protocol == NetCatProtocol.Udp ? "udp" : "tcp");
        writer.WriteBoolean("tls", options.UseTls);
        if (result.RemoteAddress is not null)
        {
            writer.WriteString("remote_address", result.RemoteAddress);
        }
        if (result.LocalAddress is not null)
        {
            writer.WriteString("local_address", result.LocalAddress);
        }
        writer.WriteNumber("bytes_sent", result.BytesSent);
        writer.WriteNumber("bytes_received", result.BytesReceived);
        JsonHelper.WriteFixedDecimal(writer, "duration_ms", result.DurationMilliseconds, decimals: 2);
        writer.WriteNumber("exit_code", result.ExitCode);
        writer.WriteString("exit_reason", result.ExitReason);
        writer.WriteEndObject();
        writer.Flush();
        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Returns a minimal JSON error envelope for cases where an unexpected exception escapes
    /// past all per-dispatch RunResult mapping (e.g. DispatchCoreAsync throws mid-flight).
    /// This lets automation that requested <c>--json</c> still see structured output instead of
    /// a stderr-only crash line plus exit 126. Mirrors the schema produced by
    /// <see cref="FormatRunJson"/> so consumers can parse it with the same fields.
    /// </summary>
    public static string FormatErrorJson(string version, NetCatOptions options, int exitCode, string exitReason, string errorMessage)
    {
        (var writer, var buffer) = JsonHelper.CreateWriter();
        writer.WriteStartObject();
        writer.WriteString("tool", "nc");
        writer.WriteString("version", version);
        writer.WriteString("mode", options.Mode == NetCatMode.Listen ? "listen" : options.Mode == NetCatMode.Check ? "check" : "connect");
        if (options.Host is not null) { writer.WriteString("host", options.Host); }
        if (options.Ports is { Count: > 0 } && options.Ports[0].Low == options.Ports[0].High)
        {
            writer.WriteNumber("port", options.Ports[0].Low);
        }
        writer.WriteString("protocol", options.Protocol == NetCatProtocol.Udp ? "udp" : "tcp");
        writer.WriteBoolean("tls", options.UseTls);
        writer.WriteNumber("exit_code", exitCode);
        writer.WriteString("exit_reason", exitReason);
        writer.WriteString("error", errorMessage);
        writer.WriteEndObject();
        writer.Flush();
        return JsonHelper.GetString(buffer);
    }

    /// <summary>Formats an error message for stderr (red when colour enabled).</summary>
    public static string FormatErrorLine(string message, bool useColor)
        => useColor
            ? AnsiColor.Red(true) + "nc: " + message + AnsiColor.Reset(true)
            : "nc: " + message;

    /// <summary>Formats a warning message for stderr (yellow when colour enabled).</summary>
    public static string FormatWarningLine(string message, bool useColor)
        => useColor
            ? AnsiColor.Yellow(true) + "nc: warning — " + message + AnsiColor.Reset(true)
            : "nc: warning — " + message;

    private static string StatusName(PortCheckStatus status) => status switch
    {
        PortCheckStatus.Open => "open",
        PortCheckStatus.Closed => "closed",
        PortCheckStatus.Timeout => "timeout",
        PortCheckStatus.Error => "error",
        _ => "unknown",
    };

    private static string Wrap(string text, string colorOpen, bool useColor)
        => useColor ? colorOpen + text + AnsiColor.Reset(true) : text;
}
