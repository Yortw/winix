#nullable enable

namespace Winix.NetCat;

/// <summary>
/// The operating mode of an nc invocation. Mutually exclusive.
/// </summary>
public enum NetCatMode
{
    /// <summary>Outbound connection to a host:port (default).</summary>
    Connect,

    /// <summary>Inbound listener on a port; accepts one connection then exits.</summary>
    Listen,

    /// <summary>Probe one or more ports for openness and exit immediately.</summary>
    Check,
}
