# Program.cs Cleanup — Design Spec

**Date:** 2026-03-30
**Status:** Approved

## Overview

Improve code quality and readability of all three tool entry points (`timeit`, `squeeze`, `peep`). No feature or behaviour changes — purely structural.

**Motivation:** The Program.cs files use top-level statements (minimal API style), and two of the three have logic that belongs in their class libraries rather than the console entry point. Peep's Program.cs is 912 lines with a 540-line event loop method that takes 18 parameters.

**Goals:**
- Proper `namespace`, `class Program`, explicit `Main` in all three tools
- Move stream orchestration logic from squeeze's Program.cs into `Winix.Squeeze`
- Extract peep's interactive event loop into a class in `Winix.Peep`
- Eliminate repeated error-reporting boilerplate across all three tools

**Non-goals:**
- Feature changes or new flags
- Changing any externally-visible behaviour (same args, same output, same exit codes)
- Refactoring the class libraries themselves (only adding to them)

## 1. All tools: proper Main method

Each Program.cs gets a namespace, class, and explicit `Main`. Top-level statements are removed.

```csharp
namespace Peep;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        // arg parsing, validation, dispatch
    }

    private static string GetVersion() { ... }
}
```

- **Namespace:** PascalCase tool name — `TimeIt`, `Squeeze`, `Peep`
- **Class:** `internal sealed class Program` — no reason for external visibility or subclassing
- **Main:** `static int Main` for timeit, `static async Task<int> Main` for squeeze and peep
- **Helpers** like `GetVersion()` become `private static` methods on `Program`

## 2. ParseResult.WriteError

Add a single-message companion to the existing `WriteErrors()` on `ParseResult`:

```csharp
/// <summary>
/// Writes a single error message and returns the usage error exit code.
/// If --json was set, writes a JSON error object instead of plain text.
/// </summary>
public int WriteError(string message, TextWriter writer)
```

Behaviour: identical to `WriteErrors()` but for a single ad-hoc message rather than the collected parse errors list. Uses the same tool name, version, JSON mode, and usage error code that `WriteErrors()` uses.

**Replaces:**
- `WriteUsageError()` in squeeze Program.cs (deleted entirely)
- The "no command specified" if/else blocks in timeit and peep Program.cs
- The regex parse error block in peep Program.cs

**Does not replace:** The `CommandNotFoundException` / `CommandNotExecutableException` handlers in timeit and peep — those use exit codes 126/127, not the usage error code.

**Call site example:**
```csharp
// Before (6 lines):
if (jsonOutput)
{
    Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "peep", version));
}
else
{
    Console.Error.WriteLine("peep: no command specified. Run 'peep --help' for usage.");
}
return ExitCode.UsageError;

// After (1 line):
return result.WriteError("no command specified. Run 'peep --help' for usage.", Console.Error);
```

## 3. Squeeze: move stream operations to library

Two methods move from Program.cs to `Winix.Squeeze`. Library methods take `Stream` parameters (no `Console` references) and return result objects (no error output writing).

### RunStdoutModeAsync → FileOperations.ProcessFileToStreamAsync

Already returns `FileOperationResult`. Sits naturally alongside the existing `CompressFileAsync` / `DecompressFileAsync`. The change: takes a `Stream output` parameter instead of calling `Console.OpenStandardOutput()`.

```csharp
// In Winix.Squeeze/FileOperations.cs
public static async Task<FileOperationResult> ProcessFileToStreamAsync(
    string inputPath, Stream output,
    bool decompress, CompressionFormat format, int level,
    CompressionFormat? explicitFormat)
```

### RunPipeModeAsync → PipeOperations.ProcessAsync

Stream-to-stream with no file paths — different responsibility from `FileOperations`, so it gets its own class. Returns `FileOperationResult` (same shape — exit code, reason, result, error message). Creating a near-duplicate result type would be worse than a slightly misleading name.

```csharp
// In Winix.Squeeze/PipeOperations.cs
public static class PipeOperations
{
    public static async Task<FileOperationResult> ProcessAsync(
        Stream input, Stream output,
        bool decompress, CompressionFormat format, int level,
        CompressionFormat? explicitFormat)
}
```

### What stays in Program.cs

- Opening `Console.OpenStandardInput()` / `Console.OpenStandardOutput()` and passing them to library methods
- Error output after getting the result back (JSON vs plain text formatting to stderr)
- Mode dispatch (pipe mode vs stdout mode vs file mode)
- Arg validation (level range, -o with multiple files)

Program.cs shrinks from ~360 lines to ~190 lines.

## 4. Peep: extract InteractiveSession

The 540-line `RunLoopAsync` and its helpers move into `InteractiveSession` in `Winix.Peep`.

### SessionConfig record

Configuration values passed as a flat record — avoids an 18-parameter constructor.

```csharp
// In Winix.Peep/SessionConfig.cs
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

### InteractiveSession class

```csharp
// In Winix.Peep/InteractiveSession.cs
public sealed class InteractiveSession
{
    private readonly SessionConfig _config;

    // Mutable state (currently local variables in RunLoopAsync)
    private int _runCount;
    private PeepResult? _lastResult;
    private string? _previousOutput;
    private bool _isPaused;
    private bool _showHelp;
    private int _scrollOffset;
    private string _exitReason = "manual";
    private SnapshotHistory _history;
    private bool _isTimeMachine;
    private bool _historyOverlayOpen;
    private int _historyOverlaySelection;

    public InteractiveSession(SessionConfig config) { ... }

    /// <summary>Runs the interactive event loop until exit.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken) { ... }

    // Private methods extracted from the event loop:
    private void HandleKeyPress(ConsoleKeyInfo key) { ... }
    private void HandleHistoryKey(ConsoleKeyInfo key) { ... }
    private bool CheckAutoExit() { ... }
    private void RenderCurrentScreen() { ... }
    private async Task<PeepResult?> TryRunCommandAsync(TriggerSource trigger, CancellationToken ct) { ... }
    private int HandleExit() { ... }
}
```

### What stays in Program.cs

- Arg parsing and validation (~80 lines)
- Constructing `SessionConfig` and `InteractiveSession`
- `RunOnceAsync` — 55 lines of linear code that doesn't share state with the event loop
- Command-not-found / not-executable exception handling for once mode

### GetTerminalHeight / GetTerminalWidth

Move to `ConsoleEnv` in ShellKit — they're terminal capability queries, which is exactly what `ConsoleEnv` is for.

### Program.cs shrinks from ~912 lines to ~120 lines.

## File Map

| File | Action | Change |
|------|--------|--------|
| `src/Yort.ShellKit/ParseResult.cs` | Modify | Add `WriteError(message, writer)` |
| `src/Yort.ShellKit/ConsoleEnv.cs` | Modify | Add `GetTerminalHeight()`, `GetTerminalWidth()` |
| `tests/Yort.ShellKit.Tests/CommandLineParserTests.cs` | Modify | Add `WriteError` tests |
| `tests/Yort.ShellKit.Tests/ConsoleEnvTests.cs` | Modify | Add terminal size tests (if testable) |
| `src/timeit/Program.cs` | Modify | Namespace + Main + use WriteError |
| `src/squeeze/Program.cs` | Modify | Namespace + Main + use WriteError + remove stream methods |
| `src/Winix.Squeeze/PipeOperations.cs` | Create | Stream-to-stream processing |
| `src/Winix.Squeeze/FileOperations.cs` | Modify | Add `ProcessFileToStreamAsync` |
| `tests/Winix.Squeeze.Tests/PipeOperationsTests.cs` | Create | Pipe operation tests |
| `tests/Winix.Squeeze.Tests/FileOperationsTests.cs` | Modify | Add stream output tests |
| `src/peep/Program.cs` | Modify | Namespace + Main + use WriteError + use InteractiveSession |
| `src/Winix.Peep/SessionConfig.cs` | Create | Configuration record |
| `src/Winix.Peep/InteractiveSession.cs` | Create | Event loop class |
| `tests/Winix.Peep.Tests/InteractiveSessionTests.cs` | Create | Session tests (where practical) |

## Testing

- **ParseResult.WriteError:** Unit tests for plain text and JSON mode, custom usage error code
- **PipeOperations:** Unit tests with `MemoryStream` — compress and decompress round-trip, error cases
- **FileOperations.ProcessFileToStreamAsync:** Unit tests with temp files and `MemoryStream` output
- **InteractiveSession:** Integration-style tests where practical. The event loop is inherently hard to unit test (console I/O, alternate screen buffer), but `CheckAutoExit` and similar pure-logic methods can be tested if made internal + `InternalsVisibleTo`
- **All existing tests must continue to pass** — this is a refactor, not a behaviour change
