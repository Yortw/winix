#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

namespace Winix.HCat;

/// <summary>Resolved bind target + display info. <see cref="Exposed"/> is true exactly when the bind
/// reaches beyond loopback (the safety-relevant condition that gates the QR and the exposure banner).</summary>
public sealed record BindInfo(IPAddress Address, bool Exposed, IReadOnlyList<string> Urls);

/// <summary>Pure resolution of <see cref="HCatOptions"/> to a bind address + display URLs. The LAN IP
/// lookup is injected so the policy is unit-testable without touching the network.</summary>
public static class BindResolver
{
    /// <summary>Resolves the bind target. <paramref name="lanIps"/> supplies the machine's non-loopback
    /// IPv4 addresses (used for the LAN URLs / QR when exposed).</summary>
    public static BindInfo Resolve(HCatOptions options, Func<IReadOnlyList<string>> lanIps)
    {
        string scheme = options.Https ? "https" : "http";
        string port = options.Port.ToString(CultureInfo.InvariantCulture);

        // --host wins; else --lan → Any; else loopback.
        if (options.Host is not null && IPAddress.TryParse(options.Host, out IPAddress? hostAddr))
        {
            bool exposed = !IPAddress.IsLoopback(hostAddr);
            var urls = new List<string> { $"{scheme}://{options.Host}:{port}" };
            return new BindInfo(hostAddr, exposed, urls);
        }

        if (options.Lan)
        {
            var urls = new List<string>();
            foreach (string ip in lanIps())
            {
                urls.Add($"{scheme}://{ip}:{port}");
            }
            if (urls.Count == 0)
            {
                urls.Add($"{scheme}://0.0.0.0:{port}");
            }
            return new BindInfo(IPAddress.Any, Exposed: true, urls);
        }

        return new BindInfo(IPAddress.Loopback, Exposed: false,
            new[] { $"{scheme}://127.0.0.1:{port}" });
    }
}
