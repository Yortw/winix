#nullable enable

using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Winix.NetCat;
using Xunit;

namespace Winix.NetCat.Tests;

public sealed class ClientListenerRoundtripTests
{
    [Fact]
    public async Task TcpClient_SendsBytesAndReceivesEcho()
    {
        // Spin up a tiny echo listener on an ephemeral loopback port.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task echoTask = Task.Run(async () =>
        {
            using TcpClient peer = await listener.AcceptTcpClientAsync();
            using NetworkStream s = peer.GetStream();
            var buf = new byte[1024];
            int n;
            while ((n = await s.ReadAsync(buf)) > 0)
            {
                await s.WriteAsync(buf.AsMemory(0, n));
            }
        });

        try
        {
            var options = new NetCatOptions
            {
                Mode = NetCatMode.Connect,
                Protocol = NetCatProtocol.Tcp,
                Host = "127.0.0.1",
                Ports = new[] { new PortRange(port) },
                Timeout = System.TimeSpan.FromSeconds(5),
            };

            byte[] payload = Encoding.ASCII.GetBytes("ping");
            using var stdin = new MemoryStream(payload);
            using var stdout = new MemoryStream();
            using var stderr = new StringWriter();

            var client = new NetCatClient();
            RunResult result = await client.RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("success", result.ExitReason);
            Assert.Equal(4, result.BytesSent);
            Assert.Equal(4, result.BytesReceived);
            Assert.Equal(payload, stdout.ToArray());
        }
        finally
        {
            listener.Stop();
            await echoTask.WaitAsync(System.TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task TcpClient_NothingListening_ReturnsExitOne()
    {
        // Find a port nothing is bound to.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var options = new NetCatOptions
        {
            Mode = NetCatMode.Connect,
            Protocol = NetCatProtocol.Tcp,
            Host = "127.0.0.1",
            Ports = new[] { new PortRange(port) },
            Timeout = System.TimeSpan.FromSeconds(5),
        };

        using var stdin = new MemoryStream();
        using var stdout = new MemoryStream();
        using var stderr = new StringWriter();

        var client = new NetCatClient();
        RunResult result = await client.RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("connection_refused", result.ExitReason);
    }

    [Fact]
    public async Task UdpClient_SendsDatagram_AndReceivesReply()
    {
        // Tiny UDP echo server on an ephemeral port.
        using var server = new UdpClient(0, AddressFamily.InterNetwork);
        int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        Task serverTask = Task.Run(async () =>
        {
            UdpReceiveResult rx = await server.ReceiveAsync();
            await server.SendAsync(rx.Buffer, rx.Buffer.Length, rx.RemoteEndPoint);
        });

        var options = new NetCatOptions
        {
            Mode = NetCatMode.Connect,
            Protocol = NetCatProtocol.Udp,
            Host = "127.0.0.1",
            Ports = new[] { new PortRange(port) },
            Timeout = System.TimeSpan.FromSeconds(2),
        };

        byte[] payload = Encoding.ASCII.GetBytes("hello-udp");
        using var stdin = new MemoryStream(payload);
        using var stdout = new MemoryStream();
        using var stderr = new StringWriter();

        var client = new NetCatClient();
        RunResult result = await client.RunAsync(options, stdin, stdout, stderr, CancellationToken.None);
        await serverTask.WaitAsync(System.TimeSpan.FromSeconds(2));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(payload.Length, result.BytesSent);
        Assert.Equal(payload, stdout.ToArray());
    }

    [Fact]
    public async Task TcpListener_AcceptsOneConnection_AndRelays()
    {
        // Pick an ephemeral port up-front so we can connect to it from the test's "client".
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var options = new NetCatOptions
        {
            Mode = NetCatMode.Listen,
            Protocol = NetCatProtocol.Tcp,
            BindAddress = "127.0.0.1",
            Ports = new[] { new PortRange(port) },
            Timeout = System.TimeSpan.FromSeconds(5),
        };

        byte[] toSend = Encoding.ASCII.GetBytes("listener-says-hi");
        using var stdin = new MemoryStream(toSend);
        using var stdout = new MemoryStream();
        using var stderr = new StringWriter();

        // Run the listener and a fake client concurrently.
        var listenerTask = new NetCatListener().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

        // Give listener a moment to bind.
        await Task.Delay(100);

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
            using NetworkStream cs = client.GetStream();
            byte[] from = Encoding.ASCII.GetBytes("client-says-hi");
            await cs.WriteAsync(from);
            cs.Socket.Shutdown(SocketShutdown.Send);

            var receivedFromListener = new MemoryStream();
            var rb = new byte[1024];
            int n;
            while ((n = await cs.ReadAsync(rb)) > 0)
            {
                await receivedFromListener.WriteAsync(rb.AsMemory(0, n));
            }
            Assert.Equal(toSend, receivedFromListener.ToArray());
        }

        RunResult result = await listenerTask;
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("client-says-hi", Encoding.ASCII.GetString(stdout.ToArray()));
    }

    [Fact]
    public async Task TcpListener_PortAlreadyInUse_ReturnsExitOne()
    {
        // Hold a port in this test, then try to bind a second listener to the same port.
        var hold = new TcpListener(IPAddress.Loopback, 0);
        hold.Start();
        int port = ((IPEndPoint)hold.LocalEndpoint).Port;

        try
        {
            var options = new NetCatOptions
            {
                Mode = NetCatMode.Listen,
                Protocol = NetCatProtocol.Tcp,
                BindAddress = "127.0.0.1",
                Ports = new[] { new PortRange(port) },
                Timeout = System.TimeSpan.FromSeconds(2),
            };

            using var stdin = new MemoryStream();
            using var stdout = new MemoryStream();
            using var stderr = new StringWriter();

            var listener = new NetCatListener();
            RunResult result = await listener.RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal("bind_failed", result.ExitReason);
            Assert.Contains("cannot bind", stderr.ToString());
        }
        finally
        {
            hold.Stop();
        }
    }
}
