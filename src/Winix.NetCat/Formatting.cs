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
    /// Returns a JSON summary for a Check-mode run. Schema:
    /// <code>
    /// { "tool":"nc", "version":"...", "mode":"check", "host":"...",
    ///   "ports":[ { "port":int, "status":"open|closed|timeout|error",
    ///               "latency_ms":number?, "error":string? }, ... ],
    ///   "exit_code":int, "exit_reason":"all_open|some_closed|some_timeout|all_failed" }
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
