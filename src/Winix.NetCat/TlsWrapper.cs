#nullable enable

using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.NetCat;

/// <summary>
/// Wraps a transport <see cref="Stream"/> in an <see cref="SslStream"/>
/// authenticated as a TLS client. Server-side TLS is not supported in v0.2.0.
/// </summary>
public static class TlsWrapper
{
    /// <summary>
    /// Wraps <paramref name="transport"/> in an <see cref="SslStream"/> and
    /// authenticates as a client to <paramref name="targetHost"/> (used for SNI
    /// and certificate-name validation).
    /// </summary>
    /// <param name="transport">Underlying connected stream (typically a NetworkStream).</param>
    /// <param name="targetHost">Hostname for SNI and cert validation.</param>
    /// <param name="insecure">When true, skip cert chain validation.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<SslStream> WrapClientAsync(Stream transport, string targetHost, bool insecure, CancellationToken ct)
    {
        RemoteCertificateValidationCallback? validation = insecure
            ? (_, _, _, _) => true
            : null;
        var ssl = new SslStream(transport, leaveInnerStreamOpen: false, validation);
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        };
        await ssl.AuthenticateAsClientAsync(options, ct).ConfigureAwait(false);
        return ssl;
    }
}
