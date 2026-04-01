using System.Diagnostics;

namespace Winix.Squeeze;

/// <summary>
/// Stream-to-stream compress/decompress operations for pipe mode (stdin → stdout).
/// </summary>
public static class PipeOperations
{
    /// <summary>
    /// Processes a stream-to-stream compress or decompress operation with byte counting
    /// and error handling.
    /// </summary>
    /// <param name="input">Input stream to read from.</param>
    /// <param name="output">Output stream to write to.</param>
    /// <param name="decompress">True to decompress, false to compress.</param>
    /// <param name="format">Compression format (used for compression and as fallback label).</param>
    /// <param name="level">Compression level (only used when compressing).</param>
    /// <param name="explicitFormat">When set, decompress using this format instead of auto-detecting.</param>
    public static async Task<FileOperationResult> ProcessAsync(
        Stream input, Stream output,
        bool decompress, CompressionFormat format, int level,
        CompressionFormat? explicitFormat)
    {
        var stopwatch = Stopwatch.StartNew();
        long inputBytes = 0;
        long outputBytes = 0;

        try
        {
            // Buffer input so we can count bytes before processing
            using var inputBuffer = new MemoryStream();
            await input.CopyToAsync(inputBuffer).ConfigureAwait(false);
            inputBytes = inputBuffer.Length;
            inputBuffer.Position = 0;

            if (decompress)
            {
                // Decompress into an intermediate buffer to capture output byte count,
                // then copy to the real output. Mirrors the compression path.
                // Without this, non-seekable streams (stdout) would report 0 output bytes.
                using var countingOutput = new MemoryStream();

                if (explicitFormat.HasValue)
                {
                    await Compressor.DecompressAsync(inputBuffer, countingOutput, explicitFormat.Value)
                        .ConfigureAwait(false);
                }
                else
                {
                    CompressionFormat? detected = await Compressor.DecompressAutoDetectAsync(
                        inputBuffer, countingOutput, filename: null).ConfigureAwait(false);

                    if (!detected.HasValue)
                    {
                        return new FileOperationResult(1, "corrupt_input", null,
                            "unrecognised format");
                    }

                    format = detected.Value;
                }

                outputBytes = countingOutput.Length;
                countingOutput.Position = 0;
                await countingOutput.CopyToAsync(output).ConfigureAwait(false);
            }
            else
            {
                // Compress into an intermediate buffer to capture output byte count
                using var countingOutput = new MemoryStream();
                await Compressor.CompressAsync(inputBuffer, countingOutput, format, level)
                    .ConfigureAwait(false);
                outputBytes = countingOutput.Length;
                countingOutput.Position = 0;
                await countingOutput.CopyToAsync(output).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            string reason = decompress ? "corrupt_input" : "io_error";
            return new FileOperationResult(1, reason, null, ex.Message);
        }

        stopwatch.Stop();

        var result = new SqueezeResult("<stdin>", "<stdout>", inputBytes, outputBytes,
            format, decompress ? 0 : level, stopwatch.Elapsed);
        return new FileOperationResult(0, "success", result, null);
    }
}
