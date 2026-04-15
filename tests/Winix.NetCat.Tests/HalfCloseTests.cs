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

public sealed class HalfCloseTests
{
    [Fact]
    public async Task TcpClient_StdinEof_PeerSeesReadEof_AndResponseStillRelays()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // Server: read until EOF, then send a fixed response, then close.
        Task serverTask = Task.Run(async () =>
        {
            using TcpClient peer = await listener.AcceptTcpClientAsync();
            using NetworkStream s = peer.GetStream();
            var buf = new byte[1024];
            while (await s.ReadAsync(buf) > 0) { /* drain */ }
            byte[] reply = Encoding.ASCII.GetBytes("HTTP/1.0 200 OK\r\n\r\nbody");
            await s.WriteAsync(reply);
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

            byte[] request = Encoding.ASCII.GetBytes("GET / HTTP/1.0\r\n\r\n");
            using var stdin = new MemoryStream(request);
            using var stdout = new MemoryStream();
            using var stderr = new StringWriter();

            var client = new NetCatClient();
            RunResult result = await client.RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("HTTP/1.0 200 OK", Encoding.ASCII.GetString(stdout.ToArray()));
        }
        finally
        {
            listener.Stop();
            await serverTask.WaitAsync(System.TimeSpan.FromSeconds(2));
        }
    }
}
