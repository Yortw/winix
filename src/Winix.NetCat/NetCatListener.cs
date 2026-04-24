#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.NetCat;

/// <summary>
/// Inbound (Listen-mode) runner. Binds a TCP/UDP socket, accepts a single
/// client/datagram, then relays bytes between the socket and stdin/stdout.
/// </summary>
public sealed class NetCatListener
{
    /// <summary>Runs the listener for one connection (TCP) or one datagram (UDP).</summary>
    public async Task<RunResult> RunAsync(NetCatOptions options, Stream stdin, Stream stdout, TextWriter stderr, CancellationToken ct)
    {
        if (options.Protocol == NetCatProtocol.Udp)
        {
            return await RunUdpAsync(options, stdout, stderr, ct).ConfigureAwait(false);
        }

        return await RunTcpAsync(options, stdin, stdout, stderr, ct).ConfigureAwait(false);
    }

    private static async Task<RunResult> RunTcpAsync(NetCatOptions options, Stream stdin, Stream stdout, TextWriter stderr, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        IPAddress bind = ResolveBind(options);
        int port = options.Ports[0].Low;

        var listener = new TcpListener(bind, port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            stderr.WriteLine(Formatting.FormatErrorLine($"cannot bind {bind}:{port} — {ex.Message}", options.UseColor));
            int exitCode = ex.SocketErrorCode == SocketError.AccessDenied ? 126 : 1;
            return new RunResult { ExitCode = exitCode, ExitReason = "bind_failed", DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
        }

        try
        {
            using var acceptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (options.Timeout > TimeSpan.Zero)
            {
                acceptCts.CancelAfter(options.Timeout);
            }

            using TcpClient client = await listener.AcceptTcpClientAsync(acceptCts.Token).ConfigureAwait(false);
            string remote = client.Client.RemoteEndPoint?.ToString() ?? "";
            string local = client.Client.LocalEndPoint?.ToString() ?? "";
            using NetworkStream stream = client.GetStream();

            var pump = new RelayPump();
            // See NetCatClient for why the shutdown must fire from the pump's
            // onSendComplete hook rather than after RunAsync returns: peers that
            // wait for our EOF before responding would otherwise deadlock us.
            TcpClient capturedClient = client;
            Func<Task> onSendComplete = () =>
            {
                // Half-close is cosmetic — any failure here must not mask the primary outcome.
                // Broad catch mirrors the client-side fix (round-3 SFH I4): catching only
                // SocketException/ObjectDisposedException lets InvalidOperationException
                // (racy socket state) or IOException escape, which RelayPump then surfaces as
                // a pump failure — silently mis-classifying a successful listen as socket_error.
                try { capturedClient.Client.Shutdown(SocketShutdown.Send); }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException) { }
                return Task.CompletedTask;
            };

            try
            {
                await pump.RunAsync(stream, stream, stdin, stdout, halfCloseOnStdinEof: !options.NoShutdown, ct, onSendComplete).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new RunResult { ExitCode = 130, ExitReason = "interrupted",
                    BytesSent = pump.BytesSent, BytesReceived = pump.BytesReceived,
                    DurationMilliseconds = sw.Elapsed.TotalMilliseconds, LocalAddress = local };
            }
            catch (StdoutClosedException)
            {
                // Round-2 I1: downstream pipe closed — same semantic as NetCatClient. Exit 0.
                sw.Stop();
                return new RunResult { ExitCode = 0, ExitReason = "stdout_closed",
                    BytesSent = pump.BytesSent, BytesReceived = pump.BytesReceived,
                    DurationMilliseconds = sw.Elapsed.TotalMilliseconds, LocalAddress = local, RemoteAddress = remote };
            }
            catch (IOException ex)
            {
                // Post-connect transport failure — peer RST mid-transfer, TLS-alert after
                // handshake, etc. Matches NetCatClient's symmetric arm so exit codes line up.
                sw.Stop();
                stderr.WriteLine(Formatting.FormatErrorLine($"{remote} — {ex.Message}", options.UseColor));
                return new RunResult { ExitCode = 1, ExitReason = "socket_error",
                    BytesSent = pump.BytesSent, BytesReceived = pump.BytesReceived,
                    DurationMilliseconds = sw.Elapsed.TotalMilliseconds, LocalAddress = local, RemoteAddress = remote };
            }
            catch (SocketException ex)
            {
                sw.Stop();
                stderr.WriteLine(Formatting.FormatErrorLine($"{remote} — {ex.Message}", options.UseColor));
                return new RunResult { ExitCode = 1, ExitReason = NetCatClient.MapSocketError(ex),
                    BytesSent = pump.BytesSent, BytesReceived = pump.BytesReceived,
                    DurationMilliseconds = sw.Elapsed.TotalMilliseconds, LocalAddress = local, RemoteAddress = remote };
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException)
            {
                // Round-5 SFH-I3 (listener parity): same defensive broad catch as NetCatClient's
                // pump block — ObjectDisposedException / InvalidOperationException from racy
                // socket state during half-close can no longer escape to Main's 126 safety-net
                // (which would lose byte counts from the JSON envelope).
                sw.Stop();
                stderr.WriteLine(Formatting.FormatErrorLine($"{remote} — {ex.Message}", options.UseColor));
                return new RunResult { ExitCode = 1, ExitReason = "pump_failed",
                    BytesSent = pump.BytesSent, BytesReceived = pump.BytesReceived,
                    DurationMilliseconds = sw.Elapsed.TotalMilliseconds, LocalAddress = local, RemoteAddress = remote };
            }

            sw.Stop();
            return new RunResult
            {
                ExitCode = 0,
                ExitReason = "success",
                BytesSent = pump.BytesSent,
                BytesReceived = pump.BytesReceived,
                DurationMilliseconds = sw.Elapsed.TotalMilliseconds,
                LocalAddress = local,
                RemoteAddress = remote,
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Round-2 I2: emit a diagnostic stderr line. Without this, `nc --listen -w 5 8080`
            // with no client exited 2 with empty stderr — same silent-failure pattern as
            // check-mode all-failed (round-1 I-5).
            stderr.WriteLine(Formatting.FormatErrorLine(
                $"listen {bind}:{port} — no client within {options.Timeout.TotalSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}s",
                options.UseColor));
            sw.Stop();
            return new RunResult { ExitCode = 2, ExitReason = "timeout", DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
        }
        catch (SocketException ex)
        {
            // Accept can surface SocketException on some Linux paths (interface flap, fd limit).
            // Without this, the exception escapes the try/finally to Main → exit 126 "unexpected
            // error" with no JSON envelope. Round-3 I (SFH I1) fix.
            sw.Stop();
            stderr.WriteLine(Formatting.FormatErrorLine($"accept {bind}:{port} — {ex.Message}", options.UseColor));
            return new RunResult { ExitCode = 1, ExitReason = NetCatClient.MapSocketError(ex), DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException)
        {
            // Round-7 SFH I-2: NetCatClient's connect path got a broad safety-net in round 3
            // (→ connect_failed). The listener's outer accept block was left without the
            // parallel arm — any ObjectDisposedException / InvalidOperationException /
            // ArgumentException from a racy AcceptTcpClientAsync escaped the try/finally all
            // the way to Main's 126 "unexpected_error" safety-net, losing the JSON envelope.
            // Mirror the client's treatment here for symmetric class-B coverage.
            sw.Stop();
            string msg = string.IsNullOrEmpty(ex.Message) ? ex.GetType().Name : ex.Message;
            stderr.WriteLine(Formatting.FormatErrorLine($"accept {bind}:{port} — {msg}", options.UseColor));
            return new RunResult { ExitCode = 1, ExitReason = "accept_failed", DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<RunResult> RunUdpAsync(NetCatOptions options, Stream stdout, TextWriter stderr, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        IPAddress bind = ResolveBind(options);
        int port = options.Ports[0].Low;

        UdpClient udp;
        try
        {
            udp = new UdpClient(new IPEndPoint(bind, port));
        }
        catch (SocketException ex)
        {
            stderr.WriteLine(Formatting.FormatErrorLine($"cannot bind {bind}:{port} — {ex.Message}", options.UseColor));
            int exitCode = ex.SocketErrorCode == SocketError.AccessDenied ? 126 : 1;
            return new RunResult { ExitCode = exitCode, ExitReason = "bind_failed", DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
        }

        using (udp)
        {
            using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (options.Timeout > TimeSpan.Zero) { receiveCts.CancelAfter(options.Timeout); }

            UdpReceiveResult rx;
            try
            {
                rx = await udp.ReceiveAsync(receiveCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Round-2 I2: emit a diagnostic stderr line for UDP listener timeout too.
                stderr.WriteLine(Formatting.FormatErrorLine(
                    $"listen {bind}:{port} — no datagram within {options.Timeout.TotalSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}s",
                    options.UseColor));
                sw.Stop();
                return new RunResult { ExitCode = 2, ExitReason = "timeout", DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
            }
            catch (OperationCanceledException)
            {
                // User-cancel (Ctrl-C) during UDP receive. The TCP listen path already returns
                // a RunResult(130) inside its pump try; the UDP path was missing this parity,
                // so Ctrl-C fell through to Main's OCE arm with no byte counts / duration and
                // --json mode never emitted an envelope. Round-3 I (SFH I2) fix.
                sw.Stop();
                return new RunResult { ExitCode = 130, ExitReason = "interrupted",
                    DurationMilliseconds = sw.Elapsed.TotalMilliseconds, LocalAddress = $"{bind}:{port}" };
            }
            catch (SocketException ex)
            {
                sw.Stop();
                stderr.WriteLine(Formatting.FormatErrorLine($"listen {bind}:{port} — {ex.Message}", options.UseColor));
                return new RunResult { ExitCode = 1, ExitReason = NetCatClient.MapSocketError(ex),
                    DurationMilliseconds = sw.Elapsed.TotalMilliseconds, LocalAddress = $"{bind}:{port}" };
            }

            try
            {
                await stdout.WriteAsync(rx.Buffer.AsMemory(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Round-5 SFH-I2 (listener): user Ctrl-C during the stdout write after a datagram
                // was received. Preserve BytesReceived + RemoteAddress so the envelope shows the
                // datagram arrived — failure was only the downstream write.
                sw.Stop();
                return new RunResult { ExitCode = 130, ExitReason = "interrupted",
                    BytesReceived = rx.Buffer.Length, DurationMilliseconds = sw.Elapsed.TotalMilliseconds,
                    LocalAddress = $"{bind}:{port}", RemoteAddress = rx.RemoteEndPoint.ToString() };
            }
            catch (IOException)
            {
                // Downstream pipe closed. Treat as exit 0/stdout_closed — same BSD nc semantic
                // as the client path. Round-3 I fix (parity with SFH C4).
                sw.Stop();
                return new RunResult { ExitCode = 0, ExitReason = "stdout_closed",
                    BytesReceived = rx.Buffer.Length, DurationMilliseconds = sw.Elapsed.TotalMilliseconds,
                    LocalAddress = $"{bind}:{port}", RemoteAddress = rx.RemoteEndPoint.ToString() };
            }
            sw.Stop();
            return new RunResult
            {
                ExitCode = 0,
                ExitReason = "success",
                BytesReceived = rx.Buffer.Length,
                DurationMilliseconds = sw.Elapsed.TotalMilliseconds,
                LocalAddress = $"{bind}:{port}",
                RemoteAddress = rx.RemoteEndPoint.ToString(),
            };
        }
    }

    /// <summary>
    /// Resolves the bind address for the listener. <paramref name="options"/>.<see cref="NetCatOptions.BindAddress"/>
    /// must already be validated as a parseable IP by <c>Program.BuildOptions</c> — a bad string
    /// SHOULD have been rejected as a usage error there, not silently fall through to
    /// <c>IPAddress.Any</c> (which would defeat the security intent of <c>--bind</c>). If an
    /// unparseable string reaches this method, throw rather than silently bind everywhere.
    /// </summary>
    private static IPAddress ResolveBind(NetCatOptions options)
    {
        if (options.BindAddress is not null)
        {
            if (!IPAddress.TryParse(options.BindAddress, out IPAddress? parsed))
            {
                // Defence-in-depth: upstream validation should have caught this. Throw rather
                // than fall through to IPAddress.Any — silent-fallback was the whole bug.
                throw new InvalidOperationException(
                    $"BindAddress '{options.BindAddress}' is not a valid IP — validation in Program.BuildOptions should have rejected this earlier.");
            }
            return parsed;
        }
        return options.AddressFamily == AddressFamily.InterNetworkV6
            ? IPAddress.IPv6Any
            : IPAddress.Any;
    }
}
