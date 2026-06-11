#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Online;

/// <summary>
/// The layered, captive-portal-aware "is the internet actually up" check:
/// <list type="number">
/// <item>Route present — a fast OS negative (<c>NetworkInterface.GetIsNetworkAvailable()</c> in
/// production). <see langword="false"/> ⇒ offline with zero external traffic. <see langword="true"/>
/// is untrustworthy (lies about virtual adapters), so it only gates continuation.</item>
/// <item>DNS resolves for the endpoint host.</item>
/// <item>HTTP GET returns status <c>204</c> — a 200 (portal login page), 302 (portal redirect), or
/// any other status ⇒ not online. The 204 status is the portal discriminator (a portal must return
/// 200/302 to present a login page; none return 204), so the body is never read (review F3/F9).</item>
/// </list>
/// Endpoints are tried in the order returned by the injected <c>order</c> seam (randomised in
/// production), and the first endpoint that returns a 204 wins (short-circuit).
/// </summary>
public sealed class InternetCheck : IReadinessCheck
{
    private readonly IReadOnlyList<string> _endpoints;
    private readonly Func<bool> _routeAvailable;
    private readonly DnsProbe _dnsProbe;
    private readonly HttpProbe _httpProbe;
    private readonly Func<IReadOnlyList<string>, IReadOnlyList<string>> _order;

    /// <summary>Creates the internet check with injectable network and ordering seams.</summary>
    public InternetCheck(
        IReadOnlyList<string> endpoints,
        Func<bool> routeAvailable,
        DnsProbe dnsProbe,
        HttpProbe httpProbe,
        Func<IReadOnlyList<string>, IReadOnlyList<string>> order)
    {
        _endpoints = endpoints;
        _routeAvailable = routeAvailable;
        _dnsProbe = dnsProbe;
        _httpProbe = httpProbe;
        _order = order;
    }

    /// <inheritdoc/>
    public async Task<CheckResult> RunAsync(CancellationToken cancellationToken)
    {
        // Rung 1 — cheap OS negative. The common outage (wifi dropped, cable out) ends here with
        // no external requests at all.
        if (!_routeAvailable())
        {
            return new CheckResult("internet", null, false, "no network route");
        }

        IReadOnlyList<string> ordered = _order(_endpoints);
        string lastDetail = "no connectivity endpoints configured";

        foreach (string url in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string host = ExtractHost(url);

            // Rung 2 — DNS. A failure here means this endpoint is unusable; try the next one.
            if (!await _dnsProbe(host, cancellationToken))
            {
                lastDetail = $"DNS resolution failed for {host}";
                continue;
            }

            // Rung 3 — HTTP. 204 ⇒ online; anything else ⇒ portal / not online.
            HttpProbeResult probe = await _httpProbe(url, cancellationToken);
            if (!probe.Connected)
            {
                lastDetail = $"connect failed to {url}";
                continue;
            }
            if (probe.StatusCode == 204)
            {
                return new CheckResult("internet", null, true, $"204 via {url}");
            }

            lastDetail = $"unexpected {probe.StatusCode} from {url} (captive portal?)";
        }

        return new CheckResult("internet", null, false, lastDetail);
    }

    private static string ExtractHost(string url)
    {
        // Endpoints are validated as absolute http(s) URIs at options-build time, so this succeeds.
        // Falls back to the raw string defensively rather than throwing inside the poll loop.
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Host : url;
    }
}
