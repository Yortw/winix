#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Yort.ShellKit;

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
            if (options.AddressFamily is AddressFamily af)
            {
                // Honour --ipv4 / --ipv6 by resolving ourselves and filtering to the chosen
                // family. The default TcpClient() + ConnectAsync(string,int) path lets the
                // OS resolver pick dual-stack — silently defeating the flag. Round-3 C1 fix.
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(options.Host, af, connectCts.Token).ConfigureAwait(false);
                if (addresses.Length == 0)
                {
                    string family = af == AddressFamily.InterNetwork ? "IPv4" : "IPv6";
                    stderr.WriteLine(Formatting.FormatErrorLine($"{options.Host}:{port} — no {family} address for host", options.UseColor));
                    return new RunResult { ExitCode = 1, ExitReason = "host_not_found", DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
                }
                tcp = new TcpClient(af);
                await tcp.ConnectAsync(addresses[0], port, connectCts.Token).ConfigureAwait(false);
            }
            else
            {
                tcp = new TcpClient();
                await tcp.ConnectAsync(options.Host, port, connectCts.Token).ConfigureAwait(false);
            }
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
        catch (IOException ex)
        {
            // DNS/transport failures that surface as IOException (e.g. some .NET runtime paths
            // wrap resolver errors). Previously escaped to Main's safety-net → exit 126 with
            // "unexpected error". Round-3 C (class B) fix — classify as connect_failed.
            stderr.WriteLine(Formatting.FormatErrorLine($"{options.Host}:{port} — {SafeError.Describe(ex)}", options.UseColor));
            return new RunResult { ExitCode = 1, ExitReason = "socket_error", DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException)
        {
            // Final safety net: ConnectAsync can surface ArgumentException (invalid host chars),
            // NotSupportedException (AF mismatch on some runtimes), or other types that would
            // otherwise escape to Main → exit 126 "unexpected error" with no JSON envelope —
            // the SFH-class-B defect. Classify as connect_failed so the user gets a proper
            // exit 1 and JSON consumers see a complete envelope. Mirrors PortChecker.ProbeOneAsync.
            stderr.WriteLine(Formatting.FormatErrorLine($"{options.Host}:{port} — {SafeError.Describe(ex)}", options.UseColor));
            return new RunResult { ExitCode = 1, ExitReason = "connect_failed", DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
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
                // Round-2 C2 fix: renew the timeout around the handshake. The outer `connectCts`
                // covers only the TCP connect — by this point its CancelAfter timer has already
                // fired or been consumed, so passing its token to the TLS wrapper would give the
                // handshake effectively no deadline. A hanging TLS server used to block nc
                // indefinitely despite `-w N` being set, silently contradicting the docs.
                using var tlsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                if (options.Timeout > TimeSpan.Zero)
                {
                    tlsCts.CancelAfter(options.Timeout);
                }
                try
                {
                    stream = await TlsWrapper.WrapClientAsync(tcp.GetStream(), options.Host!, options.InsecureTls, tlsCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // User-initiated cancel during handshake — rethrow so the outer handler
                    // maps to 130/interrupted rather than mis-labelling as "tls_failed".
                    throw;
                }
                catch (OperationCanceledException)
                {
                    // Timeout fired during handshake (tlsCts.CancelAfter). Exit 2/timeout matches
                    // the TCP-connect timeout arm above — same user-visible semantic.
                    stderr.WriteLine(Formatting.FormatErrorLine($"{options.Host}:{port} — TLS handshake timed out", options.UseColor));
                    return new RunResult { ExitCode = 2, ExitReason = "timeout", DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
                }
                catch (AuthenticationException ex)
                {
                    // Cert validation failure, protocol mismatch, etc. Specific type so
                    // consumers can distinguish "bad cert" from "transport failure".
                    stderr.WriteLine(Formatting.FormatErrorLine($"TLS — certificate validation failed: {SafeError.Describe(ex)}", options.UseColor));
                    return new RunResult { ExitCode = 1, ExitReason = "tls_failed", DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
                }
                catch (IOException ex)
                {
                    // Transport error mid-handshake (peer RST, network blip). Same exit code
                    // as auth failure but distinct reason text for diagnostics.
                    stderr.WriteLine(Formatting.FormatErrorLine($"TLS — handshake I/O error: {SafeError.Describe(ex)}", options.UseColor));
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
                // Capture tcp + SSL stream into locals for the closure (avoids `this`-style surprises).
                TcpClient capturedTcp = tcp;
                SslStream? capturedSsl = stream as SslStream;
                Func<Task> onSendComplete = async () =>
                {
                    // Close the write half so the peer sees EOF and can respond. Must fire
                    // BEFORE awaiting the receive direction — request/response protocols deadlock
                    // (peer waits for our EOF before sending their reply). For TLS, send
                    // close_notify first so strict peers don't treat this as a truncation attack.
                    //
                    // Catch broadly: half-close is cosmetic — if any of it fails, the primary
                    // pump outcome must still reach the user. The catch in RelayPump around
                    // onSendComplete would otherwise mis-classify a successful transfer as a
                    // socket failure when only the shutdown step blipped. SFH I4 fix (round 3).
                    if (capturedSsl is not null)
                    {
                        try { await capturedSsl.ShutdownAsync().ConfigureAwait(false); }
                        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException) { }
                    }
                    try { capturedTcp.Client.Shutdown(SocketShutdown.Send); }
                    catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException) { }
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
                catch (StdoutClosedException)
                {
                    // Round-2 I1: downstream pipe closed (e.g. `nc host 80 | head -c 10`).
                    // BSD nc semantics: exit 0, not a socket error. Preserves byte counts so the
                    // JSON envelope reflects what actually moved before the pipe closed.
                    sw.Stop();
                    return new RunResult { ExitCode = 0, ExitReason = "stdout_closed",
                        BytesSent = pump.BytesSent, BytesReceived = pump.BytesReceived,
                        DurationMilliseconds = sw.Elapsed.TotalMilliseconds, RemoteAddress = remote };
                }
                catch (IOException ex)
                {
                    // Post-connect transport failure (peer RST mid-transfer, stdout pipe closed,
                    // TLS alert after handshake). Previously uncaught — crashed nc with a stack
                    // trace. Surface as exit 1 + socket_error with partial byte counts so the
                    // JSON envelope reflects what actually moved.
                    sw.Stop();
                    stderr.WriteLine(Formatting.FormatErrorLine($"{options.Host}:{port} — {SafeError.Describe(ex)}", options.UseColor));
                    return new RunResult { ExitCode = 1, ExitReason = "socket_error",
                        BytesSent = pump.BytesSent, BytesReceived = pump.BytesReceived,
                        DurationMilliseconds = sw.Elapsed.TotalMilliseconds, RemoteAddress = remote };
                }
                catch (SocketException ex)
                {
                    sw.Stop();
                    stderr.WriteLine(Formatting.FormatErrorLine($"{options.Host}:{port} — {ex.Message}", options.UseColor));
                    return new RunResult { ExitCode = 1, ExitReason = MapSocketError(ex),
                        BytesSent = pump.BytesSent, BytesReceived = pump.BytesReceived,
                        DurationMilliseconds = sw.Elapsed.TotalMilliseconds, RemoteAddress = remote };
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException)
                {
                    // Round-5 SFH-I3: the pre-pump connect paths got defensive broad-catch arms
                    // in round 3, but the post-connect pump was left narrow (OCE + StdoutClosed +
                    // IOException + SocketException). Racy-disposal during half-close can surface
                    // ObjectDisposedException or InvalidOperationException from NetworkStream —
                    // those previously escaped to Main's 126 safety-net, losing byte counts from
                    // the JSON envelope. Round-4's onSendComplete broad catch swallows the
                    // shutdown-side error, making the recv-side bad-state path more likely.
                    sw.Stop();
                    stderr.WriteLine(Formatting.FormatErrorLine($"{options.Host}:{port} — {SafeError.Describe(ex)}", options.UseColor));
                    return new RunResult { ExitCode = 1, ExitReason = "pump_failed",
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
        try
        {
            udp.Connect(options.Host, port);
        }
        catch (SocketException ex)
        {
            // DNS failure, bad AF (e.g. --ipv4 but host is v6-only), permission denied, etc.
            // Previously uncaught — crashed nc with a stack trace.
            sw.Stop();
            stderr.WriteLine(Formatting.FormatErrorLine($"{options.Host}:{port} — {ex.Message}", options.UseColor));
            return new RunResult { ExitCode = 1, ExitReason = MapSocketError(ex), DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException)
        {
            // UdpClient.Connect(string,int) can also throw ArgumentException (empty/whitespace
            // host), ArgumentOutOfRangeException (port race), or ObjectDisposedException. Without
            // this arm those escape to Main's safety-net → exit 126 "unexpected error" with no
            // JSON envelope. Round-3 class-B (SFH C2) fix.
            sw.Stop();
            stderr.WriteLine(Formatting.FormatErrorLine($"{options.Host}:{port} — {SafeError.Describe(ex)}", options.UseColor));
            return new RunResult { ExitCode = 1, ExitReason = "connect_failed", DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
        }

        // Read all of stdin and send each buffer as a datagram.
        long sent = 0;
        var buf = new byte[65507];
        try
        {
            while (true)
            {
                int n = await stdin.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false);
                if (n == 0) { break; }
                int chunkSent = await udp.SendAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                sent += chunkSent;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // User-initiated Ctrl-C during the send loop. Return a RunResult with partial byte
            // counts + duration so --json consumers see a complete envelope. Without this the
            // OCE escapes to Main → exit 130 but the JSON write never happens. Mirrors TCP
            // Connect's OCE arm inside the pump try/catch. Round-3 I (CR I5) fix.
            sw.Stop();
            return new RunResult { ExitCode = 130, ExitReason = "interrupted",
                BytesSent = sent, DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
        }
        catch (SocketException ex)
        {
            // MTU mismatch, ICMP-unreachable feedback (on Windows — surfaced as WSAECONNRESET
            // on the next Send), or send-side buffer exhaustion.
            sw.Stop();
            stderr.WriteLine(Formatting.FormatErrorLine($"{options.Host}:{port} — {ex.Message}", options.UseColor));
            return new RunResult { ExitCode = 1, ExitReason = MapSocketError(ex),
                BytesSent = sent, DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
        }
        catch (IOException ex)
        {
            // stdin pipe broken (redirected-from-file hit bad sector, closed pipe in some
            // shells). Previously escaped to Main → exit 126. Round-3 class-B (SFH C3) fix.
            sw.Stop();
            stderr.WriteLine(Formatting.FormatErrorLine($"stdin — {SafeError.Describe(ex)}", options.UseColor));
            return new RunResult { ExitCode = 1, ExitReason = "io_error",
                BytesSent = sent, DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
        }

        // Optionally wait briefly for a single response (timeout > 0 = wait).
        long received = 0;
        if (options.Timeout > TimeSpan.Zero)
        {
            using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            receiveCts.CancelAfter(options.Timeout);
            UdpReceiveResult rx;
            try
            {
                rx = await udp.ReceiveAsync(receiveCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Round-5 SFH-I1: user Ctrl-C during the receive wait. Return a full RunResult
                // with partial byte counts so --json consumers see a complete envelope. Without
                // this the OCE escaped to Main → exit 130 via FormatErrorJson (which lacks the
                // bytes_sent/duration fields). Mirrors the send-side cancel arm above.
                sw.Stop();
                return new RunResult { ExitCode = 130, ExitReason = "interrupted",
                    BytesSent = sent, DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // No response within timeout — BSD nc precedent is exit 0 with no output. Round-2
                // I3: emit a short stderr note (unless --json) so the user knows WHY output is
                // empty rather than assuming the exchange succeeded silently.
                if (!options.JsonOutput)
                {
                    stderr.WriteLine(Formatting.FormatWarningLine(
                        $"no UDP response within {options.Timeout.TotalSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}s",
                        options.UseColor));
                }
                rx = default;
            }
            catch (SocketException ex)
            {
                // Receive can surface an earlier send's ICMP-unreachable as a SocketException on
                // Windows. Treat as exit 1; don't let it escape.
                sw.Stop();
                stderr.WriteLine(Formatting.FormatErrorLine($"{options.Host}:{port} — {ex.Message}", options.UseColor));
                return new RunResult { ExitCode = 1, ExitReason = MapSocketError(ex),
                    BytesSent = sent, DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
            }

            if (rx.Buffer is not null && rx.Buffer.Length > 0)
            {
                // Write to stdout in its own try so a closed downstream pipe (`nc -u host 53 | head -c 4`)
                // exits 0/stdout_closed per BSD nc semantics, not 126 "unexpected error". Round-3
                // class-B (SFH C4) fix. The outer `ct` is correct here — the receive's timeout
                // has already been consumed; blocking forever on stdout write is a policy question
                // the outer cancel token handles.
                try
                {
                    await stdout.WriteAsync(rx.Buffer.AsMemory(), ct).ConfigureAwait(false);
                    received = rx.Buffer.Length;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Round-5 SFH-I2: user Ctrl-C during the stdout write AFTER the datagram was
                    // received. Preserve BytesReceived so the envelope reflects that we did in
                    // fact receive the response — the failure was the downstream write.
                    sw.Stop();
                    return new RunResult { ExitCode = 130, ExitReason = "interrupted",
                        BytesSent = sent, BytesReceived = rx.Buffer.Length, DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
                }
                catch (IOException)
                {
                    sw.Stop();
                    return new RunResult { ExitCode = 0, ExitReason = "stdout_closed",
                        BytesSent = sent, BytesReceived = rx.Buffer.Length, DurationMilliseconds = sw.Elapsed.TotalMilliseconds };
                }
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

    internal static string MapSocketError(SocketException ex) => ex.SocketErrorCode switch
    {
        SocketError.ConnectionRefused => "connection_refused",
        SocketError.HostNotFound => "host_not_found",
        SocketError.HostUnreachable => "host_unreachable",
        SocketError.NetworkUnreachable => "network_unreachable",
        SocketError.TimedOut => "timeout",
        _ => "socket_error",
    };
}
