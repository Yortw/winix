using System.Diagnostics;

namespace Winix.Squeeze;

/// <summary>
/// Result of a file-level compress or decompress operation, including exit code and optional error details.
/// </summary>
public sealed record FileOperationResult(
    int ExitCode,
    string ExitReason,
    SqueezeResult? Result,
    string? ErrorMessage
);

/// <summary>
/// File-level compress/decompress operations with overwrite protection, partial-output cleanup,
/// and optional input removal.
/// </summary>
public static class FileOperations
{
    /// <summary>
    /// Returns the output path for compressing the given input file with the specified format.
    /// Appends the format's file extension (e.g. "data.txt" + gzip → "data.txt.gz").
    /// </summary>
    public static string GetCompressOutputPath(string inputPath, CompressionFormat format)
    {
        return inputPath + CompressionFormatInfo.GetExtension(format);
    }

    /// <summary>
    /// Returns the output path for decompressing the given input file by stripping a known
    /// compression extension, or null if the extension is not recognised.
    /// </summary>
    public static string? GetDecompressOutputPath(string inputPath)
    {
        string ext = Path.GetExtension(inputPath);

        if (ext.Equals(".gz", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".br", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".zst", StringComparison.OrdinalIgnoreCase))
        {
            return inputPath.Substring(0, inputPath.Length - ext.Length);
        }

        return null;
    }

    /// <summary>
    /// Compresses a file to the specified format with overwrite protection and optional input removal.
    /// </summary>
    /// <param name="inputPath">Path to the file to compress.</param>
    /// <param name="outputPath">
    /// Explicit output path, or null to auto-generate by appending the format extension.
    /// </param>
    /// <param name="format">Compression format to use.</param>
    /// <param name="level">Format-specific compression level.</param>
    /// <param name="force">When true, overwrites an existing output file.</param>
    /// <param name="remove">When true, deletes the input file after successful compression.</param>
    public static async Task<FileOperationResult> CompressFileAsync(
        string inputPath,
        string? outputPath,
        CompressionFormat format,
        int level,
        bool force,
        bool remove)
    {
        if (!File.Exists(inputPath))
        {
            return new FileOperationResult(1, "file_not_found", null, $"File not found: {inputPath}");
        }

        string resolvedOutput = outputPath ?? GetCompressOutputPath(inputPath, format);

        if (!force && File.Exists(resolvedOutput))
        {
            return new FileOperationResult(1, "output_exists", null, $"Output file already exists: {resolvedOutput}");
        }

        var stopwatch = Stopwatch.StartNew();
        long inputBytes;
        long outputBytes;

        try
        {
            using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                inputBytes = input.Length;

                using (var output = new FileStream(resolvedOutput, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await Compressor.CompressAsync(input, output, format, level).ConfigureAwait(false);
                    outputBytes = output.Length;
                }
            }
        }
        catch (Exception ex)
        {
            TryDeleteFile(resolvedOutput);
            return new FileOperationResult(1, "compress_failed", null, ex.Message);
        }

        stopwatch.Stop();

        if (remove)
        {
            TryDeleteFile(inputPath);
        }

        var result = new SqueezeResult(inputPath, resolvedOutput, inputBytes, outputBytes, format, stopwatch.Elapsed);
        return new FileOperationResult(0, "success", result, null);
    }

    /// <summary>
    /// Decompresses a file with auto-detection, overwrite protection, and optional input removal.
    /// </summary>
    /// <param name="inputPath">Path to the compressed file.</param>
    /// <param name="outputPath">
    /// Explicit output path, or null to auto-generate by stripping the compression extension.
    /// </param>
    /// <param name="explicitFormat">
    /// When specified, uses this format instead of auto-detecting. Useful when the file has
    /// a non-standard extension.
    /// </param>
    /// <param name="force">When true, overwrites an existing output file.</param>
    /// <param name="remove">When true, deletes the input file after successful decompression.</param>
    public static async Task<FileOperationResult> DecompressFileAsync(
        string inputPath,
        string? outputPath,
        CompressionFormat? explicitFormat,
        bool force,
        bool remove)
    {
        if (!File.Exists(inputPath))
        {
            return new FileOperationResult(1, "file_not_found", null, $"File not found: {inputPath}");
        }

        string? resolvedOutput = outputPath ?? GetDecompressOutputPath(inputPath);

        if (resolvedOutput is null)
        {
            return new FileOperationResult(1, "unknown_extension", null,
                $"Cannot determine output filename: unknown extension on {inputPath}. Use -o to specify output path.");
        }

        if (!force && File.Exists(resolvedOutput))
        {
            return new FileOperationResult(1, "output_exists", null, $"Output file already exists: {resolvedOutput}");
        }

        var stopwatch = Stopwatch.StartNew();
        long inputBytes;
        long outputBytes;
        CompressionFormat detectedFormat;

        try
        {
            using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                inputBytes = input.Length;

                using (var output = new FileStream(resolvedOutput, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    if (explicitFormat.HasValue)
                    {
                        await Compressor.DecompressAsync(input, output, explicitFormat.Value).ConfigureAwait(false);
                        detectedFormat = explicitFormat.Value;
                    }
                    else
                    {
                        CompressionFormat? detected = await Compressor.DecompressAutoDetectAsync(
                            input, output, Path.GetFileName(inputPath)).ConfigureAwait(false);

                        if (!detected.HasValue)
                        {
                            // Clean up partial output before returning error
                            output.Close();
                            TryDeleteFile(resolvedOutput);
                            return new FileOperationResult(1, "corrupt_input", null,
                                $"Could not detect compression format for {inputPath}");
                        }

                        detectedFormat = detected.Value;
                    }

                    outputBytes = output.Length;
                }
            }
        }
        catch (Exception ex)
        {
            TryDeleteFile(resolvedOutput);
            return new FileOperationResult(1, "decompress_failed", null, ex.Message);
        }

        stopwatch.Stop();

        if (remove)
        {
            TryDeleteFile(inputPath);
        }

        var result = new SqueezeResult(inputPath, resolvedOutput, inputBytes, outputBytes, detectedFormat, stopwatch.Elapsed);
        return new FileOperationResult(0, "success", result, null);
    }

    /// <summary>
    /// Best-effort file deletion. Swallows all exceptions — used for partial output cleanup
    /// and optional input removal where failure is not fatal.
    /// </summary>
    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup — nothing useful to do if deletion fails
        }
    }
}
