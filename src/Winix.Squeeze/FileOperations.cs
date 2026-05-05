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
            var (reason, message) = ClassifyIoException(ex, inputPath, decompress: false);
            return new FileOperationResult(1, reason, null, message);
        }

        stopwatch.Stop();

        if (remove)
        {
            // Round-1 review TA-I8: surface --remove deletion failures rather than
            // silently swallowing them. Compression succeeded, so exit 0 is preserved,
            // but the user gets a stderr warning that the original wasn't removed.
            if (!TryDeleteFileWithStatus(inputPath, out string? failureReason))
            {
                Console.Error.WriteLine(
                    $"squeeze: warning: failed to remove input '{inputPath}': {failureReason}");
            }
        }

        var result = new SqueezeResult(inputPath, resolvedOutput, inputBytes, outputBytes, format, level, stopwatch.Elapsed);
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
            var (reason, message) = ClassifyIoException(ex, inputPath, decompress: true);
            return new FileOperationResult(1, reason, null, message);
        }

        stopwatch.Stop();

        if (remove)
        {
            // Round-1 review TA-I8: surface --remove deletion failures (see CompressFileAsync
            // for full rationale). Decompression succeeded; exit 0 preserved.
            if (!TryDeleteFileWithStatus(inputPath, out string? failureReason))
            {
                Console.Error.WriteLine(
                    $"squeeze: warning: failed to remove input '{inputPath}': {failureReason}");
            }
        }

        var result = new SqueezeResult(inputPath, resolvedOutput, inputBytes, outputBytes, detectedFormat, 0, stopwatch.Elapsed);
        return new FileOperationResult(0, "success", result, null);
    }

    /// <summary>
    /// Processes a file to an output stream — compresses or decompresses with byte counting.
    /// Used for --stdout mode where the output goes to a stream rather than a file.
    /// </summary>
    /// <param name="inputPath">Path to the input file.</param>
    /// <param name="output">Stream to write output to.</param>
    /// <param name="decompress">True to decompress, false to compress.</param>
    /// <param name="format">Compression format (used for compression).</param>
    /// <param name="level">Compression level (only used when compressing).</param>
    /// <param name="explicitFormat">When set, decompress using this format instead of auto-detecting.</param>
    public static async Task<FileOperationResult> ProcessFileToStreamAsync(
        string inputPath, Stream output,
        bool decompress, CompressionFormat format, int level,
        CompressionFormat? explicitFormat)
    {
        if (!File.Exists(inputPath))
        {
            return new FileOperationResult(1, "file_not_found", null,
                $"squeeze: {inputPath}: No such file");
        }

        var stopwatch = Stopwatch.StartNew();
        long inputBytes;

        try
        {
            using var inputStream = File.OpenRead(inputPath);
            inputBytes = inputStream.Length;

            // Stream directly to the output through a counting wrapper instead of
            // buffering the entire result in a MemoryStream. The old approach would
            // OOM on large files since it held the full result in memory.
            using var countingOutput = new CountingStream(output);

            if (decompress)
            {
                if (explicitFormat.HasValue)
                {
                    await Compressor.DecompressAsync(inputStream, countingOutput, explicitFormat.Value)
                        .ConfigureAwait(false);
                }
                else
                {
                    CompressionFormat? detected = await Compressor.DecompressAutoDetectAsync(
                        inputStream, countingOutput, filename: inputPath).ConfigureAwait(false);

                    if (!detected.HasValue)
                    {
                        return new FileOperationResult(1, "corrupt_input", null,
                            $"squeeze: {inputPath}: unrecognised format");
                    }

                    format = detected.Value;
                }
            }
            else
            {
                await Compressor.CompressAsync(inputStream, countingOutput, format, level)
                    .ConfigureAwait(false);
            }

            await countingOutput.FlushAsync().ConfigureAwait(false);
            long outputBytes = countingOutput.BytesWritten;

            stopwatch.Stop();

            var result = new SqueezeResult(inputPath, "<stdout>", inputBytes, outputBytes,
                format, decompress ? 0 : level, stopwatch.Elapsed);
            return new FileOperationResult(0, "success", result, null);
        }
        catch (Exception ex)
        {
            var (reason, message) = ClassifyIoException(ex, inputPath, decompress);
            return new FileOperationResult(1, reason, null, message);
        }
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

    /// <summary>
    /// Best-effort delete that returns whether deletion succeeded. Used by --remove so a
    /// silent failure to delete the original (e.g. file locked by antivirus, read-only
    /// attribute) can be surfaced to the user instead of being swallowed.
    /// </summary>
    /// <remarks>
    /// Round-1 review TA-I8: <see cref="TryDeleteFile"/> swallows all exceptions, which is
    /// the correct contract for partial-output cleanup but the wrong one for --remove.
    /// </remarks>
    internal static bool TryDeleteFileWithStatus(string path, out string? failureReason)
    {
        try
        {
            File.Delete(path);
            failureReason = null;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            failureReason = "permission denied";
            return false;
        }
        catch (IOException)
        {
            // File-locked-by-another-process / read-only attribute / etc.
            failureReason = "file is in use or read-only";
            return false;
        }
    }

    /// <summary>
    /// Maps a framework exception thrown during compress/decompress I/O to a clean
    /// project-controlled English message + exit-reason classifier.
    /// </summary>
    /// <remarks>
    /// Round-1 review CR-C1 / SFH-C2/C3: piping <c>ex.Message</c> from framework exceptions
    /// (IOException / DirectoryNotFoundException / UnauthorizedAccessException /
    /// ArgumentException) to user output leaks resource keys (<c>IO_PathNotFound_Path</c>,
    /// <c>Arg_ParamName_Name</c>) under <c>InvariantGlobalization=true</c>. Classify by
    /// exception subtype and emit project-controlled English; reserve <c>ex.Message</c> as
    /// a fallback for genuinely unexpected I/O codes (with the exception type prefixed so
    /// the user has actionable context).
    /// </remarks>
    internal static (string ExitReason, string Message) ClassifyIoException(
        Exception ex, string contextPath, bool decompress)
    {
        return ex switch
        {
            DirectoryNotFoundException =>
                ("io_error", $"squeeze: {contextPath}: parent directory does not exist"),
            UnauthorizedAccessException =>
                ("io_error", $"squeeze: {contextPath}: permission denied"),
            FileNotFoundException =>
                ("file_not_found", $"squeeze: {contextPath}: no such file"),
            PathTooLongException =>
                ("io_error", $"squeeze: {contextPath}: path too long"),
            InvalidDataException =>
                (decompress ? "corrupt_input" : "compress_failed",
                 $"squeeze: {contextPath}: data is corrupt or truncated"),
            ArgumentException =>
                ("io_error", $"squeeze: {contextPath}: invalid path"),
            // Fallback: prefix the exception type so the user can search for the underlying
            // cause even if ex.Message is a resource key.
            _ => (decompress ? "decompress_failed" : "compress_failed",
                 $"squeeze: {contextPath} ({ex.GetType().Name}): {ex.Message}"),
        };
    }
}
