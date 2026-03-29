# Yort.ShellKit — Shared Library Extraction

**Date:** 2026-03-29
**Status:** Design approved
**Project:** Winix (`D:\projects\winix`)

## Purpose

Extract duplicated terminal/formatting code from `Winix.TimeIt` and `Winix.Squeeze` into a shared library `Yort.ShellKit`. Both tools currently have identical copies of `ConsoleEnv` and `AnsiColor`, and near-identical copies of `FormatBytes` and `FormatDuration`. This extraction eliminates the duplication, unifies the divergent formatting to the best version of each, and establishes the shared library for future tools.

## Scope

Tightly scoped to what's duplicated today. No speculative additions.

### Extracted into `Yort.ShellKit`

| Class | Origin | Changes |
|-------|--------|---------|
| `ConsoleEnv` | Identical in both tools | Namespace only |
| `AnsiColor` | Identical in both tools | `internal` → `public` (consumers call it from their formatters) |
| `DisplayFormat` | New — merges best of both | `FormatBytes` from squeeze (B/KB/MB/GB with decimals), `FormatDuration` from timeit (with hours tier) |

### Stays in each tool

- Tool-specific result records (`TimeItResult`, `SqueezeResult`)
- Tool-specific formatters (`FormatDefault`, `FormatOneLine`, `FormatJson`, `FormatHuman`)
- Tool-specific logic (compression, process spawning, NativeMetrics, etc.)

### Why `DisplayFormat` instead of `Formatting`

Each tool already has a `Formatting` class with tool-specific methods. Using `DisplayFormat` for the shared class avoids naming collisions and clarifies the boundary: `DisplayFormat` is generic display helpers, `Formatting` is tool-specific output composition.

## Project Structure

```
src/
  Yort.ShellKit/
    Yort.ShellKit.csproj
    ConsoleEnv.cs
    AnsiColor.cs
    DisplayFormat.cs
tests/
  Yort.ShellKit.Tests/
    Yort.ShellKit.Tests.csproj
    ConsoleEnvTests.cs
    DisplayFormatTests.cs
```

### csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
</Project>
```

No dependencies. No `AllowUnsafeBlocks` (that's timeit's NativeMetrics concern, not ShellKit's).

### Dependency Chain

- `Yort.ShellKit` → nothing
- `Winix.TimeIt` → `Yort.ShellKit`
- `Winix.Squeeze` → `Yort.ShellKit`

Namespace: `Yort.ShellKit` throughout. Consumers use `using Yort.ShellKit;`.

## API Surface

### ConsoleEnv (unchanged)

```csharp
public static class ConsoleEnv
{
    public static bool IsNoColorEnvSet();
    public static bool IsTerminal(bool checkStdErr);
    public static bool ResolveUseColor(bool colorFlag, bool noColorFlag, bool noColorEnv, bool isTerminal);
}
```

### AnsiColor (internal → public)

```csharp
public static class AnsiColor
{
    public static string Dim(bool enabled);
    public static string Green(bool enabled);
    public static string Red(bool enabled);
    public static string Reset(bool enabled);
}
```

### DisplayFormat (new — unified)

```csharp
public static class DisplayFormat
{
    /// <summary>
    /// Formats a byte count as a human-friendly auto-scaling string.
    /// 0-1023: "500 B". 1 KB-1 MB: "384.0 KB". 1 MB-1 GB: "1.5 MB". 1 GB+: "2.3 GB".
    /// </summary>
    public static string FormatBytes(long bytes);

    /// <summary>
    /// Formats a duration as a human-friendly auto-scaling string.
    /// Under 1s: "0.842s". 1-60s: "12.4s". 1-60m: "3m 27.1s". Over 60m: "1h 12m 03s".
    /// </summary>
    public static string FormatDuration(TimeSpan duration);
}
```

`FormatBytes` uses squeeze's implementation (handles bytes, uses decimals for KB/MB).
`FormatDuration` uses timeit's implementation (includes hours tier).

## Impact on Existing Tools

### Winix.TimeIt

**Files deleted:**
- `src/Winix.TimeIt/ConsoleEnv.cs`
- `src/Winix.TimeIt/AnsiColor.cs`

**Files modified:**
- `src/Winix.TimeIt/Winix.TimeIt.csproj` — add `ProjectReference` to `Yort.ShellKit`
- `src/Winix.TimeIt/Formatting.cs` — delete `FormatBytes` and `FormatDuration` methods, call `DisplayFormat.FormatBytes` and `DisplayFormat.FormatDuration` instead. Add `using Yort.ShellKit;`.

**Test changes:**
- Delete `tests/Winix.TimeIt.Tests/ConsoleEnvTests.cs` (moved to ShellKit)
- Update `FormatBytesTests` — values change to match unified format:
  - `0` → `"0 B"` (was `"0 KB"`)
  - `393_216` → `"384.0 KB"` (was `"384 KB"`)
  - `999_424` → `"976.0 KB"` (was `"976 KB"`)
  - `1_048_576` → `"1.0 MB"` (was `"1 MB"`)
  - `505_413_632` → `"482.0 MB"` (was `"482 MB"`)
- Update any composed-output assertions in `FormatDefaultTests`, `FormatOneLineTests` that check for specific byte strings (e.g. `"482 MB"` → `"482.0 MB"`)
- `FormatDurationTests` — no changes (timeit's version is the one we're keeping)

### Winix.Squeeze

**Files deleted:**
- `src/Winix.Squeeze/ConsoleEnv.cs`
- `src/Winix.Squeeze/AnsiColor.cs`

**Files modified:**
- `src/Winix.Squeeze/Winix.Squeeze.csproj` — add `ProjectReference` to `Yort.ShellKit`
- `src/Winix.Squeeze/Formatting.cs` — delete `FormatBytes` and `FormatDuration` methods, call `DisplayFormat` instead. Add `using Yort.ShellKit;`.

**Test changes:**
- Delete `FormatBytesTests` and `FormatDurationTests` from squeeze's `FormattingTests.cs` (moved to ShellKit)
- Existing composed-output tests should pass unchanged (squeeze was already using the format being kept)
- Add hours-tier duration test case (now supported by shared `FormatDuration`)

## Testing

### Yort.ShellKit.Tests

- `ConsoleEnvTests.cs` — moved from timeit tests, same test cases, `Yort.ShellKit` namespace
- `DisplayFormatTests.cs` — unified set:
  - `FormatBytes`: 0 B, 500 B, 1.0 KB, 512.0 KB, 1.0 MB, 1.5 MB, 1.0 GB, 2.3 GB
  - `FormatDuration`: sub-second (0.842s), seconds (12.4s), minutes (3m 27.1s), hours (1h 12m 03s)

### Verification

After extraction, `dotnet test Winix.sln` must pass with all existing tests (updated for unified format) plus the new ShellKit tests. No behavioural changes except timeit's byte formatting gaining precision (B tier, decimals for KB/MB).

## Explicitly Not In This Extraction

- **No new ShellKit components** (ArgBuilder, GlobExpander, PipeReader, etc.) — add when a tool needs them
- **No NuGet publishing** — just a project reference within the solution for now
- **No JSON formatting in ShellKit** — each tool's JSON schema is specific; only generic display helpers belong in ShellKit
- **No separate repo** — stays in Winix until external consumers appear
