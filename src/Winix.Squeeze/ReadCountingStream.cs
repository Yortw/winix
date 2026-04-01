namespace Winix.Squeeze;

/// <summary>
/// A read-only stream wrapper that counts the bytes read from the underlying stream
/// without buffering. Used to measure input size when streaming from non-seekable
/// sources (e.g. stdin) where <see cref="Stream.Length"/> is not available.
/// </summary>
internal sealed class ReadCountingStream : Stream
{
    private readonly Stream _inner;
    private long _bytesRead;

    /// <summary>
    /// Wraps <paramref name="inner"/> to count bytes read through it.
    /// </summary>
    public ReadCountingStream(Stream inner)
    {
        _inner = inner;
    }

    /// <summary>Total bytes read through this stream.</summary>
    public long BytesRead => _bytesRead;

    /// <inheritdoc />
    public override bool CanRead => _inner.CanRead;
    /// <inheritdoc />
    public override bool CanSeek => false;
    /// <inheritdoc />
    public override bool CanWrite => false;
    /// <inheritdoc />
    public override long Length => _inner.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = _inner.Read(buffer, offset, count);
        _bytesRead += read;
        return read;
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        int read = _inner.Read(buffer);
        _bytesRead += read;
        return read;
    }

    /// <inheritdoc />
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int read = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        _bytesRead += read;
        return read;
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        _bytesRead += read;
        return read;
    }

    /// <inheritdoc />
    public override int ReadByte()
    {
        int b = _inner.ReadByte();
        if (b >= 0)
        {
            _bytesRead++;
        }
        return b;
    }

    /// <inheritdoc />
    public override void Flush() => _inner.Flush();
    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();
}
