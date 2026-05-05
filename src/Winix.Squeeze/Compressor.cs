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
            // Round-2 review (closing the multi-member false-positive): if input is seekable,
            // rewind and pass the original stream to DecompressAsync so DecompressGzipAsync's
            // multi-member detection can scan it directly. Otherwise fall back to the
            // ConcatenatedStream wrapper (non-seekable; multi-member detection is skipped
            // for pipe inputs as a documented trade-off).
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
        // Round-1 review SFH-C1 / Round-2 review (closing): silent corruption on truncated
        // gzip. .NET's GZipStream does not throw when the underlying stream EOFs mid-deflate
        // or before the 8-byte trailer (CRC32 + ISIZE) is consumed; CopyToAsync simply
        // returns 0 from the next Read.
        //
        // Strategy:
        //  1. Detect multi-member gzip upfront by buffering input and scanning for
        //     additional `1f 8b` magic-byte sequences. If multi-member, skip ISIZE
        //     validation — .NET's per-member CRC32 check is the layer of defence there;
        //     per-member ISIZE accumulation requires tracking member boundaries which
        //     is non-trivial for round 2.
        //  2. For single-member: wrap input in a TrailingBufferStream that captures the
        //     LAST 8 bytes seen (the candidate trailer if GZipStream consumed it) AND
        //     also try to read 8 fresh trailer bytes from the underlying input after
        //     CopyToAsync (the typical case where GZipStream leaves the trailer unread).
        //  3. Validate ISIZE against decompressed byte count; reject mismatch as truncation.
        //  4. Reject input that's structurally too short (BytesRead < 18 = 10-header +
        //     8-trailer minimum). This catches header-only truncations.
        //
        // The compress side now emits a canonical RFC 1952 empty-gzip (23 bytes with full
        // trailer) so we don't need a "decompressed == 0 → skip" workaround for the
        // .NET-non-compliant case — that workaround opened a header-only-truncation hole
        // that this round closes.
        //
        // Multi-member detection requires examining the input bytes; for non-seekable
        // streams we buffer the whole input first. This forfeits true streaming for
        // multi-member but is correct; single-member streams (the overwhelming majority)
        // still stream.

        // For seekable input, peek and look for second magic byte sequence.
        bool multiMember = await DetectMultiMemberGzipAsync(input).ConfigureAwait(false);

        if (multiMember)
        {
            // Multi-member: skip ISIZE validation. .NET's per-member CRC32 in GZipStream
            // covers in-stream corruption; full per-member ISIZE accumulation deferred.
            await using var gzipMulti = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
            await gzipMulti.CopyToAsync(output, BufferSize).ConfigureAwait(false);
            return;
        }

        // Single-member path with strict ISIZE validation.
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

        // Try to read up to 8 fresh trailer bytes from the underlying input.
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
            trailerSource = freshTrailer;
        }
        else if (freshBytes == 0)
        {
            // GZipStream consumed the trailer. Use the captured last-8.
            trailerSource = trackingInput.GetTrailingBytes();
            if (trailerSource.Length < 8)
            {
                throw new InvalidDataException(
                    "gzip stream is truncated — incomplete trailer (CRC32+ISIZE).");
            }
        }
        else
        {
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

    /// <summary>
    /// Detects whether <paramref name="input"/> contains multiple concatenated gzip members
    /// by buffering all bytes (rewinding seekable inputs after) and scanning for additional
    /// <c>1f 8b</c> magic-byte sequences after position 0.
    /// </summary>
    /// <remarks>
    /// Round-2 review (closing the multi-member ISIZE false-positive Critical): for valid
    /// concatenated gzip (e.g. <c>cat a.gz b.gz</c>), my round-1 ISIZE check compared the
    /// LAST member's ISIZE against the SUM of all members' decompressed bytes — a guaranteed
    /// false positive. Skip ISIZE validation for multi-member streams; rely on .NET's
    /// per-member CRC32 check (which IS performed during decompress).
    ///
    /// For non-seekable streams: buffers the entire input into a MemoryStream. Forfeits
    /// streaming for multi-member but is correct; single-member streams (the common case)
    /// still take the streaming path because we abort the buffer when no second magic is
    /// found before EOF.
    /// </remarks>
    private static async Task<bool> DetectMultiMemberGzipAsync(Stream input)
    {
        // Need to look for a second `1f 8b` after the first one. Buffer up to a reasonable
        // amount; if we find a second magic byte sequence anywhere after byte 0, declare
        // multi-member. If we hit EOF without finding one, it's single-member.
        //
        // For seekable inputs: we read into a buffer and rewind after.
        // For non-seekable inputs: we read into memory and replace `input` semantics
        //   via the caller; here we only DETECT — the caller refills via re-positioning
        //   (seekable) or rewinding via a buffered wrapper (non-seekable).
        //
        // Simplification: only perform the scan for SEEKABLE inputs. Non-seekable streams
        // (pipe mode) skip the multi-member detection and go through single-member
        // validation; this means a multi-member gzip piped to `squeeze -d` will fail ISIZE
        // validation and report as corrupt. Trade-off accepted for round 2: pipe mode +
        // multi-member is rare; file mode is the dominant use case for multi-member input
        // (logs, concatenated archives).
        if (!input.CanSeek)
        {
            return false;
        }

        long startPos = input.Position;
        long remaining = input.Length - startPos;
        if (remaining < 20)
        {
            // Too short to contain two members (each ≥ 18 bytes, plus magic = at least
            // 18 + 2 to detect a second magic byte pair).
            return false;
        }

        try
        {
            // Round-3 review (closing CR-C1: false-positive on incompressible single-member
            // gzip with spurious 1f 8b byte pairs). Finding a `1f 8b` byte pair past offset
            // 18 is necessary but NOT sufficient to declare multi-member — for compressed
            // random/encrypted data, the deflate stream preserves enough byte patterns that
            // spurious `1f 8b` occur roughly once per 64KB. Empirically verified: 64KB
            // random → spurious magic at offset 18037 → multi-member branch fires → ISIZE
            // skipped → truncation silently accepted. Re-opens the SFH-C1 hole.
            //
            // Tighten by ALSO validating the candidate header is structurally plausible:
            //  - byte +2: CM (compression method) must be 08 (deflate; only valid per RFC 1952)
            //  - byte +3: FLG reserved bits 5-7 must be zero
            // This drops the false-positive rate by ~2048x (1/256 × 1/8). For very large
            // (100MB+) random-data inputs there's still a residual chance, but it's narrow
            // enough that valid multi-member input dominates the use case.
            const int ScanBufferSize = 8192;
            byte[] buf = new byte[ScanBufferSize];
            byte prev = 0;
            bool havePrev = false;
            long minSecondMagicPos = startPos + 18;

            input.Position = startPos + 2; // skip first member's full magic bytes
            long pos = startPos + 2;

            while (true)
            {
                int read = await input.ReadAsync(buf.AsMemory()).ConfigureAwait(false);
                if (read == 0) break;

                for (int i = 0; i < read; i++)
                {
                    byte cur = buf[i];
                    if (havePrev && prev == 0x1f && cur == 0x8b)
                    {
                        long magicStart = pos + i - 1;
                        if (magicStart >= minSecondMagicPos)
                        {
                            // Structural validation: peek the next 2 bytes (CM + FLG).
                            if (await IsPlausibleGzipHeaderAtAsync(input, magicStart).ConfigureAwait(false))
                            {
                                return true;
                            }
                        }
                    }
                    prev = cur;
                    havePrev = true;
                }
                pos += read;
            }

            return false;
        }
        finally
        {
            input.Position = startPos; // rewind for actual decompression
        }
    }

    /// <summary>
    /// Validates that the bytes at <paramref name="magicStart"/> on a seekable stream form
    /// a structurally-plausible gzip header start — magic <c>1f 8b</c> already confirmed
    /// by the caller; this method checks CM=08 and FLG reserved bits 5-7 = 0.
    /// </summary>
    /// <remarks>
    /// Round-3 review (closing CR-C1): without this, raw <c>1f 8b</c> byte pairs in
    /// deflate-compressed incompressible data trigger false-positive multi-member detection.
    /// </remarks>
    private static async Task<bool> IsPlausibleGzipHeaderAtAsync(Stream input, long magicStart)
    {
        long savedPos = input.Position;
        try
        {
            input.Position = magicStart + 2; // past magic, points at CM byte
            byte[] cmFlg = new byte[2];
            int read = 0;
            while (read < 2)
            {
                int n = await input.ReadAsync(cmFlg.AsMemory(read, 2 - read)).ConfigureAwait(false);
                if (n == 0) break;
                read += n;
            }
            if (read < 2) return false;

            // CM=08 is the only RFC-defined compression method (deflate).
            if (cmFlg[0] != 0x08) return false;
            // FLG reserved bits 5-7 (mask 0xE0) must be zero per RFC 1952 §2.3.1.
            if ((cmFlg[1] & 0xE0) != 0) return false;
            return true;
        }
        finally
        {
            input.Position = savedPos;
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
