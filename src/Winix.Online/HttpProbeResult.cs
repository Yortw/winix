#nullable enable

namespace Winix.Online;

/// <summary>
/// Outcome of a single HTTP probe. <see cref="Connected"/> is <see langword="false"/> when no HTTP
/// reply was obtained at all — connect/TLS failure, a DNS failure surfaced at request time, or a
/// per-probe timeout. When <see langword="true"/>, <see cref="StatusCode"/> describes the response.
/// </summary>
/// <remarks>
/// The body is deliberately NOT captured: the internet rung's portal discriminator is the 204 STATUS
/// (a captive portal must return 200/302 to show a login page; none return 204), so reading the body
/// adds nothing but a per-cycle full-body allocation and a false-negative risk if an intermediary
/// injects a byte into an otherwise-empty 204. The production probe uses
/// <c>HttpCompletionOption.ResponseHeadersRead</c> and never buffers the body. (Resolves adversarial
/// review F3 + F9.)
/// </remarks>
/// <param name="Connected">Whether an HTTP response was received.</param>
/// <param name="StatusCode">HTTP status code (0 when not connected).</param>
public sealed record HttpProbeResult(bool Connected, int StatusCode)
{
    /// <summary>Shared "no HTTP reply" result for connect failures and per-probe timeouts.</summary>
    public static readonly HttpProbeResult Unreachable = new(false, 0);
}
