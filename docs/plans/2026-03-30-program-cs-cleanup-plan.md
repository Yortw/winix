# Program.cs Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve code quality of all three tool entry points by using proper Main methods, moving stream logic to class libraries, and extracting peep's event loop into its own class.

**Architecture:** Console apps become thin entry points with namespace/class/Main. Stream orchestration moves to library classes (`PipeOperations`, `FileOperations`). Peep's interactive loop becomes `InteractiveSession` owning its own state via `SessionConfig`. A new `ParseResult.WriteError` eliminates repeated error-reporting boilerplate.

**Tech Stack:** .NET 10, C#, xUnit, AOT-compatible (no reflection in new code)

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Yort.ShellKit/ParseResult.cs` | Modify | Add `WriteError(message, writer)` method |
| `src/Yort.ShellKit/ConsoleEnv.cs` | Modify | Add `GetTerminalHeight()`, `GetTerminalWidth()` |
| `tests/Yort.ShellKit.Tests/CommandLineParserTests.cs` | Modify | Add `WriteError` tests |
| `src/timeit/Program.cs` | Modify | Namespace + class + Main, use WriteError |
| `src/squeeze/Program.cs` | Modify | Namespace + class + Main, use WriteError, remove stream methods |
| `src/Winix.Squeeze/PipeOperations.cs` | Create | Stream-to-stream compress/decompress |
| `src/Winix.Squeeze/FileOperations.cs` | Modify | Add `ProcessFileToStreamAsync` |
| `tests/Winix.Squeeze.Tests/PipeOperationsTests.cs` | Create | Pipe operation round-trip tests |
| `tests/Winix.Squeeze.Tests/FileOperationTests.cs` | Modify | Add stream output tests |
| `src/peep/Program.cs` | Modify | Namespace + class + Main, use WriteError, use InteractiveSession |
| `src/Winix.Peep/SessionConfig.cs` | Create | Immutable configuration record |
| `src/Winix.Peep/InteractiveSession.cs` | Create | Event loop class with owned state |
| `CLAUDE.md` | Modify | Add entry point conventions |

---

### Task 1: ParseResult.WriteError + tests

**Files:**
- Modify: `src/Yort.ShellKit/ParseResult.cs`
- Modify: `tests/Yort.ShellKit.Tests/CommandLineParserTests.cs`

- [ ] **Step 1: Write failing tests for WriteError**

Append to test file:

```csharp
public class WriteErrorTests
{
    [Fact]
    public void WriteError_PlainText_WritesToolPrefixedMessage()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .Flag("--verbose", null, "Verbose");

        var result = parser.Parse(Array.Empty<string>());

        var writer = new StringWriter();
        int exitCode = result.WriteError("no command specified", writer);

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("mytool: no command specified", writer.ToString());
    }

    [Fact]
    public void WriteError_JsonMode_WritesJsonError()
    {
        var parser = new CommandLineParser("mytool", "2.0.0")
            .StandardFlags();

        var result = parser.Parse(new[] { "--json" });

        var writer = new StringWriter();
        int exitCode = result.WriteError("no command specified", writer);

        Assert.Equal(ExitCode.UsageError, exitCode);
        string output = writer.ToString();
        Assert.Contains("\"tool\":\"mytool\"", output);
        Assert.Contains("\"exit_code\":125", output);
        Assert.Contains("\"exit_reason\":\"usage_error\"", output);
    }

    [Fact]
    public void WriteError_CustomUsageErrorCode_ReturnsCustomCode()
    {
        var parser = new CommandLineParser("squeeze", "1.0.0")
            .UsageErrorCode(2);

        var result = parser.Parse(Array.Empty<string>());

        var writer = new StringWriter();
        int exitCode = result.WriteError("bad args", writer);

        Assert.Equal(2, exitCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Yort.ShellKit.Tests/ --filter "WriteErrorTests"`
Expected: FAIL — `WriteError` method does not exist

- [ ] **Step 3: Implement WriteError on ParseResult**

Add to `src/Yort.ShellKit/ParseResult.cs`, immediately after the `WriteErrors` method:

```csharp
    /// <summary>
    /// Writes a single error message and returns the usage error exit code.
    /// If --json was set, writes a JSON error object instead of plain text.
    /// Use for post-parse validation errors (e.g. "no command specified").
    /// </summary>
    public int WriteError(string message, TextWriter writer)
    {
        if (_hasJson)
        {
            writer.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{{\"tool\":\"{0}\",\"version\":\"{1}\",\"exit_code\":{2},\"exit_reason\":\"usage_error\"}}",
                    EscapeJson(_toolName),
                    EscapeJson(_version),
                    _usageErrorCode));
        }
        else
        {
            writer.WriteLine($"{_toolName}: {message}");
        }

        return _usageErrorCode;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Yort.ShellKit.Tests/ --filter "WriteErrorTests"`
Expected: All 3 tests PASS

- [ ] **Step 5: Commit**

```
git add src/Yort.ShellKit/ParseResult.cs tests/Yort.ShellKit.Tests/CommandLineParserTests.cs
git commit -m "feat: add ParseResult.WriteError for post-parse validation errors"
```

---

### Task 2: ConsoleEnv terminal size helpers

**Files:**
- Modify: `src/Yort.ShellKit/ConsoleEnv.cs`

- [ ] **Step 1: Add GetTerminalHeight and GetTerminalWidth to ConsoleEnv**

Append to the `ConsoleEnv` class in `src/Yort.ShellKit/ConsoleEnv.cs`:

```csharp
    /// <summary>
    /// Returns the terminal height in rows, or 24 if not attached to a terminal.
    /// </summary>
    public static int GetTerminalHeight()
    {
        try
        {
            return Console.WindowHeight;
        }
        catch
        {
            return 24;
        }
    }

    /// <summary>
    /// Returns the terminal width in columns, or 80 if not attached to a terminal.
    /// </summary>
    public static int GetTerminalWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch
        {
            return 80;
        }
    }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/Yort.ShellKit/Yort.ShellKit.csproj`
Expected: Build succeeded, 0 warnings

- [ ] **Step 3: Commit**

```
git add src/Yort.ShellKit/ConsoleEnv.cs
git commit -m "feat: add GetTerminalHeight/GetTerminalWidth to ConsoleEnv"
```

---

### Task 3: Migrate timeit to proper Main + WriteError

**Files:**
- Modify: `src/timeit/Program.cs`

- [ ] **Step 1: Rewrite timeit Program.cs**

Replace the entire file with:

```csharp
using System.Reflection;
using Winix.TimeIt;
using Yort.ShellKit;

namespace TimeIt;

internal sealed class Program
{
    static int Main(string[] args)
    {
        string version = GetVersion();

        var parser = new CommandLineParser("timeit", version)
            .Description("Time a command and show wall clock, CPU time, peak memory, and exit code.")
            .StandardFlags()
            .Flag("--oneline", "-1", "Single-line output format")
            .Flag("--stdout", "Write summary to stdout instead of stderr")
            .CommandMode()
            .ExitCodes(
                (0, "Child process exit code (pass-through)"),
                (ExitCode.UsageError, "No command specified or bad timeit arguments"),
                (ExitCode.NotExecutable, "Command not executable (permission denied)"),
                (ExitCode.NotFound, "Command not found"));

        var result = parser.Parse(args);
        if (result.IsHandled) return result.ExitCode;
        if (result.HasErrors) return result.WriteErrors(Console.Error);

        bool oneLine = result.Has("--oneline");
        bool jsonOutput = result.Has("--json");
        bool useStdout = result.Has("--stdout");
        bool useColor = result.ResolveColor();
        TextWriter writer = useStdout ? Console.Out : Console.Error;

        if (result.Command.Length == 0)
        {
            return result.WriteError("no command specified. Run 'timeit --help' for usage.", writer);
        }

        string command = result.Command[0];
        string[] commandArgs = result.Command.Skip(1).ToArray();

        TimeItResult timeResult;
        try
        {
            timeResult = CommandRunner.Run(command, commandArgs);
        }
        catch (CommandNotExecutableException ex)
        {
            if (jsonOutput)
            {
                writer.WriteLine(Formatting.FormatJsonError(ExitCode.NotExecutable, "command_not_executable", "timeit", version));
            }
            else
            {
                Console.Error.WriteLine($"timeit: {ex.Message}");
            }
            return ExitCode.NotExecutable;
        }
        catch (CommandNotFoundException ex)
        {
            if (jsonOutput)
            {
                writer.WriteLine(Formatting.FormatJsonError(ExitCode.NotFound, "command_not_found", "timeit", version));
            }
            else
            {
                Console.Error.WriteLine($"timeit: {ex.Message}");
            }
            return ExitCode.NotFound;
        }

        string output;
        if (jsonOutput)
        {
            output = Formatting.FormatJson(timeResult, "timeit", version);
        }
        else if (oneLine)
        {
            output = Formatting.FormatOneLine(timeResult, useColor);
        }
        else
        {
            output = Formatting.FormatDefault(timeResult, useColor);
        }

        writer.WriteLine(output);

        return timeResult.ExitCode;
    }

    private static string GetVersion()
    {
        return typeof(TimeItResult).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build src/timeit/timeit.csproj`
Expected: Build succeeded, 0 warnings

Run: `dotnet test tests/Winix.TimeIt.Tests/`
Expected: All 30 tests pass

- [ ] **Step 3: Commit**

```
git add src/timeit/Program.cs
git commit -m "refactor: timeit Program.cs to proper namespace/class/Main, use WriteError"
```

---

### Task 4: Squeeze — move PipeOperations to library + tests

**Files:**
- Create: `src/Winix.Squeeze/PipeOperations.cs`
- Create: `tests/Winix.Squeeze.Tests/PipeOperationsTests.cs`

- [ ] **Step 1: Write failing tests for PipeOperations**

Create `tests/Winix.Squeeze.Tests/PipeOperationsTests.cs`:

```csharp
using Xunit;
using Winix.Squeeze;

namespace Winix.Squeeze.Tests;

public class PipeOperationsTests : IDisposable
{
    private readonly string _tempDir;

    public PipeOperationsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"squeeze-pipe-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task ProcessAsync_CompressAndDecompress_RoundTrips()
    {
        byte[] original = "Hello, pipe operations!"u8.ToArray();

        // Compress
        using var compressInput = new MemoryStream(original);
        using var compressed = new MemoryStream();
        var compressResult = await PipeOperations.ProcessAsync(
            compressInput, compressed,
            decompress: false, CompressionFormat.Gzip, level: 6, explicitFormat: null);

        Assert.Equal(0, compressResult.ExitCode);
        Assert.NotNull(compressResult.Result);
        Assert.True(compressed.Length > 0);

        // Decompress
        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        var decompressResult = await PipeOperations.ProcessAsync(
            compressed, decompressed,
            decompress: true, CompressionFormat.Gzip, level: 6, explicitFormat: null);

        Assert.Equal(0, decompressResult.ExitCode);
        Assert.Equal(original, decompressed.ToArray());
    }

    [Fact]
    public async Task ProcessAsync_DecompressAutoDetect_DetectsFormat()
    {
        byte[] original = "Auto-detect test"u8.ToArray();

        using var compressInput = new MemoryStream(original);
        using var compressed = new MemoryStream();
        await PipeOperations.ProcessAsync(
            compressInput, compressed,
            decompress: false, CompressionFormat.Gzip, level: 6, explicitFormat: null);

        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        var result = await PipeOperations.ProcessAsync(
            compressed, decompressed,
            decompress: true, CompressionFormat.Gzip, level: 6, explicitFormat: null);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(original, decompressed.ToArray());
    }

    [Fact]
    public async Task ProcessAsync_DecompressInvalidData_ReturnsError()
    {
        byte[] garbage = "not compressed data"u8.ToArray();

        using var input = new MemoryStream(garbage);
        using var output = new MemoryStream();
        var result = await PipeOperations.ProcessAsync(
            input, output,
            decompress: true, CompressionFormat.Gzip, level: 6, explicitFormat: CompressionFormat.Gzip);

        Assert.Equal(1, result.ExitCode);
        Assert.NotNull(result.ErrorMessage);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Squeeze.Tests/ --filter "PipeOperationsTests"`
Expected: FAIL — `PipeOperations` does not exist

- [ ] **Step 3: Implement PipeOperations**

Create `src/Winix.Squeeze/PipeOperations.cs`:

```csharp
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
            // Buffer input so we can count bytes
            using var inputBuffer = new MemoryStream();
            await input.CopyToAsync(inputBuffer).ConfigureAwait(false);
            inputBytes = inputBuffer.Length;
            inputBuffer.Position = 0;

            if (decompress)
            {
                if (explicitFormat.HasValue)
                {
                    await Compressor.DecompressAsync(inputBuffer, output, explicitFormat.Value)
                        .ConfigureAwait(false);
                }
                else
                {
                    CompressionFormat? detected = await Compressor.DecompressAutoDetectAsync(
                        inputBuffer, output, filename: null).ConfigureAwait(false);

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
                using var countingOutput = new MemoryStream();
                await Compressor.CompressAsync(inputBuffer, countingOutput, format, level)
                    .ConfigureAwait(false);
                countingOutput.Position = 0;
                outputBytes = countingOutput.Length;
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
            format, stopwatch.Elapsed);
        return new FileOperationResult(0, "success", result, null);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Squeeze.Tests/ --filter "PipeOperationsTests"`
Expected: All 3 tests PASS

- [ ] **Step 5: Commit**

```
git add src/Winix.Squeeze/PipeOperations.cs tests/Winix.Squeeze.Tests/PipeOperationsTests.cs
git commit -m "feat: add PipeOperations for stream-to-stream compress/decompress"
```

---

### Task 5: Squeeze — move ProcessFileToStreamAsync to FileOperations + tests

**Files:**
- Modify: `src/Winix.Squeeze/FileOperations.cs`
- Modify: `tests/Winix.Squeeze.Tests/FileOperationTests.cs`

- [ ] **Step 1: Write failing tests for ProcessFileToStreamAsync**

Append to `tests/Winix.Squeeze.Tests/FileOperationTests.cs`, inside or after the existing `FileOperationIntegrationTests` class:

```csharp
public class FileToStreamTests : IDisposable
{
    private readonly string _tempDir;

    public FileToStreamTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"squeeze-stream-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task ProcessFileToStreamAsync_Compress_WritesToStream()
    {
        string inputPath = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(inputPath, "Hello, stream output!");

        using var output = new MemoryStream();
        var result = await FileOperations.ProcessFileToStreamAsync(
            inputPath, output,
            decompress: false, CompressionFormat.Gzip, level: 6, explicitFormat: null);

        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Result);
        Assert.True(output.Length > 0);
    }

    [Fact]
    public async Task ProcessFileToStreamAsync_CompressDecompress_RoundTrips()
    {
        string inputPath = Path.Combine(_tempDir, "roundtrip.txt");
        string original = "Round-trip content for stream test.";
        await File.WriteAllTextAsync(inputPath, original);

        // Compress to stream
        using var compressed = new MemoryStream();
        await FileOperations.ProcessFileToStreamAsync(
            inputPath, compressed,
            decompress: false, CompressionFormat.Gzip, level: 6, explicitFormat: null);

        // Write compressed bytes to a file, then decompress to stream
        string compressedPath = Path.Combine(_tempDir, "roundtrip.txt.gz");
        await File.WriteAllBytesAsync(compressedPath, compressed.ToArray());

        using var decompressed = new MemoryStream();
        var result = await FileOperations.ProcessFileToStreamAsync(
            compressedPath, decompressed,
            decompress: true, CompressionFormat.Gzip, level: 6, explicitFormat: null);

        Assert.Equal(0, result.ExitCode);
        string text = System.Text.Encoding.UTF8.GetString(decompressed.ToArray());
        Assert.Equal(original, text);
    }

    [Fact]
    public async Task ProcessFileToStreamAsync_FileNotFound_ReturnsError()
    {
        using var output = new MemoryStream();
        var result = await FileOperations.ProcessFileToStreamAsync(
            Path.Combine(_tempDir, "nonexistent.txt"), output,
            decompress: false, CompressionFormat.Gzip, level: 6, explicitFormat: null);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("file_not_found", result.ExitReason);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Squeeze.Tests/ --filter "FileToStreamTests"`
Expected: FAIL — `ProcessFileToStreamAsync` does not exist

- [ ] **Step 3: Implement ProcessFileToStreamAsync**

Add to `src/Winix.Squeeze/FileOperations.cs`, before the `TryDeleteFile` method:

```csharp
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

            using var countingOutput = new MemoryStream();

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

            long outputBytes = countingOutput.Length;
            countingOutput.Position = 0;
            await countingOutput.CopyToAsync(output).ConfigureAwait(false);

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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Squeeze.Tests/ --filter "FileToStreamTests"`
Expected: All 3 tests PASS

- [ ] **Step 5: Commit**

```
git add src/Winix.Squeeze/FileOperations.cs tests/Winix.Squeeze.Tests/FileOperationTests.cs
git commit -m "feat: add FileOperations.ProcessFileToStreamAsync for stdout mode"
```

---

### Task 6: Migrate squeeze to proper Main + use library methods

**Files:**
- Modify: `src/squeeze/Program.cs`

- [ ] **Step 1: Rewrite squeeze Program.cs**

Replace the entire file. The key changes:
- Wrap in `namespace Squeeze` / `class Program` / `async Task<int> Main`
- Replace inline `RunPipeModeAsync` with call to `PipeOperations.ProcessAsync`
- Replace inline `RunStdoutModeAsync` with call to `FileOperations.ProcessFileToStreamAsync`
- Replace `WriteUsageError` calls with `result.WriteError`
- Delete `RunPipeModeAsync`, `RunStdoutModeAsync`, `WriteUsageError`, `PrintHelp` methods

```csharp
using System.Reflection;
using Winix.Squeeze;
using Yort.ShellKit;

namespace Squeeze;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
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
                result, decompress, format, level, formatFlag, jsonOutput, version);
        }

        if (files.Length == 0)
        {
            return result.WriteError("no input files. Run 'squeeze --help' for usage.", Console.Error);
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

    private static async Task<int> RunPipeModeAsync(
        ParseResult parseResult, bool decompress, CompressionFormat format, int level,
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
```

- [ ] **Step 2: Build and test**

Run: `dotnet build src/squeeze/squeeze.csproj`
Expected: Build succeeded, 0 warnings

Run: `dotnet test tests/Winix.Squeeze.Tests/`
Expected: All tests pass (103 existing + 6 new)

- [ ] **Step 3: Commit**

```
git add src/squeeze/Program.cs
git commit -m "refactor: squeeze Program.cs to proper Main, use library PipeOperations/FileOperations"
```

---

### Task 7: Peep — create SessionConfig and InteractiveSession

**Files:**
- Create: `src/Winix.Peep/SessionConfig.cs`
- Create: `src/Winix.Peep/InteractiveSession.cs`

This is the largest task. It moves the event loop and all its helpers from Program.cs into a class. The code is a direct move — no logic changes.

- [ ] **Step 1: Create SessionConfig**

Create `src/Winix.Peep/SessionConfig.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Winix.Peep;

/// <summary>
/// Immutable configuration for an interactive peep session. Constructed from parsed
/// command-line arguments by the entry point, consumed by <see cref="InteractiveSession"/>.
/// </summary>
public sealed record SessionConfig(
    string Command,
    string[] CommandArgs,
    string CommandDisplay,
    double IntervalSeconds,
    bool UseInterval,
    string[] WatchPatterns,
    int DebounceMs,
    int HistoryCapacity,
    bool NoGitIgnore,
    bool ExitOnChange,
    bool ExitOnSuccess,
    bool ExitOnError,
    Regex[] ExitOnMatchRegexes,
    bool DiffEnabled,
    bool NoHeader,
    bool JsonOutput,
    bool JsonOutputIncludeOutput,
    bool UseColor,
    string Version);
```

- [ ] **Step 2: Create InteractiveSession**

Create `src/Winix.Peep/InteractiveSession.cs`. This is a direct move of `RunLoopAsync` and its helpers from Program.cs, with parameters becoming fields. The full implementation follows.

Read the current `src/peep/Program.cs` lines 194-912 for the exact code being moved. The key transformations:

1. All parameters of `RunLoopAsync` → read from `_config` fields
2. All local mutable state (`runCount`, `isPaused`, `scrollOffset`, etc.) → private fields
3. All helper methods (`CheckAutoExit`, `RenderScreen`, `RenderTimeMachineScreen`, `HandleExitFromLoop`, `TryRunCommand`) → private instance methods reading from fields
4. `GetTerminalHeight()` / `GetTerminalWidth()` → `ConsoleEnv.GetTerminalHeight()` / `ConsoleEnv.GetTerminalWidth()`

```csharp
using System.Diagnostics;
using System.Text.RegularExpressions;
using Yort.ShellKit;

namespace Winix.Peep;

/// <summary>
/// Runs the interactive peep event loop — polls/watches for changes, renders to alternate
/// screen buffer, handles keyboard input (scroll, pause, time-machine, diff toggle).
/// </summary>
public sealed class InteractiveSession
{
    private readonly SessionConfig _config;

    // Mutable session state
    private int _runCount;
    private PeepResult? _lastResult;
    private string? _previousOutput;
    private bool _isPaused;
    private bool _showHelp;
    private int _scrollOffset;
    private string _exitReason = "manual";
    private bool _running;
    private SnapshotHistory _history;
    private bool _isTimeMachine;
    private bool _historyOverlayOpen;
    private int _historyOverlaySelection;
    private bool _diffEnabled;

    public InteractiveSession(SessionConfig config)
    {
        _config = config;
        _history = new SnapshotHistory(config.HistoryCapacity);
        _diffEnabled = config.DiffEnabled;
    }

    /// <summary>Runs the interactive event loop until an exit condition is met.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var sessionStopwatch = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken ct = cts.Token;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _exitReason = "interrupted";
            cts.Cancel();
        };

        // Set up file watcher
        FileWatcher? fileWatcher = null;
        SemaphoreSlim? fileChangeSemaphore = null;
        if (_config.WatchPatterns.Length > 0)
        {
            Func<string, bool>? excludeFilter = null;
            if (!_config.NoGitIgnore && GitIgnoreChecker.IsGitRepo())
            {
                excludeFilter = GitIgnoreChecker.IsIgnored;
            }

            fileChangeSemaphore = new SemaphoreSlim(0);
            fileWatcher = new FileWatcher(_config.WatchPatterns, _config.DebounceMs, excludeFilter);
            fileWatcher.FileChanged += () =>
            {
                fileChangeSemaphore.Release();
            };
            fileWatcher.Start();
        }

        ScreenRenderer.EnterAlternateBuffer(Console.Out);

        try
        {
            // Initial run
            PeepResult? initialResult = await TryRunCommandAsync(TriggerSource.Initial, ct);

            if (initialResult is not null)
            {
                _runCount++;
                _lastResult = initialResult;
                _previousOutput = initialResult.Output;
                _history.Add(initialResult, DateTime.Now, _runCount);
                RenderCurrentScreen();
            }
            else
            {
                return HandleExit(sessionStopwatch);
            }

            if (CheckAutoExit(null))
            {
                return HandleExit(sessionStopwatch);
            }

            // Main event loop
            var nextRunTime = DateTime.UtcNow.AddSeconds(_config.IntervalSeconds);
            bool fileChangeSignalled = false;

            if (fileChangeSemaphore is not null)
            {
                _ = Task.Run(async () =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        await fileChangeSemaphore.WaitAsync(ct);
                        fileChangeSignalled = true;
                    }
                }, ct);
            }

            while (!ct.IsCancellationRequested)
            {
                // Check for key presses (non-blocking)
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);

                    if (_historyOverlayOpen)
                    {
                        HandleHistoryKey(key);
                        continue;
                    }

                    await HandleKeyPressAsync(key, cts, ct);
                }

                if (ct.IsCancellationRequested)
                {
                    break;
                }

                // Check if we should trigger a run
                bool shouldRun = false;
                TriggerSource trigger = TriggerSource.Interval;

                if (fileChangeSignalled && !_running)
                {
                    fileChangeSignalled = false;
                    shouldRun = true;
                    trigger = TriggerSource.FileChange;
                }
                else if (_config.UseInterval && DateTime.UtcNow >= nextRunTime && !_running)
                {
                    shouldRun = true;
                    trigger = TriggerSource.Interval;
                }

                if (shouldRun)
                {
                    await RunAndProcessResultAsync(trigger, cts, ct);
                    nextRunTime = DateTime.UtcNow.AddSeconds(_config.IntervalSeconds);

                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }
                }

                try
                {
                    await Task.Delay(50, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        finally
        {
            fileWatcher?.Dispose();
            fileChangeSemaphore?.Dispose();
            ScreenRenderer.ExitAlternateBuffer(Console.Out);
        }

        return HandleExit(sessionStopwatch);
    }

    private async Task HandleKeyPressAsync(ConsoleKeyInfo key, CancellationTokenSource cts, CancellationToken ct)
    {
        switch (key.Key)
        {
            case ConsoleKey.Q:
                _exitReason = "manual";
                cts.Cancel();
                break;

            case ConsoleKey.Spacebar:
                if (_isTimeMachine)
                {
                    _isTimeMachine = false;
                    _isPaused = false;
                    _scrollOffset = 0;
                    _history.MoveToNewest();
                    RenderCurrentScreen();
                    break;
                }
                _isPaused = !_isPaused;
                if (!_isPaused)
                {
                    _scrollOffset = 0;
                    _showHelp = false;
                }
                RenderCurrentScreen();
                break;

            case ConsoleKey.R:
            case ConsoleKey.Enter:
                if (!_running)
                {
                    await RunAndProcessResultAsync(TriggerSource.Manual, cts, ct);
                }
                break;

            case ConsoleKey.UpArrow:
                if (_isPaused || _isTimeMachine)
                {
                    _scrollOffset = Math.Max(0, _scrollOffset - 1);
                    RenderCurrentScreen();
                }
                break;

            case ConsoleKey.DownArrow:
                if (_isPaused || _isTimeMachine)
                {
                    _scrollOffset++;
                    RenderCurrentScreen();
                }
                break;

            case ConsoleKey.PageUp:
                if (_isPaused || _isTimeMachine)
                {
                    int pageSize = ConsoleEnv.GetTerminalHeight() - 2;
                    _scrollOffset = Math.Max(0, _scrollOffset - Math.Max(1, pageSize));
                    RenderCurrentScreen();
                }
                break;

            case ConsoleKey.PageDown:
                if (_isPaused || _isTimeMachine)
                {
                    int pageSz = ConsoleEnv.GetTerminalHeight() - 2;
                    _scrollOffset += Math.Max(1, pageSz);
                    RenderCurrentScreen();
                }
                break;

            case ConsoleKey.LeftArrow:
                if (_history.Count > 1)
                {
                    if (!_isTimeMachine)
                    {
                        _isTimeMachine = true;
                        _isPaused = true;
                        _scrollOffset = 0;
                        _showHelp = false;
                        _history.MoveOlder();
                    }
                    else
                    {
                        _history.MoveOlder();
                    }
                    RenderTimeMachineScreen();
                }
                break;

            case ConsoleKey.RightArrow:
                if (_isTimeMachine)
                {
                    _history.MoveNewer();
                    if (_history.IsAtNewest)
                    {
                        _isTimeMachine = false;
                        _isPaused = false;
                        _scrollOffset = 0;
                        RenderCurrentScreen();
                    }
                    else
                    {
                        RenderTimeMachineScreen();
                    }
                }
                break;

            case ConsoleKey.D:
                _diffEnabled = !_diffEnabled;
                if (!_showHelp)
                {
                    RenderCurrentScreen();
                }
                break;

            case ConsoleKey.Escape:
                if (_isTimeMachine)
                {
                    _isTimeMachine = false;
                    _isPaused = false;
                    _scrollOffset = 0;
                    _history.MoveToNewest();
                    RenderCurrentScreen();
                }
                else if (_showHelp)
                {
                    _showHelp = false;
                    RenderCurrentScreen();
                }
                break;

            default:
                if (key.KeyChar == 't')
                {
                    if (_history.Count > 0)
                    {
                        if (!_isTimeMachine)
                        {
                            _isTimeMachine = true;
                            _isPaused = true;
                            _scrollOffset = 0;
                            _showHelp = false;
                        }
                        _historyOverlayOpen = true;
                        _historyOverlaySelection = _history.CursorIndex;
                        ScreenRenderer.RenderHistoryOverlay(Console.Out, _history,
                            _historyOverlaySelection, ConsoleEnv.GetTerminalWidth(), ConsoleEnv.GetTerminalHeight());
                    }
                }
                else if (key.KeyChar == '?')
                {
                    _showHelp = !_showHelp;
                    if (_showHelp)
                    {
                        ScreenRenderer.RenderHelpOverlay(Console.Out,
                            ConsoleEnv.GetTerminalWidth(), ConsoleEnv.GetTerminalHeight());
                    }
                    else
                    {
                        RenderCurrentScreen();
                    }
                }
                break;
        }
    }

    private void HandleHistoryKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                if (_historyOverlaySelection < _history.Count - 1)
                {
                    _historyOverlaySelection++;
                }
                ScreenRenderer.RenderHistoryOverlay(Console.Out, _history,
                    _historyOverlaySelection, ConsoleEnv.GetTerminalWidth(), ConsoleEnv.GetTerminalHeight());
                break;

            case ConsoleKey.DownArrow:
                if (_historyOverlaySelection > 0)
                {
                    _historyOverlaySelection--;
                }
                ScreenRenderer.RenderHistoryOverlay(Console.Out, _history,
                    _historyOverlaySelection, ConsoleEnv.GetTerminalWidth(), ConsoleEnv.GetTerminalHeight());
                break;

            case ConsoleKey.Enter:
                _history.MoveToNewest();
                while (_history.CursorIndex > _historyOverlaySelection)
                {
                    _history.MoveOlder();
                }
                _historyOverlayOpen = false;
                _scrollOffset = 0;
                RenderTimeMachineScreen();
                break;

            case ConsoleKey.Escape:
                _historyOverlayOpen = false;
                RenderTimeMachineScreen();
                break;

            default:
                if (key.KeyChar == 't')
                {
                    _historyOverlayOpen = false;
                    RenderTimeMachineScreen();
                }
                break;
        }
    }

    private async Task RunAndProcessResultAsync(
        TriggerSource trigger, CancellationTokenSource cts, CancellationToken ct)
    {
        _running = true;
        string? prevOutput = _lastResult?.Output;
        PeepResult? newResult = await TryRunCommandAsync(trigger, ct);

        if (newResult is not null)
        {
            _runCount++;
            _lastResult = newResult;

            if (_isTimeMachine)
            {
                int savedCursor = _history.CursorIndex;
                int countBefore = _history.Count;
                _history.Add(newResult, DateTime.Now, _runCount);
                if (_history.Count == countBefore)
                {
                    savedCursor = Math.Max(0, savedCursor - 1);
                }
                _history.MoveToNewest();
                while (_history.CursorIndex > savedCursor && _history.MoveOlder()) { }
            }
            else
            {
                _history.Add(newResult, DateTime.Now, _runCount);
            }

            _previousOutput = prevOutput;
        }

        _running = false;

        if (!_isPaused && !_showHelp)
        {
            RenderCurrentScreen();
        }

        if (_lastResult is not null && CheckAutoExit(prevOutput))
        {
            cts.Cancel();
        }
    }

    private async Task<PeepResult?> TryRunCommandAsync(TriggerSource trigger, CancellationToken ct)
    {
        try
        {
            return await CommandExecutor.RunAsync(_config.Command, _config.CommandArgs, trigger, ct);
        }
        catch (CommandNotFoundException ex)
        {
            Console.Error.WriteLine($"peep: {ex.Message}");
            return null;
        }
        catch (CommandNotExecutableException ex)
        {
            Console.Error.WriteLine($"peep: {ex.Message}");
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private bool CheckAutoExit(string? prevOutput)
    {
        if (_lastResult is null)
        {
            return false;
        }

        if (_config.ExitOnSuccess && _lastResult.ExitCode == 0)
        {
            _exitReason = "exit_on_success";
            return true;
        }

        if (_config.ExitOnError && _lastResult.ExitCode != 0)
        {
            _exitReason = "exit_on_error";
            return true;
        }

        if (_config.ExitOnChange && prevOutput is not null
            && !string.Equals(_lastResult.Output, prevOutput, StringComparison.Ordinal))
        {
            _exitReason = "exit_on_change";
            return true;
        }

        if (_config.ExitOnMatchRegexes.Length > 0 && _lastResult.Output is not null)
        {
            string stripped = Formatting.StripAnsi(_lastResult.Output);
            foreach (Regex regex in _config.ExitOnMatchRegexes)
            {
                if (regex.IsMatch(stripped))
                {
                    _exitReason = "exit_on_match";
                    return true;
                }
            }
        }

        return false;
    }

    private void RenderCurrentScreen()
    {
        if (_isTimeMachine)
        {
            RenderTimeMachineScreen();
            return;
        }

        string? header = _config.NoHeader ? null : ScreenRenderer.FormatHeader(
            _config.IntervalSeconds, _config.CommandDisplay, DateTime.Now,
            _lastResult?.ExitCode, _runCount, _isPaused, _config.UseColor,
            isDiffEnabled: _diffEnabled);

        string? watchLine = _config.NoHeader ? null : ScreenRenderer.FormatWatchLine(
            _config.WatchPatterns, _config.UseColor);

        ScreenRenderer.Render(
            Console.Out,
            header,
            watchLine,
            _lastResult?.Output ?? "",
            ConsoleEnv.GetTerminalHeight(),
            _scrollOffset,
            showHeader: !_config.NoHeader,
            previousOutput: _diffEnabled ? _previousOutput : null,
            diffEnabled: _diffEnabled);
    }

    private void RenderTimeMachineScreen()
    {
        Snapshot current = _history.Current;
        Snapshot? previous = _history.GetPreviousOf(_history.CursorIndex);

        string? header = _config.NoHeader ? null : ScreenRenderer.FormatHeader(
            _config.IntervalSeconds, _config.CommandDisplay, current.Timestamp,
            current.Result.ExitCode, current.RunNumber, isPaused: true, _config.UseColor,
            isDiffEnabled: _diffEnabled,
            isTimeMachine: true,
            timeMachinePosition: _history.CursorIndex + 1,
            timeMachineTotal: _history.Count);

        string? watchLine = _config.NoHeader ? null : ScreenRenderer.FormatWatchLine(
            _config.WatchPatterns, _config.UseColor);

        ScreenRenderer.Render(
            Console.Out,
            header,
            watchLine,
            current.Result.Output,
            ConsoleEnv.GetTerminalHeight(),
            _scrollOffset,
            showHeader: !_config.NoHeader,
            previousOutput: _diffEnabled ? previous?.Result.Output : null,
            diffEnabled: _diffEnabled);
    }

    private int HandleExit(Stopwatch sessionStopwatch)
    {
        sessionStopwatch.Stop();

        int exitCode = _lastResult?.ExitCode ?? 0;

        if (_exitReason == "exit_on_change" || _exitReason == "exit_on_success")
        {
            exitCode = 0;
        }

        if (_config.JsonOutput)
        {
            Console.Error.WriteLine(Formatting.FormatJson(
                exitCode: exitCode,
                exitReason: _exitReason,
                runs: _runCount,
                lastChildExitCode: _lastResult?.ExitCode,
                durationSeconds: sessionStopwatch.Elapsed.TotalSeconds,
                command: _config.CommandDisplay,
                lastOutput: _config.JsonOutputIncludeOutput ? _lastResult?.Output : null,
                toolName: "peep",
                version: _config.Version,
                historyRetained: _history.Count));
        }

        return exitCode;
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/Winix.Peep/Winix.Peep.csproj`
Expected: Build succeeded, 0 warnings

- [ ] **Step 4: Commit**

```
git add src/Winix.Peep/SessionConfig.cs src/Winix.Peep/InteractiveSession.cs
git commit -m "feat: add SessionConfig and InteractiveSession to Winix.Peep"
```

---

### Task 8: Migrate peep to proper Main + use InteractiveSession

**Files:**
- Modify: `src/peep/Program.cs`

- [ ] **Step 1: Rewrite peep Program.cs**

Replace the entire file. The key changes:
- Wrap in `namespace Peep` / `class Program` / `async Task<int> Main`
- Replace `RunLoopAsync` call with `InteractiveSession` construction + `RunAsync`
- Keep `RunOnceAsync` as a private method (linear, doesn't share state)
- Delete all the moved methods (`RunLoopAsync`, `CheckAutoExit`, `RenderScreen`, `RenderTimeMachineScreen`, `HandleExitFromLoop`, `TryRunCommand`, `GetTerminalHeight`, `GetTerminalWidth`)
- Use `result.WriteError` for "no command" and regex parse errors

```csharp
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Winix.Peep;
using Yort.ShellKit;

namespace Peep;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        string version = GetVersion();

        var parser = new CommandLineParser("peep", version)
            .Description("Run a command repeatedly and display output on a refreshing screen.")
            .StandardFlags()
            .DoubleOption("--interval", "-n", "N", "Seconds between runs (default: 2)",
                validate: v => v > 0 ? null : "must be positive")
            .ListOption("--watch", "-w", "GLOB", "Re-run on file changes matching glob")
            .IntOption("--debounce", null, "N", "Milliseconds to debounce file changes (default: 300)",
                validate: v => v >= 0 ? null : "must be non-negative")
            .IntOption("--history", null, "N", "Max history snapshots to retain (default: 1000, 0=unlimited)",
                validate: v => v >= 0 ? null : "must be non-negative")
            .Flag("--exit-on-change", "-g", "Exit when output changes")
            .Flag("--exit-on-success", "Exit when command returns exit code 0")
            .Flag("--exit-on-error", "-e", "Exit when command returns non-zero")
            .ListOption("--exit-on-match", null, "PAT", "Exit when output matches regex")
            .Flag("--differences", "-d", "Highlight changed lines between runs")
            .Flag("--no-gitignore", "Disable automatic .gitignore filtering")
            .Flag("--once", "Run once, display, and exit")
            .Flag("--no-header", "-t", "Hide the header lines")
            .Flag("--json-output", "Include last captured output in JSON (implies --json)")
            .Section("Compatibility",
                """
                These flags match watch for muscle memory:
                -n N                   Same as --interval
                -g                     Same as --exit-on-change
                -e                     Same as --exit-on-error
                -d                     Same as --differences
                -t                     Same as --no-header
                """)
            .Section("Interactive",
                """
                q / Ctrl+C             Quit
                Space                  Pause/unpause display
                r / Enter              Force immediate re-run
                d                      Toggle diff highlighting
                Up/Down / PgUp/Dn     Scroll while paused
                Left/Right             Time travel (older/newer)
                t                      History overlay
                ?                      Show/hide help overlay
                """)
            .CommandMode()
            .ExitCodes(
                (0, "Auto-exit condition met, or manual quit with last child exit 0"),
                (ExitCode.UsageError, "Usage error"),
                (ExitCode.NotExecutable, "Command not executable"),
                (ExitCode.NotFound, "Command not found"));

        var result = parser.Parse(args);
        if (result.IsHandled) return result.ExitCode;
        if (result.HasErrors) return result.WriteErrors(Console.Error);

        double intervalSeconds = result.GetDouble("--interval", defaultValue: 2.0);
        bool intervalExplicit = result.Has("--interval");
        string[] watchPatterns = result.GetList("--watch");
        bool once = result.Has("--once");
        bool jsonOutput = result.Has("--json") || result.Has("--json-output");

        if (result.Command.Length == 0)
        {
            return result.WriteError("no command specified. Run 'peep --help' for usage.", Console.Error);
        }

        string command = result.Command[0];
        string[] commandArgs = result.Command.Skip(1).ToArray();
        string commandDisplay = string.Join(" ", result.Command);

        // Compile exit-on-match patterns
        Regex[] exitOnMatchRegexes;
        try
        {
            exitOnMatchRegexes = result.GetList("--exit-on-match")
                .Select(p => new Regex(p, RegexOptions.Compiled))
                .ToArray();
        }
        catch (RegexParseException ex)
        {
            return result.WriteError($"invalid regex pattern: {ex.Message}", Console.Error);
        }

        if (once)
        {
            return await RunOnceAsync(command, commandArgs, commandDisplay,
                jsonOutput, result.Has("--json-output"), version);
        }

        var config = new SessionConfig(
            Command: command,
            CommandArgs: commandArgs,
            CommandDisplay: commandDisplay,
            IntervalSeconds: intervalSeconds,
            UseInterval: watchPatterns.Length == 0 || intervalExplicit,
            WatchPatterns: watchPatterns,
            DebounceMs: result.GetInt("--debounce", defaultValue: 300),
            HistoryCapacity: result.GetInt("--history", defaultValue: 1000),
            NoGitIgnore: result.Has("--no-gitignore"),
            ExitOnChange: result.Has("--exit-on-change"),
            ExitOnSuccess: result.Has("--exit-on-success"),
            ExitOnError: result.Has("--exit-on-error"),
            ExitOnMatchRegexes: exitOnMatchRegexes,
            DiffEnabled: result.Has("--differences"),
            NoHeader: result.Has("--no-header"),
            JsonOutput: jsonOutput,
            JsonOutputIncludeOutput: result.Has("--json-output"),
            UseColor: result.ResolveColor(),
            Version: version);

        var session = new InteractiveSession(config);
        return await session.RunAsync(CancellationToken.None);
    }

    private static async Task<int> RunOnceAsync(
        string command, string[] commandArgs, string commandDisplay,
        bool jsonOutput, bool jsonOutputIncludeOutput, string version)
    {
        var sessionStopwatch = Stopwatch.StartNew();

        try
        {
            PeepResult peepResult = await CommandExecutor.RunAsync(command, commandArgs, TriggerSource.Initial);
            sessionStopwatch.Stop();

            Console.Write(peepResult.Output);

            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJson(
                    exitCode: peepResult.ExitCode == 0 ? 0 : peepResult.ExitCode,
                    exitReason: "once",
                    runs: 1,
                    lastChildExitCode: peepResult.ExitCode,
                    durationSeconds: sessionStopwatch.Elapsed.TotalSeconds,
                    command: commandDisplay,
                    lastOutput: jsonOutputIncludeOutput ? peepResult.Output : null,
                    toolName: "peep",
                    version: version));
            }

            return peepResult.ExitCode;
        }
        catch (CommandNotFoundException ex)
        {
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(127, "command_not_found", "peep", version));
            }
            else
            {
                Console.Error.WriteLine($"peep: {ex.Message}");
            }
            return 127;
        }
        catch (CommandNotExecutableException ex)
        {
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(126, "command_not_executable", "peep", version));
            }
            else
            {
                Console.Error.WriteLine($"peep: {ex.Message}");
            }
            return 126;
        }
    }

    private static string GetVersion()
    {
        return typeof(PeepResult).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build src/peep/peep.csproj`
Expected: Build succeeded, 0 warnings

Run: `dotnet test tests/Winix.Peep.Tests/`
Expected: All 140 tests pass

- [ ] **Step 3: Commit**

```
git add src/peep/Program.cs
git commit -m "refactor: peep Program.cs to proper Main, use InteractiveSession"
```

---

### Task 9: Update CLAUDE.md with entry point conventions

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add entry point convention to CLAUDE.md**

In the `## Conventions` section, after the existing bullet points, add:

```markdown
- Console apps use proper `namespace`/`class Program`/`static Main` — no top-level statements
- Console apps are thin: arg parsing, validation, constructing library objects, error output. Stream orchestration, event loops, and domain logic belong in the class library.
```

- [ ] **Step 2: Commit**

```
git add CLAUDE.md
git commit -m "docs: add entry point conventions to CLAUDE.md"
```

---

### Task 10: Full build, test, and AOT verification

**Files:** None (verification only)

- [ ] **Step 1: Full build**

Run: `dotnet build Winix.sln`
Expected: Build succeeded, 0 errors, 0 warnings

- [ ] **Step 2: Full test suite**

Run: `dotnet test Winix.sln`
Expected: All tests pass (~365 tests).

- [ ] **Step 3: AOT publish all three tools**

Run: `dotnet publish src/timeit/timeit.csproj -c Release -r win-x64`
Run: `dotnet publish src/squeeze/squeeze.csproj -c Release -r win-x64`
Run: `dotnet publish src/peep/peep.csproj -c Release -r win-x64`
Expected: All three succeed with no trim warnings.

- [ ] **Step 4: Smoke test help output**

Run each tool with `--help` and `--version` from the publish output to verify correct output.

- [ ] **Step 5: Commit any fixups**

If any fixes were needed during verification:
```
git add -A
git commit -m "fix: address build/test issues from Program.cs cleanup"
```
