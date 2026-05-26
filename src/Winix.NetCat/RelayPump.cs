#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.NetCat;

/// <summary>
/// Bidirectional byte copy between a socket-side pair of streams and the
/// console's stdin/stdout. Tracks byte counts and supports half-close on
/// stdin EOF (so HTTP-style request/response patterns work).
/// </summary>
/// <remarks>
/// The socket is modelled as two streams (<c>socketRead</c>, <c>socketWrite</c>)
/// rather than one bidirectional stream so tests can substitute pipe streams
/// without ever opening a real socket. The console-app entry point passes the
/// same <see cref="System.Net.Sockets.NetworkStream"/> for both arguments.
/// </remarks>
public sealed class RelayPump
{
    private long _bytesSent;
    private long _bytesReceived;

    /// <summary>Bytes copied from stdin to <c>socketWrite</c>.</summary>
    public long BytesSent => _bytesSent;

    /// <summary>Bytes copied from <c>socketRead</c> to stdout.</summary>
    public long BytesReceived => _bytesReceived;

    /// <summary>
    /// Runs both copy directions in parallel until both terminate naturally
    /// (peer EOF on socketRead, stdin EOF), or <paramref name="ct"/> fires.
    /// </summary>
    /// <param name="socketRead">Read side of the socket (peer → us).</param>
    /// <param name="socketWrite">Write side of the socket (us → peer).</param>
    /// <param name="stdin">Console stdin (or a stand-in stream).</param>
    /// <param name="stdout">Console stdout (or a stand-in stream).</param>
    /// <param name="halfCloseOnStdinEof">
    /// When true, the pump signals the caller (via <paramref name="onSendComplete"/> and
    /// <see cref="ShouldShutdownSend"/>) to close the write side of the socket after stdin
    /// reaches EOF. Without this, peers waiting for EOF to process a request would hang.
    /// </param>
    /// <param name="onSendComplete">
    /// Optional callback invoked after <c>stdin → socketWrite</c> copy finishes and before
    /// the pump awaits the receive direction. When <paramref name="halfCloseOnStdinEof"/>
    /// is true, this is the hook point for the caller to issue
    /// <c>Socket.Shutdown(SocketShutdown.Send)</c>. Invoking shutdown here (rather than
    /// after the pump returns) is required: the receive direction cannot terminate until
    /// the peer sends EOF, which it typically won't do until it sees our EOF first.
    /// </param>
    public async Task RunAsync(
        Stream socketRead,
        Stream socketWrite,
        Stream stdin,
        Stream stdout,
        bool halfCloseOnStdinEof,
        CancellationToken ct,
        Func<Task>? onSendComplete = null)
    {
        // Link a CTS so a failure on one leg cancels the other. Without this, sequential
        // `await sendTask; await recvTask` left recvTask running unobserved whenever sendTask
        // threw — the caller's `using (stream)` would then dispose the stream underneath the
        // still-running copy, producing a silent UnobservedTaskException. Round-1 C1 fix.
        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task sendTask = CopyAsync(stdin, socketWrite, isReceive: false, pumpCts.Token);
        Task recvTask = CopyAsync(socketRead, stdout, isReceive: true, pumpCts.Token);

        // Observe sendTask's outcome first so the half-close hook runs before we await recv.
        // Use try/finally so recvTask is always observed even when sendTask throws — otherwise
        // its exception (often a follow-on from the cancellation we trigger here) would escape
        // the process as an UnobservedTaskException.
        Exception? sendFailure = null;
        try
        {
            await sendTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            sendFailure = ex;
            // Cancel the receive side — it's usually stuck in ReadAsync and would otherwise
            // continue running after this method returns.
            try { pumpCts.Cancel(); } catch (ObjectDisposedException) { }
        }

        bool shouldHalfClose = halfCloseOnStdinEof && sendFailure is null;
        if (shouldHalfClose && onSendComplete is not null)
        {
            try
            {
                await onSendComplete().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Half-close shutdown failure shouldn't mask the primary outcome. Clear the flag
                // so any post-RunAsync caller reading ShouldShutdownSend doesn't re-issue the
                // shutdown that just failed. Attach the onSendComplete failure only if the primary
                // send didn't already fail. Round-3 CR-I3: previously ShouldShutdownSend stayed
                // true even after the callback threw — a latent trap for future callers.
                shouldHalfClose = false;
                sendFailure ??= ex;
                try { pumpCts.Cancel(); } catch (ObjectDisposedException) { }
            }
        }
        ShouldShutdownSend = shouldHalfClose;

        try
        {
            await recvTask.ConfigureAwait(false);
        }
        catch when (sendFailure is not null)
        {
            // Prefer the original send-side exception — the recv exception is usually a
            // cancellation-cascade artefact, not the interesting cause.
        }

        if (sendFailure is not null)
        {
            // Rethrow preserving the stack via ExceptionDispatchInfo so callers see the origin.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(sendFailure).Throw();
        }
    }

    /// <summary>
    /// True when <see cref="RunAsync"/> finished and the caller should call
    /// <c>Socket.Shutdown(SocketShutdown.Send)</c>. Used by NetCatClient/Listener
    /// which own the actual <see cref="System.Net.Sockets.Socket"/>.
    /// </summary>
    public bool ShouldShutdownSend { get; private set; }

    private async Task CopyAsync(Stream source, Stream sink, bool isReceive, CancellationToken ct)
    {
        const int bufferSize = 4096;
        byte[] buffer = new byte[bufferSize];
        while (true)
        {
            int read = await source.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }
            try
            {
                await sink.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                await sink.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (IOException ex) when (isReceive)
            {
                // Downstream pipe closed — e.g. `nc host 80 | head -c 10`. BSD nc precedent
                // treats this as a clean exit rather than a socket failure. Round-2 I1 fix:
                // wrap in a typed exception so NetCatClient/Listener can distinguish from
                // genuine socket IOException (peer RST mid-transfer, TLS record error, etc.).
                throw new StdoutClosedException("stdout pipe closed", ex);
            }
            if (isReceive)
            {
                System.Threading.Interlocked.Add(ref _bytesReceived, read);
            }
            else
            {
                System.Threading.Interlocked.Add(ref _bytesSent, read);
            }
        }
    }
}

/// <summary>
/// Signals that the downstream stdout pipe was closed while the pump was writing received bytes.
/// Callers (NetCatClient / NetCatListener) treat this as a clean exit (code 0, reason
/// <c>stdout_closed</c>), matching BSD netcat's SIGPIPE behaviour.
/// </summary>
/// <remarks>
/// CATCH-ORDER IS LOAD-BEARING: callers MUST catch <see cref="StdoutClosedException"/> BEFORE
/// <see cref="IOException"/> in their handler chain, or the IOException arm will consume this
/// subtype first and silently mis-classify a clean broken-pipe as a generic "socket_error"
/// exit 1. Reordering the catch blocks in NetCatClient / NetCatListener is a silent regression —
/// round-3 CR-I7 flagged this trap explicitly.
/// </remarks>
public sealed class StdoutClosedException : IOException
{
    /// <summary>Creates a new instance with the given message and inner exception.</summary>
    public StdoutClosedException(string message, Exception inner) : base(message, inner) { }
}
