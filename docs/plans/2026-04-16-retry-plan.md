# retry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `retry`, a cross-platform command retry tool with configurable backoff, jitter, and exit-code filtering.

**Architecture:** Standard Winix two-project pattern: `Winix.Retry` class library (retry loop, backoff calculation, formatting) + `retry` thin console app (arg parsing, output). Process spawning follows the `timeit` pattern (inherited stdio, `ArgumentList`). Exit-code filtering uses `--on`/`--until` dual flags.

**Tech Stack:** .NET 10, AOT, xUnit, ShellKit (CommandLineParser, ExitCode, JsonHelper, AnsiColor, DurationParser)

---

### Task 1: Move DurationParser to ShellKit and add `ms` support

`DurationParser` currently lives in `Winix.FileWalk` but is a general-purpose utility needed by retry (and has no filesystem dependency). Move it to `Yort.ShellKit` and add millisecond support.

**Files:**
- Move: `src/Winix.FileWalk/DurationParser.cs` → `src/Yort.ShellKit/DurationParser.cs`
- Modify: `src/files/Program.cs` (change `using`)
- Modify: `src/treex/Program.cs` (change `using`)
- Move: `tests/Winix.FileWalk.Tests/DurationParserTests.cs` → `tests/Yort.ShellKit.Tests/DurationParserTests.cs`
- Modify: `tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj` (if FileWalk ref needed for tests — check)

- [ ] **Step 1: Copy DurationParser.cs to ShellKit and change namespace**

Copy `src/Winix.FileWalk/DurationParser.cs` to `src/Yort.ShellKit/DurationParser.cs`. Change the namespace from `Winix.FileWalk` to `Yort.ShellKit`. Do not modify the logic yet.

- [ ] **Step 2: Add `ms` suffix support to the ShellKit copy**

The current parser uses a single-char suffix. Change it to support the two-character `ms` suffix for milliseconds. The parsing logic should:
1. Check if the value ends with `ms` first (two-char suffix takes priority over single-char)
2. If so, parse the digits before `ms` and return `TimeSpan.FromMilliseconds(raw)`
3. Otherwise fall back to the existing single-char suffix logic

Update the `TryParse` method:

```csharp
public static bool TryParse(string value, out TimeSpan duration)
{
    duration = TimeSpan.Zero;

    if (string.IsNullOrEmpty(value) || value.Length < 2)
    {
        return false;
    }

    // Check for two-character suffix "ms" first.
    ReadOnlySpan<char> digits;
    char suffix;
    bool isMilliseconds = false;

    if (value.Length >= 3 && value[value.Length - 2] == 'm' && value[value.Length - 1] == 's')
    {
        digits = value.AsSpan(0, value.Length - 2);
        suffix = 's'; // not used for ms path, but keeps the variable in scope
        isMilliseconds = true;
    }
    else
    {
        suffix = value[value.Length - 1];
        digits = value.AsSpan(0, value.Length - 1);
    }

    if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long raw))
    {
        return false;
    }

    if (!isMilliseconds && suffix is not 's' and not 'm' and not 'h' and not 'd' and not 'w')
    {
        return false;
    }

    try
    {
        if (isMilliseconds)
        {
            duration = TimeSpan.FromMilliseconds(raw);
        }
        else
        {
            duration = suffix switch
            {
                's' => TimeSpan.FromSeconds(raw),
                'm' => TimeSpan.FromMinutes(raw),
                'h' => TimeSpan.FromHours(raw),
                'd' => TimeSpan.FromDays(raw),
                'w' => TimeSpan.FromDays(checked(raw * 7)),
                _ => throw new InvalidOperationException("Unreachable — suffix validated above")
            };
        }
    }
    catch (OverflowException)
    {
        duration = TimeSpan.Zero;
        return false;
    }
    catch (ArgumentOutOfRangeException)
    {
        duration = TimeSpan.Zero;
        return false;
    }

    return true;
}
```

Also update the `Parse` method's error message and XML doc to mention `ms`.

- [ ] **Step 3: Move DurationParserTests to ShellKit.Tests**

Copy `tests/Winix.FileWalk.Tests/DurationParserTests.cs` to `tests/Yort.ShellKit.Tests/DurationParserTests.cs`. Change namespace from `Winix.FileWalk.Tests` to `Yort.ShellKit.Tests`. Change `using Winix.FileWalk;` to `using Yort.ShellKit;`.

- [ ] **Step 4: Add ms tests to DurationParserTests**

Add these tests to the ShellKit copy:

```csharp
[Fact]
public void Parse_Milliseconds_ReturnsCorrectTimeSpan()
{
    Assert.Equal(TimeSpan.FromMilliseconds(500), DurationParser.Parse("500ms"));
}

[Fact]
public void Parse_OneMillisecond_ReturnsCorrectTimeSpan()
{
    Assert.Equal(TimeSpan.FromMilliseconds(1), DurationParser.Parse("1ms"));
}

[Fact]
public void TryParse_Milliseconds_ReturnsTrue()
{
    bool ok = DurationParser.TryParse("200ms", out TimeSpan duration);
    Assert.True(ok);
    Assert.Equal(TimeSpan.FromMilliseconds(200), duration);
}

[Fact]
public void TryParse_ZeroMs_ReturnsTrue()
{
    bool ok = DurationParser.TryParse("0ms", out TimeSpan duration);
    Assert.True(ok);
    Assert.Equal(TimeSpan.Zero, duration);
}
```

- [ ] **Step 5: Run ShellKit tests**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj`
Expected: All tests pass, including existing ShellKit tests and the new/moved DurationParser tests.

- [ ] **Step 6: Delete the old DurationParser from FileWalk**

Delete `src/Winix.FileWalk/DurationParser.cs`. Delete `tests/Winix.FileWalk.Tests/DurationParserTests.cs`.

- [ ] **Step 7: Update consumers to use ShellKit namespace**

In `src/files/Program.cs`: change `using Winix.FileWalk;` to `using Yort.ShellKit;` (if `DurationParser` is the only reason for that import — check whether other FileWalk types are also used; if so, add a second `using` rather than removing the FileWalk one).

In `src/treex/Program.cs`: same change — add `using Yort.ShellKit;` if not already present, and only remove `using Winix.FileWalk;` if nothing else from FileWalk is used in that file.

- [ ] **Step 8: Run full solution tests**

Run: `dotnet test Winix.sln`
Expected: All 1038+ tests pass. No regressions.

- [ ] **Step 9: Commit**

```
git add -A
git commit -m "refactor: move DurationParser to ShellKit and add ms support

DurationParser is a general-purpose utility, not filesystem-specific.
Moving to ShellKit makes it available to retry (and future tools)
without pulling in FileWalk. Added millisecond suffix (500ms, 200ms)."
```

---

### Task 2: Create project scaffolding

Set up the three projects (library, console app, tests) and add them to the solution.

**Files:**
- Create: `src/Winix.Retry/Winix.Retry.csproj`
- Create: `src/retry/retry.csproj`
- Create: `src/retry/Program.cs` (minimal stub)
- Create: `tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj`
- Modify: `Winix.sln`

- [ ] **Step 1: Create the class library project**

```xml
<!-- src/Winix.Retry/Winix.Retry.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
    <IsPackable>false</IsPackable>
    <PackageId>Winix.Retry.Library</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the console app project**

```xml
<!-- src/retry/retry.csproj -->
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
    <ToolCommandName>retry</ToolCommandName>
    <PackageId>Winix.Retry</PackageId>
    <Description>Run a command with automatic retries, configurable backoff, and exit-code filtering.</Description>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.Retry\Winix.Retry.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create minimal Program.cs stub**

```csharp
// src/retry/Program.cs
using System.Reflection;
using Winix.Retry;
using Yort.ShellKit;

namespace Retry;

internal sealed class Program
{
    static int Main(string[] args)
    {
        // Stub — will be implemented in Task 7
        return ExitCode.Success;
    }
}
```

- [ ] **Step 4: Create the test project**

```xml
<!-- tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Winix.Retry\Winix.Retry.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Add all three projects to the solution**

```bash
dotnet sln Winix.sln add src/Winix.Retry/Winix.Retry.csproj
dotnet sln Winix.sln add src/retry/retry.csproj
dotnet sln Winix.sln add tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj
```

- [ ] **Step 6: Verify the solution builds**

Run: `dotnet build Winix.sln`
Expected: Build succeeds with no errors or warnings.

- [ ] **Step 7: Commit**

```
git add -A
git commit -m "feat(retry): add project scaffolding

Class library (Winix.Retry), console app (retry), and test project
(Winix.Retry.Tests). Added to solution. Minimal stubs, builds clean."
```

---

### Task 3: BackoffStrategy enum and BackoffCalculator

Pure computation, no I/O. TDD-first.

**Files:**
- Create: `src/Winix.Retry/BackoffStrategy.cs`
- Create: `src/Winix.Retry/BackoffCalculator.cs`
- Create: `tests/Winix.Retry.Tests/BackoffCalculatorTests.cs`

- [ ] **Step 1: Write failing tests for BackoffCalculator**

```csharp
// tests/Winix.Retry.Tests/BackoffCalculatorTests.cs
using Xunit;
using Winix.Retry;

namespace Winix.Retry.Tests;

public class BackoffCalculatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void Fixed_ReturnsSameDelay_RegardlessOfAttempt(int attempt)
    {
        var delay = BackoffCalculator.Calculate(
            TimeSpan.FromSeconds(2), attempt, BackoffStrategy.Fixed, jitter: false, random: null);

        Assert.Equal(TimeSpan.FromSeconds(2), delay);
    }

    [Theory]
    [InlineData(1, 2.0)]
    [InlineData(2, 4.0)]
    [InlineData(3, 6.0)]
    public void Linear_ReturnsDelayTimesAttempt(int attempt, double expectedSeconds)
    {
        var delay = BackoffCalculator.Calculate(
            TimeSpan.FromSeconds(2), attempt, BackoffStrategy.Linear, jitter: false, random: null);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Theory]
    [InlineData(1, 2.0)]   // 2 * 2^0 = 2
    [InlineData(2, 4.0)]   // 2 * 2^1 = 4
    [InlineData(3, 8.0)]   // 2 * 2^2 = 8
    [InlineData(4, 16.0)]  // 2 * 2^3 = 16
    public void Exponential_ReturnsDelayTimesPowerOfTwo(int attempt, double expectedSeconds)
    {
        var delay = BackoffCalculator.Calculate(
            TimeSpan.FromSeconds(2), attempt, BackoffStrategy.Exponential, jitter: false, random: null);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Fact]
    public void Jitter_ReducesDelay_WithinExpectedRange()
    {
        // Use a seeded Random for deterministic testing.
        var random = new Random(42);
        var delays = new List<TimeSpan>();

        for (int i = 0; i < 100; i++)
        {
            delays.Add(BackoffCalculator.Calculate(
                TimeSpan.FromSeconds(10), 1, BackoffStrategy.Fixed, jitter: true, random: random));
        }

        // All delays should be in [5.0, 10.0) — [0.5, 1.0) * 10s
        foreach (var delay in delays)
        {
            Assert.InRange(delay.TotalSeconds, 5.0, 10.0);
            Assert.True(delay.TotalSeconds < 10.0, "Jitter factor is [0.5, 1.0) — must be strictly less than base");
        }
    }

    [Fact]
    public void Jitter_WithExponential_AppliesAfterExponentialCalculation()
    {
        var random = new Random(42);

        var delay = BackoffCalculator.Calculate(
            TimeSpan.FromSeconds(2), 3, BackoffStrategy.Exponential, jitter: true, random: random);

        // Base exponential for attempt 3: 2 * 2^2 = 8s. With jitter: [4.0, 8.0)
        Assert.InRange(delay.TotalSeconds, 4.0, 8.0);
    }

    [Fact]
    public void ZeroDelay_ReturnsZero_RegardlessOfStrategy()
    {
        Assert.Equal(TimeSpan.Zero, BackoffCalculator.Calculate(
            TimeSpan.Zero, 1, BackoffStrategy.Fixed, jitter: false, random: null));
        Assert.Equal(TimeSpan.Zero, BackoffCalculator.Calculate(
            TimeSpan.Zero, 3, BackoffStrategy.Exponential, jitter: false, random: null));
    }

    [Fact]
    public void SubSecondDelay_WorksCorrectly()
    {
        var delay = BackoffCalculator.Calculate(
            TimeSpan.FromMilliseconds(200), 3, BackoffStrategy.Exponential, jitter: false, random: null);

        // 200ms * 2^2 = 800ms
        Assert.Equal(TimeSpan.FromMilliseconds(800), delay);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj`
Expected: Compilation errors — `BackoffCalculator` and `BackoffStrategy` don't exist yet.

- [ ] **Step 3: Create BackoffStrategy enum**

```csharp
// src/Winix.Retry/BackoffStrategy.cs
namespace Winix.Retry;

/// <summary>
/// Delay scaling strategy between retry attempts.
/// </summary>
public enum BackoffStrategy
{
    /// <summary>Same delay every time.</summary>
    Fixed,

    /// <summary>Delay grows linearly: base × attempt.</summary>
    Linear,

    /// <summary>Delay doubles each time: base × 2^(attempt−1).</summary>
    Exponential
}
```

- [ ] **Step 4: Create BackoffCalculator**

```csharp
// src/Winix.Retry/BackoffCalculator.cs
namespace Winix.Retry;

/// <summary>
/// Calculates delay durations for retry attempts based on strategy and optional jitter.
/// </summary>
public static class BackoffCalculator
{
    /// <summary>
    /// Calculates the delay for a given retry attempt number.
    /// </summary>
    /// <param name="baseDelay">The base delay configured by the user.</param>
    /// <param name="attempt">The retry attempt number (1-indexed: 1 = first retry).</param>
    /// <param name="strategy">How the delay scales across attempts.</param>
    /// <param name="jitter">Whether to randomise the delay within [50%, 100%) of the calculated value.</param>
    /// <param name="random">Random instance for jitter. Required when <paramref name="jitter"/> is true.</param>
    /// <returns>The delay to wait before this attempt.</returns>
    public static TimeSpan Calculate(TimeSpan baseDelay, int attempt,
        BackoffStrategy strategy, bool jitter, Random? random)
    {
        double multiplier = strategy switch
        {
            BackoffStrategy.Fixed => 1.0,
            BackoffStrategy.Linear => attempt,
            BackoffStrategy.Exponential => Math.Pow(2, attempt - 1),
            _ => 1.0
        };

        double totalMs = baseDelay.TotalMilliseconds * multiplier;

        if (jitter && random != null && totalMs > 0)
        {
            // Scale to [0.5, 1.0) of calculated delay
            double factor = 0.5 + (random.NextDouble() * 0.5);
            totalMs *= factor;
        }

        return TimeSpan.FromMilliseconds(totalMs);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj`
Expected: All BackoffCalculator tests pass.

- [ ] **Step 6: Commit**

```
git add -A
git commit -m "feat(retry): add BackoffCalculator with fixed/linear/exp strategies and jitter"
```

---

### Task 4: RetryOptions, RetryOutcome, RetryResult, AttemptInfo

Data types consumed and returned by the runner. TDD — test validation rules.

**Files:**
- Create: `src/Winix.Retry/RetryOptions.cs`
- Create: `src/Winix.Retry/RetryOutcome.cs`
- Create: `src/Winix.Retry/RetryResult.cs`
- Create: `src/Winix.Retry/AttemptInfo.cs`
- Create: `tests/Winix.Retry.Tests/RetryOptionsTests.cs`

- [ ] **Step 1: Write failing tests for RetryOptions validation**

```csharp
// tests/Winix.Retry.Tests/RetryOptionsTests.cs
using Xunit;
using Winix.Retry;

namespace Winix.Retry.Tests;

public class RetryOptionsTests
{
    [Fact]
    public void Construct_WithDefaults_SetsExpectedValues()
    {
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1));

        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), options.Delay);
        Assert.Equal(BackoffStrategy.Fixed, options.Backoff);
        Assert.False(options.Jitter);
        Assert.Null(options.RetryOnCodes);
        Assert.Null(options.StopOnCodes);
    }

    [Fact]
    public void Construct_NegativeRetries_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetryOptions(maxRetries: -1, delay: TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Construct_NegativeDelay_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Construct_BothRetryOnAndStopOn_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new RetryOptions(
                maxRetries: 3,
                delay: TimeSpan.FromSeconds(1),
                retryOnCodes: new HashSet<int> { 1 },
                stopOnCodes: new HashSet<int> { 0 }));
    }

    [Fact]
    public void Construct_ZeroRetries_IsValid()
    {
        var options = new RetryOptions(maxRetries: 0, delay: TimeSpan.FromSeconds(1));
        Assert.Equal(0, options.MaxRetries);
    }

    [Fact]
    public void Construct_ZeroDelay_IsValid()
    {
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, options.Delay);
    }

    [Fact]
    public void Construct_WithRetryOnCodes_SetsCorrectly()
    {
        var codes = new HashSet<int> { 1, 2, 3 };
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1), retryOnCodes: codes);

        Assert.NotNull(options.RetryOnCodes);
        Assert.Contains(1, options.RetryOnCodes);
        Assert.Contains(2, options.RetryOnCodes);
        Assert.Contains(3, options.RetryOnCodes);
        Assert.Null(options.StopOnCodes);
    }

    [Fact]
    public void Construct_WithStopOnCodes_SetsCorrectly()
    {
        var codes = new HashSet<int> { 0, 1 };
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1), stopOnCodes: codes);

        Assert.NotNull(options.StopOnCodes);
        Assert.Contains(0, options.StopOnCodes);
        Assert.Contains(1, options.StopOnCodes);
        Assert.Null(options.RetryOnCodes);
    }

    [Fact]
    public void ShouldRetry_Default_ReturnsTrueForNonZero()
    {
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1));

        Assert.True(options.ShouldRetry(1));
        Assert.True(options.ShouldRetry(2));
        Assert.True(options.ShouldRetry(137));
        Assert.False(options.ShouldRetry(0));
    }

    [Fact]
    public void ShouldRetry_WithRetryOnCodes_ReturnsTrueOnlyForListed()
    {
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1),
            retryOnCodes: new HashSet<int> { 1, 2 });

        Assert.True(options.ShouldRetry(1));
        Assert.True(options.ShouldRetry(2));
        Assert.False(options.ShouldRetry(3));
        Assert.False(options.ShouldRetry(0));
    }

    [Fact]
    public void ShouldRetry_WithStopOnCodes_ReturnsFalseForListed()
    {
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1),
            stopOnCodes: new HashSet<int> { 0, 1 });

        Assert.False(options.ShouldRetry(0));
        Assert.False(options.ShouldRetry(1));
        Assert.True(options.ShouldRetry(2));
        Assert.True(options.ShouldRetry(137));
    }

    [Fact]
    public void ShouldRetry_WithUntilWithout0_ReturnsTrueFor0()
    {
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1),
            stopOnCodes: new HashSet<int> { 1 });

        Assert.True(options.ShouldRetry(0));
        Assert.False(options.ShouldRetry(1));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj`
Expected: Compilation errors — types don't exist yet.

- [ ] **Step 3: Create RetryOutcome enum**

```csharp
// src/Winix.Retry/RetryOutcome.cs
namespace Winix.Retry;

/// <summary>
/// How the retry loop terminated.
/// </summary>
public enum RetryOutcome
{
    /// <summary>A target exit code was reached (default: exit 0).</summary>
    Succeeded,

    /// <summary>All retry attempts exhausted without reaching target.</summary>
    RetriesExhausted,

    /// <summary>Exit code was not in the --on set — stopped early.</summary>
    NotRetryable
}
```

- [ ] **Step 4: Create AttemptInfo**

```csharp
// src/Winix.Retry/AttemptInfo.cs
namespace Winix.Retry;

/// <summary>
/// Information about a single retry attempt, passed to the progress callback.
/// </summary>
public sealed class AttemptInfo
{
    /// <summary>Which attempt just completed (1-indexed).</summary>
    public int Attempt { get; }

    /// <summary>Total max attempts allowed (initial + retries).</summary>
    public int MaxAttempts { get; }

    /// <summary>Exit code from this attempt's child process.</summary>
    public int ExitCode { get; }

    /// <summary>Delay before the next attempt, or null if this is the final attempt or loop is stopping.</summary>
    public TimeSpan? NextDelay { get; }

    /// <summary>Whether this attempt will be followed by another retry.</summary>
    public bool WillRetry { get; }

    /// <summary>Why the loop is stopping (null if <see cref="WillRetry"/> is true).</summary>
    public RetryOutcome? StopReason { get; }

    /// <summary>
    /// Creates a new attempt info record.
    /// </summary>
    public AttemptInfo(int attempt, int maxAttempts, int exitCode,
        TimeSpan? nextDelay, bool willRetry, RetryOutcome? stopReason)
    {
        Attempt = attempt;
        MaxAttempts = maxAttempts;
        ExitCode = exitCode;
        NextDelay = nextDelay;
        WillRetry = willRetry;
        StopReason = stopReason;
    }
}
```

- [ ] **Step 5: Create RetryResult**

```csharp
// src/Winix.Retry/RetryResult.cs
namespace Winix.Retry;

/// <summary>
/// Final result of a retry sequence.
/// </summary>
public sealed class RetryResult
{
    /// <summary>Total number of attempts made (initial run + retries).</summary>
    public int Attempts { get; }

    /// <summary>Maximum attempts that were allowed (MaxRetries + 1).</summary>
    public int MaxAttempts { get; }

    /// <summary>Exit code of the last child process run.</summary>
    public int ChildExitCode { get; }

    /// <summary>How the retry loop terminated.</summary>
    public RetryOutcome Outcome { get; }

    /// <summary>Total wall time including delays between attempts.</summary>
    public TimeSpan TotalTime { get; }

    /// <summary>Actual delay durations between attempts (for JSON reporting).</summary>
    public IReadOnlyList<TimeSpan> Delays { get; }

    /// <summary>
    /// Creates a new retry result.
    /// </summary>
    public RetryResult(int attempts, int maxAttempts, int childExitCode,
        RetryOutcome outcome, TimeSpan totalTime, IReadOnlyList<TimeSpan> delays)
    {
        Attempts = attempts;
        MaxAttempts = maxAttempts;
        ChildExitCode = childExitCode;
        Outcome = outcome;
        TotalTime = totalTime;
        Delays = delays;
    }
}
```

- [ ] **Step 6: Create RetryOptions with validation and ShouldRetry**

```csharp
// src/Winix.Retry/RetryOptions.cs
namespace Winix.Retry;

/// <summary>
/// Configuration for a retry sequence.
/// </summary>
public sealed class RetryOptions
{
    /// <summary>Maximum number of retry attempts (not counting the initial run).</summary>
    public int MaxRetries { get; }

    /// <summary>Base delay before retries.</summary>
    public TimeSpan Delay { get; }

    /// <summary>How the delay scales across attempts.</summary>
    public BackoffStrategy Backoff { get; }

    /// <summary>Whether to add random jitter to delay calculations.</summary>
    public bool Jitter { get; }

    /// <summary>
    /// Exit codes that trigger a retry (--on whitelist). Null means use default/StopOnCodes logic.
    /// </summary>
    public IReadOnlySet<int>? RetryOnCodes { get; }

    /// <summary>
    /// Exit codes that stop retrying (--until targets). Null means use default (stop on 0).
    /// </summary>
    public IReadOnlySet<int>? StopOnCodes { get; }

    /// <summary>
    /// Creates retry options with validation.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">MaxRetries is negative or Delay is negative.</exception>
    /// <exception cref="ArgumentException">Both retryOnCodes and stopOnCodes are specified.</exception>
    public RetryOptions(
        int maxRetries,
        TimeSpan delay,
        BackoffStrategy backoff = BackoffStrategy.Fixed,
        bool jitter = false,
        IReadOnlySet<int>? retryOnCodes = null,
        IReadOnlySet<int>? stopOnCodes = null)
    {
        if (maxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetries), maxRetries,
                "Max retries cannot be negative.");
        }
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay,
                "Delay cannot be negative.");
        }
        if (retryOnCodes != null && stopOnCodes != null)
        {
            throw new ArgumentException(
                "Cannot specify both --on and --until. They are contradictory.");
        }

        MaxRetries = maxRetries;
        Delay = delay;
        Backoff = backoff;
        Jitter = jitter;
        RetryOnCodes = retryOnCodes;
        StopOnCodes = stopOnCodes;
    }

    /// <summary>
    /// Determines whether the given exit code should trigger a retry.
    /// </summary>
    /// <param name="exitCode">The child process exit code.</param>
    /// <returns>True if the exit code warrants another attempt.</returns>
    public bool ShouldRetry(int exitCode)
    {
        if (RetryOnCodes != null)
        {
            // --on mode: retry only if code is in the whitelist. 0 always stops.
            return exitCode != 0 && RetryOnCodes.Contains(exitCode);
        }

        if (StopOnCodes != null)
        {
            // --until mode: stop if code is in the target set, retry everything else.
            return !StopOnCodes.Contains(exitCode);
        }

        // Default: retry on any non-zero (implicit --until 0).
        return exitCode != 0;
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj`
Expected: All RetryOptions and BackoffCalculator tests pass.

- [ ] **Step 8: Commit**

```
git add -A
git commit -m "feat(retry): add RetryOptions, RetryResult, AttemptInfo, RetryOutcome

Includes validation (negative retries/delay, --on+--until conflict)
and ShouldRetry logic for default, --on, and --until modes."
```

---

### Task 5: RetryRunner

The core retry loop. Uses a process-spawning delegate for testability.

**Files:**
- Create: `src/Winix.Retry/RetryRunner.cs`
- Create: `tests/Winix.Retry.Tests/RetryRunnerTests.cs`

- [ ] **Step 1: Write failing tests for RetryRunner**

Use a delegate to inject fake exit codes instead of spawning real processes:

```csharp
// tests/Winix.Retry.Tests/RetryRunnerTests.cs
using Xunit;
using Winix.Retry;

namespace Winix.Retry.Tests;

public class RetryRunnerTests
{
    /// <summary>
    /// Helper: creates a process runner that returns exit codes from the given sequence.
    /// </summary>
    private static Func<string, string[], int> ExitCodeSequence(params int[] codes)
    {
        int index = 0;
        return (cmd, args) =>
        {
            if (index >= codes.Length)
            {
                return codes[codes.Length - 1]; // repeat last code if overrun
            }
            return codes[index++];
        };
    }

    [Fact]
    public void Run_SucceedsOnFirstAttempt_ReturnsSucceeded()
    {
        var runner = new RetryRunner(ExitCodeSequence(0));
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, result.Attempts);
        Assert.Equal(0, result.ChildExitCode);
        Assert.Empty(result.Delays);
    }

    [Fact]
    public void Run_FailsThenSucceeds_ReturnsSucceeded()
    {
        var runner = new RetryRunner(ExitCodeSequence(1, 1, 0));
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.Succeeded, result.Outcome);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(0, result.ChildExitCode);
        Assert.Equal(2, result.Delays.Count);
    }

    [Fact]
    public void Run_AllAttemptsFail_ReturnsExhausted()
    {
        var runner = new RetryRunner(ExitCodeSequence(1, 1, 1, 1));
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.RetriesExhausted, result.Outcome);
        Assert.Equal(4, result.Attempts);
        Assert.Equal(4, result.MaxAttempts);
        Assert.Equal(1, result.ChildExitCode);
        Assert.Equal(3, result.Delays.Count);
    }

    [Fact]
    public void Run_ZeroRetries_RunsOnceOnly()
    {
        var runner = new RetryRunner(ExitCodeSequence(1));
        var options = new RetryOptions(maxRetries: 0, delay: TimeSpan.Zero);

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.RetriesExhausted, result.Outcome);
        Assert.Equal(1, result.Attempts);
        Assert.Empty(result.Delays);
    }

    [Fact]
    public void Run_WithOnCodes_StopsOnNonRetryableCode()
    {
        var runner = new RetryRunner(ExitCodeSequence(1, 137));
        var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.Zero,
            retryOnCodes: new HashSet<int> { 1 });

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.NotRetryable, result.Outcome);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(137, result.ChildExitCode);
    }

    [Fact]
    public void Run_WithUntilCodes_StopsOnTargetCode()
    {
        var runner = new RetryRunner(ExitCodeSequence(0, 0, 1));
        var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.Zero,
            stopOnCodes: new HashSet<int> { 1 });

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.Succeeded, result.Outcome);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(1, result.ChildExitCode);
    }

    [Fact]
    public void Run_WithUntilWithoutZero_RetriesOnZero()
    {
        var runner = new RetryRunner(ExitCodeSequence(0, 0, 0, 1));
        var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.Zero,
            stopOnCodes: new HashSet<int> { 1 });

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(4, result.Attempts);
        Assert.Equal(1, result.ChildExitCode);
        Assert.Equal(RetryOutcome.Succeeded, result.Outcome);
    }

    [Fact]
    public void Run_InvokesCallback_ForEachAttempt()
    {
        var runner = new RetryRunner(ExitCodeSequence(1, 0));
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);
        var callbacks = new List<AttemptInfo>();

        var result = runner.Run("cmd", Array.Empty<string>(), options,
            onAttempt: info => callbacks.Add(info));

        Assert.Equal(2, callbacks.Count);

        // First attempt: failed, will retry
        Assert.Equal(1, callbacks[0].Attempt);
        Assert.Equal(1, callbacks[0].ExitCode);
        Assert.True(callbacks[0].WillRetry);
        Assert.NotNull(callbacks[0].NextDelay);

        // Second attempt: succeeded, no retry
        Assert.Equal(2, callbacks[1].Attempt);
        Assert.Equal(0, callbacks[1].ExitCode);
        Assert.False(callbacks[1].WillRetry);
        Assert.Equal(RetryOutcome.Succeeded, callbacks[1].StopReason);
    }

    [Fact]
    public void Run_Cancellation_BreaksLoop()
    {
        int callCount = 0;
        var cts = new CancellationTokenSource();

        var runner = new RetryRunner((cmd, args) =>
        {
            callCount++;
            if (callCount == 2) { cts.Cancel(); }
            return 1;
        });

        var options = new RetryOptions(maxRetries: 10, delay: TimeSpan.Zero);

        var result = runner.Run("cmd", Array.Empty<string>(), options,
            cancellationToken: cts.Token);

        // Should have stopped after cancellation, not run all 11 attempts
        Assert.True(result.Attempts <= 3);
        Assert.Equal(1, result.ChildExitCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj`
Expected: Compilation error — `RetryRunner` doesn't exist yet.

- [ ] **Step 3: Implement RetryRunner**

```csharp
// src/Winix.Retry/RetryRunner.cs
using System.Diagnostics;

namespace Winix.Retry;

/// <summary>
/// Executes a command with automatic retries according to <see cref="RetryOptions"/>.
/// </summary>
public sealed class RetryRunner
{
    private readonly Func<string, string[], int> _runProcess;

    /// <summary>
    /// Creates a runner that uses the given delegate to execute the command.
    /// The delegate receives (command, arguments) and returns the exit code.
    /// </summary>
    /// <param name="runProcess">
    /// Process execution delegate. For production use, pass a delegate that spawns
    /// a real child process. For testing, pass a fake that returns scripted exit codes.
    /// </param>
    public RetryRunner(Func<string, string[], int> runProcess)
    {
        _runProcess = runProcess;
    }

    /// <summary>
    /// Runs the command with retries.
    /// </summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="arguments">Arguments for the command.</param>
    /// <param name="options">Retry configuration.</param>
    /// <param name="onAttempt">Optional callback invoked after each attempt.</param>
    /// <param name="delayAction">
    /// Optional delay delegate (for testing). Defaults to <see cref="Thread.Sleep(TimeSpan)"/>.
    /// </param>
    /// <param name="cancellationToken">Token to abort the retry loop between attempts.</param>
    /// <returns>The final result describing how the loop terminated.</returns>
    public RetryResult Run(
        string command,
        string[] arguments,
        RetryOptions options,
        Action<AttemptInfo>? onAttempt = null,
        Action<TimeSpan>? delayAction = null,
        CancellationToken cancellationToken = default)
    {
        delayAction ??= Thread.Sleep;
        Random? random = options.Jitter ? new Random() : null;

        int maxAttempts = options.MaxRetries + 1;
        var delays = new List<TimeSpan>();
        var stopwatch = Stopwatch.StartNew();
        int lastExitCode = 0;
        int attemptNumber = 0;

        for (int i = 0; i < maxAttempts; i++)
        {
            if (i > 0 && cancellationToken.IsCancellationRequested)
            {
                break;
            }

            attemptNumber = i + 1;
            lastExitCode = _runProcess(command, arguments);

            bool shouldRetry = options.ShouldRetry(lastExitCode);
            bool hasRetriesLeft = attemptNumber < maxAttempts;
            bool willRetry = shouldRetry && hasRetriesLeft && !cancellationToken.IsCancellationRequested;

            TimeSpan? nextDelay = null;
            RetryOutcome? stopReason = null;

            if (willRetry)
            {
                nextDelay = BackoffCalculator.Calculate(
                    options.Delay, attemptNumber, options.Backoff, options.Jitter, random);
            }
            else if (!shouldRetry)
            {
                // Determine the specific stop reason
                if (options.RetryOnCodes != null && lastExitCode != 0)
                {
                    stopReason = RetryOutcome.NotRetryable;
                }
                else
                {
                    stopReason = RetryOutcome.Succeeded;
                }
            }
            else if (!hasRetriesLeft)
            {
                stopReason = RetryOutcome.RetriesExhausted;
            }

            onAttempt?.Invoke(new AttemptInfo(
                attemptNumber, maxAttempts, lastExitCode,
                nextDelay, willRetry, stopReason));

            if (!willRetry)
            {
                break;
            }

            delays.Add(nextDelay!.Value);
            delayAction(nextDelay!.Value);
        }

        stopwatch.Stop();

        RetryOutcome outcome;
        if (!options.ShouldRetry(lastExitCode))
        {
            if (options.RetryOnCodes != null && lastExitCode != 0)
            {
                outcome = RetryOutcome.NotRetryable;
            }
            else
            {
                outcome = RetryOutcome.Succeeded;
            }
        }
        else
        {
            outcome = RetryOutcome.RetriesExhausted;
        }

        return new RetryResult(
            attemptNumber, maxAttempts, lastExitCode,
            outcome, stopwatch.Elapsed, delays);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj`
Expected: All RetryRunner tests pass.

- [ ] **Step 5: Commit**

```
git add -A
git commit -m "feat(retry): add RetryRunner with exit-code filtering and cancellation

Testable via injected process-execution delegate. Supports --on
whitelist, --until targets, backoff, jitter, and CancellationToken."
```

---

### Task 6: Formatting (human + JSON)

Progress lines and JSON output. Pure string formatting, no I/O.

**Files:**
- Create: `src/Winix.Retry/Formatting.cs`
- Create: `tests/Winix.Retry.Tests/FormattingTests.cs`

- [ ] **Step 1: Write failing tests for Formatting**

```csharp
// tests/Winix.Retry.Tests/FormattingTests.cs
using Xunit;
using Winix.Retry;

namespace Winix.Retry.Tests;

public class FormatAttemptTests
{
    [Fact]
    public void FormatAttempt_FailedWillRetry_ShowsRetryMessage()
    {
        var info = new AttemptInfo(1, 4, exitCode: 1,
            nextDelay: TimeSpan.FromSeconds(2), willRetry: true, stopReason: null);

        string line = Formatting.FormatAttempt(info, useColor: false);

        Assert.Contains("attempt 1/4", line);
        Assert.Contains("failed", line);
        Assert.Contains("exit 1", line);
        Assert.Contains("retrying in 2", line);
    }

    [Fact]
    public void FormatAttempt_Succeeded_ShowsSuccessMessage()
    {
        var info = new AttemptInfo(3, 4, exitCode: 0,
            nextDelay: null, willRetry: false, stopReason: RetryOutcome.Succeeded);

        string line = Formatting.FormatAttempt(info, useColor: false);

        Assert.Contains("attempt 3/4", line);
        Assert.Contains("succeeded", line);
        Assert.Contains("3 attempts", line);
    }

    [Fact]
    public void FormatAttempt_Exhausted_ShowsNoRetriesMessage()
    {
        var info = new AttemptInfo(4, 4, exitCode: 1,
            nextDelay: null, willRetry: false, stopReason: RetryOutcome.RetriesExhausted);

        string line = Formatting.FormatAttempt(info, useColor: false);

        Assert.Contains("attempt 4/4", line);
        Assert.Contains("failed", line);
        Assert.Contains("no retries remaining", line);
    }

    [Fact]
    public void FormatAttempt_NotRetryable_ShowsStoppingMessage()
    {
        var info = new AttemptInfo(1, 4, exitCode: 137,
            nextDelay: null, willRetry: false, stopReason: RetryOutcome.NotRetryable);

        string line = Formatting.FormatAttempt(info, useColor: false);

        Assert.Contains("attempt 1/4", line);
        Assert.Contains("exit 137", line);
        Assert.Contains("not retryable", line);
    }

    [Fact]
    public void FormatAttempt_UntilTargetHit_ShowsMatchedMessage()
    {
        var info = new AttemptInfo(2, 4, exitCode: 1,
            nextDelay: null, willRetry: false, stopReason: RetryOutcome.Succeeded);

        // Exit code 1 succeeded = --until target hit (non-zero success)
        string line = Formatting.FormatAttempt(info, useColor: false);

        Assert.Contains("attempt 2/4", line);
        Assert.Contains("matched target", line);
        Assert.Contains("exit 1", line);
    }

    [Fact]
    public void FormatAttempt_WithColor_ContainsAnsiSequences()
    {
        var info = new AttemptInfo(1, 4, exitCode: 1,
            nextDelay: TimeSpan.FromSeconds(2), willRetry: true, stopReason: null);

        string line = Formatting.FormatAttempt(info, useColor: true);

        Assert.Contains("\x1b[", line);
    }

    [Fact]
    public void FormatAttempt_SubSecondDelay_ShowsMilliseconds()
    {
        var info = new AttemptInfo(1, 4, exitCode: 1,
            nextDelay: TimeSpan.FromMilliseconds(500), willRetry: true, stopReason: null);

        string line = Formatting.FormatAttempt(info, useColor: false);

        Assert.Contains("500ms", line);
    }
}

public class FormatJsonTests
{
    [Fact]
    public void FormatJson_Succeeded_ContainsExpectedFields()
    {
        var result = new RetryResult(
            attempts: 3, maxAttempts: 4, childExitCode: 0,
            outcome: RetryOutcome.Succeeded,
            totalTime: TimeSpan.FromSeconds(6.5),
            delays: new List<TimeSpan> { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2) });

        string json = Formatting.FormatJson(result, "retry", "0.3.0");

        Assert.Contains("\"tool\":\"retry\"", json);
        Assert.Contains("\"version\":\"0.3.0\"", json);
        Assert.Contains("\"exit_code\":0", json);
        Assert.Contains("\"exit_reason\":\"succeeded\"", json);
        Assert.Contains("\"child_exit_code\":0", json);
        Assert.Contains("\"attempts\":3", json);
        Assert.Contains("\"max_attempts\":4", json);
        Assert.Contains("\"total_seconds\":", json);
        Assert.Contains("\"delays_seconds\":[", json);
    }

    [Fact]
    public void FormatJson_Exhausted_ShowsCorrectReason()
    {
        var result = new RetryResult(
            attempts: 4, maxAttempts: 4, childExitCode: 1,
            outcome: RetryOutcome.RetriesExhausted,
            totalTime: TimeSpan.FromSeconds(12),
            delays: new List<TimeSpan> { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2) });

        string json = Formatting.FormatJson(result, "retry", "0.3.0");

        Assert.Contains("\"exit_code\":1", json);
        Assert.Contains("\"exit_reason\":\"retries_exhausted\"", json);
    }

    [Fact]
    public void FormatJson_NotRetryable_ShowsCorrectReason()
    {
        var result = new RetryResult(
            attempts: 2, maxAttempts: 4, childExitCode: 137,
            outcome: RetryOutcome.NotRetryable,
            totalTime: TimeSpan.FromSeconds(3),
            delays: new List<TimeSpan> { TimeSpan.FromSeconds(1) });

        string json = Formatting.FormatJson(result, "retry", "0.3.0");

        Assert.Contains("\"exit_code\":137", json);
        Assert.Contains("\"exit_reason\":\"not_retryable\"", json);
    }

    [Fact]
    public void FormatJsonError_ContainsExpectedFields()
    {
        string json = Formatting.FormatJsonError(127, "command_not_found", "retry", "0.3.0");

        Assert.Contains("\"tool\":\"retry\"", json);
        Assert.Contains("\"exit_code\":127", json);
        Assert.Contains("\"exit_reason\":\"command_not_found\"", json);
        Assert.Contains("\"child_exit_code\":null", json);
        Assert.Contains("\"attempts\":0", json);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj`
Expected: Compilation error — `Formatting` class doesn't exist yet.

- [ ] **Step 3: Implement Formatting**

```csharp
// src/Winix.Retry/Formatting.cs
using System.Globalization;
using Yort.ShellKit;

namespace Winix.Retry;

/// <summary>
/// Formatting helpers for retry progress lines and JSON output.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Formats a progress line for a single attempt.
    /// </summary>
    /// <param name="info">The attempt info.</param>
    /// <param name="useColor">Whether to include ANSI colour escapes.</param>
    public static string FormatAttempt(AttemptInfo info, bool useColor)
    {
        string dim = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);
        string red = AnsiColor.Red(useColor);
        string green = AnsiColor.Green(useColor);
        string yellow = AnsiColor.Yellow(useColor);

        string prefix = $"retry: {dim}attempt {info.Attempt}/{info.MaxAttempts}{reset}";

        if (info.WillRetry)
        {
            string delayStr = FormatDelay(info.NextDelay!.Value);
            return $"{prefix} {red}failed{reset} (exit {info.ExitCode}), retrying in {yellow}{delayStr}{reset}...";
        }

        if (info.StopReason == RetryOutcome.Succeeded)
        {
            if (info.ExitCode == 0)
            {
                return $"{prefix} {green}succeeded{reset} (exit 0) after {info.Attempt} attempts";
            }
            // --until target hit with non-zero code
            return $"{prefix} {green}matched target{reset} (exit {info.ExitCode}) after {info.Attempt} attempts";
        }

        if (info.StopReason == RetryOutcome.NotRetryable)
        {
            return $"{prefix} {red}failed{reset} (exit {info.ExitCode}), not retryable — stopping";
        }

        // RetriesExhausted
        return $"{prefix} {red}failed{reset} (exit {info.ExitCode}), no retries remaining";
    }

    /// <summary>
    /// Formats a delay duration as a human-friendly string (e.g. "2s", "500ms", "1m 30s").
    /// </summary>
    internal static string FormatDelay(TimeSpan delay)
    {
        if (delay.TotalMilliseconds < 1000)
        {
            return $"{(int)delay.TotalMilliseconds}ms";
        }
        return DisplayFormat.FormatDuration(delay);
    }

    /// <summary>
    /// Formats the final JSON summary after all attempts.
    /// </summary>
    public static string FormatJson(RetryResult result, string toolName, string version)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", result.ChildExitCode);
            writer.WriteString("exit_reason", OutcomeToReason(result.Outcome));
            writer.WriteNumber("child_exit_code", result.ChildExitCode);
            writer.WriteNumber("attempts", result.Attempts);
            writer.WriteNumber("max_attempts", result.MaxAttempts);
            JsonHelper.WriteFixedDecimal(writer, "total_seconds", result.TotalTime.TotalSeconds, 3);

            writer.WriteStartArray("delays_seconds");
            foreach (var delay in result.Delays)
            {
                writer.WriteRawValue(delay.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture));
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Formats a JSON error for retry's own failures (command not found, usage error, etc.).
    /// </summary>
    public static string FormatJsonError(int exitCode, string exitReason, string toolName, string version)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            writer.WriteNull("child_exit_code");
            writer.WriteNumber("attempts", 0);
            writer.WriteNumber("max_attempts", 0);
            JsonHelper.WriteFixedDecimal(writer, "total_seconds", 0.0, 3);
            writer.WriteStartArray("delays_seconds");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }

    private static string OutcomeToReason(RetryOutcome outcome)
    {
        return outcome switch
        {
            RetryOutcome.Succeeded => "succeeded",
            RetryOutcome.RetriesExhausted => "retries_exhausted",
            RetryOutcome.NotRetryable => "not_retryable",
            _ => "unknown"
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj`
Expected: All Formatting tests pass.

- [ ] **Step 5: Commit**

```
git add -A
git commit -m "feat(retry): add Formatting for progress lines and JSON output"
```

---

### Task 7: Console app (Program.cs)

Wire up arg parsing, process execution, output.

**Files:**
- Modify: `src/retry/Program.cs`

- [ ] **Step 1: Implement Program.cs**

Replace the stub with the full implementation:

```csharp
// src/retry/Program.cs
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Winix.Retry;
using Yort.ShellKit;

namespace Retry;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        string version = GetVersion();

        var parser = new CommandLineParser("retry", version)
            .Description("Run a command with automatic retries on failure.")
            .StandardFlags()
            .Option("--times", "-n", "N", "3", "Max retry attempts (not counting initial run)")
            .Option("--delay", "-d", "DURATION", "1s", "Delay before retries (e.g. 500ms, 2s, 1m)")
            .Option("--backoff", "-b", "STRATEGY", "fixed", "Backoff strategy: fixed, linear, exp")
            .Flag("--jitter", null, "Add random jitter to delay (50-100% of calculated value)")
            .Option("--on", null, "CODES", null, "Retry only on these exit codes (comma-separated)")
            .Option("--until", null, "CODES", null, "Stop when exit code matches (comma-separated)")
            .Flag("--stdout", null, "Write summary to stdout instead of stderr")
            .CommandMode()
            .ExitCodes(
                (0, "Child process exit code (pass-through)"),
                (ExitCode.UsageError, "Bad retry arguments or no command specified"),
                (ExitCode.NotExecutable, "Command not executable (permission denied)"),
                (ExitCode.NotFound, "Command not found"))
            .Platform("cross-platform",
                replaces: new[] { "bash retry loops", "PowerShell retry loops" },
                valueOnWindows: "No native retry command; manual loops are verbose and non-portable",
                valueOnUnix: "Replaces ad-hoc until/while+sleep loops with backoff and exit-code control")
            .StdinDescription("Inherited by child process")
            .StdoutDescription("Child process stdout passes through unmodified")
            .StderrDescription("Retry progress and summary. JSON with --json. Child stderr also passes through.")
            .Example("retry dotnet test", "Retry flaky tests up to 3 times")
            .Example("retry --times 5 --delay 2s dotnet test", "5 retries with 2s fixed delay")
            .Example("retry --times 5 --delay 1s --backoff exp --jitter curl -f http://api/health", "API health check with exponential backoff")
            .Example("retry --until 0 --delay 5s docker ps", "Poll until Docker daemon is ready")
            .Example("retry --on 1,2 --times 3 make build", "Retry only on exit codes 1 or 2")
            .ComposesWith("timeit", "timeit retry make test", "Time the entire retry sequence")
            .ComposesWith("peep", "peep -- retry --times 2 make test", "File-watch with auto-retry on failure")
            .JsonField("tool", "string", "Tool name (\"retry\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("exit_code", "int", "Child exit code (pass-through)")
            .JsonField("exit_reason", "string", "succeeded|retries_exhausted|not_retryable")
            .JsonField("child_exit_code", "int", "Child process exit code")
            .JsonField("attempts", "int", "Total attempts made")
            .JsonField("max_attempts", "int", "Max attempts allowed")
            .JsonField("total_seconds", "float", "Total wall time including delays")
            .JsonField("delays_seconds", "float[]", "Actual delay durations between attempts");

        var result = parser.Parse(args);
        if (result.IsHandled) return result.ExitCode;
        if (result.HasErrors) return result.WriteErrors(Console.Error);

        bool jsonOutput = result.Has("--json");
        bool useStdout = result.Has("--stdout");
        bool useColor = result.ResolveColor(checkStdErr: !useStdout);
        TextWriter writer = useStdout ? Console.Out : Console.Error;

        if (result.Command.Length == 0)
        {
            return result.WriteError("no command specified. Run 'retry --help' for usage.", writer);
        }

        // Parse --times
        if (!int.TryParse(result.Value("--times"), out int maxRetries) || maxRetries < 0)
        {
            return result.WriteError("--times must be a non-negative integer.", writer);
        }

        // Parse --delay
        string delayStr = result.Value("--delay") ?? "1s";
        if (!DurationParser.TryParse(delayStr, out TimeSpan delay))
        {
            return result.WriteError($"invalid duration: '{delayStr}'. Expected e.g. 500ms, 2s, 1m.", writer);
        }

        // Parse --backoff
        string backoffStr = result.Value("--backoff") ?? "fixed";
        BackoffStrategy backoff;
        if (string.Equals(backoffStr, "fixed", StringComparison.OrdinalIgnoreCase))
        {
            backoff = BackoffStrategy.Fixed;
        }
        else if (string.Equals(backoffStr, "linear", StringComparison.OrdinalIgnoreCase))
        {
            backoff = BackoffStrategy.Linear;
        }
        else if (string.Equals(backoffStr, "exp", StringComparison.OrdinalIgnoreCase))
        {
            backoff = BackoffStrategy.Exponential;
        }
        else
        {
            return result.WriteError($"unknown backoff strategy: '{backoffStr}'. Expected fixed, linear, or exp.", writer);
        }

        bool jitter = result.Has("--jitter");

        // Parse --on / --until
        HashSet<int>? retryOnCodes = ParseCodeList(result.Value("--on"));
        HashSet<int>? stopOnCodes = ParseCodeList(result.Value("--until"));

        if (retryOnCodes != null && stopOnCodes != null)
        {
            return result.WriteError("--on and --until cannot be used together.", writer);
        }

        RetryOptions options;
        try
        {
            options = new RetryOptions(maxRetries, delay, backoff, jitter, retryOnCodes, stopOnCodes);
        }
        catch (ArgumentException ex)
        {
            return result.WriteError(ex.Message, writer);
        }

        string command = result.Command[0];
        string[] commandArgs = result.Command.Skip(1).ToArray();

        // Build the real process runner
        var runner = new RetryRunner((cmd, cmdArgs) => RunProcess(cmd, cmdArgs));

        RetryResult retryResult;
        try
        {
            retryResult = runner.Run(command, commandArgs, options,
                onAttempt: info =>
                {
                    if (!jsonOutput)
                    {
                        writer.WriteLine(Formatting.FormatAttempt(info, useColor));
                    }
                });
        }
        catch (CommandNotFoundException ex)
        {
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.NotFound, "command_not_found", "retry", version));
            }
            else
            {
                Console.Error.WriteLine($"retry: {ex.Message}");
            }
            return ExitCode.NotFound;
        }
        catch (CommandNotExecutableException ex)
        {
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.NotExecutable, "command_not_executable", "retry", version));
            }
            else
            {
                Console.Error.WriteLine($"retry: {ex.Message}");
            }
            return ExitCode.NotExecutable;
        }

        if (jsonOutput)
        {
            writer.WriteLine(Formatting.FormatJson(retryResult, "retry", version));
        }

        return retryResult.ChildExitCode;
    }

    private static int RunProcess(string command, string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
        };

        foreach (string arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new CommandNotFoundException(command);
        }
        catch (Win32Exception ex)
        {
            if (ex.NativeErrorCode == 5 || ex.NativeErrorCode == 13)
            {
                throw new CommandNotExecutableException(command);
            }
            if (ex.NativeErrorCode == 2 || ex.NativeErrorCode == 3)
            {
                throw new CommandNotFoundException(command);
            }
            throw new InvalidOperationException($"failed to start '{command}': {ex.Message}", ex);
        }

        try
        {
            process.WaitForExit();
            return process.ExitCode;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static HashSet<int>? ParseCodeList(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var codes = new HashSet<int>();
        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out int code))
            {
                codes.Add(code);
            }
        }
        return codes.Count > 0 ? codes : null;
    }

    private static string GetVersion()
    {
        return typeof(RetryResult).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
```

- [ ] **Step 2: Verify the solution builds**

Run: `dotnet build Winix.sln`
Expected: Build succeeds with no errors or warnings.

- [ ] **Step 3: Smoke-test manually**

Run a few quick commands to verify basic operation:

```bash
# Should retry 3 times (default) and fail
dotnet run --project src/retry -- false

# Should succeed immediately
dotnet run --project src/retry -- true

# Should retry with delay
dotnet run --project src/retry -- --times 2 --delay 500ms false

# JSON output
dotnet run --project src/retry -- --json --times 1 false
```

- [ ] **Step 4: Commit**

```
git add -A
git commit -m "feat(retry): implement console app with full arg parsing

Wires up CommandLineParser, process execution, --on/--until exit-code
filtering, backoff strategies, jitter, JSON output, and --describe."
```

---

### Task 8: README, man page source, AI guide, llms.txt, scoop manifest

Documentation and discoverability. Follow existing patterns exactly.

**Files:**
- Create: `src/retry/README.md`
- Create: `src/retry/retry.1.md` (man page pandoc source)
- Create: `docs/ai/retry.md`
- Modify: `llms.txt`
- Create: `bucket/retry.json`
- Modify: `bucket/winix.json` (add retry to bin array)

- [ ] **Step 1: Create README.md**

Follow the pattern from `src/timeit/README.md`. Include: description, install (scoop, dotnet tool, from source), usage examples, options table, exit codes, colour section, composability examples.

- [ ] **Step 2: Create retry.1.md man page source**

Follow the pattern from `src/timeit/timeit.1.md`. Pandoc-flavoured markdown with metadata header, NAME, SYNOPSIS, DESCRIPTION, OPTIONS, EXIT CODES, EXAMPLES, SEE ALSO sections.

- [ ] **Step 3: Add man page content item to retry.csproj**

Add to `src/retry/retry.csproj`:

```xml
<ItemGroup>
  <Content Include="man\man1\retry.1" CopyToPublishDirectory="PreserveNewest" Link="share\man\man1\retry.1" />
</ItemGroup>
```

Note: The actual `.1` groff file is generated from `retry.1.md` via pandoc. If pandoc is available, run: `pandoc src/retry/retry.1.md -s -t man -o src/retry/man/man1/retry.1`. If not, create the `man/man1/` directory and note that the groff file needs generating.

- [ ] **Step 4: Create AI guide**

Create `docs/ai/retry.md` following the pattern from `docs/ai/timeit.md`. Sections: What This Tool Does, Platform Story, When to Use This, Common Patterns, Exit Codes, JSON Output, Composability, What This Tool Is Not For.

- [ ] **Step 5: Update llms.txt**

Add retry to the tools list in `llms.txt`, maintaining alphabetical order within the list:

```
- [retry](docs/ai/retry.md): Run a command with automatic retries, backoff, and exit-code filtering. Replaces ad-hoc shell retry loops.
```

- [ ] **Step 6: Create scoop manifest**

Create `bucket/retry.json` following the pattern of existing manifests (e.g. `bucket/timeit.json`). Use `Winix.Retry` as the package identifier.

- [ ] **Step 7: Update winix.json bin array**

Add `"retry.exe"` to the `bin` array in `bucket/winix.json`.

- [ ] **Step 8: Run full solution build and tests**

Run: `dotnet build Winix.sln`
Run: `dotnet test Winix.sln`
Expected: All tests pass, no build warnings.

- [ ] **Step 9: Commit**

```
git add -A
git commit -m "docs(retry): add README, man page, AI guide, llms.txt, scoop manifest"
```

---

### Task 9: Release pipeline integration

Add retry to the CI/CD workflows.

**Files:**
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Read the current release.yml**

Read `.github/workflows/release.yml` and identify where each tool is listed. There will be sections for:
1. AOT publish steps (per tool per RID)
2. Individual zip creation
3. Combined winix zip
4. NuGet pack
5. GitHub release upload
6. Scoop manifest hash updates
7. Winget manifest generation

- [ ] **Step 2: Add retry to all pipeline sections**

Follow the exact pattern used by the most recently added tool (nc). Add `retry` in every section where nc appears. The binary name is `retry`, the publish project is `src/retry/retry.csproj`, the NuGet package ID is `Winix.Retry`.

- [ ] **Step 3: Verify the workflow YAML is valid**

If `actionlint` or `yq` is available, validate the YAML syntax. Otherwise, visually check indentation and structure.

- [ ] **Step 4: Commit**

```
git add -A
git commit -m "ci(retry): integrate retry into release pipeline"
```

---

### Task 10: Final validation

Run the full test suite and verify everything works end-to-end.

- [ ] **Step 1: Run full solution tests**

Run: `dotnet test Winix.sln`
Expected: All tests pass (previous 1038 + new retry tests).

- [ ] **Step 2: Verify AOT publish works**

Run: `dotnet publish src/retry/retry.csproj -c Release -r win-x64`
Expected: Produces native binary with no AOT warnings.

- [ ] **Step 3: Smoke-test the native binary**

Test the published binary directly:

```bash
# Succeed on first try
./src/retry/bin/Release/net10.0/win-x64/publish/retry.exe true

# Fail and retry
./src/retry/bin/Release/net10.0/win-x64/publish/retry.exe --times 2 --delay 200ms false

# JSON output
./src/retry/bin/Release/net10.0/win-x64/publish/retry.exe --json --times 1 false

# Help
./src/retry/bin/Release/net10.0/win-x64/publish/retry.exe --help

# Describe
./src/retry/bin/Release/net10.0/win-x64/publish/retry.exe --describe
```

- [ ] **Step 4: Commit any fixes if needed**

Only if issues were found in steps 1-3.
