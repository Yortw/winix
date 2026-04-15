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
            // UDP path is implemented in a later task; throw to fail fast in this commit's tests.
            throw new System.NotImplementedException("UDP client is implemented in Phase 6.");
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
            using NetworkStream stream = tcp.GetStream();

            var pump = new RelayPump();
            try
            {
                await pump.RunAsync(stream, stream, stdin, stdout, halfCloseOnStdinEof: !options.NoShutdown, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new RunResult { ExitCode = 130, ExitReason = "interrupted",
                    BytesSent = pump.BytesSent, BytesReceived = pump.BytesReceived,
                    DurationMilliseconds = sw.Elapsed.TotalMilliseconds, RemoteAddress = remote };
            }

            if (pump.ShouldShutdownSend)
            {
                try { tcp.Client.Shutdown(SocketShutdown.Send); } catch (SocketException) { /* peer already gone */ }
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
