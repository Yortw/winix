#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Winix.Squeeze;

namespace Winix.Squeeze.Tests;

// Round-2 review (closing the test-coverage gap on TrailingBufferStream's ring-buffer
// edge cases). 130 LOC of new code with multiple Read overloads + ring-wrap arithmetic
// was previously exercised only indirectly through the gzip decompress integration
// tests. These tests pin the contract directly so a future ring-buffer arithmetic
// regression fails an explicit assertion.
public class TrailingBufferStreamTests
{
    private static byte[] Bytes(int from, int count)
    {
        byte[] b = new byte[count];
        for (int i = 0; i < count; i++) b[i] = (byte)(from + i);
        return b;
    }

    [Fact]
    public void GetTrailingBytes_TotalLessThanCapture_ReturnsAllBytes()
    {
        using MemoryStream ms = new(new byte[] { 1, 2, 3 });
        using TrailingBufferStream t = new(ms, captureSize: 8);
        byte[] buf = new byte[3];
        Assert.Equal(3, t.Read(buf, 0, 3));
        Assert.Equal(0, t.Read(buf, 0, 3));
        Assert.Equal(new byte[] { 1, 2, 3 }, t.GetTrailingBytes().ToArray());
    }

    [Fact]
    public void GetTrailingBytes_TotalEqualsCapture_ReturnsAllBytes()
    {
        using MemoryStream ms = new(Bytes(1, 8));
        using TrailingBufferStream t = new(ms, captureSize: 8);
        byte[] buf = new byte[8];
        Assert.Equal(8, t.Read(buf, 0, 8));
        Assert.Equal(Bytes(1, 8), t.GetTrailingBytes().ToArray());
    }

    [Fact]
    public void GetTrailingBytes_TotalGreaterThanCapture_ReturnsLastN()
    {
        using MemoryStream ms = new(Bytes(1, 20));
        using TrailingBufferStream t = new(ms, captureSize: 8);
        byte[] buf = new byte[20];
        Assert.Equal(20, t.Read(buf, 0, 20));
        // Last 8 bytes are 13..20.
        Assert.Equal(Bytes(13, 8), t.GetTrailingBytes().ToArray());
    }

    [Fact]
    public void GetTrailingBytes_LargeChunk_ReplacesEntireWindow()
    {
        // A single read of more than captureSize bytes should keep only the last `captureSize`.
        using MemoryStream ms = new(Bytes(1, 100));
        using TrailingBufferStream t = new(ms, captureSize: 8);
        byte[] buf = new byte[100];
        Assert.Equal(100, t.Read(buf, 0, 100));
        Assert.Equal(Bytes(93, 8), t.GetTrailingBytes().ToArray());
    }

    [Fact]
    public void GetTrailingBytes_MultipleSmallReads_StraddleFillBoundary()
    {
        // Two reads of 5 bytes each = 10 total. Last 8 are 3..10.
        using MemoryStream ms = new(Bytes(1, 10));
        using TrailingBufferStream t = new(ms, captureSize: 8);
        byte[] buf = new byte[5];
        Assert.Equal(5, t.Read(buf, 0, 5)); // 1..5 captured
        Assert.Equal(5, t.Read(buf, 0, 5)); // 6..10 — fills then wraps
        Assert.Equal(Bytes(3, 8), t.GetTrailingBytes().ToArray());
    }

    [Fact]
    public void GetTrailingBytes_RingWrapMidRead_OrdersChronologically()
    {
        // Read bytes [1..6], then [7..14] → ring wraps. Last 8 = [7..14].
        using MemoryStream ms = new(Bytes(1, 14));
        using TrailingBufferStream t = new(ms, captureSize: 8);
        byte[] buf = new byte[6];
        Assert.Equal(6, t.Read(buf, 0, 6));
        buf = new byte[8];
        Assert.Equal(8, t.Read(buf, 0, 8));
        Assert.Equal(Bytes(7, 8), t.GetTrailingBytes().ToArray());
    }

    [Fact]
    public void BytesRead_TracksCumulativeTotal()
    {
        using MemoryStream ms = new(Bytes(1, 20));
        using TrailingBufferStream t = new(ms, captureSize: 8);
        byte[] buf = new byte[10];
        _ = t.Read(buf, 0, 10);
        Assert.Equal(10, t.BytesRead);
        _ = t.Read(buf, 0, 10);
        Assert.Equal(20, t.BytesRead);
    }

    [Fact]
    public void ReadByte_UpdatesTrailingBuffer()
    {
        using MemoryStream ms = new(Bytes(1, 10));
        using TrailingBufferStream t = new(ms, captureSize: 4);
        for (int i = 0; i < 10; i++) t.ReadByte();
        Assert.Equal(10, t.BytesRead);
        Assert.Equal(Bytes(7, 4), t.GetTrailingBytes().ToArray());
    }

    [Fact]
    public async Task ReadAsync_UpdatesTrailingBuffer()
    {
        using MemoryStream ms = new(Bytes(1, 12));
        using TrailingBufferStream t = new(ms, captureSize: 6);
        byte[] buf = new byte[12];
        Assert.Equal(12, await t.ReadAsync(buf.AsMemory(0, 12)));
        Assert.Equal(12, t.BytesRead);
        Assert.Equal(Bytes(7, 6), t.GetTrailingBytes().ToArray());
    }

    [Fact]
    public void Dispose_DoesNotDisposeInner()
    {
        using MemoryStream ms = new(Bytes(1, 8));
        TrailingBufferStream t = new(ms, captureSize: 8);
        t.Dispose();
        // Inner should still be usable.
        Assert.True(ms.CanRead);
    }
}
