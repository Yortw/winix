namespace Winix.Squeeze;

/// <summary>
/// Read-only stream that serves a prefix byte array first, then reads from an inner stream.
/// Used to reconstruct a full stream after header bytes have been consumed for format detection.
/// </summary>
internal sealed class ConcatenatedStream : Stream
{
    private readonly byte[] _prefix;
    private int _prefixOffset;
    private readonly Stream _inner;
    private bool _disposed;

    /// <summary>
    /// Creates a new <see cref="ConcatenatedStream"/> that reads <paramref name="prefix"/>
    /// bytes first, then continues from <paramref name="inner"/>.
    /// </summary>
    /// <param name="prefix">Header bytes to serve before the inner stream.</param>
    /// <param name="inner">The remaining stream. Not disposed when this stream is disposed.</param>
    public ConcatenatedStream(byte[] prefix, Stream inner)
    {
        _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int totalRead = 0;

        // Serve prefix bytes first
        if (_prefixOffset < _prefix.Length)
        {
            int available = _prefix.Length - _prefixOffset;
            int toCopy = Math.Min(available, count);
            Buffer.BlockCopy(_prefix, _prefixOffset, buffer, offset, toCopy);
            _prefixOffset += toCopy;
            totalRead += toCopy;
            offset += toCopy;
            count -= toCopy;
        }

        // Then read from inner stream
        if (count > 0)
        {
            totalRead += _inner.Read(buffer, offset, count);
        }

        return totalRead;
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int totalRead = 0;

        if (_prefixOffset < _prefix.Length)
        {
            int available = _prefix.Length - _prefixOffset;
            int toCopy = Math.Min(available, buffer.Length);
            _prefix.AsSpan(_prefixOffset, toCopy).CopyTo(buffer);
            _prefixOffset += toCopy;
            totalRead += toCopy;
            buffer = buffer.Slice(toCopy);
        }

        if (buffer.Length > 0)
        {
            totalRead += _inner.Read(buffer);
        }

        return totalRead;
    }

    /// <inheritdoc />
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int totalRead = 0;

        if (_prefixOffset < _prefix.Length)
        {
            int available = _prefix.Length - _prefixOffset;
            int toCopy = Math.Min(available, count);
            Buffer.BlockCopy(_prefix, _prefixOffset, buffer, offset, toCopy);
            _prefixOffset += toCopy;
            totalRead += toCopy;
            offset += toCopy;
            count -= toCopy;
        }

        if (count > 0)
        {
            totalRead += await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }

        return totalRead;
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int totalRead = 0;

        if (_prefixOffset < _prefix.Length)
        {
            int available = _prefix.Length - _prefixOffset;
            int toCopy = Math.Min(available, buffer.Length);
            _prefix.AsMemory(_prefixOffset, toCopy).CopyTo(buffer);
            _prefixOffset += toCopy;
            totalRead += toCopy;
            buffer = buffer.Slice(toCopy);
        }

        if (buffer.Length > 0)
        {
            totalRead += await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        return totalRead;
    }

    /// <inheritdoc />
    public override void Flush()
    {
        // No-op for read-only stream
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        // Don't dispose inner stream — caller owns it
        _disposed = true;
        base.Dispose(disposing);
    }
}
