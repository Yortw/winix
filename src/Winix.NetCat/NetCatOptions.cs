#nullable enable

using System.Collections.Generic;
using System.Net.Sockets;

namespace Winix.NetCat;

/// <summary>
/// Frozen, validated options for one nc invocation. Built by the console app's
/// argument parser and passed to the runner for the chosen <see cref="Mode"/>.
/// </summary>
public sealed class NetCatOptions
{
    /// <summary>The operating mode (Connect | Listen | Check).</summary>
    public required NetCatMode Mode { get; init; }

    /// <summary>The transport protocol (Tcp | Udp).</summary>
    public required NetCatProtocol Protocol { get; init; }

    /// <summary>
    /// Target host (for Connect and Check modes). Null in Listen mode — use
    /// <see cref="BindAddress"/> instead.
    /// </summary>
    public string? Host { get; init; }

    /// <summary>
    /// Port(s) to operate on. Always at least one entry. For Listen mode this
    /// has exactly one single-port range. For Connect mode this also has exactly
    /// one single-port range. Check mode may have any combination.
    /// </summary>
    public required IReadOnlyList<PortRange> Ports { get; init; }

    /// <summary>
    /// Bind address for Listen mode. Null = bind all interfaces (IPAddress.Any
    /// or IPAddress.IPv6Any depending on <see cref="AddressFamily"/>). Ignored
    /// outside Listen mode.
    /// </summary>
    public string? BindAddress { get; init; }

    /// <summary>Wrap the connection in TLS. Client mode only (Connect).</summary>
    public bool UseTls { get; init; }

    /// <summary>Skip TLS certificate validation. Only meaningful with <see cref="UseTls"/>.</summary>
    public bool InsecureTls { get; init; }

    /// <summary>
    /// Optional address-family preference. Null = let the resolver choose.
    /// </summary>
    public AddressFamily? AddressFamily { get; init; }

    /// <summary>
    /// Connect/check timeout. For Connect mode applies to the initial connect only.
    /// For Check mode applies to each per-port probe. Default 10 seconds for Check,
    /// no timeout (TimeSpan.Zero) for Connect/Listen if not set.
    /// </summary>
    public System.TimeSpan Timeout { get; init; } = System.TimeSpan.Zero;

    /// <summary>
    /// When true, do NOT call Socket.Shutdown(Send) on stdin EOF — keep the socket
    /// fully open until the peer closes. Default false (do shutdown).
    /// </summary>
    public bool NoShutdown { get; init; }

    /// <summary>When true, show closed/timeout ports too in Check mode (default false).</summary>
    public bool Verbose { get; init; }

    /// <summary>When true, emit a JSON summary to stderr after the run.</summary>
    public bool JsonOutput { get; init; }

    /// <summary>When true, ANSI colour codes appear in stderr status messages.</summary>
    public bool UseColor { get; init; }
}
