# ShellKit Argument Parser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a declarative argument parser in Yort.ShellKit and migrate all three Winix tools (timeit, squeeze, peep) to use it, eliminating manual arg-parsing boilerplate.

**Architecture:** Fluent `CommandLineParser` builder registers flags/options, `Parse(args)` returns an immutable `ParseResult` with typed access. Help text, error reporting, and standard flags (help, version, color, json) are handled automatically. Each tool's Program.cs shrinks from 100-200 lines of manual parsing to ~30 lines of declaration + ~10 lines of extraction.

**Tech Stack:** .NET 10, C#, xUnit, AOT-compatible (no reflection in parser)

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Yort.ShellKit/ExitCode.cs` | Create | Standard exit code constants |
| `src/Yort.ShellKit/CommandLineParser.cs` | Create | Fluent builder: flag/option registration, Parse() method, help generation |
| `src/Yort.ShellKit/ParseResult.cs` | Create | Immutable parse result with typed access, error reporting |
| `tests/Yort.ShellKit.Tests/CommandLineParserTests.cs` | Create | Comprehensive parser tests |
| `src/timeit/Program.cs` | Modify | Replace manual parsing with CommandLineParser |
| `src/squeeze/Program.cs` | Modify | Replace manual parsing with CommandLineParser |
| `src/peep/Program.cs` | Modify | Replace manual parsing with CommandLineParser |

---

### Task 1: ExitCode constants

**Files:**
- Create: `src/Yort.ShellKit/ExitCode.cs`

- [ ] **Step 1: Create ExitCode static class**

```csharp
namespace Yort.ShellKit;

/// <summary>
/// Standard POSIX-convention exit codes for Winix CLI tools.
/// Tools with domain-specific codes (e.g. squeeze uses 1 for compression error)
/// can define their own constants alongside these.
/// </summary>
public static class ExitCode
{
    /// <summary>Successful execution.</summary>
    public const int Success = 0;

    /// <summary>Usage error: bad arguments, missing required input.</summary>
    public const int UsageError = 125;

    /// <summary>Command not executable (permission denied).</summary>
    public const int NotExecutable = 126;

    /// <summary>Command not found on PATH.</summary>
    public const int NotFound = 127;
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Yort.ShellKit/Yort.ShellKit.csproj`
Expected: Build succeeded, 0 warnings

- [ ] **Step 3: Commit**

```
git add src/Yort.ShellKit/ExitCode.cs
git commit -m "feat: add ExitCode constants to ShellKit"
```

---

### Task 2: Core parsing — Flag + Parse() + ParseResult + tests

Build the minimal end-to-end pipeline: register a flag, parse args, check the result.

**Files:**
- Create: `src/Yort.ShellKit/CommandLineParser.cs`
- Create: `src/Yort.ShellKit/ParseResult.cs`
- Create: `tests/Yort.ShellKit.Tests/CommandLineParserTests.cs`

- [ ] **Step 1: Write failing tests for flag parsing**

```csharp
using Xunit;
using Yort.ShellKit;

namespace Yort.ShellKit.Tests;

public class FlagParsingTests
{
    [Fact]
    public void Flag_Present_HasReturnsTrue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose output");

        var result = parser.Parse(new[] { "--verbose" });

        Assert.True(result.Has("--verbose"));
    }

    [Fact]
    public void Flag_Absent_HasReturnsFalse()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose output");

        var result = parser.Parse(Array.Empty<string>());

        Assert.False(result.Has("--verbose"));
    }

    [Fact]
    public void Flag_ShortForm_HasReturnsTrue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", "-v", "Verbose output");

        var result = parser.Parse(new[] { "-v" });

        Assert.True(result.Has("--verbose"));
    }

    [Fact]
    public void Flag_MultipleFlags_AllRecognised()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", "-v", "Verbose output")
            .Flag("--force", "-f", "Force");

        var result = parser.Parse(new[] { "-v", "-f" });

        Assert.True(result.Has("--verbose"));
        Assert.True(result.Has("--force"));
    }

    [Fact]
    public void Flag_UnknownFlag_HasErrors()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose output");

        var result = parser.Parse(new[] { "--unknown" });

        Assert.True(result.HasErrors);
        Assert.Contains("unknown option: --unknown", result.Errors[0]);
    }

    [Fact]
    public void Parse_NoArgs_NoErrors()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose output");

        var result = parser.Parse(Array.Empty<string>());

        Assert.False(result.HasErrors);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Yort.ShellKit.Tests/ --filter "FlagParsingTests"`
Expected: FAIL — `CommandLineParser` does not exist

- [ ] **Step 3: Implement CommandLineParser with Flag() and Parse()**

In `src/Yort.ShellKit/CommandLineParser.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace Yort.ShellKit;

// Why a custom parser instead of System.CommandLine, Spectre.Console.Cli, or McMaster?
//
// - System.CommandLine: abandoned by Microsoft (stalled at preview for years). Unstable API,
//   heavy middleware pipeline designed for large CLI frameworks.
// - Spectre.Console.Cli: pulls in the full Spectre rendering library (~500KB+ AOT).
//   Overkill when you only need arg parsing.
// - McMaster.Extensions.CommandLineUtils: reflection-based model binding, hostile to AOT trimming.
// - All three target large apps with subcommand trees. Winix tools are single-purpose with flat
//   flag sets. This parser is smaller, fully AOT-safe, and enforces Winix CLI conventions
//   (exit codes, color precedence, JSON error format) by default.

/// <summary>
/// Declarative argument parser for Winix CLI tools. Register flags and options via the fluent
/// builder, then call <see cref="Parse"/> to get an immutable <see cref="ParseResult"/>.
/// </summary>
public sealed class CommandLineParser
{
    private readonly string _toolName;
    private readonly string _version;
    private string? _description;
    private bool _commandMode;
    private string? _positionalLabel;
    private int _usageErrorCode = ExitCode.UsageError;
    private readonly List<FlagDef> _flags = new();
    private readonly List<OptionDef> _options = new();
    private readonly List<ListOptionDef> _listOptions = new();
    private readonly List<AliasDef> _aliases = new();
    private readonly List<(string Title, string Body)> _sections = new();
    private readonly List<(int Code, string Description)> _exitCodes = new();

    // Lookup tables built lazily on first Parse()
    private Dictionary<string, FlagDef>? _flagLookup;
    private Dictionary<string, OptionDef>? _optionLookup;
    private Dictionary<string, ListOptionDef>? _listOptionLookup;
    private Dictionary<string, AliasDef>? _aliasLookup;
    private bool _standardFlagsRegistered;

    /// <summary>
    /// Creates a new parser for the specified tool.
    /// </summary>
    /// <param name="toolName">Tool executable name (e.g. "peep"). Used in error messages and help.</param>
    /// <param name="version">Tool version string. Used by --version.</param>
    public CommandLineParser(string toolName, string version)
    {
        _toolName = toolName;
        _version = version;
    }

    /// <summary>Sets the tool description shown in help output.</summary>
    public CommandLineParser Description(string text)
    {
        _description = text;
        return this;
    }

    /// <summary>
    /// Registers a boolean flag (no value). Access via <see cref="ParseResult.Has"/>.
    /// </summary>
    /// <param name="longName">Long flag name (e.g. "--verbose").</param>
    /// <param name="shortName">Optional short flag name (e.g. "-v").</param>
    /// <param name="description">Description shown in help output.</param>
    public CommandLineParser Flag(string longName, string? shortName, string description)
    {
        _flags.Add(new FlagDef(longName, shortName, description));
        return this;
    }

    /// <summary>
    /// Convenience overload for flags with no short name.
    /// </summary>
    public CommandLineParser Flag(string longName, string description)
    {
        return Flag(longName, null, description);
    }

    /// <summary>
    /// Parses command-line arguments against registered flags and options.
    /// </summary>
    public ParseResult Parse(string[] args)
    {
        BuildLookups();

        var flagsSet = new HashSet<string>(StringComparer.Ordinal);
        var optionValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var listValues = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var errors = new List<string>();
        var positionals = new List<string>();
        var command = new List<string>();
        bool isHandled = false;
        int handledExitCode = 0;
        bool commandBoundaryReached = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            // -- stops flag parsing
            if (arg == "--")
            {
                for (int j = i + 1; j < args.Length; j++)
                {
                    if (_commandMode)
                    {
                        command.Add(args[j]);
                    }
                    else
                    {
                        positionals.Add(args[j]);
                    }
                }
                break;
            }

            // Non-flag arg
            if (!arg.StartsWith('-'))
            {
                if (_commandMode)
                {
                    // First non-flag stops parsing in command mode
                    for (int j = i; j < args.Length; j++)
                    {
                        command.Add(args[j]);
                    }
                    break;
                }
                else
                {
                    positionals.Add(arg);
                    continue;
                }
            }

            // Check aliases first
            if (_aliasLookup!.TryGetValue(arg, out AliasDef? alias))
            {
                optionValues[alias.TargetOption] = alias.Value;
                continue;
            }

            // Check flags
            if (_flagLookup!.TryGetValue(arg, out FlagDef? flag))
            {
                flagsSet.Add(flag.LongName);
                continue;
            }

            // Check options (string, int, double)
            if (_optionLookup!.TryGetValue(arg, out OptionDef? option))
            {
                if (i + 1 >= args.Length)
                {
                    errors.Add($"{option.LongName} requires a value");
                    continue;
                }

                i++;
                string rawValue = args[i];

                // Type validation
                if (option.Type == OptionType.Int)
                {
                    if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
                    {
                        errors.Add($"{option.LongName}: '{rawValue}' is not a valid integer");
                        continue;
                    }
                    if (option.IntValidate is not null)
                    {
                        string? validationError = option.IntValidate(intVal);
                        if (validationError is not null)
                        {
                            errors.Add($"{option.LongName}: {validationError}");
                            continue;
                        }
                    }
                }
                else if (option.Type == OptionType.Double)
                {
                    if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double dblVal))
                    {
                        errors.Add($"{option.LongName}: '{rawValue}' is not a valid number");
                        continue;
                    }
                    if (option.DoubleValidate is not null)
                    {
                        string? validationError = option.DoubleValidate(dblVal);
                        if (validationError is not null)
                        {
                            errors.Add($"{option.LongName}: {validationError}");
                            continue;
                        }
                    }
                }

                optionValues[option.LongName] = rawValue;
                continue;
            }

            // Check list options
            if (_listOptionLookup!.TryGetValue(arg, out ListOptionDef? listOption))
            {
                if (i + 1 >= args.Length)
                {
                    errors.Add($"{listOption.LongName} requires a value");
                    continue;
                }

                i++;
                if (!listValues.TryGetValue(listOption.LongName, out List<string>? list))
                {
                    list = new List<string>();
                    listValues[listOption.LongName] = list;
                }
                list.Add(args[i]);
                continue;
            }

            // Unknown flag
            errors.Add($"unknown option: {arg}");
        }

        // Handle --help and --version
        if (flagsSet.Contains("--help") && _standardFlagsRegistered)
        {
            Console.WriteLine(GenerateHelp());
            isHandled = true;
            handledExitCode = 0;
        }
        else if (flagsSet.Contains("--version") && _standardFlagsRegistered)
        {
            Console.WriteLine($"{_toolName} {_version}");
            isHandled = true;
            handledExitCode = 0;
        }

        return new ParseResult(
            toolName: _toolName,
            version: _version,
            flagsSet: flagsSet,
            optionValues: optionValues,
            listValues: listValues,
            command: command.ToArray(),
            positionals: positionals.ToArray(),
            errors: errors,
            isHandled: isHandled,
            handledExitCode: handledExitCode,
            usageErrorCode: _usageErrorCode,
            hasJson: flagsSet.Contains("--json"));
    }

    private void BuildLookups()
    {
        if (_flagLookup is not null)
        {
            return;
        }

        _flagLookup = new Dictionary<string, FlagDef>(StringComparer.Ordinal);
        _optionLookup = new Dictionary<string, OptionDef>(StringComparer.Ordinal);
        _listOptionLookup = new Dictionary<string, ListOptionDef>(StringComparer.Ordinal);
        _aliasLookup = new Dictionary<string, AliasDef>(StringComparer.Ordinal);

        foreach (FlagDef f in _flags)
        {
            _flagLookup[f.LongName] = f;
            if (f.ShortName is not null)
            {
                _flagLookup[f.ShortName] = f;
            }
        }

        foreach (OptionDef o in _options)
        {
            _optionLookup[o.LongName] = o;
            if (o.ShortName is not null)
            {
                _optionLookup[o.ShortName] = o;
            }
        }

        foreach (ListOptionDef l in _listOptions)
        {
            _listOptionLookup[l.LongName] = l;
            if (l.ShortName is not null)
            {
                _listOptionLookup[l.ShortName] = l;
            }
        }

        foreach (AliasDef a in _aliases)
        {
            _aliasLookup[a.Alias] = a;
        }
    }

    // Help generation placeholder — implemented in Task 8
    internal string GenerateHelp()
    {
        return $"Usage: {_toolName} [options]";
    }

    // Internal definition types
    internal sealed record FlagDef(string LongName, string? ShortName, string Description);

    internal enum OptionType { String, Int, Double }

    internal sealed record OptionDef(
        string LongName, string? ShortName, string Placeholder, string Description,
        OptionType Type,
        Func<int, string?>? IntValidate = null,
        Func<double, string?>? DoubleValidate = null);

    internal sealed record ListOptionDef(string LongName, string? ShortName, string Placeholder, string Description);

    internal sealed record AliasDef(string Alias, string TargetOption, string Value);
}
```

- [ ] **Step 4: Implement ParseResult**

In `src/Yort.ShellKit/ParseResult.cs`:

```csharp
using System.Globalization;

namespace Yort.ShellKit;

/// <summary>
/// Immutable result of parsing command-line arguments. Provides typed access to
/// flag and option values, error reporting, and color resolution.
/// </summary>
public sealed class ParseResult
{
    private readonly string _toolName;
    private readonly string _version;
    private readonly HashSet<string> _flagsSet;
    private readonly Dictionary<string, string> _optionValues;
    private readonly Dictionary<string, List<string>> _listValues;
    private readonly int _usageErrorCode;
    private readonly bool _hasJson;

    internal ParseResult(
        string toolName,
        string version,
        HashSet<string> flagsSet,
        Dictionary<string, string> optionValues,
        Dictionary<string, List<string>> listValues,
        string[] command,
        string[] positionals,
        List<string> errors,
        bool isHandled,
        int handledExitCode,
        int usageErrorCode,
        bool hasJson)
    {
        _toolName = toolName;
        _version = version;
        _flagsSet = flagsSet;
        _optionValues = optionValues;
        _listValues = listValues;
        _usageErrorCode = usageErrorCode;
        _hasJson = hasJson;
        Command = command;
        Positionals = positionals;
        Errors = errors.AsReadOnly();
        IsHandled = isHandled;
        ExitCode = handledExitCode;
    }

    /// <summary>Args after the command boundary (CommandMode only).</summary>
    public string[] Command { get; }

    /// <summary>Non-flag positional args (non-CommandMode).</summary>
    public string[] Positionals { get; }

    /// <summary>Parse error messages.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>True if --help or --version was handled (output already printed).</summary>
    public bool IsHandled { get; }

    /// <summary>Exit code when IsHandled is true.</summary>
    public int ExitCode { get; }

    /// <summary>True if any parse errors were detected.</summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>Returns true if the specified flag was present in the arguments.</summary>
    /// <param name="name">Long flag name (e.g. "--verbose").</param>
    public bool Has(string name)
    {
        return _flagsSet.Contains(name);
    }

    /// <summary>Returns the string value of an option, or the default if not provided.</summary>
    /// <exception cref="InvalidOperationException">Option was not provided and no default given.</exception>
    public string GetString(string name, string? defaultValue = null)
    {
        if (_optionValues.TryGetValue(name, out string? value))
        {
            return value;
        }

        if (defaultValue is not null)
        {
            return defaultValue;
        }

        throw new InvalidOperationException($"Option {name} was not provided.");
    }

    /// <summary>Returns the int value of an option, or the default if not provided.</summary>
    /// <exception cref="InvalidOperationException">Option was not provided and no default given.</exception>
    public int GetInt(string name, int? defaultValue = null)
    {
        if (_optionValues.TryGetValue(name, out string? raw))
        {
            return int.Parse(raw, CultureInfo.InvariantCulture);
        }

        if (defaultValue.HasValue)
        {
            return defaultValue.Value;
        }

        throw new InvalidOperationException($"Option {name} was not provided.");
    }

    /// <summary>Returns the double value of an option, or the default if not provided.</summary>
    /// <exception cref="InvalidOperationException">Option was not provided and no default given.</exception>
    public double GetDouble(string name, double? defaultValue = null)
    {
        if (_optionValues.TryGetValue(name, out string? raw))
        {
            return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        if (defaultValue.HasValue)
        {
            return defaultValue.Value;
        }

        throw new InvalidOperationException($"Option {name} was not provided.");
    }

    /// <summary>Returns all values for a list option (empty array if none).</summary>
    public string[] GetList(string name)
    {
        if (_listValues.TryGetValue(name, out List<string>? values))
        {
            return values.ToArray();
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Resolves whether colour output should be used, applying Winix precedence:
    /// explicit --color/--no-color flag &gt; NO_COLOR env var &gt; terminal auto-detection.
    /// </summary>
    public bool ResolveColor()
    {
        return ConsoleEnv.ResolveUseColor(
            Has("--color"),
            Has("--no-color"),
            ConsoleEnv.IsNoColorEnvSet(),
            ConsoleEnv.IsTerminal(checkStdErr: false));
    }

    /// <summary>
    /// Writes parse errors to the specified writer and returns the usage error exit code.
    /// If --json was set, writes a JSON error object instead of plain text.
    /// </summary>
    public int WriteErrors(TextWriter writer)
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
            foreach (string error in Errors)
            {
                writer.WriteLine($"{_toolName}: {error}");
            }
        }

        return _usageErrorCode;
    }

    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Yort.ShellKit.Tests/ --filter "FlagParsingTests"`
Expected: All 6 tests PASS

- [ ] **Step 6: Commit**

```
git add src/Yort.ShellKit/CommandLineParser.cs src/Yort.ShellKit/ParseResult.cs tests/Yort.ShellKit.Tests/CommandLineParserTests.cs
git commit -m "feat: add CommandLineParser with flag parsing and ParseResult"
```

---

### Task 3: Option parsing (string, int, double) with validation + tests

**Files:**
- Modify: `src/Yort.ShellKit/CommandLineParser.cs`
- Modify: `tests/Yort.ShellKit.Tests/CommandLineParserTests.cs`

- [ ] **Step 1: Write failing tests for option parsing**

Append to test file:

```csharp
public class OptionParsingTests
{
    [Fact]
    public void StringOption_Present_ReturnsValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Option("--output", "-o", "FILE", "Output file");

        var result = parser.Parse(new[] { "--output", "file.txt" });

        Assert.Equal("file.txt", result.GetString("--output"));
    }

    [Fact]
    public void StringOption_ShortForm_ReturnsValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Option("--output", "-o", "FILE", "Output file");

        var result = parser.Parse(new[] { "-o", "file.txt" });

        Assert.Equal("file.txt", result.GetString("--output"));
    }

    [Fact]
    public void StringOption_Absent_ReturnsDefault()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Option("--output", null, "FILE", "Output file");

        var result = parser.Parse(Array.Empty<string>());

        Assert.Equal("default.txt", result.GetString("--output", defaultValue: "default.txt"));
    }

    [Fact]
    public void StringOption_AbsentNoDefault_Throws()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Option("--output", null, "FILE", "Output file");

        var result = parser.Parse(Array.Empty<string>());

        Assert.Throws<InvalidOperationException>(() => result.GetString("--output"));
    }

    [Fact]
    public void StringOption_MissingValue_HasErrors()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Option("--output", null, "FILE", "Output file");

        var result = parser.Parse(new[] { "--output" });

        Assert.True(result.HasErrors);
        Assert.Contains("requires a value", result.Errors[0]);
    }

    [Fact]
    public void IntOption_Present_ReturnsValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--count", "-c", "N", "Count");

        var result = parser.Parse(new[] { "--count", "42" });

        Assert.Equal(42, result.GetInt("--count"));
    }

    [Fact]
    public void IntOption_InvalidValue_HasErrors()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--count", null, "N", "Count");

        var result = parser.Parse(new[] { "--count", "abc" });

        Assert.True(result.HasErrors);
        Assert.Contains("not a valid integer", result.Errors[0]);
    }

    [Fact]
    public void IntOption_WithValidation_RejectsInvalidValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--level", null, "N", "Level",
                validate: v => v >= 1 && v <= 9 ? null : "must be 1-9");

        var result = parser.Parse(new[] { "--level", "0" });

        Assert.True(result.HasErrors);
        Assert.Contains("must be 1-9", result.Errors[0]);
    }

    [Fact]
    public void IntOption_WithValidation_AcceptsValidValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--level", null, "N", "Level",
                validate: v => v >= 1 && v <= 9 ? null : "must be 1-9");

        var result = parser.Parse(new[] { "--level", "5" });

        Assert.False(result.HasErrors);
        Assert.Equal(5, result.GetInt("--level"));
    }

    [Fact]
    public void IntOption_Absent_ReturnsDefault()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--count", null, "N", "Count");

        var result = parser.Parse(Array.Empty<string>());

        Assert.Equal(10, result.GetInt("--count", defaultValue: 10));
    }

    [Fact]
    public void DoubleOption_Present_ReturnsValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .DoubleOption("--interval", "-n", "N", "Interval");

        var result = parser.Parse(new[] { "--interval", "2.5" });

        Assert.Equal(2.5, result.GetDouble("--interval"));
    }

    [Fact]
    public void DoubleOption_InvalidValue_HasErrors()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .DoubleOption("--interval", null, "N", "Interval");

        var result = parser.Parse(new[] { "--interval", "xyz" });

        Assert.True(result.HasErrors);
        Assert.Contains("not a valid number", result.Errors[0]);
    }

    [Fact]
    public void DoubleOption_WithValidation_RejectsInvalidValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .DoubleOption("--interval", null, "N", "Interval",
                validate: v => v > 0 ? null : "must be positive");

        var result = parser.Parse(new[] { "--interval", "-1.0" });

        Assert.True(result.HasErrors);
        Assert.Contains("must be positive", result.Errors[0]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Yort.ShellKit.Tests/ --filter "OptionParsingTests"`
Expected: FAIL — `Option`, `IntOption`, `DoubleOption` methods don't exist

- [ ] **Step 3: Add Option, IntOption, DoubleOption methods to CommandLineParser**

```csharp
    /// <summary>Registers a string-valued option.</summary>
    public CommandLineParser Option(string longName, string? shortName, string placeholder, string description)
    {
        _options.Add(new OptionDef(longName, shortName, placeholder, description, OptionType.String));
        return this;
    }

    /// <summary>Registers an integer-valued option with optional validation.</summary>
    public CommandLineParser IntOption(string longName, string? shortName, string placeholder, string description,
        Func<int, string?>? validate = null)
    {
        _options.Add(new OptionDef(longName, shortName, placeholder, description, OptionType.Int, IntValidate: validate));
        return this;
    }

    /// <summary>Registers a double-valued option with optional validation.</summary>
    public CommandLineParser DoubleOption(string longName, string? shortName, string placeholder, string description,
        Func<double, string?>? validate = null)
    {
        _options.Add(new OptionDef(longName, shortName, placeholder, description, OptionType.Double, DoubleValidate: validate));
        return this;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Yort.ShellKit.Tests/ --filter "OptionParsingTests"`
Expected: All 13 tests PASS

- [ ] **Step 5: Commit**

```
git add src/Yort.ShellKit/CommandLineParser.cs tests/Yort.ShellKit.Tests/CommandLineParserTests.cs
git commit -m "feat: add string, int, and double option parsing with validation"
```

---

### Task 4: ListOption + FlagAlias + tests

**Files:**
- Modify: `src/Yort.ShellKit/CommandLineParser.cs`
- Modify: `tests/Yort.ShellKit.Tests/CommandLineParserTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
public class ListOptionTests
{
    [Fact]
    public void ListOption_SingleValue_ReturnsSingleElementArray()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .ListOption("--watch", "-w", "GLOB", "Watch pattern");

        var result = parser.Parse(new[] { "--watch", "*.cs" });

        Assert.Equal(new[] { "*.cs" }, result.GetList("--watch"));
    }

    [Fact]
    public void ListOption_MultipleValues_ReturnsAll()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .ListOption("--watch", "-w", "GLOB", "Watch pattern");

        var result = parser.Parse(new[] { "-w", "*.cs", "-w", "*.fs" });

        Assert.Equal(new[] { "*.cs", "*.fs" }, result.GetList("--watch"));
    }

    [Fact]
    public void ListOption_Absent_ReturnsEmptyArray()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .ListOption("--watch", null, "GLOB", "Watch pattern");

        var result = parser.Parse(Array.Empty<string>());

        Assert.Empty(result.GetList("--watch"));
    }

    [Fact]
    public void ListOption_MissingValue_HasErrors()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .ListOption("--watch", null, "GLOB", "Watch pattern");

        var result = parser.Parse(new[] { "--watch" });

        Assert.True(result.HasErrors);
        Assert.Contains("requires a value", result.Errors[0]);
    }
}

public class FlagAliasTests
{
    [Fact]
    public void FlagAlias_ExpandsToOptionValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--level", null, "N", "Level")
            .FlagAlias("-9", "--level", "9");

        var result = parser.Parse(new[] { "-9" });

        Assert.Equal(9, result.GetInt("--level"));
    }

    [Fact]
    public void FlagAlias_MultipleAliases_LastWins()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--level", null, "N", "Level")
            .FlagAlias("-1", "--level", "1")
            .FlagAlias("-9", "--level", "9");

        var result = parser.Parse(new[] { "-1", "-9" });

        Assert.Equal(9, result.GetInt("--level"));
    }

    [Fact]
    public void FlagAlias_NotShownAsUnknown()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--level", null, "N", "Level")
            .FlagAlias("-5", "--level", "5");

        var result = parser.Parse(new[] { "-5" });

        Assert.False(result.HasErrors);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Yort.ShellKit.Tests/ --filter "ListOptionTests|FlagAliasTests"`
Expected: FAIL — `ListOption`, `FlagAlias` methods don't exist

- [ ] **Step 3: Add ListOption and FlagAlias methods**

```csharp
    /// <summary>Registers a repeatable option that collects values into a list.</summary>
    public CommandLineParser ListOption(string longName, string? shortName, string placeholder, string description)
    {
        _listOptions.Add(new ListOptionDef(longName, shortName, placeholder, description));
        return this;
    }

    /// <summary>
    /// Registers a flag alias that expands to an option+value pair during parsing.
    /// Used for backward-compatibility shortcuts (e.g. -9 → --level 9).
    /// Aliases are not shown in the auto-generated options table.
    /// </summary>
    public CommandLineParser FlagAlias(string alias, string targetOption, string value)
    {
        _aliases.Add(new AliasDef(alias, targetOption, value));
        return this;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Yort.ShellKit.Tests/ --filter "ListOptionTests|FlagAliasTests"`
Expected: All 7 tests PASS

- [ ] **Step 5: Commit**

```
git add src/Yort.ShellKit/CommandLineParser.cs tests/Yort.ShellKit.Tests/CommandLineParserTests.cs
git commit -m "feat: add ListOption and FlagAlias to CommandLineParser"
```

---

### Task 5: CommandMode + Positionals + -- separator + tests

**Files:**
- Modify: `src/Yort.ShellKit/CommandLineParser.cs`
- Modify: `tests/Yort.ShellKit.Tests/CommandLineParserTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
public class CommandModeTests
{
    [Fact]
    public void CommandMode_FirstNonFlag_StartsCommand()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose")
            .CommandMode();

        var result = parser.Parse(new[] { "--verbose", "ls", "-la" });

        Assert.True(result.Has("--verbose"));
        Assert.Equal(new[] { "ls", "-la" }, result.Command);
    }

    [Fact]
    public void CommandMode_DoubleDash_StartsCommand()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose")
            .CommandMode();

        var result = parser.Parse(new[] { "--verbose", "--", "git", "status" });

        Assert.True(result.Has("--verbose"));
        Assert.Equal(new[] { "git", "status" }, result.Command);
    }

    [Fact]
    public void CommandMode_NoCommand_EmptyArray()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .CommandMode();

        var result = parser.Parse(Array.Empty<string>());

        Assert.Empty(result.Command);
    }

    [Fact]
    public void CommandMode_DoubleDashThenFlags_FlagsAreCommand()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose")
            .CommandMode();

        var result = parser.Parse(new[] { "--", "--verbose", "arg" });

        Assert.False(result.Has("--verbose"));
        Assert.Equal(new[] { "--verbose", "arg" }, result.Command);
    }
}

public class PositionalTests
{
    [Fact]
    public void Positional_NonFlags_CollectedInOrder()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose");

        var result = parser.Parse(new[] { "file1.txt", "--verbose", "file2.txt" });

        Assert.Equal(new[] { "file1.txt", "file2.txt" }, result.Positionals);
        Assert.True(result.Has("--verbose"));
    }

    [Fact]
    public void Positional_AfterDoubleDash_AllPositional()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose");

        var result = parser.Parse(new[] { "--", "--verbose", "file.txt" });

        Assert.Equal(new[] { "--verbose", "file.txt" }, result.Positionals);
        Assert.False(result.Has("--verbose"));
    }

    [Fact]
    public void Positional_NoArgs_EmptyArray()
    {
        var parser = new CommandLineParser("test", "1.0.0");

        var result = parser.Parse(Array.Empty<string>());

        Assert.Empty(result.Positionals);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Yort.ShellKit.Tests/ --filter "CommandModeTests|PositionalTests"`
Expected: FAIL — `CommandMode()` method doesn't exist

- [ ] **Step 3: Add CommandMode and Positional methods**

```csharp
    /// <summary>
    /// Enables command mode: the first non-flag argument stops flag parsing,
    /// and it plus all remaining arguments become the command array.
    /// Used by tools that run child commands (timeit, peep).
    /// </summary>
    public CommandLineParser CommandMode()
    {
        _commandMode = true;
        return this;
    }

    /// <summary>Sets the label for positional args in the usage line (e.g. "files...").</summary>
    public CommandLineParser Positional(string label)
    {
        _positionalLabel = label;
        return this;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Yort.ShellKit.Tests/ --filter "CommandModeTests|PositionalTests"`
Expected: All 7 tests PASS

- [ ] **Step 5: Commit**

```
git add src/Yort.ShellKit/CommandLineParser.cs tests/Yort.ShellKit.Tests/CommandLineParserTests.cs
git commit -m "feat: add CommandMode and positional arg support to CommandLineParser"
```

---

### Task 6: StandardFlags + ResolveColor + IsHandled + tests

**Files:**
- Modify: `src/Yort.ShellKit/CommandLineParser.cs`
- Modify: `tests/Yort.ShellKit.Tests/CommandLineParserTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
public class StandardFlagTests
{
    [Fact]
    public void StandardFlags_RegistersHelpVersionColorJson()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .StandardFlags();

        var result = parser.Parse(new[] { "--json", "--no-color" });

        Assert.True(result.Has("--json"));
        Assert.True(result.Has("--no-color"));
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void StandardFlags_Help_IsHandled()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("test", "1.0.0")
                .StandardFlags();

            var result = parser.Parse(new[] { "--help" });

            Assert.True(result.IsHandled);
            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void StandardFlags_Version_IsHandled()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("test", "1.0.0")
                .StandardFlags();

            var result = parser.Parse(new[] { "--version" });

            Assert.True(result.IsHandled);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("test 1.0.0", writer.ToString());
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void StandardFlags_ShortHelp_IsHandled()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("test", "1.0.0")
                .StandardFlags();

            var result = parser.Parse(new[] { "-h" });

            Assert.True(result.IsHandled);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void StandardFlags_NormalArgs_NotHandled()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .StandardFlags()
            .Flag("--verbose", null, "Verbose");

        var result = parser.Parse(new[] { "--verbose" });

        Assert.False(result.IsHandled);
    }
}

public class WriteErrorsTests
{
    [Fact]
    public void WriteErrors_PlainText_WritesToolPrefixedErrors()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .Flag("--verbose", null, "Verbose");

        var result = parser.Parse(new[] { "--unknown" });

        var writer = new StringWriter();
        int exitCode = result.WriteErrors(writer);

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("mytool: unknown option: --unknown", writer.ToString());
    }

    [Fact]
    public void WriteErrors_JsonMode_WritesJsonError()
    {
        var parser = new CommandLineParser("mytool", "2.0.0")
            .StandardFlags()
            .Flag("--verbose", null, "Verbose");

        var result = parser.Parse(new[] { "--json", "--unknown" });

        var writer = new StringWriter();
        int exitCode = result.WriteErrors(writer);

        Assert.Equal(ExitCode.UsageError, exitCode);
        string output = writer.ToString();
        Assert.Contains("\"tool\":\"mytool\"", output);
        Assert.Contains("\"exit_code\":125", output);
        Assert.Contains("\"exit_reason\":\"usage_error\"", output);
    }

    [Fact]
    public void WriteErrors_CustomUsageErrorCode_ReturnsCustomCode()
    {
        var parser = new CommandLineParser("squeeze", "1.0.0")
            .Flag("--verbose", null, "Verbose")
            .UsageErrorCode(2);

        var result = parser.Parse(new[] { "--unknown" });

        var writer = new StringWriter();
        int exitCode = result.WriteErrors(writer);

        Assert.Equal(2, exitCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Yort.ShellKit.Tests/ --filter "StandardFlagTests|WriteErrorsTests"`
Expected: FAIL — `StandardFlags()`, `UsageErrorCode()` don't exist

- [ ] **Step 3: Add StandardFlags and UsageErrorCode methods**

```csharp
    /// <summary>
    /// Registers the standard Winix CLI flags: --help, -h, --version, --color, --no-color, --json.
    /// </summary>
    public CommandLineParser StandardFlags()
    {
        _standardFlagsRegistered = true;
        Flag("--help", "-h", "Show help");
        Flag("--version", "Show version");
        Flag("--color", "Force colored output");
        Flag("--no-color", "Disable colored output");
        Flag("--json", "JSON output to stderr");
        return this;
    }

    /// <summary>
    /// Overrides the exit code returned by <see cref="ParseResult.WriteErrors"/>.
    /// Default is 125 (POSIX usage error). Squeeze uses 2.
    /// </summary>
    public CommandLineParser UsageErrorCode(int code)
    {
        _usageErrorCode = code;
        return this;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Yort.ShellKit.Tests/ --filter "StandardFlagTests|WriteErrorsTests"`
Expected: All 8 tests PASS

- [ ] **Step 5: Commit**

```
git add src/Yort.ShellKit/CommandLineParser.cs tests/Yort.ShellKit.Tests/CommandLineParserTests.cs
git commit -m "feat: add StandardFlags, UsageErrorCode, WriteErrors to CommandLineParser"
```

---

### Task 7: ExitCodes metadata + Section + help text generation + tests

**Files:**
- Modify: `src/Yort.ShellKit/CommandLineParser.cs`
- Modify: `tests/Yort.ShellKit.Tests/CommandLineParserTests.cs`

- [ ] **Step 1: Write failing tests for help generation**

```csharp
public class HelpGenerationTests
{
    [Fact]
    public void GenerateHelp_IncludesUsageLine()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .Description("A test tool")
            .StandardFlags();

        // Access help via --help handling
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var result = parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("Usage: mytool", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_CommandMode_ShowsCommandInUsage()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .CommandMode();

        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("<command>", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_Positional_ShowsLabelInUsage()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .Positional("files...");

        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("[files...]", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_IncludesDescription()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .Description("A useful test tool")
            .StandardFlags();

        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("A useful test tool", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_IncludesRegisteredFlags()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .Flag("--verbose", "-v", "Enable verbose output");

        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("-v, --verbose", help);
            Assert.Contains("Enable verbose output", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_IncludesOptionWithPlaceholder()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .IntOption("--count", "-c", "N", "Number of items");

        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("-c, --count N", help);
            Assert.Contains("Number of items", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_ListOption_ShowsRepeatable()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .ListOption("--watch", "-w", "GLOB", "Watch pattern");

        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("(repeatable)", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_IncludesCustomSections()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .Section("Compatibility", "These flags match gzip");

        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("Compatibility:", help);
            Assert.Contains("These flags match gzip", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_IncludesExitCodes()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .ExitCodes(
                (0, "Success"),
                (125, "Usage error"));

        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("Exit Codes:", help);
            Assert.Contains("0", help);
            Assert.Contains("Success", help);
            Assert.Contains("125", help);
            Assert.Contains("Usage error", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_StandardFlagsAppearLast()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .Flag("--verbose", "-v", "Verbose output");

        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            int verbosePos = help.IndexOf("--verbose");
            int helpPos = help.IndexOf("--help");
            Assert.True(verbosePos < helpPos, "--verbose should appear before --help in options list");
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Yort.ShellKit.Tests/ --filter "HelpGenerationTests"`
Expected: FAIL — help output is just a stub

- [ ] **Step 3: Add Section, ExitCodes methods and implement GenerateHelp**

Add builder methods:

```csharp
    /// <summary>Adds a free-form text section to the help output.</summary>
    public CommandLineParser Section(string title, string body)
    {
        _sections.Add((title, body));
        return this;
    }

    /// <summary>Adds exit code descriptions to the help output.</summary>
    public CommandLineParser ExitCodes(params (int Code, string Description)[] codes)
    {
        _exitCodes.AddRange(codes);
        return this;
    }
```

Replace the `GenerateHelp` stub with the full implementation:

```csharp
    internal string GenerateHelp()
    {
        var sb = new StringBuilder();

        // Usage line
        sb.Append($"Usage: {_toolName} [options]");
        if (_commandMode)
        {
            sb.Append(" [--] <command> [args...]");
        }
        else if (_positionalLabel is not null)
        {
            sb.Append($" [{_positionalLabel}]");
        }
        sb.AppendLine();

        // Description
        if (_description is not null)
        {
            sb.AppendLine();
            sb.AppendLine(_description);
        }

        // Options table
        sb.AppendLine();
        sb.AppendLine("Options:");

        // Collect all option lines: (leftColumn, description, isStandard)
        var optionLines = new List<(string Left, string Desc, bool IsStandard)>();
        string[] standardNames = { "--help", "--version", "--color", "--no-color", "--json" };

        foreach (FlagDef f in _flags)
        {
            bool isStd = Array.IndexOf(standardNames, f.LongName) >= 0;
            string left = f.ShortName is not null
                ? $"  {f.ShortName}, {f.LongName}"
                : $"  {f.LongName}";
            optionLines.Add((left, f.Description, isStd));
        }

        foreach (OptionDef o in _options)
        {
            string left = o.ShortName is not null
                ? $"  {o.ShortName}, {o.LongName} {o.Placeholder}"
                : $"  {o.LongName} {o.Placeholder}";
            optionLines.Add((left, o.Description, false));
        }

        foreach (ListOptionDef l in _listOptions)
        {
            string left = l.ShortName is not null
                ? $"  {l.ShortName}, {l.LongName} {l.Placeholder}"
                : $"  {l.LongName} {l.Placeholder}";
            optionLines.Add((left, l.Description + " (repeatable)", false));
        }

        // Sort: non-standard first (in registration order), then standard
        var nonStandard = optionLines.Where(o => !o.IsStandard).ToList();
        var standard = optionLines.Where(o => o.IsStandard).ToList();
        var sorted = nonStandard.Concat(standard).ToList();

        // Calculate alignment
        int maxLeft = sorted.Max(o => o.Left.Length);
        int alignCol = maxLeft + 2; // minimum 2-space gap

        foreach (var (left, desc, _) in sorted)
        {
            sb.Append(left.PadRight(alignCol));
            sb.AppendLine(desc);
        }

        // Custom sections
        foreach (var (title, body) in _sections)
        {
            sb.AppendLine();
            sb.AppendLine($"{title}:");
            // Indent each line of the body by 2 spaces
            foreach (string line in body.Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (trimmed.Length > 0)
                {
                    sb.AppendLine($"  {trimmed}");
                }
                else
                {
                    sb.AppendLine();
                }
            }
        }

        // Exit codes
        if (_exitCodes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Exit Codes:");
            int maxCode = _exitCodes.Max(e => e.Code.ToString(CultureInfo.InvariantCulture).Length);
            foreach (var (code, desc) in _exitCodes)
            {
                string codeStr = code.ToString(CultureInfo.InvariantCulture);
                sb.AppendLine($"  {codeStr.PadRight(maxCode + 2)}{desc}");
            }
        }

        return sb.ToString().TrimEnd();
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Yort.ShellKit.Tests/ --filter "HelpGenerationTests"`
Expected: All 10 tests PASS

- [ ] **Step 5: Run full ShellKit test suite**

Run: `dotnet test tests/Yort.ShellKit.Tests/`
Expected: All tests PASS (existing + new)

- [ ] **Step 6: Commit**

```
git add src/Yort.ShellKit/CommandLineParser.cs tests/Yort.ShellKit.Tests/CommandLineParserTests.cs
git commit -m "feat: add help text generation with sections and exit codes"
```

---

### Task 8: Migrate timeit to CommandLineParser

**Files:**
- Modify: `src/timeit/Program.cs`

- [ ] **Step 1: Read current timeit Program.cs and plan migration**

Current timeit parsing: lines 8-56, ~50 lines of manual switch. Help: lines 133-156. Version: line 158-163.

- [ ] **Step 2: Rewrite timeit Program.cs arg parsing**

Replace the entire arg parsing section (lines 8-80) with:

```csharp
using System.Reflection;
using Winix.TimeIt;
using Yort.ShellKit;

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

if (result.Command.Length == 0)
{
    if (jsonOutput)
    {
        TextWriter errWriter = useStdout ? Console.Out : Console.Error;
        errWriter.WriteLine(Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "timeit", version));
    }
    else
    {
        Console.Error.WriteLine("timeit: no command specified. Run 'timeit --help' for usage.");
    }
    return ExitCode.UsageError;
}

string command = result.Command[0];
string[] commandArgs = result.Command.Skip(1).ToArray();
```

Keep everything after this point (command execution, formatting, output) exactly as-is. Remove the old `PrintHelp()` function — it's handled by the parser. Keep `GetVersion()`.

- [ ] **Step 3: Build and test**

Run: `dotnet build src/timeit/timeit.csproj`
Expected: Build succeeded, 0 warnings

Run: `dotnet test tests/Winix.TimeIt.Tests/`
Expected: All 30 tests pass

- [ ] **Step 4: Commit**

```
git add src/timeit/Program.cs
git commit -m "refactor: migrate timeit to CommandLineParser"
```

---

### Task 9: Migrate squeeze to CommandLineParser

**Files:**
- Modify: `src/squeeze/Program.cs`

- [ ] **Step 1: Rewrite squeeze arg parsing**

Squeeze is more complex: positional files, gzip compat aliases, custom exit codes, format-specific flags (`--brotli`, `--zstd`), `-k` no-op, level validation.

Replace the arg parsing section (lines 9-130) with:

```csharp
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
        .Section("Compatibility", """
            These flags match gzip for muscle memory:
            -d                  Same as --decompress
            -c                  Same as --stdout
            -k                  Accepted (keep is default, no-op)
            -1..-9              Same as --level 1..9
            -v                  Same as --verbose
            -f                  Same as --force""")
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
        // Need to check if we can use GetString here
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
```

Wait — squeeze has a special case: `-o -` means stdout. The parser stores the raw string value for `--output`. The tool can check:

```csharp
    string? outputFile = null;
    try
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
    catch (InvalidOperationException)
    {
        // --output not provided, that's fine
    }
```

Actually, that's ugly. Better to check `Has` first — but `Has` is for flags, not options. We need a way to check if an option was provided. Add a method or use a pattern. The simplest: just check if `GetString` with a sentinel default returns the sentinel:

Actually, looking at this more carefully, the `Has` method checks the flagsSet which only contains flags, not options. We need to be able to check if an option was set. Let me add this to the plan — `ParseResult` should expose `HasOption(name)` or the `Has` method should work for both flags and options.

The simplest fix: make `Has()` also check `_optionValues.ContainsKey(name)`. That way `result.Has("--output")` returns true if `--output FILE` was passed. This is intuitive and matches how tools currently use the check.

I'll note this in the plan — the implementer should update `Has()` to also check option and list option presence.

```csharp
    // In ParseResult.Has():
    public bool Has(string name)
    {
        return _flagsSet.Contains(name)
            || _optionValues.ContainsKey(name)
            || _listValues.ContainsKey(name);
    }
```

Then the squeeze code becomes clean:

```csharp
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

    int? levelFlag = null;
    if (result.Has("--level"))
    {
        levelFlag = result.GetInt("--level");
    }
```

Keep the rest of the tool logic (`RunPipeModeAsync`, `RunStdoutModeAsync`, etc.) exactly as-is. The arg parsing section is the only thing that changes. Remove `PrintHelp()`, `WriteUsageError()` — both handled by the parser.

- [ ] **Step 2: Update ParseResult.Has to also check options and lists**

In `src/Yort.ShellKit/ParseResult.cs`, update `Has()`:

```csharp
    public bool Has(string name)
    {
        return _flagsSet.Contains(name)
            || _optionValues.ContainsKey(name)
            || _listValues.ContainsKey(name);
    }
```

- [ ] **Step 3: Build and test**

Run: `dotnet build src/squeeze/squeeze.csproj`
Expected: Build succeeded, 0 warnings

Run: `dotnet test tests/Winix.Squeeze.Tests/`
Expected: All 103 tests pass

- [ ] **Step 4: Commit**

```
git add src/squeeze/Program.cs src/Yort.ShellKit/ParseResult.cs
git commit -m "refactor: migrate squeeze to CommandLineParser"
```

---

### Task 10: Migrate peep to CommandLineParser

**Files:**
- Modify: `src/peep/Program.cs`

- [ ] **Step 1: Rewrite peep arg parsing**

Peep is the most complex: repeatable options, typed numeric values with validation, command mode, custom sections, `--json-output` implying `--json`.

Replace the arg parsing section (lines 9-227) with:

```csharp
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
    .Section("Compatibility", """
        These flags match watch for muscle memory:
        -n N                   Same as --interval
        -g                     Same as --exit-on-change
        -e                     Same as --exit-on-error
        -d                     Same as --differences
        -t                     Same as --no-header""")
    .Section("Interactive", """
        q / Ctrl+C             Quit
        Space                  Pause/unpause display
        r / Enter              Force immediate re-run
        d                      Toggle diff highlighting
        Up/Down / PgUp/Dn     Scroll while paused
        Left/Right             Time travel (older/newer)
        t                      History overlay
        ?                      Show/hide help overlay""")
    .CommandMode()
    .ExitCodes(
        (0, "Auto-exit condition met, or manual quit with last child exit 0"),
        (ExitCode.UsageError, "Usage error"),
        (ExitCode.NotExecutable, "Command not executable"),
        (ExitCode.NotFound, "Command not found"));

var result = parser.Parse(args);
if (result.IsHandled) return result.ExitCode;
if (result.HasErrors) return result.WriteErrors(Console.Error);

// Extract values
double intervalSeconds = result.GetDouble("--interval", defaultValue: 2.0);
bool intervalExplicit = result.Has("--interval");
string[] watchPatterns = result.GetList("--watch");
int debounceMs = result.GetInt("--debounce", defaultValue: 300);
int historyCapacity = result.GetInt("--history", defaultValue: 1000);
bool exitOnChange = result.Has("--exit-on-change");
bool exitOnSuccess = result.Has("--exit-on-success");
bool exitOnError = result.Has("--exit-on-error");
string[] exitOnMatchPatterns = result.GetList("--exit-on-match").ToArray();
bool diffEnabled = result.Has("--differences");
bool noGitIgnore = result.Has("--no-gitignore");
bool once = result.Has("--once");
bool noHeader = result.Has("--no-header");
bool jsonOutput = result.Has("--json") || result.Has("--json-output");
bool jsonOutputIncludeOutput = result.Has("--json-output");
bool useColor = result.ResolveColor();

// If only file watching (no explicit -n), disable interval polling
bool useInterval = watchPatterns.Length == 0 || intervalExplicit;

if (result.Command.Length == 0)
{
    if (jsonOutput)
    {
        Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "peep", version));
    }
    else
    {
        Console.Error.WriteLine("peep: no command specified. Run 'peep --help' for usage.");
    }
    return ExitCode.UsageError;
}

string command = result.Command[0];
string[] commandArgs = result.Command.Skip(1).ToArray();
string commandDisplay = string.Join(" ", result.Command);
```

Compile regex patterns from exitOnMatchPatterns (same as current code):

```csharp
Regex[] exitOnMatchRegexes;
try
{
    exitOnMatchRegexes = exitOnMatchPatterns
        .Select(p => new Regex(p, RegexOptions.Compiled))
        .ToArray();
}
catch (RegexParseException ex)
{
    if (jsonOutput)
    {
        Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "peep", version));
    }
    else
    {
        Console.Error.WriteLine($"peep: invalid regex pattern: {ex.Message}");
    }
    return ExitCode.UsageError;
}
```

Keep the rest of peep (`RunOnceAsync`, `RunLoopAsync`, all event loop code, `RenderScreen`, `RenderTimeMachineScreen`, etc.) exactly as-is. Remove `PrintHelp()` and `WriteUsageError()`. Keep `GetVersion()`, `GetTerminalHeight()`, `GetTerminalWidth()`.

- [ ] **Step 2: Build and test**

Run: `dotnet build src/peep/peep.csproj`
Expected: Build succeeded, 0 warnings

Run: `dotnet test tests/Winix.Peep.Tests/`
Expected: All 140 tests pass

- [ ] **Step 3: Commit**

```
git add src/peep/Program.cs
git commit -m "refactor: migrate peep to CommandLineParser"
```

---

### Task 11: Full build, test, and AOT verification

**Files:** None (verification only)

- [ ] **Step 1: Full build**

Run: `dotnet build Winix.sln`
Expected: Build succeeded, 0 errors, 0 warnings

- [ ] **Step 2: Full test suite**

Run: `dotnet test Winix.sln`
Expected: All tests pass. Total should be ~350+ tests (305 existing + ~50 new parser tests).

- [ ] **Step 3: AOT publish all three tools**

Run: `dotnet publish src/timeit/timeit.csproj -c Release -r win-x64`
Run: `dotnet publish src/squeeze/squeeze.csproj -c Release -r win-x64`
Run: `dotnet publish src/peep/peep.csproj -c Release -r win-x64`
Expected: All three succeed with no trim warnings related to the parser.

- [ ] **Step 4: Smoke test help output**

Run each tool with `--help` and `--version` from the publish output to verify help is correctly generated and version is printed.

- [ ] **Step 5: Commit any fixups**

If any fixes were needed during verification:
```
git add -A
git commit -m "fix: address build/test issues from parser migration"
```
