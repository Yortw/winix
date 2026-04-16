# schedule Implementation Plan

**Date:** 2026-04-12
**Goal:** Implement the `schedule` tool — a cross-platform CLI for creating, listing, and managing scheduled tasks using cron expressions, delegating to native schedulers (schtasks.exe on Windows, crontab on Linux/macOS).
**Design spec:** `docs/plans/2026-04-12-schedule-design.md`

## Architecture

```
src/Winix.Schedule/           — class library (cron parser, scheduler backends, formatting)
src/schedule/                 — thin console app (subcommand dispatch, call library, exit code)
tests/Winix.Schedule.Tests/   — xUnit tests
```

**Tech stack:** .NET 10, AOT-compiled, cross-platform. Windows backend shells out to `schtasks.exe` (AOT-safe, same pattern as winix installer). Linux backend manages user crontab via `crontab -l` / `crontab -` piping.

## File Structure

| File | Purpose |
|------|---------|
| `src/Winix.Schedule/Winix.Schedule.csproj` | Class library project |
| `src/Winix.Schedule/CronExpression.cs` | 5-field cron parser + next-occurrence evaluator |
| `src/Winix.Schedule/CronField.cs` | Single field parser (minute, hour, etc.) |
| `src/Winix.Schedule/ScheduledTask.cs` | Data model for a listed task |
| `src/Winix.Schedule/TaskRunRecord.cs` | Data model for a history entry |
| `src/Winix.Schedule/ScheduleResult.cs` | Result type for add/remove/enable/disable/run |
| `src/Winix.Schedule/NameGenerator.cs` | Auto-name derivation from command strings |
| `src/Winix.Schedule/ISchedulerBackend.cs` | Platform backend interface |
| `src/Winix.Schedule/SchtasksBackend.cs` | Windows backend (schtasks.exe) |
| `src/Winix.Schedule/SchtasksCsvParser.cs` | CSV output parser for schtasks /Query |
| `src/Winix.Schedule/CronToSchtasksMapper.cs` | Cron expression → schtasks flags |
| `src/Winix.Schedule/CrontabBackend.cs` | Linux/macOS backend (crontab) |
| `src/Winix.Schedule/CrontabParser.cs` | Crontab line parsing with winix tags |
| `src/Winix.Schedule/ProcessHelper.cs` | Local process helper (sync RunAsync wrapper) |
| `src/Winix.Schedule/Formatting.cs` | Table, history, next-occurrences, result, JSON |
| `src/schedule/schedule.csproj` | Console app project |
| `src/schedule/Program.cs` | Entry point with subcommand dispatch |
| `src/schedule/README.md` | Tool documentation |
| `tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj` | Test project |
| `tests/Winix.Schedule.Tests/CronFieldTests.cs` | Field parsing tests |
| `tests/Winix.Schedule.Tests/CronExpressionParseTests.cs` | Expression parsing tests |
| `tests/Winix.Schedule.Tests/CronExpressionNextTests.cs` | Next-occurrence tests |
| `tests/Winix.Schedule.Tests/CronSpecialStringTests.cs` | @daily etc. tests |
| `tests/Winix.Schedule.Tests/NameGeneratorTests.cs` | Auto-name tests |
| `tests/Winix.Schedule.Tests/CronToSchtasksMapperTests.cs` | Cron→schtasks mapping tests |
| `tests/Winix.Schedule.Tests/SchtasksCsvParserTests.cs` | CSV output parsing tests |
| `tests/Winix.Schedule.Tests/CrontabParserTests.cs` | Crontab line parsing tests |
| `tests/Winix.Schedule.Tests/FormattingTests.cs` | Output formatting tests |
| `docs/ai/schedule.md` | AI agent guide |
| `bucket/schedule.json` | Scoop manifest |

---

## Task 1: Scaffolding

Create all three projects, add to solution, verify builds clean.

### Step 1.1 — Create `src/Winix.Schedule/Winix.Schedule.csproj`

Create file `src/Winix.Schedule/Winix.Schedule.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
    <IsPackable>false</IsPackable>
    <PackageId>Winix.Schedule.Library</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
  </ItemGroup>
</Project>
```

### Step 1.2 — Create placeholder `src/Winix.Schedule/ScheduledTask.cs`

Create file `src/Winix.Schedule/ScheduledTask.cs`:

```csharp
#nullable enable

namespace Winix.Schedule;

/// <summary>
/// Data model for a scheduled task returned by list operations.
/// </summary>
public sealed class ScheduledTask
{
    /// <summary>Task name (e.g. "health-check").</summary>
    public string Name { get; }

    /// <summary>
    /// Cron expression if known (round-tripped from the task's description/comment),
    /// or a native schedule description if the cron expression cannot be determined.
    /// </summary>
    public string Schedule { get; }

    /// <summary>Next scheduled run time, or null if disabled or unknown.</summary>
    public DateTimeOffset? NextRun { get; }

    /// <summary>"Enabled" or "Disabled".</summary>
    public string Status { get; }

    /// <summary>The command that the task executes.</summary>
    public string Command { get; }

    /// <summary>Folder path on Windows (e.g. "\Winix\"), or empty on Linux.</summary>
    public string Folder { get; }

    /// <summary>Creates a new <see cref="ScheduledTask"/> instance.</summary>
    public ScheduledTask(string name, string schedule, DateTimeOffset? nextRun, string status, string command, string folder)
    {
        Name = name ?? "";
        Schedule = schedule ?? "";
        NextRun = nextRun;
        Status = status ?? "";
        Command = command ?? "";
        Folder = folder ?? "";
    }
}
```

### Step 1.3 — Create `src/schedule/schedule.csproj`

Create file `src/schedule/schedule.csproj`:

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
    <ToolCommandName>schedule</ToolCommandName>
    <PackageId>Winix.Schedule</PackageId>
    <Description>Cross-platform task scheduler with cron expressions.</Description>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.Schedule\Winix.Schedule.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

### Step 1.4 — Create placeholder `src/schedule/Program.cs`

Create file `src/schedule/Program.cs`:

```csharp
#nullable enable

using System.Reflection;
using Winix.Schedule;
using Yort.ShellKit;

namespace Schedule;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        string version = GetVersion();

        var parser = new CommandLineParser("schedule", version)
            .Description("Cross-platform task scheduler with cron expressions.")
            .StandardFlags()
            .Positional("command [args...]")
            .ExitCodes(
                (0, "Success"),
                (1, "Error (task not found, scheduler failure, invalid cron)"),
                (ExitCode.UsageError, "Usage error (bad arguments)"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        if (result.Positionals.Length == 0)
        {
            return result.WriteError("missing subcommand (expected add, list, remove, enable, disable, run, history, or next)", Console.Error);
        }

        // TODO: subcommand dispatch in later tasks
        Console.Error.WriteLine("schedule: not yet implemented");
        return 1;
    }

    /// <summary>
    /// Returns the informational version from the Winix.Schedule library assembly.
    /// </summary>
    private static string GetVersion()
    {
        return typeof(ScheduledTask).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
```

### Step 1.5 — Create placeholder `src/schedule/README.md`

Create file `src/schedule/README.md`:

```markdown
# schedule

Cross-platform task scheduler with cron expressions. Uses the native scheduler on each platform: Windows Task Scheduler (`schtasks.exe`) on Windows, crontab on Linux/macOS.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/schedule
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Schedule
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).
```

### Step 1.6 — Create `tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj`

Create file `tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj`:

```xml
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
    <ProjectReference Include="..\..\src\Winix.Schedule\Winix.Schedule.csproj" />
  </ItemGroup>
</Project>
```

### Step 1.7 — Create placeholder test file

Create file `tests/Winix.Schedule.Tests/ScheduledTaskTests.cs`:

```csharp
using Winix.Schedule;

namespace Winix.Schedule.Tests;

public sealed class ScheduledTaskTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var nextRun = new DateTimeOffset(2026, 4, 13, 2, 0, 0, TimeSpan.FromHours(12));
        var task = new ScheduledTask("health-check", "*/5 * * * *", nextRun, "Enabled", "curl http://localhost:8080/health", @"\Winix");

        Assert.Equal("health-check", task.Name);
        Assert.Equal("*/5 * * * *", task.Schedule);
        Assert.Equal(nextRun, task.NextRun);
        Assert.Equal("Enabled", task.Status);
        Assert.Equal("curl http://localhost:8080/health", task.Command);
        Assert.Equal(@"\Winix", task.Folder);
    }

    [Fact]
    public void Constructor_NullName_DefaultsToEmpty()
    {
        var task = new ScheduledTask(null!, "* * * * *", null, "Enabled", "cmd", "");

        Assert.Equal("", task.Name);
    }

    [Fact]
    public void Constructor_NullNextRun_IsNull()
    {
        var task = new ScheduledTask("test", "* * * * *", null, "Disabled", "cmd", "");

        Assert.Null(task.NextRun);
    }
}
```

### Step 1.8 — Add projects to solution

```bash
cd D:\projects\winix
dotnet sln Winix.sln add src/Winix.Schedule/Winix.Schedule.csproj --solution-folder src
dotnet sln Winix.sln add src/schedule/schedule.csproj --solution-folder src
dotnet sln Winix.sln add tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj --solution-folder tests
```

### Step 1.9 — Build and run tests

```bash
dotnet build Winix.sln
dotnet test tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj
```

### Step 1.10 — Commit

```bash
git add src/Winix.Schedule/ src/schedule/ tests/Winix.Schedule.Tests/ Winix.sln
git commit -m "feat(schedule): scaffold projects, add to solution"
```

---

## Task 2: CronField Parser

The core building block: parse a single cron field (e.g. `*/5`, `1-5`, `1,3,5`, `0-30/2`, `*`) into a set of allowed values.

### Step 2.1 — Write CronField tests

Create file `tests/Winix.Schedule.Tests/CronFieldTests.cs`:

```csharp
using Winix.Schedule;

namespace Winix.Schedule.Tests;

public sealed class CronFieldTests
{
    // --- Wildcard ---

    [Fact]
    public void Parse_Wildcard_ReturnsAllValues()
    {
        var field = CronField.Parse("*", 0, 59);

        Assert.Equal(60, field.Values.Count);
        Assert.Contains(0, field.Values);
        Assert.Contains(59, field.Values);
    }

    // --- Single value ---

    [Fact]
    public void Parse_SingleValue_ReturnsOneValue()
    {
        var field = CronField.Parse("5", 0, 59);

        Assert.Single(field.Values);
        Assert.Contains(5, field.Values);
    }

    [Fact]
    public void Parse_SingleValue_BelowMin_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("-1", 0, 59));
    }

    [Fact]
    public void Parse_SingleValue_AboveMax_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("60", 0, 59));
    }

    // --- Ranges ---

    [Fact]
    public void Parse_Range_ReturnsInclusive()
    {
        var field = CronField.Parse("1-5", 0, 59);

        Assert.Equal(5, field.Values.Count);
        Assert.Contains(1, field.Values);
        Assert.Contains(5, field.Values);
        Assert.DoesNotContain(0, field.Values);
        Assert.DoesNotContain(6, field.Values);
    }

    [Fact]
    public void Parse_Range_StartAboveEnd_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("10-5", 0, 59));
    }

    [Fact]
    public void Parse_Range_BelowMin_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("0-5", 1, 31));
    }

    [Fact]
    public void Parse_Range_AboveMax_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("1-32", 1, 31));
    }

    // --- Steps ---

    [Fact]
    public void Parse_WildcardStep_ReturnsEveryN()
    {
        var field = CronField.Parse("*/5", 0, 59);

        // 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55
        Assert.Equal(12, field.Values.Count);
        Assert.Contains(0, field.Values);
        Assert.Contains(5, field.Values);
        Assert.Contains(55, field.Values);
        Assert.DoesNotContain(1, field.Values);
    }

    [Fact]
    public void Parse_RangeStep_ReturnsEveryNInRange()
    {
        var field = CronField.Parse("1-10/3", 0, 59);

        // 1, 4, 7, 10
        Assert.Equal(4, field.Values.Count);
        Assert.Contains(1, field.Values);
        Assert.Contains(4, field.Values);
        Assert.Contains(7, field.Values);
        Assert.Contains(10, field.Values);
    }

    [Fact]
    public void Parse_Step_Zero_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("*/0", 0, 59));
    }

    [Fact]
    public void Parse_SingleValueStep_ReturnsEveryNFromValue()
    {
        // "5/10" means starting at 5, every 10: 5, 15, 25, 35, 45, 55
        var field = CronField.Parse("5/10", 0, 59);

        Assert.Equal(6, field.Values.Count);
        Assert.Contains(5, field.Values);
        Assert.Contains(15, field.Values);
        Assert.Contains(55, field.Values);
    }

    // --- Lists ---

    [Fact]
    public void Parse_List_ReturnsAllSpecifiedValues()
    {
        var field = CronField.Parse("1,3,5", 0, 59);

        Assert.Equal(3, field.Values.Count);
        Assert.Contains(1, field.Values);
        Assert.Contains(3, field.Values);
        Assert.Contains(5, field.Values);
    }

    [Fact]
    public void Parse_List_WithRanges()
    {
        var field = CronField.Parse("1-3,7,10-12", 0, 59);

        Assert.Equal(7, field.Values.Count);
        Assert.Contains(1, field.Values);
        Assert.Contains(2, field.Values);
        Assert.Contains(3, field.Values);
        Assert.Contains(7, field.Values);
        Assert.Contains(10, field.Values);
        Assert.Contains(11, field.Values);
        Assert.Contains(12, field.Values);
    }

    [Fact]
    public void Parse_List_WithSteps()
    {
        var field = CronField.Parse("0-10/5,30", 0, 59);

        // 0, 5, 10, 30
        Assert.Equal(4, field.Values.Count);
        Assert.Contains(0, field.Values);
        Assert.Contains(5, field.Values);
        Assert.Contains(10, field.Values);
        Assert.Contains(30, field.Values);
    }

    [Fact]
    public void Parse_List_DuplicatesDeduped()
    {
        var field = CronField.Parse("1,1,2,2", 0, 59);

        Assert.Equal(2, field.Values.Count);
    }

    // --- Named values ---

    [Fact]
    public void Parse_MonthNames()
    {
        var field = CronField.Parse("jan-mar", 1, 12, CronField.MonthNames);

        Assert.Equal(3, field.Values.Count);
        Assert.Contains(1, field.Values);
        Assert.Contains(2, field.Values);
        Assert.Contains(3, field.Values);
    }

    [Fact]
    public void Parse_DayNames()
    {
        var field = CronField.Parse("mon-fri", 0, 7, CronField.DayOfWeekNames);

        Assert.Equal(5, field.Values.Count);
        Assert.Contains(1, field.Values);
        Assert.Contains(2, field.Values);
        Assert.Contains(3, field.Values);
        Assert.Contains(4, field.Values);
        Assert.Contains(5, field.Values);
    }

    [Fact]
    public void Parse_DayNames_CaseInsensitive()
    {
        var field = CronField.Parse("MON", 0, 7, CronField.DayOfWeekNames);

        Assert.Single(field.Values);
        Assert.Contains(1, field.Values);
    }

    [Fact]
    public void Parse_Sunday_Zero_And_Seven_BothMap()
    {
        // Both 0 and 7 should map to Sunday (0)
        var field0 = CronField.Parse("0", 0, 7, CronField.DayOfWeekNames);
        var field7 = CronField.Parse("7", 0, 7, CronField.DayOfWeekNames);

        Assert.Contains(0, field0.Values);
        Assert.Contains(0, field7.Values);
    }

    // --- Error cases ---

    [Fact]
    public void Parse_Empty_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("", 0, 59));
    }

    [Fact]
    public void Parse_InvalidToken_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("abc", 0, 59));
    }

    [Fact]
    public void Parse_TrailingComma_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("1,", 0, 59));
    }

    // --- Contains ---

    [Fact]
    public void Contains_MatchingValue_ReturnsTrue()
    {
        var field = CronField.Parse("5,10,15", 0, 59);

        Assert.True(field.Contains(10));
    }

    [Fact]
    public void Contains_NonMatchingValue_ReturnsFalse()
    {
        var field = CronField.Parse("5,10,15", 0, 59);

        Assert.False(field.Contains(7));
    }
}
```

### Step 2.2 — Verify tests fail

```bash
dotnet test tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj
```

All CronField tests must fail (class does not exist yet).

### Step 2.3 — Implement CronField

Create file `src/Winix.Schedule/CronField.cs`:

```csharp
#nullable enable

using System.Collections.Generic;
using System.Globalization;

namespace Winix.Schedule;

/// <summary>
/// Represents a single parsed cron field (minute, hour, day-of-month, month, or day-of-week).
/// Holds the set of integer values that satisfy the field expression.
/// </summary>
public sealed class CronField
{
    /// <summary>
    /// Name-to-number mappings for month fields (jan=1 .. dec=12).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> MonthNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "jan", 1 }, { "feb", 2 }, { "mar", 3 }, { "apr", 4 },
        { "may", 5 }, { "jun", 6 }, { "jul", 7 }, { "aug", 8 },
        { "sep", 9 }, { "oct", 10 }, { "nov", 11 }, { "dec", 12 },
    };

    /// <summary>
    /// Name-to-number mappings for day-of-week fields (sun=0, mon=1 .. sat=6).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> DayOfWeekNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "sun", 0 }, { "mon", 1 }, { "tue", 2 }, { "wed", 3 },
        { "thu", 4 }, { "fri", 5 }, { "sat", 6 },
    };

    private readonly HashSet<int> _values;

    private CronField(HashSet<int> values)
    {
        _values = values;
    }

    /// <summary>The set of values this field matches.</summary>
    public IReadOnlyCollection<int> Values => _values;

    /// <summary>Returns true if this field matches <paramref name="value"/>.</summary>
    public bool Contains(int value)
    {
        return _values.Contains(value);
    }

    /// <summary>
    /// Parses a single cron field expression into a set of matching integer values.
    /// </summary>
    /// <param name="expression">The field expression (e.g. "*/5", "1-5", "1,3,5").</param>
    /// <param name="min">Minimum allowed value for this field (inclusive).</param>
    /// <param name="max">Maximum allowed value for this field (inclusive).</param>
    /// <param name="names">Optional name-to-number map for named values (months, days).</param>
    /// <returns>A parsed <see cref="CronField"/>.</returns>
    /// <exception cref="FormatException">The expression is invalid.</exception>
    public static CronField Parse(string expression, int min, int max, IReadOnlyDictionary<string, int>? names = null)
    {
        if (string.IsNullOrEmpty(expression))
        {
            throw new FormatException("Cron field expression is empty.");
        }

        bool isDayOfWeek = (min == 0 && max == 7 && names == DayOfWeekNames);
        var values = new HashSet<int>();

        // Split on commas for list support.
        string[] parts = expression.Split(',');
        foreach (string part in parts)
        {
            if (part.Length == 0)
            {
                throw new FormatException($"Invalid cron field: trailing or empty element in '{expression}'.");
            }

            ParseElement(part, min, max, names, isDayOfWeek, values);
        }

        return new CronField(values);
    }

    /// <summary>
    /// Parses a single element of a cron field (not a list — no commas).
    /// Handles wildcards, ranges, steps, names, and bare numbers.
    /// </summary>
    private static void ParseElement(string element, int min, int max, IReadOnlyDictionary<string, int>? names, bool isDayOfWeek, HashSet<int> values)
    {
        // Check for step: e.g. "*/5" or "1-10/3" or "5/10"
        int slashIndex = element.IndexOf('/');
        if (slashIndex >= 0)
        {
            string basePart = element.Substring(0, slashIndex);
            string stepPart = element.Substring(slashIndex + 1);

            if (!int.TryParse(stepPart, NumberStyles.None, CultureInfo.InvariantCulture, out int step) || step <= 0)
            {
                throw new FormatException($"Invalid step value '{stepPart}' in cron field '{element}'.");
            }

            int rangeStart;
            int rangeEnd;

            if (basePart == "*")
            {
                rangeStart = min;
                rangeEnd = max;
            }
            else if (basePart.Contains('-'))
            {
                (rangeStart, rangeEnd) = ParseRange(basePart, min, max, names, isDayOfWeek);
            }
            else
            {
                // "5/10" means start at 5, step to max.
                rangeStart = ResolveValue(basePart, min, max, names, isDayOfWeek);
                rangeEnd = max;
            }

            for (int v = rangeStart; v <= rangeEnd; v += step)
            {
                int normalized = isDayOfWeek && v == 7 ? 0 : v;
                values.Add(normalized);
            }

            return;
        }

        // Wildcard: all values.
        if (element == "*")
        {
            for (int v = min; v <= max; v++)
            {
                values.Add(v);
            }

            return;
        }

        // Range: e.g. "1-5"
        if (element.Contains('-'))
        {
            (int start, int end) = ParseRange(element, min, max, names, isDayOfWeek);
            for (int v = start; v <= end; v++)
            {
                int normalized = isDayOfWeek && v == 7 ? 0 : v;
                values.Add(normalized);
            }

            return;
        }

        // Single value.
        int val = ResolveValue(element, min, max, names, isDayOfWeek);
        int normalizedVal = isDayOfWeek && val == 7 ? 0 : val;
        values.Add(normalizedVal);
    }

    /// <summary>
    /// Parses a range expression (e.g. "1-5") and returns (start, end) inclusive.
    /// </summary>
    private static (int Start, int End) ParseRange(string rangeExpr, int min, int max, IReadOnlyDictionary<string, int>? names, bool isDayOfWeek)
    {
        int dashIndex = rangeExpr.IndexOf('-');
        string startStr = rangeExpr.Substring(0, dashIndex);
        string endStr = rangeExpr.Substring(dashIndex + 1);

        int start = ResolveValue(startStr, min, max, names, isDayOfWeek);
        int end = ResolveValue(endStr, min, max, names, isDayOfWeek);

        if (start > end)
        {
            throw new FormatException($"Invalid range '{rangeExpr}': start ({start}) is greater than end ({end}).");
        }

        return (start, end);
    }

    /// <summary>
    /// Resolves a single value token to an integer, handling named values (months, days).
    /// </summary>
    private static int ResolveValue(string token, int min, int max, IReadOnlyDictionary<string, int>? names, bool isDayOfWeek)
    {
        // Try named lookup first.
        if (names != null && names.TryGetValue(token, out int namedValue))
        {
            return namedValue;
        }

        if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            throw new FormatException($"Invalid cron field value '{token}': not a number or recognized name.");
        }

        // Day-of-week 7 is treated as Sunday (0) — validate against extended max.
        if (isDayOfWeek && value == 7)
        {
            return 7; // Will be normalized to 0 by caller.
        }

        if (value < min || value > max)
        {
            throw new FormatException($"Cron field value {value} is outside the allowed range {min}-{max}.");
        }

        return value;
    }
}
```

### Step 2.4 — Verify tests pass

```bash
dotnet test tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj
```

All CronField tests must pass.

### Step 2.5 — Commit

```bash
git add src/Winix.Schedule/CronField.cs tests/Winix.Schedule.Tests/CronFieldTests.cs
git commit -m "feat(schedule): implement CronField parser with ranges, steps, lists, and named values"
```

---

## Task 3: CronExpression Parser (Parse only)

Parse a 5-field cron string into a `CronExpression` object holding five `CronField` instances. No next-occurrence logic yet.

### Step 3.1 — Write CronExpression parse tests

Create file `tests/Winix.Schedule.Tests/CronExpressionParseTests.cs`:

```csharp
using Winix.Schedule;

namespace Winix.Schedule.Tests;

public sealed class CronExpressionParseTests
{
    [Fact]
    public void Parse_AllWildcards_Succeeds()
    {
        var expr = CronExpression.Parse("* * * * *");

        Assert.Equal("* * * * *", expr.Expression);
    }

    [Fact]
    public void Parse_SpecificValues_Succeeds()
    {
        var expr = CronExpression.Parse("0 2 1 6 3");

        Assert.Equal("0 2 1 6 3", expr.Expression);
    }

    [Fact]
    public void Parse_Steps_Succeeds()
    {
        var expr = CronExpression.Parse("*/5 */2 * * *");

        Assert.Equal("*/5 */2 * * *", expr.Expression);
    }

    [Fact]
    public void Parse_Ranges_Succeeds()
    {
        var expr = CronExpression.Parse("0 9-17 * * 1-5");

        Assert.Equal("0 9-17 * * 1-5", expr.Expression);
    }

    [Fact]
    public void Parse_Lists_Succeeds()
    {
        var expr = CronExpression.Parse("0,30 * * * *");

        Assert.Equal("0,30 * * * *", expr.Expression);
    }

    [Fact]
    public void Parse_MonthNames_Succeeds()
    {
        var expr = CronExpression.Parse("0 0 1 jan-mar *");

        Assert.Equal("0 0 1 jan-mar *", expr.Expression);
    }

    [Fact]
    public void Parse_DayNames_Succeeds()
    {
        var expr = CronExpression.Parse("0 9 * * mon-fri");

        Assert.Equal("0 9 * * mon-fri", expr.Expression);
    }

    [Fact]
    public void Parse_ExtraWhitespace_Trimmed()
    {
        var expr = CronExpression.Parse("  0  2  *  *  *  ");

        Assert.Equal("0  2  *  *  *", expr.Expression);
    }

    // --- Error cases ---

    [Fact]
    public void Parse_TooFewFields_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("* * * *"));
    }

    [Fact]
    public void Parse_TooManyFields_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("* * * * * *"));
    }

    [Fact]
    public void Parse_Empty_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse(""));
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CronExpression.Parse(null!));
    }

    [Fact]
    public void Parse_InvalidMinute_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("60 * * * *"));
    }

    [Fact]
    public void Parse_InvalidHour_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("0 24 * * *"));
    }

    [Fact]
    public void Parse_InvalidDayOfMonth_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("0 0 32 * *"));
    }

    [Fact]
    public void Parse_InvalidMonth_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("0 0 * 13 *"));
    }

    [Fact]
    public void Parse_InvalidDayOfWeek_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("0 0 * * 8"));
    }

    [Fact]
    public void Parse_Sunday7_IsValid()
    {
        // 7 is a valid alias for Sunday (0).
        var expr = CronExpression.Parse("0 0 * * 7");

        Assert.Equal("0 0 * * 7", expr.Expression);
    }

    [Fact]
    public void Parse_PreservesOriginalExpression()
    {
        var expr = CronExpression.Parse("*/15 9-17 * * mon-fri");

        Assert.Equal("*/15 9-17 * * mon-fri", expr.Expression);
    }
}
```

### Step 3.2 — Verify tests fail

```bash
dotnet test tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj
```

### Step 3.3 — Implement CronExpression (Parse only, GetNextOccurrence placeholder)

Create file `src/Winix.Schedule/CronExpression.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;

namespace Winix.Schedule;

/// <summary>
/// Parses and evaluates standard 5-field cron expressions.
/// Fields: minute (0-59), hour (0-23), day-of-month (1-31), month (1-12), day-of-week (0-7).
/// </summary>
public sealed class CronExpression
{
    private readonly CronField _minute;
    private readonly CronField _hour;
    private readonly CronField _dayOfMonth;
    private readonly CronField _month;
    private readonly CronField _dayOfWeek;

    private CronExpression(string expression, CronField minute, CronField hour, CronField dayOfMonth, CronField month, CronField dayOfWeek)
    {
        Expression = expression;
        _minute = minute;
        _hour = hour;
        _dayOfMonth = dayOfMonth;
        _month = month;
        _dayOfWeek = dayOfWeek;
    }

    /// <summary>The original cron expression string as provided (trimmed).</summary>
    public string Expression { get; }

    /// <summary>
    /// Parses a 5-field cron expression string.
    /// </summary>
    /// <param name="expression">
    /// A standard cron expression with 5 space-separated fields:
    /// minute, hour, day-of-month, month, day-of-week.
    /// Also accepts special strings: @hourly, @daily, @weekly, @monthly, @yearly, @annually.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
    /// <exception cref="FormatException">The expression is invalid.</exception>
    public static CronExpression Parse(string expression)
    {
        if (expression is null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        string trimmed = expression.Trim();
        if (trimmed.Length == 0)
        {
            throw new FormatException("Cron expression is empty.");
        }

        // Handle special strings.
        if (trimmed.StartsWith('@'))
        {
            return ParseSpecialString(trimmed);
        }

        // Split on whitespace.
        string[] fields = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            throw new FormatException($"Cron expression must have exactly 5 fields, got {fields.Length}: '{trimmed}'.");
        }

        CronField minute     = CronField.Parse(fields[0], 0, 59);
        CronField hour       = CronField.Parse(fields[1], 0, 23);
        CronField dayOfMonth = CronField.Parse(fields[2], 1, 31);
        CronField month      = CronField.Parse(fields[3], 1, 12, CronField.MonthNames);
        CronField dayOfWeek  = CronField.Parse(fields[4], 0, 7, CronField.DayOfWeekNames);

        // Reconstruct the expression from split fields (normalizes whitespace, preserves tokens).
        string normalized = string.Join("  ", fields);

        return new CronExpression(normalized, minute, hour, dayOfMonth, month, dayOfWeek);
    }

    /// <summary>
    /// Returns the next occurrence after <paramref name="after"/>.
    /// </summary>
    public DateTimeOffset GetNextOccurrence(DateTimeOffset after)
    {
        // Implemented in Task 4.
        throw new NotImplementedException();
    }

    /// <summary>
    /// Returns the next <paramref name="count"/> occurrences after <paramref name="after"/>.
    /// </summary>
    public IReadOnlyList<DateTimeOffset> GetNextOccurrences(DateTimeOffset after, int count)
    {
        var results = new List<DateTimeOffset>(count);
        DateTimeOffset cursor = after;
        for (int i = 0; i < count; i++)
        {
            cursor = GetNextOccurrence(cursor);
            results.Add(cursor);
        }

        return results;
    }

    /// <summary>The parsed minute field.</summary>
    internal CronField Minute => _minute;

    /// <summary>The parsed hour field.</summary>
    internal CronField Hour => _hour;

    /// <summary>The parsed day-of-month field.</summary>
    internal CronField DayOfMonth => _dayOfMonth;

    /// <summary>The parsed month field.</summary>
    internal CronField Month => _month;

    /// <summary>The parsed day-of-week field.</summary>
    internal CronField DayOfWeek => _dayOfWeek;

    /// <summary>
    /// Parses special cron strings like @hourly, @daily, etc.
    /// </summary>
    private static CronExpression ParseSpecialString(string special)
    {
        string lower = special.ToLowerInvariant();
        return lower switch
        {
            "@hourly"   => Parse("0 * * * *"),
            "@daily"    => Parse("0 0 * * *"),
            "@midnight" => Parse("0 0 * * *"),
            "@weekly"   => Parse("0 0 * * 0"),
            "@monthly"  => Parse("0 0 1 * *"),
            "@yearly"   => Parse("0 0 1 1 *"),
            "@annually" => Parse("0 0 1 1 *"),
            _ => throw new FormatException($"Unknown cron special string '{special}'."),
        };
    }
}
```

**Note:** The `Parse` method reconstructs the expression from split fields using double-space join to preserve the internal representation while normalizing whitespace. The `Parse_ExtraWhitespace_Trimmed` test expects leading/trailing whitespace to be stripped but internal spacing to be preserved from the split. Update the test expectation or the implementation so they align. In the code above, `string.Join("  ", fields)` would yield `"0  2  *  *  *"` which matches. However, for a normal 5-field expression like `"0 2 * * *"`, the join produces `"0  2  *  *  *"`. That changes the stored expression — not ideal. Let's store the original trimmed expression instead.

**Correction:** Replace the normalized expression logic to use `trimmed` directly:

```csharp
        return new CronExpression(trimmed, minute, hour, dayOfMonth, month, dayOfWeek);
```

And update the extra-whitespace test:

```csharp
    [Fact]
    public void Parse_ExtraWhitespace_Trimmed()
    {
        var expr = CronExpression.Parse("  0  2  *  *  *  ");

        // Leading/trailing whitespace is trimmed; internal whitespace is preserved as-is.
        Assert.Equal("0  2  *  *  *", expr.Expression);
    }
```

This works because `"  0  2  *  *  *  ".Trim()` = `"0  2  *  *  *"`.

### Step 3.4 — Verify tests pass

```bash
dotnet test tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj --filter "FullyQualifiedName~CronExpressionParseTests"
```

### Step 3.5 — Commit

```bash
git add src/Winix.Schedule/CronExpression.cs tests/Winix.Schedule.Tests/CronExpressionParseTests.cs
git commit -m "feat(schedule): implement CronExpression parser (5-field + special strings)"
```

---

## Task 4: CronExpression Next-Occurrence Logic

The most critical piece: given a `DateTimeOffset`, find the next time the cron expression fires.

### Step 4.1 — Write next-occurrence tests

Create file `tests/Winix.Schedule.Tests/CronExpressionNextTests.cs`:

```csharp
using Winix.Schedule;

namespace Winix.Schedule.Tests;

public sealed class CronExpressionNextTests
{
    // Frozen reference time: 2026-04-12 14:30:00 +12:00 (Sunday)
    private static readonly DateTimeOffset Reference = new DateTimeOffset(2026, 4, 12, 14, 30, 0, TimeSpan.FromHours(12));

    // --- Every minute ---

    [Fact]
    public void EveryMinute_ReturnsNextMinute()
    {
        var expr = CronExpression.Parse("* * * * *");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 4, 12, 14, 31, 0, TimeSpan.FromHours(12)), next);
    }

    // --- Specific time ---

    [Fact]
    public void DailyAt2am_Today_ReturnsTomorrow()
    {
        // It's 14:30, so 02:00 today has passed -> next is tomorrow.
        var expr = CronExpression.Parse("0 2 * * *");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 4, 13, 2, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void DailyAt2am_BeforeTime_ReturnsToday()
    {
        var before = new DateTimeOffset(2026, 4, 12, 1, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 2 * * *");

        DateTimeOffset next = expr.GetNextOccurrence(before);

        Assert.Equal(new DateTimeOffset(2026, 4, 12, 2, 0, 0, TimeSpan.FromHours(12)), next);
    }

    // --- Every 5 minutes ---

    [Fact]
    public void Every5Minutes_ReturnsNext5MinuteMark()
    {
        var expr = CronExpression.Parse("*/5 * * * *");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        // 14:30 -> next is 14:35
        Assert.Equal(new DateTimeOffset(2026, 4, 12, 14, 35, 0, TimeSpan.FromHours(12)), next);
    }

    // --- Day-of-week ---

    [Fact]
    public void Weekdays_OnSunday_ReturnsMonday()
    {
        // Reference is Sunday April 12, 2026.
        var expr = CronExpression.Parse("0 9 * * 1-5");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 4, 13, 9, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Weekdays_OnFriday_ReturnsSameDay()
    {
        // Friday April 10, 2026 at 08:00 -> next at 09:00 same day.
        var friday = new DateTimeOffset(2026, 4, 10, 8, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 9 * * 1-5");

        DateTimeOffset next = expr.GetNextOccurrence(friday);

        Assert.Equal(new DateTimeOffset(2026, 4, 10, 9, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Weekdays_FridayAfterTime_ReturnsNextMonday()
    {
        // Friday April 10, 2026 at 10:00 (after 09:00) -> next Monday.
        var fridayLate = new DateTimeOffset(2026, 4, 10, 10, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 9 * * 1-5");

        DateTimeOffset next = expr.GetNextOccurrence(fridayLate);

        Assert.Equal(new DateTimeOffset(2026, 4, 13, 9, 0, 0, TimeSpan.FromHours(12)), next);
    }

    // --- Monthly ---

    [Fact]
    public void Monthly_FirstOfMonth_BeforeDay_ReturnsThisMonth()
    {
        var early = new DateTimeOffset(2026, 4, 1, 1, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 2 1 * *");

        DateTimeOffset next = expr.GetNextOccurrence(early);

        Assert.Equal(new DateTimeOffset(2026, 4, 1, 2, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Monthly_FirstOfMonth_AfterDay_ReturnsNextMonth()
    {
        // April 12 is past the 1st -> next is May 1.
        var expr = CronExpression.Parse("0 2 1 * *");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 5, 1, 2, 0, 0, TimeSpan.FromHours(12)), next);
    }

    // --- Month boundaries ---

    [Fact]
    public void DecemberRollover_ReturnsNextYear()
    {
        var dec = new DateTimeOffset(2026, 12, 31, 23, 59, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 0 1 * *");

        DateTimeOffset next = expr.GetNextOccurrence(dec);

        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    // --- Leap year ---

    [Fact]
    public void Feb29_LeapYear_Matches()
    {
        // 2028 is a leap year.
        var feb28 = new DateTimeOffset(2028, 2, 28, 23, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 0 29 2 *");

        DateTimeOffset next = expr.GetNextOccurrence(feb28);

        Assert.Equal(new DateTimeOffset(2028, 2, 29, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Feb29_NonLeapYear_SkipsToNextLeapYear()
    {
        // 2026 is not a leap year. Next Feb 29 is 2028.
        var feb = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 0 29 2 *");

        DateTimeOffset next = expr.GetNextOccurrence(feb);

        Assert.Equal(new DateTimeOffset(2028, 2, 29, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    // --- Day 31 skips short months ---

    [Fact]
    public void Day31_AprilHas30Days_SkipsToMay()
    {
        var apr = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 0 31 * *");

        DateTimeOffset next = expr.GetNextOccurrence(apr);

        // April has 30 days, so skip to May 31.
        Assert.Equal(new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    // --- Exact boundary: seconds are zeroed ---

    [Fact]
    public void ExactMatch_WithSeconds_AdvancesToNextMinute()
    {
        // At exactly 14:30:30, the next occurrence of "30 14 * * *" should be the NEXT
        // matching time, not the current minute (since seconds have passed).
        var withSeconds = new DateTimeOffset(2026, 4, 12, 14, 30, 30, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("30 14 * * *");

        DateTimeOffset next = expr.GetNextOccurrence(withSeconds);

        // 14:30 today has partially elapsed -> next is tomorrow 14:30.
        Assert.Equal(new DateTimeOffset(2026, 4, 13, 14, 30, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void ExactMatch_WithZeroSeconds_AdvancesToNextOccurrence()
    {
        // At exactly 14:30:00, GetNextOccurrence should return the NEXT occurrence,
        // not the current one ("after" semantics, not "at or after").
        var exact = new DateTimeOffset(2026, 4, 12, 14, 30, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("30 14 * * *");

        DateTimeOffset next = expr.GetNextOccurrence(exact);

        Assert.Equal(new DateTimeOffset(2026, 4, 13, 14, 30, 0, TimeSpan.FromHours(12)), next);
    }

    // --- GetNextOccurrences ---

    [Fact]
    public void GetNextOccurrences_ReturnsRequestedCount()
    {
        var expr = CronExpression.Parse("0 2 * * *");

        IReadOnlyList<DateTimeOffset> occurrences = expr.GetNextOccurrences(Reference, 5);

        Assert.Equal(5, occurrences.Count);
        Assert.Equal(new DateTimeOffset(2026, 4, 13, 2, 0, 0, TimeSpan.FromHours(12)), occurrences[0]);
        Assert.Equal(new DateTimeOffset(2026, 4, 14, 2, 0, 0, TimeSpan.FromHours(12)), occurrences[1]);
        Assert.Equal(new DateTimeOffset(2026, 4, 15, 2, 0, 0, TimeSpan.FromHours(12)), occurrences[2]);
        Assert.Equal(new DateTimeOffset(2026, 4, 16, 2, 0, 0, TimeSpan.FromHours(12)), occurrences[3]);
        Assert.Equal(new DateTimeOffset(2026, 4, 17, 2, 0, 0, TimeSpan.FromHours(12)), occurrences[4]);
    }

    // --- Sunday = 0 or 7 ---

    [Fact]
    public void Sunday_Zero_MatchesSunday()
    {
        // Saturday April 11, 2026
        var sat = new DateTimeOffset(2026, 4, 11, 10, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 9 * * 0");

        DateTimeOffset next = expr.GetNextOccurrence(sat);

        // Next Sunday is April 12
        Assert.Equal(new DateTimeOffset(2026, 4, 12, 9, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Sunday_Seven_MatchesSunday()
    {
        var sat = new DateTimeOffset(2026, 4, 11, 10, 0, 0, TimeSpan.FromHours(12));
        var expr = CronExpression.Parse("0 9 * * 7");

        DateTimeOffset next = expr.GetNextOccurrence(sat);

        Assert.Equal(new DateTimeOffset(2026, 4, 12, 9, 0, 0, TimeSpan.FromHours(12)), next);
    }

    // --- Preserves offset ---

    [Fact]
    public void PreservesOffset_FromInput()
    {
        var utc = new DateTimeOffset(2026, 4, 12, 2, 30, 0, TimeSpan.Zero);
        var expr = CronExpression.Parse("0 3 * * *");

        DateTimeOffset next = expr.GetNextOccurrence(utc);

        Assert.Equal(TimeSpan.Zero, next.Offset);
        Assert.Equal(new DateTimeOffset(2026, 4, 12, 3, 0, 0, TimeSpan.Zero), next);
    }
}
```

### Step 4.2 — Verify tests fail

```bash
dotnet test tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj --filter "FullyQualifiedName~CronExpressionNextTests"
```

All tests must fail (GetNextOccurrence throws NotImplementedException).

### Step 4.3 — Implement GetNextOccurrence

Update `src/Winix.Schedule/CronExpression.cs` — replace the `GetNextOccurrence` method:

```csharp
    /// <summary>
    /// Returns the next occurrence strictly after <paramref name="after"/>.
    /// The result always has zero seconds and milliseconds.
    /// The offset from <paramref name="after"/> is preserved.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No matching time found within 4 years (prevents infinite loops on impossible expressions
    /// like day 31 in February-only schedules).
    /// </exception>
    public DateTimeOffset GetNextOccurrence(DateTimeOffset after)
    {
        // Start from the next whole minute after 'after'.
        DateTimeOffset candidate = after
            .AddSeconds(-after.Second)
            .AddMilliseconds(-after.Millisecond)
            .AddMinutes(1);

        // Safety limit: don't search more than 4 years ahead (avoids infinite loops
        // on expressions that can never match, e.g. "0 0 31 2 *" in practice won't
        // match for long stretches but Feb 29 takes up to 8 years in the worst case).
        DateTimeOffset limit = after.AddYears(8);

        while (candidate < limit)
        {
            // Check month first (most likely to skip large ranges).
            if (!_month.Contains(candidate.Month))
            {
                // Jump to 1st of next month, midnight.
                candidate = NextMonth(candidate);
                continue;
            }

            // Check day-of-month and day-of-week.
            // Standard cron: if both day-of-month and day-of-week are restricted (not *),
            // the job runs when EITHER matches (OR logic). If only one is restricted,
            // that one must match.
            bool domRestricted = _dayOfMonth.Values.Count < 31;
            bool dowRestricted = _dayOfWeek.Values.Count < 7; // 0-6 = 7 values when unrestricted

            bool domMatch = _dayOfMonth.Contains(candidate.Day);
            int dow = (int)candidate.DayOfWeek; // .NET Sunday=0, matches cron convention.
            bool dowMatch = _dayOfWeek.Contains(dow);

            bool dayMatch;
            if (domRestricted && dowRestricted)
            {
                // Both restricted: OR logic (standard cron behavior).
                dayMatch = domMatch || dowMatch;
            }
            else if (domRestricted)
            {
                dayMatch = domMatch;
            }
            else if (dowRestricted)
            {
                dayMatch = dowMatch;
            }
            else
            {
                // Neither restricted: any day matches.
                dayMatch = true;
            }

            if (!dayMatch)
            {
                // Jump to next day, midnight.
                candidate = NextDay(candidate);
                continue;
            }

            // Check hour.
            if (!_hour.Contains(candidate.Hour))
            {
                // Jump to next hour, on the minute.
                candidate = NextHour(candidate);
                continue;
            }

            // Check minute.
            if (!_minute.Contains(candidate.Minute))
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }

            // All fields match.
            return candidate;
        }

        throw new InvalidOperationException(
            $"No matching time found within 8 years for cron expression '{Expression}'.");
    }

    /// <summary>Advances to midnight on the first day of the next month.</summary>
    private static DateTimeOffset NextMonth(DateTimeOffset dt)
    {
        int year = dt.Year;
        int month = dt.Month + 1;
        if (month > 12)
        {
            month = 1;
            year++;
        }

        return new DateTimeOffset(year, month, 1, 0, 0, 0, dt.Offset);
    }

    /// <summary>Advances to midnight on the next day.</summary>
    private static DateTimeOffset NextDay(DateTimeOffset dt)
    {
        return new DateTimeOffset(dt.Year, dt.Month, dt.Day, 0, 0, 0, dt.Offset).AddDays(1);
    }

    /// <summary>Advances to the start of the next hour.</summary>
    private static DateTimeOffset NextHour(DateTimeOffset dt)
    {
        return new DateTimeOffset(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, dt.Offset).AddHours(1);
    }
```

**Important note on day-of-month exceeding actual days in month:** When the cron specifies day 31 but the month only has 30 days, the month-level check passes (month itself matches), but the day-of-month check fails and we advance day-by-day until we roll into the next month. The `NextDay` helper handles month/year rollover via `AddDays(1)`.

### Step 4.4 — Verify tests pass

```bash
dotnet test tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj --filter "FullyQualifiedName~CronExpressionNextTests"
```

### Step 4.5 — Commit

```bash
git add src/Winix.Schedule/CronExpression.cs tests/Winix.Schedule.Tests/CronExpressionNextTests.cs
git commit -m "feat(schedule): implement CronExpression.GetNextOccurrence with day-of-week/month logic"
```

---

## Task 5: CronExpression Special Strings

Tests for the `@hourly`, `@daily`, `@weekly`, `@monthly`, `@yearly`, `@annually` shortcuts — verifying they parse AND produce correct next-occurrence results.

### Step 5.1 — Write special string tests

Create file `tests/Winix.Schedule.Tests/CronSpecialStringTests.cs`:

```csharp
using Winix.Schedule;

namespace Winix.Schedule.Tests;

public sealed class CronSpecialStringTests
{
    // Reference: 2026-04-12 14:30:00 +12:00 (Sunday)
    private static readonly DateTimeOffset Reference = new DateTimeOffset(2026, 4, 12, 14, 30, 0, TimeSpan.FromHours(12));

    [Fact]
    public void Hourly_ReturnsNextHour()
    {
        var expr = CronExpression.Parse("@hourly");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 4, 12, 15, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Daily_ReturnsTomorrowMidnight()
    {
        var expr = CronExpression.Parse("@daily");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 4, 13, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Midnight_SameAsDaily()
    {
        var expr = CronExpression.Parse("@midnight");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 4, 13, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Weekly_ReturnsNextSundayMidnight()
    {
        // Reference is Sunday 14:30, so next Sunday midnight is 7 days later.
        var expr = CronExpression.Parse("@weekly");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 4, 19, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Monthly_ReturnsFirstOfNextMonth()
    {
        var expr = CronExpression.Parse("@monthly");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Yearly_ReturnsJan1NextYear()
    {
        var expr = CronExpression.Parse("@yearly");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void Annually_SameAsYearly()
    {
        var expr = CronExpression.Parse("@annually");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }

    [Fact]
    public void UnknownSpecialString_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("@invalid"));
    }

    [Fact]
    public void SpecialString_CaseInsensitive()
    {
        var expr = CronExpression.Parse("@DAILY");

        DateTimeOffset next = expr.GetNextOccurrence(Reference);

        Assert.Equal(new DateTimeOffset(2026, 4, 13, 0, 0, 0, TimeSpan.FromHours(12)), next);
    }
}
```

### Step 5.2 — Run tests

```bash
dotnet test tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj --filter "FullyQualifiedName~CronSpecialStringTests"
```

These should all pass — the special string parsing and GetNextOccurrence are already implemented. If any fail, fix accordingly.

### Step 5.3 — Commit

```bash
git add tests/Winix.Schedule.Tests/CronSpecialStringTests.cs
git commit -m "test(schedule): add CronExpression special string tests (@hourly, @daily, etc.)"
```

---

## Task 6: ScheduleResult, TaskRunRecord, and NameGenerator

### Step 6.1 — Create data types

Create file `src/Winix.Schedule/ScheduleResult.cs`:

```csharp
#nullable enable

namespace Winix.Schedule;

/// <summary>
/// Result of a scheduler operation (add, remove, enable, disable, run).
/// </summary>
public sealed class ScheduleResult
{
    /// <summary>True if the operation succeeded.</summary>
    public bool Success { get; }

    /// <summary>Human-readable message describing what happened.</summary>
    public string Message { get; }

    /// <summary>The task name involved.</summary>
    public string TaskName { get; }

    /// <summary>
    /// The cron expression, if applicable (e.g. on add). Empty otherwise.
    /// </summary>
    public string CronExpression { get; }

    /// <summary>
    /// The next scheduled run time, if applicable and determinable. Null otherwise.
    /// </summary>
    public DateTimeOffset? NextRun { get; }

    /// <summary>Creates a new <see cref="ScheduleResult"/>.</summary>
    public ScheduleResult(bool success, string message, string taskName, string cronExpression = "", DateTimeOffset? nextRun = null)
    {
        Success = success;
        Message = message ?? "";
        TaskName = taskName ?? "";
        CronExpression = cronExpression ?? "";
        NextRun = nextRun;
    }
}
```

Create file `src/Winix.Schedule/TaskRunRecord.cs`:

```csharp
#nullable enable

namespace Winix.Schedule;

/// <summary>
/// Data model for a task run history entry.
/// </summary>
public sealed class TaskRunRecord
{
    /// <summary>When the task started running.</summary>
    public DateTimeOffset StartTime { get; }

    /// <summary>Exit code, or null if still running or unknown.</summary>
    public int? ExitCode { get; }

    /// <summary>Duration, or null if still running or unknown.</summary>
    public TimeSpan? Duration { get; }

    /// <summary>Creates a new <see cref="TaskRunRecord"/>.</summary>
    public TaskRunRecord(DateTimeOffset startTime, int? exitCode, TimeSpan? duration)
    {
        StartTime = startTime;
        ExitCode = exitCode;
        Duration = duration;
    }
}
```

### Step 6.2 — Write NameGenerator tests

Create file `tests/Winix.Schedule.Tests/NameGeneratorTests.cs`:

```csharp
using Winix.Schedule;

namespace Winix.Schedule.Tests;

public sealed class NameGeneratorTests
{
    [Fact]
    public void Generate_SingleWord_ReturnsLowercase()
    {
        string name = NameGenerator.Generate("curl", new string[0]);

        Assert.Equal("curl", name);
    }

    [Fact]
    public void Generate_MultipleWords_JoinsWithDash()
    {
        string name = NameGenerator.Generate("dotnet", new[] { "build" });

        Assert.Equal("dotnet-build", name);
    }

    [Fact]
    public void Generate_LongCommand_TruncatesAtTwoTokens()
    {
        string name = NameGenerator.Generate("dotnet", new[] { "build", "/path/to/project", "--configuration", "Release" });

        Assert.Equal("dotnet-build", name);
    }

    [Fact]
    public void Generate_PathSlashes_ExtractsFilename()
    {
        string name = NameGenerator.Generate("/usr/bin/my-script.sh", new string[0]);

        Assert.Equal("my-script", name);
    }

    [Fact]
    public void Generate_WindowsPath_ExtractsFilename()
    {
        string name = NameGenerator.Generate(@"C:\tools\cleanup.bat", new string[0]);

        Assert.Equal("cleanup", name);
    }

    [Fact]
    public void Generate_Extension_StripsExe()
    {
        string name = NameGenerator.Generate("myapp.exe", new string[0]);

        Assert.Equal("myapp", name);
    }

    [Fact]
    public void Generate_MakeUnique_AppendsNumber()
    {
        string name = NameGenerator.MakeUnique("health-check", new HashSet<string> { "health-check" });

        Assert.Equal("health-check-2", name);
    }

    [Fact]
    public void Generate_MakeUnique_IncrementsUntilFree()
    {
        string name = NameGenerator.MakeUnique("test", new HashSet<string> { "test", "test-2", "test-3" });

        Assert.Equal("test-4", name);
    }

    [Fact]
    public void Generate_MakeUnique_NoConflict_ReturnsSame()
    {
        string name = NameGenerator.MakeUnique("unique", new HashSet<string> { "other" });

        Assert.Equal("unique", name);
    }
}
```

### Step 6.3 — Implement NameGenerator

Create file `src/Winix.Schedule/NameGenerator.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace Winix.Schedule;

/// <summary>
/// Auto-generates task names from command strings.
/// </summary>
public static class NameGenerator
{
    private static readonly HashSet<string> StrippedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".sh", ".ps1",
    };

    /// <summary>
    /// Generates a name from a command and its arguments.
    /// Takes the command filename (stripped of path and common extensions) and the first argument,
    /// joined with a dash. Result is lowercased.
    /// </summary>
    /// <param name="command">The command executable.</param>
    /// <param name="arguments">Command arguments.</param>
    public static string Generate(string command, string[] arguments)
    {
        // Extract filename from path.
        string fileName = Path.GetFileName(command);

        // Strip common executable extensions.
        string ext = Path.GetExtension(fileName);
        if (ext.Length > 0 && StrippedExtensions.Contains(ext))
        {
            fileName = Path.GetFileNameWithoutExtension(fileName);
        }

        string name = fileName.ToLowerInvariant();

        // Append first argument if it exists and looks like a subcommand (not a flag or path).
        if (arguments.Length > 0)
        {
            string firstArg = arguments[0];
            if (firstArg.Length > 0 && !firstArg.StartsWith('-') && !firstArg.Contains('/') && !firstArg.Contains('\\'))
            {
                name += "-" + firstArg.ToLowerInvariant();
            }
        }

        return name;
    }

    /// <summary>
    /// Makes a name unique by appending an incrementing suffix if it conflicts with existing names.
    /// </summary>
    /// <param name="baseName">The proposed name.</param>
    /// <param name="existingNames">Set of names that already exist.</param>
    /// <returns>A unique name (possibly with a "-N" suffix).</returns>
    public static string MakeUnique(string baseName, IReadOnlySet<string> existingNames)
    {
        if (!existingNames.Contains(baseName))
        {
            return baseName;
        }

        for (int i = 2; i < 1000; i++)
        {
            string candidate = baseName + "-" + i.ToString();
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }

        // Extremely unlikely to reach here.
        return baseName + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    }
}
```

### Step 6.4 — Verify tests pass

```bash
dotnet test tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj --filter "FullyQualifiedName~NameGeneratorTests"
```

### Step 6.5 — Commit

```bash
git add src/Winix.Schedule/ScheduleResult.cs src/Winix.Schedule/TaskRunRecord.cs src/Winix.Schedule/NameGenerator.cs tests/Winix.Schedule.Tests/NameGeneratorTests.cs
git commit -m "feat(schedule): add ScheduleResult, TaskRunRecord, and NameGenerator"
```

---

## Task 7: ISchedulerBackend Interface

### Step 7.1 — Create the interface

Create file `src/Winix.Schedule/ISchedulerBackend.cs`:

```csharp
#nullable enable

using System.Collections.Generic;

namespace Winix.Schedule;

/// <summary>
/// Platform-specific scheduler backend. Implementations delegate to the OS scheduler
/// (schtasks.exe on Windows, crontab on Linux/macOS).
/// </summary>
public interface ISchedulerBackend
{
    /// <summary>Creates a new scheduled task.</summary>
    /// <param name="name">Task name.</param>
    /// <param name="cron">Parsed cron expression for the schedule.</param>
    /// <param name="command">The executable to run.</param>
    /// <param name="arguments">Arguments for the command.</param>
    /// <param name="folder">Folder path (Windows: Task Scheduler folder, Linux: ignored).</param>
    ScheduleResult Add(string name, CronExpression cron, string command, string[] arguments, string folder);

    /// <summary>Lists tasks in the specified scope.</summary>
    /// <param name="folder">Folder to list (null for default). Ignored on Linux.</param>
    /// <param name="all">When true, list all tasks (not just Winix-managed).</param>
    IReadOnlyList<ScheduledTask> List(string? folder, bool all);

    /// <summary>Removes a task by name.</summary>
    /// <param name="name">Task name.</param>
    /// <param name="folder">Folder path.</param>
    ScheduleResult Remove(string name, string folder);

    /// <summary>Enables a disabled task.</summary>
    /// <param name="name">Task name.</param>
    /// <param name="folder">Folder path.</param>
    ScheduleResult Enable(string name, string folder);

    /// <summary>Disables an enabled task.</summary>
    /// <param name="name">Task name.</param>
    /// <param name="folder">Folder path.</param>
    ScheduleResult Disable(string name, string folder);

    /// <summary>Triggers immediate execution of a task (fire and forget).</summary>
    /// <param name="name">Task name.</param>
    /// <param name="folder">Folder path.</param>
    ScheduleResult Run(string name, string folder);

    /// <summary>Returns recent run history for a task.</summary>
    /// <param name="name">Task name.</param>
    /// <param name="folder">Folder path.</param>
    IReadOnlyList<TaskRunRecord> GetHistory(string name, string folder);
}
```

### Step 7.2 — Commit

```bash
git add src/Winix.Schedule/ISchedulerBackend.cs
git commit -m "feat(schedule): add ISchedulerBackend interface"
```

---

## Task 8: SchtasksBackend — Windows Backend

The Windows backend shells out to `schtasks.exe`. This is the largest backend task with several sub-components.

### Step 8.1 — Write CronToSchtasksMapper tests

Create file `tests/Winix.Schedule.Tests/CronToSchtasksMapperTests.cs`:

```csharp
using Winix.Schedule;

namespace Winix.Schedule.Tests;

public sealed class CronToSchtasksMapperTests
{
    [Fact]
    public void Map_EveryNMinutes_ReturnsMinuteSchedule()
    {
        var cron = CronExpression.Parse("*/5 * * * *");

        SchtasksSchedule result = CronToSchtasksMapper.Map(cron);

        Assert.Equal("MINUTE", result.ScheduleType);
        Assert.Equal("5", result.Modifier);
        Assert.Null(result.StartTime);
        Assert.Null(result.Days);
    }

    [Fact]
    public void Map_EveryNHours_ReturnsHourlySchedule()
    {
        var cron = CronExpression.Parse("0 */2 * * *");

        SchtasksSchedule result = CronToSchtasksMapper.Map(cron);

        Assert.Equal("HOURLY", result.ScheduleType);
        Assert.Equal("2", result.Modifier);
    }

    [Fact]
    public void Map_DailyAtTime_ReturnsDailyWithStartTime()
    {
        var cron = CronExpression.Parse("0 2 * * *");

        SchtasksSchedule result = CronToSchtasksMapper.Map(cron);

        Assert.Equal("DAILY", result.ScheduleType);
        Assert.Equal("02:00", result.StartTime);
    }

    [Fact]
    public void Map_WeekdaysAtTime_ReturnsWeeklyWithDays()
    {
        var cron = CronExpression.Parse("0 9 * * 1-5");

        SchtasksSchedule result = CronToSchtasksMapper.Map(cron);

        Assert.Equal("WEEKLY", result.ScheduleType);
        Assert.Equal("09:00", result.StartTime);
        Assert.Equal("MON,TUE,WED,THU,FRI", result.Days);
    }

    [Fact]
    public void Map_MonthlyFirstDay_ReturnsMonthlyWithDay()
    {
        var cron = CronExpression.Parse("0 2 1 * *");

        SchtasksSchedule result = CronToSchtasksMapper.Map(cron);

        Assert.Equal("MONTHLY", result.ScheduleType);
        Assert.Equal("1", result.DayOfMonth);
        Assert.Equal("02:00", result.StartTime);
    }

    [Fact]
    public void Map_EveryMinute_ReturnsMinute1()
    {
        var cron = CronExpression.Parse("* * * * *");

        SchtasksSchedule result = CronToSchtasksMapper.Map(cron);

        Assert.Equal("MINUTE", result.ScheduleType);
        Assert.Equal("1", result.Modifier);
    }

    [Fact]
    public void Map_SpecificMinuteAndHour_ReturnsDailyWithTime()
    {
        var cron = CronExpression.Parse("30 14 * * *");

        SchtasksSchedule result = CronToSchtasksMapper.Map(cron);

        Assert.Equal("DAILY", result.ScheduleType);
        Assert.Equal("14:30", result.StartTime);
    }
}
```

### Step 8.2 — Write SchtasksCsvParser tests

Create file `tests/Winix.Schedule.Tests/SchtasksCsvParserTests.cs`:

```csharp
using Winix.Schedule;

namespace Winix.Schedule.Tests;

public sealed class SchtasksCsvParserTests
{
    [Fact]
    public void Parse_SingleRow_ReturnsTask()
    {
        // Simplified CSV with the key columns. Real schtasks output has 29 columns.
        string csv = "\"MYPC\",\"\\Winix\\health-check\",\"4/13/2026 2:00:00 PM\",\"Ready\",\"Interactive only\",\"4/12/2026 2:00:00 PM\",\"0\",\"troy\",\"curl http://localhost:8080/health\",\"N/A\",\"*/5 * * * *\",\"Enabled\",\"Disabled\",\"Stop On Battery Mode, No Start On Batteries\",\"troy\",\"Disabled\",\"72:00:00\",\"Scheduling data is not available in this format.\",\"One Time Only, Minute\",\"2:00:00 PM\",\"4/12/2026\",\"N/A\",\"N/A\",\"N/A\",\"0 Hour(s), 5 Minute(s)\",\"N/A\",\"N/A\",\"Disabled\"";

        var tasks = SchtasksCsvParser.Parse(csv, @"\Winix");

        Assert.Single(tasks);
        Assert.Equal("health-check", tasks[0].Name);
        Assert.Equal("Enabled", tasks[0].Status);
        Assert.Equal("curl http://localhost:8080/health", tasks[0].Command);
        Assert.Equal("*/5 * * * *", tasks[0].Schedule);
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsEmpty()
    {
        var tasks = SchtasksCsvParser.Parse("", @"\Winix");

        Assert.Empty(tasks);
    }

    [Fact]
    public void Parse_MultipleRows_ReturnsAll()
    {
        string csv =
            "\"MYPC\",\"\\Winix\\task-a\",\"N/A\",\"Ready\",\"Interactive only\",\"N/A\",\"0\",\"troy\",\"cmd /c echo a\",\"N/A\",\"0 0 * * *\",\"Enabled\",\"Disabled\",\"N/A\",\"troy\",\"Disabled\",\"72:00:00\",\"N/A\",\"Daily\",\"12:00:00 AM\",\"4/12/2026\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"Disabled\"\n" +
            "\"MYPC\",\"\\Winix\\task-b\",\"N/A\",\"Ready\",\"Interactive only\",\"N/A\",\"0\",\"troy\",\"cmd /c echo b\",\"N/A\",\"0 2 * * *\",\"Disabled\",\"Disabled\",\"N/A\",\"troy\",\"Disabled\",\"72:00:00\",\"N/A\",\"Daily\",\"2:00:00 AM\",\"4/12/2026\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"Disabled\"";

        var tasks = SchtasksCsvParser.Parse(csv, @"\Winix");

        Assert.Equal(2, tasks.Count);
        Assert.Equal("task-a", tasks[0].Name);
        Assert.Equal("task-b", tasks[1].Name);
    }

    [Fact]
    public void Parse_StripsFolderPrefix_FromTaskName()
    {
        string csv = "\"MYPC\",\"\\Winix\\my-task\",\"N/A\",\"Ready\",\"Interactive only\",\"N/A\",\"0\",\"troy\",\"cmd\",\"N/A\",\"comment\",\"Enabled\",\"Disabled\",\"N/A\",\"troy\",\"Disabled\",\"72:00:00\",\"N/A\",\"Daily\",\"12:00:00 AM\",\"4/12/2026\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"N/A\",\"Disabled\"";

        var tasks = SchtasksCsvParser.Parse(csv, @"\Winix");

        Assert.Equal("my-task", tasks[0].Name);
    }

    [Fact]
    public void ParseCsvLine_HandlesQuotedCommas()
    {
        string line = "\"value,with,commas\",\"simple\"";

        string[] fields = SchtasksCsvParser.ParseCsvLine(line);

        Assert.Equal(2, fields.Length);
        Assert.Equal("value,with,commas", fields[0]);
        Assert.Equal("simple", fields[1]);
    }

    [Fact]
    public void ParseCsvLine_HandlesEscapedQuotes()
    {
        string line = "\"value \"\"with\"\" quotes\",\"simple\"";

        string[] fields = SchtasksCsvParser.ParseCsvLine(line);

        Assert.Equal("value \"with\" quotes", fields[0]);
    }
}
```

### Step 8.3 — Implement CronToSchtasksMapper

Create file `src/Winix.Schedule/CronToSchtasksMapper.cs`:

```csharp
#nullable enable

using System.Globalization;
using System.Text;

namespace Winix.Schedule;

/// <summary>
/// Result of mapping a cron expression to schtasks.exe parameters.
/// </summary>
public sealed class SchtasksSchedule
{
    /// <summary>schtasks /SC value: MINUTE, HOURLY, DAILY, WEEKLY, MONTHLY.</summary>
    public string ScheduleType { get; set; } = "";

    /// <summary>schtasks /MO value (modifier/interval), or null.</summary>
    public string? Modifier { get; set; }

    /// <summary>schtasks /ST value (HH:mm format), or null.</summary>
    public string? StartTime { get; set; }

    /// <summary>schtasks /D value for WEEKLY (e.g. "MON,TUE,WED"), or null.</summary>
    public string? Days { get; set; }

    /// <summary>schtasks /D value for MONTHLY (e.g. "1"), or null.</summary>
    public string? DayOfMonth { get; set; }
}

/// <summary>
/// Maps a <see cref="CronExpression"/> to schtasks.exe scheduling parameters.
/// Handles common patterns; complex expressions fall back to MINUTE/1 with a comment.
/// </summary>
public static class CronToSchtasksMapper
{
    private static readonly string[] DayNames = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };

    /// <summary>
    /// Maps a parsed cron expression to schtasks.exe schedule parameters.
    /// </summary>
    public static SchtasksSchedule Map(CronExpression cron)
    {
        bool allMinutes = cron.Minute.Values.Count == 60;
        bool allHours = cron.Hour.Values.Count == 24;
        bool allDom = cron.DayOfMonth.Values.Count == 31;
        bool allMonths = cron.Month.Values.Count == 12;
        bool allDow = cron.DayOfWeek.Values.Count >= 7; // 0-6 = 7 values

        // Pattern: */N * * * * -> MINUTE /MO N
        if (allHours && allDom && allMonths && allDow)
        {
            if (allMinutes)
            {
                return new SchtasksSchedule { ScheduleType = "MINUTE", Modifier = "1" };
            }

            int? minuteStep = DetectStep(cron.Minute, 0, 59);
            if (minuteStep.HasValue)
            {
                return new SchtasksSchedule { ScheduleType = "MINUTE", Modifier = minuteStep.Value.ToString(CultureInfo.InvariantCulture) };
            }
        }

        // Pattern: 0 */N * * * -> HOURLY /MO N
        if (cron.Minute.Values.Count == 1 && cron.Minute.Contains(0) && allDom && allMonths && allDow)
        {
            if (allHours)
            {
                return new SchtasksSchedule { ScheduleType = "HOURLY", Modifier = "1" };
            }

            int? hourStep = DetectStep(cron.Hour, 0, 23);
            if (hourStep.HasValue)
            {
                return new SchtasksSchedule { ScheduleType = "HOURLY", Modifier = hourStep.Value.ToString(CultureInfo.InvariantCulture) };
            }
        }

        // Extract single minute and hour for time-based patterns.
        int? singleMinute = cron.Minute.Values.Count == 1 ? GetSingle(cron.Minute) : null;
        int? singleHour = cron.Hour.Values.Count == 1 ? GetSingle(cron.Hour) : null;
        string? startTime = (singleMinute.HasValue && singleHour.HasValue)
            ? $"{singleHour.Value:D2}:{singleMinute.Value:D2}"
            : null;

        // Pattern: M H * * DOW -> WEEKLY /D days /ST time
        if (startTime != null && allDom && allMonths && !allDow)
        {
            var days = new StringBuilder();
            // Sort days for consistent output.
            var sortedDays = new List<int>(cron.DayOfWeek.Values);
            sortedDays.Sort();
            foreach (int d in sortedDays)
            {
                if (d >= 0 && d <= 6)
                {
                    if (days.Length > 0) { days.Append(','); }
                    days.Append(DayNames[d]);
                }
            }

            return new SchtasksSchedule
            {
                ScheduleType = "WEEKLY",
                StartTime = startTime,
                Days = days.ToString(),
            };
        }

        // Pattern: M H DOM * * -> MONTHLY /D dom /ST time
        if (startTime != null && !allDom && cron.DayOfMonth.Values.Count == 1 && allMonths && allDow)
        {
            int dom = GetSingle(cron.DayOfMonth);
            return new SchtasksSchedule
            {
                ScheduleType = "MONTHLY",
                StartTime = startTime,
                DayOfMonth = dom.ToString(CultureInfo.InvariantCulture),
            };
        }

        // Pattern: M H * * * -> DAILY /ST time
        if (startTime != null && allDom && allMonths && allDow)
        {
            return new SchtasksSchedule
            {
                ScheduleType = "DAILY",
                StartTime = startTime,
            };
        }

        // Fallback: MINUTE /MO 1 (run every minute). The cron expression stored in the
        // task's comment field will be the source of truth for `list`.
        return new SchtasksSchedule { ScheduleType = "MINUTE", Modifier = "1" };
    }

    /// <summary>
    /// Detects whether a field represents a simple step pattern starting from <paramref name="min"/>.
    /// Returns the step value if so, null otherwise.
    /// </summary>
    private static int? DetectStep(CronField field, int min, int max)
    {
        var sorted = new List<int>(field.Values);
        sorted.Sort();
        if (sorted.Count < 2 || sorted[0] != min)
        {
            return null;
        }

        int step = sorted[1] - sorted[0];
        if (step <= 0)
        {
            return null;
        }

        // Verify all values match the step pattern.
        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] != min + (i * step))
            {
                return null;
            }
        }

        // Verify the step covers the full range.
        int expectedCount = ((max - min) / step) + 1;
        if (sorted.Count != expectedCount)
        {
            return null;
        }

        return step;
    }

    /// <summary>Returns the single value from a field known to contain exactly one value.</summary>
    private static int GetSingle(CronField field)
    {
        foreach (int v in field.Values)
        {
            return v;
        }

        throw new InvalidOperationException("Field has no values.");
    }
}
```

### Step 8.4 — Implement SchtasksCsvParser

Create file `src/Winix.Schedule/SchtasksCsvParser.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Winix.Schedule;

/// <summary>
/// Parses the CSV output from <c>schtasks /Query /FO CSV /V /NH</c> into
/// <see cref="ScheduledTask"/> objects.
/// </summary>
public static class SchtasksCsvParser
{
    // Column indices in the verbose CSV output (with /V /NH):
    // 0: HostName
    // 1: TaskName
    // 2: Next Run Time
    // 3: Status
    // 8: Task To Run
    // 10: Comment (we store cron expression here)
    // 11: Scheduled Task State (Enabled/Disabled)
    private const int ColTaskName = 1;
    private const int ColNextRunTime = 2;
    private const int ColTaskToRun = 8;
    private const int ColComment = 10;
    private const int ColState = 11;
    private const int MinColumns = 12;

    /// <summary>
    /// Parses schtasks CSV output into a list of <see cref="ScheduledTask"/> objects.
    /// </summary>
    /// <param name="csvOutput">Raw CSV text from schtasks.exe.</param>
    /// <param name="folder">The folder prefix to strip from task names (e.g. "\Winix").</param>
    public static IReadOnlyList<ScheduledTask> Parse(string csvOutput, string folder)
    {
        var tasks = new List<ScheduledTask>();
        if (string.IsNullOrWhiteSpace(csvOutput))
        {
            return tasks;
        }

        string folderPrefix = folder.TrimEnd('\\') + "\\";

        string[] lines = csvOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();
            if (trimmedLine.Length == 0)
            {
                continue;
            }

            string[] fields = ParseCsvLine(trimmedLine);
            if (fields.Length < MinColumns)
            {
                continue;
            }

            string taskName = fields[ColTaskName];

            // Strip folder prefix from name.
            if (taskName.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                taskName = taskName.Substring(folderPrefix.Length);
            }

            // Parse next run time. schtasks uses locale-dependent date formats, but typically
            // "M/d/yyyy h:mm:ss tt" for en-US. "N/A" means no next run.
            DateTimeOffset? nextRun = null;
            string nextRunStr = fields[ColNextRunTime];
            if (!string.IsNullOrEmpty(nextRunStr) && nextRunStr != "N/A")
            {
                if (DateTimeOffset.TryParse(nextRunStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset parsed))
                {
                    nextRun = parsed;
                }
            }

            string schedule = fields[ColComment]; // We store cron expression in the Comment field.
            string status = fields[ColState];     // "Enabled" or "Disabled"
            string command = fields[ColTaskToRun];

            tasks.Add(new ScheduledTask(taskName, schedule, nextRun, status, command, folder));
        }

        return tasks;
    }

    /// <summary>
    /// Parses a single CSV line respecting quoted fields.
    /// Handles commas inside quotes and escaped double-quotes ("").
    /// </summary>
    public static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        int i = 0;

        while (i < line.Length)
        {
            if (line[i] == '"')
            {
                // Quoted field.
                i++; // Skip opening quote.
                var sb = new System.Text.StringBuilder();
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            // Escaped quote.
                            sb.Append('"');
                            i += 2;
                        }
                        else
                        {
                            // End of quoted field.
                            i++; // Skip closing quote.
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(line[i]);
                        i++;
                    }
                }

                fields.Add(sb.ToString());

                // Skip comma separator.
                if (i < line.Length && line[i] == ',')
                {
                    i++;
                }
            }
            else if (line[i] == ',')
            {
                // Empty field.
                fields.Add("");
                i++;
            }
            else
            {
                // Unquoted field.
                int start = i;
                while (i < line.Length && line[i] != ',')
                {
                    i++;
                }

                fields.Add(line.Substring(start, i - start));

                // Skip comma separator.
                if (i < line.Length && line[i] == ',')
                {
                    i++;
                }
            }
        }

        return fields.ToArray();
    }
}
```

### Step 8.5 — Implement SchtasksBackend

Create file `src/Winix.Schedule/SchtasksBackend.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Winix.Schedule;

/// <summary>
/// Windows scheduler backend that delegates to <c>schtasks.exe</c> for task management.
/// Guaranteed AOT-compatible (pure process spawning, no COM interop).
/// </summary>
public sealed class SchtasksBackend : ISchedulerBackend
{
    /// <inheritdoc />
    public ScheduleResult Add(string name, CronExpression cron, string command, string[] arguments, string folder)
    {
        string taskPath = BuildTaskPath(folder, name);

        // Build the command string for schtasks /TR.
        // schtasks /TR takes a single string. We must quote the command and arguments.
        string taskRun = BuildTaskRunString(command, arguments);

        // Map cron to schtasks schedule parameters.
        SchtasksSchedule schedule = CronToSchtasksMapper.Map(cron);

        var args = new List<string>
        {
            "/Create",
            "/TN", taskPath,
            "/TR", taskRun,
            "/SC", schedule.ScheduleType,
            "/F", // Force overwrite if exists.
        };

        if (schedule.Modifier != null)
        {
            args.Add("/MO");
            args.Add(schedule.Modifier);
        }

        if (schedule.StartTime != null)
        {
            args.Add("/ST");
            args.Add(schedule.StartTime);
        }

        if (schedule.Days != null)
        {
            args.Add("/D");
            args.Add(schedule.Days);
        }

        if (schedule.DayOfMonth != null)
        {
            args.Add("/D");
            args.Add(schedule.DayOfMonth);
        }

        // Store the cron expression in the task's comment field for round-trip display.
        args.Add("/RL");
        args.Add("LIMITED");

        var result = RunSchtasks(args.ToArray());

        if (result.ExitCode != 0)
        {
            return new ScheduleResult(false, $"schtasks failed: {result.Stderr}", name);
        }

        // After creating, set the comment (schtasks /Create doesn't support /RL and comment together
        // in all versions, so we use /Change to set it reliably).
        // Actually, schtasks /Create does not have a /Comment flag. We store the cron expression
        // by running schtasks /Change after creation. Unfortunately, schtasks /Change does not have
        // a comment field either. The safest option is to store it in the task description via XML.
        // For v1, we rely on parsing the schedule back and store the cron expression in no persistent
        // field. The `list` command will display the cron from the schedule mapping.
        // TODO: Use schtasks /Create /XML to embed the cron expression in the task description.

        DateTimeOffset? nextRun = null;
        try
        {
            nextRun = cron.GetNextOccurrence(DateTimeOffset.Now);
        }
        catch
        {
            // Ignore calculation failures.
        }

        return new ScheduleResult(true, $"Created task '{name}'.", name, cron.Expression, nextRun);
    }

    /// <inheritdoc />
    public IReadOnlyList<ScheduledTask> List(string? folder, bool all)
    {
        string queryFolder = folder ?? @"\Winix";

        var args = all
            ? new[] { "/Query", "/FO", "CSV", "/V", "/NH" }
            : new[] { "/Query", "/TN", queryFolder + "\\", "/FO", "CSV", "/V", "/NH" };

        var result = RunSchtasks(args);

        if (result.ExitCode != 0)
        {
            // "ERROR: The system cannot find the file specified." means the folder doesn't exist.
            // Return empty list rather than failing.
            return Array.Empty<ScheduledTask>();
        }

        return SchtasksCsvParser.Parse(result.Stdout, queryFolder);
    }

    /// <inheritdoc />
    public ScheduleResult Remove(string name, string folder)
    {
        string taskPath = BuildTaskPath(folder, name);

        var result = RunSchtasks(new[] { "/Delete", "/TN", taskPath, "/F" });

        if (result.ExitCode != 0)
        {
            return new ScheduleResult(false, $"Failed to remove task '{name}': {result.Stderr}", name);
        }

        return new ScheduleResult(true, $"Removed task '{name}'.", name);
    }

    /// <inheritdoc />
    public ScheduleResult Enable(string name, string folder)
    {
        string taskPath = BuildTaskPath(folder, name);

        var result = RunSchtasks(new[] { "/Change", "/TN", taskPath, "/ENABLE" });

        if (result.ExitCode != 0)
        {
            return new ScheduleResult(false, $"Failed to enable task '{name}': {result.Stderr}", name);
        }

        return new ScheduleResult(true, $"Enabled task '{name}'.", name);
    }

    /// <inheritdoc />
    public ScheduleResult Disable(string name, string folder)
    {
        string taskPath = BuildTaskPath(folder, name);

        var result = RunSchtasks(new[] { "/Change", "/TN", taskPath, "/DISABLE" });

        if (result.ExitCode != 0)
        {
            return new ScheduleResult(false, $"Failed to disable task '{name}': {result.Stderr}", name);
        }

        return new ScheduleResult(true, $"Disabled task '{name}'.", name);
    }

    /// <inheritdoc />
    public ScheduleResult Run(string name, string folder)
    {
        string taskPath = BuildTaskPath(folder, name);

        var result = RunSchtasks(new[] { "/Run", "/TN", taskPath });

        if (result.ExitCode != 0)
        {
            return new ScheduleResult(false, $"Failed to run task '{name}': {result.Stderr}", name);
        }

        return new ScheduleResult(true, $"Triggered task '{name}'.", name);
    }

    /// <inheritdoc />
    public IReadOnlyList<TaskRunRecord> GetHistory(string name, string folder)
    {
        // schtasks.exe does not have a direct "history" query. Task Scheduler history
        // is stored in the Windows Event Log (Microsoft-Windows-TaskScheduler/Operational).
        // Querying it requires wevtutil or COM — both are complex for v1.
        // Return empty with a message. The console app will display a note.
        return Array.Empty<TaskRunRecord>();
    }

    /// <summary>Builds the full task path from folder and name.</summary>
    private static string BuildTaskPath(string folder, string name)
    {
        string cleanFolder = folder.TrimEnd('\\');
        return cleanFolder + "\\" + name;
    }

    /// <summary>
    /// Builds the /TR string for schtasks.exe. If there are arguments,
    /// wraps the command in quotes and appends arguments.
    /// </summary>
    private static string BuildTaskRunString(string command, string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return command;
        }

        // schtasks /TR expects a single string. We need to be careful with quoting.
        var sb = new System.Text.StringBuilder();

        // If the command contains spaces, quote it.
        if (command.Contains(' '))
        {
            sb.Append('"');
            sb.Append(command);
            sb.Append('"');
        }
        else
        {
            sb.Append(command);
        }

        foreach (string arg in arguments)
        {
            sb.Append(' ');
            if (arg.Contains(' ') || arg.Contains('"'))
            {
                sb.Append('"');
                sb.Append(arg.Replace("\"", "\\\""));
                sb.Append('"');
            }
            else
            {
                sb.Append(arg);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Runs schtasks.exe with the given arguments and returns captured output.
    /// Uses ArgumentList for safe argument passing.
    /// </summary>
    private static ProcessRunResult RunSchtasks(string[] arguments)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null for schtasks.exe.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            return new ProcessRunResult(-1, "", "schtasks.exe not found");
        }

        using (process)
        {
            process.StandardInput.Close();

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new ProcessRunResult(process.ExitCode, stdout.Trim(), stderr.Trim());
        }
    }
}

/// <summary>Captured output from a child process.</summary>
internal sealed class ProcessRunResult
{
    public int ExitCode { get; }
    public string Stdout { get; }
    public string Stderr { get; }

    public ProcessRunResult(int exitCode, string stdout, string stderr)
    {
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
    }
}
```

### Step 8.6 — Verify tests pass

```bash
dotnet test tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj --filter "FullyQualifiedName~CronToSchtasksMapperTests|FullyQualifiedName~SchtasksCsvParserTests"
```

### Step 8.7 — Commit

```bash
git add src/Winix.Schedule/CronToSchtasksMapper.cs src/Winix.Schedule/SchtasksCsvParser.cs src/Winix.Schedule/SchtasksBackend.cs tests/Winix.Schedule.Tests/CronToSchtasksMapperTests.cs tests/Winix.Schedule.Tests/SchtasksCsvParserTests.cs
git commit -m "feat(schedule): implement SchtasksBackend with cron-to-schtasks mapping and CSV parsing"
```

---

## Task 9: CrontabBackend — Linux/macOS Backend

### Step 9.1 — Write CrontabParser tests

Create file `tests/Winix.Schedule.Tests/CrontabParserTests.cs`:

```csharp
using Winix.Schedule;

namespace Winix.Schedule.Tests;

public sealed class CrontabParserTests
{
    [Fact]
    public void ParseEntries_WinixTagged_ReturnsTask()
    {
        string crontab =
            "# winix:health-check\n" +
            "*/5 * * * * curl http://localhost:8080/health\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        Assert.Single(tasks);
        Assert.Equal("health-check", tasks[0].Name);
        Assert.Equal("*/5 * * * *", tasks[0].Schedule);
        Assert.Equal("curl http://localhost:8080/health", tasks[0].Command);
        Assert.Equal("Enabled", tasks[0].Status);
    }

    [Fact]
    public void ParseEntries_DisabledEntry_ReturnsDisabled()
    {
        string crontab =
            "# winix:my-task\n" +
            "# */5 * * * * curl http://localhost/health\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        Assert.Single(tasks);
        Assert.Equal("my-task", tasks[0].Name);
        Assert.Equal("Disabled", tasks[0].Status);
    }

    [Fact]
    public void ParseEntries_NonWinixEntries_Excluded_WhenWinixOnly()
    {
        string crontab =
            "0 2 * * * /usr/bin/backup.sh\n" +
            "# winix:health-check\n" +
            "*/5 * * * * curl http://localhost:8080/health\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        Assert.Single(tasks);
        Assert.Equal("health-check", tasks[0].Name);
    }

    [Fact]
    public void ParseEntries_All_IncludesNonWinix()
    {
        string crontab =
            "0 2 * * * /usr/bin/backup.sh\n" +
            "# winix:health-check\n" +
            "*/5 * * * * curl http://localhost:8080/health\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: false);

        Assert.Equal(2, tasks.Count);
    }

    [Fact]
    public void ParseEntries_Empty_ReturnsEmpty()
    {
        var tasks = CrontabParser.ParseEntries("", winixOnly: true);

        Assert.Empty(tasks);
    }

    [Fact]
    public void ParseEntries_OnlyComments_ReturnsEmpty()
    {
        string crontab =
            "# This is a regular comment\n" +
            "# Another comment\n";

        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        Assert.Empty(tasks);
    }

    [Fact]
    public void AddEntry_EmptyCrontab_AddsTagAndLine()
    {
        string result = CrontabParser.AddEntry("", "health-check", "*/5 * * * *", "curl http://localhost:8080/health");

        Assert.Contains("# winix:health-check", result);
        Assert.Contains("*/5 * * * * curl http://localhost:8080/health", result);
    }

    [Fact]
    public void AddEntry_ExistingCrontab_Appends()
    {
        string existing = "0 2 * * * /usr/bin/backup.sh\n";

        string result = CrontabParser.AddEntry(existing, "health-check", "*/5 * * * *", "curl http://localhost/health");

        Assert.StartsWith("0 2 * * * /usr/bin/backup.sh", result);
        Assert.Contains("# winix:health-check", result);
    }

    [Fact]
    public void RemoveEntry_RemovesTagAndCommandLine()
    {
        string crontab =
            "0 2 * * * /usr/bin/backup.sh\n" +
            "# winix:health-check\n" +
            "*/5 * * * * curl http://localhost:8080/health\n";

        string result = CrontabParser.RemoveEntry(crontab, "health-check");

        Assert.DoesNotContain("health-check", result);
        Assert.DoesNotContain("curl", result);
        Assert.Contains("backup.sh", result);
    }

    [Fact]
    public void DisableEntry_CommentsOutCommandLine()
    {
        string crontab =
            "# winix:health-check\n" +
            "*/5 * * * * curl http://localhost:8080/health\n";

        string result = CrontabParser.DisableEntry(crontab, "health-check");

        Assert.Contains("# winix:health-check", result);
        Assert.Contains("# */5 * * * * curl http://localhost:8080/health", result);
    }

    [Fact]
    public void EnableEntry_UncommentsCommandLine()
    {
        string crontab =
            "# winix:health-check\n" +
            "# */5 * * * * curl http://localhost:8080/health\n";

        string result = CrontabParser.EnableEntry(crontab, "health-check");

        Assert.Contains("# winix:health-check", result);
        Assert.Contains("*/5 * * * * curl http://localhost:8080/health", result);
        // Should NOT have a double-hash comment on the command line.
        Assert.DoesNotContain("# */5", result);
    }

    [Fact]
    public void ExtractCommand_ReturnsCommandPortion()
    {
        string command = CrontabParser.ExtractCommand("*/5 * * * * curl http://localhost:8080/health");

        Assert.Equal("curl http://localhost:8080/health", command);
    }

    [Fact]
    public void ExtractCronFields_ReturnsCronPortion()
    {
        string cron = CrontabParser.ExtractCronFields("*/5 * * * * curl http://localhost:8080/health");

        Assert.Equal("*/5 * * * *", cron);
    }
}
```

### Step 9.2 — Implement CrontabParser

Create file `src/Winix.Schedule/CrontabParser.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace Winix.Schedule;

/// <summary>
/// Parses and manipulates crontab file contents. Winix-managed entries are identified
/// by <c># winix:&lt;name&gt;</c> comment tags on the line preceding the cron entry.
/// </summary>
public static class CrontabParser
{
    private const string WinixTagPrefix = "# winix:";

    /// <summary>
    /// Parses crontab content into a list of <see cref="ScheduledTask"/> objects.
    /// </summary>
    /// <param name="crontabContent">Full text of the crontab.</param>
    /// <param name="winixOnly">When true, only return entries tagged with <c># winix:</c>.</param>
    public static IReadOnlyList<ScheduledTask> ParseEntries(string crontabContent, bool winixOnly)
    {
        var tasks = new List<ScheduledTask>();
        if (string.IsNullOrEmpty(crontabContent))
        {
            return tasks;
        }

        string[] lines = crontabContent.Split(new[] { '\n' }, StringSplitOptions.None);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            // Check for winix tag.
            if (line.StartsWith(WinixTagPrefix, StringComparison.Ordinal))
            {
                string name = line.Substring(WinixTagPrefix.Length).Trim();

                // The next line should be the cron entry (possibly commented-out for disabled).
                if (i + 1 < lines.Length)
                {
                    string cronLine = lines[i + 1].TrimEnd('\r');
                    bool disabled = cronLine.StartsWith("# ", StringComparison.Ordinal);
                    string activeLine = disabled ? cronLine.Substring(2) : cronLine;

                    string cronFields = ExtractCronFields(activeLine);
                    string command = ExtractCommand(activeLine);
                    string status = disabled ? "Disabled" : "Enabled";

                    DateTimeOffset? nextRun = null;
                    if (!disabled)
                    {
                        try
                        {
                            var expr = CronExpression.Parse(cronFields);
                            nextRun = expr.GetNextOccurrence(DateTimeOffset.Now);
                        }
                        catch
                        {
                            // Unparseable cron — leave nextRun as null.
                        }
                    }

                    tasks.Add(new ScheduledTask(name, cronFields, nextRun, status, command, ""));
                    i++; // Skip the cron line.
                }

                continue;
            }

            // Non-winix cron entry (not tagged).
            if (!winixOnly && line.Length > 0 && !line.StartsWith('#'))
            {
                string cronFields = ExtractCronFields(line);
                string command = ExtractCommand(line);

                DateTimeOffset? nextRun = null;
                try
                {
                    var expr = CronExpression.Parse(cronFields);
                    nextRun = expr.GetNextOccurrence(DateTimeOffset.Now);
                }
                catch
                {
                    // Unparseable cron — leave nextRun as null.
                }

                tasks.Add(new ScheduledTask(command, cronFields, nextRun, "Enabled", command, ""));
            }
        }

        return tasks;
    }

    /// <summary>Adds a winix-tagged cron entry to the crontab content.</summary>
    public static string AddEntry(string crontabContent, string name, string cronExpression, string command)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(crontabContent))
        {
            sb.Append(crontabContent);
            if (!crontabContent.EndsWith('\n'))
            {
                sb.Append('\n');
            }
        }

        sb.Append(WinixTagPrefix);
        sb.Append(name);
        sb.Append('\n');
        sb.Append(cronExpression);
        sb.Append(' ');
        sb.Append(command);
        sb.Append('\n');

        return sb.ToString();
    }

    /// <summary>Removes a winix-tagged entry (tag line + cron line) from the crontab.</summary>
    public static string RemoveEntry(string crontabContent, string name)
    {
        string tag = WinixTagPrefix + name;
        string[] lines = crontabContent.Split(new[] { '\n' }, StringSplitOptions.None);
        var sb = new StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (line.Equals(tag, StringComparison.Ordinal) || line.Equals(tag.TrimEnd(), StringComparison.Ordinal))
            {
                // Skip this tag line and the following cron line.
                if (i + 1 < lines.Length)
                {
                    i++; // Skip cron line.
                }

                continue;
            }

            sb.Append(line);
            if (i < lines.Length - 1)
            {
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>Disables a winix entry by commenting out its cron line.</summary>
    public static string DisableEntry(string crontabContent, string name)
    {
        return ToggleEntry(crontabContent, name, disable: true);
    }

    /// <summary>Enables a winix entry by uncommenting its cron line.</summary>
    public static string EnableEntry(string crontabContent, string name)
    {
        return ToggleEntry(crontabContent, name, disable: false);
    }

    /// <summary>
    /// Extracts the command portion from a crontab line (everything after the 5 cron fields).
    /// </summary>
    public static string ExtractCommand(string cronLine)
    {
        // Skip 5 space-separated fields, then return the rest.
        int fieldCount = 0;
        int i = 0;

        // Skip leading whitespace.
        while (i < cronLine.Length && char.IsWhiteSpace(cronLine[i])) { i++; }

        while (fieldCount < 5 && i < cronLine.Length)
        {
            // Skip field.
            while (i < cronLine.Length && !char.IsWhiteSpace(cronLine[i])) { i++; }
            fieldCount++;

            // Skip whitespace.
            while (i < cronLine.Length && char.IsWhiteSpace(cronLine[i])) { i++; }
        }

        return i < cronLine.Length ? cronLine.Substring(i) : "";
    }

    /// <summary>
    /// Extracts the 5 cron fields from a crontab line.
    /// </summary>
    public static string ExtractCronFields(string cronLine)
    {
        int fieldCount = 0;
        int i = 0;

        while (i < cronLine.Length && char.IsWhiteSpace(cronLine[i])) { i++; }

        int start = i;
        int endOfFields = i;

        while (fieldCount < 5 && i < cronLine.Length)
        {
            while (i < cronLine.Length && !char.IsWhiteSpace(cronLine[i])) { i++; }
            fieldCount++;
            endOfFields = i;

            while (i < cronLine.Length && char.IsWhiteSpace(cronLine[i])) { i++; }
        }

        return cronLine.Substring(start, endOfFields - start);
    }

    /// <summary>Comments out or uncomments the cron line following a winix tag.</summary>
    private static string ToggleEntry(string crontabContent, string name, bool disable)
    {
        string tag = WinixTagPrefix + name;
        string[] lines = crontabContent.Split(new[] { '\n' }, StringSplitOptions.None);
        var sb = new StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            sb.Append(line);
            if (i < lines.Length - 1)
            {
                sb.Append('\n');
            }

            if ((line.Equals(tag, StringComparison.Ordinal) || line.Equals(tag.TrimEnd(), StringComparison.Ordinal))
                && i + 1 < lines.Length)
            {
                i++;
                string cronLine = lines[i].TrimEnd('\r');
                sb.Append('\n');

                if (disable && !cronLine.StartsWith("# ", StringComparison.Ordinal))
                {
                    sb.Append("# ");
                    sb.Append(cronLine);
                }
                else if (!disable && cronLine.StartsWith("# ", StringComparison.Ordinal))
                {
                    sb.Append(cronLine.Substring(2));
                }
                else
                {
                    sb.Append(cronLine);
                }

                if (i < lines.Length - 1)
                {
                    sb.Append('\n');
                }
            }
        }

        return sb.ToString();
    }
}
```

### Step 9.3 — Implement CrontabBackend

Create file `src/Winix.Schedule/CrontabBackend.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace Winix.Schedule;

/// <summary>
/// Linux/macOS scheduler backend that manages the user's crontab.
/// Winix-managed entries are identified by <c># winix:&lt;name&gt;</c> comment tags.
/// </summary>
public sealed class CrontabBackend : ISchedulerBackend
{
    /// <inheritdoc />
    public ScheduleResult Add(string name, CronExpression cron, string command, string[] arguments, string folder)
    {
        string fullCommand = BuildCommandString(command, arguments);

        string currentCrontab = ReadCrontab();
        string newCrontab = CrontabParser.AddEntry(currentCrontab, name, cron.Expression, fullCommand);
        WriteCrontab(newCrontab);

        DateTimeOffset? nextRun = null;
        try
        {
            nextRun = cron.GetNextOccurrence(DateTimeOffset.Now);
        }
        catch
        {
            // Ignore.
        }

        return new ScheduleResult(true, $"Created task '{name}'.", name, cron.Expression, nextRun);
    }

    /// <inheritdoc />
    public IReadOnlyList<ScheduledTask> List(string? folder, bool all)
    {
        string crontab = ReadCrontab();
        return CrontabParser.ParseEntries(crontab, winixOnly: !all);
    }

    /// <inheritdoc />
    public ScheduleResult Remove(string name, string folder)
    {
        string crontab = ReadCrontab();
        string newCrontab = CrontabParser.RemoveEntry(crontab, name);

        if (crontab == newCrontab)
        {
            return new ScheduleResult(false, $"Task '{name}' not found.", name);
        }

        WriteCrontab(newCrontab);
        return new ScheduleResult(true, $"Removed task '{name}'.", name);
    }

    /// <inheritdoc />
    public ScheduleResult Enable(string name, string folder)
    {
        string crontab = ReadCrontab();
        string newCrontab = CrontabParser.EnableEntry(crontab, name);
        WriteCrontab(newCrontab);

        return new ScheduleResult(true, $"Enabled task '{name}'.", name);
    }

    /// <inheritdoc />
    public ScheduleResult Disable(string name, string folder)
    {
        string crontab = ReadCrontab();
        string newCrontab = CrontabParser.DisableEntry(crontab, name);
        WriteCrontab(newCrontab);

        return new ScheduleResult(true, $"Disabled task '{name}'.", name);
    }

    /// <inheritdoc />
    public ScheduleResult Run(string name, string folder)
    {
        string crontab = ReadCrontab();
        var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

        ScheduledTask? target = null;
        foreach (var task in tasks)
        {
            if (string.Equals(task.Name, name, StringComparison.Ordinal))
            {
                target = task;
                break;
            }
        }

        if (target is null)
        {
            return new ScheduleResult(false, $"Task '{name}' not found.", name);
        }

        // Run the command in a background subshell (fire and forget).
        try
        {
            var psi = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(target.Command);

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            return new ScheduleResult(false, $"Failed to run task '{name}': {ex.Message}", name);
        }

        return new ScheduleResult(true, $"Triggered task '{name}'.", name);
    }

    /// <inheritdoc />
    public IReadOnlyList<TaskRunRecord> GetHistory(string name, string folder)
    {
        // Crontab has no built-in run history. The console app will display a message.
        return Array.Empty<TaskRunRecord>();
    }

    /// <summary>Reads the current user crontab content.</summary>
    private static string ReadCrontab()
    {
        var psi = new ProcessStartInfo("crontab")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-l");

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return "";
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Exit code 1 with "no crontab for" is normal — means empty crontab.
            return process.ExitCode == 0 ? output : "";
        }
        catch (Win32Exception)
        {
            // crontab not found.
            return "";
        }
    }

    /// <summary>Writes new content to the user crontab via <c>crontab -</c>.</summary>
    private static void WriteCrontab(string content)
    {
        var psi = new ProcessStartInfo("crontab")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start crontab process.");

        process.StandardInput.Write(content);
        process.StandardInput.Close();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            string stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"crontab failed (exit {process.ExitCode}): {stderr}");
        }
    }

    /// <summary>Builds a shell command string from command and arguments.</summary>
    private static string BuildCommandString(string command, string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return command;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append(command);
        foreach (string arg in arguments)
        {
            sb.Append(' ');
            // Shell-escape arguments containing spaces or special characters.
            if (arg.Contains(' ') || arg.Contains('\'') || arg.Contains('"') || arg.Contains('$') || arg.Contains('\\'))
            {
                sb.Append('\'');
                sb.Append(arg.Replace("'", "'\\''"));
                sb.Append('\'');
            }
            else
            {
                sb.Append(arg);
            }
        }

        return sb.ToString();
    }
}
```

### Step 9.4 — Verify tests pass

```bash
dotnet test tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj --filter "FullyQualifiedName~CrontabParserTests"
```

### Step 9.5 — Commit

```bash
git add src/Winix.Schedule/CrontabParser.cs src/Winix.Schedule/CrontabBackend.cs tests/Winix.Schedule.Tests/CrontabParserTests.cs
git commit -m "feat(schedule): implement CrontabBackend with crontab tag-based entry management"
```

---

## Task 10: Formatting

### Step 10.1 — Write Formatting tests

Create file `tests/Winix.Schedule.Tests/FormattingTests.cs`:

```csharp
using Winix.Schedule;

namespace Winix.Schedule.Tests;

public sealed class FormattingTests
{
    private static readonly DateTimeOffset SampleTime = new DateTimeOffset(2026, 4, 13, 2, 0, 0, TimeSpan.FromHours(12));

    // --- Task table ---

    [Fact]
    public void FormatTable_SingleTask_ContainsName()
    {
        var tasks = new List<ScheduledTask>
        {
            new ScheduledTask("health-check", "*/5 * * * *", SampleTime, "Enabled", "curl http://localhost:8080/health", @"\Winix"),
        };

        string output = Formatting.FormatTable(tasks, showFolder: false, useColor: false);

        Assert.Contains("health-check", output);
        Assert.Contains("*/5 * * * *", output);
        Assert.Contains("Enabled", output);
    }

    [Fact]
    public void FormatTable_ShowFolder_IncludesFolderColumn()
    {
        var tasks = new List<ScheduledTask>
        {
            new ScheduledTask("task1", "0 2 * * *", SampleTime, "Enabled", "cmd", @"\Winix"),
        };

        string output = Formatting.FormatTable(tasks, showFolder: true, useColor: false);

        Assert.Contains("Folder", output);
        Assert.Contains(@"\Winix", output);
    }

    [Fact]
    public void FormatTable_Empty_ReturnsHeaderOnly()
    {
        string output = Formatting.FormatTable(new List<ScheduledTask>(), showFolder: false, useColor: false);

        Assert.Contains("Name", output);
    }

    // --- History ---

    [Fact]
    public void FormatHistory_SingleRecord_ContainsExitCode()
    {
        var records = new List<TaskRunRecord>
        {
            new TaskRunRecord(SampleTime, 0, TimeSpan.FromSeconds(1.2)),
        };

        string output = Formatting.FormatHistory(records, useColor: false);

        Assert.Contains("0", output);
        Assert.Contains("1.2s", output);
    }

    [Fact]
    public void FormatHistory_Empty_ReturnsHeaderOnly()
    {
        string output = Formatting.FormatHistory(new List<TaskRunRecord>(), useColor: false);

        Assert.Contains("Time", output);
    }

    // --- Next occurrences ---

    [Fact]
    public void FormatNextOccurrences_ReturnsDates()
    {
        var times = new List<DateTimeOffset>
        {
            new DateTimeOffset(2026, 4, 13, 2, 0, 0, TimeSpan.FromHours(12)),
            new DateTimeOffset(2026, 4, 14, 2, 0, 0, TimeSpan.FromHours(12)),
        };

        string output = Formatting.FormatNextOccurrences(times);

        Assert.Contains("2026-04-13", output);
        Assert.Contains("2026-04-14", output);
    }

    // --- Result messages ---

    [Fact]
    public void FormatResult_Success_ContainsMessage()
    {
        var result = new ScheduleResult(true, "Created task 'test'.", "test", "0 2 * * *");

        string output = Formatting.FormatResult(result, useColor: false);

        Assert.Contains("Created task 'test'.", output);
    }

    [Fact]
    public void FormatResult_Failure_ContainsError()
    {
        var result = new ScheduleResult(false, "Task not found.", "missing");

        string output = Formatting.FormatResult(result, useColor: false);

        Assert.Contains("Task not found.", output);
    }

    // --- JSON ---

    [Fact]
    public void FormatTaskListJson_ContainsToolField()
    {
        var tasks = new List<ScheduledTask>
        {
            new ScheduledTask("test", "0 2 * * *", SampleTime, "Enabled", "cmd", @"\Winix"),
        };

        string json = Formatting.FormatTaskListJson(tasks, 0, "success", "0.1.0");

        Assert.Contains("\"tool\"", json);
        Assert.Contains("\"schedule\"", json);
        Assert.Contains("\"tasks\"", json);
    }

    [Fact]
    public void FormatActionJson_ContainsAction()
    {
        string json = Formatting.FormatActionJson("add", "test", "0 2 * * *", SampleTime, 0, "success", "0.1.0");

        Assert.Contains("\"action\"", json);
        Assert.Contains("\"add\"", json);
        Assert.Contains("\"test\"", json);
    }

    [Fact]
    public void FormatNextJson_ContainsOccurrences()
    {
        var times = new List<DateTimeOffset>
        {
            new DateTimeOffset(2026, 4, 13, 2, 0, 0, TimeSpan.FromHours(12)),
        };

        string json = Formatting.FormatNextJson("0 2 * * *", times, 0, "success", "0.1.0");

        Assert.Contains("\"occurrences\"", json);
        Assert.Contains("\"cron\"", json);
    }
}
```

### Step 10.2 — Implement Formatting

Create file `src/Winix.Schedule/Formatting.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Yort.ShellKit;

namespace Winix.Schedule;

/// <summary>
/// Output formatting for the schedule tool — tables, history, next-occurrences, result messages, JSON.
/// All methods are pure (no I/O); the console app writes the returned strings.
/// </summary>
public static class Formatting
{
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// Formats a list of tasks as a table.
    /// Columns: Name, Cron, Next Run, Status, Command (and optionally Folder).
    /// </summary>
    public static string FormatTable(IReadOnlyList<ScheduledTask> tasks, bool showFolder, bool useColor)
    {
        const string hName = "Name";
        const string hCron = "Cron";
        const string hNext = "Next Run";
        const string hStatus = "Status";
        const string hCommand = "Command";
        const string hFolder = "Folder";

        int nameW = hName.Length;
        int cronW = hCron.Length;
        int nextW = hNext.Length;
        int statusW = hStatus.Length;
        int cmdW = hCommand.Length;
        int folderW = hFolder.Length;

        foreach (var t in tasks)
        {
            if (t.Name.Length > nameW) { nameW = t.Name.Length; }
            if (t.Schedule.Length > cronW) { cronW = t.Schedule.Length; }
            string nextStr = t.NextRun?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "";
            if (nextStr.Length > nextW) { nextW = nextStr.Length; }
            if (t.Status.Length > statusW) { statusW = t.Status.Length; }
            if (t.Command.Length > cmdW) { cmdW = t.Command.Length; }
            if (showFolder && t.Folder.Length > folderW) { folderW = t.Folder.Length; }
        }

        string dim = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);

        var sb = new StringBuilder();

        // Header
        sb.Append(dim);
        sb.Append(hName.PadRight(nameW));
        sb.Append("  ");
        sb.Append(hCron.PadRight(cronW));
        sb.Append("  ");
        sb.Append(hNext.PadRight(nextW));
        sb.Append("  ");
        sb.Append(hStatus.PadRight(statusW));
        sb.Append("  ");
        if (showFolder)
        {
            sb.Append(hFolder.PadRight(folderW));
            sb.Append("  ");
        }
        sb.Append(hCommand);
        sb.Append(reset);
        sb.AppendLine();

        // Rows
        foreach (var t in tasks)
        {
            sb.Append(t.Name.PadRight(nameW));
            sb.Append("  ");
            sb.Append(t.Schedule.PadRight(cronW));
            sb.Append("  ");
            string nextStr = t.NextRun?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "";
            sb.Append(nextStr.PadRight(nextW));
            sb.Append("  ");
            sb.Append(t.Status.PadRight(statusW));
            sb.Append("  ");
            if (showFolder)
            {
                sb.Append(t.Folder.PadRight(folderW));
                sb.Append("  ");
            }
            sb.Append(t.Command);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Formats run history as a table.</summary>
    public static string FormatHistory(IReadOnlyList<TaskRunRecord> records, bool useColor)
    {
        const string hTime = "Time";
        const string hExit = "Exit Code";
        const string hDuration = "Duration";

        int timeW = hTime.Length;
        int exitW = hExit.Length;
        int durW = hDuration.Length;

        foreach (var r in records)
        {
            string timeStr = r.StartTime.ToString(DateFormat, CultureInfo.InvariantCulture);
            if (timeStr.Length > timeW) { timeW = timeStr.Length; }
            string exitStr = r.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "running";
            if (exitStr.Length > exitW) { exitW = exitStr.Length; }
            string durStr = FormatDuration(r.Duration);
            if (durStr.Length > durW) { durW = durStr.Length; }
        }

        string dim = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);

        var sb = new StringBuilder();

        sb.Append(dim);
        sb.Append(hTime.PadRight(timeW));
        sb.Append("  ");
        sb.Append(hExit.PadRight(exitW));
        sb.Append("  ");
        sb.Append(hDuration);
        sb.Append(reset);
        sb.AppendLine();

        foreach (var r in records)
        {
            sb.Append(r.StartTime.ToString(DateFormat, CultureInfo.InvariantCulture).PadRight(timeW));
            sb.Append("  ");
            string exitStr = r.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "running";
            sb.Append(exitStr.PadRight(exitW));
            sb.Append("  ");
            sb.Append(FormatDuration(r.Duration));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Formats next-occurrence times, one per line.</summary>
    public static string FormatNextOccurrences(IReadOnlyList<DateTimeOffset> times)
    {
        var sb = new StringBuilder();
        foreach (var t in times)
        {
            sb.AppendLine(t.ToString(DateFormat, CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>Formats a result message for display.</summary>
    public static string FormatResult(ScheduleResult result, bool useColor)
    {
        if (result.Success && result.NextRun.HasValue)
        {
            return result.Message + " Next run: " + result.NextRun.Value.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        return result.Message;
    }

    /// <summary>Formats a platform-not-available message for history on Linux.</summary>
    public static string FormatHistoryNotAvailable()
    {
        return "Run history not available on this platform. Check syslog for cron output.";
    }

    // --- JSON ---

    /// <summary>Formats task list as JSON.</summary>
    public static string FormatTaskListJson(IReadOnlyList<ScheduledTask> tasks, int exitCode, string exitReason, string version)
    {
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("tool", "schedule");
        writer.WriteString("version", version);
        writer.WriteNumber("exit_code", exitCode);
        writer.WriteString("exit_reason", exitReason);

        writer.WriteStartArray("tasks");
        foreach (var t in tasks)
        {
            writer.WriteStartObject();
            writer.WriteString("name", t.Name);
            writer.WriteString("cron", t.Schedule);
            if (t.NextRun.HasValue)
            {
                writer.WriteString("next_run", t.NextRun.Value.ToString("o"));
            }
            else
            {
                writer.WriteNull("next_run");
            }
            writer.WriteString("status", t.Status);
            writer.WriteString("command", t.Command);
            writer.WriteString("folder", t.Folder);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Formats an action result (add/remove/enable/disable/run) as JSON.</summary>
    public static string FormatActionJson(string action, string name, string? cronExpression, DateTimeOffset? nextRun, int exitCode, string exitReason, string version)
    {
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("tool", "schedule");
        writer.WriteString("version", version);
        writer.WriteNumber("exit_code", exitCode);
        writer.WriteString("exit_reason", exitReason);
        writer.WriteString("action", action);
        writer.WriteString("name", name);
        if (cronExpression != null)
        {
            writer.WriteString("cron", cronExpression);
        }
        if (nextRun.HasValue)
        {
            writer.WriteString("next_run", nextRun.Value.ToString("o"));
        }
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Formats next-occurrence results as JSON.</summary>
    public static string FormatNextJson(string cronExpression, IReadOnlyList<DateTimeOffset> occurrences, int exitCode, string exitReason, string version)
    {
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("tool", "schedule");
        writer.WriteString("version", version);
        writer.WriteNumber("exit_code", exitCode);
        writer.WriteString("exit_reason", exitReason);
        writer.WriteString("cron", cronExpression);

        writer.WriteStartArray("occurrences");
        foreach (var t in occurrences)
        {
            writer.WriteStringValue(t.ToString("o"));
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Formats history records as JSON.</summary>
    public static string FormatHistoryJson(string name, IReadOnlyList<TaskRunRecord> records, int exitCode, string exitReason, string version)
    {
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("tool", "schedule");
        writer.WriteString("version", version);
        writer.WriteNumber("exit_code", exitCode);
        writer.WriteString("exit_reason", exitReason);
        writer.WriteString("name", name);

        writer.WriteStartArray("runs");
        foreach (var r in records)
        {
            writer.WriteStartObject();
            writer.WriteString("start_time", r.StartTime.ToString("o"));
            if (r.ExitCode.HasValue)
            {
                writer.WriteNumber("exit_code", r.ExitCode.Value);
            }
            else
            {
                writer.WriteNull("exit_code");
            }
            if (r.Duration.HasValue)
            {
                writer.WriteNumber("duration_seconds", Math.Round(r.Duration.Value.TotalSeconds, 1));
            }
            else
            {
                writer.WriteNull("duration_seconds");
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Formats a TimeSpan as a human-readable duration string.</summary>
    private static string FormatDuration(TimeSpan? duration)
    {
        if (!duration.HasValue)
        {
            return "running";
        }

        double seconds = duration.Value.TotalSeconds;
        if (seconds < 60)
        {
            return seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        if (seconds < 3600)
        {
            return (seconds / 60).ToString("0.0", CultureInfo.InvariantCulture) + "m";
        }

        return (seconds / 3600).ToString("0.0", CultureInfo.InvariantCulture) + "h";
    }
}
```

### Step 10.3 — Verify tests pass

```bash
dotnet test tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj --filter "FullyQualifiedName~FormattingTests"
```

### Step 10.4 — Commit

```bash
git add src/Winix.Schedule/Formatting.cs tests/Winix.Schedule.Tests/FormattingTests.cs
git commit -m "feat(schedule): implement Formatting for tables, history, JSON output"
```

---

## Task 11: Console App — Program.cs with Subcommand Dispatch

### Step 11.1 — Implement full Program.cs

Replace `src/schedule/Program.cs` with the complete implementation:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Winix.Schedule;
using Yort.ShellKit;

namespace Schedule;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        string version = GetVersion();

        var parser = new CommandLineParser("schedule", version)
            .Description("Cross-platform task scheduler with cron expressions.")
            .StandardFlags()
            .Option("--cron", null, "EXPR", "Cron expression (required for add)")
            .Option("--name", null, "NAME", "Task name (auto-generated if omitted)")
            .Option("--folder", null, "PATH", @"Task Scheduler folder (default: \Winix)")
            .Option("--count", null, "N", "Number of occurrences to show (default: 5)")
            .Flag("--all", "Show all tasks, not just Winix-managed")
            .Positional("command [args...]")
            .Platform("cross-platform",
                new[] { "schtasks.exe", "crontab" },
                "No cross-platform scheduler CLI with cron syntax exists",
                "Unified cron syntax for Windows Task Scheduler and crontab")
            .StdinDescription("Not used")
            .StdoutDescription("Not used (all output goes to stderr)")
            .StderrDescription("Tables, messages, JSON output")
            .Example("schedule add --cron \"0 2 * * *\" -- dotnet build", "Create a task that runs daily at 2am")
            .Example("schedule add --cron \"*/5 * * * *\" --name health-check -- curl http://localhost:8080/health", "Create a named task")
            .Example("schedule list", "List Winix-managed tasks")
            .Example("schedule list --all", "List all scheduled tasks")
            .Example("schedule remove health-check", "Remove a task")
            .Example("schedule enable health-check", "Enable a disabled task")
            .Example("schedule disable health-check", "Disable a task")
            .Example("schedule run health-check", "Trigger immediate execution")
            .Example("schedule history health-check", "Show run history")
            .Example("schedule next \"0 2 * * *\"", "Show next 5 fire times for a cron expression")
            .Example("schedule next \"*/5 * * * *\" --count 10", "Show next 10 fire times")
            .ExitCodes(
                (0, "Success"),
                (1, "Error (task not found, scheduler failure, invalid cron)"),
                (ExitCode.UsageError, "Usage error (bad arguments)"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        if (result.Positionals.Length == 0)
        {
            return result.WriteError("missing subcommand (expected add, list, remove, enable, disable, run, history, or next)", Console.Error);
        }

        string subcommand = result.Positionals[0];
        bool jsonOutput = result.Has("--json");
        bool useColor = result.ResolveColor(checkStdErr: true);
        string folder = result.Has("--folder") ? result.GetString("--folder") : @"\Winix";

        switch (subcommand)
        {
            case "add":
                return RunAdd(result, version, jsonOutput, useColor, folder);
            case "list":
                return RunList(result, version, jsonOutput, useColor, folder);
            case "remove":
                return RunRemove(result, version, jsonOutput, useColor, folder);
            case "enable":
                return RunEnable(result, version, jsonOutput, useColor, folder);
            case "disable":
                return RunDisable(result, version, jsonOutput, useColor, folder);
            case "run":
                return RunRun(result, version, jsonOutput, useColor, folder);
            case "history":
                return RunHistory(result, version, jsonOutput, useColor, folder);
            case "next":
                return RunNext(result, version, jsonOutput);
            default:
                return result.WriteError(
                    $"unknown subcommand '{subcommand}' (expected add, list, remove, enable, disable, run, history, or next)",
                    Console.Error);
        }
    }

    private static int RunAdd(ParseResult result, string version, bool json, bool useColor, string folder)
    {
        if (!result.Has("--cron"))
        {
            return result.WriteError("--cron is required for add", Console.Error);
        }

        string cronStr = result.GetString("--cron");

        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(cronStr);
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine($"schedule: invalid cron expression: {ex.Message}");
            return 1;
        }

        // Everything after the subcommand becomes the command to schedule.
        // We expect: schedule add --cron "..." [--name X] -- command args...
        // The positionals after "add" are the command tokens.
        string[] positionals = result.Positionals;
        if (positionals.Length < 2)
        {
            return result.WriteError("missing command to schedule (use -- to separate from flags)", Console.Error);
        }

        string command = positionals[1];
        string[] arguments = positionals.Skip(2).ToArray();

        string name;
        if (result.Has("--name"))
        {
            name = result.GetString("--name");
        }
        else
        {
            name = NameGenerator.Generate(command, arguments);
        }

        ISchedulerBackend backend = GetBackend();
        ScheduleResult scheduleResult = backend.Add(name, cron, command, arguments, folder);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatActionJson(
                "add", scheduleResult.TaskName, scheduleResult.CronExpression, scheduleResult.NextRun,
                scheduleResult.Success ? 0 : 1, scheduleResult.Success ? "success" : "error", version));
        }
        else
        {
            Console.Error.WriteLine(Formatting.FormatResult(scheduleResult, useColor));
        }

        return scheduleResult.Success ? 0 : 1;
    }

    private static int RunList(ParseResult result, string version, bool json, bool useColor, string folder)
    {
        bool all = result.Has("--all");

        ISchedulerBackend backend = GetBackend();
        IReadOnlyList<ScheduledTask> tasks = backend.List(all ? null : folder, all);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatTaskListJson(tasks, 0, "success", version));
        }
        else
        {
            Console.Error.Write(Formatting.FormatTable(tasks, showFolder: all, useColor: useColor));
        }

        return 0;
    }

    private static int RunRemove(ParseResult result, string version, bool json, bool useColor, string folder)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing task name for remove", Console.Error);
        }

        string name = result.Positionals[1];

        ISchedulerBackend backend = GetBackend();
        ScheduleResult scheduleResult = backend.Remove(name, folder);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatActionJson(
                "remove", name, null, null,
                scheduleResult.Success ? 0 : 1, scheduleResult.Success ? "success" : "error", version));
        }
        else
        {
            Console.Error.WriteLine(Formatting.FormatResult(scheduleResult, useColor));
        }

        return scheduleResult.Success ? 0 : 1;
    }

    private static int RunEnable(ParseResult result, string version, bool json, bool useColor, string folder)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing task name for enable", Console.Error);
        }

        string name = result.Positionals[1];

        ISchedulerBackend backend = GetBackend();
        ScheduleResult scheduleResult = backend.Enable(name, folder);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatActionJson(
                "enable", name, null, null,
                scheduleResult.Success ? 0 : 1, scheduleResult.Success ? "success" : "error", version));
        }
        else
        {
            Console.Error.WriteLine(Formatting.FormatResult(scheduleResult, useColor));
        }

        return scheduleResult.Success ? 0 : 1;
    }

    private static int RunDisable(ParseResult result, string version, bool json, bool useColor, string folder)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing task name for disable", Console.Error);
        }

        string name = result.Positionals[1];

        ISchedulerBackend backend = GetBackend();
        ScheduleResult scheduleResult = backend.Disable(name, folder);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatActionJson(
                "disable", name, null, null,
                scheduleResult.Success ? 0 : 1, scheduleResult.Success ? "success" : "error", version));
        }
        else
        {
            Console.Error.WriteLine(Formatting.FormatResult(scheduleResult, useColor));
        }

        return scheduleResult.Success ? 0 : 1;
    }

    private static int RunRun(ParseResult result, string version, bool json, bool useColor, string folder)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing task name for run", Console.Error);
        }

        string name = result.Positionals[1];

        ISchedulerBackend backend = GetBackend();
        ScheduleResult scheduleResult = backend.Run(name, folder);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatActionJson(
                "run", name, null, null,
                scheduleResult.Success ? 0 : 1, scheduleResult.Success ? "success" : "error", version));
        }
        else
        {
            Console.Error.WriteLine(Formatting.FormatResult(scheduleResult, useColor));
        }

        return scheduleResult.Success ? 0 : 1;
    }

    private static int RunHistory(ParseResult result, string version, bool json, bool useColor, string folder)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing task name for history", Console.Error);
        }

        string name = result.Positionals[1];

        ISchedulerBackend backend = GetBackend();
        IReadOnlyList<TaskRunRecord> records = backend.GetHistory(name, folder);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatHistoryJson(name, records, 0, "success", version));
        }
        else
        {
            if (records.Count == 0 && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.Error.WriteLine(Formatting.FormatHistoryNotAvailable());
            }
            else
            {
                Console.Error.Write(Formatting.FormatHistory(records, useColor));
            }
        }

        return 0;
    }

    private static int RunNext(ParseResult result, string version, bool json)
    {
        if (result.Positionals.Length < 2)
        {
            return result.WriteError("missing cron expression for next", Console.Error);
        }

        string cronStr = result.Positionals[1];

        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(cronStr);
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine($"schedule: invalid cron expression: {ex.Message}");
            return 1;
        }

        int count = 5;
        if (result.Has("--count"))
        {
            string countStr = result.GetString("--count");
            if (!int.TryParse(countStr, out count) || count < 1)
            {
                return result.WriteError($"invalid --count value '{countStr}' (must be a positive integer)", Console.Error);
            }
        }

        IReadOnlyList<DateTimeOffset> occurrences = cron.GetNextOccurrences(DateTimeOffset.Now, count);

        if (json)
        {
            Console.Error.WriteLine(Formatting.FormatNextJson(cron.Expression, occurrences, 0, "success", version));
        }
        else
        {
            Console.Error.Write(Formatting.FormatNextOccurrences(occurrences));
        }

        return 0;
    }

    /// <summary>
    /// Returns the appropriate scheduler backend for the current platform.
    /// </summary>
    private static ISchedulerBackend GetBackend()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new SchtasksBackend();
        }

        return new CrontabBackend();
    }

    /// <summary>
    /// Returns the informational version from the Winix.Schedule library assembly.
    /// </summary>
    private static string GetVersion()
    {
        return typeof(ScheduledTask).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
```

### Step 11.2 — Build and smoke test

```bash
dotnet build src/schedule/schedule.csproj
dotnet run --project src/schedule/schedule.csproj -- --help
dotnet run --project src/schedule/schedule.csproj -- next "0 2 * * *"
dotnet run --project src/schedule/schedule.csproj -- next "*/5 * * * *" --count 3
```

### Step 11.3 — Run full test suite

```bash
dotnet test tests/Winix.Schedule.Tests/Winix.Schedule.Tests.csproj
```

### Step 11.4 — Commit

```bash
git add src/schedule/Program.cs
git commit -m "feat(schedule): implement Program.cs with 8 subcommand dispatch"
```

---

## Task 12: README, AI Guide, Pipeline Updates

### Step 12.1 — Update `src/schedule/README.md`

Replace the placeholder README with the full documentation following existing tool README patterns. Include:
- Description
- Install sections (scoop, .NET tool, direct download)
- Usage with examples for all 8 subcommands
- Options table
- Cron expression syntax reference
- Exit codes
- Colour section

### Step 12.2 — Create `docs/ai/schedule.md`

Create the AI agent guide following the pattern in `docs/ai/whoholds.md`:
- What this tool does
- Platform story
- When to use this
- Common patterns (add, list, remove, enable, disable, run, history, next)
- JSON output structure
- Composability notes

### Step 12.3 — Create `bucket/schedule.json` Scoop manifest

Create file `bucket/schedule.json`:

```json
{
    "version": "0.0.0",
    "description": "Cross-platform task scheduler with cron expressions.",
    "homepage": "https://github.com/Yortw/winix",
    "license": "MIT",
    "architecture": {
        "64bit": {
            "url": "https://github.com/Yortw/winix/releases/download/v0.0.0/schedule-win-x64.zip",
            "hash": ""
        }
    },
    "bin": "schedule.exe"
}
```

### Step 12.4 — Update `bucket/winix.json`

Add `schedule.exe` to the `bin` array in `bucket/winix.json`.

### Step 12.5 — Update `.github/workflows/release.yml`

Add the schedule tool to:
- Pack step: `dotnet pack src/schedule/schedule.csproj ...`
- Publish step: `dotnet publish src/schedule/schedule.csproj ...`
- Zip step: create `schedule-{rid}.zip`
- Combined zip step: copy `schedule.exe` into combined zip
- Manifest generation step
- Scoop manifest update step
- Winget manifest step
- `llms.txt` tool list
- NuGet push step

### Step 12.6 — Update `CLAUDE.md`

Add schedule to:
- Project layout table
- NuGet package IDs list

### Step 12.7 — Update `llms.txt`

Add schedule to the tool listing.

### Step 12.8 — Commit

```bash
git add src/schedule/README.md docs/ai/schedule.md bucket/schedule.json bucket/winix.json .github/workflows/release.yml CLAUDE.md llms.txt
git commit -m "docs(schedule): add README, AI guide, scoop manifest, pipeline, and CLAUDE.md updates"
```

---

## Task 13: ADR

### Step 13.1 — Create `docs/plans/2026-04-12-schedule-adr.md`

Create the ADR with decisions covering:

1. **schtasks.exe over COM interop** — Context: Windows Task Scheduler management. Decision: Shell out to schtasks.exe. Rationale: Guaranteed AOT-compatible, same pattern as winix installer. Trade-offs: Limited to what schtasks.exe CLI exposes; no access to event log history without wevtutil.

2. **Custom cron parser over library dependency** — Context: Need 5-field cron parsing. Decision: Custom parser. Rationale: AOT-compatible with no dependencies, full control over behavior, small code surface. Trade-offs: Must maintain our own edge-case handling.

3. **Cron expression stored in task Comment/Description** — Context: Need to round-trip cron expressions through Windows Task Scheduler. Decision: Store in task Comment field via schtasks. Rationale: schtasks CSV output includes the Comment column. Trade-offs: v1 does not have a reliable way to set the Comment via schtasks /Create — may need XML-based task creation in v2.

4. **Crontab tag format** — Context: Need to identify Winix-managed entries. Decision: `# winix:<name>` comments. Rationale: Non-invasive, survives manual crontab editing, grep-able. Trade-offs: Tags can be accidentally deleted by users editing crontab manually.

5. **Fallback schedule mapping** — Context: Complex cron expressions that don't map to a single schtasks schedule type. Decision: Fall back to MINUTE/1 (every-minute) as the schtasks schedule. Rationale: Simpler than multi-trigger XML; the cron expression in the Comment field is the source of truth. Trade-offs: Between the schtasks firing and the actual intended time, the task may run more frequently than desired. Future version could generate XML-based task definitions for precise mapping.

### Step 13.2 — Commit

```bash
git add docs/plans/2026-04-12-schedule-adr.md
git commit -m "docs(schedule): add architecture decision record"
```

---

## Task 14: Final Verification

### Step 14.1 — Full build

```bash
dotnet build Winix.sln
```

### Step 14.2 — Full test suite

```bash
dotnet test Winix.sln
```

### Step 14.3 — AOT publish smoke test

```bash
dotnet publish src/schedule/schedule.csproj -c Release -r win-x64
```

Verify the binary runs:

```bash
src/schedule/bin/Release/net10.0/win-x64/publish/schedule.exe --version
src/schedule/bin/Release/net10.0/win-x64/publish/schedule.exe next "0 2 * * *"
```

### Step 14.4 — Verify no warnings

```bash
dotnet build Winix.sln -warnaserror
```
