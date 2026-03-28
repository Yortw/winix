namespace Winix.Squeeze;

/// <summary>
/// Supported compression formats.
/// </summary>
public enum CompressionFormat
{
    /// <summary>gzip (RFC 1952).</summary>
    Gzip,

    /// <summary>Brotli (RFC 7932).</summary>
    Brotli,

    /// <summary>Zstandard (RFC 8878).</summary>
    Zstd
}

/// <summary>
/// Metadata and validation for <see cref="CompressionFormat"/> values.
/// </summary>
public static class CompressionFormatInfo
{
    /// <summary>
    /// Returns the file extension (including leading dot) for the given format.
    /// </summary>
    public static string GetExtension(CompressionFormat format) => format switch
    {
        CompressionFormat.Gzip => ".gz",
        CompressionFormat.Brotli => ".br",
        CompressionFormat.Zstd => ".zst",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    /// <summary>
    /// Returns the short name used in stats display and JSON output.
    /// </summary>
    public static string GetShortName(CompressionFormat format) => format switch
    {
        CompressionFormat.Gzip => "gz",
        CompressionFormat.Brotli => "br",
        CompressionFormat.Zstd => "zst",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    /// <summary>
    /// Returns the magic bytes that identify the format in a stream, or null if the format
    /// has no magic bytes (brotli).
    /// </summary>
    public static byte[]? GetMagicBytes(CompressionFormat format) => format switch
    {
        CompressionFormat.Gzip => new byte[] { 0x1f, 0x8b },
        CompressionFormat.Zstd => new byte[] { 0x28, 0xb5, 0x2f, 0xfd },
        CompressionFormat.Brotli => null,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    /// <summary>
    /// Returns format metadata as a tuple of (Extension, DefaultLevel, MinLevel, MaxLevel).
    /// </summary>
    public static (string Extension, int DefaultLevel, int MinLevel, int MaxLevel) GetMetadata(
        CompressionFormat format) => format switch
    {
        CompressionFormat.Gzip => (".gz", 6, 1, 9),
        CompressionFormat.Brotli => (".br", 6, 0, 11),
        CompressionFormat.Zstd => (".zst", 3, 1, 22),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    /// <summary>
    /// Returns the default compression level for the given format.
    /// </summary>
    public static int GetDefaultLevel(CompressionFormat format) => GetMetadata(format).DefaultLevel;

    /// <summary>
    /// Returns true if the given level is within the valid range for the format.
    /// </summary>
    public static bool IsLevelValid(CompressionFormat format, int level)
    {
        var (_, _, min, max) = GetMetadata(format);
        return level >= min && level <= max;
    }
}
