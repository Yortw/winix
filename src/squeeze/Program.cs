using System.Reflection;
using Winix.Squeeze;
using Yort.ShellKit;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    string version = GetVersion();

    var parser = new CommandLineParser("squeeze", version)
        .Description("Compress and decompress files using gzip, brotli, or zstd.")
        .StandardFlags()
        .Flag("--decompress", "-d", "Decompress (auto-detects format)")
        .Flag("--brotli", "-b", "Use brotli format")
        .Flag("--zstd", "-z", "Use zstd format")
        .Flag("--stdout", "-c", "Write to stdout")
        .Flag("--force", "-f", "Overwrite existing output files")
        .Flag("--remove", "Delete input file after success")
        .Flag("--keep", "-k", "Keep original file (default, accepted for gzip compat)")
        .Flag("--verbose", "-v", "Show stats even when piped")
        .Flag("--quiet", "-q", "Suppress stats even on terminal")
        .IntOption("--level", null, "N", "Compression level (format-specific range)")
        .Option("--output", "-o", "FILE", "Output file (single input only)")
        .FlagAlias("-1", "--level", "1")
        .FlagAlias("-2", "--level", "2")
        .FlagAlias("-3", "--level", "3")
        .FlagAlias("-4", "--level", "4")
        .FlagAlias("-5", "--level", "5")
        .FlagAlias("-6", "--level", "6")
        .FlagAlias("-7", "--level", "7")
        .FlagAlias("-8", "--level", "8")
        .FlagAlias("-9", "--level", "9")
        .Positional("file...")
        .UsageErrorCode(2)
        .Section("Compatibility",
            """
            These flags match gzip for muscle memory:
            -d                  Same as --decompress
            -c                  Same as --stdout
            -k                  Accepted (keep is default, no-op)
            -1..-9              Same as --level 1..9
            -v                  Same as --verbose
            -f                  Same as --force
            """)
        .ExitCodes(
            (0, "Success"),
            (1, "Compression/decompression error"),
            (2, "Usage error"));

    var result = parser.Parse(args);
    if (result.IsHandled) return result.ExitCode;
    if (result.HasErrors) return result.WriteErrors(Console.Error);

    bool decompress = result.Has("--decompress");
    bool stdout = result.Has("--stdout");
    bool force = result.Has("--force");
    bool remove = result.Has("--remove");
    bool verbose = result.Has("--verbose");
    bool quiet = result.Has("--quiet");
    bool jsonOutput = result.Has("--json");
    bool useColor = result.ResolveColor();

    // Handle -o with "-" as stdout alias
    string? outputFile = null;
    if (result.Has("--output"))
    {
        string raw = result.GetString("--output");
        if (raw == "-")
        {
            stdout = true;
        }
        else
        {
            outputFile = raw;
        }
    }

    // Format flags → CompressionFormat
    CompressionFormat? formatFlag = null;
    if (result.Has("--brotli"))
    {
        formatFlag = CompressionFormat.Brotli;
    }
    else if (result.Has("--zstd"))
    {
        formatFlag = CompressionFormat.Zstd;
    }

    int? levelFlag = null;
    if (result.Has("--level"))
    {
        levelFlag = result.GetInt("--level");
    }

    string[] files = result.Positionals;

    // --- Validate arguments ---
    CompressionFormat format = formatFlag ?? CompressionFormat.Gzip;
    int level = levelFlag ?? CompressionFormatInfo.GetDefaultLevel(format);

    if (levelFlag.HasValue && !CompressionFormatInfo.IsLevelValid(format, levelFlag.Value))
    {
        var (_, _, min, max) = CompressionFormatInfo.GetMetadata(format);
        return WriteUsageError(
            $"level {levelFlag.Value} out of range for {CompressionFormatInfo.GetShortName(format)} ({min}-{max})",
            jsonOutput, version);
    }

    if (outputFile is not null && files.Length > 1)
    {
        return WriteUsageError("-o cannot be used with multiple input files", jsonOutput, version);
    }

    // --- Resolve stats visibility ---
    bool isTerminal = ConsoleEnv.IsTerminal(checkStdErr: true);
    bool showStats = !jsonOutput && !quiet && (verbose || isTerminal);

    // --- Pipe mode ---
    if (files.Length == 0 && Console.IsInputRedirected)
    {
        return await RunPipeModeAsync(
            decompress, format, level, formatFlag, jsonOutput, version);
    }

    // --- No files and no pipe ---
    if (files.Length == 0)
    {
        return WriteUsageError("no input files. Run 'squeeze --help' for usage.", jsonOutput, version);
    }

    // --- File mode ---
    int exitCode = 0;
    List<SqueezeResult> results = new();

    foreach (string file in files)
    {
        FileOperationResult opResult;
        string? thisOutput = files.Length == 1 ? outputFile : null;

        if (stdout)
        {
            opResult = await RunStdoutModeAsync(
                file, decompress, format, level, formatFlag);
        }
        else if (decompress)
        {
            opResult = await FileOperations.DecompressFileAsync(
                file, thisOutput, formatFlag, force, remove);
        }
        else
        {
            opResult = await FileOperations.CompressFileAsync(
                file, thisOutput, format, level, force, remove);
        }

        if (opResult.ExitCode != 0)
        {
            if (jsonOutput)
            {
                Console.Error.WriteLine(
                    Formatting.FormatJsonError(opResult.ExitCode, opResult.ExitReason, "squeeze", version));
            }
            else
            {
                Console.Error.WriteLine(opResult.ErrorMessage);
            }

            exitCode = opResult.ExitCode;
            continue;
        }

        if (opResult.Result is not null)
        {
            results.Add(opResult.Result);

            if (showStats)
            {
                Console.Error.WriteLine(Formatting.FormatHuman(opResult.Result, useColor));
            }
        }
    }

    if (jsonOutput && results.Count > 0)
    {
        Console.Error.WriteLine(
            Formatting.FormatJson(results, exitCode, exitCode == 0 ? "success" : "partial_failure",
                "squeeze", version));
    }

    return exitCode;
}

static async Task<int> RunPipeModeAsync(
    bool decompress, CompressionFormat format, int level,
    CompressionFormat? explicitFormat, bool jsonOutput, string version)
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    long inputBytes = 0;
    long outputBytes = 0;

    try
    {
        using var stdin = Console.OpenStandardInput();
        using var stdoutStream = Console.OpenStandardOutput();

        // Buffer stdin so we can count bytes
        using var inputBuffer = new MemoryStream();
        await stdin.CopyToAsync(inputBuffer);
        inputBytes = inputBuffer.Length;
        inputBuffer.Position = 0;

        if (decompress)
        {
            if (explicitFormat.HasValue)
            {
                await Compressor.DecompressAsync(inputBuffer, stdoutStream, explicitFormat.Value);
            }
            else
            {
                var detected = await Compressor.DecompressAutoDetectAsync(
                    inputBuffer, stdoutStream, filename: null);

                if (!detected.HasValue)
                {
                    if (jsonOutput)
                    {
                        Console.Error.WriteLine(
                            Formatting.FormatJsonError(1, "corrupt_input", "squeeze", version));
                    }
                    else
                    {
                        Console.Error.WriteLine("squeeze: <stdin>: unrecognised format");
                    }
                    return 1;
                }

                format = detected.Value;
            }
        }
        else
        {
            using var countingOutput = new MemoryStream();
            await Compressor.CompressAsync(inputBuffer, countingOutput, format, level);
            countingOutput.Position = 0;
            outputBytes = countingOutput.Length;
            await countingOutput.CopyToAsync(stdoutStream);
        }
    }
    catch (Exception ex)
    {
        string reason = decompress ? "corrupt_input" : "io_error";
        if (jsonOutput)
        {
            Console.Error.WriteLine(Formatting.FormatJsonError(1, reason, "squeeze", version));
        }
        else
        {
            Console.Error.WriteLine($"squeeze: <stdin>: {ex.Message}");
        }
        return 1;
    }

    stopwatch.Stop();

    if (jsonOutput)
    {
        var result = new SqueezeResult("<stdin>", "<stdout>", inputBytes, outputBytes,
            format, stopwatch.Elapsed);
        Console.Error.WriteLine(
            Formatting.FormatJson(new[] { result }, 0, "success", "squeeze", version));
    }

    return 0;
}

static async Task<FileOperationResult> RunStdoutModeAsync(
    string inputPath, bool decompress, CompressionFormat format, int level,
    CompressionFormat? explicitFormat)
{
    if (!File.Exists(inputPath))
    {
        return new FileOperationResult(1, "file_not_found", null,
            $"squeeze: {inputPath}: No such file");
    }

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    long inputBytes;

    try
    {
        using var inputStream = File.OpenRead(inputPath);
        inputBytes = inputStream.Length;

        using var stdoutStream = Console.OpenStandardOutput();
        using var countingOutput = new MemoryStream();

        if (decompress)
        {
            if (explicitFormat.HasValue)
            {
                await Compressor.DecompressAsync(inputStream, countingOutput, explicitFormat.Value);
            }
            else
            {
                var detected = await Compressor.DecompressAutoDetectAsync(
                    inputStream, countingOutput, filename: inputPath);

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
            await Compressor.CompressAsync(inputStream, countingOutput, format, level);
        }

        long outputBytes = countingOutput.Length;
        countingOutput.Position = 0;
        await countingOutput.CopyToAsync(stdoutStream);

        stopwatch.Stop();

        var result = new SqueezeResult(inputPath, "<stdout>", inputBytes, outputBytes,
            format, stopwatch.Elapsed);
        return new FileOperationResult(0, "success", result, null);
    }
    catch (Exception ex)
    {
        return new FileOperationResult(1, decompress ? "corrupt_input" : "io_error", null,
            $"squeeze: {inputPath}: {ex.Message}");
    }
}

static int WriteUsageError(string message, bool jsonOutput, string version)
{
    if (jsonOutput)
    {
        Console.Error.WriteLine(
            Formatting.FormatJsonError(2, "usage_error", "squeeze", version));
    }
    else
    {
        Console.Error.WriteLine($"squeeze: {message}");
    }
    return 2;
}

static string GetVersion()
{
    return typeof(SqueezeResult).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "0.0.0";
}
