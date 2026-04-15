#nullable enable

namespace Winix.NetCat;

/// <summary>
/// The transport protocol used for an nc invocation.
/// </summary>
public enum NetCatProtocol
{
    /// <summary>TCP (default).</summary>
    Tcp,

    /// <summary>UDP.</summary>
    Udp,
}
