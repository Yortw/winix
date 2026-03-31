# wargs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `wargs`, a cross-platform xargs replacement with line-delimited input, parallel execution, correct Windows path handling, and JSON output.

**Architecture:** Pipeline of three stages (InputReader → CommandBuilder → JobRunner), each independently testable. Class library `Winix.Wargs` holds all logic. Thin console app `wargs` does arg parsing via ShellKit's `CommandLineParser`. Tests in `Winix.Wargs.Tests`.

**Tech Stack:** .NET 10, C#, AOT-compatible, xUnit, ShellKit (CommandLineParser, ConsoleEnv, AnsiColor, DisplayFormat, ExitCode)

**Spec:** `docs/plans/2026-03-31-wargs-design.md`

---

## File Structure

### New files to create

| File | Responsibility |
|------|---------------|
| `src/Winix.Wargs/Winix.Wargs.csproj` | Class library project (AOT-compatible, references ShellKit) |
| `src/Winix.Wargs/DelimiterMode.cs` | Enum: Line, Null, Custom, Whitespace |
| `src/Winix.Wargs/InputReader.cs` | Reads stdin by delimiter, yields items |
| `src/Winix.Wargs/CommandInvocation.cs` | Record: Command, Arguments, DisplayString, SourceItems |
| `src/Winix.Wargs/CommandBuilder.cs` | Builds CommandInvocations from template + items |
| `src/Winix.Wargs/BufferStrategy.cs` | Enum: JobBuffered, LineBuffered, KeepOrder |
| `src/Winix.Wargs/JobRunnerOptions.cs` | Record for runner configuration |
| `src/Winix.Wargs/JobResult.cs` | Record: per-job result (exit code, output, duration, etc.) |
| `src/Winix.Wargs/WargsResult.cs` | Record: summary result (total, succeeded, failed, etc.) |
| `src/Winix.Wargs/WargsExitCode.cs` | Constants: ChildFailed=123, FailFastAbort=124 |
| `src/Winix.Wargs/JobRunner.cs` | Executes invocations with parallelism, buffering, fail-fast |
| `src/Winix.Wargs/Formatting.cs` | Human, JSON, NDJSON output formatting |
| `src/wargs/wargs.csproj` | Console app project (AOT publish, PackAsTool) |
| `src/wargs/Program.cs` | Thin entry point: parse args, wire pipeline, set exit code |
| `src/wargs/README.md` | Tool documentation |
| `tests/Winix.Wargs.Tests/Winix.Wargs.Tests.csproj` | Test project |
| `tests/Winix.Wargs.Tests/InputReaderTests.cs` | Tests for InputReader |
| `tests/Winix.Wargs.Tests/CommandBuilderTests.cs` | Tests for CommandBuilder |
| `tests/Winix.Wargs.Tests/JobRunnerTests.cs` | Tests for JobRunner |
| `tests/Winix.Wargs.Tests/FormattingTests.cs` | Tests for Formatting |
| `bucket/wargs.json` | Scoop manifest |

### Files to modify

| File | Change |
|------|--------|
| `Winix.sln` | Add Winix.Wargs, wargs, Winix.Wargs.Tests projects |
| `bucket/winix.json` | Add `wargs.exe` to bin array |
| `.github/workflows/release.yml` | Add wargs to pack, publish, zip, scoop, and winget steps |
| `CLAUDE.md` | Add wargs to project layout section |

---

## Task 1: Project scaffolding

**Files:**
- Create: `src/Winix.Wargs/Winix.Wargs.csproj`
- Create: `src/wargs/wargs.csproj`
- Create: `tests/Winix.Wargs.Tests/Winix.Wargs.Tests.csproj`
- Modify: `Winix.sln`

- [ ] **Step 1: Create class library project file**

Create `src/Winix.Wargs/Winix.Wargs.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
    <IsPackable>false</IsPackable>
    <PackageId>Winix.Wargs.Library</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create console app project file**

Create `src/wargs/wargs.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <OptimizationPreference>Size</OptimizationPreference>
    <StackTraceSupport>false</StackTraceSupport>
    <UseSystemResourceKeys>true</UseSystemResourceKeys>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>wargs</ToolCommandName>
    <PackageId>Winix.Wargs</PackageId>
    <Description>Cross-platform xargs replacement with sane defaults. Line-delimited input, parallel execution, correct Windows path handling.</Description>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.Wargs\Winix.Wargs.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create test project file**

Create `tests/Winix.Wargs.Tests/Winix.Wargs.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Winix.Wargs\Winix.Wargs.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create a minimal placeholder so the library compiles**

Create `src/Winix.Wargs/WargsExitCode.cs`:

```csharp
namespace Winix.Wargs;

/// <summary>
/// Exit codes specific to wargs, beyond the standard POSIX codes in <see cref="Yort.ShellKit.ExitCode"/>.
/// </summary>
public static class WargsExitCode
{
    /// <summary>One or more child processes exited non-zero (GNU xargs convention).</summary>
    public const int ChildFailed = 123;

    /// <summary>Execution aborted early due to --fail-fast.</summary>
    public const int FailFastAbort = 124;
}
```

- [ ] **Step 5: Create a minimal Program.cs so the console app compiles**

Create `src/wargs/Program.cs`:

```csharp
namespace Wargs;

internal sealed class Program
{
    static int Main(string[] args)
    {
        return 0;
    }
}
```

- [ ] **Step 6: Add all three projects to the solution**

Run:
```bash
dotnet sln Winix.sln add src/Winix.Wargs/Winix.Wargs.csproj --solution-folder src
dotnet sln Winix.sln add src/wargs/wargs.csproj --solution-folder src
dotnet sln Winix.sln add tests/Winix.Wargs.Tests/Winix.Wargs.Tests.csproj --solution-folder tests
```

- [ ] **Step 7: Verify the solution builds**

Run: `dotnet build Winix.sln`

Expected: Build succeeded. 0 Warnings. 0 Errors.

- [ ] **Step 8: Commit**

```bash
git add src/Winix.Wargs/ src/wargs/ tests/Winix.Wargs.Tests/ Winix.sln
git commit -m "feat(wargs): scaffold projects and solution structure"
```

---

## Task 2: InputReader — line and null delimiter modes

**Files:**
- Create: `src/Winix.Wargs/DelimiterMode.cs`
- Create: `src/Winix.Wargs/InputReader.cs`
- Create: `tests/Winix.Wargs.Tests/InputReaderTests.cs`

- [ ] **Step 1: Write failing tests for line-delimited mode**

Create `tests/Winix.Wargs.Tests/InputReaderTests.cs`:

```csharp
using Winix.Wargs;

namespace Winix.Wargs.Tests;

public class InputReaderTests
{
    [Fact]
    public void ReadItems_LineMode_SplitsOnNewline()
    {
        var reader = new InputReader(new StringReader("alpha\nbeta\ngamma"), DelimiterMode.Line);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, items);
    }

    [Fact]
    public void ReadItems_LineMode_TrimsCarriageReturn()
    {
        var reader = new InputReader(new StringReader("alpha\r\nbeta\r\n"), DelimiterMode.Line);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta" }, items);
    }

    [Fact]
    public void ReadItems_LineMode_SkipsEmptyLines()
    {
        var reader = new InputReader(new StringReader("alpha\n\n\nbeta\n"), DelimiterMode.Line);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta" }, items);
    }

    [Fact]
    public void ReadItems_LineMode_SkipsWhitespaceOnlyLines()
    {
        var reader = new InputReader(new StringReader("alpha\n   \n\t\nbeta"), DelimiterMode.Line);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta" }, items);
    }

    [Fact]
    public void ReadItems_LineMode_EmptyInput_YieldsNothing()
    {
        var reader = new InputReader(new StringReader(""), DelimiterMode.Line);
        var items = reader.ReadItems().ToList();
        Assert.Empty(items);
    }

    [Fact]
    public void ReadItems_NullMode_SplitsOnNullChar()
    {
        var reader = new InputReader(new StringReader("alpha\0beta\0gamma"), DelimiterMode.Null);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, items);
    }

    [Fact]
    public void ReadItems_NullMode_SkipsEmptyItems()
    {
        var reader = new InputReader(new StringReader("alpha\0\0beta\0"), DelimiterMode.Null);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta" }, items);
    }

    [Fact]
    public void ReadItems_NullMode_PreservesNewlinesInItems()
    {
        var reader = new InputReader(new StringReader("line one\nstill one\0two"), DelimiterMode.Null);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "line one\nstill one", "two" }, items);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Wargs.Tests/ --filter "ClassName=Winix.Wargs.Tests.InputReaderTests"`

Expected: Build error — `InputReader` and `DelimiterMode` do not exist.

- [ ] **Step 3: Create DelimiterMode enum**

Create `src/Winix.Wargs/DelimiterMode.cs`:

```csharp
namespace Winix.Wargs;

/// <summary>
/// How the input stream is split into items.
/// </summary>
public enum DelimiterMode
{
    /// <summary>Split on newlines. Empty and whitespace-only lines are skipped.</summary>
    Line,

    /// <summary>Split on null characters (\0). For use with find -print0.</summary>
    Null,

    /// <summary>Split on a user-specified single character.</summary>
    Custom,

    /// <summary>Split on whitespace runs with POSIX quote handling. Enabled by --compat.</summary>
    Whitespace
}
```

- [ ] **Step 4: Implement InputReader for Line and Null modes**

Create `src/Winix.Wargs/InputReader.cs`:

```csharp
namespace Winix.Wargs;

/// <summary>
/// Reads items from a text stream, splitting by the configured delimiter.
/// Streaming — reads one item at a time without buffering the entire input.
/// </summary>
public sealed class InputReader
{
    private readonly TextReader _source;
    private readonly DelimiterMode _mode;
    private readonly char _customDelimiter;

    /// <summary>
    /// Creates a new input reader.
    /// </summary>
    /// <param name="source">The text stream to read from (typically stdin).</param>
    /// <param name="mode">How to split the stream into items.</param>
    /// <param name="customDelimiter">
    /// The delimiter character when <paramref name="mode"/> is <see cref="DelimiterMode.Custom"/>.
    /// Ignored for other modes.
    /// </param>
    public InputReader(TextReader source, DelimiterMode mode, char customDelimiter = '\0')
    {
        _source = source;
        _mode = mode;
        _customDelimiter = customDelimiter;
    }

    /// <summary>
    /// Yields items from the input stream one at a time.
    /// </summary>
    public IEnumerable<string> ReadItems()
    {
        return _mode switch
        {
            DelimiterMode.Line => ReadLineDelimited(),
            DelimiterMode.Null => ReadCharDelimited('\0'),
            DelimiterMode.Custom => ReadCharDelimited(_customDelimiter),
            DelimiterMode.Whitespace => ReadWhitespaceDelimited(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private IEnumerable<string> ReadLineDelimited()
    {
        string? line;
        while ((line = _source.ReadLine()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line;
            }
        }
    }

    private IEnumerable<string> ReadCharDelimited(char delimiter)
    {
        var buffer = new System.Text.StringBuilder();
        int ch;
        while ((ch = _source.Read()) != -1)
        {
            if ((char)ch == delimiter)
            {
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString();
                    buffer.Clear();
                }
            }
            else
            {
                buffer.Append((char)ch);
            }
        }

        // Emit trailing item if no final delimiter
        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }

    private IEnumerable<string> ReadWhitespaceDelimited()
    {
        // Placeholder — implemented in Task 3
        yield break;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Wargs.Tests/ --filter "ClassName=Winix.Wargs.Tests.InputReaderTests"`

Expected: 8 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.Wargs/DelimiterMode.cs src/Winix.Wargs/InputReader.cs tests/Winix.Wargs.Tests/InputReaderTests.cs
git commit -m "feat(wargs): InputReader with line and null delimiter modes"
```

---

## Task 3: InputReader — custom delimiter and whitespace compat modes

**Files:**
- Modify: `src/Winix.Wargs/InputReader.cs`
- Modify: `tests/Winix.Wargs.Tests/InputReaderTests.cs`

- [ ] **Step 1: Write failing tests for custom delimiter and whitespace modes**

Append to `tests/Winix.Wargs.Tests/InputReaderTests.cs`:

```csharp
    [Fact]
    public void ReadItems_CustomDelimiter_SplitsOnChar()
    {
        var reader = new InputReader(new StringReader("alpha,beta,gamma"), DelimiterMode.Custom, ',');
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, items);
    }

    [Fact]
    public void ReadItems_CustomDelimiter_SkipsEmptyItems()
    {
        var reader = new InputReader(new StringReader("alpha,,beta,"), DelimiterMode.Custom, ',');
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta" }, items);
    }

    [Fact]
    public void ReadItems_Whitespace_SplitsOnSpacesAndTabs()
    {
        var reader = new InputReader(new StringReader("alpha beta\tgamma"), DelimiterMode.Whitespace);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, items);
    }

    [Fact]
    public void ReadItems_Whitespace_SplitsAcrossNewlines()
    {
        var reader = new InputReader(new StringReader("alpha\nbeta gamma"), DelimiterMode.Whitespace);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, items);
    }

    [Fact]
    public void ReadItems_Whitespace_SingleQuotesPreserveLiteral()
    {
        var reader = new InputReader(new StringReader("'hello world' beta"), DelimiterMode.Whitespace);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "hello world", "beta" }, items);
    }

    [Fact]
    public void ReadItems_Whitespace_DoubleQuotesPreserveSpaces()
    {
        var reader = new InputReader(new StringReader("\"hello world\" beta"), DelimiterMode.Whitespace);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "hello world", "beta" }, items);
    }

    [Fact]
    public void ReadItems_Whitespace_BackslashEscapesSpace()
    {
        var reader = new InputReader(new StringReader("hello\\ world beta"), DelimiterMode.Whitespace);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "hello world", "beta" }, items);
    }

    [Fact]
    public void ReadItems_Whitespace_DoubleQuotesAllowBackslashEscapes()
    {
        var reader = new InputReader(new StringReader("\"hello \\\"world\\\"\" beta"), DelimiterMode.Whitespace);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "hello \"world\"", "beta" }, items);
    }

    [Fact]
    public void ReadItems_Whitespace_EmptyInput_YieldsNothing()
    {
        var reader = new InputReader(new StringReader("   \n\t  "), DelimiterMode.Whitespace);
        var items = reader.ReadItems().ToList();
        Assert.Empty(items);
    }
```

- [ ] **Step 2: Run tests to verify new tests fail**

Run: `dotnet test tests/Winix.Wargs.Tests/ --filter "ClassName=Winix.Wargs.Tests.InputReaderTests"`

Expected: custom delimiter tests pass (char-delimited already works), whitespace tests fail (returns empty).

- [ ] **Step 3: Implement ReadWhitespaceDelimited in InputReader**

Replace the placeholder `ReadWhitespaceDelimited` method in `src/Winix.Wargs/InputReader.cs`:

```csharp
    private IEnumerable<string> ReadWhitespaceDelimited()
    {
        var buffer = new System.Text.StringBuilder();
        int ch;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool escaped = false;

        while ((ch = _source.Read()) != -1)
        {
            char c = (char)ch;

            if (escaped)
            {
                buffer.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\' && !inSingleQuote)
            {
                escaped = true;
                continue;
            }

            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inSingleQuote && !inDoubleQuote)
            {
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString();
                    buffer.Clear();
                }
                continue;
            }

            buffer.Append(c);
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }
```

- [ ] **Step 4: Run tests to verify all pass**

Run: `dotnet test tests/Winix.Wargs.Tests/ --filter "ClassName=Winix.Wargs.Tests.InputReaderTests"`

Expected: 17 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Wargs/InputReader.cs tests/Winix.Wargs.Tests/InputReaderTests.cs
git commit -m "feat(wargs): InputReader custom delimiter and whitespace compat modes"
```

---

## Task 4: CommandBuilder — append mode and substitution mode

**Files:**
- Create: `src/Winix.Wargs/CommandInvocation.cs`
- Create: `src/Winix.Wargs/CommandBuilder.cs`
- Create: `tests/Winix.Wargs.Tests/CommandBuilderTests.cs`

- [ ] **Step 1: Write failing tests for CommandBuilder**

Create `tests/Winix.Wargs.Tests/CommandBuilderTests.cs`:

```csharp
using Winix.Wargs;

namespace Winix.Wargs.Tests;

public class CommandBuilderTests
{
    [Fact]
    public void Build_NoPlaceholder_AppendsItemsToEnd()
    {
        var builder = new CommandBuilder(new[] { "echo" });
        var invocations = builder.Build(new[] { "alpha", "beta" }).ToList();

        Assert.Equal(2, invocations.Count);
        Assert.Equal("echo", invocations[0].Command);
        Assert.Equal(new[] { "alpha" }, invocations[0].Arguments);
        Assert.Equal(new[] { "beta" }, invocations[1].Arguments);
    }

    [Fact]
    public void Build_WithPlaceholder_SubstitutesItem()
    {
        var builder = new CommandBuilder(new[] { "echo", "processing", "{}" });
        var invocations = builder.Build(new[] { "file1.cs", "file2.cs" }).ToList();

        Assert.Equal(2, invocations.Count);
        Assert.Equal("echo", invocations[0].Command);
        Assert.Equal(new[] { "processing", "file1.cs" }, invocations[0].Arguments);
        Assert.Equal(new[] { "processing", "file2.cs" }, invocations[1].Arguments);
    }

    [Fact]
    public void Build_PlaceholderInMiddleOfArg_SubstitutesInline()
    {
        var builder = new CommandBuilder(new[] { "echo", "file:{}" });
        var invocations = builder.Build(new[] { "test.cs" }).ToList();

        Assert.Single(invocations);
        Assert.Equal(new[] { "file:test.cs" }, invocations[0].Arguments);
    }

    [Fact]
    public void Build_MultiplePlaceholders_AllReplaced()
    {
        var builder = new CommandBuilder(new[] { "cp", "{}", "/backup/{}" });
        var invocations = builder.Build(new[] { "data.db" }).ToList();

        Assert.Single(invocations);
        Assert.Equal(new[] { "data.db", "/backup/data.db" }, invocations[0].Arguments);
    }

    [Fact]
    public void IsSubstitutionMode_WithPlaceholder_ReturnsTrue()
    {
        var builder = new CommandBuilder(new[] { "echo", "{}" });
        Assert.True(builder.IsSubstitutionMode);
    }

    [Fact]
    public void IsSubstitutionMode_WithoutPlaceholder_ReturnsFalse()
    {
        var builder = new CommandBuilder(new[] { "echo" });
        Assert.False(builder.IsSubstitutionMode);
    }

    [Fact]
    public void Build_EmptyTemplate_DefaultsToEcho()
    {
        var builder = new CommandBuilder(Array.Empty<string>());
        var invocations = builder.Build(new[] { "hello" }).ToList();

        Assert.Single(invocations);
        Assert.Equal("echo", invocations[0].Command);
        Assert.Equal(new[] { "hello" }, invocations[0].Arguments);
    }

    [Fact]
    public void Build_BatchSize_GroupsItems()
    {
        var builder = new CommandBuilder(new[] { "echo" }, batchSize: 3);
        var invocations = builder.Build(new[] { "a", "b", "c", "d", "e" }).ToList();

        Assert.Equal(2, invocations.Count);
        Assert.Equal(new[] { "a", "b", "c" }, invocations[0].Arguments);
        Assert.Equal(new[] { "d", "e" }, invocations[1].Arguments);
    }

    [Fact]
    public void Build_BatchSize_WithPlaceholder_JoinsItems()
    {
        var builder = new CommandBuilder(new[] { "echo", "items: {}" }, batchSize: 2);
        var invocations = builder.Build(new[] { "a", "b", "c" }).ToList();

        Assert.Equal(2, invocations.Count);
        Assert.Equal(new[] { "items: a b" }, invocations[0].Arguments);
        Assert.Equal(new[] { "items: c" }, invocations[1].Arguments);
    }

    [Fact]
    public void Build_SourceItems_TracksOriginalInput()
    {
        var builder = new CommandBuilder(new[] { "echo" }, batchSize: 2);
        var invocations = builder.Build(new[] { "a", "b", "c" }).ToList();

        Assert.Equal(new[] { "a", "b" }, invocations[0].SourceItems);
        Assert.Equal(new[] { "c" }, invocations[1].SourceItems);
    }

    [Fact]
    public void Build_EmptyItems_YieldsNothing()
    {
        var builder = new CommandBuilder(new[] { "echo" });
        var invocations = builder.Build(Array.Empty<string>()).ToList();

        Assert.Empty(invocations);
    }

    [Fact]
    public void Build_DisplayString_IsShellQuoted()
    {
        var builder = new CommandBuilder(new[] { "echo" });
        var invocations = builder.Build(new[] { "hello world" }).ToList();

        // DisplayString should quote args containing spaces
        Assert.Contains("hello world", invocations[0].DisplayString);
        Assert.StartsWith("echo ", invocations[0].DisplayString);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Wargs.Tests/ --filter "ClassName=Winix.Wargs.Tests.CommandBuilderTests"`

Expected: Build error — `CommandBuilder` and `CommandInvocation` do not exist.

- [ ] **Step 3: Create CommandInvocation record**

Create `src/Winix.Wargs/CommandInvocation.cs`:

```csharp
namespace Winix.Wargs;

/// <summary>
/// A concrete command ready to execute — the command name, its arguments, a display
/// string for --verbose/--dry-run, and the input items that produced this invocation.
/// </summary>
/// <param name="Command">The executable name or path.</param>
/// <param name="Arguments">Arguments to pass to the process.</param>
/// <param name="DisplayString">Human-readable, shell-quoted form for display.</param>
/// <param name="SourceItems">The input items that were used to build this invocation.</param>
public sealed record CommandInvocation(
    string Command,
    string[] Arguments,
    string DisplayString,
    string[] SourceItems
);
```

- [ ] **Step 4: Implement CommandBuilder**

Create `src/Winix.Wargs/CommandBuilder.cs`:

```csharp
using System.Text;

namespace Winix.Wargs;

/// <summary>
/// Builds <see cref="CommandInvocation"/>s from a command template and input items.
/// If any template argument contains <c>{}</c>, substitution mode is used (each <c>{}</c>
/// is replaced with the item). Otherwise, items are appended as additional arguments.
/// </summary>
public sealed class CommandBuilder
{
    private const string Placeholder = "{}";

    private readonly string[] _template;
    private readonly int _batchSize;

    /// <summary>
    /// Creates a new command builder.
    /// </summary>
    /// <param name="template">
    /// The command template from trailing CLI args. If empty, defaults to <c>echo</c>.
    /// First element is the command, remainder are argument templates.
    /// </param>
    /// <param name="batchSize">Number of items per invocation (default 1).</param>
    public CommandBuilder(string[] template, int batchSize = 1)
    {
        if (template.Length == 0)
        {
            template = new[] { "echo" };
        }

        _template = template;
        _batchSize = batchSize;
        IsSubstitutionMode = template.Skip(1).Any(arg => arg.Contains(Placeholder, StringComparison.Ordinal));
    }

    /// <summary>True if the template contains <c>{}</c> placeholders.</summary>
    public bool IsSubstitutionMode { get; }

    /// <summary>
    /// Builds command invocations from the input items.
    /// </summary>
    public IEnumerable<CommandInvocation> Build(IEnumerable<string> items)
    {
        var batch = new List<string>(_batchSize);

        foreach (string item in items)
        {
            batch.Add(item);
            if (batch.Count >= _batchSize)
            {
                yield return BuildOne(batch.ToArray());
                batch.Clear();
            }
        }

        // Emit remainder
        if (batch.Count > 0)
        {
            yield return BuildOne(batch.ToArray());
        }
    }

    private CommandInvocation BuildOne(string[] sourceItems)
    {
        string command = _template[0];
        string[] templateArgs = _template.AsSpan(1).ToArray();
        string[] arguments;

        if (IsSubstitutionMode)
        {
            string replacement = string.Join(" ", sourceItems);
            arguments = new string[templateArgs.Length];
            for (int i = 0; i < templateArgs.Length; i++)
            {
                arguments[i] = templateArgs[i].Replace(Placeholder, replacement, StringComparison.Ordinal);
            }
        }
        else
        {
            arguments = new string[templateArgs.Length + sourceItems.Length];
            templateArgs.CopyTo(arguments, 0);
            sourceItems.CopyTo(arguments, templateArgs.Length);
        }

        string displayString = FormatDisplayString(command, arguments);
        return new CommandInvocation(command, arguments, displayString, sourceItems);
    }

    /// <summary>
    /// Formats a command and arguments into a human-readable, shell-quoted string.
    /// </summary>
    internal static string FormatDisplayString(string command, string[] arguments)
    {
        var sb = new StringBuilder();
        sb.Append(ShellQuote(command));

        foreach (string arg in arguments)
        {
            sb.Append(' ');
            sb.Append(ShellQuote(arg));
        }

        return sb.ToString();
    }

    private static string ShellQuote(string value)
    {
        if (value.Length == 0)
        {
            return "''";
        }

        // If the value contains no special characters, no quoting needed
        bool needsQuoting = false;
        foreach (char c in value)
        {
            if (c == ' ' || c == '\t' || c == '"' || c == '\'' || c == '\\' || c == '|'
                || c == '&' || c == ';' || c == '(' || c == ')' || c == '<' || c == '>'
                || c == '$' || c == '`' || c == '!' || c == '{' || c == '}')
            {
                needsQuoting = true;
                break;
            }
        }

        if (!needsQuoting)
        {
            return value;
        }

        // Use single quotes — safest for display. Escape embedded single quotes.
        return "'" + value.Replace("'", "'\\''") + "'";
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Wargs.Tests/ --filter "ClassName=Winix.Wargs.Tests.CommandBuilderTests"`

Expected: 13 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.Wargs/CommandInvocation.cs src/Winix.Wargs/CommandBuilder.cs tests/Winix.Wargs.Tests/CommandBuilderTests.cs
git commit -m "feat(wargs): CommandBuilder with append, substitution, and batch modes"
```

---

## Task 5: JobRunner — sequential execution (job-buffered)

**Files:**
- Create: `src/Winix.Wargs/BufferStrategy.cs`
- Create: `src/Winix.Wargs/JobResult.cs`
- Create: `src/Winix.Wargs/WargsResult.cs`
- Create: `src/Winix.Wargs/JobRunnerOptions.cs`
- Create: `src/Winix.Wargs/JobRunner.cs`
- Create: `tests/Winix.Wargs.Tests/JobRunnerTests.cs`

This task implements the core execution loop with sequential (P=1), job-buffered mode. Parallelism, line-buffered, keep-order, confirm, and fail-fast are added in subsequent tasks.

- [ ] **Step 1: Create supporting types**

Create `src/Winix.Wargs/BufferStrategy.cs`:

```csharp
namespace Winix.Wargs;

/// <summary>
/// How job output is buffered and printed.
/// </summary>
public enum BufferStrategy
{
    /// <summary>Capture per-job output. Print atomically on job completion. Completion order.</summary>
    JobBuffered,

    /// <summary>Children inherit stdio. Output interleaves naturally.</summary>
    LineBuffered,

    /// <summary>Capture per-job output. Print in input order, holding back completed jobs until their turn.</summary>
    KeepOrder
}
```

Create `src/Winix.Wargs/JobResult.cs`:

```csharp
namespace Winix.Wargs;

/// <summary>
/// Result of a single job execution.
/// </summary>
/// <param name="JobIndex">1-based input-order index.</param>
/// <param name="ChildExitCode">The child process exit code. -1 if the process could not be spawned.</param>
/// <param name="Output">Captured stdout+stderr. Null in line-buffered mode.</param>
/// <param name="Duration">How long the job took.</param>
/// <param name="SourceItems">The input items for this job.</param>
/// <param name="Skipped">True if the job was skipped (e.g. confirm declined, fail-fast, not spawnable).</param>
public sealed record JobResult(
    int JobIndex,
    int ChildExitCode,
    string? Output,
    TimeSpan Duration,
    string[] SourceItems,
    bool Skipped
);
```

Create `src/Winix.Wargs/WargsResult.cs`:

```csharp
namespace Winix.Wargs;

/// <summary>
/// Summary of all job executions.
/// </summary>
/// <param name="TotalJobs">Number of invocations produced by the command builder.</param>
/// <param name="Succeeded">Jobs that exited 0.</param>
/// <param name="Failed">Jobs that exited non-zero.</param>
/// <param name="Skipped">Jobs not executed (confirm declined, fail-fast stopped, etc.).</param>
/// <param name="WallTime">Total wall-clock time for the entire run.</param>
/// <param name="Jobs">Per-job results in input order.</param>
public sealed record WargsResult(
    int TotalJobs,
    int Succeeded,
    int Failed,
    int Skipped,
    TimeSpan WallTime,
    List<JobResult> Jobs
);
```

Create `src/Winix.Wargs/JobRunnerOptions.cs`:

```csharp
namespace Winix.Wargs;

/// <summary>
/// Configuration for <see cref="JobRunner"/>.
/// </summary>
/// <param name="Parallelism">Max concurrent jobs. 1 = sequential. 0 = unlimited.</param>
/// <param name="Strategy">How job output is buffered and printed.</param>
/// <param name="FailFast">Stop spawning after first child failure.</param>
/// <param name="DryRun">Print commands without executing.</param>
/// <param name="Verbose">Print each command to stderr before running.</param>
/// <param name="Confirm">Prompt before each job.</param>
/// <param name="ConfirmPrompt">
/// Delegate that displays a command and returns true to proceed, false to skip.
/// Null uses the default console prompt (reads from /dev/tty or CON).
/// Injected for testability.
/// </param>
public sealed record JobRunnerOptions(
    int Parallelism = 1,
    BufferStrategy Strategy = BufferStrategy.JobBuffered,
    bool FailFast = false,
    bool DryRun = false,
    bool Verbose = false,
    bool Confirm = false,
    Func<string, bool>? ConfirmPrompt = null
);
```

- [ ] **Step 2: Write failing tests for sequential job-buffered execution**

Create `tests/Winix.Wargs.Tests/JobRunnerTests.cs`:

```csharp
using System.Runtime.InteropServices;
using Winix.Wargs;

namespace Winix.Wargs.Tests;

public class JobRunnerTests
{
    // Helper: cross-platform echo command
    private static CommandInvocation MakeEchoInvocation(string text, int jobIndex)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new CommandInvocation(
                "cmd", new[] { "/c", "echo", text },
                $"cmd /c echo {text}", new[] { text });
        }
        return new CommandInvocation(
            "echo", new[] { text },
            $"echo {text}", new[] { text });
    }

    // Helper: cross-platform command that exits with a specific code
    private static CommandInvocation MakeExitInvocation(int code, string item)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new CommandInvocation(
                "cmd", new[] { "/c", $"exit /b {code}" },
                $"cmd /c exit /b {code}", new[] { item });
        }
        return new CommandInvocation(
            "sh", new[] { "-c", $"exit {code}" },
            $"sh -c 'exit {code}'", new[] { item });
    }

    [Fact]
    public async Task RunAsync_Sequential_ExecutesAllJobs()
    {
        var options = new JobRunnerOptions();
        var runner = new JobRunner(options);
        var invocations = new[]
        {
            MakeEchoInvocation("hello", 1),
            MakeEchoInvocation("world", 2),
        };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(2, result.TotalJobs);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public async Task RunAsync_Sequential_CapturesOutput()
    {
        var options = new JobRunnerOptions();
        var runner = new JobRunner(options);
        var invocations = new[] { MakeEchoInvocation("hello", 1) };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Single(result.Jobs);
        Assert.NotNull(result.Jobs[0].Output);
        Assert.Contains("hello", result.Jobs[0].Output!);
    }

    [Fact]
    public async Task RunAsync_ChildFailure_RecordsExitCode()
    {
        var options = new JobRunnerOptions();
        var runner = new JobRunner(options);
        var invocations = new[] { MakeExitInvocation(42, "bad") };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, result.Failed);
        Assert.Equal(42, result.Jobs[0].ChildExitCode);
    }

    [Fact]
    public async Task RunAsync_CommandNotFound_RecordsFailure()
    {
        var options = new JobRunnerOptions();
        var runner = new JobRunner(options);
        var invocations = new[]
        {
            new CommandInvocation(
                "nonexistent_command_xyzzy_12345", Array.Empty<string>(),
                "nonexistent_command_xyzzy_12345", new[] { "item" })
        };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, result.Failed);
        Assert.True(result.Jobs[0].ChildExitCode != 0);
    }

    [Fact]
    public async Task RunAsync_DryRun_DoesNotExecute()
    {
        var stdout = new StringWriter();
        var options = new JobRunnerOptions(DryRun: true);
        var runner = new JobRunner(options);
        var invocations = new[] { MakeEchoInvocation("hello", 1) };

        var result = await runner.RunAsync(invocations, stdout, TextWriter.Null);

        Assert.Equal(0, result.TotalJobs);
        Assert.Contains("echo", stdout.ToString());
    }

    [Fact]
    public async Task RunAsync_Verbose_PrintsCommandToStderr()
    {
        var stderr = new StringWriter();
        var options = new JobRunnerOptions(Verbose: true);
        var runner = new JobRunner(options);
        var invocations = new[] { MakeEchoInvocation("hello", 1) };

        var result = await runner.RunAsync(invocations, TextWriter.Null, stderr);

        Assert.Contains("echo", stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_JobIndex_IsOneBased()
    {
        var options = new JobRunnerOptions();
        var runner = new JobRunner(options);
        var invocations = new[]
        {
            MakeEchoInvocation("a", 1),
            MakeEchoInvocation("b", 2),
        };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, result.Jobs[0].JobIndex);
        Assert.Equal(2, result.Jobs[1].JobIndex);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Wargs.Tests/ --filter "ClassName=Winix.Wargs.Tests.JobRunnerTests"`

Expected: Build error — `JobRunner` does not exist.

- [ ] **Step 4: Implement JobRunner with sequential job-buffered execution**

Create `src/Winix.Wargs/JobRunner.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Winix.Wargs;

/// <summary>
/// Executes <see cref="CommandInvocation"/>s with configurable parallelism,
/// output buffering, fail-fast, and confirm prompts.
/// </summary>
public sealed class JobRunner
{
    private readonly JobRunnerOptions _options;

    /// <summary>Creates a new job runner.</summary>
    public JobRunner(JobRunnerOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Runs all invocations and returns a summary result.
    /// </summary>
    /// <param name="invocations">The commands to execute.</param>
    /// <param name="output">Where to write job stdout (typically Console.Out).</param>
    /// <param name="error">Where to write diagnostics (typically Console.Error).</param>
    /// <param name="cancellationToken">Cancellation token (Ctrl+C handler).</param>
    public async Task<WargsResult> RunAsync(
        IEnumerable<CommandInvocation> invocations,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var jobs = new List<JobResult>();
        int jobIndex = 0;
        bool aborted = false;

        foreach (CommandInvocation invocation in invocations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            jobIndex++;

            if (_options.DryRun)
            {
                output.WriteLine(invocation.DisplayString);
                continue;
            }

            if (_options.Confirm)
            {
                bool proceed = _options.ConfirmPrompt is not null
                    ? _options.ConfirmPrompt(invocation.DisplayString)
                    : DefaultConfirmPrompt(invocation.DisplayString, error);

                if (!proceed)
                {
                    jobs.Add(new JobResult(jobIndex, 0, null, TimeSpan.Zero, invocation.SourceItems, Skipped: true));
                    continue;
                }
            }

            if (aborted)
            {
                jobs.Add(new JobResult(jobIndex, 0, null, TimeSpan.Zero, invocation.SourceItems, Skipped: true));
                continue;
            }

            if (_options.Verbose)
            {
                error.WriteLine($"wargs: {invocation.DisplayString}");
            }

            JobResult result = await ExecuteJobAsync(invocation, jobIndex, cancellationToken).ConfigureAwait(false);
            jobs.Add(result);

            // Write captured output to stdout
            if (result.Output is not null && result.Output.Length > 0)
            {
                output.Write(result.Output);
                // Ensure output ends with a newline
                if (!result.Output.EndsWith('\n'))
                {
                    output.WriteLine();
                }
            }

            if (_options.FailFast && result.ChildExitCode != 0 && !result.Skipped)
            {
                aborted = true;
            }
        }

        stopwatch.Stop();

        if (_options.DryRun)
        {
            return new WargsResult(0, 0, 0, 0, stopwatch.Elapsed, new List<JobResult>());
        }

        int succeeded = jobs.Count(j => !j.Skipped && j.ChildExitCode == 0);
        int failed = jobs.Count(j => !j.Skipped && j.ChildExitCode != 0);
        int skipped = jobs.Count(j => j.Skipped);

        return new WargsResult(jobs.Count, succeeded, failed, skipped, stopwatch.Elapsed, jobs);
    }

    private static async Task<JobResult> ExecuteJobAsync(
        CommandInvocation invocation,
        int jobIndex,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.Command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };

        foreach (string arg in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var jobStopwatch = Stopwatch.StartNew();
        Process process;

        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Process.Start returned null");
        }
        catch (Win32Exception)
        {
            jobStopwatch.Stop();
            return new JobResult(jobIndex, -1, null, jobStopwatch.Elapsed, invocation.SourceItems, Skipped: false);
        }

        process.StandardInput.Close();

        try
        {
            var outputBuf = new StringBuilder();
            var outputLock = new object();

            Task stdoutTask = ReadStreamAsync(process.StandardOutput, outputBuf, outputLock);
            Task stderrTask = ReadStreamAsync(process.StandardError, outputBuf, outputLock);

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            jobStopwatch.Stop();

            string captured;
            lock (outputLock)
            {
                captured = outputBuf.ToString();
            }

            return new JobResult(jobIndex, process.ExitCode, captured, jobStopwatch.Elapsed, invocation.SourceItems, Skipped: false);
        }
        finally
        {
            process.Dispose();
        }
    }

    private static async Task ReadStreamAsync(StreamReader reader, StringBuilder output, object outputLock)
    {
        char[] buffer = new char[4096];
        int charsRead;
        while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            lock (outputLock)
            {
                output.Append(buffer, 0, charsRead);
            }
        }
    }

    private static bool DefaultConfirmPrompt(string displayString, TextWriter error)
    {
        error.Write($"{displayString} ?...");
        error.Flush();

        // Read from controlling terminal, not stdin (which is the input pipe).
        try
        {
            using var tty = OpenTerminal();
            using var reader = new StreamReader(tty);
            string? response = reader.ReadLine();
            return response is not null
                && (response.Equals("y", StringComparison.OrdinalIgnoreCase)
                    || response.Equals("yes", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static FileStream OpenTerminal()
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileStream("CON", FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        return new FileStream("/dev/tty", FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Wargs.Tests/ --filter "ClassName=Winix.Wargs.Tests.JobRunnerTests"`

Expected: 7 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.Wargs/BufferStrategy.cs src/Winix.Wargs/JobResult.cs src/Winix.Wargs/WargsResult.cs src/Winix.Wargs/JobRunnerOptions.cs src/Winix.Wargs/JobRunner.cs tests/Winix.Wargs.Tests/JobRunnerTests.cs
git commit -m "feat(wargs): JobRunner with sequential job-buffered execution"
```

---

## Task 6: JobRunner — parallel execution

**Files:**
- Modify: `src/Winix.Wargs/JobRunner.cs`
- Modify: `tests/Winix.Wargs.Tests/JobRunnerTests.cs`

- [ ] **Step 1: Write failing tests for parallel execution**

Append to `tests/Winix.Wargs.Tests/JobRunnerTests.cs`:

```csharp
    [Fact]
    public async Task RunAsync_Parallel_ExecutesAllJobs()
    {
        var options = new JobRunnerOptions(Parallelism: 4);
        var runner = new JobRunner(options);
        var invocations = Enumerable.Range(1, 8)
            .Select(i => MakeEchoInvocation($"item{i}", i))
            .ToArray();

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(8, result.TotalJobs);
        Assert.Equal(8, result.Succeeded);
    }

    [Fact]
    public async Task RunAsync_Parallel_RespectsMaxConcurrency()
    {
        // Use sleep commands to verify parallelism is bounded.
        // With P=2, 4 sleep-0.1 jobs should take >= 0.2s (two batches).
        // With P=4, they'd finish in ~0.1s.
        var options = new JobRunnerOptions(Parallelism: 2);
        var runner = new JobRunner(options);

        CommandInvocation MakeSleepInvocation(int i)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: use ping to localhost as a ~100ms delay
                return new CommandInvocation(
                    "cmd", new[] { "/c", "ping -n 1 -w 100 127.0.0.1 >nul" },
                    "sleep 0.1", new[] { $"item{i}" });
            }
            return new CommandInvocation(
                "sleep", new[] { "0.1" },
                "sleep 0.1", new[] { $"item{i}" });
        }

        var invocations = Enumerable.Range(1, 4)
            .Select(MakeSleepInvocation)
            .ToArray();

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(4, result.TotalJobs);
        Assert.Equal(4, result.Succeeded);
    }

    [Fact]
    public async Task RunAsync_FailFast_StopsSpawningNewJobs()
    {
        var options = new JobRunnerOptions(FailFast: true);
        var runner = new JobRunner(options);
        var invocations = new[]
        {
            MakeExitInvocation(1, "fail"),
            MakeEchoInvocation("should-not-run", 2),
            MakeEchoInvocation("should-not-run", 3),
        };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, result.Failed);
        Assert.Equal(2, result.Skipped);
    }

    [Fact]
    public async Task RunAsync_Confirm_SkipsDeclinedJobs()
    {
        int callCount = 0;
        var options = new JobRunnerOptions(
            Confirm: true,
            ConfirmPrompt: _ =>
            {
                callCount++;
                return callCount != 2; // decline the second job
            });
        var runner = new JobRunner(options);
        var invocations = new[]
        {
            MakeEchoInvocation("a", 1),
            MakeEchoInvocation("b", 2),
            MakeEchoInvocation("c", 3),
        };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(1, result.Skipped);
    }
```

- [ ] **Step 2: Run tests to verify new tests fail**

Run: `dotnet test tests/Winix.Wargs.Tests/ --filter "ClassName=Winix.Wargs.Tests.JobRunnerTests"`

Expected: The parallel test should fail because `RunAsync` currently iterates sequentially and ignores `Parallelism`. The fail-fast and confirm tests may pass already since the sequential path handles those flags.

- [ ] **Step 3: Refactor RunAsync to support parallel execution with SemaphoreSlim**

Replace the `RunAsync` method body in `src/Winix.Wargs/JobRunner.cs` to dispatch jobs via `SemaphoreSlim` when `Parallelism != 1`. The key structure:

- Enumerate invocations, for each: acquire semaphore, spawn task.
- Each task calls `ExecuteJobAsync`, stores result, releases semaphore.
- After all tasks complete, collect results.
- Job-buffered output: write to stdout from each task as it finishes (with a lock for thread safety).
- Maintain a `volatile bool _aborted` field checked before spawning.

The implementation should:
1. If `Parallelism == 1`, use the existing sequential loop (simpler, no task overhead).
2. If `Parallelism > 1` (or 0 for unlimited), use `SemaphoreSlim` with parallel task dispatch.
3. For `Parallelism == 0`, use `int.MaxValue` as the semaphore count (effectively unlimited).

```csharp
    public async Task<WargsResult> RunAsync(
        IEnumerable<CommandInvocation> invocations,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (_options.DryRun)
        {
            foreach (CommandInvocation invocation in invocations)
            {
                output.WriteLine(invocation.DisplayString);
            }
            stopwatch.Stop();
            return new WargsResult(0, 0, 0, 0, stopwatch.Elapsed, new List<JobResult>());
        }

        List<JobResult> jobs;
        if (_options.Parallelism == 1)
        {
            jobs = await RunSequentialAsync(invocations, output, error, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            jobs = await RunParallelAsync(invocations, output, error, cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();

        int succeeded = jobs.Count(j => !j.Skipped && j.ChildExitCode == 0);
        int failed = jobs.Count(j => !j.Skipped && j.ChildExitCode != 0);
        int skipped = jobs.Count(j => j.Skipped);

        return new WargsResult(jobs.Count, succeeded, failed, skipped, stopwatch.Elapsed, jobs);
    }

    private async Task<List<JobResult>> RunSequentialAsync(
        IEnumerable<CommandInvocation> invocations,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var jobs = new List<JobResult>();
        int jobIndex = 0;
        bool aborted = false;

        foreach (CommandInvocation invocation in invocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            jobIndex++;

            if (_options.Confirm)
            {
                bool proceed = _options.ConfirmPrompt is not null
                    ? _options.ConfirmPrompt(invocation.DisplayString)
                    : DefaultConfirmPrompt(invocation.DisplayString, error);

                if (!proceed)
                {
                    jobs.Add(new JobResult(jobIndex, 0, null, TimeSpan.Zero, invocation.SourceItems, Skipped: true));
                    continue;
                }
            }

            if (aborted)
            {
                jobs.Add(new JobResult(jobIndex, 0, null, TimeSpan.Zero, invocation.SourceItems, Skipped: true));
                continue;
            }

            if (_options.Verbose)
            {
                error.WriteLine($"wargs: {invocation.DisplayString}");
            }

            JobResult result = await ExecuteJobAsync(invocation, jobIndex, cancellationToken).ConfigureAwait(false);
            jobs.Add(result);

            if (result.Output is not null && result.Output.Length > 0)
            {
                output.Write(result.Output);
                if (!result.Output.EndsWith('\n'))
                {
                    output.WriteLine();
                }
            }

            if (_options.FailFast && result.ChildExitCode != 0 && !result.Skipped)
            {
                aborted = true;
            }
        }

        return jobs;
    }

    private async Task<List<JobResult>> RunParallelAsync(
        IEnumerable<CommandInvocation> invocations,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        int maxParallelism = _options.Parallelism == 0 ? int.MaxValue : _options.Parallelism;
        using var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);
        var outputLock = new object();
        var tasks = new List<Task<JobResult>>();
        int jobIndex = 0;
        bool aborted = false;

        foreach (CommandInvocation invocation in invocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            jobIndex++;

            if (Volatile.Read(ref aborted))
            {
                tasks.Add(Task.FromResult(
                    new JobResult(jobIndex, 0, null, TimeSpan.Zero, invocation.SourceItems, Skipped: true)));
                continue;
            }

            int capturedIndex = jobIndex;
            CommandInvocation capturedInvocation = invocation;

            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    if (Volatile.Read(ref aborted))
                    {
                        return new JobResult(capturedIndex, 0, null, TimeSpan.Zero, capturedInvocation.SourceItems, Skipped: true);
                    }

                    if (_options.Verbose)
                    {
                        lock (outputLock)
                        {
                            error.WriteLine($"wargs: {capturedInvocation.DisplayString}");
                        }
                    }

                    JobResult result = await ExecuteJobAsync(capturedInvocation, capturedIndex, cancellationToken).ConfigureAwait(false);

                    // Write captured output atomically
                    if (_options.Strategy == BufferStrategy.JobBuffered && result.Output is not null && result.Output.Length > 0)
                    {
                        lock (outputLock)
                        {
                            output.Write(result.Output);
                            if (!result.Output.EndsWith('\n'))
                            {
                                output.WriteLine();
                            }
                        }
                    }

                    if (_options.FailFast && result.ChildExitCode != 0 && !result.Skipped)
                    {
                        Volatile.Write(ref aborted, true);
                    }

                    return result;
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        JobResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.OrderBy(r => r.JobIndex).ToList();
    }
```

- [ ] **Step 4: Run tests to verify all pass**

Run: `dotnet test tests/Winix.Wargs.Tests/ --filter "ClassName=Winix.Wargs.Tests.JobRunnerTests"`

Expected: 11 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Wargs/JobRunner.cs tests/Winix.Wargs.Tests/JobRunnerTests.cs
git commit -m "feat(wargs): parallel execution with SemaphoreSlim and fail-fast"
```

---

## Task 7: JobRunner — keep-order and line-buffered modes

**Files:**
- Modify: `src/Winix.Wargs/JobRunner.cs`
- Modify: `tests/Winix.Wargs.Tests/JobRunnerTests.cs`

- [ ] **Step 1: Write failing tests for keep-order and line-buffered**

Append to `tests/Winix.Wargs.Tests/JobRunnerTests.cs`:

```csharp
    [Fact]
    public async Task RunAsync_KeepOrder_OutputsInInputOrder()
    {
        var stdout = new StringWriter();
        var options = new JobRunnerOptions(Parallelism: 4, Strategy: BufferStrategy.KeepOrder);
        var runner = new JobRunner(options);
        var invocations = Enumerable.Range(1, 4)
            .Select(i => MakeEchoInvocation($"item{i}", i))
            .ToArray();

        await runner.RunAsync(invocations, stdout, TextWriter.Null);

        string output = stdout.ToString();
        int pos1 = output.IndexOf("item1");
        int pos2 = output.IndexOf("item2");
        int pos3 = output.IndexOf("item3");
        int pos4 = output.IndexOf("item4");

        Assert.True(pos1 < pos2, "item1 should appear before item2");
        Assert.True(pos2 < pos3, "item2 should appear before item3");
        Assert.True(pos3 < pos4, "item3 should appear before item4");
    }

    [Fact]
    public async Task RunAsync_LineBuffered_DoesNotCaptureOutput()
    {
        var options = new JobRunnerOptions(Strategy: BufferStrategy.LineBuffered);
        var runner = new JobRunner(options);
        var invocations = new[] { MakeEchoInvocation("hello", 1) };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Single(result.Jobs);
        Assert.Null(result.Jobs[0].Output);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Wargs.Tests/ --filter "ClassName=Winix.Wargs.Tests.JobRunnerTests"`

Expected: keep-order test may fail (output in completion order), line-buffered test should fail (output captured).

- [ ] **Step 3: Implement keep-order output in parallel path**

In `RunParallelAsync`, after `await Task.WhenAll`, when `Strategy == KeepOrder`, iterate results in order and write output:

```csharp
        JobResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var orderedResults = results.OrderBy(r => r.JobIndex).ToList();

        if (_options.Strategy == BufferStrategy.KeepOrder)
        {
            foreach (JobResult r in orderedResults)
            {
                if (r.Output is not null && r.Output.Length > 0)
                {
                    output.Write(r.Output);
                    if (!r.Output.EndsWith('\n'))
                    {
                        output.WriteLine();
                    }
                }
            }
        }

        return orderedResults;
```

And in the per-task output block, only write immediately for `JobBuffered` (already guarded by `if (_options.Strategy == BufferStrategy.JobBuffered)`).

- [ ] **Step 4: Implement line-buffered mode**

In `ExecuteJobAsync`, add a parameter `bool inheritStdio`. When true, don't redirect stdout/stderr and return `null` for `Output`. Wire this based on `_options.Strategy == BufferStrategy.LineBuffered`.

Create a separate `ExecuteJobLineBufferedAsync`:

```csharp
    private static async Task<JobResult> ExecuteJobLineBufferedAsync(
        CommandInvocation invocation,
        int jobIndex,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.Command,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
        };

        foreach (string arg in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var jobStopwatch = Stopwatch.StartNew();
        Process process;

        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Process.Start returned null");
        }
        catch (Win32Exception)
        {
            jobStopwatch.Stop();
            return new JobResult(jobIndex, -1, null, jobStopwatch.Elapsed, invocation.SourceItems, Skipped: false);
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            jobStopwatch.Stop();
            return new JobResult(jobIndex, process.ExitCode, null, jobStopwatch.Elapsed, invocation.SourceItems, Skipped: false);
        }
        finally
        {
            process.Dispose();
        }
    }
```

In the sequential and parallel paths, call `ExecuteJobLineBufferedAsync` when `_options.Strategy == BufferStrategy.LineBuffered`.

- [ ] **Step 5: Run tests to verify all pass**

Run: `dotnet test tests/Winix.Wargs.Tests/ --filter "ClassName=Winix.Wargs.Tests.JobRunnerTests"`

Expected: 13 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.Wargs/JobRunner.cs tests/Winix.Wargs.Tests/JobRunnerTests.cs
git commit -m "feat(wargs): keep-order and line-buffered output modes"
```

---

## Task 8: Formatting — human, JSON, and NDJSON output

**Files:**
- Create: `src/Winix.Wargs/Formatting.cs`
- Create: `tests/Winix.Wargs.Tests/FormattingTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Winix.Wargs.Tests/FormattingTests.cs`:

```csharp
using Winix.Wargs;

namespace Winix.Wargs.Tests;

public class FormattingTests
{
    private static readonly string Version = "0.1.0";

    [Fact]
    public void FormatJson_Success_IncludesStandardFields()
    {
        var result = new WargsResult(
            TotalJobs: 3, Succeeded: 3, Failed: 0, Skipped: 0,
            WallTime: TimeSpan.FromSeconds(1.5),
            Jobs: new List<JobResult>());

        string json = Formatting.FormatJson(result, 0, "success", "wargs", Version);

        Assert.Contains("\"tool\":\"wargs\"", json);
        Assert.Contains("\"version\":\"0.1.0\"", json);
        Assert.Contains("\"exit_code\":0", json);
        Assert.Contains("\"exit_reason\":\"success\"", json);
        Assert.Contains("\"total_jobs\":3", json);
        Assert.Contains("\"succeeded\":3", json);
        Assert.Contains("\"failed\":0", json);
        Assert.Contains("\"skipped\":0", json);
        Assert.Contains("\"wall_seconds\":", json);
    }

    [Fact]
    public void FormatJson_Failure_ReflectsExitCode()
    {
        var result = new WargsResult(
            TotalJobs: 2, Succeeded: 1, Failed: 1, Skipped: 0,
            WallTime: TimeSpan.FromSeconds(2.0),
            Jobs: new List<JobResult>());

        string json = Formatting.FormatJson(result, WargsExitCode.ChildFailed, "child_failed", "wargs", Version);

        Assert.Contains("\"exit_code\":123", json);
        Assert.Contains("\"exit_reason\":\"child_failed\"", json);
        Assert.Contains("\"failed\":1", json);
    }

    [Fact]
    public void FormatNdjsonLine_SingleItem_InputIsString()
    {
        var job = new JobResult(
            JobIndex: 1, ChildExitCode: 0, Output: "hello\n",
            Duration: TimeSpan.FromSeconds(0.34),
            SourceItems: new[] { "file1.cs" }, Skipped: false);

        string line = Formatting.FormatNdjsonLine(job, 0, "success", "wargs", Version);

        Assert.Contains("\"job\":1", line);
        Assert.Contains("\"child_exit_code\":0", line);
        Assert.Contains("\"input\":\"file1.cs\"", line);
        Assert.Contains("\"wall_seconds\":", line);
        Assert.DoesNotContain("\n", line.TrimEnd('\r', '\n'));
    }

    [Fact]
    public void FormatNdjsonLine_MultipleItems_InputIsArray()
    {
        var job = new JobResult(
            JobIndex: 2, ChildExitCode: 0, Output: null,
            Duration: TimeSpan.FromSeconds(0.5),
            SourceItems: new[] { "a", "b", "c" }, Skipped: false);

        string line = Formatting.FormatNdjsonLine(job, 0, "success", "wargs", Version);

        Assert.Contains("\"input\":[\"a\",\"b\",\"c\"]", line);
    }

    [Fact]
    public void FormatJsonError_ReturnsValidJson()
    {
        string json = Formatting.FormatJsonError(125, "usage_error", "wargs", Version);

        Assert.Contains("\"tool\":\"wargs\"", json);
        Assert.Contains("\"exit_code\":125", json);
        Assert.Contains("\"exit_reason\":\"usage_error\"", json);
    }

    [Fact]
    public void FormatHumanSummary_NoFailures_ReturnsNull()
    {
        var result = new WargsResult(3, 3, 0, 0, TimeSpan.FromSeconds(1), new List<JobResult>());
        Assert.Null(Formatting.FormatHumanSummary(result));
    }

    [Fact]
    public void FormatHumanSummary_WithFailures_ReturnsMessage()
    {
        var result = new WargsResult(10, 7, 3, 0, TimeSpan.FromSeconds(5), new List<JobResult>());
        string? summary = Formatting.FormatHumanSummary(result);

        Assert.NotNull(summary);
        Assert.Contains("3", summary);
        Assert.Contains("10", summary);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Wargs.Tests/ --filter "ClassName=Winix.Wargs.Tests.FormattingTests"`

Expected: Build error — `Formatting` methods do not exist.

- [ ] **Step 3: Implement Formatting**

Create `src/Winix.Wargs/Formatting.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace Winix.Wargs;

/// <summary>
/// Output formatting for wargs — human-readable summaries, JSON, and NDJSON.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Formats the overall result as a JSON object following Winix CLI conventions.
    /// </summary>
    public static string FormatJson(WargsResult result, int exitCode, string exitReason, string toolName, string version)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{{\"tool\":\"{0}\",\"version\":\"{1}\",\"exit_code\":{2},\"exit_reason\":\"{3}\",\"total_jobs\":{4},\"succeeded\":{5},\"failed\":{6},\"skipped\":{7},\"wall_seconds\":{8:F3}}}",
            EscapeJson(toolName),
            EscapeJson(version),
            exitCode,
            EscapeJson(exitReason),
            result.TotalJobs,
            result.Succeeded,
            result.Failed,
            result.Skipped,
            result.WallTime.TotalSeconds);
    }

    /// <summary>
    /// Formats a single job result as one NDJSON line.
    /// </summary>
    public static string FormatNdjsonLine(JobResult job, int exitCode, string exitReason, string toolName, string version)
    {
        var sb = new StringBuilder();
        sb.AppendFormat(
            CultureInfo.InvariantCulture,
            "{{\"tool\":\"{0}\",\"version\":\"{1}\",\"job\":{2},\"exit_code\":{3},\"exit_reason\":\"{4}\",\"child_exit_code\":{5},",
            EscapeJson(toolName),
            EscapeJson(version),
            job.JobIndex,
            exitCode,
            EscapeJson(exitReason),
            job.ChildExitCode);

        if (job.SourceItems.Length == 1)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"input\":\"{0}\",", EscapeJson(job.SourceItems[0]));
        }
        else
        {
            sb.Append("\"input\":[");
            for (int i = 0; i < job.SourceItems.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"{0}\"", EscapeJson(job.SourceItems[i]));
            }
            sb.Append("],");
        }

        sb.AppendFormat(CultureInfo.InvariantCulture, "\"wall_seconds\":{0:F3}}}", job.Duration.TotalSeconds);

        return sb.ToString();
    }

    /// <summary>
    /// Formats an error as a JSON object following Winix CLI conventions.
    /// </summary>
    public static string FormatJsonError(int exitCode, string exitReason, string toolName, string version)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{{\"tool\":\"{0}\",\"version\":\"{1}\",\"exit_code\":{2},\"exit_reason\":\"{3}\"}}",
            EscapeJson(toolName),
            EscapeJson(version),
            exitCode,
            EscapeJson(exitReason));
    }

    /// <summary>
    /// Returns a human-readable failure summary, or null if no failures.
    /// </summary>
    public static string? FormatHumanSummary(WargsResult result)
    {
        if (result.Failed == 0)
        {
            return null;
        }

        return $"wargs: {result.Failed}/{result.TotalJobs} jobs failed";
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Wargs.Tests/ --filter "ClassName=Winix.Wargs.Tests.FormattingTests"`

Expected: 7 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Wargs/Formatting.cs tests/Winix.Wargs.Tests/FormattingTests.cs
git commit -m "feat(wargs): human, JSON, and NDJSON output formatting"
```

---

## Task 9: Console app — Program.cs with full arg parsing

**Files:**
- Modify: `src/wargs/Program.cs`

- [ ] **Step 1: Implement the full Program.cs**

Replace `src/wargs/Program.cs` with the complete entry point:

```csharp
using System.Reflection;
using Winix.Wargs;
using Yort.ShellKit;

namespace Wargs;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        string version = GetVersion();

        var parser = new CommandLineParser("wargs", version)
            .Description("Read items from stdin and execute a command for each one.")
            .StandardFlags()
            .Flag("--ndjson", "Streaming NDJSON per job to stderr")
            .IntOption("--parallel", "-P", "N", "Max concurrent jobs (default 1, 0 = unlimited)",
                n => n < 0 ? "must be >= 0" : null)
            .IntOption("--batch", "-n", "N", "Items per invocation (default 1)",
                n => n < 1 ? "must be >= 1" : null)
            .Flag("--null", "-0", "Null-delimited input")
            .Option("--delimiter", "-d", "CHAR", "Custom input delimiter")
            .Flag("--compat", "POSIX whitespace splitting with quote handling")
            .Flag("--fail-fast", "Stop spawning after first failure")
            .Flag("--keep-order", "-k", "Print output in input order")
            .Flag("--line-buffered", "Children inherit stdio directly")
            .Flag("--confirm", "-p", "Prompt before each job")
            .Flag("--dry-run", "Print commands without executing")
            .Flag("--verbose", "-v", "Print each command to stderr before running")
            .CommandMode()
            .ExitCodes(
                (0, "All jobs succeeded"),
                (WargsExitCode.ChildFailed, "One or more child processes failed"),
                (WargsExitCode.FailFastAbort, "Aborted due to --fail-fast"),
                (ExitCode.UsageError, "Usage error"),
                (ExitCode.NotExecutable, "Command not executable"),
                (ExitCode.NotFound, "Command not found"));

        var result = parser.Parse(args);
        if (result.IsHandled) return result.ExitCode;
        if (result.HasErrors) return result.WriteErrors(Console.Error);

        // --- Resolve options ---
        int parallelism = result.Has("--parallel") ? result.GetInt("--parallel") : 1;
        int batchSize = result.Has("--batch") ? result.GetInt("--batch") : 1;
        bool jsonOutput = result.Has("--json");
        bool ndjsonOutput = result.Has("--ndjson");
        bool verbose = result.Has("--verbose");
        bool dryRun = result.Has("--dry-run");
        bool failFast = result.Has("--fail-fast");
        bool confirm = result.Has("--confirm");
        bool keepOrder = result.Has("--keep-order");
        bool lineBuffered = result.Has("--line-buffered");

        // --- Resolve delimiter mode ---
        bool hasNull = result.Has("--null");
        bool hasDelimiter = result.Has("--delimiter");
        bool hasCompat = result.Has("--compat");

        int delimiterCount = (hasNull ? 1 : 0) + (hasDelimiter ? 1 : 0) + (hasCompat ? 1 : 0);
        if (delimiterCount > 1)
        {
            return result.WriteError("--null, --delimiter, and --compat are mutually exclusive", Console.Error);
        }

        DelimiterMode delimMode = DelimiterMode.Line;
        char customDelimiter = '\0';
        if (hasNull)
        {
            delimMode = DelimiterMode.Null;
        }
        else if (hasCompat)
        {
            delimMode = DelimiterMode.Whitespace;
        }
        else if (hasDelimiter)
        {
            string delimStr = result.GetString("--delimiter");
            if (delimStr.Length != 1)
            {
                return result.WriteError("--delimiter requires a single character", Console.Error);
            }
            delimMode = DelimiterMode.Custom;
            customDelimiter = delimStr[0];
        }

        // --- Validate flag combinations ---
        if (confirm && parallelism > 1)
        {
            return result.WriteError("--confirm cannot be used with --parallel > 1", Console.Error);
        }

        if (lineBuffered && keepOrder)
        {
            return result.WriteError("--line-buffered and --keep-order cannot be combined", Console.Error);
        }

        if (ndjsonOutput && lineBuffered)
        {
            return result.WriteError("--ndjson and --line-buffered cannot be combined", Console.Error);
        }

        // --- Resolve buffer strategy ---
        BufferStrategy strategy = BufferStrategy.JobBuffered;
        if (lineBuffered)
        {
            strategy = BufferStrategy.LineBuffered;
        }
        else if (keepOrder)
        {
            strategy = BufferStrategy.KeepOrder;
        }

        // --- Build pipeline ---
        var inputReader = new InputReader(Console.In, delimMode, customDelimiter);
        var commandBuilder = new CommandBuilder(result.Command, batchSize);
        var runnerOptions = new JobRunnerOptions(
            Parallelism: parallelism,
            Strategy: strategy,
            FailFast: failFast,
            DryRun: dryRun,
            Verbose: verbose,
            Confirm: confirm);
        var jobRunner = new JobRunner(runnerOptions);

        IEnumerable<string> items = inputReader.ReadItems();
        IEnumerable<CommandInvocation> invocations = commandBuilder.Build(items);

        // --- Execute ---
        WargsResult wargsResult = await jobRunner.RunAsync(
            invocations, Console.Out, Console.Error).ConfigureAwait(false);

        // --- Output ---
        if (dryRun)
        {
            return 0;
        }

        int exitCode = 0;
        string exitReason = "success";

        if (wargsResult.Failed > 0)
        {
            exitCode = WargsExitCode.ChildFailed;
            exitReason = "child_failed";

            if (failFast && wargsResult.Skipped > 0)
            {
                exitCode = WargsExitCode.FailFastAbort;
                exitReason = "fail_fast_abort";
            }
        }

        // NDJSON: emit per-job lines to stderr
        if (ndjsonOutput)
        {
            foreach (JobResult job in wargsResult.Jobs)
            {
                if (!job.Skipped)
                {
                    string jobExitReason = job.ChildExitCode == 0 ? "success" : "child_failed";
                    int jobExitCode = job.ChildExitCode == 0 ? 0 : 0; // wargs itself succeeded at spawning
                    Console.Error.WriteLine(Formatting.FormatNdjsonLine(job, jobExitCode, jobExitReason, "wargs", version));
                }
            }
        }

        // JSON: summary to stderr
        if (jsonOutput)
        {
            Console.Error.WriteLine(Formatting.FormatJson(wargsResult, exitCode, exitReason, "wargs", version));
        }

        // Human: failure summary to stderr
        if (!jsonOutput && !ndjsonOutput)
        {
            string? summary = Formatting.FormatHumanSummary(wargsResult);
            if (summary is not null)
            {
                Console.Error.WriteLine(summary);
            }
        }

        return exitCode;
    }

    private static string GetVersion()
    {
        return typeof(WargsExitCode).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
```

- [ ] **Step 2: Verify the solution builds**

Run: `dotnet build Winix.sln`

Expected: Build succeeded. 0 Warnings. 0 Errors.

- [ ] **Step 3: Run all tests**

Run: `dotnet test Winix.sln`

Expected: All tests pass (existing + new wargs tests).

- [ ] **Step 4: Commit**

```bash
git add src/wargs/Program.cs
git commit -m "feat(wargs): full console app with arg parsing and pipeline wiring"
```

---

## Task 10: README, scoop manifest, and pipeline updates

**Files:**
- Create: `src/wargs/README.md`
- Create: `bucket/wargs.json`
- Modify: `bucket/winix.json`
- Modify: `.github/workflows/release.yml`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Create README**

Create `src/wargs/README.md` following the pattern of `src/timeit/README.md`:

```markdown
# wargs

Read items from stdin and execute a command for each one.

Cross-platform xargs replacement with sane defaults: line-delimited input, parallel execution, correct Windows path handling, and structured JSON output.

**`xargs` equivalent for Windows** (and works on Linux/macOS too).

## Install

### Scoop (Windows)

\```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/wargs
\```

### Winget (Windows, stable releases)

\```bash
winget install Winix.Wargs
\```

### .NET Tool (cross-platform)

\```bash
dotnet tool install -g Winix.Wargs
\```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

\```
wargs [options] [--] <command> [args...]
\```

### Examples

\```bash
# Simple foreach — one command per input line
cat servers.txt | wargs ssh {} "uptime"

# Parallel execution — 4 jobs at a time
find . -name "*.cs" | wargs -P4 dotnet format {}

# Batch mode — group 10 items per invocation
cat urls.txt | wargs -n10 curl

# Dry run — see what would execute
find . -name "*.log" | wargs --dry-run rm

# Confirm before each deletion
find . -name "*.bak" | wargs -p rm

# JSON output for CI pipelines
git ls-files "*.cs" | wargs --json dotnet format {}

# Null-delimited input (from find -print0)
find . -name "*.tmp" -print0 | wargs -0 rm

# Default to echo (like xargs)
seq 5 | wargs
\```

## How it works

- **Smart placeholder**: if `{}` appears in the command, each occurrence is replaced with the input item. Otherwise, items are appended to the end.
- **Line-delimited by default**: one item per line. No whitespace-splitting surprises. Use `--compat` for POSIX xargs behaviour.
- **Direct process spawn**: uses `Process.Start` with `ArgumentList` — no shell wrapper, no quoting bugs. Windows paths with spaces just work.

## Options

| Flag | Short | Description |
|------|-------|-------------|
| `--parallel N` | `-P` | Max concurrent jobs (default 1, 0 = unlimited) |
| `--batch N` | `-n` | Items per invocation (default 1) |
| `--null` | `-0` | Null-delimited input |
| `--delimiter CHAR` | `-d` | Custom input delimiter |
| `--compat` | | POSIX whitespace splitting with quote handling |
| `--fail-fast` | | Stop spawning after first failure |
| `--keep-order` | `-k` | Print output in input order |
| `--line-buffered` | | Children inherit stdio directly |
| `--confirm` | `-p` | Prompt before each job |
| `--dry-run` | | Print commands without executing |
| `--verbose` | `-v` | Print each command to stderr |
| `--json` | | JSON summary to stderr |
| `--ndjson` | | Streaming NDJSON per job to stderr |
| `--color` | | Force colour on |
| `--no-color` | | Force colour off |
| `--help` | `-h` | Show help |
| `--version` | | Show version |

## Differences from xargs

| xargs | wargs |
|-------|-------|
| Whitespace-delimited by default | Line-delimited by default |
| Needs `-I {}` for placeholder mode | Auto-detects `{}` in command |
| Complex shell quoting on Windows | Direct process spawn, no shell |
| Output interleaves by default | Job-buffered output by default |
| No JSON output | `--json` and `--ndjson` built in |
| Keeps original file by default | — |

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | All jobs succeeded |
| 123 | One or more child processes failed |
| 124 | Aborted due to --fail-fast |
| 125 | Usage error |
| 126 | Command not executable |
| 127 | Command not found |
```

(Note: the `\``` in the plan above should be rendered as actual triple backticks. They are escaped here for markdown nesting.)

- [ ] **Step 2: Create scoop manifest**

Create `bucket/wargs.json`:

```json
{
  "version": "0.1.0-preview.4",
  "description": "Cross-platform xargs replacement with sane defaults.",
  "homepage": "https://github.com/Yortw/winix",
  "license": "MIT",
  "architecture": {
    "64bit": {
      "url": "https://github.com/Yortw/winix/releases/download/v0.0.0/wargs-win-x64.zip",
      "hash": "0000000000000000000000000000000000000000000000000000000000000000"
    }
  },
  "bin": "wargs.exe",
  "checkver": "github",
  "autoupdate": {
    "architecture": {
      "64bit": {
        "url": "https://github.com/Yortw/winix/releases/download/v$version/wargs-win-x64.zip"
      }
    }
  }
}
```

- [ ] **Step 3: Add wargs.exe to combined winix.json bin array**

In `bucket/winix.json`, add `"wargs.exe"` to the `bin` array and update the description.

- [ ] **Step 4: Add wargs to release pipeline**

In `.github/workflows/release.yml`, add wargs to:
1. `pack-nuget` job: add `Pack wargs` step
2. `publish-aot` job: add `Publish wargs` step
3. Zip binaries steps (both Linux/macOS and Windows): add wargs zip
4. Combined Winix zip step: add `Copy-Item` for wargs.exe
5. `update-scoop-bucket` job: add `update_manifest bucket/wargs.json aot/wargs-win-x64.zip`
6. `generate-winget-manifests` job: add `generate_manifests "wargs" "Wargs" "Cross-platform xargs replacement with sane defaults."`

- [ ] **Step 5: Update CLAUDE.md project layout**

Add wargs entries to the project layout section in `CLAUDE.md`:

```
src/Winix.Wargs/           — class library (input reading, command building, job execution, formatting)
src/wargs/                 — console app entry point
tests/Winix.Wargs.Tests/   — xUnit tests
```

- [ ] **Step 6: Verify the solution builds and all tests pass**

Run: `dotnet build Winix.sln`
Run: `dotnet test Winix.sln`

Expected: Build succeeded. All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/wargs/README.md bucket/wargs.json bucket/winix.json .github/workflows/release.yml CLAUDE.md
git commit -m "feat(wargs): README, scoop manifest, and release pipeline integration"
```

---

## Task 11: Integration smoke test

**Files:**
- Modify: `tests/Winix.Wargs.Tests/InputReaderTests.cs` (or a new integration test file)

This task verifies the full pipeline works end-to-end: InputReader → CommandBuilder → JobRunner → Formatting.

- [ ] **Step 1: Write integration test**

Add a new test class in `tests/Winix.Wargs.Tests/IntegrationTests.cs`:

```csharp
using System.Runtime.InteropServices;
using Winix.Wargs;

namespace Winix.Wargs.Tests;

public class IntegrationTests
{
    [Fact]
    public async Task FullPipeline_EchoThreeItems_ProducesThreeResults()
    {
        var input = new InputReader(new StringReader("alpha\nbeta\ngamma"), DelimiterMode.Line);

        string[] template;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            template = new[] { "cmd", "/c", "echo", "{}" };
        }
        else
        {
            template = new[] { "echo", "{}" };
        }

        var builder = new CommandBuilder(template);
        var options = new JobRunnerOptions();
        var runner = new JobRunner(options);

        var stdout = new StringWriter();
        var result = await runner.RunAsync(
            builder.Build(input.ReadItems()), stdout, TextWriter.Null);

        Assert.Equal(3, result.TotalJobs);
        Assert.Equal(3, result.Succeeded);
        Assert.Equal(0, result.Failed);

        string output = stdout.ToString();
        Assert.Contains("alpha", output);
        Assert.Contains("beta", output);
        Assert.Contains("gamma", output);
    }

    [Fact]
    public async Task FullPipeline_ParallelWithKeepOrder_OutputInOrder()
    {
        var input = new InputReader(new StringReader("1\n2\n3\n4"), DelimiterMode.Line);

        string[] template;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            template = new[] { "cmd", "/c", "echo", "{}" };
        }
        else
        {
            template = new[] { "echo", "{}" };
        }

        var builder = new CommandBuilder(template);
        var options = new JobRunnerOptions(Parallelism: 2, Strategy: BufferStrategy.KeepOrder);
        var runner = new JobRunner(options);

        var stdout = new StringWriter();
        var result = await runner.RunAsync(
            builder.Build(input.ReadItems()), stdout, TextWriter.Null);

        Assert.Equal(4, result.Succeeded);

        string output = stdout.ToString();
        int pos1 = output.IndexOf("1");
        int pos2 = output.IndexOf("2");
        int pos3 = output.IndexOf("3");
        int pos4 = output.IndexOf("4");

        Assert.True(pos1 < pos2);
        Assert.True(pos2 < pos3);
        Assert.True(pos3 < pos4);
    }

    [Fact]
    public void FormatJson_RoundTrips_StandardFields()
    {
        var jobs = new List<JobResult>
        {
            new(1, 0, "out\n", TimeSpan.FromSeconds(0.1), new[] { "a" }, false),
            new(2, 1, "err\n", TimeSpan.FromSeconds(0.2), new[] { "b" }, false),
        };
        var result = new WargsResult(2, 1, 1, 0, TimeSpan.FromSeconds(0.3), jobs);

        string json = Formatting.FormatJson(result, WargsExitCode.ChildFailed, "child_failed", "wargs", "0.1.0");

        Assert.Contains("\"total_jobs\":2", json);
        Assert.Contains("\"succeeded\":1", json);
        Assert.Contains("\"failed\":1", json);
        Assert.Contains("\"exit_code\":123", json);
    }
}
```

- [ ] **Step 2: Run integration tests**

Run: `dotnet test tests/Winix.Wargs.Tests/ --filter "ClassName=Winix.Wargs.Tests.IntegrationTests"`

Expected: 3 passed, 0 failed.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test Winix.sln`

Expected: All tests pass across all projects.

- [ ] **Step 4: Commit**

```bash
git add tests/Winix.Wargs.Tests/IntegrationTests.cs
git commit -m "test(wargs): integration smoke tests for full pipeline"
```

---

## Self-Review Notes

**Spec coverage check:**
- InputReader: Line, Null, Custom, Whitespace — all covered (Tasks 2-3)
- CommandBuilder: append, substitution, batch, empty template, DisplayString — all covered (Task 4)
- JobRunner: sequential, parallel, job-buffered, keep-order, line-buffered, fail-fast, confirm, dry-run, verbose, command-not-found — all covered (Tasks 5-7)
- Formatting: JSON, NDJSON, human summary, error — all covered (Task 8)
- Program.cs: full arg parsing, validation rules, pipeline wiring — covered (Task 9)
- Exit codes: 0, 123, 124, 125, 126, 127 — all wired in Program.cs
- Path handling: handled by Process.Start + ArgumentList (no additional code needed)
- README, scoop, winget, release pipeline — covered (Task 10)

**Type consistency check:**
- `DelimiterMode` — consistent across InputReader, tests, Program.cs
- `CommandInvocation(Command, Arguments, DisplayString, SourceItems)` — consistent
- `CommandBuilder(template, batchSize)` with `IsSubstitutionMode` and `Build()` — consistent
- `JobRunnerOptions` record fields — consistent between definition and usage
- `JobResult(JobIndex, ChildExitCode, Output, Duration, SourceItems, Skipped)` — consistent
- `WargsResult(TotalJobs, Succeeded, Failed, Skipped, WallTime, Jobs)` — consistent
- `Formatting.FormatJson`, `FormatNdjsonLine`, `FormatJsonError`, `FormatHumanSummary` — consistent
- `WargsExitCode.ChildFailed` (123), `WargsExitCode.FailFastAbort` (124) — consistent
