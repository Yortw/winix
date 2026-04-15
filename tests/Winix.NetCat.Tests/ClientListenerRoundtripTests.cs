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
}
