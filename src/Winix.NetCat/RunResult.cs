#nullable enable

using System.Collections.Generic;

namespace Winix.NetCat;

/// <summary>
/// Outcome of running one nc invocation. Returned by every mode runner so
/// the console app can produce its exit code and JSON summary uniformly.
/// </summary>
public sealed class RunResult
{
    /// <summary>Process exit code (e.g. 0, 1, 2, 130).</summary>
    public required int ExitCode { get; init; }

    /// <summary>
    /// Machine-readable reason string for the JSON summary
    /// (e.g. "success", "connection_refused", "timeout", "interrupted").
    /// </summary>
    public required string ExitReason { get; init; }

    /// <summary>Bytes received from the remote peer (Connect/Listen modes).</summary>
    public long BytesReceived { get; init; }

    /// <summary>Bytes sent to the remote peer (Connect/Listen modes).</summary>
    public long BytesSent { get; init; }

    /// <summary>Total wall-clock duration of the run, in milliseconds.</summary>
    public double DurationMilliseconds { get; init; }

    /// <summary>Resolved remote address (Connect mode only).</summary>
    public string? RemoteAddress { get; init; }

    /// <summary>Local bind address (Listen mode only).</summary>
    public string? LocalAddress { get; init; }

    /// <summary>Per-port results (Check mode only).</summary>
    public IReadOnlyList<PortCheckResult>? PortResults { get; init; }
}
