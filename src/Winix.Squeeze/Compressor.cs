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

        // Round-1 review SFH-C1 (auto-detect path): empty input was silently succeeding via
        // the brotli/deflate fallback (BrotliStream.CopyToAsync from empty returns 0 without
        // throwing). gzip(1) rejects empty input on -d; we now do too.
        if (format is null && headerBytes.Length == 0)
        {
            throw new InvalidDataException(
                "input is empty — no compressed data to decompress.");
        }

        if (format.HasValue)
        {
            using var combined = new ConcatenatedStream(headerBytes, input);
            await DecompressAsync(combined, output, format.Value).ConfigureAwait(false);
            return format.Value;
        }

        // No magic bytes or extension match — try brotli then raw deflate as fallbacks.
        // Buffer each attempt into a local MemoryStream so that:
        //  (a) partial writes from a failed attempt don't corrupt the real output stream
        //  (b) we don't call SetLength(0) on the output, which may not support seeking (e.g. CountingStream)
        if (input.CanSeek)
        {
            long savedPosition = input.Position;

            using (var probe = new MemoryStream())
            {
                if (await TryDecompressBrotliAsync(headerBytes, input, probe).ConfigureAwait(false))
                {
                    probe.Position = 0;
                    await probe.CopyToAsync(output, BufferSize).ConfigureAwait(false);
                    return CompressionFormat.Brotli;
                }
            }

            input.Position = savedPosition;

            using (var probe = new MemoryStream())
            {
                if (await TryDecompressRawDeflateAsync(headerBytes, input, probe).ConfigureAwait(false))
                {
                    probe.Position = 0;
                    await probe.CopyToAsync(output, BufferSize).ConfigureAwait(false);
                    // Raw deflate is not a named format — return null to indicate unknown wrapper
                    return null;
                }
            }
        }
        else
        {
            // Non-seekable: buffer the remainder so we can retry
            using var buffered = new MemoryStream();
            await input.CopyToAsync(buffered, BufferSize).ConfigureAwait(false);
            byte[] remainingBytes = buffered.ToArray();

            using var brotliAttempt = new MemoryStream(remainingBytes);
            using (var probe = new MemoryStream())
            {
                if (await TryDecompressBrotliAsync(headerBytes, brotliAttempt, probe).ConfigureAwait(false))
                {
                    probe.Position = 0;
                    await probe.CopyToAsync(output, BufferSize).ConfigureAwait(false);
                    return CompressionFormat.Brotli;
                }
            }

            using var deflateAttempt = new MemoryStream(remainingBytes);
            using (var probe = new MemoryStream())
            {
                if (await TryDecompressRawDeflateAsync(headerBytes, deflateAttempt, probe).ConfigureAwait(false))
                {
                    probe.Position = 0;
                    await probe.CopyToAsync(output, BufferSize).ConfigureAwait(false);
                    return null;
                }
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
        // Round-1 review SFH-C1 — silent corruption on truncated gzip. .NET's GZipStream
        // does not throw when the underlying stream EOFs mid-deflate or before the 8-byte
        // trailer (CRC32 + ISIZE) is consumed. A user piping a half-downloaded .gz got
        // partial output with exit 0 — invisible data corruption.
        //
        // Strategy: wrap input in a TrailingBufferStream that captures the LAST 8 bytes
        // that flowed through it. For valid single-member gzip these bytes ARE the
        // trailer (CRC32 + ISIZE). For truncated input these are random pre-trailer
        // bytes whose ISIZE will not match the actual decompressed byte count.
        //
        // After CopyToAsync, ALSO try to read up to 8 more bytes directly from `input`
        // (in case GZipStream stopped reading before consuming the trailer). Whichever
        // path yields 8 bytes is the candidate trailer; if neither yields 8, the stream
        // is structurally truncated.
        //
        // Multi-member gzip: ISIZE applies only to the LAST member. .NET's GZipStream
        // handles concatenated members natively; the LAST member's trailer is what we
        // validate, which is correct.
        TrailingBufferStream trackingInput = new(input, captureSize: 8);
        CountingStream countingOutput = new(output);

        await using (var gzip = new GZipStream(trackingInput, CompressionMode.Decompress, leaveOpen: true))
        {
            await gzip.CopyToAsync(countingOutput, BufferSize).ConfigureAwait(false);
        }

        long decompressed = countingOutput.BytesWritten;

        if (trackingInput.BytesRead == 0)
        {
            throw new InvalidDataException(
                "gzip stream is empty — no compressed data to decompress.");
        }

        // Try to read up to 8 trailer bytes directly from the underlying input. .NET's
        // GZipStream often leaves the trailer unconsumed, in which case we get a clean
        // 8 bytes here. If GZipStream already consumed them, we get 0 — and the last 8
        // bytes captured by the trailing buffer ARE those trailer bytes.
        byte[] freshTrailer = new byte[8];
        int freshBytes = 0;
        while (freshBytes < 8)
        {
            int n = await input.ReadAsync(freshTrailer.AsMemory(freshBytes, 8 - freshBytes))
                .ConfigureAwait(false);
            if (n == 0) break;
            freshBytes += n;
        }

        // .NET 10's GZipStream emits a non-RFC-compliant 15-byte output for empty input
        // (header + minimal end-block, no trailer) — see github.com/dotnet/runtime PR #56299.
        // When decompressed == 0, .NET's own decompress accepts this as valid, and we
        // can't distinguish .NET-empty-gzip from truncated-to-empty-output. Skip ISIZE
        // validation in that case; the BytesRead == 0 check above already catches
        // truly-empty input.
        if (decompressed == 0)
        {
            return;
        }

        ReadOnlySpan<byte> trailerSource;
        if (freshBytes == 8)
        {
            // GZipStream left the trailer for us — typical .NET behaviour for non-empty input.
            trailerSource = freshTrailer;
        }
        else if (freshBytes == 0)
        {
            // GZipStream consumed everything, including the trailer. The last 8 bytes
            // captured by the trailing buffer ARE the trailer (when input was valid)
            // or random pre-trailer bytes (when input was truncated).
            trailerSource = trackingInput.GetTrailingBytes();
            if (trailerSource.Length < 8)
            {
                throw new InvalidDataException(
                    $"gzip stream is truncated — only {trackingInput.BytesRead} bytes read, " +
                    "shorter than the 18-byte minimum (10-byte header + 8-byte trailer).");
            }
        }
        else
        {
            // Got partial trailer bytes (1-7) from `input`. Stream definitely truncated.
            throw new InvalidDataException(
                "gzip stream is truncated — incomplete trailer (CRC32+ISIZE).");
        }

        uint isize = (uint)trailerSource[4]
                   | ((uint)trailerSource[5] << 8)
                   | ((uint)trailerSource[6] << 16)
                   | ((uint)trailerSource[7] << 24);
        uint actualLow32 = (uint)(decompressed & 0xFFFFFFFFu);

        if (isize != actualLow32)
        {
            throw new InvalidDataException(
                $"gzip integrity check failed — trailer ISIZE={isize} but decompressed " +
                $"{decompressed} bytes (mod 2^32 = {actualLow32}). Stream is truncated or corrupt.");
        }
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
