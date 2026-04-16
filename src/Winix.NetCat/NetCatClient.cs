#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.NetCat;

/// <summary>
/// Outbound (Connect-mode) runner. Opens a TCP/UDP connection to the target
/// host:port and pumps bytes between the socket and stdin/stdout via <see cref="RelayPump"/>.
/// </summary>
public sealed class NetCatClient
{
    /// <summary>
    /// Opens the connection and runs both relay directions until the peer closes,
    /// stdin reaches EOF (with optional half-close), or cancellation fires.
    /// </summary>
    public async Task<RunResult> RunAsync(
        NetCatOptions options,
        Stream stdin,
        Stream stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        if (options.Protocol == NetCatProtocol.Udp)
        {
            return await RunUdpAsync(options, stdin, stdout, stderr, ct).ConfigureAwait(false);
        }

        var sw = Stopwatch.StartNew();
        if (options.Host is null) { throw new System.ArgumentException("Host is required for Connect mode.", nameof(options)); }
        int port = options.Ports[0].Low;

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (options.Timeout > TimeSpan.Zero)
        {
            connectCts.CancelAfter(options.Timeout);
        }

        TcpClient tcp;
        try
        {
            tcp = new TcpClient();
            await tcp.ConnectAsync(options.Host, port, connectCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stderr.WriteLine(Formatting.FormatErrorLine($"{options.Host}:{port} — connection timed out", options.UseColor));
            return new RunResult { ExitCode = 2, ExitReason = "timeout", DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
        }
        catch (SocketException ex)
        {
            stderr.WriteLine(Formatting.FormatErrorLine($"{options.Host}:{port} — {ex.Message}", options.UseColor));
            return new RunResult { ExitCode = 1, ExitReason = MapSocketError(ex), DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
        }

        using (tcp)
        {
            string remote = tcp.Client.RemoteEndPoint?.ToString() ?? "";

            Stream stream;
            if (options.UseTls)
            {
                if (options.InsecureTls)
                {
                    stderr.WriteLine(Formatting.FormatWarningLine("TLS certificate validation disabled", options.UseColor));
                }
                try
                {
                    stream = await TlsWrapper.WrapClientAsync(tcp.GetStream(), options.Host!, options.InsecureTls, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    stderr.WriteLine(Formatting.FormatErrorLine($"TLS — handshake failed: {ex.Message}", options.UseColor));
                    return new RunResult { ExitCode = 1, ExitReason = "tls_failed", DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
                }
            }
            else
            {
                stream = tcp.GetStream();
            }

            using (stream)
            {
                var pump = new RelayPump();
                // Capture tcp into a local for the closure (avoids capturing `this`-style surprises).
                TcpClient capturedTcp = tcp;
                Func<Task> onSendComplete = () =>
                {
                    // Close the write half of the socket so the peer sees EOF and can respond.
                    // Must fire BEFORE awaiting the receive direction — otherwise request/response
                    // protocols deadlock (peer waits for our EOF before sending their reply).
                    try
                    {
                        capturedTcp.Client.Shutdown(SocketShutdown.Send);
                    }
                    catch (SocketException)
                    {
                        // Peer already gone — harmless.
                    }
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
                        DurationMilliseconds = sw.Elapsed.TotalMilliseconds, RemoteAddress = remote };
                }

                sw.Stop();
                return new RunResult
                {
                    ExitCode = 0,
                    ExitReason = "success",
                    BytesSent = pump.BytesSent,
                    BytesReceived = pump.BytesReceived,
                    DurationMilliseconds = sw.Elapsed.TotalMilliseconds,
                    RemoteAddress = remote,
                };
            }
        }
    }

    private static async Task<RunResult> RunUdpAsync(NetCatOptions options, Stream stdin, Stream stdout, TextWriter stderr, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (options.Host is null) { throw new ArgumentException("Host is required for Connect mode.", nameof(options)); }
        int port = options.Ports[0].Low;

        using var udp = new UdpClient(0, options.AddressFamily ?? AddressFamily.InterNetwork);
        udp.Connect(options.Host, port);

        // Read all of stdin and send each buffer as a datagram.
        long sent = 0;
        var buf = new byte[65507];
        while (true)
        {
            int n = await stdin.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false);
            if (n == 0) { break; }
            int chunkSent = await udp.SendAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
            sent += chunkSent;
        }

        // Optionally wait briefly for a single response (timeout > 0 = wait).
        long received = 0;
        if (options.Timeout > TimeSpan.Zero)
        {
            using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            receiveCts.CancelAfter(options.Timeout);
            try
            {
                UdpReceiveResult rx = await udp.ReceiveAsync(receiveCts.Token).ConfigureAwait(false);
                await stdout.WriteAsync(rx.Buffer.AsMemory(), ct).ConfigureAwait(false);
                received = rx.Buffer.Length;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // No response within timeout — exit cleanly anyway.
            }
        }

        sw.Stop();
        return new RunResult
        {
            ExitCode = 0,
            ExitReason = "success",
            BytesSent = sent,
            BytesReceived = received,
            DurationMilliseconds = sw.Elapsed.TotalMilliseconds,
        };
    }

    private static string MapSocketError(SocketException ex) => ex.SocketErrorCode switch
    {
        SocketError.ConnectionRefused => "connection_refused",
        SocketError.HostNotFound => "host_not_found",
        SocketError.HostUnreachable => "host_unreachable",
        SocketError.NetworkUnreachable => "network_unreachable",
        SocketError.TimedOut => "timeout",
        _ => "socket_error",
    };
}
