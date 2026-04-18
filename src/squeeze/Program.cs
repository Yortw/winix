using System.Reflection;
using Winix.Squeeze;
using Yort.ShellKit;

namespace Squeeze;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
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
            // Exit code 2 for usage error matches gzip convention, not the POSIX 125
            // used by other Winix tools. Intentional for gzip muscle-memory compatibility.
            .ExitCodes(
                (0, "Success"),
                (1, "Compression/decompression error"),
                (2, "Usage error"))
            .Platform("cross-platform",
                replaces: new[] { "gzip", "brotli", "zstd" },
                valueOnWindows: "Windows ships no compression CLI tools at all",
                valueOnUnix: "Single tool for gzip + brotli + zstd; brotli CLI is rarely installed anywhere")
            .StdinDescription("Reads input data when no file argument given (pipe mode)")
            .StdoutDescription("Compressed/decompressed data in pipe mode")
            .StderrDescription("Status summary. JSON with --json.")
            .Example("squeeze file.csv", "Compress with gzip (default)")
            .Example("squeeze --zstd largefile.bin", "Compress with zstd")
            .Example("squeeze -d file.csv.gz", "Decompress (auto-detect format)")
            .Example("cat dump.sql | squeeze > dump.gz", "Pipe mode compression")
            .Example("squeeze --json file.csv", "Machine-parseable result for CI")
            .ComposesWith("files", "files . --ext log | wargs squeeze --zstd", "Compress all log files")
            .ComposesWith("wargs", "files . --ext csv | wargs squeeze", "Batch compress files")
            .JsonField("tool", "string", "Tool name (\"squeeze\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("exit_code", "int", "Tool exit code (0 = success)")
            .JsonField("exit_reason", "string", "Machine-readable exit reason")
            .JsonField("files", "array", "Array of per-file result objects")
            .JsonField("files[].input", "string", "Input file path")
            .JsonField("files[].output", "string", "Output file path")
            .JsonField("files[].input_bytes", "int", "Original size in bytes")
            .JsonField("files[].output_bytes", "int", "Compressed/decompressed size in bytes")
            .JsonField("files[].ratio", "float", "Compression ratio (0.0–1.0)")
            .JsonField("files[].format", "string", "Compression format (gz, br, zst)")
            .JsonField("files[].seconds", "float", "Processing time in seconds");

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
        bool useColor = result.ResolveColor(checkStdErr: true);

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

        CompressionFormat format = formatFlag ?? CompressionFormat.Gzip;
        int level = levelFlag ?? CompressionFormatInfo.GetDefaultLevel(format);

        if (levelFlag.HasValue && !CompressionFormatInfo.IsLevelValid(format, levelFlag.Value))
        {
            var (_, _, min, max) = CompressionFormatInfo.GetMetadata(format);
            return result.WriteError(
                $"level {levelFlag.Value} out of range for {CompressionFormatInfo.GetShortName(format)} ({min}-{max})",
                Console.Error);
        }

        if (outputFile is not null && files.Length > 1)
        {
            return result.WriteError("-o cannot be used with multiple input files", Console.Error);
        }

        bool isTerminal = ConsoleEnv.IsTerminal(checkStdErr: true);
        bool showStats = !jsonOutput && !quiet && (verbose || isTerminal);

        // --- Pipe mode ---
        if (files.Length == 0 && Console.IsInputRedirected)
        {
            return await RunPipeModeAsync(
                decompress, format, level, formatFlag, jsonOutput, version);
        }

        if (files.Length == 0)
        {
            return result.WriteError("no input files. Run 'squeeze --help' for usage.", Console.Error);
        }

        // --- File mode ---
        int exitCode = 0;
        List<SqueezeResult> results = new();
        List<string> jsonErrors = new();

        foreach (string file in files)
        {
            FileOperationResult opResult;
            string? thisOutput = files.Length == 1 ? outputFile : null;

            if (stdout)
            {
                using var stdoutStream = Console.OpenStandardOutput();
                opResult = await FileOperations.ProcessFileToStreamAsync(
                    file, stdoutStream, decompress, format, level, formatFlag);
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
                    // Collect errors for the JSON envelope rather than emitting separate
                    // JSON objects, so the consumer receives a single parseable document.
                    jsonErrors.Add(opResult.ErrorMessage ?? $"squeeze: {file}: {opResult.ExitReason}");
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

        if (jsonOutput)
        {
            // Always emit the JSON envelope when --json is set, even if no files succeeded.
            // Errors are included in the same envelope for single-document parseability.
            string exitReason = exitCode == 0 ? "success" : (results.Count > 0 ? "partial_failure" : "failure");
            Console.Error.WriteLine(
                Formatting.FormatJson(results, exitCode, exitReason,
                    "squeeze", version, jsonErrors.Count > 0 ? jsonErrors : null));
        }

        return exitCode;
    }

    private static async Task<int> RunPipeModeAsync(
        bool decompress, CompressionFormat format, int level,
        CompressionFormat? explicitFormat, bool jsonOutput, string version)
    {
        using var stdin = Console.OpenStandardInput();
        using var stdoutStream = Console.OpenStandardOutput();

        var opResult = await PipeOperations.ProcessAsync(
            stdin, stdoutStream, decompress, format, level, explicitFormat);

        if (opResult.ExitCode != 0)
        {
            if (jsonOutput)
            {
                Console.Error.WriteLine(
                    Formatting.FormatJsonError(opResult.ExitCode, opResult.ExitReason, "squeeze", version));
            }
            else
            {
                Console.Error.WriteLine($"squeeze: <stdin>: {opResult.ErrorMessage}");
            }
            return opResult.ExitCode;
        }

        if (jsonOutput && opResult.Result is not null)
        {
            Console.Error.WriteLine(
                Formatting.FormatJson(new[] { opResult.Result }, 0, "success", "squeeze", version));
        }

        return 0;
    }

    private static string GetVersion()
    {
        return typeof(SqueezeResult).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
