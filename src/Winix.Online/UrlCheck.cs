#nullable enable

using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Online;

/// <summary>
/// Waits for a named URL to return a status matching a <see cref="StatusSpec"/> (default 2xx).
/// Connection failure, per-probe timeout, 5xx, 429, and any non-matching status all report
/// "not ready" so the caller keeps waiting (a transient server error must not end the wait).
/// </summary>
public sealed class UrlCheck : IReadinessCheck
{
    private readonly string _target;
    private readonly StatusSpec _status;
    private readonly HttpProbe _probe;

    /// <summary>Creates a check for <paramref name="target"/> against <paramref name="status"/>.</summary>
    public UrlCheck(string target, StatusSpec status, HttpProbe probe)
    {
        _target = target;
        _status = status;
        _probe = probe;
    }

    /// <inheritdoc/>
    public async Task<CheckResult> RunAsync(CancellationToken cancellationToken)
    {
        HttpProbeResult probe = await _probe(_target, cancellationToken);
        if (!probe.Connected)
        {
            return new CheckResult("url", _target, false, "connect failed");
        }

        string detail = probe.StatusCode.ToString(CultureInfo.InvariantCulture);
        return new CheckResult("url", _target, _status.Matches(probe.StatusCode), detail);
    }
}
