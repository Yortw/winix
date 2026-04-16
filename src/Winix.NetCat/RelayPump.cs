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
        Task sendTask = CopyAsync(stdin, socketWrite, isReceive: false, ct);
        Task recvTask = CopyAsync(socketRead, stdout, isReceive: true, ct);

        await sendTask.ConfigureAwait(false);
        ShouldShutdownSend = halfCloseOnStdinEof;
        if (halfCloseOnStdinEof && onSendComplete is not null)
        {
            await onSendComplete().ConfigureAwait(false);
        }
        await recvTask.ConfigureAwait(false);
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
            await sink.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            await sink.FlushAsync(ct).ConfigureAwait(false);
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
