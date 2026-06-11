#nullable enable

namespace Winix.Online;

/// <summary>
/// Outcome of a single HTTP probe. Build via <see cref="Unreachable"/> (no HTTP reply — connect/TLS
/// failure, a DNS failure surfaced at request time, or a per-probe timeout) or <see cref="Reached"/>
/// (an HTTP response was received). The private ctor makes the contradictory
/// "not connected but has a status" state unconstructible.
/// </summary>
/// <remarks>
/// The body is deliberately NOT captured: the internet rung's portal discriminator is the 204 STATUS
/// (a captive portal must return 200/302 to show a login page; none return 204), so reading the body
/// adds nothing but a per-cycle full-body allocation and a false-negative risk if an intermediary
/// injects a byte into an otherwise-empty 204. The production probe uses
/// <c>HttpCompletionOption.ResponseHeadersRead</c> and never buffers the body. (Resolves adversarial
/// review F3 + F9.)
/// </remarks>
public sealed record HttpProbeResult
{
    /// <summary>Whether an HTTP response was received.</summary>
    public bool Connected { get; }

    /// <summary>HTTP status code (0 when not connected).</summary>
    public int StatusCode { get; }

    private HttpProbeResult(bool connected, int statusCode)
    {
        Connected = connected;
        StatusCode = statusCode;
    }

    /// <summary>Shared "no HTTP reply" result for connect failures and per-probe timeouts.</summary>
    public static readonly HttpProbeResult Unreachable = new(false, 0);

    /// <summary>An HTTP response was received with the given status code.</summary>
    public static HttpProbeResult Reached(int statusCode) => new(true, statusCode);
}
