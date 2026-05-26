namespace Winix.Squeeze;

/// <summary>
/// A read-only stream wrapper that captures a sliding window of the LAST N bytes seen on
/// the underlying stream. Used to reach the gzip trailer (CRC32+ISIZE) for validation
/// without requiring the underlying stream to be seekable, and without buffering the entire
/// input in memory.
/// </summary>
/// <remarks>
/// Round-1 review SFH-C1: <see cref="Compressor.DecompressGzipAsync"/> uses this to
/// validate the ISIZE field after decompress, catching truncated gzip streams that .NET's
/// GZipStream silently treats as "successfully terminated" (returning 0 bytes from the
/// next read instead of throwing).
/// </remarks>
internal sealed class TrailingBufferStream : Stream
{
    private readonly Stream _inner;
    private readonly byte[] _buffer;
    private readonly int _captureSize;
    private int _captureCount; // 0..captureSize bytes filled
    private int _captureStart; // ring-buffer head when full
    private long _bytesRead;

    public TrailingBufferStream(Stream inner, int captureSize)
    {
        _inner = inner;
        _captureSize = captureSize;
        _buffer = new byte[captureSize];
    }

    public long BytesRead => _bytesRead;

    /// <summary>
    /// Returns the trailing bytes captured so far, in chronological order.
    /// May be shorter than <c>captureSize</c> if the stream had fewer bytes total.
    /// </summary>
    public ReadOnlySpan<byte> GetTrailingBytes()
    {
        if (_captureCount < _captureSize)
        {
            return _buffer.AsSpan(0, _captureCount);
        }
        // Ring buffer is full — emit in chronological order starting from _captureStart.
        byte[] ordered = new byte[_captureSize];
        int firstChunk = _captureSize - _captureStart;
        Array.Copy(_buffer, _captureStart, ordered, 0, firstChunk);
        Array.Copy(_buffer, 0, ordered, firstChunk, _captureStart);
        return ordered;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = _inner.Read(buffer, offset, count);
        if (read > 0)
        {
            CaptureTrailing(buffer.AsSpan(offset, read));
            _bytesRead += read;
        }
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        int read = _inner.Read(buffer);
        if (read > 0)
        {
            CaptureTrailing(buffer[..read]);
            _bytesRead += read;
        }
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int read = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        if (read > 0)
        {
            CaptureTrailing(buffer.AsSpan(offset, read));
            _bytesRead += read;
        }
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read > 0)
        {
            CaptureTrailing(buffer.Span[..read]);
            _bytesRead += read;
        }
        return read;
    }

    public override int ReadByte()
    {
        int b = _inner.ReadByte();
        if (b >= 0)
        {
            byte[] tmp = { (byte)b };
            CaptureTrailing(tmp);
            _bytesRead++;
        }
        return b;
    }

    private void CaptureTrailing(ReadOnlySpan<byte> data)
    {
        // If incoming chunk is larger than the capture window, keep only its last `captureSize` bytes.
        if (data.Length >= _captureSize)
        {
            data = data[^_captureSize..];
            data.CopyTo(_buffer);
            _captureCount = _captureSize;
            _captureStart = 0;
            return;
        }

        if (_captureCount < _captureSize)
        {
            // Filling phase — append until full.
            int free = _captureSize - _captureCount;
            int toCopy = Math.Min(free, data.Length);
            data[..toCopy].CopyTo(_buffer.AsSpan(_captureCount));
            _captureCount += toCopy;
            data = data[toCopy..];
            if (data.IsEmpty) return;
            // Spilled over — fall through to ring-buffer phase.
            _captureStart = 0;
        }

        // Ring-buffer phase — replace oldest bytes.
        int spaceToEnd = _captureSize - _captureStart;
        if (data.Length <= spaceToEnd)
        {
            data.CopyTo(_buffer.AsSpan(_captureStart));
            _captureStart = (_captureStart + data.Length) % _captureSize;
        }
        else
        {
            data[..spaceToEnd].CopyTo(_buffer.AsSpan(_captureStart));
            data[spaceToEnd..].CopyTo(_buffer);
            _captureStart = data.Length - spaceToEnd;
        }
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        // Caller owns the inner stream.
        base.Dispose(disposing);
    }
}
