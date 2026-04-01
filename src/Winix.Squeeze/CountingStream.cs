namespace Winix.Squeeze;

/// <summary>
/// A write-only stream wrapper that counts the bytes written to the underlying stream
/// without buffering. Used to measure compressed/decompressed output size when streaming
/// directly to stdout, avoiding the MemoryStream double-buffer that would OOM on large files.
/// </summary>
internal sealed class CountingStream : Stream
{
    private readonly Stream _inner;
    private long _bytesWritten;

    /// <summary>
    /// Wraps <paramref name="inner"/> to count bytes written through it.
    /// </summary>
    public CountingStream(Stream inner)
    {
        _inner = inner;
    }

    /// <summary>Total bytes written through this stream.</summary>
    public long BytesWritten => _bytesWritten;

    /// <inheritdoc />
    public override bool CanRead => false;
    /// <inheritdoc />
    public override bool CanSeek => false;
    /// <inheritdoc />
    public override bool CanWrite => _inner.CanWrite;
    /// <inheritdoc />
    public override long Length => _inner.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        _bytesWritten += count;
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _inner.Write(buffer);
        _bytesWritten += buffer.Length;
    }

    /// <inheritdoc />
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await _inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        _bytesWritten += count;
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        _bytesWritten += buffer.Length;
    }

    /// <inheritdoc />
    public override void WriteByte(byte value)
    {
        _inner.WriteByte(value);
        _bytesWritten++;
    }

    /// <inheritdoc />
    public override void Flush() => _inner.Flush();
    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();
}
