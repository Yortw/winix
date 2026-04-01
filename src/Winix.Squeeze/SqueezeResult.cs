namespace Winix.Squeeze;

/// <summary>
/// Immutable result of compressing or decompressing a single file or stream.
/// </summary>
public sealed record SqueezeResult(
    string InputPath,
    string OutputPath,
    long InputBytes,
    long OutputBytes,
    CompressionFormat Format,
    int Level,
    TimeSpan Elapsed
)
{
    /// <summary>
    /// Compression ratio as a fraction (0.0 to 1.0+). A ratio of 0.5 means the output is
    /// 50% smaller than the input. Zero input bytes yields a ratio of 0.0.
    /// </summary>
    public double Ratio => InputBytes > 0 ? 1.0 - ((double)OutputBytes / InputBytes) : 0.0;
}
