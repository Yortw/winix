#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Winix.Online;

/// <summary>Performs a single HTTP GET against <paramref name="url"/>. Must translate connect/TLS
/// failures and per-probe timeouts into <see cref="HttpProbeResult.Unreachable"/> and only rethrow
/// <see cref="System.OperationCanceledException"/> when the OUTER token (user cancel) fired.</summary>
public delegate Task<HttpProbeResult> HttpProbe(string url, CancellationToken cancellationToken);

/// <summary>Resolves <paramref name="host"/> to one or more addresses. Returns <see langword="false"/>
/// on resolution failure; rethrows only on outer-token cancellation.</summary>
public delegate Task<bool> DnsProbe(string host, CancellationToken cancellationToken);
