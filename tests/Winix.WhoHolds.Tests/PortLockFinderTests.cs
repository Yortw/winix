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
    [SkippableFact]
    public void Find_BoundPort_ReturnsCurrentProcess()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        if (!OperatingSystem.IsWindows()) { return; } // redundant, satisfies CA1416 analyzer

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            int currentPid = Process.GetCurrentProcess().Id;

            FindResult result = PortLockFinder.Find(port);

            Assert.False(result.QueryFailed, $"Find should not have failed: {result.Reason}");
            Assert.Contains(result.Results, r => r.ProcessId == currentPid);
        }
        finally
        {
            listener.Stop();
        }
    }

    [SkippableFact]
    public void Find_BoundPort_ResourceShowsProtocol()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        if (!OperatingSystem.IsWindows()) { return; } // redundant, satisfies CA1416 analyzer

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            int currentPid = Process.GetCurrentProcess().Id;

            FindResult result = PortLockFinder.Find(port);

            Assert.False(result.QueryFailed);
            var ours = result.Results.FirstOrDefault(r => r.ProcessId == currentPid);
            Assert.NotNull(ours);
            Assert.Contains("TCP", ours!.Resource, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($":{port}", ours.Resource, StringComparison.Ordinal);
        }
        finally
        {
            listener.Stop();
        }
    }

    [SkippableFact]
    public void Find_UdpBoundPort_ReturnsCurrentProcess()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        if (!OperatingSystem.IsWindows()) { return; } // redundant, satisfies CA1416 analyzer

        var udpClient = new UdpClient(0);
        try
        {
            int port = ((IPEndPoint)udpClient.Client.LocalEndPoint!).Port;
            int currentPid = Process.GetCurrentProcess().Id;

            FindResult result = PortLockFinder.Find(port);

            Assert.False(result.QueryFailed, $"Find should not have failed: {result.Reason}");
            Assert.Contains(result.Results, r => r.ProcessId == currentPid);
        }
        finally
        {
            udpClient.Close();
        }
    }

    [SkippableFact]
    public void Find_UnusedPort_ReturnsSuccessEmpty()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only integration test");
        if (!OperatingSystem.IsWindows()) { return; } // redundant, satisfies CA1416 analyzer

        // Bind then immediately stop to find a free port number.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        int currentPid = Process.GetCurrentProcess().Id;

        FindResult result = PortLockFinder.Find(port);

        Assert.False(result.QueryFailed);
        Assert.DoesNotContain(result.Results, r => r.ProcessId == currentPid);
    }

    /// <summary>
    /// On non-Windows platforms PortLockFinder has no backend; success-empty (not QueryFailed).
    /// Uses <see cref="SkippableFact"/> rather than plain <see cref="Fact"/> so the test is
    /// reported Skipped on Windows hosts instead of pass-by-default. See sibling test in
    /// <see cref="FileLockFinderTests.Find_OnNonWindows_ReturnsSuccessEmpty"/>.
    /// </summary>
    [SkippableFact]
    public void Find_OnNonWindows_ReturnsSuccessEmpty()
    {
        Skip.If(OperatingSystem.IsWindows(), "Off-Windows-only branch — Windows uses IP Helper backend");

        FindResult result = PortLockFinder.Find(8080);
        Assert.False(result.QueryFailed);
        Assert.Empty(result.Results);
    }
}
