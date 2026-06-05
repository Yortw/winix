// src/Winix.Squeeze/Cli.cs
using System.Reflection;
using Yort.ShellKit;

namespace Winix.Squeeze;

/// <summary>
/// Library-level orchestration for the <c>squeeze</c> CLI. Program.cs is a thin shim that
/// resolves real console state (stdin/stdout redirection, terminal detection) and delegates
/// here. Every dispatch path — arg parsing, mutual-exclusion, mode selection, file loop,
/// JSON envelope assembly — is testable from <c>Winix.Squeeze.Tests</c>.
/// </summary>
/// <remarks>
/// Round-1 review CR-I1 / TA-C1: prior to extraction, ~140 LOC of Program.cs orchestration
/// was untestable. The seam here mirrors the digest/notify/url/timeit/ids/when/qr pattern.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Entry point. Parses <paramref name="args"/>, dispatches to file-mode or pipe-mode,
    /// and writes summary/error envelopes to <paramref name="stderr"/>.
    /// </summary>
    /// <param name="args">Raw argv (excluding the binary name).</param>
    /// <param name="stdin">Stdin reader (used only in pipe mode).</param>
    /// <param name="stdout">Where compressed/decompressed bytes go in pipe / --stdout mode.
    /// Tests pass a MemoryStream; real Program.cs passes <c>Console.OpenStandardOutput()</c>.</param>
    /// <param name="stderr">Where summary lines and error envelopes go.</param>
    /// <param name="stdinIsRedirected">True when stdin is piped (i.e. safe to read).</param>
    /// <param name="stdoutIsTerminal">True when stdout is a real terminal (drives <c>showStats</c>).</param>
    /// <returns>Exit code: 0 success, 1 compression error, 2 usage error.</returns>
    public static async Task<int> RunAsync(
        string[] args,
        Stream stdin,
        Stream stdout,
        TextWriter stderr,
        bool stdinIsRedirected,
        bool stdoutIsTerminal)
    {
        string version = GetVersion();

        var parser = BuildParser(version);

        var result = parser.Parse(args);
        if (result.IsHandled) return result.ExitCode;
        if (result.HasErrors) return result.WriteErrors(stderr);

        bool decompress = result.Has("--decompress");
        bool useStdoutFlag = result.Has("--stdout");
        bool force = result.Has("--force");
        bool remove = result.Has("--remove");
        bool keep = result.Has("--keep");
        bool verbose = result.Has("--verbose");
        bool quiet = result.Has("--quiet");
        bool jsonOutput = result.Has("--json");
        bool useColor = result.ResolveColor(checkStdErr: true);

        // Round-1 review TA-I8: --keep and --remove are contradictory. Per gzip(1) -k means
        // "keep input on success" — taking precedence over --remove if both are passed.
        // Pre-fix this was undefined: --keep was inert and --remove silently won. Document
        // the precedence by emitting a warning and honouring --keep.
        if (keep && remove)
        {
            stderr.WriteLine("squeeze: warning: --keep takes precedence over --remove");
            remove = false;
        }

        string? outputFile = null;
        bool stdoutMode = useStdoutFlag;
        if (result.Has("--output"))
        {
            string raw = result.GetString("--output");
            // Round-1 review SFH-C2: empty/whitespace --output crashed with resource-key
            // leak ('Argument_EmptyString Arg_ParamName_Name'). Reject at parse time as
            // usage error 2 (squeeze uses gzip's exit-code convention, not POSIX 125).
            if (string.IsNullOrWhiteSpace(raw))
            {
                return result.WriteError("--output path must not be empty or whitespace", stderr);
            }
            if (raw == "-")
            {
                stdoutMode = true;
            }
            else
            {
                outputFile = raw;
            }
        }

        // Round-1 review TA-C2: --brotli and --zstd together silently picked brotli (first
        // if-branch wins). Reject explicitly so the user knows their flag combination was
        // ambiguous instead of getting silent precedence behaviour.
        bool wantBrotli = result.Has("--brotli");
        bool wantZstd = result.Has("--zstd");
        if (wantBrotli && wantZstd)
        {
            return result.WriteError("--brotli and --zstd are mutually exclusive", stderr);
        }

        CompressionFormat? formatFlag = wantBrotli
            ? CompressionFormat.Brotli
            : wantZstd ? CompressionFormat.Zstd : (CompressionFormat?)null;

        int? levelFlag = result.Has("--level") ? result.GetInt("--level") : (int?)null;

        string[] files = result.Positionals;

        CompressionFormat format = formatFlag ?? CompressionFormat.Gzip;
        int level = levelFlag ?? CompressionFormatInfo.GetDefaultLevel(format);

        if (levelFlag.HasValue && !CompressionFormatInfo.IsLevelValid(format, levelFlag.Value))
        {
            var (_, _, min, max) = CompressionFormatInfo.GetMetadata(format);
            return result.WriteError(
                $"level {levelFlag.Value} out of range for {CompressionFormatInfo.GetShortName(format)} ({min}-{max})",
                stderr);
        }

        if (outputFile is not null && files.Length > 1)
        {
            return result.WriteError("-o cannot be used with multiple input files", stderr);
        }

        bool showStats = !jsonOutput && !quiet && (verbose || stdoutIsTerminal);

        // --- Pipe mode ---
        if (files.Length == 0 && stdinIsRedirected)
        {
            return await RunPipeModeAsync(
                stdin, stdout, stderr,
                decompress, format, level, formatFlag, jsonOutput, version);
        }

        if (files.Length == 0)
        {
            return result.WriteError("no input files. Run 'squeeze --help' for usage.", stderr);
        }

        // --- File mode ---
        int exitCode = 0;
        List<SqueezeResult> results = new();
        List<string> jsonErrors = new();

        foreach (string file in files)
        {
            FileOperationResult opResult;
            string? thisOutput = files.Length == 1 ? outputFile : null;

            if (stdoutMode)
            {
                opResult = await FileOperations.ProcessFileToStreamAsync(
                    file, stdout, decompress, format, level, formatFlag);
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
                    jsonErrors.Add(opResult.ErrorMessage ?? $"squeeze: {file}: {opResult.ExitReason}");
                }
                else
                {
                    stderr.WriteLine(opResult.ErrorMessage);
                }

                exitCode = opResult.ExitCode;
                continue;
            }

            if (opResult.Result is not null)
            {
                results.Add(opResult.Result);

                if (showStats)
                {
                    stderr.WriteLine(Formatting.FormatHuman(opResult.Result, useColor));
                }
            }
        }

        if (jsonOutput)
        {
            string exitReason = exitCode == 0 ? "success" : (results.Count > 0 ? "partial_failure" : "failure");
            stderr.WriteLine(
                Formatting.FormatJson(results, exitCode, exitReason,
                    "squeeze", version, jsonErrors.Count > 0 ? jsonErrors : null));
        }

        return exitCode;
    }

    private static async Task<int> RunPipeModeAsync(
        Stream stdin, Stream stdout, TextWriter stderr,
        bool decompress, CompressionFormat format, int level,
        CompressionFormat? explicitFormat, bool jsonOutput, string version)
    {
        var opResult = await PipeOperations.ProcessAsync(
            stdin, stdout, decompress, format, level, explicitFormat);

        if (opResult.ExitCode != 0)
        {
            if (jsonOutput)
            {
                stderr.WriteLine(
                    Formatting.FormatJsonError(opResult.ExitCode, opResult.ExitReason, "squeeze", version));
            }
            else
            {
                stderr.WriteLine(opResult.ErrorMessage);
            }
            return opResult.ExitCode;
        }

        if (jsonOutput && opResult.Result is not null)
        {
            stderr.WriteLine(
                Formatting.FormatJson(new[] { opResult.Result }, 0, "success", "squeeze", version));
        }

        return 0;
    }

    private static CommandLineParser BuildParser(string version)
    {
        return new CommandLineParser("squeeze", version)
            .Description("Compress and decompress files using gzip, brotli, or zstd.")
            .StandardFlags()
            .ExpandGlobPositionals()
            .Flag("--decompress", "-d", "Decompress (auto-detects format)")
            .Flag("--brotli", "-b", "Use brotli format")
            .Flag("--zstd", "-z", "Use zstd format")
            .Flag("--stdout", "-c", "Write to stdout")
            .Flag("--force", "-f", "Overwrite existing output files")
            .Flag("--remove", "Delete input file after success")
            .Flag("--keep", "-k", "Keep original file (default; takes precedence if --remove also set)")
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
                -k                  Keep input (precedence over --remove if both set)
                -1..-9              Same as --level 1..9
                -v                  Same as --verbose
                -f                  Same as --force
                """)
            .ExitCodes(
                (0, "Success"),
                (1, "Compression/decompression error: corrupt input, write failed, format detection failed"),
                (2, "Usage error: bad flags, missing input, --brotli with --zstd, --output empty/whitespace, --output with multiple inputs, level out of range"))
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
            .JsonField("exit_code", "int", "Tool exit code (0 = success, 1 = compression error, 2 = usage error)")
            .JsonField("exit_reason", "string", "Machine-readable exit reason: success, partial_failure, failure, usage_error, file_not_found, corrupt_input, decompress_failed, compress_failed, io_error, output_exists, unknown_extension")
            .JsonField("files", "array", "Array of per-file result objects")
            .JsonField("files[].input", "string", "Input file path")
            .JsonField("files[].output", "string", "Output file path")
            .JsonField("files[].input_bytes", "int", "Original size in bytes")
            .JsonField("files[].output_bytes", "int", "Compressed/decompressed size in bytes")
            .JsonField("files[].ratio", "float", "Compression ratio (0.0–1.0)")
            .JsonField("files[].format", "string", "Compression format short name (gz, br, zst)")
            .JsonField("files[].seconds", "float", "Processing time in seconds")
            .JsonField("errors", "array|null", "Per-file error messages when exit_reason is partial_failure or failure (file mode only). Null or omitted on success.");
    }

    private static string GetVersion()
    {
        // SDK appends a SourceLink "+gitsha" suffix to AssemblyInformationalVersion
        // by default; strip it so users see plain "X.Y.Z" — matches the convention
        // adopted across clip / digest / ids / schedule / etc.
        string raw = typeof(SqueezeResult).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
