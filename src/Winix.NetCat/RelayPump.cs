#nullable enable

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
    /// When true, after stdin reaches EOF the caller should close the write side of the socket
    /// so the peer sees end-of-stream. This pump signals that intent by leaving
    /// <see cref="ShouldShutdownSend"/> true; the caller (which owns the actual Socket) does it.
    /// </param>
    public async Task RunAsync(
        Stream socketRead,
        Stream socketWrite,
        Stream stdin,
        Stream stdout,
        bool halfCloseOnStdinEof,
        CancellationToken ct)
    {
        Task sendTask = CopyAsync(stdin, socketWrite, isReceive: false, ct);
        Task recvTask = CopyAsync(socketRead, stdout, isReceive: true, ct);

        await sendTask.ConfigureAwait(false);
        ShouldShutdownSend = halfCloseOnStdinEof;
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
