using System.IO.Compression;

namespace Winix.Squeeze;

/// <summary>
/// Provides compression and decompression for supported formats (gzip, brotli, zstd).
/// All methods are static and use streaming APIs with <see cref="Stream"/>.
/// </summary>
public static class Compressor
{
    private const int BufferSize = 81920;

    /// <summary>
    /// Compresses <paramref name="input"/> to <paramref name="output"/> using the specified
    /// format and compression level.
    /// </summary>
    /// <param name="input">Source stream to read uncompressed data from.</param>
    /// <param name="output">Destination stream to write compressed data to.</param>
    /// <param name="format">Compression format to use.</param>
    /// <param name="level">
    /// Format-specific compression level. Use <see cref="CompressionFormatInfo.GetMetadata"/>
    /// to discover valid ranges.
    /// </param>
    public static async Task CompressAsync(Stream input, Stream output, CompressionFormat format, int level)
    {
        switch (format)
        {
            case CompressionFormat.Gzip:
                await CompressGzipAsync(input, output, level).ConfigureAwait(false);
                break;

            case CompressionFormat.Brotli:
                await CompressBrotliAsync(input, output, level).ConfigureAwait(false);
                break;

            case CompressionFormat.Zstd:
                await CompressZstdAsync(input, output, level).ConfigureAwait(false);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    /// <summary>
    /// Decompresses <paramref name="input"/> to <paramref name="output"/> using the specified format.
    /// The caller must know the format in advance; for auto-detection use
    /// <see cref="DecompressAutoDetectAsync"/>.
    /// </summary>
    public static async Task DecompressAsync(Stream input, Stream output, CompressionFormat format)
    {
        switch (format)
        {
            case CompressionFormat.Gzip:
                await DecompressGzipAsync(input, output).ConfigureAwait(false);
                break;

            case CompressionFormat.Brotli:
                await DecompressBrotliAsync(input, output).ConfigureAwait(false);
                break;

            case CompressionFormat.Zstd:
                await DecompressZstdAsync(input, output).ConfigureAwait(false);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    /// <summary>
    /// Detects the compression format from <paramref name="input"/> (via magic bytes, then
    /// filename extension, then brute-force brotli/deflate) and decompresses to
    /// <paramref name="output"/>. Returns the detected format, or null if the data could not
    /// be decompressed as any known format.
    /// </summary>
    /// <param name="input">Compressed input stream.</param>
    /// <param name="output">Destination stream for decompressed data.</param>
    /// <param name="filename">Optional filename hint for extension-based detection (e.g. "data.br").</param>
    /// <returns>The detected format, or null if decompression failed for all formats.</returns>
    public static async Task<CompressionFormat?> DecompressAutoDetectAsync(
        Stream input, Stream output, string? filename)
    {
        var (format, headerBytes) = await FormatDetector.DetectFromStreamAsync(input, filename);

        if (format.HasValue)
        {
            using var combined = new ConcatenatedStream(headerBytes, input);
            await DecompressAsync(combined, output, format.Value).ConfigureAwait(false);
            return format.Value;
        }

        // No magic bytes or extension match — try brotli then raw deflate as fallbacks
        if (input.CanSeek)
        {
            long savedPosition = input.Position;

            if (await TryDecompressBrotliAsync(headerBytes, input, output).ConfigureAwait(false))
            {
                return CompressionFormat.Brotli;
            }

            output.SetLength(0);
            input.Position = savedPosition;

            if (await TryDecompressRawDeflateAsync(headerBytes, input, output).ConfigureAwait(false))
            {
                // Raw deflate is not a named format — return null to indicate unknown wrapper
                return null;
            }
        }
        else
        {
            // Non-seekable: buffer the remainder so we can retry
            using var buffered = new MemoryStream();
            await input.CopyToAsync(buffered, BufferSize).ConfigureAwait(false);
            byte[] remainingBytes = buffered.ToArray();

            using var brotliAttempt = new MemoryStream(remainingBytes);
            if (await TryDecompressBrotliAsync(headerBytes, brotliAttempt, output).ConfigureAwait(false))
            {
                return CompressionFormat.Brotli;
            }

            output.SetLength(0);
            using var deflateAttempt = new MemoryStream(remainingBytes);
            if (await TryDecompressRawDeflateAsync(headerBytes, deflateAttempt, output).ConfigureAwait(false))
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to decompress brotli data where the header bytes have already been read.
    /// Returns true if decompression succeeded, false if the data is not valid brotli.
    /// </summary>
    internal static async Task<bool> TryDecompressBrotliAsync(byte[] headerBytes, Stream remainingStream, Stream output)
    {
        try
        {
            using var combined = new ConcatenatedStream(headerBytes, remainingStream);
            await using var brotli = new BrotliStream(combined, CompressionMode.Decompress, leaveOpen: true);
            await brotli.CopyToAsync(output, BufferSize).ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to decompress raw deflate data where the header bytes have already been read.
    /// Returns true if decompression succeeded, false if the data is not valid deflate.
    /// </summary>
    internal static async Task<bool> TryDecompressRawDeflateAsync(byte[] headerBytes, Stream remainingStream, Stream output)
    {
        try
        {
            using var combined = new ConcatenatedStream(headerBytes, remainingStream);
            await using var deflate = new DeflateStream(combined, CompressionMode.Decompress, leaveOpen: true);
            await deflate.CopyToAsync(output, BufferSize).ConfigureAwait(false);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static CompressionLevel MapGzipLevel(int level)
    {
        // GZipStream only supports three tiers
        if (level <= 1)
        {
            return CompressionLevel.Fastest;
        }

        if (level >= 9)
        {
            return CompressionLevel.SmallestSize;
        }

        return CompressionLevel.Optimal;
    }

    private static CompressionLevel MapBrotliLevel(int level)
    {
        // BrotliStream only supports three tiers via CompressionLevel
        if (level <= 1)
        {
            return CompressionLevel.Fastest;
        }

        if (level >= 10)
        {
            return CompressionLevel.SmallestSize;
        }

        return CompressionLevel.Optimal;
    }

    private static async Task CompressGzipAsync(Stream input, Stream output, int level)
    {
        // Explicit block + FlushAsync ensures gzip header/trailer are written even for empty input
        {
            await using var gzip = new GZipStream(output, MapGzipLevel(level), leaveOpen: true);
            await input.CopyToAsync(gzip, BufferSize).ConfigureAwait(false);
            await gzip.FlushAsync().ConfigureAwait(false);
        }
    }

    private static async Task CompressBrotliAsync(Stream input, Stream output, int level)
    {
        {
            await using var brotli = new BrotliStream(output, MapBrotliLevel(level), leaveOpen: true);
            await input.CopyToAsync(brotli, BufferSize).ConfigureAwait(false);
            await brotli.FlushAsync().ConfigureAwait(false);
        }
    }

    private static async Task CompressZstdAsync(Stream input, Stream output, int level)
    {
        using var compressor = new ZstdSharp.Compressor(level);
        {
            await using var zstdStream = new ZstdSharp.CompressionStream(output, compressor, leaveOpen: true);
            await input.CopyToAsync(zstdStream, BufferSize).ConfigureAwait(false);
            await zstdStream.FlushAsync().ConfigureAwait(false);
        }
    }

    private static async Task DecompressGzipAsync(Stream input, Stream output)
    {
        await using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
        await gzip.CopyToAsync(output, BufferSize).ConfigureAwait(false);
    }

    private static async Task DecompressBrotliAsync(Stream input, Stream output)
    {
        await using var brotli = new BrotliStream(input, CompressionMode.Decompress, leaveOpen: true);
        await brotli.CopyToAsync(output, BufferSize).ConfigureAwait(false);
    }

    private static async Task DecompressZstdAsync(Stream input, Stream output)
    {
        using var decompressor = new ZstdSharp.Decompressor();
        await using var zstdStream = new ZstdSharp.DecompressionStream(input, decompressor, leaveOpen: true);
        await zstdStream.CopyToAsync(output, BufferSize).ConfigureAwait(false);
    }
}
