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

    private static int GetEphemeralPortNoLongerBound()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
