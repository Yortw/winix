namespace Winix.Squeeze;

/// <summary>
/// Detects compression format from magic bytes and file extension.
/// </summary>
public static class FormatDetector
{
    private static ReadOnlySpan<byte> GzipMagic => new byte[] { 0x1f, 0x8b };
    private static ReadOnlySpan<byte> ZstdMagic => new byte[] { 0x28, 0xb5, 0x2f, 0xfd };

    /// <summary>
    /// Maximum number of header bytes needed for magic byte detection.
    /// </summary>
    public const int MaxHeaderSize = 4;

    /// <summary>
    /// Attempts to identify the compression format from the first bytes of data.
    /// Returns null if no known magic bytes match.
    /// </summary>
    public static CompressionFormat? DetectFromMagicBytes(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 4 && header.Slice(0, 4).SequenceEqual(ZstdMagic))
        {
            return CompressionFormat.Zstd;
        }

        if (header.Length >= 2 && header.Slice(0, 2).SequenceEqual(GzipMagic))
        {
            return CompressionFormat.Gzip;
        }

        return null;
    }

    /// <summary>
    /// Attempts to identify the compression format from a file extension.
    /// Comparison is case-insensitive. Returns null if the extension is not recognised.
    /// </summary>
    public static CompressionFormat? DetectFromExtension(string filename)
    {
        if (string.IsNullOrEmpty(filename))
        {
            return null;
        }

        string ext = Path.GetExtension(filename);

        if (ext.Equals(".gz", StringComparison.OrdinalIgnoreCase))
        {
            return CompressionFormat.Gzip;
        }

        if (ext.Equals(".br", StringComparison.OrdinalIgnoreCase))
        {
            return CompressionFormat.Brotli;
        }

        if (ext.Equals(".zst", StringComparison.OrdinalIgnoreCase))
        {
            return CompressionFormat.Zstd;
        }

        return null;
    }

    /// <summary>
    /// Reads the header bytes from a stream and attempts format detection using the full
    /// priority chain: magic bytes, extension hint, then null if unrecognised.
    /// The stream is not rewound -- the caller receives the header bytes that were read
    /// and must prepend them when decompressing.
    /// </summary>
    public static async Task<(CompressionFormat? Format, byte[] HeaderBytes)> DetectFromStreamAsync(
        Stream stream, string? filename)
    {
        byte[] header = new byte[MaxHeaderSize];
        int bytesRead = 0;
        while (bytesRead < MaxHeaderSize)
        {
            int read = await stream.ReadAsync(header.AsMemory(bytesRead, MaxHeaderSize - bytesRead));
            if (read == 0)
            {
                break;
            }
            bytesRead += read;
        }

        byte[] actualHeader = bytesRead == MaxHeaderSize ? header : header[..bytesRead];

        // Magic bytes are definitive for gzip and zstd
        CompressionFormat? format = DetectFromMagicBytes(actualHeader);
        if (format.HasValue)
        {
            return (format, actualHeader);
        }

        // Extension hint covers brotli and ambiguous cases
        if (filename is not null)
        {
            format = DetectFromExtension(filename);
            if (format.HasValue)
            {
                return (format, actualHeader);
            }
        }

        // Unknown -- caller will attempt brotli then raw deflate as fallbacks
        return (null, actualHeader);
    }
}
