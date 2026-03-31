# files + AI Discoverability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `files` cross-platform file finder tool with shared `Winix.FileWalk` library, add `--describe` AI discoverability to ShellKit, and create `llms.txt` + AI guide docs.

**Architecture:** Three new projects (`Winix.FileWalk` shared library, `Winix.Files` formatting library, `files` console app) plus ShellKit enhancements (`GitIgnoreFilter`, `--describe` fluent API). `Winix.FileWalk` owns directory walking and predicate filtering. `Winix.Files` owns output formatting. The console app is thin — arg parsing, pipeline wiring, exit codes. ShellKit's `CommandLineParser` gains fluent methods for `--describe` metadata.

**Tech Stack:** .NET 10, C#, xUnit, `Microsoft.Extensions.FileSystemGlobbing` for glob matching, `System.Text.RegularExpressions` for regex, hand-crafted JSON (no serialiser — matches existing pattern).

**Specs:**
- `docs/plans/2026-03-31-files-design.md` — files tool design
- `docs/plans/2026-03-31-files-adr.md` — files tool ADR
- `docs/plans/2026-03-31-ai-discoverability-design.md` — AI discoverability design
- `docs/plans/2026-03-31-ai-discoverability-adr.md` — AI discoverability ADR
- `docs/plans/2026-03-29-winix-cli-conventions.md` — CLI conventions (JSON, flags, exit codes)

**Conventions:**
- TDD: write failing test, implement, verify, commit
- Full braces always, nullable reference types enabled, warnings as errors
- Console apps: `namespace`/`class Program`/`static Main` — no top-level statements
- Class libraries: `IsAotCompatible`, `IsTrimmable`
- All output formatting in class library, all I/O in console app
- Summary output to stderr, data output to stdout
- Respect `NO_COLOR` env var

---

## File Map

### New Files

**ShellKit enhancements:**
- `src/Yort.ShellKit/GitIgnoreFilter.cs` — wraps `git check-ignore --stdin -z`
- `tests/Yort.ShellKit.Tests/GitIgnoreFilterTests.cs`
- `tests/Yort.ShellKit.Tests/DescribeTests.cs` — tests for `--describe` JSON output

**Winix.FileWalk (shared library):**
- `src/Winix.FileWalk/Winix.FileWalk.csproj`
- `src/Winix.FileWalk/FileEntry.cs` — immutable result record
- `src/Winix.FileWalk/FileEntryType.cs` — enum
- `src/Winix.FileWalk/FileWalkerOptions.cs` — immutable config record
- `src/Winix.FileWalk/FileWalker.cs` — directory walking engine
- `src/Winix.FileWalk/SizeParser.cs` — parse `100k`, `10M`, `1G`
- `src/Winix.FileWalk/DurationParser.cs` — parse `30s`, `5m`, `1h`, `7d`
- `src/Winix.FileWalk/ContentDetector.cs` — text vs binary detection
- `src/Winix.FileWalk/GlobMatcher.cs` — wrapper around FileSystemGlobbing
- `tests/Winix.FileWalk.Tests/Winix.FileWalk.Tests.csproj`
- `tests/Winix.FileWalk.Tests/SizeParserTests.cs`
- `tests/Winix.FileWalk.Tests/DurationParserTests.cs`
- `tests/Winix.FileWalk.Tests/ContentDetectorTests.cs`
- `tests/Winix.FileWalk.Tests/GlobMatcherTests.cs`
- `tests/Winix.FileWalk.Tests/FileWalkerTests.cs`

**Winix.Files (formatting library):**
- `src/Winix.Files/Winix.Files.csproj`
- `src/Winix.Files/Formatting.cs` — all output format methods
- `tests/Winix.Files.Tests/Winix.Files.Tests.csproj`
- `tests/Winix.Files.Tests/FormattingTests.cs`

**files (console app):**
- `src/files/files.csproj`
- `src/files/Program.cs`
- `src/files/README.md`

**AI discoverability docs:**
- `llms.txt` — repo root
- `docs/ai/files.md` — AI agent guide for files

### Modified Files

- `src/Yort.ShellKit/CommandLineParser.cs` — add `--describe` fluent methods + `GenerateDescribe()` + `StandardFlags()` update
- `src/Yort.ShellKit/ParseResult.cs` — no changes needed (IsHandled already works)
- `Winix.sln` — add new projects
- `CLAUDE.md` — update project layout section
- `bucket/winix.json` — add files binary to combined scoop manifest
- `.github/workflows/release.yml` — add files to build/pack/publish steps

---

## Task 1: Project Scaffolding

Create all new project files and add them to the solution. No logic yet — just the skeleton.

**Files:**
- Create: `src/Winix.FileWalk/Winix.FileWalk.csproj`
- Create: `src/Winix.Files/Winix.Files.csproj`
- Create: `src/files/files.csproj`
- Create: `tests/Winix.FileWalk.Tests/Winix.FileWalk.Tests.csproj`
- Create: `tests/Winix.Files.Tests/Winix.Files.Tests.csproj`
- Modify: `Winix.sln`

- [ ] **Step 1: Create Winix.FileWalk class library project**

```xml
<!-- src/Winix.FileWalk/Winix.FileWalk.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
    <IsPackable>false</IsPackable>
    <PackageId>Winix.FileWalk.Library</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.FileSystemGlobbing" Version="10.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create Winix.Files class library project**

```xml
<!-- src/Winix.Files/Winix.Files.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
    <IsPackable>false</IsPackable>
    <PackageId>Winix.Files.Library</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.FileWalk\Winix.FileWalk.csproj" />
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create files console app project**

```xml
<!-- src/files/files.csproj -->
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
    <ToolCommandName>files</ToolCommandName>
    <PackageId>Winix.Files</PackageId>
    <Description>Find files by name, size, date, type, and content. A cross-platform find replacement with glob patterns, JSON output, and AI discoverability.</Description>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.Files\Winix.Files.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create test projects**

```xml
<!-- tests/Winix.FileWalk.Tests/Winix.FileWalk.Tests.csproj -->
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
    <ProjectReference Include="..\..\src\Winix.FileWalk\Winix.FileWalk.csproj" />
  </ItemGroup>
</Project>
```

```xml
<!-- tests/Winix.Files.Tests/Winix.Files.Tests.csproj -->
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
    <ProjectReference Include="..\..\src\Winix.Files\Winix.Files.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Add placeholder source files so projects compile**

Each project needs at least one source file. Create empty marker files:

```csharp
// src/Winix.FileWalk/FileEntry.cs
namespace Winix.FileWalk;

/// <summary>Represents a file system entry found during walking.</summary>
public sealed record FileEntry();
```

```csharp
// src/Winix.Files/Formatting.cs
namespace Winix.Files;

/// <summary>Output formatting for the files tool.</summary>
public static class Formatting
{
}
```

```csharp
// src/files/Program.cs
namespace Files;

internal sealed class Program
{
    static int Main(string[] args)
    {
        return 0;
    }
}
```

```csharp
// tests/Winix.FileWalk.Tests/SizeParserTests.cs
namespace Winix.FileWalk.Tests;

public class SizeParserTests
{
}
```

```csharp
// tests/Winix.Files.Tests/FormattingTests.cs
namespace Winix.Files.Tests;

public class FormattingTests
{
}
```

- [ ] **Step 6: Add all projects to Winix.sln**

Run:
```bash
dotnet sln Winix.sln add src/Winix.FileWalk/Winix.FileWalk.csproj --solution-folder src
dotnet sln Winix.sln add src/Winix.Files/Winix.Files.csproj --solution-folder src
dotnet sln Winix.sln add src/files/files.csproj --solution-folder src
dotnet sln Winix.sln add tests/Winix.FileWalk.Tests/Winix.FileWalk.Tests.csproj --solution-folder tests
dotnet sln Winix.sln add tests/Winix.Files.Tests/Winix.Files.Tests.csproj --solution-folder tests
```

- [ ] **Step 7: Verify build and test**

Run: `dotnet build Winix.sln`
Expected: Build succeeded, 0 warnings, 0 errors

Run: `dotnet test Winix.sln`
Expected: All existing tests pass, new test projects have 0 tests

- [ ] **Step 8: Commit**

```bash
git add src/Winix.FileWalk/ src/Winix.Files/ src/files/ tests/Winix.FileWalk.Tests/ tests/Winix.Files.Tests/ Winix.sln
git commit -m "chore: scaffold files tool, Winix.FileWalk, and Winix.Files projects"
```

---

## Task 2: SizeParser

Parse human-friendly size strings (`100`, `100k`, `10M`, `1G`) to bytes. Pure logic, no I/O.

**Files:**
- Create: `src/Winix.FileWalk/SizeParser.cs`
- Create: `tests/Winix.FileWalk.Tests/SizeParserTests.cs` (replace placeholder)

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Winix.FileWalk.Tests/SizeParserTests.cs
using Winix.FileWalk;
using Xunit;

namespace Winix.FileWalk.Tests;

public class SizeParserTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    [InlineData("1k", 1024)]
    [InlineData("1K", 1024)]
    [InlineData("10k", 10240)]
    [InlineData("1m", 1048576)]
    [InlineData("1M", 1048576)]
    [InlineData("1g", 1073741824)]
    [InlineData("1G", 1073741824)]
    [InlineData("512k", 524288)]
    public void Parse_ValidInput_ReturnsBytes(string input, long expected)
    {
        long result = SizeParser.Parse(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("k")]
    [InlineData("-1k")]
    [InlineData("1.5k")]
    [InlineData("1x")]
    public void Parse_InvalidInput_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => SizeParser.Parse(input));
    }

    [Fact]
    public void TryParse_ValidInput_ReturnsTrueAndValue()
    {
        bool ok = SizeParser.TryParse("10M", out long bytes);
        Assert.True(ok);
        Assert.Equal(10485760, bytes);
    }

    [Fact]
    public void TryParse_InvalidInput_ReturnsFalse()
    {
        bool ok = SizeParser.TryParse("bad", out long bytes);
        Assert.False(ok);
        Assert.Equal(0, bytes);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.FileWalk.Tests --filter "FullyQualifiedName~SizeParserTests" -v quiet`
Expected: Build error — `SizeParser` does not exist

- [ ] **Step 3: Implement SizeParser**

```csharp
// src/Winix.FileWalk/SizeParser.cs
using System.Globalization;

namespace Winix.FileWalk;

/// <summary>
/// Parses human-friendly size strings (e.g. "100k", "10M", "1G") to byte counts.
/// Suffixes are case-insensitive and use binary units (k=1024, M=1024^2, G=1024^3).
/// </summary>
public static class SizeParser
{
    /// <summary>Parses a size string to bytes. Throws <see cref="FormatException"/> on invalid input.</summary>
    public static long Parse(string value)
    {
        if (!TryParse(value, out long bytes))
        {
            throw new FormatException($"Invalid size: '{value}'. Expected a non-negative integer optionally followed by k, M, or G.");
        }
        return bytes;
    }

    /// <summary>Tries to parse a size string to bytes.</summary>
    public static bool TryParse(string value, out long bytes)
    {
        bytes = 0;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        long multiplier = 1;
        ReadOnlySpan<char> digits = value.AsSpan();

        char last = value[value.Length - 1];
        if (!char.IsDigit(last))
        {
            multiplier = char.ToLowerInvariant(last) switch
            {
                'k' => 1024L,
                'm' => 1024L * 1024,
                'g' => 1024L * 1024 * 1024,
                _ => -1
            };

            if (multiplier < 0)
            {
                return false;
            }

            digits = value.AsSpan(0, value.Length - 1);
        }

        if (digits.Length == 0)
        {
            return false;
        }

        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long raw))
        {
            return false;
        }

        if (raw < 0)
        {
            return false;
        }

        bytes = raw * multiplier;
        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.FileWalk.Tests --filter "FullyQualifiedName~SizeParserTests" -v quiet`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add src/Winix.FileWalk/SizeParser.cs tests/Winix.FileWalk.Tests/SizeParserTests.cs
git commit -m "feat(filewalk): SizeParser for human-friendly size strings"
```

---

## Task 3: DurationParser

Parse human-friendly duration strings (`30s`, `5m`, `1h`, `7d`, `2w`) to `TimeSpan`.

**Files:**
- Create: `src/Winix.FileWalk/DurationParser.cs`
- Create: `tests/Winix.FileWalk.Tests/DurationParserTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Winix.FileWalk.Tests/DurationParserTests.cs
using Winix.FileWalk;
using Xunit;

namespace Winix.FileWalk.Tests;

public class DurationParserTests
{
    [Theory]
    [InlineData("30s", 30)]
    [InlineData("1s", 1)]
    [InlineData("0s", 0)]
    public void Parse_Seconds_ReturnsCorrectTimeSpan(string input, int expectedSeconds)
    {
        TimeSpan result = DurationParser.Parse(input);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Theory]
    [InlineData("5m", 5 * 60)]
    [InlineData("1m", 60)]
    [InlineData("90m", 90 * 60)]
    public void Parse_Minutes_ReturnsCorrectTimeSpan(string input, int expectedSeconds)
    {
        TimeSpan result = DurationParser.Parse(input);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Theory]
    [InlineData("1h", 3600)]
    [InlineData("24h", 86400)]
    public void Parse_Hours_ReturnsCorrectTimeSpan(string input, int expectedSeconds)
    {
        TimeSpan result = DurationParser.Parse(input);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Theory]
    [InlineData("1d", 86400)]
    [InlineData("7d", 604800)]
    public void Parse_Days_ReturnsCorrectTimeSpan(string input, int expectedSeconds)
    {
        TimeSpan result = DurationParser.Parse(input);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Theory]
    [InlineData("1w", 604800)]
    [InlineData("2w", 1209600)]
    public void Parse_Weeks_ReturnsCorrectTimeSpan(string input, int expectedSeconds)
    {
        TimeSpan result = DurationParser.Parse(input);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("s")]
    [InlineData("-1h")]
    [InlineData("1.5h")]
    [InlineData("1x")]
    [InlineData("100")]
    public void Parse_InvalidInput_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => DurationParser.Parse(input));
    }

    [Fact]
    public void TryParse_ValidInput_ReturnsTrueAndValue()
    {
        bool ok = DurationParser.TryParse("2h", out TimeSpan duration);
        Assert.True(ok);
        Assert.Equal(TimeSpan.FromHours(2), duration);
    }

    [Fact]
    public void TryParse_InvalidInput_ReturnsFalse()
    {
        bool ok = DurationParser.TryParse("bad", out TimeSpan duration);
        Assert.False(ok);
        Assert.Equal(TimeSpan.Zero, duration);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.FileWalk.Tests --filter "FullyQualifiedName~DurationParserTests" -v quiet`
Expected: Build error — `DurationParser` does not exist

- [ ] **Step 3: Implement DurationParser**

```csharp
// src/Winix.FileWalk/DurationParser.cs
using System.Globalization;

namespace Winix.FileWalk;

/// <summary>
/// Parses human-friendly duration strings (e.g. "30s", "5m", "1h", "7d", "2w") to <see cref="TimeSpan"/>.
/// A suffix is required: s (seconds), m (minutes), h (hours), d (days), w (weeks).
/// </summary>
public static class DurationParser
{
    /// <summary>Parses a duration string. Throws <see cref="FormatException"/> on invalid input.</summary>
    public static TimeSpan Parse(string value)
    {
        if (!TryParse(value, out TimeSpan duration))
        {
            throw new FormatException($"Invalid duration: '{value}'. Expected a non-negative integer followed by s, m, h, d, or w.");
        }
        return duration;
    }

    /// <summary>Tries to parse a duration string.</summary>
    public static bool TryParse(string value, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;

        if (string.IsNullOrEmpty(value) || value.Length < 2)
        {
            return false;
        }

        char suffix = value[value.Length - 1];
        ReadOnlySpan<char> digits = value.AsSpan(0, value.Length - 1);

        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long raw))
        {
            return false;
        }

        if (raw < 0)
        {
            return false;
        }

        duration = suffix switch
        {
            's' => TimeSpan.FromSeconds(raw),
            'm' => TimeSpan.FromMinutes(raw),
            'h' => TimeSpan.FromHours(raw),
            'd' => TimeSpan.FromDays(raw),
            'w' => TimeSpan.FromDays(raw * 7),
            _ => TimeSpan.MinValue
        };

        if (duration == TimeSpan.MinValue)
        {
            duration = TimeSpan.Zero;
            return false;
        }

        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.FileWalk.Tests --filter "FullyQualifiedName~DurationParserTests" -v quiet`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add src/Winix.FileWalk/DurationParser.cs tests/Winix.FileWalk.Tests/DurationParserTests.cs
git commit -m "feat(filewalk): DurationParser for human-friendly duration strings"
```

---

## Task 4: ContentDetector (text vs binary)

Detect whether a file is text or binary using the null-byte heuristic (same as git).

**Files:**
- Create: `src/Winix.FileWalk/ContentDetector.cs`
- Create: `tests/Winix.FileWalk.Tests/ContentDetectorTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Winix.FileWalk.Tests/ContentDetectorTests.cs
using Winix.FileWalk;
using Xunit;

namespace Winix.FileWalk.Tests;

public class ContentDetectorTests
{
    [Fact]
    public void IsTextFile_PlainTextContent_ReturnsTrue()
    {
        string path = CreateTempFile("Hello, world!\nThis is a text file.\n");
        try
        {
            Assert.True(ContentDetector.IsTextFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsTextFile_BinaryContent_ReturnsFalse()
    {
        string path = CreateTempFileBytes(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 });
        try
        {
            Assert.False(ContentDetector.IsTextFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsTextFile_EmptyFile_ReturnsTrue()
    {
        string path = CreateTempFile("");
        try
        {
            Assert.True(ContentDetector.IsTextFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsTextFile_TextWithUtf8Bom_ReturnsTrue()
    {
        byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF };
        byte[] content = System.Text.Encoding.UTF8.GetBytes("Hello UTF-8");
        byte[] full = new byte[bom.Length + content.Length];
        bom.CopyTo(full, 0);
        content.CopyTo(full, bom.Length);

        string path = CreateTempFileBytes(full);
        try
        {
            Assert.True(ContentDetector.IsTextFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsTextFile_NullByteInMiddle_ReturnsFalse()
    {
        byte[] content = System.Text.Encoding.UTF8.GetBytes("Hello\0World");
        string path = CreateTempFileBytes(content);
        try
        {
            Assert.False(ContentDetector.IsTextFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsTextFile_LargeTextFile_OnlyReadsFirst8KB()
    {
        // Create a file larger than 8KB: 8KB of text then a null byte
        byte[] textPart = new byte[8192];
        Array.Fill(textPart, (byte)'A');
        byte[] full = new byte[8193];
        textPart.CopyTo(full, 0);
        full[8192] = 0x00; // null byte after 8KB boundary

        string path = CreateTempFileBytes(full);
        try
        {
            // Should be detected as text because null byte is past 8KB
            Assert.True(ContentDetector.IsTextFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsTextFile_NonExistentFile_ReturnsFalse()
    {
        Assert.False(ContentDetector.IsTextFile("/nonexistent/file/path.txt"));
    }

    private static string CreateTempFile(string content)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }

    private static string CreateTempFileBytes(byte[] content)
    {
        string path = Path.GetTempFileName();
        File.WriteAllBytes(path, content);
        return path;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.FileWalk.Tests --filter "FullyQualifiedName~ContentDetectorTests" -v quiet`
Expected: Build error — `ContentDetector` does not exist

- [ ] **Step 3: Implement ContentDetector**

```csharp
// src/Winix.FileWalk/ContentDetector.cs
namespace Winix.FileWalk;

/// <summary>
/// Detects whether a file contains text or binary content using the null-byte heuristic.
/// Reads the first 8KB and checks for null bytes — the same method git uses.
/// </summary>
public static class ContentDetector
{
    private const int SampleSize = 8192;

    /// <summary>
    /// Returns true if the file appears to be a text file (no null bytes in the first 8KB).
    /// Returns true for empty files. Returns false if the file cannot be read.
    /// </summary>
    public static bool IsTextFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] buffer = new byte[SampleSize];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                {
                    return false;
                }
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.FileWalk.Tests --filter "FullyQualifiedName~ContentDetectorTests" -v quiet`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add src/Winix.FileWalk/ContentDetector.cs tests/Winix.FileWalk.Tests/ContentDetectorTests.cs
git commit -m "feat(filewalk): ContentDetector for text vs binary detection"
```

---

## Task 5: Core Types (FileEntry, FileEntryType, FileWalkerOptions)

Define the immutable data types used throughout the walking engine.

**Files:**
- Modify: `src/Winix.FileWalk/FileEntry.cs` (replace placeholder)
- Create: `src/Winix.FileWalk/FileEntryType.cs`
- Create: `src/Winix.FileWalk/FileWalkerOptions.cs`

- [ ] **Step 1: Define FileEntryType enum**

```csharp
// src/Winix.FileWalk/FileEntryType.cs
namespace Winix.FileWalk;

/// <summary>The type of a file system entry.</summary>
public enum FileEntryType
{
    /// <summary>A regular file.</summary>
    File,

    /// <summary>A directory.</summary>
    Directory,

    /// <summary>A symbolic link.</summary>
    Symlink
}
```

- [ ] **Step 2: Define FileEntry record**

```csharp
// src/Winix.FileWalk/FileEntry.cs
namespace Winix.FileWalk;

/// <summary>
/// Represents a file system entry found during directory walking.
/// Immutable — produced by <see cref="FileWalker"/> and consumed by formatters.
/// </summary>
/// <param name="Path">Relative or absolute path (determined by walker options).</param>
/// <param name="Name">Filename only (no directory component).</param>
/// <param name="Type">File, directory, or symlink.</param>
/// <param name="SizeBytes">File size in bytes. -1 for directories.</param>
/// <param name="Modified">Last modified timestamp.</param>
/// <param name="Depth">Depth relative to the search root (root entries are depth 0).</param>
/// <param name="IsText">True if text, false if binary. Null unless --text/--binary detection was requested.</param>
public sealed record FileEntry(
    string Path,
    string Name,
    FileEntryType Type,
    long SizeBytes,
    DateTimeOffset Modified,
    int Depth,
    bool? IsText);
```

- [ ] **Step 3: Define FileWalkerOptions record**

```csharp
// src/Winix.FileWalk/FileWalkerOptions.cs
namespace Winix.FileWalk;

/// <summary>
/// Immutable configuration for <see cref="FileWalker"/>. All filtering predicates and
/// behaviour flags are set here before walking begins.
/// </summary>
public sealed record FileWalkerOptions(
    IReadOnlyList<string> GlobPatterns,
    IReadOnlyList<string> RegexPatterns,
    FileEntryType? TypeFilter,
    bool? TextOnly,
    long? MinSize,
    long? MaxSize,
    DateTimeOffset? NewerThan,
    DateTimeOffset? OlderThan,
    int? MaxDepth,
    bool IncludeHidden,
    bool FollowSymlinks,
    bool UseGitIgnore,
    bool AbsolutePaths,
    bool CaseInsensitive);
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/Winix.FileWalk/Winix.FileWalk.csproj`
Expected: Build succeeded, 0 warnings

- [ ] **Step 5: Commit**

```bash
git add src/Winix.FileWalk/FileEntry.cs src/Winix.FileWalk/FileEntryType.cs src/Winix.FileWalk/FileWalkerOptions.cs
git commit -m "feat(filewalk): core types — FileEntry, FileEntryType, FileWalkerOptions"
```

---

## Task 6: GlobMatcher

Wrapper around `Microsoft.Extensions.FileSystemGlobbing` for matching filenames against glob patterns, with case sensitivity support.

**Files:**
- Create: `src/Winix.FileWalk/GlobMatcher.cs`
- Create: `tests/Winix.FileWalk.Tests/GlobMatcherTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Winix.FileWalk.Tests/GlobMatcherTests.cs
using Winix.FileWalk;
using Xunit;

namespace Winix.FileWalk.Tests;

public class GlobMatcherTests
{
    [Fact]
    public void IsMatch_SinglePattern_MatchesCorrectly()
    {
        var matcher = new GlobMatcher(new[] { "*.cs" }, caseInsensitive: false);
        Assert.True(matcher.IsMatch("Program.cs"));
        Assert.False(matcher.IsMatch("readme.md"));
    }

    [Fact]
    public void IsMatch_MultiplePatterns_MatchesAny()
    {
        var matcher = new GlobMatcher(new[] { "*.cs", "*.fs" }, caseInsensitive: false);
        Assert.True(matcher.IsMatch("Program.cs"));
        Assert.True(matcher.IsMatch("Module.fs"));
        Assert.False(matcher.IsMatch("readme.md"));
    }

    [Fact]
    public void IsMatch_CaseInsensitive_MatchesRegardlessOfCase()
    {
        var matcher = new GlobMatcher(new[] { "*.cs" }, caseInsensitive: true);
        Assert.True(matcher.IsMatch("Program.cs"));
        Assert.True(matcher.IsMatch("Program.CS"));
        Assert.True(matcher.IsMatch("PROGRAM.Cs"));
    }

    [Fact]
    public void IsMatch_CaseSensitive_RequiresExactCase()
    {
        var matcher = new GlobMatcher(new[] { "*.cs" }, caseInsensitive: false);
        Assert.True(matcher.IsMatch("Program.cs"));
        Assert.False(matcher.IsMatch("Program.CS"));
    }

    [Fact]
    public void IsMatch_BraceExpansion_MatchesAny()
    {
        var matcher = new GlobMatcher(new[] { "*.{cs,fs,vb}" }, caseInsensitive: false);
        Assert.True(matcher.IsMatch("Program.cs"));
        Assert.True(matcher.IsMatch("Module.fs"));
        Assert.True(matcher.IsMatch("Form.vb"));
        Assert.False(matcher.IsMatch("readme.md"));
    }

    [Fact]
    public void IsMatch_QuestionMark_MatchesSingleChar()
    {
        var matcher = new GlobMatcher(new[] { "test?.cs" }, caseInsensitive: false);
        Assert.True(matcher.IsMatch("test1.cs"));
        Assert.True(matcher.IsMatch("testA.cs"));
        Assert.False(matcher.IsMatch("test12.cs"));
        Assert.False(matcher.IsMatch("test.cs"));
    }

    [Fact]
    public void IsMatch_EmptyPatterns_MatchesNothing()
    {
        var matcher = new GlobMatcher(Array.Empty<string>(), caseInsensitive: false);
        Assert.False(matcher.IsMatch("anything.txt"));
    }

    [Fact]
    public void HasPatterns_WithPatterns_ReturnsTrue()
    {
        var matcher = new GlobMatcher(new[] { "*.cs" }, caseInsensitive: false);
        Assert.True(matcher.HasPatterns);
    }

    [Fact]
    public void HasPatterns_NoPatterns_ReturnsFalse()
    {
        var matcher = new GlobMatcher(Array.Empty<string>(), caseInsensitive: false);
        Assert.False(matcher.HasPatterns);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.FileWalk.Tests --filter "FullyQualifiedName~GlobMatcherTests" -v quiet`
Expected: Build error — `GlobMatcher` does not exist

- [ ] **Step 3: Implement GlobMatcher**

```csharp
// src/Winix.FileWalk/GlobMatcher.cs
using Microsoft.Extensions.FileSystemGlobbing;

namespace Winix.FileWalk;

/// <summary>
/// Matches filenames against one or more glob patterns. Any pattern matching is a hit (OR logic).
/// Supports case-insensitive matching for Windows/macOS.
/// </summary>
public sealed class GlobMatcher
{
    private readonly Matcher? _matcher;
    private readonly bool _caseInsensitive;
    private readonly string[] _patterns;

    /// <summary>Creates a matcher for the given glob patterns.</summary>
    /// <param name="patterns">Glob patterns to match against (OR logic).</param>
    /// <param name="caseInsensitive">True for case-insensitive matching (Windows/macOS default).</param>
    public GlobMatcher(IEnumerable<string> patterns, bool caseInsensitive)
    {
        _patterns = patterns.ToArray();
        _caseInsensitive = caseInsensitive;

        if (_patterns.Length > 0)
        {
            _matcher = new Matcher(caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            foreach (string pattern in _patterns)
            {
                _matcher.AddInclude(pattern);
            }
        }
    }

    /// <summary>True if at least one pattern was registered.</summary>
    public bool HasPatterns => _patterns.Length > 0;

    /// <summary>Tests whether a filename matches any registered pattern.</summary>
    /// <param name="fileName">Filename only (no directory component).</param>
    public bool IsMatch(string fileName)
    {
        if (_matcher is null)
        {
            return false;
        }

        // FileSystemGlobbing matches relative paths against a directory tree.
        // For filename-only matching, we use Match(root, file) with an empty root.
        return _matcher.Match("/", "/" + fileName).HasMatches;
    }
}
```

Note: `Microsoft.Extensions.FileSystemGlobbing.Matcher` accepts a `StringComparison` parameter in its constructor for case sensitivity control. The `Match(root, file)` overload allows matching a single file path without scanning a real directory.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.FileWalk.Tests --filter "FullyQualifiedName~GlobMatcherTests" -v quiet`
Expected: All tests pass

If `Matcher` doesn't support the constructor-based `StringComparison` or the `Match("/", "/" + fileName)` pattern, adjust the implementation:
- For case sensitivity: normalise both pattern and filename to lowercase when `caseInsensitive` is true
- For single-file matching: use `Matcher.Match(directoryPath, files)` with a one-element files list

- [ ] **Step 5: Commit**

```bash
git add src/Winix.FileWalk/GlobMatcher.cs tests/Winix.FileWalk.Tests/GlobMatcherTests.cs
git commit -m "feat(filewalk): GlobMatcher with case sensitivity support"
```

---

## Task 7: FileWalker (the walking engine)

The core directory walking engine. Enumerates file system entries, applies all predicates, yields `FileEntry` records lazily.

**Files:**
- Create: `src/Winix.FileWalk/FileWalker.cs`
- Create: `tests/Winix.FileWalk.Tests/FileWalkerTests.cs`

- [ ] **Step 1: Write failing tests**

The tests create a temporary directory tree with a known structure, walk it with various options, and verify the results.

```csharp
// tests/Winix.FileWalk.Tests/FileWalkerTests.cs
using System.Text.RegularExpressions;
using Winix.FileWalk;
using Xunit;

namespace Winix.FileWalk.Tests;

public class FileWalkerTests : IDisposable
{
    private readonly string _tempDir;

    public FileWalkerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "winix-filewalk-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        // Create test structure:
        // root/
        //   file1.cs
        //   file2.txt
        //   .hidden
        //   sub/
        //     file3.cs
        //     file4.json
        //     deep/
        //       file5.cs
        CreateFile("file1.cs", "class A {}");
        CreateFile("file2.txt", "hello");
        CreateFile(".hidden", "secret");
        Directory.CreateDirectory(Path.Combine(_tempDir, "sub"));
        CreateFile("sub/file3.cs", "class B {}");
        CreateFile("sub/file4.json", "{}");
        Directory.CreateDirectory(Path.Combine(_tempDir, "sub", "deep"));
        CreateFile("sub/deep/file5.cs", "class C {}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    [Fact]
    public void Walk_NoFilters_ReturnsAllEntries()
    {
        var options = MakeOptions();
        var walker = new FileWalker(options);
        List<FileEntry> results = walker.Walk(new[] { _tempDir }).ToList();

        // Should find: file1.cs, file2.txt, .hidden, sub/, sub/file3.cs, sub/file4.json, sub/deep/, sub/deep/file5.cs
        Assert.True(results.Count >= 5, $"Expected at least 5 entries, got {results.Count}");
        Assert.Contains(results, e => e.Name == "file1.cs");
        Assert.Contains(results, e => e.Name == "file5.cs");
    }

    [Fact]
    public void Walk_GlobFilter_MatchesOnlyGlob()
    {
        var options = MakeOptions(globPatterns: new[] { "*.cs" });
        var walker = new FileWalker(options);
        List<FileEntry> results = walker.Walk(new[] { _tempDir }).ToList();

        Assert.All(results, e => Assert.True(
            e.Type == FileEntryType.Directory || e.Name.EndsWith(".cs"),
            $"Unexpected non-.cs file: {e.Name}"));
        Assert.Contains(results, e => e.Name == "file1.cs");
        Assert.Contains(results, e => e.Name == "file3.cs");
        Assert.Contains(results, e => e.Name == "file5.cs");
    }

    [Fact]
    public void Walk_TypeFilterFile_ExcludesDirectories()
    {
        var options = MakeOptions(typeFilter: FileEntryType.File);
        var walker = new FileWalker(options);
        List<FileEntry> results = walker.Walk(new[] { _tempDir }).ToList();

        Assert.All(results, e => Assert.Equal(FileEntryType.File, e.Type));
    }

    [Fact]
    public void Walk_TypeFilterDirectory_OnlyDirectories()
    {
        var options = MakeOptions(typeFilter: FileEntryType.Directory);
        var walker = new FileWalker(options);
        List<FileEntry> results = walker.Walk(new[] { _tempDir }).ToList();

        Assert.All(results, e => Assert.Equal(FileEntryType.Directory, e.Type));
        Assert.Contains(results, e => e.Name == "sub");
    }

    [Fact]
    public void Walk_MaxDepth_LimitsRecursion()
    {
        var options = MakeOptions(maxDepth: 1);
        var walker = new FileWalker(options);
        List<FileEntry> results = walker.Walk(new[] { _tempDir }).ToList();

        // Depth 0: file1.cs, file2.txt, .hidden, sub/
        // Depth 1: sub/file3.cs, sub/file4.json, sub/deep/
        // Should NOT include sub/deep/file5.cs (depth 2)
        Assert.DoesNotContain(results, e => e.Name == "file5.cs");
        Assert.Contains(results, e => e.Name == "file3.cs");
    }

    [Fact]
    public void Walk_NoHidden_SkipsHiddenFiles()
    {
        var options = MakeOptions(includeHidden: false);
        var walker = new FileWalker(options);
        List<FileEntry> results = walker.Walk(new[] { _tempDir }).ToList();

        Assert.DoesNotContain(results, e => e.Name == ".hidden");
    }

    [Fact]
    public void Walk_RegexFilter_MatchesPattern()
    {
        var options = MakeOptions(regexPatterns: new[] { @"file\d+\.cs" });
        var walker = new FileWalker(options);
        List<FileEntry> results = walker.Walk(new[] { _tempDir }).ToList();

        var fileResults = results.Where(e => e.Type == FileEntryType.File).ToList();
        Assert.All(fileResults, e => Assert.Matches(@"file\d+\.cs", e.Name));
    }

    [Fact]
    public void Walk_AbsolutePaths_ReturnsAbsolute()
    {
        var options = MakeOptions(absolutePaths: true);
        var walker = new FileWalker(options);
        List<FileEntry> results = walker.Walk(new[] { _tempDir }).ToList();

        Assert.All(results, e => Assert.True(
            Path.IsPathRooted(e.Path),
            $"Expected absolute path, got: {e.Path}"));
    }

    [Fact]
    public void Walk_DepthValues_AreCorrect()
    {
        var options = MakeOptions(typeFilter: FileEntryType.File);
        var walker = new FileWalker(options);
        List<FileEntry> results = walker.Walk(new[] { _tempDir }).ToList();

        FileEntry? file1 = results.FirstOrDefault(e => e.Name == "file1.cs");
        FileEntry? file3 = results.FirstOrDefault(e => e.Name == "file3.cs");
        FileEntry? file5 = results.FirstOrDefault(e => e.Name == "file5.cs");

        Assert.NotNull(file1);
        Assert.NotNull(file3);
        Assert.NotNull(file5);
        Assert.Equal(0, file1.Depth);
        Assert.Equal(1, file3.Depth);
        Assert.Equal(2, file5.Depth);
    }

    [Fact]
    public void Walk_TextOnly_FiltersTextFiles()
    {
        // Create a binary file
        File.WriteAllBytes(Path.Combine(_tempDir, "binary.dat"), new byte[] { 0x00, 0x01, 0x02 });

        var options = MakeOptions(textOnly: true, typeFilter: FileEntryType.File);
        var walker = new FileWalker(options);
        List<FileEntry> results = walker.Walk(new[] { _tempDir }).ToList();

        Assert.DoesNotContain(results, e => e.Name == "binary.dat");
        Assert.Contains(results, e => e.Name == "file1.cs");
        Assert.All(results, e => Assert.True(e.IsText == true));
    }

    [Fact]
    public void Walk_BinaryOnly_FiltersBinaryFiles()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, "binary.dat"), new byte[] { 0x00, 0x01, 0x02 });

        var options = MakeOptions(textOnly: false, typeFilter: FileEntryType.File);
        var walker = new FileWalker(options);
        List<FileEntry> results = walker.Walk(new[] { _tempDir }).ToList();

        Assert.Contains(results, e => e.Name == "binary.dat");
        Assert.DoesNotContain(results, e => e.Name == "file1.cs");
        Assert.All(results, e => Assert.True(e.IsText == false));
    }

    private void CreateFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(fullPath, content);
    }

    private static FileWalkerOptions MakeOptions(
        string[]? globPatterns = null,
        string[]? regexPatterns = null,
        FileEntryType? typeFilter = null,
        bool? textOnly = null,
        long? minSize = null,
        long? maxSize = null,
        DateTimeOffset? newerThan = null,
        DateTimeOffset? olderThan = null,
        int? maxDepth = null,
        bool includeHidden = true,
        bool followSymlinks = false,
        bool useGitIgnore = false,
        bool absolutePaths = false,
        bool caseInsensitive = false)
    {
        return new FileWalkerOptions(
            GlobPatterns: (IReadOnlyList<string>)(globPatterns ?? Array.Empty<string>()),
            RegexPatterns: (IReadOnlyList<string>)(regexPatterns ?? Array.Empty<string>()),
            TypeFilter: typeFilter,
            TextOnly: textOnly,
            MinSize: minSize,
            MaxSize: maxSize,
            NewerThan: newerThan,
            OlderThan: olderThan,
            MaxDepth: maxDepth,
            IncludeHidden: includeHidden,
            FollowSymlinks: followSymlinks,
            UseGitIgnore: useGitIgnore,
            AbsolutePaths: absolutePaths,
            CaseInsensitive: caseInsensitive);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.FileWalk.Tests --filter "FullyQualifiedName~FileWalkerTests" -v quiet`
Expected: Build error — `FileWalker` class does not exist

- [ ] **Step 3: Implement FileWalker**

```csharp
// src/Winix.FileWalk/FileWalker.cs
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Yort.ShellKit;

namespace Winix.FileWalk;

/// <summary>
/// Walks directory trees and yields <see cref="FileEntry"/> records matching the configured predicates.
/// Lazy enumeration via yield return — does not buffer the entire tree.
/// </summary>
public sealed class FileWalker
{
    private readonly FileWalkerOptions _options;
    private readonly GitIgnoreFilter? _ignoreFilter;
    private readonly GlobMatcher _globMatcher;
    private readonly Regex[]? _regexPatterns;

    /// <summary>Creates a walker with the given options and optional gitignore filter.</summary>
    public FileWalker(FileWalkerOptions options, GitIgnoreFilter? ignoreFilter = null)
    {
        _options = options;
        _ignoreFilter = options.UseGitIgnore ? ignoreFilter : null;

        _globMatcher = new GlobMatcher(options.GlobPatterns, options.CaseInsensitive);

        if (options.RegexPatterns.Count > 0)
        {
            var regexOptions = RegexOptions.Compiled;
            if (options.CaseInsensitive)
            {
                regexOptions |= RegexOptions.IgnoreCase;
            }
            _regexPatterns = options.RegexPatterns
                .Select(p => new Regex(p, regexOptions))
                .ToArray();
        }
    }

    /// <summary>
    /// Walks the given root directories and yields matching entries.
    /// Directories are always yielded if they pass filters (regardless of TypeFilter)
    /// so the caller can see structure. TypeFilter is applied to the final output.
    /// </summary>
    public IEnumerable<FileEntry> Walk(IReadOnlyList<string> roots)
    {
        var visited = _options.FollowSymlinks ? new HashSet<string>(StringComparer.Ordinal) : null;

        foreach (string root in roots)
        {
            string fullRoot = Path.GetFullPath(root);
            foreach (FileEntry entry in WalkDirectory(fullRoot, fullRoot, 0, visited))
            {
                yield return entry;
            }
        }
    }

    private IEnumerable<FileEntry> WalkDirectory(string root, string currentDir, int depth, HashSet<string>? visited)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(currentDir);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        catch (DirectoryNotFoundException)
        {
            yield break;
        }

        foreach (string fullPath in entries)
        {
            string name = Path.GetFileName(fullPath);

            // Skip hidden files/directories if requested
            if (!_options.IncludeHidden && IsHidden(fullPath, name))
            {
                continue;
            }

            // Determine type
            FileAttributes attrs;
            try
            {
                attrs = File.GetAttributes(fullPath);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            bool isDirectory = (attrs & FileAttributes.Directory) != 0;
            bool isSymlink = (attrs & FileAttributes.ReparsePoint) != 0;
            FileEntryType type = isSymlink ? FileEntryType.Symlink
                : isDirectory ? FileEntryType.Directory
                : FileEntryType.File;

            // Gitignore check
            if (_ignoreFilter is not null)
            {
                string relativePath = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
                if (isDirectory)
                {
                    relativePath += "/";
                }
                if (_ignoreFilter.IsIgnored(relativePath))
                {
                    continue;
                }
            }

            // For directories: recurse first (depth-first), then yield the directory if it passes filters
            if (isDirectory)
            {
                // Symlink cycle detection
                if (_options.FollowSymlinks && isSymlink)
                {
                    string realPath;
                    try
                    {
                        realPath = Path.GetFullPath(fullPath);
                    }
                    catch
                    {
                        continue;
                    }
                    if (!visited!.Add(realPath))
                    {
                        continue; // Cycle detected
                    }
                }

                if (!_options.FollowSymlinks && isSymlink)
                {
                    // Don't recurse into symlinked directories unless --follow
                }
                else if (_options.MaxDepth is null || depth < _options.MaxDepth)
                {
                    foreach (FileEntry child in WalkDirectory(root, fullPath, depth + 1, visited))
                    {
                        yield return child;
                    }
                }

                // Yield directory entry if type filter allows
                if (MatchesFilters(name, type, -1, default, depth))
                {
                    string outputPath = _options.AbsolutePaths ? fullPath : Path.GetRelativePath(root, fullPath);
                    yield return new FileEntry(
                        Path: outputPath.Replace('\\', '/'),
                        Name: name,
                        Type: type,
                        SizeBytes: -1,
                        Modified: Directory.GetLastWriteTime(fullPath),
                        Depth: depth,
                        IsText: null);
                }
            }
            else
            {
                // File or symlink to file
                long size = -1;
                DateTimeOffset modified = default;
                try
                {
                    var info = new FileInfo(fullPath);
                    size = info.Length;
                    modified = info.LastWriteTimeUtc;
                }
                catch
                {
                    continue;
                }

                if (!MatchesFilters(name, type, size, modified, depth))
                {
                    continue;
                }

                // Text/binary detection (late filter — only on matched files)
                bool? isText = null;
                if (_options.TextOnly.HasValue)
                {
                    bool fileIsText = ContentDetector.IsTextFile(fullPath);
                    if (_options.TextOnly.Value && !fileIsText)
                    {
                        continue;
                    }
                    if (!_options.TextOnly.Value && fileIsText)
                    {
                        continue;
                    }
                    isText = fileIsText;
                }

                string outputPath = _options.AbsolutePaths ? fullPath : Path.GetRelativePath(root, fullPath);
                yield return new FileEntry(
                    Path: outputPath.Replace('\\', '/'),
                    Name: name,
                    Type: type,
                    SizeBytes: size,
                    Modified: modified,
                    Depth: depth,
                    IsText: isText);
            }
        }
    }

    private bool MatchesFilters(string name, FileEntryType type, long size, DateTimeOffset modified, int depth)
    {
        // Type filter
        if (_options.TypeFilter.HasValue && type != _options.TypeFilter.Value)
        {
            return false;
        }

        // Glob filter (filename only)
        if (_globMatcher.HasPatterns && type != FileEntryType.Directory && !_globMatcher.IsMatch(name))
        {
            return false;
        }

        // Regex filter (filename only)
        if (_regexPatterns is not null && type != FileEntryType.Directory)
        {
            bool anyMatch = false;
            foreach (Regex regex in _regexPatterns)
            {
                if (regex.IsMatch(name))
                {
                    anyMatch = true;
                    break;
                }
            }
            if (!anyMatch)
            {
                return false;
            }
        }

        // Size filters (files only)
        if (type == FileEntryType.File)
        {
            if (_options.MinSize.HasValue && size < _options.MinSize.Value)
            {
                return false;
            }
            if (_options.MaxSize.HasValue && size > _options.MaxSize.Value)
            {
                return false;
            }
        }

        // Date filters
        if (type != FileEntryType.Directory && modified != default)
        {
            if (_options.NewerThan.HasValue && modified < _options.NewerThan.Value)
            {
                return false;
            }
            if (_options.OlderThan.HasValue && modified > _options.OlderThan.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHidden(string fullPath, string name)
    {
        // Dot-prefix convention (all platforms)
        if (name.Length > 0 && name[0] == '.')
        {
            return true;
        }

        // Windows hidden attribute
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                FileAttributes attrs = File.GetAttributes(fullPath);
                if ((attrs & FileAttributes.Hidden) != 0)
                {
                    return true;
                }
            }
            catch
            {
                // If we can't read attributes, don't hide it
            }
        }

        return false;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.FileWalk.Tests --filter "FullyQualifiedName~FileWalkerTests" -v quiet`
Expected: All tests pass

If any tests fail, debug and fix. Common issues:
- Path separator differences (use `Replace('\\', '/')` for consistent output)
- `Directory.GetLastWriteTime` returns local time, may need `DateTimeOffset` conversion
- `EnumerateFileSystemEntries` order varies by OS

- [ ] **Step 5: Commit**

```bash
git add src/Winix.FileWalk/FileWalker.cs tests/Winix.FileWalk.Tests/FileWalkerTests.cs
git commit -m "feat(filewalk): FileWalker directory walking engine with predicate filtering"
```

---

## Task 8: GitIgnoreFilter in ShellKit

Wraps `git check-ignore --stdin -z` for reliable gitignore checking.

**Files:**
- Create: `src/Yort.ShellKit/GitIgnoreFilter.cs`
- Create: `tests/Yort.ShellKit.Tests/GitIgnoreFilterTests.cs`

- [ ] **Step 1: Write failing tests**

These tests require git on PATH and create a temporary git repo.

```csharp
// tests/Yort.ShellKit.Tests/GitIgnoreFilterTests.cs
using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class GitIgnoreFilterTests : IDisposable
{
    private readonly string _tempDir;

    public GitIgnoreFilterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "winix-gitignore-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    [Fact]
    public void Create_NotGitRepo_ReturnsNull()
    {
        using GitIgnoreFilter? filter = GitIgnoreFilter.Create(_tempDir);
        Assert.Null(filter);
    }

    [Fact]
    public void Create_GitRepoWithIgnore_ReturnsFilter()
    {
        InitGitRepo();
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "*.log\nbin/\n");

        using GitIgnoreFilter? filter = GitIgnoreFilter.Create(_tempDir);
        Assert.NotNull(filter);
    }

    [Fact]
    public void IsIgnored_IgnoredFile_ReturnsTrue()
    {
        InitGitRepo();
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "*.log\n");
        File.WriteAllText(Path.Combine(_tempDir, "debug.log"), "log content");

        using GitIgnoreFilter? filter = GitIgnoreFilter.Create(_tempDir);
        Assert.NotNull(filter);
        Assert.True(filter!.IsIgnored("debug.log"));
    }

    [Fact]
    public void IsIgnored_NotIgnoredFile_ReturnsFalse()
    {
        InitGitRepo();
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "*.log\n");
        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "hello");

        using GitIgnoreFilter? filter = GitIgnoreFilter.Create(_tempDir);
        Assert.NotNull(filter);
        Assert.False(filter!.IsIgnored("readme.md"));
    }

    [Fact]
    public void IsIgnored_IgnoredDirectory_ReturnsTrue()
    {
        InitGitRepo();
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "bin/\n");
        Directory.CreateDirectory(Path.Combine(_tempDir, "bin"));

        using GitIgnoreFilter? filter = GitIgnoreFilter.Create(_tempDir);
        Assert.NotNull(filter);
        Assert.True(filter!.IsIgnored("bin/"));
    }

    private void InitGitRepo()
    {
        RunGit("init");
        RunGit("config user.email test@test.com");
        RunGit("config user.name Test");
    }

    private void RunGit(string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = _tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.WaitForExit(10000);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Yort.ShellKit.Tests --filter "FullyQualifiedName~GitIgnoreFilterTests" -v quiet`
Expected: Build error — `GitIgnoreFilter` does not exist

- [ ] **Step 3: Implement GitIgnoreFilter**

```csharp
// src/Yort.ShellKit/GitIgnoreFilter.cs
using System.Diagnostics;

namespace Yort.ShellKit;

/// <summary>
/// Checks whether file paths are ignored by gitignore rules. Wraps <c>git check-ignore</c>
/// for correctness — handles nested .gitignore, .git/info/exclude, and global excludes.
/// </summary>
public sealed class GitIgnoreFilter : IDisposable
{
    private readonly Process _process;
    private readonly object _lock = new();
    private bool _disposed;

    private GitIgnoreFilter(Process process)
    {
        _process = process;
    }

    /// <summary>
    /// Creates a filter for the given directory. Returns null if the directory is not
    /// inside a git working tree or if git is not available on PATH.
    /// </summary>
    public static GitIgnoreFilter? Create(string rootPath)
    {
        // First check if this is a git repo
        try
        {
            var checkPsi = new ProcessStartInfo("git", "rev-parse --git-dir")
            {
                WorkingDirectory = rootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var checkProcess = Process.Start(checkPsi);
            if (checkProcess is null)
            {
                return null;
            }
            checkProcess.WaitForExit(5000);
            if (checkProcess.ExitCode != 0)
            {
                return null;
            }
        }
        catch
        {
            return null;
        }

        // Start long-running git check-ignore process
        try
        {
            var psi = new ProcessStartInfo("git", "check-ignore --stdin -v -n")
            {
                WorkingDirectory = rootPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }
            return new GitIgnoreFilter(process);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks whether a path is ignored by gitignore rules.
    /// </summary>
    /// <param name="relativePath">Path relative to the git root. Use forward slashes. Append / for directories.</param>
    public bool IsIgnored(string relativePath)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(GitIgnoreFilter));
        }

        lock (_lock)
        {
            try
            {
                _process.StandardInput.WriteLine(relativePath);
                _process.StandardInput.Flush();

                string? line = _process.StandardOutput.ReadLine();
                // git check-ignore -v -n outputs a line for every input:
                // <source>:<linenum>:<pattern>\t<pathname> for ignored files
                // ::\t<pathname> for non-ignored files
                if (line is null)
                {
                    return false;
                }

                // If the line starts with "::" it means no rule matched (not ignored)
                return !line.StartsWith("::");
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Disposes the underlying git process.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            _process.StandardInput.Close();
            _process.WaitForExit(3000);
            if (!_process.HasExited)
            {
                _process.Kill();
            }
            _process.Dispose();
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
```

Note: The `git check-ignore -v -n` flags produce one output line per input line (even for non-ignored files), making the protocol synchronous and predictable. Without `-n`, git only outputs for ignored files, creating ambiguity about which response corresponds to which input.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Yort.ShellKit.Tests --filter "FullyQualifiedName~GitIgnoreFilterTests" -v quiet`
Expected: All tests pass

If `git check-ignore -v -n` doesn't produce output for non-ignored files on the test system, adjust the implementation to use `git check-ignore -q` with per-path process invocation as a fallback, or use `--stdin` with `-z` null-delimited I/O.

- [ ] **Step 5: Commit**

```bash
git add src/Yort.ShellKit/GitIgnoreFilter.cs tests/Yort.ShellKit.Tests/GitIgnoreFilterTests.cs
git commit -m "feat(shellkit): GitIgnoreFilter wrapping git check-ignore"
```

---

## Task 9: `--describe` Fluent API and JSON Serialiser

Add fluent builder methods to `CommandLineParser` for `--describe` metadata, and implement `GenerateDescribe()` JSON output.

**Files:**
- Modify: `src/Yort.ShellKit/CommandLineParser.cs`
- Create: `tests/Yort.ShellKit.Tests/DescribeTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Yort.ShellKit.Tests/DescribeTests.cs
using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class DescribeTests
{
    [Fact]
    public void GenerateDescribe_ContainsToolAndVersion()
    {
        var parser = new CommandLineParser("testool", "1.0.0")
            .Description("A test tool.")
            .StandardFlags();

        string json = parser.GenerateDescribe();

        Assert.Contains("\"tool\":\"testool\"", json);
        Assert.Contains("\"version\":\"1.0.0\"", json);
        Assert.Contains("\"description\":\"A test tool.\"", json);
    }

    [Fact]
    public void GenerateDescribe_IncludesOptions()
    {
        var parser = new CommandLineParser("testool", "1.0.0")
            .StandardFlags()
            .Flag("--verbose", "-v", "Verbose output")
            .Option("--format", "-f", "FMT", "Output format");

        string json = parser.GenerateDescribe();

        Assert.Contains("\"long\":\"--verbose\"", json);
        Assert.Contains("\"short\":\"-v\"", json);
        Assert.Contains("\"type\":\"flag\"", json);
        Assert.Contains("\"long\":\"--format\"", json);
        Assert.Contains("\"short\":\"-f\"", json);
        Assert.Contains("\"placeholder\":\"FMT\"", json);
    }

    [Fact]
    public void GenerateDescribe_IncludesExamples()
    {
        var parser = new CommandLineParser("testool", "1.0.0")
            .StandardFlags()
            .Example("testool --verbose", "Run with verbose output");

        string json = parser.GenerateDescribe();

        Assert.Contains("\"command\":\"testool --verbose\"", json);
        Assert.Contains("\"description\":\"Run with verbose output\"", json);
    }

    [Fact]
    public void GenerateDescribe_IncludesPlatform()
    {
        var parser = new CommandLineParser("testool", "1.0.0")
            .StandardFlags()
            .Platform("cross-platform",
                replaces: new[] { "find" },
                valueOnWindows: "No native equivalent",
                valueOnUnix: "Better output");

        string json = parser.GenerateDescribe();

        Assert.Contains("\"scope\":\"cross-platform\"", json);
        Assert.Contains("\"replaces\":[\"find\"]", json);
        Assert.Contains("\"value_on_windows\":\"No native equivalent\"", json);
        Assert.Contains("\"value_on_unix\":\"Better output\"", json);
    }

    [Fact]
    public void GenerateDescribe_IncludesIo()
    {
        var parser = new CommandLineParser("testool", "1.0.0")
            .StandardFlags()
            .StdinDescription("not used")
            .StdoutDescription("one path per line")
            .StderrDescription("errors");

        string json = parser.GenerateDescribe();

        Assert.Contains("\"stdin\":\"not used\"", json);
        Assert.Contains("\"stdout\":\"one path per line\"", json);
        Assert.Contains("\"stderr\":\"errors\"", json);
    }

    [Fact]
    public void GenerateDescribe_IncludesComposesWith()
    {
        var parser = new CommandLineParser("testool", "1.0.0")
            .StandardFlags()
            .ComposesWith("wargs", "testool | wargs cmd", "Pipe to wargs");

        string json = parser.GenerateDescribe();

        Assert.Contains("\"tool\":\"wargs\"", json);
        Assert.Contains("\"pattern\":\"testool | wargs cmd\"", json);
    }

    [Fact]
    public void GenerateDescribe_IncludesJsonFields()
    {
        var parser = new CommandLineParser("testool", "1.0.0")
            .StandardFlags()
            .JsonField("path", "string", "File path");

        string json = parser.GenerateDescribe();

        Assert.Contains("\"name\":\"path\"", json);
        Assert.Contains("\"type\":\"string\"", json);
    }

    [Fact]
    public void GenerateDescribe_IncludesExitCodes()
    {
        var parser = new CommandLineParser("testool", "1.0.0")
            .StandardFlags()
            .ExitCodes((0, "Success"), (1, "Error"));

        string json = parser.GenerateDescribe();

        Assert.Contains("\"code\":0", json);
        Assert.Contains("\"description\":\"Success\"", json);
    }

    [Fact]
    public void Parse_DescribeFlag_SetsIsHandled()
    {
        var parser = new CommandLineParser("testool", "1.0.0")
            .StandardFlags();

        // Capture stdout to avoid polluting test output
        var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            ParseResult result = parser.Parse(new[] { "--describe" });
            Assert.True(result.IsHandled);
            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateDescribe_OutputIsValidJson()
    {
        var parser = new CommandLineParser("testool", "1.0.0")
            .Description("Test tool")
            .StandardFlags()
            .Flag("--verbose", "-v", "Verbose")
            .Example("testool --verbose", "Be verbose")
            .Platform("cross-platform", replaces: new[] { "test" }, valueOnWindows: "W", valueOnUnix: "U")
            .StdinDescription("none")
            .StdoutDescription("data")
            .StderrDescription("errors")
            .ComposesWith("other", "testool | other", "Pipe")
            .JsonField("result", "string", "The result")
            .ExitCodes((0, "OK"), (1, "Fail"));

        string json = parser.GenerateDescribe();

        // Verify it parses as valid JSON
        System.Text.Json.JsonDocument.Parse(json);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Yort.ShellKit.Tests --filter "FullyQualifiedName~DescribeTests" -v quiet`
Expected: Build error — `GenerateDescribe`, `Example`, `Platform`, etc. do not exist

- [ ] **Step 3: Add fluent builder methods and GenerateDescribe to CommandLineParser**

Add the following new fields, methods, and `GenerateDescribe()` implementation to `src/Yort.ShellKit/CommandLineParser.cs`:

New private fields (add after existing field declarations around line 35):
```csharp
    private string? _stdinDescription;
    private string? _stdoutDescription;
    private string? _stderrDescription;
    private readonly List<(string Command, string Description)> _examples = new();
    private readonly List<(string Tool, string Pattern, string Description)> _composability = new();
    private readonly List<(string Name, string Type, string Description)> _jsonFields = new();
    private string? _platformScope;
    private string[]? _platformReplaces;
    private string? _platformValueWindows;
    private string? _platformValueUnix;
```

New fluent builder methods (add after existing builder methods, before `Parse`):
```csharp
    /// <summary>Describes what the tool reads from stdin (for --describe metadata).</summary>
    public CommandLineParser StdinDescription(string text)
    {
        _stdinDescription = text;
        return this;
    }

    /// <summary>Describes what the tool writes to stdout (for --describe metadata).</summary>
    public CommandLineParser StdoutDescription(string text)
    {
        _stdoutDescription = text;
        return this;
    }

    /// <summary>Describes what the tool writes to stderr (for --describe metadata).</summary>
    public CommandLineParser StderrDescription(string text)
    {
        _stderrDescription = text;
        return this;
    }

    /// <summary>Adds a usage example (for --describe metadata and --help).</summary>
    public CommandLineParser Example(string command, string description)
    {
        _examples.Add((command, description));
        return this;
    }

    /// <summary>Declares a tool this composes well with (for --describe metadata).</summary>
    public CommandLineParser ComposesWith(string tool, string pattern, string description)
    {
        _composability.Add((tool, pattern, description));
        return this;
    }

    /// <summary>Describes a field in the tool's --json output (for --describe metadata).</summary>
    public CommandLineParser JsonField(string name, string type, string description)
    {
        _jsonFields.Add((name, type, description));
        return this;
    }

    /// <summary>Describes the tool's platform story (for --describe metadata).</summary>
    public CommandLineParser Platform(string scope, string[] replaces, string valueOnWindows, string valueOnUnix)
    {
        _platformScope = scope;
        _platformReplaces = replaces;
        _platformValueWindows = valueOnWindows;
        _platformValueUnix = valueOnUnix;
        return this;
    }
```

Update `StandardFlags()` to include `--describe`:
```csharp
    public CommandLineParser StandardFlags()
    {
        _standardFlagsRegistered = true;
        Flag("--help", "-h", "Show help");
        Flag("--version", "Show version");
        Flag("--describe", "Structured JSON metadata for AI agents");
        Flag("--color", "Force colored output");
        Flag("--no-color", "Disable colored output");
        Flag("--json", "JSON output to stderr");
        return this;
    }
```

Update `Parse()` to handle `--describe` (add after the `--version` handling block, before the `return`):
```csharp
        else if (flagsSet.Contains("--describe") && _standardFlagsRegistered)
        {
            Console.WriteLine(GenerateDescribe());
            isHandled = true;
            handledExitCode = 0;
        }
```

Add `GenerateDescribe()` method (add after `GenerateHelp()`):
```csharp
    /// <summary>Generates the --describe JSON output from all registered metadata.</summary>
    internal string GenerateDescribe()
    {
        var sb = new StringBuilder();
        sb.Append('{');

        // Standard fields
        sb.Append($"\"tool\":\"{EscapeJson(_toolName)}\"");
        sb.Append($",\"version\":\"{EscapeJson(_version)}\"");
        if (_description is not null)
        {
            sb.Append($",\"description\":\"{EscapeJson(_description)}\"");
        }

        // Platform
        if (_platformScope is not null)
        {
            sb.Append(",\"platform\":{");
            sb.Append($"\"scope\":\"{EscapeJson(_platformScope)}\"");
            if (_platformReplaces is not null && _platformReplaces.Length > 0)
            {
                sb.Append(",\"replaces\":[");
                for (int i = 0; i < _platformReplaces.Length; i++)
                {
                    if (i > 0) { sb.Append(','); }
                    sb.Append($"\"{EscapeJson(_platformReplaces[i])}\"");
                }
                sb.Append(']');
            }
            if (_platformValueWindows is not null)
            {
                sb.Append($",\"value_on_windows\":\"{EscapeJson(_platformValueWindows)}\"");
            }
            if (_platformValueUnix is not null)
            {
                sb.Append($",\"value_on_unix\":\"{EscapeJson(_platformValueUnix)}\"");
            }
            sb.Append('}');
        }

        // Usage line
        var usageSb = new StringBuilder($"{_toolName} [options]");
        if (_commandMode)
        {
            usageSb.Append(" [--] <command> [args...]");
        }
        else if (_positionalLabel is not null)
        {
            usageSb.Append($" [{_positionalLabel}]");
        }
        sb.Append($",\"usage\":\"{EscapeJson(usageSb.ToString())}\"");

        // Options
        sb.Append(",\"options\":[");
        string[] standardNames = { "--help", "--version", "--describe", "--color", "--no-color", "--json" };
        bool firstOption = true;

        foreach (FlagDef f in _flags)
        {
            if (!firstOption) { sb.Append(','); }
            firstOption = false;
            sb.Append('{');
            sb.Append($"\"long\":\"{EscapeJson(f.LongName)}\"");
            if (f.ShortName is not null)
            {
                sb.Append($",\"short\":\"{EscapeJson(f.ShortName)}\"");
            }
            sb.Append(",\"type\":\"flag\"");
            sb.Append($",\"description\":\"{EscapeJson(f.Description)}\"");
            sb.Append('}');
        }

        foreach (OptionDef o in _options)
        {
            if (!firstOption) { sb.Append(','); }
            firstOption = false;
            sb.Append('{');
            sb.Append($"\"long\":\"{EscapeJson(o.LongName)}\"");
            if (o.ShortName is not null)
            {
                sb.Append($",\"short\":\"{EscapeJson(o.ShortName)}\"");
            }
            string typeStr = o.Type switch
            {
                OptionType.Int => "int",
                OptionType.Double => "double",
                _ => "string"
            };
            sb.Append($",\"type\":\"{typeStr}\"");
            sb.Append($",\"placeholder\":\"{EscapeJson(o.Placeholder)}\"");
            sb.Append($",\"description\":\"{EscapeJson(o.Description)}\"");
            sb.Append(",\"repeatable\":false");
            sb.Append('}');
        }

        foreach (ListOptionDef l in _listOptions)
        {
            if (!firstOption) { sb.Append(','); }
            firstOption = false;
            sb.Append('{');
            sb.Append($"\"long\":\"{EscapeJson(l.LongName)}\"");
            if (l.ShortName is not null)
            {
                sb.Append($",\"short\":\"{EscapeJson(l.ShortName)}\"");
            }
            sb.Append(",\"type\":\"string\"");
            sb.Append($",\"placeholder\":\"{EscapeJson(l.Placeholder)}\"");
            sb.Append($",\"description\":\"{EscapeJson(l.Description)}\"");
            sb.Append(",\"repeatable\":true");
            sb.Append('}');
        }
        sb.Append(']');

        // Exit codes
        if (_exitCodes.Count > 0)
        {
            sb.Append(",\"exit_codes\":[");
            for (int i = 0; i < _exitCodes.Count; i++)
            {
                if (i > 0) { sb.Append(','); }
                sb.Append($"{{\"code\":{_exitCodes[i].Code},\"description\":\"{EscapeJson(_exitCodes[i].Description)}\"}}");
            }
            sb.Append(']');
        }

        // I/O
        if (_stdinDescription is not null || _stdoutDescription is not null || _stderrDescription is not null)
        {
            sb.Append(",\"io\":{");
            bool firstIo = true;
            if (_stdinDescription is not null)
            {
                sb.Append($"\"stdin\":\"{EscapeJson(_stdinDescription)}\"");
                firstIo = false;
            }
            if (_stdoutDescription is not null)
            {
                if (!firstIo) { sb.Append(','); }
                sb.Append($"\"stdout\":\"{EscapeJson(_stdoutDescription)}\"");
                firstIo = false;
            }
            if (_stderrDescription is not null)
            {
                if (!firstIo) { sb.Append(','); }
                sb.Append($"\"stderr\":\"{EscapeJson(_stderrDescription)}\"");
            }
            sb.Append('}');
        }

        // Examples
        if (_examples.Count > 0)
        {
            sb.Append(",\"examples\":[");
            for (int i = 0; i < _examples.Count; i++)
            {
                if (i > 0) { sb.Append(','); }
                sb.Append($"{{\"command\":\"{EscapeJson(_examples[i].Command)}\",\"description\":\"{EscapeJson(_examples[i].Description)}\"}}");
            }
            sb.Append(']');
        }

        // Composability
        if (_composability.Count > 0)
        {
            sb.Append(",\"composes_with\":[");
            for (int i = 0; i < _composability.Count; i++)
            {
                if (i > 0) { sb.Append(','); }
                var c = _composability[i];
                sb.Append($"{{\"tool\":\"{EscapeJson(c.Tool)}\",\"pattern\":\"{EscapeJson(c.Pattern)}\",\"description\":\"{EscapeJson(c.Description)}\"}}");
            }
            sb.Append(']');
        }

        // JSON output fields
        if (_jsonFields.Count > 0)
        {
            sb.Append(",\"json_output_fields\":[");
            for (int i = 0; i < _jsonFields.Count; i++)
            {
                if (i > 0) { sb.Append(','); }
                var f = _jsonFields[i];
                sb.Append($"{{\"name\":\"{EscapeJson(f.Name)}\",\"type\":\"{EscapeJson(f.Type)}\",\"description\":\"{EscapeJson(f.Description)}\"}}");
            }
            sb.Append(']');
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Yort.ShellKit.Tests --filter "FullyQualifiedName~DescribeTests" -v quiet`
Expected: All tests pass

Note: The `GenerateDescribe_OutputIsValidJson` test requires `System.Text.Json` for validation only. If the test project doesn't already reference it, .NET 10 includes it in the default SDK — no extra package needed.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test Winix.sln -v quiet`
Expected: All tests pass (existing + new). Verify `--describe` doesn't break `--help` or `--version` handling in existing tools.

- [ ] **Step 6: Commit**

```bash
git add src/Yort.ShellKit/CommandLineParser.cs tests/Yort.ShellKit.Tests/DescribeTests.cs
git commit -m "feat(shellkit): --describe fluent API and JSON serialiser for AI discoverability"
```

---

## Task 10: Winix.Files Formatting

Output formatting for the files tool — default paths, long format, null-delimited, NDJSON, and JSON summary.

**Files:**
- Modify: `src/Winix.Files/Formatting.cs` (replace placeholder)
- Modify: `tests/Winix.Files.Tests/FormattingTests.cs` (replace placeholder)

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Winix.Files.Tests/FormattingTests.cs
using Winix.FileWalk;
using Winix.Files;
using Xunit;

namespace Winix.Files.Tests;

public class FormattingTests
{
    private static readonly FileEntry SampleFile = new(
        Path: "src/Program.cs",
        Name: "Program.cs",
        Type: FileEntryType.File,
        SizeBytes: 2340,
        Modified: new DateTimeOffset(2026, 3, 31, 14, 22, 0, TimeSpan.FromHours(13)),
        Depth: 1,
        IsText: null);

    private static readonly FileEntry SampleDir = new(
        Path: "src",
        Name: "src",
        Type: FileEntryType.Directory,
        SizeBytes: -1,
        Modified: new DateTimeOffset(2026, 3, 31, 10, 0, 0, TimeSpan.FromHours(13)),
        Depth: 0,
        IsText: null);

    [Fact]
    public void FormatPath_ReturnsPathOnly()
    {
        string result = Formatting.FormatPath(SampleFile);
        Assert.Equal("src/Program.cs", result);
    }

    [Fact]
    public void FormatLong_IncludesAllColumns()
    {
        string result = Formatting.FormatLong(SampleFile);

        Assert.Contains("src/Program.cs", result);
        Assert.Contains("2,340", result);
        Assert.Contains("file", result);
    }

    [Fact]
    public void FormatLong_Directory_ShowsMinusOneSize()
    {
        string result = Formatting.FormatLong(SampleDir);
        Assert.Contains("-", result);
        Assert.Contains("dir", result);
    }

    [Fact]
    public void FormatNdjsonLine_ContainsStandardFields()
    {
        string json = Formatting.FormatNdjsonLine(SampleFile, "files", "1.0.0");

        Assert.Contains("\"tool\":\"files\"", json);
        Assert.Contains("\"version\":\"1.0.0\"", json);
        Assert.Contains("\"exit_code\":0", json);
        Assert.Contains("\"exit_reason\":\"success\"", json);
        Assert.Contains("\"path\":\"src/Program.cs\"", json);
        Assert.Contains("\"name\":\"Program.cs\"", json);
        Assert.Contains("\"type\":\"file\"", json);
        Assert.Contains("\"size_bytes\":2340", json);
        Assert.Contains("\"depth\":1", json);
    }

    [Fact]
    public void FormatNdjsonLine_WithIsText_IncludesField()
    {
        var entry = SampleFile with { IsText = true };
        string json = Formatting.FormatNdjsonLine(entry, "files", "1.0.0");

        Assert.Contains("\"is_text\":true", json);
    }

    [Fact]
    public void FormatNdjsonLine_WithoutIsText_OmitsField()
    {
        string json = Formatting.FormatNdjsonLine(SampleFile, "files", "1.0.0");

        Assert.DoesNotContain("is_text", json);
    }

    [Fact]
    public void FormatJsonSummary_ContainsStandardFields()
    {
        string json = Formatting.FormatJsonSummary(42, new[] { "src" }, 0, "success", "files", "1.0.0");

        Assert.Contains("\"tool\":\"files\"", json);
        Assert.Contains("\"version\":\"1.0.0\"", json);
        Assert.Contains("\"exit_code\":0", json);
        Assert.Contains("\"exit_reason\":\"success\"", json);
        Assert.Contains("\"count\":42", json);
        Assert.Contains("\"searched_roots\":[\"src\"]", json);
    }

    [Fact]
    public void FormatJsonError_ContainsErrorFields()
    {
        string json = Formatting.FormatJsonError(125, "usage_error", "files", "1.0.0");

        Assert.Contains("\"exit_code\":125", json);
        Assert.Contains("\"exit_reason\":\"usage_error\"", json);
    }

    [Fact]
    public void FormatTypeString_CorrectValues()
    {
        Assert.Equal("file", Formatting.FormatTypeString(FileEntryType.File));
        Assert.Equal("dir", Formatting.FormatTypeString(FileEntryType.Directory));
        Assert.Equal("link", Formatting.FormatTypeString(FileEntryType.Symlink));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Files.Tests -v quiet`
Expected: Build error — `Formatting` methods don't exist

- [ ] **Step 3: Implement Formatting**

```csharp
// src/Winix.Files/Formatting.cs
using System.Globalization;
using System.Text;
using Winix.FileWalk;

namespace Winix.Files;

/// <summary>Output formatting for the files tool. All methods are pure (no I/O).</summary>
public static class Formatting
{
    /// <summary>Formats a FileEntry as a plain path string.</summary>
    public static string FormatPath(FileEntry entry)
    {
        return entry.Path;
    }

    /// <summary>Formats a FileEntry as a tab-delimited long line: path, size, modified, type.</summary>
    public static string FormatLong(FileEntry entry)
    {
        string size = entry.SizeBytes >= 0
            ? entry.SizeBytes.ToString("N0", CultureInfo.InvariantCulture)
            : "-";
        string modified = entry.Modified != default
            ? entry.Modified.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "-";
        string type = FormatTypeString(entry.Type);

        return $"{entry.Path}\t{size}\t{modified}\t{type}";
    }

    /// <summary>Formats a FileEntry as an NDJSON line with standard Winix fields.</summary>
    public static string FormatNdjsonLine(FileEntry entry, string toolName, string version)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append($"\"tool\":\"{EscapeJson(toolName)}\"");
        sb.Append($",\"version\":\"{EscapeJson(version)}\"");
        sb.Append(",\"exit_code\":0");
        sb.Append(",\"exit_reason\":\"success\"");
        sb.Append($",\"path\":\"{EscapeJson(entry.Path)}\"");
        sb.Append($",\"name\":\"{EscapeJson(entry.Name)}\"");
        sb.Append($",\"type\":\"{FormatTypeString(entry.Type)}\"");
        sb.Append(string.Create(CultureInfo.InvariantCulture, $",\"size_bytes\":{entry.SizeBytes}"));
        sb.Append($",\"modified\":\"{entry.Modified.ToString("o", CultureInfo.InvariantCulture)}\"");
        sb.Append(string.Create(CultureInfo.InvariantCulture, $",\"depth\":{entry.Depth}"));

        if (entry.IsText.HasValue)
        {
            sb.Append(entry.IsText.Value ? ",\"is_text\":true" : ",\"is_text\":false");
        }

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>Formats a JSON summary object (written to stderr after walk completes).</summary>
    public static string FormatJsonSummary(int count, IReadOnlyList<string> searchedRoots, int exitCode, string exitReason, string toolName, string version)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append($"\"tool\":\"{EscapeJson(toolName)}\"");
        sb.Append($",\"version\":\"{EscapeJson(version)}\"");
        sb.Append(string.Create(CultureInfo.InvariantCulture, $",\"exit_code\":{exitCode}"));
        sb.Append($",\"exit_reason\":\"{EscapeJson(exitReason)}\"");
        sb.Append(string.Create(CultureInfo.InvariantCulture, $",\"count\":{count}"));

        sb.Append(",\"searched_roots\":[");
        for (int i = 0; i < searchedRoots.Count; i++)
        {
            if (i > 0) { sb.Append(','); }
            sb.Append($"\"{EscapeJson(searchedRoots[i])}\"");
        }
        sb.Append(']');

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>Formats a JSON error object.</summary>
    public static string FormatJsonError(int exitCode, string exitReason, string toolName, string version)
    {
        return $"{{\"tool\":\"{EscapeJson(toolName)}\",\"version\":\"{EscapeJson(version)}\",\"exit_code\":{exitCode},\"exit_reason\":\"{EscapeJson(exitReason)}\"}}";
    }

    /// <summary>Returns a short string representation of a FileEntryType.</summary>
    public static string FormatTypeString(FileEntryType type)
    {
        return type switch
        {
            FileEntryType.File => "file",
            FileEntryType.Directory => "dir",
            FileEntryType.Symlink => "link",
            _ => "unknown"
        };
    }

    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Files.Tests -v quiet`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Files/Formatting.cs tests/Winix.Files.Tests/FormattingTests.cs
git commit -m "feat(files): output formatting — paths, long, NDJSON, JSON summary"
```

---

## Task 11: files Console App (Program.cs)

The thin console app: arg parsing, pipeline wiring, `--describe` metadata, exit codes.

**Files:**
- Modify: `src/files/Program.cs` (replace placeholder)

- [ ] **Step 1: Implement Program.cs**

```csharp
// src/files/Program.cs
using System.Reflection;
using System.Runtime.InteropServices;
using Winix.FileWalk;
using Winix.Files;
using Yort.ShellKit;

namespace Files;

internal sealed class Program
{
    static int Main(string[] args)
    {
        string version = GetVersion();
        bool platformCaseInsensitive = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        var parser = new CommandLineParser("files", version)
            .Description("Find files by name, size, date, type, and content. A cross-platform find replacement.")
            .StandardFlags()
            .Flag("--ndjson", "Streaming NDJSON per file to stdout")
            .ListOption("--glob", "-g", "PATTERN", "Match filenames against glob pattern")
            .ListOption("--regex", "-e", "PATTERN", "Match filenames against regex")
            .ListOption("--ext", null, "EXT", "Match file extension (e.g. cs, json)")
            .Option("--type", "-t", "TYPE", "Filter by type: f (file), d (directory), l (symlink)")
            .Flag("--text", "Only text files (no null bytes in first 8KB)")
            .Flag("--binary", "Only binary files (has null bytes in first 8KB)")
            .Option("--min-size", null, "SIZE", "Minimum file size (e.g. 100k, 10M, 1G)")
            .Option("--max-size", null, "SIZE", "Maximum file size")
            .Option("--newer", null, "DURATION", "Modified within duration (e.g. 1h, 30m, 7d)")
            .Option("--older", null, "DURATION", "Modified before duration")
            .IntOption("--max-depth", "-d", "N", "Maximum directory depth",
                n => n < 0 ? "must be >= 0" : null)
            .Flag("--follow", "-L", "Follow symlinks")
            .Flag("--absolute", "Output absolute paths")
            .Flag("--no-hidden", "Skip hidden/dot files and directories")
            .Flag("--gitignore", "Respect .gitignore (requires git on PATH)")
            .Flag("--ignore-case", "-i", "Case-insensitive pattern matching")
            .Flag("--case-sensitive", "Case-sensitive pattern matching")
            .Flag("--long", "-l", "Tab-delimited: path, size, modified, type")
            .Flag("--print0", "-0", "Null-delimited output")
            .Positional("paths...")
            .ExitCodes(
                (0, "Success"),
                (1, "Runtime error"),
                (ExitCode.UsageError, "Usage error"))
            .Platform("cross-platform",
                replaces: new[] { "find" },
                valueOnWindows: "No native find equivalent; fills a major gap",
                valueOnUnix: "Cleaner flag syntax, --json output, composes with wargs")
            .StdinDescription("Not used")
            .StdoutDescription("One file path per line (default). Null-delimited with --print0. NDJSON with --ndjson.")
            .StderrDescription("Warnings, errors, and --json summary output.")
            .Example("files src --glob '*.cs'", "Find all C# source files under src/")
            .Example("files . --ext cs", "Find all C# files (shorthand for --glob '*.cs')")
            .Example("files . --newer 1h --type f", "Files modified in the last hour")
            .Example("files . --glob '*.log' | wargs rm", "Delete all log files (compose with wargs)")
            .Example("files . --long --ext cs", "List C# files with size and date")
            .Example("files . --text", "Find all text files (skip binaries)")
            .Example("files . --gitignore --no-hidden --ext cs", "fd-style: source files only")
            .ComposesWith("wargs",
                "files ... | wargs <command>",
                "Find files then execute a command for each one (find | xargs pattern)")
            .ComposesWith("peep",
                "peep -- files . --glob '*.log' --newer 5m",
                "Watch for recently created log files on an interval")
            .ComposesWith("squeeze",
                "files . --glob '*.json' | wargs squeeze --zstd",
                "Compress all JSON files with zstd")
            .JsonField("path", "string", "File path (relative or absolute)")
            .JsonField("name", "string", "Filename only")
            .JsonField("type", "string", "file, directory, or symlink")
            .JsonField("size_bytes", "int", "File size in bytes (-1 for directories)")
            .JsonField("modified", "string", "ISO 8601 last modified timestamp")
            .JsonField("depth", "int", "Depth relative to search root")
            .JsonField("is_text", "bool?", "True if text, false if binary. Present only when --text/--binary used.");

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        // --- Resolve options ---
        bool jsonOutput = result.Has("--json");
        bool ndjsonOutput = result.Has("--ndjson");
        bool longOutput = result.Has("--long");
        bool print0 = result.Has("--print0");
        bool useColor = result.ResolveColor();

        bool text = result.Has("--text");
        bool binary = result.Has("--binary");
        if (text && binary)
        {
            return result.WriteError("--text and --binary are mutually exclusive", Console.Error);
        }

        bool ignoreCase = result.Has("--ignore-case");
        bool caseSensitive = result.Has("--case-sensitive");
        if (ignoreCase && caseSensitive)
        {
            return result.WriteError("--ignore-case and --case-sensitive are mutually exclusive", Console.Error);
        }

        bool caseInsensitive = ignoreCase || (!caseSensitive && platformCaseInsensitive);

        // --- Resolve type filter ---
        FileEntryType? typeFilter = null;
        if (result.Has("--type"))
        {
            string typeStr = result.GetString("--type");
            typeFilter = typeStr switch
            {
                "f" => FileEntryType.File,
                "d" => FileEntryType.Directory,
                "l" => FileEntryType.Symlink,
                _ => null
            };
            if (typeFilter is null)
            {
                return result.WriteError($"--type must be f, d, or l (got '{typeStr}')", Console.Error);
            }
        }

        // Validate --text/--binary with --type d
        if ((text || binary) && typeFilter == FileEntryType.Directory)
        {
            return result.WriteError("--text/--binary cannot be combined with --type d (directories have no content)", Console.Error);
        }

        // --- Resolve glob patterns (--glob + --ext combined) ---
        List<string> globs = new(result.GetList("--glob"));
        foreach (string ext in result.GetList("--ext"))
        {
            string cleanExt = ext.StartsWith('.') ? ext[1..] : ext;
            if (ext.StartsWith('.'))
            {
                Console.Error.WriteLine($"files: warning: --ext expects no leading dot, stripping: .{cleanExt}");
            }
            globs.Add($"*.{cleanExt}");
        }

        // --- Parse size/duration options ---
        long? minSize = null;
        if (result.Has("--min-size"))
        {
            if (!SizeParser.TryParse(result.GetString("--min-size"), out long ms))
            {
                return result.WriteError($"--min-size: invalid size '{result.GetString("--min-size")}'", Console.Error);
            }
            minSize = ms;
        }

        long? maxSize = null;
        if (result.Has("--max-size"))
        {
            if (!SizeParser.TryParse(result.GetString("--max-size"), out long ms))
            {
                return result.WriteError($"--max-size: invalid size '{result.GetString("--max-size")}'", Console.Error);
            }
            maxSize = ms;
        }

        DateTimeOffset? newerThan = null;
        if (result.Has("--newer"))
        {
            if (!DurationParser.TryParse(result.GetString("--newer"), out TimeSpan dur))
            {
                return result.WriteError($"--newer: invalid duration '{result.GetString("--newer")}'", Console.Error);
            }
            newerThan = DateTimeOffset.UtcNow - dur;
        }

        DateTimeOffset? olderThan = null;
        if (result.Has("--older"))
        {
            if (!DurationParser.TryParse(result.GetString("--older"), out TimeSpan dur))
            {
                return result.WriteError($"--older: invalid duration '{result.GetString("--older")}'", Console.Error);
            }
            olderThan = DateTimeOffset.UtcNow - dur;
        }

        // --- Build walker options ---
        var options = new FileWalkerOptions(
            GlobPatterns: globs,
            RegexPatterns: result.GetList("--regex"),
            TypeFilter: typeFilter,
            TextOnly: text ? true : binary ? false : null,
            MinSize: minSize,
            MaxSize: maxSize,
            NewerThan: newerThan,
            OlderThan: olderThan,
            MaxDepth: result.Has("--max-depth") ? result.GetInt("--max-depth") : null,
            IncludeHidden: !result.Has("--no-hidden"),
            FollowSymlinks: result.Has("--follow"),
            UseGitIgnore: result.Has("--gitignore"),
            AbsolutePaths: result.Has("--absolute"),
            CaseInsensitive: caseInsensitive);

        // --- Resolve search roots ---
        string[] roots = result.Positionals.Length > 0 ? result.Positionals : new[] { "." };

        // Validate roots exist
        var validRoots = new List<string>();
        foreach (string root in roots)
        {
            if (Directory.Exists(root))
            {
                validRoots.Add(root);
            }
            else
            {
                Console.Error.WriteLine($"files: {root}: No such directory");
            }
        }

        if (validRoots.Count == 0)
        {
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(1, "no_valid_roots", "files", version));
            }
            return 1;
        }

        // --- Set up gitignore filter ---
        GitIgnoreFilter? ignoreFilter = null;
        if (options.UseGitIgnore)
        {
            ignoreFilter = GitIgnoreFilter.Create(Path.GetFullPath(validRoots[0]));
            if (ignoreFilter is null)
            {
                Console.Error.WriteLine("files: warning: --gitignore specified but git not found or not in a git repo");
            }
        }

        // --- Walk and output ---
        try
        {
            var walker = new FileWalker(options, ignoreFilter);
            int count = 0;

            foreach (FileEntry entry in walker.Walk(validRoots))
            {
                count++;

                if (ndjsonOutput)
                {
                    Console.Out.WriteLine(Formatting.FormatNdjsonLine(entry, "files", version));
                }
                else if (print0)
                {
                    Console.Out.Write(entry.Path);
                    Console.Out.Write('\0');
                }
                else if (longOutput)
                {
                    Console.Out.WriteLine(Formatting.FormatLong(entry));
                }
                else
                {
                    Console.Out.WriteLine(Formatting.FormatPath(entry));
                }
            }

            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonSummary(count, validRoots, 0, "success", "files", version));
            }

            return 0;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"files: {ex.Message}");
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(1, "permission_denied", "files", version));
            }
            return 1;
        }
        finally
        {
            ignoreFilter?.Dispose();
        }
    }

    private static string GetVersion()
    {
        return typeof(FileWalker).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build Winix.sln`
Expected: Build succeeded, 0 warnings

- [ ] **Step 3: Smoke test**

Run: `dotnet run --project src/files -- src --ext cs --type f`
Expected: Lists all `.cs` files under `src/` directory, one per line

Run: `dotnet run --project src/files -- --describe`
Expected: JSON metadata output with all registered fields

Run: `dotnet run --project src/files -- --help`
Expected: Help text with all flags

- [ ] **Step 4: Commit**

```bash
git add src/files/Program.cs
git commit -m "feat(files): console app with arg parsing, pipeline wiring, and --describe metadata"
```

---

## Task 12: README and AI Discoverability Docs

Create the files tool README, AI guide, and llms.txt.

**Files:**
- Create: `src/files/README.md`
- Create: `docs/ai/files.md`
- Create: `llms.txt`
- Modify: `CLAUDE.md` (update project layout)

- [ ] **Step 1: Create files README**

Create `src/files/README.md` following the pattern of existing tool READMEs (see `src/wargs/README.md` for format). Include: description, install sections (scoop, winget, dotnet tool, direct download), usage/examples, options table, exit codes, colour section.

Key content points:
- Description: cross-platform `find` replacement with glob/regex patterns, text/binary detection, JSON output
- Highlight: finds everything by default (unlike fd), `--text`/`--binary` for content detection, `--describe` for AI agents
- Examples: `files src --ext cs`, `files . --text`, `files . --newer 1h --type f`, `files . --ndjson`, `files . --glob '*.log' | wargs rm`

- [ ] **Step 2: Create AI guide**

Create `docs/ai/files.md` following the template from the AI discoverability design doc. Sections: What This Tool Does, Platform Story, When to Use This, Common Patterns, Composing with Other Tools, Gotchas, Getting Structured Data.

Key content points:
- Platform story: cross-platform, fills Windows gap, improves on find syntax everywhere
- When to use: finding files by name/pattern, filtering by size/date/type, building file lists for batch operations, finding text-only files for analysis
- Common patterns: `files --ext cs`, `files --text`, `files --newer 1h`, `files | wargs`, `files --ndjson | jq`
- Gotchas: shows all files by default (unlike fd), `--gitignore` requires git on PATH, case sensitivity matches platform filesystem

- [ ] **Step 3: Create llms.txt**

Create `llms.txt` at the repo root following the content structure from the AI discoverability design doc. Initially includes only `files` (other tools added during retrofit).

```markdown
# Winix

Cross-platform CLI tool suite for the gaps between Windows and *nix. Native binaries (AOT-compiled .NET), no runtime required.

## Tools

- [files](docs/ai/files.md): Find files by name, size, date, type, and content. Replaces `find` with glob patterns and clean output.

## Key Features for AI Agents

- Every tool supports `--describe` for structured JSON metadata (flags, types, examples, composability, platform scope)
- Every tool supports `--json` for machine-parseable output with standard fields
- Consistent exit codes across all tools (0 = success, 125 = usage error)
- Tools compose via pipes: `files ... | wargs ...` replaces `find ... | xargs ...`
- All current tools are cross-platform. Each tool's `--describe` output includes a `platform` section explaining what it replaces and its value on each OS.

## Quick Reference

Run `<tool> --describe` to get full structured metadata for any tool.
Run `<tool> --help` for human-readable help.

## Install

Available via Scoop (Windows), winget (Windows), .NET tool (cross-platform), or direct download.
See: https://github.com/Yortw/winix
```

- [ ] **Step 4: Update CLAUDE.md project layout**

Add `files` entries to the project layout section in `CLAUDE.md`:
```
src/Winix.FileWalk/        — shared library (directory walking, predicates, glob/regex matching)
src/Winix.Files/           — class library (output formatting)
src/files/                 — console app entry point
tests/Winix.FileWalk.Tests/ — xUnit tests
tests/Winix.Files.Tests/   — xUnit tests
```

Also update the NuGet package IDs line to include `Winix.Files`.

- [ ] **Step 5: Commit**

```bash
git add src/files/README.md docs/ai/files.md llms.txt CLAUDE.md
git commit -m "docs: README, AI guide, and llms.txt for files tool"
```

---

## Task 13: Release Pipeline and Scoop Integration

Add files to the release pipeline, scoop manifests, and winget generation.

**Files:**
- Modify: `.github/workflows/release.yml`
- Create: `bucket/files.json`
- Modify: `bucket/winix.json`

- [ ] **Step 1: Add files to release.yml**

Add `files` to all pipeline steps that currently list timeit, squeeze, peep, wargs. This includes:
- `pack-nuget` job: add `dotnet pack src/files/files.csproj`
- `publish-aot` job: add `dotnet publish src/files/files.csproj` for each RID
- Zip steps (Linux/macOS and Windows): add files zip
- Combined winix zip step: add `files.exe`
- `generate-winget-manifests` job: add files manifest generation

Follow the exact pattern used by existing tools — copy a wargs line and change the tool name.

- [ ] **Step 2: Create scoop manifest**

Create `bucket/files.json` following the pattern of `bucket/wargs.json`. Key differences:
- Tool name: `files`
- Description: "Find files by name, size, date, type, and content. A cross-platform find replacement."

- [ ] **Step 3: Update combined scoop manifest**

Add `files.exe` to the `bin` array in `bucket/winix.json`.

- [ ] **Step 4: Verify build**

Run: `dotnet build Winix.sln`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/release.yml bucket/files.json bucket/winix.json
git commit -m "chore: add files to release pipeline, scoop manifests, and winget"
```

---

## Task 14: Retrofit --describe to Existing Tools

Add `--describe` metadata (platform, I/O, examples, composability, JSON fields) to timeit, squeeze, peep, and wargs.

**Files:**
- Modify: `src/timeit/Program.cs`
- Modify: `src/squeeze/Program.cs`
- Modify: `src/peep/Program.cs`
- Modify: `src/wargs/Program.cs`

- [ ] **Step 1: Add --describe metadata to timeit**

Add fluent calls after the existing `StandardFlags()` call in `src/timeit/Program.cs`. The metadata should include: `.Platform()`, `.StdinDescription()`, `.StdoutDescription()`, `.StderrDescription()`, `.Example()` (3-5 examples), `.ComposesWith()`, and `.JsonField()` for each field in timeit's `--json` output.

Refer to `src/Winix.TimeIt/Formatting.cs` for the JSON field names.

- [ ] **Step 2: Add --describe metadata to squeeze**

Same pattern as timeit. Refer to `src/Winix.Squeeze/Formatting.cs` for JSON fields.

- [ ] **Step 3: Add --describe metadata to peep**

Same pattern. Refer to `src/Winix.Peep/Formatting.cs` for JSON fields.

- [ ] **Step 4: Add --describe metadata to wargs**

Same pattern. Refer to `src/Winix.Wargs/Formatting.cs` for JSON fields.

- [ ] **Step 5: Verify all --describe outputs**

Run each tool with `--describe` and verify the JSON is valid and complete:
```bash
dotnet run --project src/timeit -- --describe | python -m json.tool
dotnet run --project src/squeeze -- --describe | python -m json.tool
dotnet run --project src/peep -- --describe | python -m json.tool
dotnet run --project src/wargs -- --describe | python -m json.tool
dotnet run --project src/files -- --describe | python -m json.tool
```

- [ ] **Step 6: Run full test suite**

Run: `dotnet test Winix.sln`
Expected: All tests pass. The new `--describe` flag is handled by the parser before tool logic runs, so existing tests should be unaffected.

- [ ] **Step 7: Commit**

```bash
git add src/timeit/Program.cs src/squeeze/Program.cs src/peep/Program.cs src/wargs/Program.cs
git commit -m "feat: add --describe AI discoverability metadata to all tools"
```

---

## Task 15: Retrofit AI Guides and Update llms.txt

Write AI guide docs for existing tools and update llms.txt with all entries.

**Files:**
- Create: `docs/ai/timeit.md`
- Create: `docs/ai/squeeze.md`
- Create: `docs/ai/peep.md`
- Create: `docs/ai/wargs.md`
- Modify: `llms.txt`

- [ ] **Step 1: Write AI guides**

Create `docs/ai/<tool>.md` for each existing tool following the template: What This Tool Does, Platform Story, When to Use This, Common Patterns, Composing with Other Tools, Gotchas, Getting Structured Data.

Each guide should be 50-100 lines, focused on workflow guidance and "when to use this" — not flag reference (that's what `--describe` is for).

- [ ] **Step 2: Update llms.txt**

Add all tools to the Tools section:
```markdown
- [timeit](docs/ai/timeit.md): Time a command — wall clock, CPU, memory, exit code. Replaces POSIX `time`.
- [squeeze](docs/ai/squeeze.md): Multi-format compression/decompression (gzip, brotli, zstd). Replaces `gzip`/`brotli`/`zstd`.
- [peep](docs/ai/peep.md): Watch a command on interval + re-run on file changes. Replaces `watch` + `entr`.
- [wargs](docs/ai/wargs.md): Build and execute commands from stdin. Replaces `xargs` with sane defaults.
- [files](docs/ai/files.md): Find files by name, size, date, type, and content. Replaces `find` with glob patterns and clean output.
```

- [ ] **Step 3: Update root README.md**

Add `files` and `wargs` to the shipped tools table in `README.md`. Update the status line.

- [ ] **Step 4: Commit**

```bash
git add docs/ai/ llms.txt README.md
git commit -m "docs: AI guides for all tools and updated llms.txt"
```

---

## Task 16: Final Verification

End-to-end verification that everything works together.

- [ ] **Step 1: Full build and test**

Run: `dotnet build Winix.sln`
Expected: 0 warnings, 0 errors

Run: `dotnet test Winix.sln`
Expected: All tests pass

- [ ] **Step 2: Smoke test files tool**

```bash
# Basic search
dotnet run --project src/files -- src --ext cs --type f

# Long format
dotnet run --project src/files -- src --ext cs --long

# NDJSON
dotnet run --project src/files -- src --ext cs --ndjson

# Text files only
dotnet run --project src/files -- src --text --type f

# Describe
dotnet run --project src/files -- --describe

# Compose with wargs (dry run)
dotnet run --project src/files -- src --ext cs | dotnet run --project src/wargs -- --dry-run echo
```

- [ ] **Step 3: Trial NuGet pack**

```bash
dotnet pack src/files/files.csproj -c Release -o /tmp/files-pack-test
unzip -l /tmp/files-pack-test/Winix.Files.*.nupkg | grep -i winix.png
```

Expected: Package created successfully, icon included.

- [ ] **Step 4: Push**

```bash
git push
```
