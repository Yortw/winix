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
            // Stream through counting wrappers instead of buffering in MemoryStream.
            // The old approach held both full input and full output in memory, which
            // would OOM on large pipes. ReadCountingStream counts input bytes as the
            // compressor/decompressor reads through, and CountingStream counts output
            // bytes as they flow to stdout.
            using var countingInput = new ReadCountingStream(input);
            using var countingOutput = new CountingStream(output);

            if (decompress)
            {
                if (explicitFormat.HasValue)
                {
                    await Compressor.DecompressAsync(countingInput, countingOutput, explicitFormat.Value)
                        .ConfigureAwait(false);
                }
                else
                {
                    CompressionFormat? detected = await Compressor.DecompressAutoDetectAsync(
                        countingInput, countingOutput, filename: null).ConfigureAwait(false);

                    if (!detected.HasValue)
                    {
                        return new FileOperationResult(1, "corrupt_input", null,
                            "unrecognised format");
                    }

                    format = detected.Value;
                }
            }
            else
            {
                await Compressor.CompressAsync(countingInput, countingOutput, format, level)
                    .ConfigureAwait(false);
            }

            await countingOutput.FlushAsync().ConfigureAwait(false);
            inputBytes = countingInput.BytesRead;
            outputBytes = countingOutput.BytesWritten;
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
