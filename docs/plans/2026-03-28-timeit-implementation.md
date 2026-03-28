# timeit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `timeit`, the first tool in the Winix CLI suite — a command timer that shows wall clock, CPU time, peak memory, and exit code.

**Architecture:** Class library (`Winix.TimeIt`) containing all logic, thin console app (`timeit`) for the entry point. TDD throughout. The class library is testable without spawning processes for formatting logic; integration tests spawn real child processes for execution logic.

**Tech Stack:** .NET 10 (LTS) / C# / xUnit. AOT-ready. No external dependencies.

**Spec:** `docs/plans/2026-03-28-timeit-design.md`

---

## File Map

| File | Responsibility |
|------|---------------|
| `Directory.Build.props` | Shared build settings: version, nullable, warnings-as-errors, trim analyzers |
| `Winix.sln` | Solution file for all projects |
| `src/Winix.TimeIt/Winix.TimeIt.csproj` | Class library project (net10.0, trimmable) |
| `src/Winix.TimeIt/TimeItResult.cs` | Immutable record holding wall clock, CPU time, peak memory, exit code |
| `src/Winix.TimeIt/Formatting.cs` | Static methods: `FormatDuration`, `FormatBytes`, `FormatDefault`, `FormatOneLine`, `FormatJson` |
| `src/Winix.TimeIt/ConsoleEnv.cs` | Terminal detection: is-pipe, supports-color, NO_COLOR, --color/--no-color resolution |
| `src/Winix.TimeIt/CommandRunner.cs` | Spawn child process, collect metrics, return `TimeItResult` |
| `src/Winix.TimeIt/AnsiColor.cs` | Tiny helper: `Dim()`, `Green()`, `Red()`, `Reset()` — returns ANSI strings or empty based on color flag |
| `src/timeit/timeit.csproj` | Console app (net10.0, PackAsTool, PublishAot) |
| `src/timeit/Program.cs` | Arg parsing, call CommandRunner, format+write output, set exit code |
| `tests/Winix.TimeIt.Tests/Winix.TimeIt.Tests.csproj` | xUnit test project |
| `tests/Winix.TimeIt.Tests/FormattingTests.cs` | Tests for all formatting logic |
| `tests/Winix.TimeIt.Tests/ConsoleEnvTests.cs` | Tests for color resolution logic |
| `tests/Winix.TimeIt.Tests/CommandRunnerTests.cs` | Integration tests spawning real child processes |

---

### Task 1: Solution and Project Scaffolding

**Files:**
- Create: `Directory.Build.props`
- Create: `Winix.sln`
- Create: `src/Winix.TimeIt/Winix.TimeIt.csproj`
- Create: `src/timeit/timeit.csproj`
- Create: `tests/Winix.TimeIt.Tests/Winix.TimeIt.Tests.csproj`

- [ ] **Step 1: Create Directory.Build.props**

Create `d:\projects\winix\Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Version>0.1.0</Version>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create the class library project**

Run from `d:\projects\winix`:

```bash
dotnet new classlib -n Winix.TimeIt -o src/Winix.TimeIt -f net10.0
```

Replace the generated `Winix.TimeIt.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
</Project>
```

Delete the auto-generated `Class1.cs`.

- [ ] **Step 3: Create the console app project**

```bash
dotnet new console -n timeit -o src/timeit -f net10.0
```

Replace the generated `timeit.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>timeit</ToolCommandName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.TimeIt\Winix.TimeIt.csproj" />
  </ItemGroup>
</Project>
```

Replace `src/timeit/Program.cs` with a placeholder:

```csharp
return 0;
```

- [ ] **Step 4: Create the test project**

```bash
dotnet new xunit -n Winix.TimeIt.Tests -o tests/Winix.TimeIt.Tests -f net10.0
```

Replace the generated `Winix.TimeIt.Tests.csproj` with:

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
    <ProjectReference Include="..\..\src\Winix.TimeIt\Winix.TimeIt.csproj" />
  </ItemGroup>
</Project>
```

Delete the auto-generated `UnitTest1.cs`.

- [ ] **Step 5: Create the solution and add all projects**

```bash
dotnet new sln -n Winix -o d:/projects/winix
dotnet sln d:/projects/winix/Winix.sln add d:/projects/winix/src/Winix.TimeIt/Winix.TimeIt.csproj
dotnet sln d:/projects/winix/Winix.sln add d:/projects/winix/src/timeit/timeit.csproj
dotnet sln d:/projects/winix/Winix.sln add d:/projects/winix/tests/Winix.TimeIt.Tests/Winix.TimeIt.Tests.csproj
```

- [ ] **Step 6: Create .gitignore**

Create `d:\projects\winix\.gitignore`:

```
bin/
obj/
*.user
*.suo
.vs/
TestResults/
```

- [ ] **Step 7: Verify build**

```bash
dotnet build d:/projects/winix/Winix.sln
```

Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add Directory.Build.props Winix.sln .gitignore src/ tests/
git commit -m "scaffold: Winix solution with TimeIt library, console app, and test project"
```

---

### Task 2: TimeItResult Record

**Files:**
- Create: `src/Winix.TimeIt/TimeItResult.cs`
- Create: `tests/Winix.TimeIt.Tests/TimeItResultTests.cs`

- [ ] **Step 1: Write a test verifying the record holds data correctly**

Create `tests/Winix.TimeIt.Tests/TimeItResultTests.cs`:

```csharp
using Winix.TimeIt;

namespace Winix.TimeIt.Tests;

public class TimeItResultTests
{
    [Fact]
    public void Properties_ReturnConstructorValues()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(12.4),
            CpuTime: TimeSpan.FromSeconds(9.1),
            PeakMemoryBytes: 505_413_632,
            ExitCode: 0
        );

        Assert.Equal(TimeSpan.FromSeconds(12.4), result.WallTime);
        Assert.Equal(TimeSpan.FromSeconds(9.1), result.CpuTime);
        Assert.Equal(505_413_632, result.PeakMemoryBytes);
        Assert.Equal(0, result.ExitCode);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test d:/projects/winix/tests/Winix.TimeIt.Tests/
```

Expected: compilation error — `TimeItResult` does not exist.

- [ ] **Step 3: Implement TimeItResult**

Create `src/Winix.TimeIt/TimeItResult.cs`:

```csharp
namespace Winix.TimeIt;

/// <summary>
/// Immutable result of timing a child process.
/// </summary>
/// <param name="WallTime">Elapsed wall-clock time.</param>
/// <param name="CpuTime">Total CPU time (user + kernel).</param>
/// <param name="PeakMemoryBytes">Peak working set of the child process in bytes.</param>
/// <param name="ExitCode">Exit code of the child process.</param>
public sealed record TimeItResult(
    TimeSpan WallTime,
    TimeSpan CpuTime,
    long PeakMemoryBytes,
    int ExitCode
);
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test d:/projects/winix/tests/Winix.TimeIt.Tests/
```

Expected: 1 test passed.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.TimeIt/TimeItResult.cs tests/Winix.TimeIt.Tests/TimeItResultTests.cs
git commit -m "feat: add TimeItResult record"
```

---

### Task 3: Duration and Memory Formatting

**Files:**
- Create: `src/Winix.TimeIt/Formatting.cs`
- Create: `tests/Winix.TimeIt.Tests/FormattingTests.cs`

- [ ] **Step 1: Write tests for FormatDuration**

Create `tests/Winix.TimeIt.Tests/FormattingTests.cs`:

```csharp
using Winix.TimeIt;

namespace Winix.TimeIt.Tests;

public class FormatDurationTests
{
    [Theory]
    [InlineData(0.0, "0.000s")]
    [InlineData(0.842, "0.842s")]
    [InlineData(0.9999, "1.000s")]
    [InlineData(1.0, "1.0s")]
    [InlineData(12.4, "12.4s")]
    [InlineData(59.99, "60.0s")]
    [InlineData(60.0, "1m 00.0s")]
    [InlineData(207.1, "3m 27.1s")]
    [InlineData(3599.9, "59m 59.9s")]
    [InlineData(3600.0, "1h 00m 00s")]
    [InlineData(4323.0, "1h 12m 03s")]
    public void FormatDuration_ProducesExpectedOutput(double seconds, string expected)
    {
        var timeSpan = TimeSpan.FromSeconds(seconds);
        Assert.Equal(expected, Formatting.FormatDuration(timeSpan));
    }
}

public class FormatBytesTests
{
    [Theory]
    [InlineData(0L, "0 KB")]
    [InlineData(393_216L, "384 KB")]
    [InlineData(999_424L, "976 KB")]
    [InlineData(1_048_576L, "1 MB")]
    [InlineData(505_413_632L, "482 MB")]
    [InlineData(1_073_741_824L, "1.0 GB")]
    [InlineData(2_469_606_195L, "2.3 GB")]
    public void FormatBytes_ProducesExpectedOutput(long bytes, string expected)
    {
        Assert.Equal(expected, Formatting.FormatBytes(bytes));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test d:/projects/winix/tests/Winix.TimeIt.Tests/
```

Expected: compilation error — `Formatting` does not exist.

- [ ] **Step 3: Implement FormatDuration and FormatBytes**

Create `src/Winix.TimeIt/Formatting.cs`:

```csharp
using System.Globalization;

namespace Winix.TimeIt;

/// <summary>
/// Formatting helpers for human-readable time and memory values.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Formats a duration as a human-friendly auto-scaling string.
    /// Under 1s: "0.842s". 1-60s: "12.4s". 1-60m: "3m 27.1s". Over 60m: "1h 12m 03s".
    /// </summary>
    public static string FormatDuration(TimeSpan duration)
    {
        double totalSeconds = duration.TotalSeconds;

        if (totalSeconds < 1.0)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F3}s", totalSeconds);
        }

        if (totalSeconds < 60.0)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F1}s", totalSeconds);
        }

        if (totalSeconds < 3600.0)
        {
            int minutes = (int)(totalSeconds / 60.0);
            double remainingSeconds = totalSeconds - (minutes * 60.0);
            return string.Format(CultureInfo.InvariantCulture, "{0}m {1:00.0}s", minutes, remainingSeconds);
        }

        {
            int hours = (int)(totalSeconds / 3600.0);
            int minutes = (int)((totalSeconds - (hours * 3600.0)) / 60.0);
            int secs = (int)(totalSeconds - (hours * 3600.0) - (minutes * 60.0));
            return string.Format(CultureInfo.InvariantCulture, "{0}h {1:00}m {2:00}s", hours, minutes, secs);
        }
    }

    /// <summary>
    /// Formats a byte count as a human-friendly auto-scaling string (KB, MB, or GB).
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = 1024 * KB;
        const long GB = 1024 * MB;

        if (bytes < MB)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} KB", bytes / KB);
        }

        if (bytes < GB)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} MB", bytes / MB);
        }

        double gb = (double)bytes / GB;
        return string.Format(CultureInfo.InvariantCulture, "{0:F1} GB", gb);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test d:/projects/winix/tests/Winix.TimeIt.Tests/
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.TimeIt/Formatting.cs tests/Winix.TimeIt.Tests/FormattingTests.cs
git commit -m "feat: add duration and memory formatting with auto-scaling"
```

---

### Task 4: Output Formatters (Default, OneLine, JSON)

**Files:**
- Modify: `src/Winix.TimeIt/Formatting.cs`
- Modify: `tests/Winix.TimeIt.Tests/FormattingTests.cs`

- [ ] **Step 1: Write tests for all three output formats**

Add to `tests/Winix.TimeIt.Tests/FormattingTests.cs`:

```csharp
public class FormatDefaultTests
{
    [Fact]
    public void FormatDefault_WithSuccessExitCode_FormatsCorrectly()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(12.4),
            CpuTime: TimeSpan.FromSeconds(9.1),
            PeakMemoryBytes: 505_413_632,
            ExitCode: 0
        );

        string output = Formatting.FormatDefault(result, useColor: false);

        Assert.Contains("real", output);
        Assert.Contains("12.4s", output);
        Assert.Contains("cpu", output);
        Assert.Contains("9.1s", output);
        Assert.Contains("peak", output);
        Assert.Contains("482 MB", output);
        Assert.Contains("exit", output);
        Assert.Contains("0", output);
    }

    [Fact]
    public void FormatDefault_WithColor_ContainsAnsiSequences()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            CpuTime: TimeSpan.FromSeconds(0.5),
            PeakMemoryBytes: 1_048_576,
            ExitCode: 0
        );

        string output = Formatting.FormatDefault(result, useColor: true);

        // Should contain ANSI escape sequences
        Assert.Contains("\x1b[", output);
    }

    [Fact]
    public void FormatDefault_FailedExitCode_WithColor_ContainsRedAnsi()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(1.0),
            CpuTime: TimeSpan.FromSeconds(0.5),
            PeakMemoryBytes: 1_048_576,
            ExitCode: 1
        );

        string output = Formatting.FormatDefault(result, useColor: true);

        // Red ANSI code for non-zero exit
        Assert.Contains("\x1b[31m", output);
    }
}

public class FormatOneLineTests
{
    [Fact]
    public void FormatOneLine_ProducesExpectedFormat()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(12.4),
            CpuTime: TimeSpan.FromSeconds(9.1),
            PeakMemoryBytes: 505_413_632,
            ExitCode: 0
        );

        string output = Formatting.FormatOneLine(result, useColor: false);

        Assert.Equal("[timeit] 12.4s wall | 9.1s cpu | 482 MB peak | exit 0", output);
    }
}

public class FormatJsonTests
{
    [Fact]
    public void FormatJson_ProducesValidJson()
    {
        var result = new TimeItResult(
            WallTime: TimeSpan.FromSeconds(12.4),
            CpuTime: TimeSpan.FromSeconds(9.1),
            PeakMemoryBytes: 505_413_632,
            ExitCode: 0
        );

        string output = Formatting.FormatJson(result);

        Assert.Contains("\"wall_seconds\":", output);
        Assert.Contains("\"cpu_seconds\":", output);
        Assert.Contains("\"peak_memory_bytes\":505413632", output);
        Assert.Contains("\"exit_code\":0", output);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test d:/projects/winix/tests/Winix.TimeIt.Tests/
```

Expected: compilation errors — `FormatDefault`, `FormatOneLine`, `FormatJson` do not exist.

- [ ] **Step 3: Create AnsiColor helper**

Create `src/Winix.TimeIt/AnsiColor.cs`:

```csharp
namespace Winix.TimeIt;

/// <summary>
/// Minimal ANSI colour helpers. Returns escape sequences when colour is enabled, empty strings otherwise.
/// </summary>
internal static class AnsiColor
{
    internal static string Dim(bool enabled) => enabled ? "\x1b[2m" : "";
    internal static string Green(bool enabled) => enabled ? "\x1b[32m" : "";
    internal static string Red(bool enabled) => enabled ? "\x1b[31m" : "";
    internal static string Reset(bool enabled) => enabled ? "\x1b[0m" : "";
}
```

- [ ] **Step 4: Implement FormatDefault, FormatOneLine, FormatJson**

Add to `src/Winix.TimeIt/Formatting.cs`, inside the `Formatting` class:

```csharp
    /// <summary>
    /// Formats a <see cref="TimeItResult"/> as a multi-line human-readable summary.
    /// </summary>
    public static string FormatDefault(TimeItResult result, bool useColor)
    {
        string dim = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);
        string exitColor = result.ExitCode == 0
            ? AnsiColor.Green(useColor)
            : AnsiColor.Red(useColor);

        return $"""
          {dim}real{reset}  {FormatDuration(result.WallTime)}
          {dim}cpu{reset}   {FormatDuration(result.CpuTime)}
          {dim}peak{reset}  {FormatBytes(result.PeakMemoryBytes)}
          {dim}exit{reset}  {exitColor}{result.ExitCode}{reset}
        """;
    }

    /// <summary>
    /// Formats a <see cref="TimeItResult"/> as a single compact line.
    /// </summary>
    public static string FormatOneLine(TimeItResult result, bool useColor)
    {
        string exitColor = result.ExitCode == 0
            ? AnsiColor.Green(useColor)
            : AnsiColor.Red(useColor);
        string reset = AnsiColor.Reset(useColor);

        return $"[timeit] {FormatDuration(result.WallTime)} wall | {FormatDuration(result.CpuTime)} cpu | {FormatBytes(result.PeakMemoryBytes)} peak | exit {exitColor}{result.ExitCode}{reset}";
    }

    /// <summary>
    /// Formats a <see cref="TimeItResult"/> as a JSON object. No colour, machine-parseable.
    /// </summary>
    public static string FormatJson(TimeItResult result)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{{\"wall_seconds\":{0:F3},\"cpu_seconds\":{1:F3},\"peak_memory_bytes\":{2},\"exit_code\":{3}}}",
            result.WallTime.TotalSeconds,
            result.CpuTime.TotalSeconds,
            result.PeakMemoryBytes,
            result.ExitCode
        );
    }
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test d:/projects/winix/tests/Winix.TimeIt.Tests/
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.TimeIt/AnsiColor.cs src/Winix.TimeIt/Formatting.cs tests/Winix.TimeIt.Tests/FormattingTests.cs
git commit -m "feat: add default, one-line, and JSON output formatters with ANSI colour"
```

---

### Task 5: ConsoleEnv — Terminal and Colour Detection

**Files:**
- Create: `src/Winix.TimeIt/ConsoleEnv.cs`
- Create: `tests/Winix.TimeIt.Tests/ConsoleEnvTests.cs`

- [ ] **Step 1: Write tests for colour resolution logic**

Create `tests/Winix.TimeIt.Tests/ConsoleEnvTests.cs`:

```csharp
using Winix.TimeIt;

namespace Winix.TimeIt.Tests;

public class ConsoleEnvTests
{
    [Fact]
    public void ResolveUseColor_ExplicitColorFlag_ReturnsTrue()
    {
        bool result = ConsoleEnv.ResolveUseColor(
            colorFlag: true, noColorFlag: false, noColorEnv: false, isTerminal: false);

        Assert.True(result);
    }

    [Fact]
    public void ResolveUseColor_ExplicitNoColorFlag_ReturnsFalse()
    {
        bool result = ConsoleEnv.ResolveUseColor(
            colorFlag: false, noColorFlag: true, noColorEnv: false, isTerminal: true);

        Assert.False(result);
    }

    [Fact]
    public void ResolveUseColor_NoColorEnvSet_ReturnsFalse()
    {
        bool result = ConsoleEnv.ResolveUseColor(
            colorFlag: false, noColorFlag: false, noColorEnv: true, isTerminal: true);

        Assert.False(result);
    }

    [Fact]
    public void ResolveUseColor_ExplicitColorFlag_OverridesNoColorEnv()
    {
        bool result = ConsoleEnv.ResolveUseColor(
            colorFlag: true, noColorFlag: false, noColorEnv: true, isTerminal: false);

        Assert.True(result);
    }

    [Fact]
    public void ResolveUseColor_NoFlags_Terminal_ReturnsTrue()
    {
        bool result = ConsoleEnv.ResolveUseColor(
            colorFlag: false, noColorFlag: false, noColorEnv: false, isTerminal: true);

        Assert.True(result);
    }

    [Fact]
    public void ResolveUseColor_NoFlags_Piped_ReturnsFalse()
    {
        bool result = ConsoleEnv.ResolveUseColor(
            colorFlag: false, noColorFlag: false, noColorEnv: false, isTerminal: false);

        Assert.False(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test d:/projects/winix/tests/Winix.TimeIt.Tests/
```

Expected: compilation error — `ConsoleEnv` does not exist.

- [ ] **Step 3: Implement ConsoleEnv**

Create `src/Winix.TimeIt/ConsoleEnv.cs`:

```csharp
namespace Winix.TimeIt;

/// <summary>
/// Terminal environment detection. Proto-Yort.ShellKit — will be extracted to the shared library
/// when the second Winix tool is built.
/// </summary>
public static class ConsoleEnv
{
    /// <summary>
    /// Returns true if the <c>NO_COLOR</c> environment variable is set (any value, including empty).
    /// See https://no-color.org.
    /// </summary>
    public static bool IsNoColorEnvSet()
    {
        return Environment.GetEnvironmentVariable("NO_COLOR") is not null;
    }

    /// <summary>
    /// Returns true if the given stream (stdout or stderr) is connected to a terminal, not a pipe.
    /// </summary>
    public static bool IsTerminal(bool checkStdErr)
    {
        return checkStdErr ? !Console.IsErrorRedirected : !Console.IsOutputRedirected;
    }

    /// <summary>
    /// Resolves whether colour output should be used, applying the precedence rules:
    /// explicit flag > NO_COLOR env var > auto-detection (is terminal?).
    /// </summary>
    /// <param name="colorFlag">True if --color was passed.</param>
    /// <param name="noColorFlag">True if --no-color was passed.</param>
    /// <param name="noColorEnv">True if NO_COLOR environment variable is set.</param>
    /// <param name="isTerminal">True if the output stream is a terminal.</param>
    public static bool ResolveUseColor(bool colorFlag, bool noColorFlag, bool noColorEnv, bool isTerminal)
    {
        // Explicit flags take highest priority
        if (colorFlag)
        {
            return true;
        }

        if (noColorFlag)
        {
            return false;
        }

        // NO_COLOR env var takes next priority
        if (noColorEnv)
        {
            return false;
        }

        // Fall back to terminal detection
        return isTerminal;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test d:/projects/winix/tests/Winix.TimeIt.Tests/
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.TimeIt/ConsoleEnv.cs tests/Winix.TimeIt.Tests/ConsoleEnvTests.cs
git commit -m "feat: add ConsoleEnv terminal/colour detection (proto-ShellKit)"
```

---

### Task 6: CommandRunner — Process Execution and Metrics

**Files:**
- Create: `src/Winix.TimeIt/CommandRunner.cs`
- Create: `tests/Winix.TimeIt.Tests/CommandRunnerTests.cs`

- [ ] **Step 1: Write integration test for successful command execution**

Create `tests/Winix.TimeIt.Tests/CommandRunnerTests.cs`:

```csharp
using Winix.TimeIt;

namespace Winix.TimeIt.Tests;

public class CommandRunnerTests
{
    [Fact]
    public void Run_SuccessfulCommand_ReturnsZeroExitCode()
    {
        var result = CommandRunner.Run("dotnet", new[] { "--version" });

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.WallTime > TimeSpan.Zero);
        Assert.True(result.PeakMemoryBytes > 0);
    }

    [Fact]
    public void Run_FailingCommand_ReturnsNonZeroExitCode()
    {
        // dotnet with a bad argument returns non-zero
        var result = CommandRunner.Run("dotnet", new[] { "nonexistent-command-that-does-not-exist" });

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void Run_CommandNotFound_ThrowsCommandNotFoundException()
    {
        Assert.Throws<CommandNotFoundException>(
            () => CommandRunner.Run("this-command-surely-does-not-exist-abcxyz", Array.Empty<string>()));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test d:/projects/winix/tests/Winix.TimeIt.Tests/
```

Expected: compilation errors — `CommandRunner` and `CommandNotFoundException` do not exist.

- [ ] **Step 3: Implement CommandRunner**

Create `src/Winix.TimeIt/CommandRunner.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;

namespace Winix.TimeIt;

/// <summary>
/// Thrown when the specified command cannot be found.
/// </summary>
public sealed class CommandNotFoundException : Exception
{
    public CommandNotFoundException(string command)
        : base($"command not found: {command}")
    {
        Command = command;
    }

    public string Command { get; }
}

/// <summary>
/// Spawns a child process and collects timing/memory metrics.
/// </summary>
public static class CommandRunner
{
    /// <summary>
    /// Runs the specified command with arguments, collecting wall time, CPU time, peak memory, and exit code.
    /// Stdin, stdout, and stderr are inherited — the child process interacts with the terminal directly.
    /// </summary>
    /// <exception cref="CommandNotFoundException">The command was not found on PATH.</exception>
    public static TimeItResult Run(string command, string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            // Inherit stdin/stdout/stderr — no redirection
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
        };

        foreach (string arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var stopwatch = Stopwatch.StartNew();
        Process process;

        try
        {
            process = Process.Start(startInfo)
                ?? throw new CommandNotFoundException(command);
        }
        catch (Win32Exception)
        {
            throw new CommandNotFoundException(command);
        }

        process.WaitForExit();
        stopwatch.Stop();

        TimeSpan cpuTime;
        long peakMemory;

        try
        {
            cpuTime = process.TotalProcessorTime;
        }
        catch (InvalidOperationException)
        {
            cpuTime = TimeSpan.Zero;
        }

        try
        {
            peakMemory = process.PeakWorkingSet64;
        }
        catch (InvalidOperationException)
        {
            peakMemory = 0;
        }

        return new TimeItResult(
            WallTime: stopwatch.Elapsed,
            CpuTime: cpuTime,
            PeakMemoryBytes: peakMemory,
            ExitCode: process.ExitCode
        );
    }
}
```

Note: `Win32Exception` is thrown on all platforms (not just Windows) when the command is not found. On Linux/macOS .NET maps the POSIX error to `Win32Exception` as well.

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test d:/projects/winix/tests/Winix.TimeIt.Tests/
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.TimeIt/CommandRunner.cs tests/Winix.TimeIt.Tests/CommandRunnerTests.cs
git commit -m "feat: add CommandRunner with process execution and metric collection"
```

---

### Task 7: Program.cs — Argument Parsing and Main Entry Point

**Files:**
- Modify: `src/timeit/Program.cs`

- [ ] **Step 1: Implement argument parsing and main flow**

Replace `src/timeit/Program.cs` with:

```csharp
using System.Reflection;
using Winix.TimeIt;

return Run(args);

static int Run(string[] args)
{
    bool colorFlag = false;
    bool noColorFlag = false;
    bool oneLine = false;
    bool jsonOutput = false;
    bool useStdout = false;
    int commandStart = -1;

    // Parse timeit flags, stop at first unrecognised argument or --
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--color":
                colorFlag = true;
                break;
            case "--no-color":
                noColorFlag = true;
                break;
            case "-1":
            case "--oneline":
                oneLine = true;
                break;
            case "--json":
                jsonOutput = true;
                break;
            case "--stdout":
                useStdout = true;
                break;
            case "--version":
                Console.WriteLine($"timeit {GetVersion()}");
                return 0;
            case "-h":
            case "--help":
                PrintHelp();
                return 0;
            case "--":
                commandStart = i + 1;
                break;
            default:
                commandStart = i;
                break;
        }

        if (commandStart >= 0)
        {
            break;
        }
    }

    if (commandStart < 0 || commandStart >= args.Length)
    {
        Console.Error.WriteLine("timeit: no command specified. Run 'timeit --help' for usage.");
        return 125;
    }

    string command = args[commandStart];
    string[] commandArgs = args[(commandStart + 1)..];

    // Resolve colour
    bool noColorEnv = ConsoleEnv.IsNoColorEnvSet();
    bool isTerminal = ConsoleEnv.IsTerminal(checkStdErr: !useStdout);
    bool useColor = ConsoleEnv.ResolveUseColor(colorFlag, noColorFlag, noColorEnv, isTerminal);

    // Run the command
    TimeItResult result;
    try
    {
        result = CommandRunner.Run(command, commandArgs);
    }
    catch (CommandNotFoundException ex)
    {
        Console.Error.WriteLine($"timeit: {ex.Message}");
        return 127;
    }

    // Format and write output
    string output;
    if (jsonOutput)
    {
        output = Formatting.FormatJson(result);
    }
    else if (oneLine)
    {
        output = Formatting.FormatOneLine(result, useColor);
    }
    else
    {
        output = Formatting.FormatDefault(result, useColor);
    }

    TextWriter writer = useStdout ? Console.Out : Console.Error;
    writer.WriteLine(output);

    return result.ExitCode;
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        Usage: timeit [options] [--] <command> [args...]

        Time a command and show wall clock, CPU time, peak memory, and exit code.

        Options:
          -1, --oneline       Single-line output format
          --json              JSON output format
          --stdout            Write summary to stdout instead of stderr
          --no-color          Disable colored output
          --color             Force colored output (even when piped)
          --version           Show version
          -h, --help          Show help
        """);
}

static string GetVersion()
{
    return typeof(TimeItResult).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "0.0.0";
}
```

- [ ] **Step 2: Build and verify no warnings**

```bash
dotnet build d:/projects/winix/Winix.sln
```

Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 3: Manual smoke test — run timeit against a simple command**

```bash
dotnet run --project d:/projects/winix/src/timeit -- dotnet --version
```

Expected: prints the dotnet version, then a timing summary on stderr.

- [ ] **Step 4: Manual smoke test — help flag**

```bash
dotnet run --project d:/projects/winix/src/timeit -- --help
```

Expected: prints usage help.

- [ ] **Step 5: Manual smoke test — exit code pass-through**

```bash
dotnet run --project d:/projects/winix/src/timeit -- dotnet nonexistent-command-xyz
```

Then check exit code:

```bash
echo $?
```

Expected: non-zero exit code from dotnet, not 0.

- [ ] **Step 6: Manual smoke test — command not found**

```bash
dotnet run --project d:/projects/winix/src/timeit -- this-command-does-not-exist-xyz
```

Expected: `timeit: command not found: this-command-does-not-exist-xyz` on stderr, exit code 127.

- [ ] **Step 7: Commit**

```bash
git add src/timeit/Program.cs
git commit -m "feat: add timeit entry point with arg parsing and output routing"
```

---

### Task 8: Full Test Suite and AOT Verification

**Files:**
- Modify: `tests/Winix.TimeIt.Tests/CommandRunnerTests.cs` (add edge case tests)

- [ ] **Step 1: Add edge case tests for CommandRunner**

Add to `tests/Winix.TimeIt.Tests/CommandRunnerTests.cs`:

```csharp
    [Fact]
    public void Run_CommandWithArguments_PassesArgsCorrectly()
    {
        // dotnet --list-sdks should succeed and produce output
        var result = CommandRunner.Run("dotnet", new[] { "--list-sdks" });

        Assert.Equal(0, result.ExitCode);
    }
```

- [ ] **Step 2: Run the full test suite**

```bash
dotnet test d:/projects/winix/Winix.sln
```

Expected: all tests pass, zero warnings.

- [ ] **Step 3: Verify AOT publish works**

```bash
dotnet publish d:/projects/winix/src/timeit/timeit.csproj -c Release -r win-x64
```

Expected: publishes successfully. Check the output directory for the native binary.

- [ ] **Step 4: Test the published binary directly**

Find the published binary path from the publish output (typically `src/timeit/bin/Release/net10.0/win-x64/publish/timeit.exe`), then run it:

```bash
d:/projects/winix/src/timeit/bin/Release/net10.0/win-x64/publish/timeit.exe dotnet --version
```

Expected: same behavior as `dotnet run` — dotnet version printed, timing summary on stderr.

- [ ] **Step 5: Test the published binary with --json**

```bash
d:/projects/winix/src/timeit/bin/Release/net10.0/win-x64/publish/timeit.exe --json dotnet --version
```

Expected: JSON summary on stderr, dotnet version on stdout.

- [ ] **Step 6: Commit**

```bash
git add tests/Winix.TimeIt.Tests/CommandRunnerTests.cs
git commit -m "test: add edge case tests and verify AOT publish"
```

---

### Task 9: CLAUDE.md and Final Polish

**Files:**
- Create: `d:\projects\winix\CLAUDE.md`

- [ ] **Step 1: Create CLAUDE.md for the Winix repo**

Create `d:\projects\winix\CLAUDE.md`:

```markdown
# Winix

Cross-platform CLI tool suite (Windows + *nix). .NET / C# / AOT.

## Build

```bash
dotnet build Winix.sln
```

## Test

```bash
dotnet test Winix.sln
```

## Publish (AOT native binary)

```bash
dotnet publish src/timeit/timeit.csproj -c Release -r win-x64
```

## Architecture

- **Class libraries** (`Winix.TimeIt`, etc.) contain all logic — testable without process spawning for formatting, integration tests for process execution
- **Console apps** (`timeit`, etc.) are thin entry points — arg parsing, call library, set exit code
- **Shared library** (`Yort.ShellKit`, future) — terminal detection, colour, path normalisation, process spawning. Currently inline in each tool as `ConsoleEnv`.

## Conventions

- TDD: write failing test, implement, verify, commit
- AOT-compatible: no unconstrained reflection, use trim analyzers
- All output formatting in class library (testable), all I/O in console app
- Summary output goes to stderr by default (don't pollute piped command output)
- Respect `NO_COLOR` env var (no-color.org)
- Exit codes: pass through child process exit code; 125/126/127 for timeit's own errors (POSIX convention)

## Project layout

```
src/Winix.TimeIt/     — class library (timing logic, formatting, terminal detection)
src/timeit/           — console app entry point
tests/Winix.TimeIt.Tests/ — xUnit tests
```
```

- [ ] **Step 2: Run full build and test one final time**

```bash
dotnet build d:/projects/winix/Winix.sln
```

```bash
dotnet test d:/projects/winix/Winix.sln
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: add CLAUDE.md with build/test/architecture guide"
```

---

## Spec Coverage Verification

| Spec Requirement | Task |
|------------------|------|
| TimeItResult data record | Task 2 |
| Duration auto-scaling (sub-1s, seconds, minutes, hours) | Task 3 |
| Memory auto-scaling (KB, MB, GB) | Task 3 |
| Default multi-line format with dim labels, green/red exit | Task 4 |
| One-line format | Task 4 |
| JSON format with raw values | Task 4 |
| ConsoleEnv: NO_COLOR, --color/--no-color, pipe detection | Task 5 |
| Colour precedence: flag > NO_COLOR > auto-detect | Task 5 |
| CommandRunner: spawn process, collect metrics | Task 6 |
| Command not found → exit 127 | Task 6 + Task 7 |
| Exit code pass-through | Task 6 + Task 7 |
| Arg parsing: -1, --json, --stdout, --color, --no-color, --version, --help | Task 7 |
| `--` separator support | Task 7 |
| No command given → exit 125 | Task 7 |
| Summary to stderr by default, --stdout override | Task 7 |
| AOT publish verification | Task 8 |
| PackAsTool configuration | Task 1 |
| Directory.Build.props shared settings | Task 1 |
| CLAUDE.md | Task 9 |
