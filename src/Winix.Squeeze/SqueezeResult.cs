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
    /// Compression ratio as a fraction. Positive values (0.0 to 1.0) indicate the output is
    /// smaller than the input (0.5 = 50% reduction). Negative values indicate expansion
    /// (the output is larger than the input, common with already-compressed or random data).
    /// Zero input bytes yields 0.0.
    /// </summary>
    public double Ratio => InputBytes > 0 ? 1.0 - ((double)OutputBytes / InputBytes) : 0.0;
}
