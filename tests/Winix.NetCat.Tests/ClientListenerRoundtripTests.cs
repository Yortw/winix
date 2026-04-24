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

    /// <summary>
    /// Pins round-2 I2 + the exit-code-2 contract for listener accept timeout. Reverting the
    /// stderr note would silently exit 2 with empty stderr (the original defect); reverting the
    /// timeout arm would block indefinitely or exit with a different code.
    /// </summary>
    [Fact]
    public async Task TcpListener_AcceptTimeout_ReturnsExitTwo_AndWritesStderrNote()
    {
        // Pick a free port; deliberately never connect to it so accept times out.
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
            Timeout = System.TimeSpan.FromMilliseconds(300),
        };

        using var stdin = new MemoryStream();
        using var stdout = new MemoryStream();
        using var stderr = new StringWriter();

        RunResult result = await new NetCatListener().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("timeout", result.ExitReason);
        string err = stderr.ToString();
        Assert.Contains("no client within", err);
        Assert.Contains("127.0.0.1", err);
    }

    /// <summary>
    /// Pins round-2 I1: when the downstream stdout pipe closes during the receive leg (e.g.
    /// `nc host 80 | head -c 10`), nc must exit 0 with exit_reason "stdout_closed" — matching
    /// BSD nc's SIGPIPE semantics. Reverting the StdoutClosedException handler would
    /// mis-classify this as exit 1 "socket_error".
    /// </summary>
    [Fact]
    public async Task TcpClient_StdoutClosesDuringReceive_ReturnsExitZero_StdoutClosed()
    {
        // Echo server that sends a small payload then closes.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task serverTask = Task.Run(async () =>
        {
            using TcpClient peer = await listener.AcceptTcpClientAsync();
            using NetworkStream s = peer.GetStream();
            // Write continuously so the client definitely hits the broken-pipe write.
            byte[] payload = Encoding.ASCII.GetBytes(new string('x', 4096));
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    await s.WriteAsync(payload);
                    await Task.Delay(10);
                }
            }
            catch (IOException) { /* peer closed */ }
            catch (System.Net.Sockets.SocketException) { /* peer reset */ }
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

            using var stdin = new MemoryStream(); // empty stdin — send leg finishes fast
            using var stdout = new ThrowAfterBytesStream(bytesBeforeThrow: 100);
            using var stderr = new StringWriter();

            RunResult result = await new NetCatClient().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("stdout_closed", result.ExitReason);
        }
        finally
        {
            listener.Stop();
            await serverTask.WaitAsync(System.TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>
    /// Pins round-2 I3: UDP connect with no response emits a stderr warning so the user knows
    /// WHY output is empty, rather than assuming the exchange silently succeeded.
    /// </summary>
    [Fact]
    public async Task UdpConnect_NoResponseWithinTimeout_ReturnsZero_WithStderrWarning()
    {
        // Bind a UDP socket to reserve a port, but never answer. The send succeeds
        // locally; the receive will hit the timeout.
        using var black = new UdpClient(0, AddressFamily.InterNetwork);
        int port = ((IPEndPoint)black.Client.LocalEndPoint!).Port;

        var options = new NetCatOptions
        {
            Mode = NetCatMode.Connect,
            Protocol = NetCatProtocol.Udp,
            Host = "127.0.0.1",
            Ports = new[] { new PortRange(port) },
            Timeout = System.TimeSpan.FromMilliseconds(300),
        };

        byte[] payload = Encoding.ASCII.GetBytes("ping");
        using var stdin = new MemoryStream(payload);
        using var stdout = new MemoryStream();
        using var stderr = new StringWriter();

        RunResult result = await new NetCatClient().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("no UDP response", stderr.ToString());
    }

    /// <summary>
    /// Stream that lets the first N bytes land in a MemoryStream then throws IOException on
    /// every subsequent write — simulating `nc host 80 | head -c 10` closing the downstream pipe.
    /// </summary>
    private sealed class ThrowAfterBytesStream : Stream
    {
        private readonly int _budget;
        private int _written;
        public ThrowAfterBytesStream(int bytesBeforeThrow) { _budget = bytesBeforeThrow; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) => WriteCore(count);
        public override ValueTask WriteAsync(System.ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            WriteCore(buffer.Length);
            return ValueTask.CompletedTask;
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            WriteCore(count);
            return Task.CompletedTask;
        }
        private void WriteCore(int n)
        {
            if (_written >= _budget) { throw new IOException("The pipe has been ended."); }
            _written += n;
        }
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
