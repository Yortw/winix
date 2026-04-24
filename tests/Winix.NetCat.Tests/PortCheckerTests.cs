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

    private static int GetEphemeralPortNoLongerBound()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
