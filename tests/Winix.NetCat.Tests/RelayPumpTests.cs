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
}
