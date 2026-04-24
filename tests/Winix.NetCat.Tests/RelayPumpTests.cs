#nullable enable

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Winix.NetCat;
using Xunit;

namespace Winix.NetCat.Tests;

public sealed class RelayPumpTests
{
    [Fact]
    public async Task RunAsync_StdinToSocket_CopiesAllBytes()
    {
        byte[] payload = Encoding.ASCII.GetBytes("hello");
        using var stdin = new MemoryStream(payload);
        using var socketWrite = new MemoryStream();
        using var socketRead = new MemoryStream();   // empty; nothing comes back
        using var stdout = new MemoryStream();

        var pump = new RelayPump();
        await pump.RunAsync(socketRead, socketWrite, stdin, stdout, halfCloseOnStdinEof: false, CancellationToken.None);

        Assert.Equal(payload, socketWrite.ToArray());
        Assert.Equal(5, pump.BytesSent);
        Assert.Equal(0, pump.BytesReceived);
    }

    [Fact]
    public async Task RunAsync_SocketToStdout_CopiesAllBytes()
    {
        byte[] response = Encoding.ASCII.GetBytes("world");
        using var stdin = new MemoryStream();         // empty stdin, immediate EOF
        using var socketWrite = new MemoryStream();
        using var socketRead = new MemoryStream(response);
        using var stdout = new MemoryStream();

        var pump = new RelayPump();
        await pump.RunAsync(socketRead, socketWrite, stdin, stdout, halfCloseOnStdinEof: false, CancellationToken.None);

        Assert.Equal(response, stdout.ToArray());
        Assert.Equal(0, pump.BytesSent);
        Assert.Equal(5, pump.BytesReceived);
    }

    [Fact]
    public async Task RunAsync_BothDirections_AllBytesAccounted()
    {
        byte[] req = Encoding.ASCII.GetBytes("ping");
        byte[] resp = Encoding.ASCII.GetBytes("pong!");
        using var stdin = new MemoryStream(req);
        using var socketWrite = new MemoryStream();
        using var socketRead = new MemoryStream(resp);
        using var stdout = new MemoryStream();

        var pump = new RelayPump();
        await pump.RunAsync(socketRead, socketWrite, stdin, stdout, halfCloseOnStdinEof: false, CancellationToken.None);

        Assert.Equal(req, socketWrite.ToArray());
        Assert.Equal(resp, stdout.ToArray());
        Assert.Equal(4, pump.BytesSent);
        Assert.Equal(5, pump.BytesReceived);
    }

    [Fact]
    public async Task RunAsync_HalfCloseRequested_FlagsShouldShutdownSendAfterStdinEof()
    {
        using var stdin = new MemoryStream(new byte[] { 1, 2, 3 });
        using var socketWrite = new MemoryStream();
        using var socketRead = new MemoryStream();
        using var stdout = new MemoryStream();

        var pump = new RelayPump();
        await pump.RunAsync(socketRead, socketWrite, stdin, stdout, halfCloseOnStdinEof: true, CancellationToken.None);

        Assert.True(pump.ShouldShutdownSend);
    }

    [Fact]
    public async Task RunAsync_HalfCloseNotRequested_LeavesShouldShutdownSendFalse()
    {
        using var stdin = new MemoryStream(new byte[] { 1, 2, 3 });
        using var socketWrite = new MemoryStream();
        using var socketRead = new MemoryStream();
        using var stdout = new MemoryStream();

        var pump = new RelayPump();
        await pump.RunAsync(socketRead, socketWrite, stdin, stdout, halfCloseOnStdinEof: false, CancellationToken.None);

        Assert.False(pump.ShouldShutdownSend);
    }

    [Fact]
    public async Task RunAsync_CancellationDuringIdle_Throws()
    {
        // Use anonymous pipes so reads block until something arrives or cancellation fires.
        var stdinPipe = new System.IO.Pipelines.Pipe();
        var socketReadPipe = new System.IO.Pipelines.Pipe();
        using var socketWrite = new MemoryStream();
        using var stdout = new MemoryStream();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(System.TimeSpan.FromMilliseconds(50));

        var pump = new RelayPump();
        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(() =>
            pump.RunAsync(
                socketReadPipe.Reader.AsStream(),
                socketWrite,
                stdinPipe.Reader.AsStream(),
                stdout,
                halfCloseOnStdinEof: false,
                cts.Token));
    }

    /// <summary>
    /// Pins round-1 C1 fix: when the send leg throws, the receive leg must be observed
    /// (not left running as an unobserved task) AND the original send-side exception must
    /// propagate to the caller with its stack trace preserved. Reverting the linked-CTS +
    /// try/finally scaffolding in RunAsync would either swallow the receive task
    /// (UnobservedTaskException) or lose the send-side stack.
    /// </summary>
    [Fact]
    public async Task RunAsync_SendSideThrows_ReceiveIsObservedAndSendExceptionPropagates()
    {
        using var throwingSocketWrite = new ThrowOnWriteStream("simulated send failure");
        // Blocking recv source so the receive leg is still pending when the send leg throws.
        var socketReadPipe = new System.IO.Pipelines.Pipe();
        using var stdin = new MemoryStream(new byte[] { 1, 2, 3, 4 }); // non-empty so send leg writes
        using var stdout = new MemoryStream();

        var pump = new RelayPump();
        var ex = await Assert.ThrowsAsync<IOException>(() =>
            pump.RunAsync(
                socketReadPipe.Reader.AsStream(),
                throwingSocketWrite,
                stdin,
                stdout,
                halfCloseOnStdinEof: false,
                CancellationToken.None));

        Assert.Equal("simulated send failure", ex.Message);
        Assert.False(pump.ShouldShutdownSend);
    }

    /// <summary>
    /// Pins round-2 I1 fix: an IOException from sink.WriteAsync during the receive leg
    /// (i.e. downstream stdout pipe closed, e.g. `| head -c 10`) must surface as a
    /// StdoutClosedException rather than a generic IOException — callers rely on the
    /// typed exception to map to exit 0 instead of exit 1 "socket_error".
    /// </summary>
    [Fact]
    public async Task RunAsync_StdoutClosesDuringReceive_ThrowsStdoutClosedException()
    {
        byte[] response = Encoding.ASCII.GetBytes("peer-data");
        using var socketRead = new MemoryStream(response);
        using var socketWrite = new MemoryStream();
        using var stdin = new MemoryStream(); // empty — send leg ends immediately
        using var throwingStdout = new ThrowOnWriteStream("The pipe has been ended");

        var pump = new RelayPump();
        var ex = await Assert.ThrowsAsync<StdoutClosedException>(() =>
            pump.RunAsync(
                socketRead,
                socketWrite,
                stdin,
                throwingStdout,
                halfCloseOnStdinEof: false,
                CancellationToken.None));

        Assert.NotNull(ex.InnerException);
        Assert.IsType<IOException>(ex.InnerException);
    }

    /// <summary>
    /// Stream that reports success on Read and throws IOException on Write. Used to simulate
    /// broken-pipe (stdout) and send-side failure conditions.
    /// </summary>
    private sealed class ThrowOnWriteStream : Stream
    {
        private readonly string _message;
        public ThrowOnWriteStream(string message) { _message = message; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException(_message);
        public override ValueTask WriteAsync(System.ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
            => throw new IOException(_message);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => throw new IOException(_message);
    }
}
