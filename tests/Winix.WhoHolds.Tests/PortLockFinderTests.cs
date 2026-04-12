#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Winix.WhoHolds;
using Xunit;

namespace Winix.WhoHolds.Tests;

public sealed class PortLockFinderTests
{
    [Fact]
    public void Find_BoundPort_ReturnsCurrentProcess()
    {
        if (!OperatingSystem.IsWindows()) { return; }

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            int currentPid = Process.GetCurrentProcess().Id;

            var results = PortLockFinder.Find(port);

            Assert.Contains(results, r => r.ProcessId == currentPid);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Find_BoundPort_ResourceShowsProtocol()
    {
        if (!OperatingSystem.IsWindows()) { return; }

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            int currentPid = Process.GetCurrentProcess().Id;

            var results = PortLockFinder.Find(port);

            var ours = results.FirstOrDefault(r => r.ProcessId == currentPid);
            Assert.NotNull(ours);
            Assert.Contains("TCP", ours!.Resource, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($":{port}", ours.Resource);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Find_UdpBoundPort_ReturnsCurrentProcess()
    {
        if (!OperatingSystem.IsWindows()) { return; }

        var udpClient = new UdpClient(0);
        try
        {
            int port = ((IPEndPoint)udpClient.Client.LocalEndPoint!).Port;
            int currentPid = Process.GetCurrentProcess().Id;

            var results = PortLockFinder.Find(port);

            Assert.Contains(results, r => r.ProcessId == currentPid);
        }
        finally
        {
            udpClient.Close();
        }
    }

    [Fact]
    public void Find_UnusedPort_ReturnsEmpty()
    {
        if (!OperatingSystem.IsWindows()) { return; }

        // Bind then immediately stop to find a free port number.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        int currentPid = Process.GetCurrentProcess().Id;

        var results = PortLockFinder.Find(port);

        Assert.DoesNotContain(results, r => r.ProcessId == currentPid);
    }
}
