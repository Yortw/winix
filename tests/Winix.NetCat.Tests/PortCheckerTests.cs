#nullable enable

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Winix.NetCat;
using Xunit;

namespace Winix.NetCat.Tests;

public sealed class PortCheckerTests
{
    [Fact]
    public async Task CheckAsync_NothingBound_ReturnsClosed()
    {
        // Bind+release immediately to claim and free a port we know nothing is listening on.
        int port = GetEphemeralPortNoLongerBound();

        var checker = new PortChecker();
        IReadOnlyList<PortCheckResult> results = await checker.CheckAsync(
            host: "127.0.0.1",
            ranges: new[] { new PortRange(port) },
            timeout: System.TimeSpan.FromSeconds(5),
            maxConcurrency: 1,
            ct: CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(PortCheckStatus.Closed, results[0].Status);
        Assert.Equal(port, results[0].Port);
    }

    [Fact]
    public async Task CheckAsync_PortBoundLocally_ReturnsOpen()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var checker = new PortChecker();
            IReadOnlyList<PortCheckResult> results = await checker.CheckAsync(
                host: "127.0.0.1",
                ranges: new[] { new PortRange(port) },
                timeout: System.TimeSpan.FromSeconds(2),
                maxConcurrency: 1,
                ct: CancellationToken.None);

            Assert.Single(results);
            Assert.Equal(PortCheckStatus.Open, results[0].Status);
            Assert.True(results[0].LatencyMilliseconds >= 0);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task CheckAsync_MultiplePortsMixedOpenClosed_ReturnsCorrectStatuses()
    {
        var openListener = new TcpListener(IPAddress.Loopback, 0);
        openListener.Start();
        int openPort = ((IPEndPoint)openListener.LocalEndpoint).Port;
        int closedPort = GetEphemeralPortNoLongerBound();

        try
        {
            var checker = new PortChecker();
            IReadOnlyList<PortCheckResult> results = await checker.CheckAsync(
                host: "127.0.0.1",
                ranges: new[] { new PortRange(openPort), new PortRange(closedPort) },
                timeout: System.TimeSpan.FromSeconds(5),
                maxConcurrency: 4,
                ct: CancellationToken.None);

            Assert.Equal(2, results.Count);
            Assert.Equal(openPort, results[0].Port);
            Assert.Equal(PortCheckStatus.Open, results[0].Status);
            Assert.Equal(closedPort, results[1].Port);
            Assert.Equal(PortCheckStatus.Closed, results[1].Status);
        }
        finally
        {
            openListener.Stop();
        }
    }

    [Fact]
    public async Task CheckAsync_UnresolvableHost_ReturnsError()
    {
        var checker = new PortChecker();
        IReadOnlyList<PortCheckResult> results = await checker.CheckAsync(
            host: "this-host-does-not-exist.invalid",
            ranges: new[] { new PortRange(80) },
            timeout: System.TimeSpan.FromSeconds(5),
            maxConcurrency: 1,
            ct: CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(PortCheckStatus.Error, results[0].Status);
        Assert.NotNull(results[0].ErrorMessage);
    }

    /// <summary>
    /// Pins round-1 I-4: an exception from TcpClient.ConnectAsync that is NOT a SocketException
    /// (e.g. ArgumentException on empty hostname, NotSupportedException on AF mismatch) must be
    /// classified as per-port Error — not escape Task.WhenAll and abort the entire scan. A revert
    /// of the broad-catch arm in PortChecker.ProbeOneAsync would surface as "UnexpectedException
    /// escaped CheckAsync" rather than a row-per-port result set.
    /// </summary>
    [Fact]
    public async Task CheckAsync_OnePortWithBadHostname_ClassifiesAsError_AndSiblingsStillRun()
    {
        // Bind a good port locally so the scan has a definitely-open sibling that MUST survive
        // even if ProbeOneAsync mishandled the other port's exception.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int openPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        int anotherPort = GetEphemeralPortNoLongerBound();
        try
        {
            var checker = new PortChecker();
            // An empty host string makes TcpClient.ConnectAsync throw ArgumentException (not a
            // SocketException). Scan includes a known-open loopback port too: the open result
            // proves the scan wasn't torn down by Task.WhenAll.
            IReadOnlyList<PortCheckResult> results = await checker.CheckAsync(
                host: "",
                ranges: new[] { new PortRange(openPort), new PortRange(anotherPort) },
                timeout: System.TimeSpan.FromSeconds(2),
                maxConcurrency: 4,
                ct: CancellationToken.None);

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal(PortCheckStatus.Error, r.Status));
            Assert.All(results, r => Assert.NotNull(r.ErrorMessage));
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Pins round-3 C1: when the user pins <c>--ipv4</c>/<c>--ipv6</c>, the probe must resolve
    /// via that family. Previously PortChecker ignored <c>options.AddressFamily</c> entirely,
    /// silently running dual-stack. Here we ask for IPv6 against the v4 loopback literal —
    /// the family filter must return empty and classify as Error.
    /// </summary>
    [Fact]
    public async Task CheckAsync_RequestIPv6_HostIsV4Literal_ReturnsErrorAndSiblingsStillRun()
    {
        // Two ports, both going through the IPv6 family filter. "127.0.0.1" is an IPv4 literal —
        // GetHostAddressesAsync(host, InterNetworkV6) returns empty for it, so both probes
        // must classify as Error (no v6 address). Regression would be that the probe falls
        // through to the default TcpClient() path and actually connects over v4.
        var openListener = new TcpListener(IPAddress.Loopback, 0);
        openListener.Start();
        int openPort = ((IPEndPoint)openListener.LocalEndpoint).Port;
        int otherPort = GetEphemeralPortNoLongerBound();
        try
        {
            var checker = new PortChecker();
            IReadOnlyList<PortCheckResult> results = await checker.CheckAsync(
                host: "127.0.0.1",
                ranges: new[] { new PortRange(openPort), new PortRange(otherPort) },
                timeout: System.TimeSpan.FromSeconds(2),
                maxConcurrency: 2,
                ct: CancellationToken.None,
                addressFamily: AddressFamily.InterNetworkV6);

            Assert.Equal(2, results.Count);
            // Both ports must be Error (no v6 address for host) — not Open, Closed, or Timeout.
            // If the AF filter were ignored (the round-3 defect), openPort would come back Open.
            Assert.All(results, r => Assert.Equal(PortCheckStatus.Error, r.Status));
            Assert.All(results, r => Assert.Contains("IPv6", r.ErrorMessage ?? ""));
        }
        finally
        {
            openListener.Stop();
        }
    }

    /// <summary>
    /// Pins round-3 C1 the other direction: requesting IPv4 against a host that resolves only
    /// to IPv6 returns Error. Uses the v6 loopback literal <c>::1</c> since it has no v4
    /// companion under GetHostAddressesAsync's family filter.
    /// </summary>
    [Fact]
    public async Task CheckAsync_RequestIPv4_HostIsV6Literal_ReturnsError()
    {
        var checker = new PortChecker();
        IReadOnlyList<PortCheckResult> results = await checker.CheckAsync(
            host: "::1",
            ranges: new[] { new PortRange(80) },
            timeout: System.TimeSpan.FromSeconds(2),
            maxConcurrency: 1,
            ct: CancellationToken.None,
            addressFamily: AddressFamily.InterNetwork);

        Assert.Single(results);
        Assert.Equal(PortCheckStatus.Error, results[0].Status);
        Assert.Contains("IPv4", results[0].ErrorMessage ?? "");
    }

    /// <summary>
    /// Round-10 review I-2: pin the cancel-mid-scan contract. CheckAsync runs probes via
    /// Task.WhenAll with each ProbeOneAsync linked to the outer ct via CreateLinkedTokenSource.
    /// ProbeOneAsync's catch arms distinguish <c>!ct.IsCancellationRequested</c> (probe-local
    /// timeout → returns Timeout result) from user cancel (no catch — OCE escapes the probe,
    /// Task.WhenAll observes it, CheckAsync rethrows). Real-world risk: <c>nc -z host 1-1000</c>
    /// interrupted by Ctrl+C must propagate to the caller as OCE rather than (a) silently
    /// returning a partial-results list as if the scan completed, (b) escaping as a different
    /// exception type Main can't classify, or (c) hanging until every probe times out
    /// individually. A regression that broadened the probe-local catch to swallow user-cancel
    /// would silently violate (a). This test pins (a)/(b)/(c) via three observable properties:
    /// the call throws OCE, the throw arrives well before the per-probe timeout would have
    /// fired, and the throw is the standard OCE type Main already handles.
    /// </summary>
    [Fact]
    public async Task CheckAsync_CancelMidScan_PropagatesOperationCanceledException()
    {
        // Probe a wide range against a non-routable RFC 5737 documentation IP — every probe
        // hangs in connect until either the per-probe timeout fires or external cancel is
        // observed. We set a long per-probe timeout (10s) so the scan would take many seconds
        // if cancel were ignored; observing the throw within ~500ms proves cancel propagated
        // before any natural timeout.
        var checker = new PortChecker();

        using var userCts = new CancellationTokenSource();
        userCts.CancelAfter(System.TimeSpan.FromMilliseconds(200));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            checker.CheckAsync(
                host: "192.0.2.1",
                ranges: new[] { new PortRange(1, 100) },
                timeout: System.TimeSpan.FromSeconds(10),
                maxConcurrency: 4,
                ct: userCts.Token));
        sw.Stop();

        // Cancel must propagate well before the per-probe timeout would have. 3-second
        // upper bound gives generous headroom for slow CI runners while still proving
        // we're not just waiting on natural completion of all 100 probes.
        Assert.True(sw.Elapsed < System.TimeSpan.FromSeconds(3),
            $"Expected cancel to propagate quickly, but CheckAsync took {sw.Elapsed.TotalSeconds:F1}s");
    }

    private static int GetEphemeralPortNoLongerBound()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
