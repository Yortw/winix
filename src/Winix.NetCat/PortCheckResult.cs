#nullable enable

namespace Winix.NetCat;

/// <summary>
/// Result of probing one port within a <c>--check</c> run.
/// </summary>
public readonly record struct PortCheckResult(int Port, PortCheckStatus Status, double LatencyMilliseconds, string? ErrorMessage)
{
    /// <summary>Convenience factory for an open result.</summary>
    public static PortCheckResult Open(int port, double latencyMs)
        => new(port, PortCheckStatus.Open, latencyMs, null);

    /// <summary>Convenience factory for a closed result.</summary>
    public static PortCheckResult Closed(int port)
        => new(port, PortCheckStatus.Closed, 0, null);

    /// <summary>Convenience factory for a timeout result.</summary>
    public static PortCheckResult Timeout(int port)
        => new(port, PortCheckStatus.Timeout, 0, null);

    /// <summary>Convenience factory for an error result.</summary>
    public static PortCheckResult Error(int port, string message)
        => new(port, PortCheckStatus.Error, 0, message);
}
