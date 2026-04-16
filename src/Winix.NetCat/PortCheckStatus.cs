#nullable enable

namespace Winix.NetCat;

/// <summary>
/// Result of probing a single port.
/// </summary>
public enum PortCheckStatus
{
    /// <summary>The port accepted a TCP connection.</summary>
    Open,

    /// <summary>The port refused the TCP connection (host responded but no listener).</summary>
    Closed,

    /// <summary>The connection attempt did not complete within the configured timeout.</summary>
    Timeout,

    /// <summary>An unexpected error (e.g. DNS failure, network unreachable).</summary>
    Error,
}
