using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Xunit;

namespace Winix.Squeeze.Tests;

/// <summary>
/// Round-3 verification probes. These intentionally exercise the round-2 fix
/// boundaries (multi-member detection chunk-boundary, ISIZE single-byte cases,
/// 1-byte-input round-trip, peeked-byte preservation, and the pipe-mode multi-
/// member trade-off). They should pass on commit a5e10d8.
/// </summary>
public class Round3ProbeTests
{
    // Forces buf-reads to chunk on a 1-byte boundary so 1f and 8b cross chunks
    // in DetectMultiMemberGzipAsync. Internally that helper uses an 8192-byte
    // buffer, but the underlying stream may return short reads — we simulate
    // that by wrapping in a SingleByteStream.
    private sealed class SingleByteStream : Stream
    {
        private readonly Stream _inner;
        public SingleByteStream(Stream inner) { _inner = inner; }
        public override bool CanRead => true;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(1, count));
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task<byte[]> GzipAsync(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            await gz.WriteAsync(data);
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task MultiMember_DetectedEvenWhenInputReturnsOneByteAtATime()
    {
        // Concatenated gzip; underlying stream returns 1 byte per Read so the
        // 1f/8b magic of member 2 will straddle every chunk boundary.
        byte[] one = await GzipAsync(new byte[] { 0x41 });
        byte[] concat = new byte[one.Length * 2];
        Buffer.BlockCopy(one, 0, concat, 0, one.Length);
        Buffer.BlockCopy(one, 0, concat, one.Length, one.Length);

        // Wrap a seekable MemoryStream in SingleByteStream so the detector's
        // ReadAsync returns one byte at a time. Note: SingleByteStream is seekable.
        using var inner = new MemoryStream(concat);
        using var slow = new SingleByteStream(inner);
        using var output = new MemoryStream();
        await Compressor.DecompressAsync(slow, output, CompressionFormat.Gzip);

        Assert.Equal(new byte[] { 0x41, 0x41 }, output.ToArray());
    }

    [Fact]
    public async Task SingleByteInput_RoundTrips_PeekedBytePreserved()
    {
        byte[] one = new byte[] { 0x5A };
        using var src = new MemoryStream(one);
        using var compressed = new MemoryStream();
        await Compressor.CompressAsync(src, compressed, CompressionFormat.Gzip, 6);

        compressed.Position = 0;
        using var output = new MemoryStream();
        await Compressor.DecompressAsync(compressed, output, CompressionFormat.Gzip);
        Assert.Equal(one, output.ToArray());
    }

    [Fact]
    public async Task EmptyRoundTrip_ProducesCanonical23ByteGzip_AndDecompressesToEmpty()
    {
        using var src = new MemoryStream(Array.Empty<byte>());
        using var compressed = new MemoryStream();
        await Compressor.CompressAsync(src, compressed, CompressionFormat.Gzip, 6);
        Assert.Equal(23, compressed.Length);

        compressed.Position = 0;
        using var output = new MemoryStream();
        await Compressor.DecompressAsync(compressed, output, CompressionFormat.Gzip);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Truncated15Bytes_ThrowsCorruptOrTruncated()
    {
        byte[] gz = await GzipAsync("hello world"u8.ToArray());
        Assert.True(gz.Length >= 15);
        byte[] truncated = new byte[15];
        Buffer.BlockCopy(gz, 0, truncated, 0, 15);

        using var src = new MemoryStream(truncated);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(
            () => Compressor.DecompressAsync(src, output, CompressionFormat.Gzip));
    }

    [Fact]
    public async Task Truncated30Bytes_ThrowsCorruptOrTruncated()
    {
        // Build a larger payload so the gzip output exceeds 30 bytes.
        byte[] payload = new byte[200];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)('A' + (i % 26));
        byte[] gz = await GzipAsync(payload);
        Assert.True(gz.Length > 30);
        byte[] truncated = new byte[30];
        Buffer.BlockCopy(gz, 0, truncated, 0, 30);

        using var src = new MemoryStream(truncated);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(
            () => Compressor.DecompressAsync(src, output, CompressionFormat.Gzip));
    }

    [Fact]
    public async Task ConcatenatedTwoMembers_RoundTrips()
    {
        byte[] one = await GzipAsync("hello "u8.ToArray());
        byte[] two = await GzipAsync("world"u8.ToArray());
        byte[] concat = new byte[one.Length + two.Length];
        Buffer.BlockCopy(one, 0, concat, 0, one.Length);
        Buffer.BlockCopy(two, 0, concat, one.Length, two.Length);

        using var src = new MemoryStream(concat);
        using var output = new MemoryStream();
        await Compressor.DecompressAsync(src, output, CompressionFormat.Gzip);
        Assert.Equal("hello world"u8.ToArray(), output.ToArray());
    }

    /// <summary>
    /// Pipe-mode multi-member: documented round-2 trade-off — non-seekable
    /// streams skip multi-member detection and go through single-member ISIZE
    /// validation, which causes a false-positive "corrupt" report.
    /// This test pins the current behaviour so a future fix removes it deliberately.
    /// </summary>
    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;
        public NonSeekableStream(byte[] data) { _inner = new MemoryStream(data); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task PipeMode_MultiMember_PinDocumentedTradeoff()
    {
        byte[] one = await GzipAsync("hello "u8.ToArray());
        byte[] two = await GzipAsync("world"u8.ToArray());
        byte[] concat = new byte[one.Length + two.Length];
        Buffer.BlockCopy(one, 0, concat, 0, one.Length);
        Buffer.BlockCopy(two, 0, concat, one.Length, two.Length);

        using var pipe = new NonSeekableStream(concat);
        using var output = new MemoryStream();

        // Documented round-2 behaviour: non-seekable multi-member is rejected as
        // corrupt (ISIZE of the LAST member compared against SUM of all members).
        // If this assertion ever flips to "round-trips", that's a positive change
        // — update the test to assert correctness and remove the trade-off note.
        await Assert.ThrowsAsync<InvalidDataException>(
            () => Compressor.DecompressAsync(pipe, output, CompressionFormat.Gzip));
    }
}
