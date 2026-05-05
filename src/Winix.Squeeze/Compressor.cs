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
            // For seekable inputs, rewind so DecompressGzipAsync's TrailingBufferStream can
            // see the full stream (the trailer-byte capture and post-CopyToAsync trailer
            // read both work cleanly when the wrapper sees raw input from position 0).
            // Non-seekable inputs use the ConcatenatedStream wrapper to virtually prepend
            // the bytes already consumed by format detection.
            if (input.CanSeek)
            {
                input.Position -= headerBytes.Length;
                await DecompressAsync(input, output, format.Value).ConfigureAwait(false);
            }
            else
            {
                using var combined = new ConcatenatedStream(headerBytes, input);
                await DecompressAsync(combined, output, format.Value).ConfigureAwait(false);
            }
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

    // Round-2 review (closing the .NET-non-compliant-empty-gzip workaround that opened
    // the SFH-C1 partial-truncation hole): a canonical RFC 1952 empty-gzip stream
    // that we emit directly when input is empty, instead of letting GZipStream produce
    // its non-RFC-compliant 15-byte output (no trailer). With this, the decompress side's
    // ISIZE validation can be strict — no need for a "decompressed == 0 → skip" hack
    // that allowed header-only truncations to silently exit 0.
    //
    // Layout (per RFC 1952 §2.2):
    //   Header (10 bytes): 1f 8b magic, CM=08 deflate, FLG=00, MTIME=0, XFL=00, OS=0a (Win) or ff (unknown)
    //   Body (5 bytes):    01 00 00 ff ff — empty stored block (BFINAL=1 BTYPE=00 LEN=0 NLEN=ffff)
    //   Trailer (8 bytes): CRC32=0 (LE), ISIZE=0 (LE)
    private static readonly byte[] CanonicalEmptyGzip = new byte[]
    {
        0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xff, // 10-byte header
        0x01, 0x00, 0x00, 0xff, 0xff,                                 // 5-byte empty stored block
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,               // 8-byte zero trailer
    };

    private static async Task CompressGzipAsync(Stream input, Stream output, int level)
    {
        // Peek 1 byte to detect empty input. If empty, emit canonical RFC-compliant gzip
        // directly. Otherwise stream through GZipStream with the peeked byte prepended.
        // This works around .NET 10's non-RFC-compliant 15-byte empty-gzip output (see
        // dotnet/runtime PR #56299), enabling strict ISIZE validation on the decompress side.
        byte[] peek = new byte[1];
        int peeked = 0;
        while (peeked < 1)
        {
            int n = await input.ReadAsync(peek.AsMemory(peeked, 1 - peeked)).ConfigureAwait(false);
            if (n == 0) break;
            peeked += n;
        }

        if (peeked == 0)
        {
            // Empty input → canonical 23-byte empty gzip.
            await output.WriteAsync(CanonicalEmptyGzip.AsMemory()).ConfigureAwait(false);
            return;
        }

        // Non-empty: prepend the peeked byte and stream through GZipStream.
        using var combined = new ConcatenatedStream(peek, input);
        await using var gzip = new GZipStream(output, MapGzipLevel(level), leaveOpen: true);
        await combined.CopyToAsync(gzip, BufferSize).ConfigureAwait(false);
        await gzip.FlushAsync().ConfigureAwait(false);
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
        // Hotfix (post-merge of 64fd7a5): close the silent-corruption hole on truncated
        // incompressible data. The round-3 multi-member detector treated any structurally-
        // plausible 1f 8b byte pair as a second member, but with ~1/2^27 false-positive
        // rate per byte position even after CM=08 + FLG-reserved-bits-zero validation,
        // multi-MB random-data inputs hit false positives reliably (10MB truncated to 5MB
        // produced ~5MB of garbled output silently in published commit 64fd7a5).
        //
        // Trade-off accepted: drop multi-member detection entirely. Concatenated gzip
        // members (rare for squeeze's audience — Windows users compressing single files
        // with `squeeze data.csv`) are rejected as "data is corrupt or truncated" rather
        // than silently accepting silent corruption on incompressible single-member input
        // (common — random data, already-compressed payloads, encrypted blobs). The
        // failure mode is now LOUD (clear error) rather than silent (wrong output).
        //
        // Workaround for the loud-failure case: `gzip -dc concat.gz | squeeze` chains, or
        // decompress with system gzip directly. Documented as a known limitation in
        // docs/ai/squeeze.md.
        //
        // The proper fix (manual member-by-member parsing per RFC 1952 §2.2) is deferred
        // to a future version — see `project_squeeze_progress.md` for design notes.

        // Wrap input in a TrailingBufferStream that captures the LAST 8 bytes seen (the
        // candidate trailer if GZipStream consumed it). Wrap output in a CountingStream so
        // we know the decompressed byte count for ISIZE validation.
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

        if (trackingInput.BytesRead < 18)
        {
            // Less than 10-byte header + 8-byte trailer minimum — definitely truncated.
            throw new InvalidDataException(
                $"gzip stream is truncated — only {trackingInput.BytesRead} bytes read, " +
                "shorter than the 18-byte minimum (10-byte header + 8-byte trailer).");
        }

        // Try to read up to 8 fresh trailer bytes from the underlying input. The hotfix's
        // key signal: if these bytes can't be read, GZipStream consumed everything
        // (which for valid single-member means it consumed the trailer; for valid multi-
        // member means it consumed all members; for truncated single-member means it
        // consumed mid-deflate and left no bytes). We can distinguish via ISIZE check below.
        byte[] freshTrailer = new byte[8];
        int freshBytes = 0;
        while (freshBytes < 8)
        {
            int n = await input.ReadAsync(freshTrailer.AsMemory(freshBytes, 8 - freshBytes))
                .ConfigureAwait(false);
            if (n == 0) break;
            freshBytes += n;
        }

        ReadOnlySpan<byte> trailerSource;
        if (freshBytes == 8)
        {
            // GZipStream left the trailer for us — typical .NET behaviour for non-empty input.
            trailerSource = freshTrailer;
        }
        else if (freshBytes == 0)
        {
            // GZipStream consumed everything. The last 8 bytes captured by the trailing
            // buffer are the candidate trailer. For valid single-member input these match
            // ISIZE. For valid multi-member input they are the LAST member's trailer and
            // ISIZE will not match cumulative `decompressed` — REJECTED as corrupt (this is
            // the documented limitation; workaround: `gzip -dc concat.gz | squeeze`). For
            // truncated single-member they're random pre-trailer bytes and ISIZE rejects.
            trailerSource = trackingInput.GetTrailingBytes();
            if (trailerSource.Length < 8)
            {
                throw new InvalidDataException(
                    "gzip stream is truncated — incomplete trailer (CRC32+ISIZE).");
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
                $"{decompressed} bytes (mod 2^32 = {actualLow32}). Stream is truncated, corrupt, " +
                "or multi-member (concatenated gzip is not currently supported — use `gzip -dc` instead).");
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
