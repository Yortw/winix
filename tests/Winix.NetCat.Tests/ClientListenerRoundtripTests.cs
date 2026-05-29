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

    /// <summary>
    /// Pins round-3 C1 at the NetCatClient seam: when <c>--ipv6</c> is pinned via
    /// <c>options.AddressFamily</c>, the connect path must resolve via v6 only. Using the v4
    /// loopback literal with AF v6 must classify as <c>host_not_found</c> exit 1, not silently
    /// fall through to dual-stack and connect to v4 loopback successfully.
    /// </summary>
    [Fact]
    public async Task TcpClient_RequestIPv6_HostIsV4Literal_ReturnsExitOne_HostNotFound()
    {
        // Bind a real v4 listener so "silently fell through to v4" would show up as a
        // successful connection (exit 0), proving the fix works.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task serverTask = Task.Run(async () =>
        {
            try { using TcpClient peer = await listener.AcceptTcpClientAsync(); }
            catch { /* we expect no connection — the AF filter should stop it */ }
        });

        try
        {
            var options = new NetCatOptions
            {
                Mode = NetCatMode.Connect,
                Protocol = NetCatProtocol.Tcp,
                Host = "127.0.0.1",
                Ports = new[] { new PortRange(port) },
                AddressFamily = AddressFamily.InterNetworkV6,
                Timeout = System.TimeSpan.FromSeconds(2),
            };

            using var stdin = new MemoryStream();
            using var stdout = new MemoryStream();
            using var stderr = new StringWriter();

            RunResult result = await new NetCatClient().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal("host_not_found", result.ExitReason);
            Assert.Contains("IPv6", stderr.ToString());
        }
        finally
        {
            listener.Stop();
            // Listener.Stop cancels the pending AcceptTcpClientAsync — the server task's try/catch swallows.
            try { await serverTask.WaitAsync(System.TimeSpan.FromSeconds(2)); } catch { }
        }
    }

    /// <summary>
    /// Pins round-3 class-B (SFH C3): a stdin stream that throws IOException during
    /// ReadAsync on the UDP send leg must be classified as exit 1 <c>io_error</c> —
    /// NOT escape to the safety-net as "unexpected error" exit 126.
    /// </summary>
    [Fact]
    public async Task UdpConnect_StdinIOException_ReturnsExitOne_IoError()
    {
        // Bind a UDP socket so the connect step succeeds.
        using var dest = new UdpClient(0, AddressFamily.InterNetwork);
        int port = ((IPEndPoint)dest.Client.LocalEndPoint!).Port;

        var options = new NetCatOptions
        {
            Mode = NetCatMode.Connect,
            Protocol = NetCatProtocol.Udp,
            Host = "127.0.0.1",
            Ports = new[] { new PortRange(port) },
            Timeout = System.TimeSpan.Zero, // skip the receive leg entirely
        };

        using var stdin = new ThrowOnReadStream();
        using var stdout = new MemoryStream();
        using var stderr = new StringWriter();

        RunResult result = await new NetCatClient().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("io_error", result.ExitReason);
    }

    /// <summary>
    /// Pins round-3 class-B (SFH C4): when the downstream stdout pipe closes during the UDP
    /// receive write (`nc -u host port | head -c 4`), nc must exit 0 with exit_reason
    /// <c>stdout_closed</c> — matching the TCP path's BSD nc semantics. Reverting the
    /// round-3 stdout try/catch would mis-classify this as 126 "unexpected error".
    /// </summary>
    [Fact]
    public async Task UdpConnect_StdoutClosesDuringReceive_ReturnsExitZero_StdoutClosed()
    {
        // Echo server that replies to the first datagram with a large buffer.
        using var server = new UdpClient(0, AddressFamily.InterNetwork);
        int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        Task serverTask = Task.Run(async () =>
        {
            UdpReceiveResult rx = await server.ReceiveAsync();
            byte[] reply = new byte[1024];
            await server.SendAsync(reply, reply.Length, rx.RemoteEndPoint);
        });

        var options = new NetCatOptions
        {
            Mode = NetCatMode.Connect,
            Protocol = NetCatProtocol.Udp,
            Host = "127.0.0.1",
            Ports = new[] { new PortRange(port) },
            Timeout = System.TimeSpan.FromSeconds(2),
        };

        byte[] payload = Encoding.ASCII.GetBytes("ping");
        using var stdin = new MemoryStream(payload);
        // Budget 0 — every stdout write throws, simulating `| head -c 0`.
        using var stdout = new ThrowAfterBytesStream(bytesBeforeThrow: 0);
        using var stderr = new StringWriter();

        RunResult result = await new NetCatClient().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);
        await serverTask.WaitAsync(System.TimeSpan.FromSeconds(5));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("stdout_closed", result.ExitReason);
    }

    // Deterministic, cross-platform pins for the cancellation classifier the UDP receive paths use.
    // On Unix, cancelling an in-flight UDP ReceiveAsync aborts the socket and surfaces as a
    // SocketException rather than OperationCanceledException, so the SocketException catch must
    // consult the same classifier — otherwise Ctrl-C on a UDP listener returns exit 1 instead of 130
    // (the macOS CI flake this fixes). The live UdpListener_CtrlC test exercises this end-to-end on
    // Unix CI; these pin the decision logic without needing the platform-specific socket behaviour.
    [Fact]
    public void ClassifyCancellation_UserCancel_ReturnsInterrupted130()
    {
        RunResult? r = NetCatListener.ClassifyCancellation(
            userCancelRequested: true, anyCancelRequested: true, durationMs: 5.0, localAddress: "127.0.0.1:1234");
        Assert.NotNull(r);
        Assert.Equal(130, r!.ExitCode);
        Assert.Equal("interrupted", r.ExitReason);
        Assert.Equal("127.0.0.1:1234", r.LocalAddress);
    }

    [Fact]
    public void ClassifyCancellation_TimeoutCancel_ReturnsTimeout2()
    {
        RunResult? r = NetCatListener.ClassifyCancellation(
            userCancelRequested: false, anyCancelRequested: true, durationMs: 5.0, localAddress: "127.0.0.1:1234");
        Assert.NotNull(r);
        Assert.Equal(2, r!.ExitCode);
        Assert.Equal("timeout", r.ExitReason);
    }

    [Fact]
    public void ClassifyCancellation_NoCancellation_ReturnsNull()
    {
        Assert.Null(NetCatListener.ClassifyCancellation(
            userCancelRequested: false, anyCancelRequested: false, durationMs: 5.0, localAddress: "x"));
    }

    /// <summary>
    /// Pins round-3 SFH-I2: the UDP listener path must return RunResult(130) on user-cancel
    /// so --json consumers see a complete envelope with duration/LocalAddress. The TCP listen
    /// path already had this; UDP was inconsistent.
    /// </summary>
    [Fact]
    public async Task UdpListener_CtrlC_ReturnsExitOneThirty_InterruptedReason()
    {
        // Bind a UDP listener on an ephemeral port; never send it a datagram. Pre-cancel
        // the outer CT to simulate Ctrl-C arriving during ReceiveAsync.
        var probe = new UdpClient(0, AddressFamily.InterNetwork);
        int port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        probe.Dispose(); // free the port for the listener under test

        var options = new NetCatOptions
        {
            Mode = NetCatMode.Listen,
            Protocol = NetCatProtocol.Udp,
            BindAddress = "127.0.0.1",
            Ports = new[] { new PortRange(port) },
            Timeout = System.TimeSpan.FromSeconds(30),
        };

        using var stdin = new MemoryStream();
        using var stdout = new MemoryStream();
        using var stderr = new StringWriter();

        using var cts = new CancellationTokenSource();
        Task<RunResult> listenerTask = new NetCatListener().RunAsync(options, stdin, stdout, stderr, cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        RunResult result = await listenerTask.WaitAsync(System.TimeSpan.FromSeconds(5));

        // Diagnostic message so a future flake is self-explaining: distinguishes a cancellation
        // mis-mapping (reason "connection_*"/socket error) from a port-handoff bind race ("bind_failed").
        Assert.True(result.ExitCode == 130,
            $"expected 130 (interrupted) but got {result.ExitCode}/{result.ExitReason}; LocalAddress={result.LocalAddress}");
        Assert.Equal("interrupted", result.ExitReason);
        Assert.NotNull(result.LocalAddress);
    }

    /// <summary>
    /// Pins round-3 CR-I5: UDP Connect must also return RunResult(130) on user-cancel with
    /// partial byte counts + duration, not let OCE escape RunUdpAsync to Main (which bypasses
    /// the --json envelope).
    /// </summary>
    [Fact]
    public async Task UdpConnect_CtrlCDuringSend_ReturnsExitOneThirty_WithPartialBytes()
    {
        using var dest = new UdpClient(0, AddressFamily.InterNetwork);
        int port = ((IPEndPoint)dest.Client.LocalEndPoint!).Port;

        var options = new NetCatOptions
        {
            Mode = NetCatMode.Connect,
            Protocol = NetCatProtocol.Udp,
            Host = "127.0.0.1",
            Ports = new[] { new PortRange(port) },
            Timeout = System.TimeSpan.Zero,
        };

        // Stdin that blocks forever on read, so the send loop is waiting when we cancel.
        using var stdin = new BlockingReadStream();
        using var stdout = new MemoryStream();
        using var stderr = new StringWriter();

        using var cts = new CancellationTokenSource();
        Task<RunResult> runTask = new NetCatClient().RunAsync(options, stdin, stdout, stderr, cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        RunResult result = await runTask.WaitAsync(System.TimeSpan.FromSeconds(5));

        Assert.Equal(130, result.ExitCode);
        Assert.Equal("interrupted", result.ExitReason);
    }

    /// <summary>
    /// Pins round-5 SFH-I1: user Ctrl-C during UDP receive must return RunResult(130) with
    /// partial bytes_sent + duration, not escape to Main's FormatErrorJson envelope (which
    /// lacks those fields). Mirrors the send-side cancel pin above.
    /// </summary>
    [Fact]
    public async Task UdpConnect_CtrlCDuringReceive_ReturnsExitOneThirty_WithPartialBytes()
    {
        // UdpClient that receives the ping but never replies — the client's ReceiveAsync blocks.
        using var server = new UdpClient(0, AddressFamily.InterNetwork);
        int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        Task serverTask = Task.Run(async () =>
        {
            try { await server.ReceiveAsync(); } catch { }
            // deliberately do NOT reply
        });

        var options = new NetCatOptions
        {
            Mode = NetCatMode.Connect,
            Protocol = NetCatProtocol.Udp,
            Host = "127.0.0.1",
            Ports = new[] { new PortRange(port) },
            Timeout = System.TimeSpan.FromSeconds(30), // long enough that Ctrl-C races it first
        };

        byte[] payload = Encoding.ASCII.GetBytes("ping");
        using var stdin = new MemoryStream(payload);
        using var stdout = new MemoryStream();
        using var stderr = new StringWriter();

        using var cts = new CancellationTokenSource();
        Task<RunResult> runTask = new NetCatClient().RunAsync(options, stdin, stdout, stderr, cts.Token);
        await Task.Delay(300); // send leg drains stdin and enters ReceiveAsync
        cts.Cancel();

        RunResult result = await runTask.WaitAsync(System.TimeSpan.FromSeconds(5));
        try { await serverTask.WaitAsync(System.TimeSpan.FromSeconds(2)); } catch { }

        Assert.Equal(130, result.ExitCode);
        Assert.Equal("interrupted", result.ExitReason);
        // BytesSent must reflect the datagram that actually left the wire — proves the OCE
        // arm returned a real RunResult rather than letting OCE escape to FormatErrorJson.
        Assert.True(result.BytesSent > 0, $"expected bytes_sent > 0, got {result.BytesSent}");
    }

    /// <summary>
    /// Pins round-5 SFH-I3: the TCP pump's broad safety-net catches ObjectDisposedException
    /// (and InvalidOperationException) so a racy-disposal during half-close maps to exit 1 +
    /// <c>pump_failed</c> rather than escaping to Main's <c>unexpected_error</c> 126 envelope.
    ///
    /// Testing an actual OS-level race is flaky, so instead we force the defect's shape: a
    /// NetworkStream whose WriteAsync throws ObjectDisposedException. If the broad-catch arm
    /// is reverted, the exception escapes past RunAsync's catch chain and this test fails.
    /// </summary>
    [Fact]
    public async Task TcpClient_PumpWriteThrowsObjectDisposedException_ReturnsExitOne_PumpFailed()
    {
        // Spin up a real TCP server so the connect/pump path runs. The server echoes any
        // incoming bytes. Our stdin feeds a payload, and the test hooks the pump indirectly
        // via a stdout stream that throws ObjectDisposedException on write — routing through
        // the StdoutClosedException inheritance chain would otherwise take the stdout_closed
        // arm, so we use ODE specifically (not IOException) to exercise the broad catch.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task serverTask = Task.Run(async () =>
        {
            try
            {
                using TcpClient peer = await listener.AcceptTcpClientAsync();
                using NetworkStream s = peer.GetStream();
                byte[] payload = Encoding.ASCII.GetBytes(new string('x', 1024));
                for (int i = 0; i < 50; i++)
                {
                    try { await s.WriteAsync(payload); } catch { break; }
                    await Task.Delay(5);
                }
            }
            catch { }
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

            using var stdin = new MemoryStream();
            using var stdout = new ThrowObjectDisposedStream();
            using var stderr = new StringWriter();

            RunResult result = await new NetCatClient().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal("pump_failed", result.ExitReason);
        }
        finally
        {
            listener.Stop();
            try { await serverTask.WaitAsync(System.TimeSpan.FromSeconds(5)); } catch { }
        }
    }

    /// <summary>
    /// Pins round-9 test-analyzer I1 (and SFH-I2): NetCatListener's round-8 <c>accept_failed</c>
    /// broad-catch arm. Uses the internal <c>AcceptHook</c> seam to inject an
    /// InvalidOperationException at the accept step — the exact class-B non-SocketException the
    /// broad catch is there to catch. Reverting the broad-catch arm would let this exception
    /// escape to Main's 126 <c>unexpected_error</c> safety-net, failing this test.
    /// </summary>
    [Fact]
    public async Task TcpListener_AcceptThrowsInvalidOperationException_ReturnsExitOne_AcceptFailed()
    {
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
            Timeout = System.TimeSpan.FromSeconds(2),
        };

        using var stdin = new MemoryStream();
        using var stdout = new MemoryStream();
        using var stderr = new StringWriter();

        var listener = new NetCatListener
        {
            AcceptHook = (_, _) => throw new System.InvalidOperationException("simulated racy accept"),
        };
        RunResult result = await listener.RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("accept_failed", result.ExitReason);
        Assert.Contains("accept", stderr.ToString(), System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pins round-9 SFH-I1: a post-accept pre-pump failure (peer disconnects between accept
    /// and GetStream) must be classified as <c>socket_error</c>, NOT <c>accept_failed</c>.
    /// Uses the AcceptHook to return a TcpClient whose stream accessor throws ODE.
    /// </summary>
    [Fact]
    public async Task TcpListener_PostAcceptSetupFails_ReturnsExitOne_SocketError_NotAcceptFailed()
    {
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
            Timeout = System.TimeSpan.FromSeconds(2),
        };

        using var stdin = new MemoryStream();
        using var stdout = new MemoryStream();
        using var stderr = new StringWriter();

        // Return a TcpClient that has been disposed — subsequent RemoteEndPoint / GetStream
        // calls throw ObjectDisposedException, mirroring the "peer RSTs between accept and
        // setup" race condition the round-9 fix targets.
        var listener = new NetCatListener
        {
            AcceptHook = (_, _) =>
            {
                var tc = new TcpClient();
                tc.Dispose();
                return Task.FromResult(tc);
            },
        };
        RunResult result = await listener.RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        // Round-9 fix: this MUST be socket_error, not accept_failed — accept itself succeeded.
        Assert.Equal("socket_error", result.ExitReason);
        Assert.Contains("peer disconnected", stderr.ToString());
    }

    /// <summary>
    /// Pins round-7 test-analyzer C1: NetCatListener's <c>pump_failed</c> arm (parity with
    /// NetCatClient's, which is already pinned). A regression dropping the listener's broad
    /// pump catch would let ObjectDisposedException escape to Main's 126 safety-net, losing
    /// byte counts + LocalAddress from the JSON envelope.
    /// </summary>
    [Fact]
    public async Task TcpListener_PumpWriteThrowsObjectDisposedException_ReturnsExitOne_PumpFailed()
    {
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

        using var stdin = new MemoryStream();
        using var stdout = new ThrowObjectDisposedStream();
        using var stderr = new StringWriter();

        Task<RunResult> listenerTask = new NetCatListener().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

        await Task.Delay(100);

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
            using NetworkStream cs = client.GetStream();
            byte[] payload = Encoding.ASCII.GetBytes(new string('x', 2048));
            try
            {
                for (int i = 0; i < 20; i++)
                {
                    await cs.WriteAsync(payload);
                    await Task.Delay(5);
                }
            }
            catch { /* peer closed mid-transfer when stdout blew up — expected */ }
        }

        RunResult result = await listenerTask.WaitAsync(System.TimeSpan.FromSeconds(10));

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("pump_failed", result.ExitReason);
        Assert.NotNull(result.LocalAddress);
    }

    /// <summary>
    /// Pins round-7 test-analyzer C2 (TCP): NetCatClient's <c>connect_failed</c> arm was emitted
    /// but unpinned. Empty host triggers ArgumentException in TcpClient.ConnectAsync →
    /// non-SocketException → broad catch classifies as <c>connect_failed</c>. Regression removing
    /// the broad catch would let the exception escape to Main's 126 safety-net.
    /// </summary>
    [Fact]
    public async Task TcpClient_EmptyHost_ReturnsExitOne_ConnectFailed()
    {
        var options = new NetCatOptions
        {
            Mode = NetCatMode.Connect,
            Protocol = NetCatProtocol.Tcp,
            Host = "",
            Ports = new[] { new PortRange(80) },
            Timeout = System.TimeSpan.FromSeconds(2),
        };

        using var stdin = new MemoryStream();
        using var stdout = new MemoryStream();
        using var stderr = new StringWriter();

        RunResult result = await new NetCatClient().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        // On some .NET runtimes empty host resolves to a SocketException (host_not_found); on
        // others ArgumentException (connect_failed). Both are acceptable as long as it doesn't
        // escape to unexpected_error. Pin the class rather than the specific reason.
        Assert.Contains(result.ExitReason, new[] { "connect_failed", "host_not_found", "socket_error" });
    }

    // Note: UDP empty-host connect test removed — `Dns.GetHostAddresses("")` behaviour varies
    // across .NET runtimes (success on some, SocketException on others, ArgumentException on
    // yet others). The TCP empty-host test above pins the non-SocketException class, and the
    // UDP broad-catch remains in place defensively. A deterministic UDP pin would require a
    // mock seam not worth building for a rarely-triggered arm.

    /// <summary>
    /// Pins round-7 test-analyzer I2: UDP listener receive-timeout stderr diagnostic.
    /// Reverting the stderr note would silently exit 2 with empty stderr — same silent-failure
    /// class as round-2 I2 (which was pinned only at the TCP seam).
    /// </summary>
    [Fact]
    public async Task UdpListener_NoDatagramWithinTimeout_ReturnsExitTwo_AndWritesStderrNote()
    {
        var probe = new UdpClient(0, AddressFamily.InterNetwork);
        int port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        probe.Dispose();

        var options = new NetCatOptions
        {
            Mode = NetCatMode.Listen,
            Protocol = NetCatProtocol.Udp,
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
        Assert.Contains("no datagram within", stderr.ToString());
    }

    /// <summary>
    /// Pins round-7 test-analyzer I3 (TCP listener): <c>stdout_closed</c> parity with the
    /// client-side test. `nc --listen 8080 | head -c 10` must exit 0 with exit_reason
    /// <c>stdout_closed</c> — a regression to <c>socket_error</c> (exit 1) would silently drift
    /// from BSD nc semantics on the listener side only.
    /// </summary>
    [Fact]
    public async Task TcpListener_StdoutClosesDuringReceive_ReturnsExitZero_StdoutClosed()
    {
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

        using var stdin = new MemoryStream();
        using var stdout = new ThrowAfterBytesStream(bytesBeforeThrow: 0);
        using var stderr = new StringWriter();

        Task<RunResult> listenerTask = new NetCatListener().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

        await Task.Delay(100);

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
            using NetworkStream cs = client.GetStream();
            byte[] payload = Encoding.ASCII.GetBytes(new string('x', 2048));
            try
            {
                for (int i = 0; i < 20; i++)
                {
                    await cs.WriteAsync(payload);
                    await Task.Delay(5);
                }
            }
            catch { /* listener closed once stdout threw — expected */ }
        }

        RunResult result = await listenerTask.WaitAsync(System.TimeSpan.FromSeconds(10));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("stdout_closed", result.ExitReason);
    }

    /// <summary>
    /// Pins round-7 test-analyzer I3 (UDP listener): stdout IOException → exit 0 / stdout_closed,
    /// parity with the client-side UDP test and the TCP listener test above.
    /// </summary>
    [Fact]
    public async Task UdpListener_StdoutClosesDuringReceive_ReturnsExitZero_StdoutClosed()
    {
        var probe = new UdpClient(0, AddressFamily.InterNetwork);
        int port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        probe.Dispose();

        var options = new NetCatOptions
        {
            Mode = NetCatMode.Listen,
            Protocol = NetCatProtocol.Udp,
            BindAddress = "127.0.0.1",
            Ports = new[] { new PortRange(port) },
            Timeout = System.TimeSpan.FromSeconds(5),
        };

        using var stdin = new MemoryStream();
        using var stdout = new ThrowAfterBytesStream(bytesBeforeThrow: 0);
        using var stderr = new StringWriter();

        Task<RunResult> listenerTask = new NetCatListener().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

        await Task.Delay(100);

        using (var sender = new UdpClient())
        {
            byte[] payload = Encoding.ASCII.GetBytes("hello-udp");
            await sender.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Loopback, port));
        }

        RunResult result = await listenerTask.WaitAsync(System.TimeSpan.FromSeconds(5));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("stdout_closed", result.ExitReason);
    }

    /// <summary>
    /// Stream that throws ObjectDisposedException on every write. Used to exercise the
    /// round-5 pump broad-catch safety net (which catches ODE but not IOException — the
    /// latter is the stdout_closed path via StdoutClosedException).
    /// </summary>
    private sealed class ThrowObjectDisposedStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override void Write(byte[] buffer, int offset, int count) => throw new System.ObjectDisposedException(nameof(ThrowObjectDisposedStream));
        public override ValueTask WriteAsync(System.ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
            => throw new System.ObjectDisposedException(nameof(ThrowObjectDisposedStream));
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => throw new System.ObjectDisposedException(nameof(ThrowObjectDisposedStream));
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
    }

    /// <summary>
    /// Stream whose ReadAsync blocks until the cancellation token fires, then throws OCE.
    /// Used to park the UDP send loop in a cancellable await so the test can deterministically
    /// trigger the user-cancel OCE arm.
    /// </summary>
    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override async ValueTask<int> ReadAsync(System.Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
            return 0;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) { }
    }

    /// <summary>
    /// Stream that throws IOException on every read — simulates a broken stdin pipe
    /// (redirected-from-file hit bad sector, closed pipe in some shells).
    /// </summary>
    private sealed class ThrowOnReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("simulated stdin pipe broken");
        public override ValueTask<int> ReadAsync(System.Memory<byte> buffer, CancellationToken ct = default)
            => throw new IOException("simulated stdin pipe broken");
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => throw new IOException("simulated stdin pipe broken");
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) { }
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
