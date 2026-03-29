using System.Reflection;
using Winix.Squeeze;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    // --- Parse arguments ---
    bool decompress = false;
    CompressionFormat? formatFlag = null;
    int? levelFlag = null;
    bool stdout = false;
    string? outputFile = null;
    bool force = false;
    bool remove = false;
    bool verbose = false;
    bool quiet = false;
    bool jsonOutput = false;
    bool colorFlag = false;
    bool noColorFlag = false;
    List<string> files = new();

    for (int i = 0; i < args.Length; i++)
    {
        string arg = args[i];

        // -- stops flag parsing
        if (arg == "--")
        {
            for (int j = i + 1; j < args.Length; j++)
            {
                files.Add(args[j]);
            }
            break;
        }

        switch (arg)
        {
            case "-d":
            case "--decompress":
                decompress = true;
                break;
            case "-b":
            case "--brotli":
                formatFlag = CompressionFormat.Brotli;
                break;
            case "-z":
            case "--zstd":
                formatFlag = CompressionFormat.Zstd;
                break;
            case "--level":
                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out int parsedLevel))
                {
                    return WriteUsageError("--level requires a numeric argument", jsonOutput);
                }
                levelFlag = parsedLevel;
                i++;
                break;
            case "-1": levelFlag = 1; break;
            case "-2": levelFlag = 2; break;
            case "-3": levelFlag = 3; break;
            case "-4": levelFlag = 4; break;
            case "-5": levelFlag = 5; break;
            case "-6": levelFlag = 6; break;
            case "-7": levelFlag = 7; break;
            case "-8": levelFlag = 8; break;
            case "-9": levelFlag = 9; break;
            case "-c":
            case "--stdout":
                stdout = true;
                break;
            case "-o":
            case "--output":
                if (i + 1 >= args.Length)
                {
                    return WriteUsageError("-o requires a filename argument", jsonOutput);
                }
                outputFile = args[i + 1];
                if (outputFile == "-")
                {
                    stdout = true;
                    outputFile = null;
                }
                i++;
                break;
            case "-f":
            case "--force":
                force = true;
                break;
            case "--remove":
                remove = true;
                break;
            case "-k":
            case "--keep":
                // No-op -- keep is default (gzip compat)
                break;
            case "-v":
            case "--verbose":
                verbose = true;
                break;
            case "-q":
            case "--quiet":
                quiet = true;
                break;
            case "--json":
                jsonOutput = true;
                break;
            case "--color":
                colorFlag = true;
                break;
            case "--no-color":
                noColorFlag = true;
                break;
            case "--version":
                Console.WriteLine($"squeeze {GetVersion()}");
                return 0;
            case "-h":
            case "--help":
                PrintHelp();
                return 0;
            default:
                if (arg.StartsWith('-'))
                {
                    return WriteUsageError($"unknown option: {arg}", jsonOutput);
                }
                files.Add(arg);
                break;
        }
    }

    string version = GetVersion();

    // --- Validate arguments ---
    CompressionFormat format = formatFlag ?? CompressionFormat.Gzip;
    int level = levelFlag ?? CompressionFormatInfo.GetDefaultLevel(format);

    if (levelFlag.HasValue && !CompressionFormatInfo.IsLevelValid(format, levelFlag.Value))
    {
        var (_, _, min, max) = CompressionFormatInfo.GetMetadata(format);
        return WriteUsageError(
            $"level {levelFlag.Value} out of range for {CompressionFormatInfo.GetShortName(format)} ({min}-{max})",
            jsonOutput);
    }

    if (outputFile is not null && files.Count > 1)
    {
        return WriteUsageError("-o cannot be used with multiple input files", jsonOutput);
    }

    // --- Resolve colour and stats visibility ---
    bool noColorEnv = ConsoleEnv.IsNoColorEnvSet();
    bool isTerminal = ConsoleEnv.IsTerminal(checkStdErr: true);
    bool useColor = ConsoleEnv.ResolveUseColor(colorFlag, noColorFlag, noColorEnv, isTerminal);
    bool showStats = !jsonOutput && !quiet && (verbose || isTerminal);

    // --- Pipe mode ---
    if (files.Count == 0 && Console.IsInputRedirected)
    {
        return await RunPipeModeAsync(
            decompress, format, level, formatFlag, jsonOutput, version);
    }

    // --- No files and no pipe ---
    if (files.Count == 0)
    {
        return WriteUsageError("no input files. Run 'squeeze --help' for usage.", jsonOutput);
    }

    // --- File mode ---
    int exitCode = 0;
    List<SqueezeResult> results = new();

    foreach (string file in files)
    {
        FileOperationResult opResult;
        string? thisOutput = files.Count == 1 ? outputFile : null;

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

static int WriteUsageError(string message, bool jsonOutput)
{
    if (jsonOutput)
    {
        Console.Error.WriteLine(
            Formatting.FormatJsonError(2, "usage_error", "squeeze", GetVersion()));
    }
    else
    {
        Console.Error.WriteLine($"squeeze: {message}");
    }
    return 2;
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        Usage: squeeze [options] [file...]

        Compress and decompress files using gzip, brotli, or zstd.

        Options:
          -d, --decompress    Decompress (auto-detects format)
          --brotli, -b        Use brotli format
          --zstd, -z          Use zstd format
          --level N           Compression level (format-specific range)
          -1..-9              Compression level shortcut
          -c, --stdout        Write to stdout
          -o, --output FILE   Output file (single input only)
          -f, --force         Overwrite existing output files
          --remove            Delete input file after success
          -v, --verbose       Show stats even when piped
          -q, --quiet         Suppress stats even on terminal
          --json              JSON output (to stderr)
          --no-color          Disable colored output
          --color             Force colored output
          --version           Show version
          -h, --help          Show help

        Compatibility:
          These flags match gzip for muscle memory:
          -d                  Same as --decompress
          -c                  Same as --stdout
          -k                  Accepted (keep is default, no-op)
          -1..-9              Same as --level 1..9
          -v                  Same as --verbose
          -f                  Same as --force

        Exit Codes:
          0    Success
          1    Compression/decompression error
          2    Usage error
        """);
}

static string GetVersion()
{
    return typeof(SqueezeResult).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "0.0.0";
}
