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

    private static int GetEphemeralPortNoLongerBound()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
