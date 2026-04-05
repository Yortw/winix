# winix Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a cross-platform CLI tool that installs, updates, and uninstalls all Winix suite tools by delegating to the platform's native package manager (winget, scoop, brew, dotnet tool).

**Architecture:** Class library (`Winix.Winix`) holds all logic — manifest parsing, PM adapter interface + 4 adapters, orchestration engine, and output formatting. Thin console app (`winix`) does arg parsing via ShellKit, calls the library, and sets exit codes. Stateless: queries PMs for installed state every time. Fetches a JSON manifest from GitHub releases for tool discovery.

**Tech Stack:** .NET 10 / C# / xUnit. AOT-ready. No external dependencies beyond ShellKit.

---

## File Structure

### Class Library: `src/Winix.Winix/`

| File | Responsibility |
|------|---------------|
| `Winix.Winix.csproj` | Class library project, references ShellKit |
| `ToolManifest.cs` | Manifest model: `ToolManifest`, `ToolEntry`, `PackageIds` records |
| `ManifestLoader.cs` | Fetch + parse `winix-manifest.json` from GitHub releases |
| `IPackageManagerAdapter.cs` | Interface: `IsAvailable`, `IsInstalled`, `GetInstalledVersion`, `Install`, `Update`, `Uninstall` |
| `WingetAdapter.cs` | Winget implementation of `IPackageManagerAdapter` |
| `ScoopAdapter.cs` | Scoop implementation of `IPackageManagerAdapter` |
| `BrewAdapter.cs` | Homebrew implementation of `IPackageManagerAdapter` |
| `DotnetToolAdapter.cs` | `dotnet tool` implementation of `IPackageManagerAdapter` |
| `ProcessHelper.cs` | Shared helper: run a process, capture stdout/stderr, return exit code |
| `PlatformDetector.cs` | Detect OS, find available PMs, resolve default PM chain |
| `SuiteManager.cs` | Orchestration: install/update/uninstall/list/status for a set of tools |
| `Formatting.cs` | Output formatting: tool result lines, list table, status summary, --describe JSON |

### Console App: `src/winix/`

| File | Responsibility |
|------|---------------|
| `winix.csproj` | Console app, references Winix.Winix |
| `Program.cs` | Arg parsing via CommandLineParser, call SuiteManager, exit codes |
| `README.md` | Tool documentation |

### Tests: `tests/Winix.Winix.Tests/`

| File | Responsibility |
|------|---------------|
| `Winix.Winix.Tests.csproj` | Test project, references Winix.Winix |
| `ToolManifestTests.cs` | Manifest parsing: valid, missing fields, unknown tools |
| `PlatformDetectorTests.cs` | PM chain resolution, --via override, no-PM-found |
| `FormattingTests.cs` | Output formatting: result lines, list table, status summary |
| `ProcessHelperTests.cs` | Integration tests with fake PM scripts |
| `WingetAdapterTests.cs` | Winget adapter argument construction and output parsing |
| `ScoopAdapterTests.cs` | Scoop adapter argument construction and output parsing |
| `BrewAdapterTests.cs` | Brew adapter argument construction and output parsing |
| `DotnetToolAdapterTests.cs` | Dotnet tool adapter argument construction and output parsing |
| `SuiteManagerTests.cs` | Orchestration: all-succeed, partial-failure, dry-run, filtering |

---

## Task 1: Project scaffolding

**Files:**
- Create: `src/Winix.Winix/Winix.Winix.csproj`
- Create: `src/winix/winix.csproj`
- Create: `tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj`
- Modify: `Winix.sln`

- [ ] **Step 1: Create the class library project**

```xml
<!-- src/Winix.Winix/Winix.Winix.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
    <IsPackable>false</IsPackable>
    <PackageId>Winix.Winix.Library</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the console app project**

```xml
<!-- src/winix/winix.csproj -->
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
    <ToolCommandName>winix</ToolCommandName>
    <PackageId>Winix.Winix</PackageId>
    <Description>Cross-platform installer for the Winix CLI tool suite.</Description>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.Winix\Winix.Winix.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create a placeholder Program.cs so it compiles**

```csharp
// src/winix/Program.cs
namespace Winix;

internal sealed class Program
{
    static int Main(string[] args)
    {
        return 0;
    }
}
```

- [ ] **Step 4: Create the test project**

```xml
<!-- tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj -->
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
    <ProjectReference Include="..\..\src\Winix.Winix\Winix.Winix.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Add all three projects to the solution**

```bash
dotnet sln Winix.sln add src/Winix.Winix/Winix.Winix.csproj --solution-folder src
dotnet sln Winix.sln add src/winix/winix.csproj --solution-folder src
dotnet sln Winix.sln add tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --solution-folder tests
```

- [ ] **Step 6: Build the solution**

Run: `dotnet build Winix.sln`
Expected: Build succeeds with 0 errors, 0 warnings.

- [ ] **Step 7: Run tests**

Run: `dotnet test Winix.sln`
Expected: All existing tests pass. No new tests yet (test project has no test classes).

- [ ] **Step 8: Commit**

```bash
git add src/Winix.Winix/Winix.Winix.csproj src/winix/winix.csproj src/winix/Program.cs tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj Winix.sln
git commit -m "chore: scaffold winix installer projects (library, console app, tests)"
```

---

## Task 2: Manifest model and parsing

**Files:**
- Create: `src/Winix.Winix/ToolManifest.cs`
- Create: `src/Winix.Winix/ManifestLoader.cs`
- Create: `tests/Winix.Winix.Tests/ToolManifestTests.cs`

- [ ] **Step 1: Write failing tests for manifest parsing**

```csharp
// tests/Winix.Winix.Tests/ToolManifestTests.cs
using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class ToolManifestTests
{
    [Fact]
    public void Parse_ValidManifest_ReturnsAllTools()
    {
        string json = """
            {
              "version": "0.2.0",
              "tools": {
                "timeit": {
                  "description": "Time a command.",
                  "packages": {
                    "winget": "Winix.TimeIt",
                    "scoop": "timeit",
                    "brew": "timeit",
                    "dotnet": "Winix.TimeIt"
                  }
                },
                "squeeze": {
                  "description": "Compress files.",
                  "packages": {
                    "winget": "Winix.Squeeze",
                    "scoop": "squeeze",
                    "brew": "squeeze",
                    "dotnet": "Winix.Squeeze"
                  }
                }
              }
            }
            """;

        ToolManifest manifest = ToolManifest.Parse(json);

        Assert.Equal("0.2.0", manifest.Version);
        Assert.Equal(2, manifest.Tools.Count);
        Assert.True(manifest.Tools.ContainsKey("timeit"));
        Assert.True(manifest.Tools.ContainsKey("squeeze"));
    }

    [Fact]
    public void Parse_ToolEntry_HasCorrectPackageIds()
    {
        string json = """
            {
              "version": "0.1.0",
              "tools": {
                "timeit": {
                  "description": "Time a command.",
                  "packages": {
                    "winget": "Winix.TimeIt",
                    "scoop": "timeit",
                    "brew": "timeit",
                    "dotnet": "Winix.TimeIt"
                  }
                }
              }
            }
            """;

        ToolManifest manifest = ToolManifest.Parse(json);
        ToolEntry entry = manifest.Tools["timeit"];

        Assert.Equal("Time a command.", entry.Description);
        Assert.Equal("Winix.TimeIt", entry.GetPackageId("winget"));
        Assert.Equal("timeit", entry.GetPackageId("scoop"));
        Assert.Equal("timeit", entry.GetPackageId("brew"));
        Assert.Equal("Winix.TimeIt", entry.GetPackageId("dotnet"));
    }

    [Fact]
    public void Parse_MissingVersion_Throws()
    {
        string json = """{ "tools": {} }""";

        var ex = Assert.Throws<ManifestParseException>(() => ToolManifest.Parse(json));
        Assert.Contains("version", ex.Message);
    }

    [Fact]
    public void Parse_MissingTools_Throws()
    {
        string json = """{ "version": "0.1.0" }""";

        var ex = Assert.Throws<ManifestParseException>(() => ToolManifest.Parse(json));
        Assert.Contains("tools", ex.Message);
    }

    [Fact]
    public void Parse_EmptyTools_ReturnsEmptyDictionary()
    {
        string json = """{ "version": "0.1.0", "tools": {} }""";

        ToolManifest manifest = ToolManifest.Parse(json);

        Assert.Empty(manifest.Tools);
    }

    [Fact]
    public void Parse_InvalidJson_Throws()
    {
        string json = "not json";

        Assert.Throws<ManifestParseException>(() => ToolManifest.Parse(json));
    }

    [Fact]
    public void GetPackageId_UnknownPm_ReturnsNull()
    {
        string json = """
            {
              "version": "0.1.0",
              "tools": {
                "timeit": {
                  "description": "Time a command.",
                  "packages": { "winget": "Winix.TimeIt" }
                }
              }
            }
            """;

        ToolManifest manifest = ToolManifest.Parse(json);

        Assert.Null(manifest.Tools["timeit"].GetPackageId("brew"));
    }

    [Fact]
    public void GetToolNames_ReturnsAllKeys()
    {
        string json = """
            {
              "version": "0.1.0",
              "tools": {
                "timeit": { "description": "A", "packages": {} },
                "squeeze": { "description": "B", "packages": {} }
              }
            }
            """;

        ToolManifest manifest = ToolManifest.Parse(json);
        string[] names = manifest.GetToolNames();

        Assert.Equal(2, names.Length);
        Assert.Contains("timeit", names);
        Assert.Contains("squeeze", names);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: FAIL — `ToolManifest`, `ToolEntry`, `ManifestParseException` do not exist.

- [ ] **Step 3: Implement the manifest model**

```csharp
// src/Winix.Winix/ToolManifest.cs
using System.Text.Json;

namespace Winix.Winix;

/// <summary>
/// Parsed tool manifest listing all Winix suite tools and their per-PM package identifiers.
/// </summary>
public sealed class ToolManifest
{
    /// <summary>Version of the Winix suite this manifest describes.</summary>
    public string Version { get; }

    /// <summary>Tools indexed by short name (e.g. "timeit", "squeeze").</summary>
    public IReadOnlyDictionary<string, ToolEntry> Tools { get; }

    private ToolManifest(string version, Dictionary<string, ToolEntry> tools)
    {
        Version = version;
        Tools = tools;
    }

    /// <summary>Returns all tool names in the manifest.</summary>
    public string[] GetToolNames()
    {
        return Tools.Keys.ToArray();
    }

    /// <summary>
    /// Parses a JSON manifest string into a <see cref="ToolManifest"/>.
    /// </summary>
    /// <exception cref="ManifestParseException">JSON is invalid or required fields are missing.</exception>
    public static ToolManifest Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ManifestParseException("Invalid JSON in manifest: " + ex.Message, ex);
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("version", out JsonElement versionEl) ||
                versionEl.ValueKind != JsonValueKind.String)
            {
                throw new ManifestParseException("Manifest missing required 'version' field.");
            }

            string version = versionEl.GetString()!;

            if (!root.TryGetProperty("tools", out JsonElement toolsEl) ||
                toolsEl.ValueKind != JsonValueKind.Object)
            {
                throw new ManifestParseException("Manifest missing required 'tools' field.");
            }

            var tools = new Dictionary<string, ToolEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty toolProp in toolsEl.EnumerateObject())
            {
                string toolName = toolProp.Name;
                JsonElement toolObj = toolProp.Value;

                string description = "";
                if (toolObj.TryGetProperty("description", out JsonElement descEl) &&
                    descEl.ValueKind == JsonValueKind.String)
                {
                    description = descEl.GetString()!;
                }

                var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (toolObj.TryGetProperty("packages", out JsonElement pkgEl) &&
                    pkgEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty pkg in pkgEl.EnumerateObject())
                    {
                        if (pkg.Value.ValueKind == JsonValueKind.String)
                        {
                            packages[pkg.Name] = pkg.Value.GetString()!;
                        }
                    }
                }

                tools[toolName] = new ToolEntry(description, packages);
            }

            return new ToolManifest(version, tools);
        }
    }
}

/// <summary>A single tool's metadata from the manifest.</summary>
public sealed class ToolEntry
{
    /// <summary>Human-readable tool description.</summary>
    public string Description { get; }

    private readonly Dictionary<string, string> _packages;

    internal ToolEntry(string description, Dictionary<string, string> packages)
    {
        Description = description;
        _packages = packages;
    }

    /// <summary>
    /// Returns the package ID for the given package manager, or null if not defined.
    /// </summary>
    /// <param name="pmName">Package manager name (e.g. "winget", "scoop", "brew", "dotnet").</param>
    public string? GetPackageId(string pmName)
    {
        return _packages.TryGetValue(pmName, out string? id) ? id : null;
    }
}

/// <summary>Thrown when a manifest cannot be parsed.</summary>
public sealed class ManifestParseException : Exception
{
    /// <inheritdoc />
    public ManifestParseException(string message) : base(message) { }

    /// <inheritdoc />
    public ManifestParseException(string message, Exception innerException) : base(message, innerException) { }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: All 7 tests PASS.

- [ ] **Step 5: Implement ManifestLoader**

```csharp
// src/Winix.Winix/ManifestLoader.cs
using System.Net.Http;

namespace Winix.Winix;

/// <summary>
/// Fetches the tool manifest from GitHub releases.
/// </summary>
public static class ManifestLoader
{
    /// <summary>Default manifest URL: latest GitHub release asset.</summary>
    public const string DefaultUrl = "https://github.com/Yortw/winix/releases/latest/download/winix-manifest.json";

    /// <summary>
    /// Downloads and parses the tool manifest.
    /// </summary>
    /// <param name="url">Override URL for testing. Defaults to <see cref="DefaultUrl"/>.</param>
    /// <returns>Parsed manifest.</returns>
    /// <exception cref="ManifestParseException">Download or parse failed.</exception>
    public static async Task<ToolManifest> LoadAsync(string? url = null)
    {
        url ??= DefaultUrl;

        string json;
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            // GitHub releases redirect — HttpClient follows by default
            json = await client.GetStringAsync(url).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ManifestParseException(
                $"Failed to download manifest from {url}: {ex.Message}", ex);
        }

        return ToolManifest.Parse(json);
    }
}
```

- [ ] **Step 6: Build the solution**

Run: `dotnet build Winix.sln`
Expected: 0 errors, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/Winix.Winix/ToolManifest.cs src/Winix.Winix/ManifestLoader.cs tests/Winix.Winix.Tests/ToolManifestTests.cs
git commit -m "feat(winix): add manifest model and parser with tests"
```

---

## Task 3: Process helper

**Files:**
- Create: `src/Winix.Winix/ProcessHelper.cs`
- Create: `tests/Winix.Winix.Tests/ProcessHelperTests.cs`

- [ ] **Step 1: Write failing tests for ProcessHelper**

```csharp
// tests/Winix.Winix.Tests/ProcessHelperTests.cs
using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class ProcessHelperTests
{
    [Fact]
    public async Task RunAsync_CapturesStdoutAndExitCode()
    {
        // "dotnet --version" is always available in CI and dev machines
        ProcessResult result = await ProcessHelper.RunAsync("dotnet", new[] { "--version" });

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Stdout));
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task RunAsync_NonZeroExitCode_CapturesStderr()
    {
        // "dotnet --invalid-flag" writes an error to stderr and returns non-zero
        ProcessResult result = await ProcessHelper.RunAsync("dotnet", new[] { "--invalid-flag" });

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_CommandNotFound_ReturnsNotFoundResult()
    {
        ProcessResult result = await ProcessHelper.RunAsync(
            "winix-definitely-not-a-real-command-9999", Array.Empty<string>());

        Assert.True(result.IsNotFound);
    }

    [Fact]
    public async Task IsOnPath_Dotnet_ReturnsTrue()
    {
        bool found = ProcessHelper.IsOnPath("dotnet");

        Assert.True(found);
    }

    [Fact]
    public async Task IsOnPath_FakeCommand_ReturnsFalse()
    {
        bool found = ProcessHelper.IsOnPath("winix-definitely-not-a-real-command-9999");

        Assert.False(found);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: FAIL — `ProcessHelper` and `ProcessResult` do not exist.

- [ ] **Step 3: Implement ProcessHelper**

```csharp
// src/Winix.Winix/ProcessHelper.cs
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Winix.Winix;

/// <summary>
/// Result of running a child process.
/// </summary>
public sealed class ProcessResult
{
    /// <summary>Process exit code. -1 if the process could not be started.</summary>
    public int ExitCode { get; }

    /// <summary>Captured stdout.</summary>
    public string Stdout { get; }

    /// <summary>Captured stderr.</summary>
    public string Stderr { get; }

    /// <summary>True if the command was not found on PATH.</summary>
    public bool IsNotFound { get; }

    internal ProcessResult(int exitCode, string stdout, string stderr, bool isNotFound = false)
    {
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
        IsNotFound = isNotFound;
    }
}

/// <summary>
/// Runs child processes with captured output. Uses <see cref="ProcessStartInfo.ArgumentList"/>
/// for safe argument passing (project convention).
/// </summary>
public static class ProcessHelper
{
    /// <summary>
    /// Runs a command and captures stdout, stderr, and exit code.
    /// Returns a not-found result instead of throwing if the command doesn't exist.
    /// </summary>
    /// <param name="command">Executable name or path.</param>
    /// <param name="arguments">Arguments to pass.</param>
    public static async Task<ProcessResult> RunAsync(string command, string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };

        foreach (string arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)!;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2 || ex.NativeErrorCode == 3)
        {
            // ERROR_FILE_NOT_FOUND (2) or ERROR_PATH_NOT_FOUND (3)
            return new ProcessResult(-1, "", "", isNotFound: true);
        }

        using (process)
        {
            // Close stdin immediately — PM commands don't need interactive input
            process.StandardInput.Close();

            // Read stdout and stderr concurrently to avoid deadlocks
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            return new ProcessResult(
                process.ExitCode,
                stdoutTask.Result.Trim(),
                stderrTask.Result.Trim());
        }
    }

    /// <summary>
    /// Checks whether <paramref name="command"/> is available on PATH by attempting to
    /// run it with <c>--version</c>. Returns true if the process starts successfully,
    /// regardless of exit code.
    /// </summary>
    public static bool IsOnPath(string command)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--version");

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            process.StandardInput.Close();
            // Don't wait for completion — we only care that it started
            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: All tests PASS (manifest tests + process helper tests).

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/ProcessHelper.cs tests/Winix.Winix.Tests/ProcessHelperTests.cs
git commit -m "feat(winix): add ProcessHelper for running child processes with captured output"
```

---

## Task 4: Package manager adapter interface and PlatformDetector

**Files:**
- Create: `src/Winix.Winix/IPackageManagerAdapter.cs`
- Create: `src/Winix.Winix/PlatformDetector.cs`
- Create: `tests/Winix.Winix.Tests/PlatformDetectorTests.cs`

- [ ] **Step 1: Write failing tests for PlatformDetector**

```csharp
// tests/Winix.Winix.Tests/PlatformDetectorTests.cs
using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class PlatformDetectorTests
{
    [Fact]
    public void GetDefaultChain_Windows_ReturnsWingetScoopDotnet()
    {
        string[] chain = PlatformDetector.GetDefaultChain(PlatformId.Windows);

        Assert.Equal(new[] { "winget", "scoop", "dotnet" }, chain);
    }

    [Fact]
    public void GetDefaultChain_MacOS_ReturnsBrewDotnet()
    {
        string[] chain = PlatformDetector.GetDefaultChain(PlatformId.MacOS);

        Assert.Equal(new[] { "brew", "dotnet" }, chain);
    }

    [Fact]
    public void GetDefaultChain_Linux_ReturnsDotnet()
    {
        string[] chain = PlatformDetector.GetDefaultChain(PlatformId.Linux);

        Assert.Equal(new[] { "dotnet" }, chain);
    }

    [Fact]
    public void ResolveAdapter_ViaOverride_ReturnsSpecifiedPm()
    {
        var adapters = new Dictionary<string, IPackageManagerAdapter>
        {
            { "winget", new FakeAdapter("winget", available: true) },
            { "scoop", new FakeAdapter("scoop", available: true) },
        };

        IPackageManagerAdapter? adapter = PlatformDetector.ResolveAdapter(
            "scoop", adapters, PlatformId.Windows);

        Assert.NotNull(adapter);
        Assert.Equal("scoop", adapter!.Name);
    }

    [Fact]
    public void ResolveAdapter_ViaOverrideNotAvailable_ReturnsNull()
    {
        var adapters = new Dictionary<string, IPackageManagerAdapter>
        {
            { "scoop", new FakeAdapter("scoop", available: false) },
        };

        IPackageManagerAdapter? adapter = PlatformDetector.ResolveAdapter(
            "scoop", adapters, PlatformId.Windows);

        Assert.Null(adapter);
    }

    [Fact]
    public void ResolveAdapter_NoOverride_ReturnsFirstAvailable()
    {
        var adapters = new Dictionary<string, IPackageManagerAdapter>
        {
            { "winget", new FakeAdapter("winget", available: false) },
            { "scoop", new FakeAdapter("scoop", available: true) },
            { "dotnet", new FakeAdapter("dotnet", available: true) },
        };

        IPackageManagerAdapter? adapter = PlatformDetector.ResolveAdapter(
            null, adapters, PlatformId.Windows);

        Assert.NotNull(adapter);
        Assert.Equal("scoop", adapter!.Name);
    }

    [Fact]
    public void ResolveAdapter_NoneAvailable_ReturnsNull()
    {
        var adapters = new Dictionary<string, IPackageManagerAdapter>
        {
            { "winget", new FakeAdapter("winget", available: false) },
            { "scoop", new FakeAdapter("scoop", available: false) },
            { "dotnet", new FakeAdapter("dotnet", available: false) },
        };

        IPackageManagerAdapter? adapter = PlatformDetector.ResolveAdapter(
            null, adapters, PlatformId.Windows);

        Assert.Null(adapter);
    }

    [Fact]
    public void GetCurrentPlatform_ReturnsValidValue()
    {
        PlatformId platform = PlatformDetector.GetCurrentPlatform();

        // Must be one of the three supported platforms
        Assert.True(
            platform == PlatformId.Windows ||
            platform == PlatformId.MacOS ||
            platform == PlatformId.Linux);
    }

    /// <summary>Test double for <see cref="IPackageManagerAdapter"/>.</summary>
    private sealed class FakeAdapter : IPackageManagerAdapter
    {
        private readonly bool _available;

        public string Name { get; }

        public FakeAdapter(string name, bool available)
        {
            Name = name;
            _available = available;
        }

        public bool IsAvailable() => _available;
        public Task<bool> IsInstalled(string packageId) => Task.FromResult(false);
        public Task<string?> GetInstalledVersion(string packageId) => Task.FromResult<string?>(null);
        public Task<ProcessResult> Install(string packageId) =>
            Task.FromResult(new ProcessResult(0, "", ""));
        public Task<ProcessResult> Update(string packageId) =>
            Task.FromResult(new ProcessResult(0, "", ""));
        public Task<ProcessResult> Uninstall(string packageId) =>
            Task.FromResult(new ProcessResult(0, "", ""));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: FAIL — `IPackageManagerAdapter`, `PlatformDetector`, `PlatformId` do not exist.

- [ ] **Step 3: Implement the adapter interface**

```csharp
// src/Winix.Winix/IPackageManagerAdapter.cs
namespace Winix.Winix;

/// <summary>
/// Abstraction over a platform package manager (winget, scoop, brew, dotnet tool).
/// </summary>
public interface IPackageManagerAdapter
{
    /// <summary>Short name of this PM (e.g. "winget", "scoop", "brew", "dotnet").</summary>
    string Name { get; }

    /// <summary>Returns true if this PM's executable is on PATH.</summary>
    bool IsAvailable();

    /// <summary>Returns true if <paramref name="packageId"/> is installed via this PM.</summary>
    Task<bool> IsInstalled(string packageId);

    /// <summary>Returns the installed version of <paramref name="packageId"/>, or null if not installed.</summary>
    Task<string?> GetInstalledVersion(string packageId);

    /// <summary>Installs <paramref name="packageId"/>.</summary>
    Task<ProcessResult> Install(string packageId);

    /// <summary>Updates <paramref name="packageId"/> to the latest version.</summary>
    Task<ProcessResult> Update(string packageId);

    /// <summary>Uninstalls <paramref name="packageId"/>.</summary>
    Task<ProcessResult> Uninstall(string packageId);
}
```

- [ ] **Step 4: Implement PlatformDetector**

```csharp
// src/Winix.Winix/PlatformDetector.cs
namespace Winix.Winix;

/// <summary>Identifies the current operating system.</summary>
public enum PlatformId
{
    /// <summary>Microsoft Windows.</summary>
    Windows,
    /// <summary>Apple macOS.</summary>
    MacOS,
    /// <summary>Linux.</summary>
    Linux
}

/// <summary>
/// Detects the current platform and resolves which package manager to use
/// based on the platform default chain and user override.
/// </summary>
public static class PlatformDetector
{
    private static readonly string[] WindowsChain = { "winget", "scoop", "dotnet" };
    private static readonly string[] MacOSChain = { "brew", "dotnet" };
    private static readonly string[] LinuxChain = { "dotnet" };

    /// <summary>Returns the current operating system.</summary>
    public static PlatformId GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) { return PlatformId.Windows; }
        if (OperatingSystem.IsMacOS()) { return PlatformId.MacOS; }
        return PlatformId.Linux;
    }

    /// <summary>
    /// Returns the ordered PM preference chain for <paramref name="platform"/>.
    /// </summary>
    public static string[] GetDefaultChain(PlatformId platform)
    {
        return platform switch
        {
            PlatformId.Windows => WindowsChain,
            PlatformId.MacOS => MacOSChain,
            PlatformId.Linux => LinuxChain,
            _ => LinuxChain,
        };
    }

    /// <summary>
    /// Resolves which adapter to use. If <paramref name="viaOverride"/> is set, returns that
    /// adapter (or null if it's not available). Otherwise walks the platform default chain and
    /// returns the first available adapter, or null if none are available.
    /// </summary>
    /// <param name="viaOverride">User's <c>--via</c> value, or null for auto-detect.</param>
    /// <param name="adapters">Available adapter implementations keyed by PM name.</param>
    /// <param name="platform">Target platform.</param>
    public static IPackageManagerAdapter? ResolveAdapter(
        string? viaOverride,
        IDictionary<string, IPackageManagerAdapter> adapters,
        PlatformId platform)
    {
        if (viaOverride is not null)
        {
            if (adapters.TryGetValue(viaOverride, out IPackageManagerAdapter? overrideAdapter) &&
                overrideAdapter.IsAvailable())
            {
                return overrideAdapter;
            }
            return null;
        }

        string[] chain = GetDefaultChain(platform);
        foreach (string pmName in chain)
        {
            if (adapters.TryGetValue(pmName, out IPackageManagerAdapter? adapter) &&
                adapter.IsAvailable())
            {
                return adapter;
            }
        }

        return null;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: All tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.Winix/IPackageManagerAdapter.cs src/Winix.Winix/PlatformDetector.cs tests/Winix.Winix.Tests/PlatformDetectorTests.cs
git commit -m "feat(winix): add IPackageManagerAdapter interface and PlatformDetector"
```

---

## Task 5: Winget adapter

**Files:**
- Create: `src/Winix.Winix/WingetAdapter.cs`
- Create: `tests/Winix.Winix.Tests/WingetAdapterTests.cs`

- [ ] **Step 1: Write failing tests**

The tests use a helper approach: rather than calling the real winget, we test that the adapter constructs the correct arguments and parses known output formats. This is done by subclassing the adapter with a test seam for `ProcessHelper.RunAsync`.

```csharp
// tests/Winix.Winix.Tests/WingetAdapterTests.cs
using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class WingetAdapterTests
{
    [Fact]
    public void Name_IsWinget()
    {
        var adapter = new WingetAdapter();
        Assert.Equal("winget", adapter.Name);
    }

    [Fact]
    public async Task Install_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new WingetAdapter(recorder.RunAsync);

        await adapter.Install("Winix.TimeIt");

        Assert.Equal("winget", recorder.LastCommand);
        Assert.Equal(
            new[] { "install", "--id", "Winix.TimeIt", "--exact", "--accept-source-agreements" },
            recorder.LastArguments);
    }

    [Fact]
    public async Task Update_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new WingetAdapter(recorder.RunAsync);

        await adapter.Update("Winix.TimeIt");

        Assert.Equal("winget", recorder.LastCommand);
        Assert.Equal(
            new[] { "upgrade", "--id", "Winix.TimeIt", "--exact", "--accept-source-agreements" },
            recorder.LastArguments);
    }

    [Fact]
    public async Task Uninstall_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new WingetAdapter(recorder.RunAsync);

        await adapter.Uninstall("Winix.TimeIt");

        Assert.Equal("winget", recorder.LastCommand);
        Assert.Equal(
            new[] { "uninstall", "--id", "Winix.TimeIt", "--exact" },
            recorder.LastArguments);
    }

    [Fact]
    public async Task IsInstalled_WhenListSucceeds_ReturnsTrue()
    {
        string wingetListOutput = """
            Name   Id              Version
            ---------------------------------
            timeit Winix.TimeIt    0.2.0
            """;
        var recorder = new ProcessRecorder(new ProcessResult(0, wingetListOutput, ""));
        var adapter = new WingetAdapter(recorder.RunAsync);

        bool installed = await adapter.IsInstalled("Winix.TimeIt");

        Assert.True(installed);
    }

    [Fact]
    public async Task IsInstalled_WhenListFails_ReturnsFalse()
    {
        var recorder = new ProcessRecorder(new ProcessResult(1, "", "No installed package found"));
        var adapter = new WingetAdapter(recorder.RunAsync);

        bool installed = await adapter.IsInstalled("Winix.TimeIt");

        Assert.False(installed);
    }

    [Fact]
    public async Task GetInstalledVersion_ParsesVersionFromOutput()
    {
        string wingetListOutput = """
            Name   Id              Version
            ---------------------------------
            timeit Winix.TimeIt    0.2.0
            """;
        var recorder = new ProcessRecorder(new ProcessResult(0, wingetListOutput, ""));
        var adapter = new WingetAdapter(recorder.RunAsync);

        string? version = await adapter.GetInstalledVersion("Winix.TimeIt");

        Assert.Equal("0.2.0", version);
    }

    [Fact]
    public async Task GetInstalledVersion_NotInstalled_ReturnsNull()
    {
        var recorder = new ProcessRecorder(new ProcessResult(1, "", "No installed package found"));
        var adapter = new WingetAdapter(recorder.RunAsync);

        string? version = await adapter.GetInstalledVersion("Winix.TimeIt");

        Assert.Null(version);
    }
}

/// <summary>
/// Records the last process invocation for assertion. Optionally returns a canned result.
/// </summary>
public sealed class ProcessRecorder
{
    private readonly ProcessResult _cannedResult;

    public string? LastCommand { get; private set; }
    public string[]? LastArguments { get; private set; }

    public ProcessRecorder(ProcessResult? cannedResult = null)
    {
        _cannedResult = cannedResult ?? new ProcessResult(0, "", "");
    }

    public Task<ProcessResult> RunAsync(string command, string[] arguments)
    {
        LastCommand = command;
        LastArguments = arguments;
        return Task.FromResult(_cannedResult);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: FAIL — `WingetAdapter` does not exist.

- [ ] **Step 3: Implement WingetAdapter**

```csharp
// src/Winix.Winix/WingetAdapter.cs
namespace Winix.Winix;

/// <summary>
/// Package manager adapter for Windows Package Manager (winget).
/// </summary>
public sealed class WingetAdapter : IPackageManagerAdapter
{
    /// <summary>
    /// Delegate for running a child process. Defaults to <see cref="ProcessHelper.RunAsync"/>.
    /// Replaceable for testing.
    /// </summary>
    private readonly Func<string, string[], Task<ProcessResult>> _runAsync;

    /// <inheritdoc />
    public string Name => "winget";

    /// <summary>Creates a WingetAdapter using the real process runner.</summary>
    public WingetAdapter() : this(ProcessHelper.RunAsync) { }

    /// <summary>Creates a WingetAdapter with an injectable process runner (for testing).</summary>
    public WingetAdapter(Func<string, string[], Task<ProcessResult>> runAsync)
    {
        _runAsync = runAsync;
    }

    /// <inheritdoc />
    public bool IsAvailable() => ProcessHelper.IsOnPath("winget");

    /// <inheritdoc />
    public async Task<bool> IsInstalled(string packageId)
    {
        ProcessResult result = await _runAsync("winget",
            new[] { "list", "--id", packageId, "--exact" }).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    /// <inheritdoc />
    public async Task<string?> GetInstalledVersion(string packageId)
    {
        ProcessResult result = await _runAsync("winget",
            new[] { "list", "--id", packageId, "--exact" }).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        return ParseVersionFromListOutput(result.Stdout, packageId);
    }

    /// <inheritdoc />
    public Task<ProcessResult> Install(string packageId)
    {
        return _runAsync("winget",
            new[] { "install", "--id", packageId, "--exact", "--accept-source-agreements" });
    }

    /// <inheritdoc />
    public Task<ProcessResult> Update(string packageId)
    {
        return _runAsync("winget",
            new[] { "upgrade", "--id", packageId, "--exact", "--accept-source-agreements" });
    }

    /// <inheritdoc />
    public Task<ProcessResult> Uninstall(string packageId)
    {
        return _runAsync("winget",
            new[] { "uninstall", "--id", packageId, "--exact" });
    }

    /// <summary>
    /// Parses the version from <c>winget list --id X --exact</c> output.
    /// The output has a header row, a separator, then data rows. The version
    /// is the last non-empty column on the data row containing the package ID.
    /// </summary>
    internal static string? ParseVersionFromListOutput(string stdout, string packageId)
    {
        // winget list output:
        //   Name   Id              Version
        //   ---------------------------------
        //   timeit Winix.TimeIt    0.2.0
        string[] lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Contains(packageId, StringComparison.OrdinalIgnoreCase))
            {
                // Version is the last whitespace-separated token
                string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    return parts[^1];
                }
            }
        }

        return null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/WingetAdapter.cs tests/Winix.Winix.Tests/WingetAdapterTests.cs
git commit -m "feat(winix): add WingetAdapter for winget package manager"
```

---

## Task 6: Scoop adapter

**Files:**
- Create: `src/Winix.Winix/ScoopAdapter.cs`
- Create: `tests/Winix.Winix.Tests/ScoopAdapterTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Winix.Winix.Tests/ScoopAdapterTests.cs
using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class ScoopAdapterTests
{
    [Fact]
    public void Name_IsScoop()
    {
        var adapter = new ScoopAdapter();
        Assert.Equal("scoop", adapter.Name);
    }

    [Fact]
    public async Task Install_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new ScoopAdapter(recorder.RunAsync);

        await adapter.Install("timeit");

        Assert.Equal("scoop", recorder.LastCommand);
        Assert.Equal(new[] { "install", "timeit" }, recorder.LastArguments);
    }

    [Fact]
    public async Task Update_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new ScoopAdapter(recorder.RunAsync);

        await adapter.Update("timeit");

        Assert.Equal("scoop", recorder.LastCommand);
        Assert.Equal(new[] { "update", "timeit" }, recorder.LastArguments);
    }

    [Fact]
    public async Task Uninstall_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new ScoopAdapter(recorder.RunAsync);

        await adapter.Uninstall("timeit");

        Assert.Equal("scoop", recorder.LastCommand);
        Assert.Equal(new[] { "uninstall", "timeit" }, recorder.LastArguments);
    }

    [Fact]
    public async Task IsInstalled_WhenListSucceeds_ReturnsTrue()
    {
        // scoop list outputs installed apps; exit 0 if found
        string scoopListOutput = """
            Installed apps:

              Name   Version Source
              ----   ------- ------
              timeit 0.2.0   winix
            """;
        var recorder = new ProcessRecorder(new ProcessResult(0, scoopListOutput, ""));
        var adapter = new ScoopAdapter(recorder.RunAsync);

        bool installed = await adapter.IsInstalled("timeit");

        Assert.True(installed);
    }

    [Fact]
    public async Task IsInstalled_WhenListFails_ReturnsFalse()
    {
        var recorder = new ProcessRecorder(new ProcessResult(1, "", "'timeit' isn't installed."));
        var adapter = new ScoopAdapter(recorder.RunAsync);

        bool installed = await adapter.IsInstalled("timeit");

        Assert.False(installed);
    }

    [Fact]
    public async Task GetInstalledVersion_ParsesVersion()
    {
        string scoopListOutput = """
            Installed apps:

              Name   Version Source
              ----   ------- ------
              timeit 0.2.0   winix
            """;
        var recorder = new ProcessRecorder(new ProcessResult(0, scoopListOutput, ""));
        var adapter = new ScoopAdapter(recorder.RunAsync);

        string? version = await adapter.GetInstalledVersion("timeit");

        Assert.Equal("0.2.0", version);
    }

    [Fact]
    public async Task EnsureBucket_WhenBucketMissing_AddsBucket()
    {
        var calls = new List<(string Command, string[] Args)>();
        int callCount = 0;
        Task<ProcessResult> FakeRun(string command, string[] args)
        {
            calls.Add((command, args));
            callCount++;
            // First call: bucket list (no winix bucket)
            if (callCount == 1)
            {
                return Task.FromResult(new ProcessResult(0, "main\nextras", ""));
            }
            // Second call: bucket add
            return Task.FromResult(new ProcessResult(0, "", ""));
        }

        var adapter = new ScoopAdapter(FakeRun);
        await adapter.EnsureBucket();

        Assert.Equal(2, calls.Count);
        Assert.Equal(new[] { "bucket", "list" }, calls[0].Args);
        Assert.Equal("bucket", calls[1].Args[0]);
        Assert.Equal("add", calls[1].Args[1]);
        Assert.Equal("winix", calls[1].Args[2]);
    }

    [Fact]
    public async Task EnsureBucket_WhenBucketExists_DoesNothing()
    {
        var calls = new List<(string Command, string[] Args)>();
        Task<ProcessResult> FakeRun(string command, string[] args)
        {
            calls.Add((command, args));
            return Task.FromResult(new ProcessResult(0, "main\nextras\nwinix", ""));
        }

        var adapter = new ScoopAdapter(FakeRun);
        await adapter.EnsureBucket();

        Assert.Single(calls); // Only the bucket list call, no add
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: FAIL — `ScoopAdapter` does not exist.

- [ ] **Step 3: Implement ScoopAdapter**

```csharp
// src/Winix.Winix/ScoopAdapter.cs
namespace Winix.Winix;

/// <summary>
/// Package manager adapter for Scoop (Windows).
/// </summary>
public sealed class ScoopAdapter : IPackageManagerAdapter
{
    private readonly Func<string, string[], Task<ProcessResult>> _runAsync;
    private const string BucketName = "winix";
    private const string BucketUrl = "https://github.com/Yortw/winix";

    /// <inheritdoc />
    public string Name => "scoop";

    /// <summary>Creates a ScoopAdapter using the real process runner.</summary>
    public ScoopAdapter() : this(ProcessHelper.RunAsync) { }

    /// <summary>Creates a ScoopAdapter with an injectable process runner (for testing).</summary>
    public ScoopAdapter(Func<string, string[], Task<ProcessResult>> runAsync)
    {
        _runAsync = runAsync;
    }

    /// <inheritdoc />
    public bool IsAvailable() => ProcessHelper.IsOnPath("scoop");

    /// <inheritdoc />
    public async Task<bool> IsInstalled(string packageId)
    {
        ProcessResult result = await _runAsync("scoop",
            new[] { "list", packageId }).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    /// <inheritdoc />
    public async Task<string?> GetInstalledVersion(string packageId)
    {
        ProcessResult result = await _runAsync("scoop",
            new[] { "list", packageId }).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        return ParseVersionFromListOutput(result.Stdout, packageId);
    }

    /// <inheritdoc />
    public Task<ProcessResult> Install(string packageId)
    {
        return _runAsync("scoop", new[] { "install", packageId });
    }

    /// <inheritdoc />
    public Task<ProcessResult> Update(string packageId)
    {
        return _runAsync("scoop", new[] { "update", packageId });
    }

    /// <inheritdoc />
    public Task<ProcessResult> Uninstall(string packageId)
    {
        return _runAsync("scoop", new[] { "uninstall", packageId });
    }

    /// <summary>
    /// Ensures the Winix scoop bucket is added. If not present, adds it automatically.
    /// </summary>
    public async Task EnsureBucket()
    {
        ProcessResult listResult = await _runAsync("scoop",
            new[] { "bucket", "list" }).ConfigureAwait(false);

        // Check if "winix" appears in the output
        string[] buckets = listResult.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (string bucket in buckets)
        {
            if (bucket.Trim().Equals(BucketName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await _runAsync("scoop",
            new[] { "bucket", "add", BucketName, BucketUrl }).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses the version from <c>scoop list</c> output for a specific package.
    /// Format: <c>  Name   Version Source</c> with dashed separator.
    /// </summary>
    internal static string? ParseVersionFromListOutput(string stdout, string packageId)
    {
        string[] lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(packageId, StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    return parts[1];
                }
            }
        }

        return null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/ScoopAdapter.cs tests/Winix.Winix.Tests/ScoopAdapterTests.cs
git commit -m "feat(winix): add ScoopAdapter with auto-bucket detection"
```

---

## Task 7: Brew adapter

**Files:**
- Create: `src/Winix.Winix/BrewAdapter.cs`
- Create: `tests/Winix.Winix.Tests/BrewAdapterTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Winix.Winix.Tests/BrewAdapterTests.cs
using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class BrewAdapterTests
{
    [Fact]
    public void Name_IsBrew()
    {
        var adapter = new BrewAdapter();
        Assert.Equal("brew", adapter.Name);
    }

    [Fact]
    public async Task Install_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new BrewAdapter(recorder.RunAsync);

        await adapter.Install("timeit");

        Assert.Equal("brew", recorder.LastCommand);
        Assert.Equal(new[] { "install", "yortw/winix/timeit" }, recorder.LastArguments);
    }

    [Fact]
    public async Task Update_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new BrewAdapter(recorder.RunAsync);

        await adapter.Update("timeit");

        Assert.Equal("brew", recorder.LastCommand);
        Assert.Equal(new[] { "upgrade", "yortw/winix/timeit" }, recorder.LastArguments);
    }

    [Fact]
    public async Task Uninstall_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new BrewAdapter(recorder.RunAsync);

        await adapter.Uninstall("timeit");

        Assert.Equal("brew", recorder.LastCommand);
        Assert.Equal(new[] { "uninstall", "timeit" }, recorder.LastArguments);
    }

    [Fact]
    public async Task IsInstalled_WhenListSucceeds_ReturnsTrue()
    {
        var recorder = new ProcessRecorder(new ProcessResult(0, "/opt/homebrew/Cellar/timeit/0.2.0", ""));
        var adapter = new BrewAdapter(recorder.RunAsync);

        bool installed = await adapter.IsInstalled("timeit");

        Assert.True(installed);
    }

    [Fact]
    public async Task IsInstalled_WhenListFails_ReturnsFalse()
    {
        var recorder = new ProcessRecorder(new ProcessResult(1, "", "Error: No such keg"));
        var adapter = new BrewAdapter(recorder.RunAsync);

        bool installed = await adapter.IsInstalled("timeit");

        Assert.False(installed);
    }

    [Fact]
    public async Task GetInstalledVersion_ParsesVersion()
    {
        var recorder = new ProcessRecorder(new ProcessResult(0, "0.2.0", ""));
        var adapter = new BrewAdapter(recorder.RunAsync);

        string? version = await adapter.GetInstalledVersion("timeit");

        Assert.Equal("0.2.0", version);
    }

    [Fact]
    public async Task EnsureTap_WhenTapMissing_AddsTap()
    {
        var calls = new List<(string Command, string[] Args)>();
        int callCount = 0;
        Task<ProcessResult> FakeRun(string command, string[] args)
        {
            calls.Add((command, args));
            callCount++;
            if (callCount == 1)
            {
                return Task.FromResult(new ProcessResult(0, "homebrew/core\nhomebrew/cask", ""));
            }
            return Task.FromResult(new ProcessResult(0, "", ""));
        }

        var adapter = new BrewAdapter(FakeRun);
        await adapter.EnsureTap();

        Assert.Equal(2, calls.Count);
        Assert.Equal(new[] { "tap" }, calls[0].Args);
        Assert.Equal(new[] { "tap", "yortw/winix" }, calls[1].Args);
    }

    [Fact]
    public async Task EnsureTap_WhenTapExists_DoesNothing()
    {
        var calls = new List<(string Command, string[] Args)>();
        Task<ProcessResult> FakeRun(string command, string[] args)
        {
            calls.Add((command, args));
            return Task.FromResult(new ProcessResult(0, "homebrew/core\nyortw/winix", ""));
        }

        var adapter = new BrewAdapter(FakeRun);
        await adapter.EnsureTap();

        Assert.Single(calls);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: FAIL — `BrewAdapter` does not exist.

- [ ] **Step 3: Implement BrewAdapter**

```csharp
// src/Winix.Winix/BrewAdapter.cs
namespace Winix.Winix;

/// <summary>
/// Package manager adapter for Homebrew (macOS, Linux).
/// </summary>
public sealed class BrewAdapter : IPackageManagerAdapter
{
    private readonly Func<string, string[], Task<ProcessResult>> _runAsync;
    private const string TapName = "yortw/winix";

    /// <inheritdoc />
    public string Name => "brew";

    /// <summary>Creates a BrewAdapter using the real process runner.</summary>
    public BrewAdapter() : this(ProcessHelper.RunAsync) { }

    /// <summary>Creates a BrewAdapter with an injectable process runner (for testing).</summary>
    public BrewAdapter(Func<string, string[], Task<ProcessResult>> runAsync)
    {
        _runAsync = runAsync;
    }

    /// <inheritdoc />
    public bool IsAvailable() => ProcessHelper.IsOnPath("brew");

    /// <inheritdoc />
    public async Task<bool> IsInstalled(string packageId)
    {
        ProcessResult result = await _runAsync("brew",
            new[] { "list", packageId }).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    /// <inheritdoc />
    public async Task<string?> GetInstalledVersion(string packageId)
    {
        // "brew list --versions <pkg>" outputs: "<pkg> 0.2.0" or nothing
        ProcessResult result = await _runAsync("brew",
            new[] { "list", "--versions", packageId }).ConfigureAwait(false);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            return null;
        }

        // Output format: "timeit 0.2.0"
        string[] parts = result.Stdout.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^1] : result.Stdout.Trim();
    }

    /// <inheritdoc />
    public Task<ProcessResult> Install(string packageId)
    {
        // Use fully qualified tap/package name for install
        return _runAsync("brew", new[] { "install", $"{TapName}/{packageId}" });
    }

    /// <inheritdoc />
    public Task<ProcessResult> Update(string packageId)
    {
        return _runAsync("brew", new[] { "upgrade", $"{TapName}/{packageId}" });
    }

    /// <inheritdoc />
    public Task<ProcessResult> Uninstall(string packageId)
    {
        return _runAsync("brew", new[] { "uninstall", packageId });
    }

    /// <summary>
    /// Ensures the Winix Homebrew tap is registered. If not present, adds it automatically.
    /// </summary>
    public async Task EnsureTap()
    {
        ProcessResult listResult = await _runAsync("brew",
            new[] { "tap" }).ConfigureAwait(false);

        string[] taps = listResult.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (string tap in taps)
        {
            if (tap.Trim().Equals(TapName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await _runAsync("brew", new[] { "tap", TapName }).ConfigureAwait(false);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/BrewAdapter.cs tests/Winix.Winix.Tests/BrewAdapterTests.cs
git commit -m "feat(winix): add BrewAdapter with auto-tap detection"
```

---

## Task 8: Dotnet tool adapter

**Files:**
- Create: `src/Winix.Winix/DotnetToolAdapter.cs`
- Create: `tests/Winix.Winix.Tests/DotnetToolAdapterTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Winix.Winix.Tests/DotnetToolAdapterTests.cs
using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class DotnetToolAdapterTests
{
    [Fact]
    public void Name_IsDotnet()
    {
        var adapter = new DotnetToolAdapter();
        Assert.Equal("dotnet", adapter.Name);
    }

    [Fact]
    public async Task Install_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        await adapter.Install("Winix.TimeIt");

        Assert.Equal("dotnet", recorder.LastCommand);
        Assert.Equal(new[] { "tool", "install", "-g", "Winix.TimeIt" }, recorder.LastArguments);
    }

    [Fact]
    public async Task Update_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        await adapter.Update("Winix.TimeIt");

        Assert.Equal("dotnet", recorder.LastCommand);
        Assert.Equal(new[] { "tool", "update", "-g", "Winix.TimeIt" }, recorder.LastArguments);
    }

    [Fact]
    public async Task Uninstall_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        await adapter.Uninstall("Winix.TimeIt");

        Assert.Equal("dotnet", recorder.LastCommand);
        Assert.Equal(new[] { "tool", "uninstall", "-g", "Winix.TimeIt" }, recorder.LastArguments);
    }

    [Fact]
    public async Task IsInstalled_WhenToolInList_ReturnsTrue()
    {
        string toolListOutput = """
            Package Id      Version      Commands
            -------------------------------------------
            winix.timeit    0.2.0        timeit
            winix.squeeze   0.2.0        squeeze
            """;
        var recorder = new ProcessRecorder(new ProcessResult(0, toolListOutput, ""));
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        bool installed = await adapter.IsInstalled("Winix.TimeIt");

        Assert.True(installed);
    }

    [Fact]
    public async Task IsInstalled_WhenToolNotInList_ReturnsFalse()
    {
        string toolListOutput = """
            Package Id      Version      Commands
            -------------------------------------------
            winix.squeeze   0.2.0        squeeze
            """;
        var recorder = new ProcessRecorder(new ProcessResult(0, toolListOutput, ""));
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        bool installed = await adapter.IsInstalled("Winix.TimeIt");

        Assert.False(installed);
    }

    [Fact]
    public async Task GetInstalledVersion_ParsesVersion()
    {
        string toolListOutput = """
            Package Id      Version      Commands
            -------------------------------------------
            winix.timeit    0.2.0        timeit
            """;
        var recorder = new ProcessRecorder(new ProcessResult(0, toolListOutput, ""));
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        string? version = await adapter.GetInstalledVersion("Winix.TimeIt");

        Assert.Equal("0.2.0", version);
    }

    [Fact]
    public async Task GetInstalledVersion_NotInstalled_ReturnsNull()
    {
        string toolListOutput = """
            Package Id      Version      Commands
            -------------------------------------------
            winix.squeeze   0.2.0        squeeze
            """;
        var recorder = new ProcessRecorder(new ProcessResult(0, toolListOutput, ""));
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        string? version = await adapter.GetInstalledVersion("Winix.TimeIt");

        Assert.Null(version);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: FAIL — `DotnetToolAdapter` does not exist.

- [ ] **Step 3: Implement DotnetToolAdapter**

```csharp
// src/Winix.Winix/DotnetToolAdapter.cs
namespace Winix.Winix;

/// <summary>
/// Package manager adapter for .NET global tools (<c>dotnet tool install -g</c>).
/// </summary>
public sealed class DotnetToolAdapter : IPackageManagerAdapter
{
    private readonly Func<string, string[], Task<ProcessResult>> _runAsync;

    /// <inheritdoc />
    public string Name => "dotnet";

    /// <summary>Creates a DotnetToolAdapter using the real process runner.</summary>
    public DotnetToolAdapter() : this(ProcessHelper.RunAsync) { }

    /// <summary>Creates a DotnetToolAdapter with an injectable process runner (for testing).</summary>
    public DotnetToolAdapter(Func<string, string[], Task<ProcessResult>> runAsync)
    {
        _runAsync = runAsync;
    }

    /// <inheritdoc />
    public bool IsAvailable() => ProcessHelper.IsOnPath("dotnet");

    /// <inheritdoc />
    public async Task<bool> IsInstalled(string packageId)
    {
        string? version = await GetInstalledVersion(packageId).ConfigureAwait(false);
        return version is not null;
    }

    /// <inheritdoc />
    public async Task<string?> GetInstalledVersion(string packageId)
    {
        ProcessResult result = await _runAsync("dotnet",
            new[] { "tool", "list", "-g" }).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        return ParseVersionFromToolList(result.Stdout, packageId);
    }

    /// <inheritdoc />
    public Task<ProcessResult> Install(string packageId)
    {
        return _runAsync("dotnet", new[] { "tool", "install", "-g", packageId });
    }

    /// <inheritdoc />
    public Task<ProcessResult> Update(string packageId)
    {
        return _runAsync("dotnet", new[] { "tool", "update", "-g", packageId });
    }

    /// <inheritdoc />
    public Task<ProcessResult> Uninstall(string packageId)
    {
        return _runAsync("dotnet", new[] { "tool", "uninstall", "-g", packageId });
    }

    /// <summary>
    /// Parses the version from <c>dotnet tool list -g</c> output.
    /// Format:
    /// <code>
    /// Package Id      Version      Commands
    /// -------------------------------------------
    /// winix.timeit    0.2.0        timeit
    /// </code>
    /// Package IDs in the output are lowercase.
    /// </summary>
    internal static string? ParseVersionFromToolList(string stdout, string packageId)
    {
        string[] lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                parts[0].Equals(packageId, StringComparison.OrdinalIgnoreCase))
            {
                return parts[1];
            }
        }

        return null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/DotnetToolAdapter.cs tests/Winix.Winix.Tests/DotnetToolAdapterTests.cs
git commit -m "feat(winix): add DotnetToolAdapter for dotnet global tools"
```

---

## Task 9: Output formatting

**Files:**
- Create: `src/Winix.Winix/Formatting.cs`
- Create: `tests/Winix.Winix.Tests/FormattingTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Winix.Winix.Tests/FormattingTests.cs
using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class FormattingTests
{
    [Fact]
    public void FormatToolResult_Success_ShowsCheckmark()
    {
        string result = Formatting.FormatToolResult("timeit", "winget", success: true, error: null, useColor: false);

        Assert.Contains("timeit", result);
        Assert.Contains("winget", result);
        Assert.Contains("\u2713", result); // ✓
    }

    [Fact]
    public void FormatToolResult_Failure_ShowsCross()
    {
        string result = Formatting.FormatToolResult("timeit", "winget", success: false, error: "exit code 1", useColor: false);

        Assert.Contains("timeit", result);
        Assert.Contains("\u2717", result); // ✗
        Assert.Contains("exit code 1", result);
    }

    [Fact]
    public void FormatToolResult_Success_WithColor_ContainsGreenEscape()
    {
        string result = Formatting.FormatToolResult("timeit", "winget", success: true, error: null, useColor: true);

        Assert.Contains("\x1b[32m", result); // green
        Assert.Contains("\x1b[0m", result);  // reset
    }

    [Fact]
    public void FormatToolResult_Failure_WithColor_ContainsRedEscape()
    {
        string result = Formatting.FormatToolResult("timeit", "winget", success: false, error: "fail", useColor: true);

        Assert.Contains("\x1b[31m", result); // red
    }

    [Fact]
    public void FormatStatusSummary_AllInstalled()
    {
        var statuses = new[]
        {
            new ToolStatus("timeit", true, "0.2.0", "winget"),
            new ToolStatus("squeeze", true, "0.2.0", "winget"),
        };

        string summary = Formatting.FormatStatusSummary(statuses, 2);

        Assert.Contains("2 of 2", summary);
        Assert.Contains("winget", summary);
    }

    [Fact]
    public void FormatStatusSummary_PartialInstall_ShowsMixed()
    {
        var statuses = new[]
        {
            new ToolStatus("timeit", true, "0.2.0", "winget"),
            new ToolStatus("squeeze", true, "0.2.0", "dotnet"),
            new ToolStatus("peep", false, null, null),
        };

        string summary = Formatting.FormatStatusSummary(statuses, 3);

        Assert.Contains("2 of 3", summary);
    }

    [Fact]
    public void FormatStatusSummary_NoneInstalled()
    {
        var statuses = new[]
        {
            new ToolStatus("timeit", false, null, null),
        };

        string summary = Formatting.FormatStatusSummary(statuses, 1);

        Assert.Contains("0 of 1", summary);
    }

    [Fact]
    public void FormatListTable_ShowsToolInfo()
    {
        var statuses = new[]
        {
            new ToolStatus("timeit", true, "0.2.0", "winget"),
            new ToolStatus("squeeze", false, null, null),
        };
        var descriptions = new Dictionary<string, string>
        {
            { "timeit", "Time a command." },
            { "squeeze", "Compress files." },
        };

        string table = Formatting.FormatListTable(statuses, descriptions, useColor: false);

        Assert.Contains("timeit", table);
        Assert.Contains("0.2.0", table);
        Assert.Contains("winget", table);
        Assert.Contains("squeeze", table);
    }

    [Fact]
    public void FormatDryRun_ShowsCommandThatWouldRun()
    {
        string line = Formatting.FormatDryRun("winget", new[] { "install", "--id", "Winix.TimeIt", "--exact" });

        Assert.Contains("winget", line);
        Assert.Contains("install", line);
        Assert.Contains("Winix.TimeIt", line);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: FAIL — `Formatting`, `ToolStatus` do not exist.

- [ ] **Step 3: Implement Formatting and ToolStatus**

```csharp
// src/Winix.Winix/Formatting.cs
using System.Text;
using Yort.ShellKit;

namespace Winix.Winix;

/// <summary>
/// Status of a single tool as discovered by probing package managers.
/// </summary>
public sealed class ToolStatus
{
    /// <summary>Tool short name (e.g. "timeit").</summary>
    public string Name { get; }

    /// <summary>Whether the tool is currently installed.</summary>
    public bool IsInstalled { get; }

    /// <summary>Installed version, or null if not installed.</summary>
    public string? Version { get; }

    /// <summary>Name of the PM that owns this tool, or null if not installed.</summary>
    public string? PackageManager { get; }

    /// <summary>Creates a new tool status.</summary>
    public ToolStatus(string name, bool isInstalled, string? version, string? packageManager)
    {
        Name = name;
        IsInstalled = isInstalled;
        Version = version;
        PackageManager = packageManager;
    }
}

/// <summary>Output formatting for the winix installer tool.</summary>
public static class Formatting
{
    /// <summary>
    /// Formats a single tool install/update/uninstall result line.
    /// </summary>
    public static string FormatToolResult(string toolName, string pmName, bool success, string? error, bool useColor)
    {
        if (success)
        {
            return $"{AnsiColor.Green(useColor)}\u2713{AnsiColor.Reset(useColor)} {toolName} (via {pmName})";
        }
        else
        {
            string errorSuffix = error is not null ? $" \u2014 {error}" : "";
            return $"{AnsiColor.Red(useColor)}\u2717{AnsiColor.Reset(useColor)} {toolName} (via {pmName}){errorSuffix}";
        }
    }

    /// <summary>
    /// Formats the status summary line (e.g. "4 of 6 tools installed (3 via winget, 1 via dotnet)").
    /// </summary>
    public static string FormatStatusSummary(IReadOnlyList<ToolStatus> statuses, int totalTools)
    {
        int installed = 0;
        var pmCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (ToolStatus status in statuses)
        {
            if (status.IsInstalled)
            {
                installed++;
                if (status.PackageManager is not null)
                {
                    if (!pmCounts.TryGetValue(status.PackageManager, out int count))
                    {
                        count = 0;
                    }
                    pmCounts[status.PackageManager] = count + 1;
                }
            }
        }

        var sb = new StringBuilder();
        sb.Append($"{installed} of {totalTools} tools installed");

        if (pmCounts.Count > 0)
        {
            sb.Append(" (");
            bool first = true;
            foreach (var (pm, count) in pmCounts.OrderByDescending(kv => kv.Value))
            {
                if (!first) { sb.Append(", "); }
                sb.Append($"{count} via {pm}");
                first = false;
            }
            sb.Append(')');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats the list table showing all tools, their install status, version, and PM.
    /// </summary>
    public static string FormatListTable(
        IReadOnlyList<ToolStatus> statuses,
        IReadOnlyDictionary<string, string> descriptions,
        bool useColor)
    {
        var sb = new StringBuilder();

        // Calculate column widths
        int nameWidth = 10;
        int descWidth = 12;
        int versionWidth = 7;
        int pmWidth = 3;

        foreach (ToolStatus status in statuses)
        {
            if (status.Name.Length > nameWidth) { nameWidth = status.Name.Length; }
            if (descriptions.TryGetValue(status.Name, out string? desc) && desc.Length > descWidth)
            {
                descWidth = Math.Min(desc.Length, 50);
            }
            if (status.Version is not null && status.Version.Length > versionWidth)
            {
                versionWidth = status.Version.Length;
            }
            if (status.PackageManager is not null && status.PackageManager.Length > pmWidth)
            {
                pmWidth = status.PackageManager.Length;
            }
        }

        // Header
        string header = string.Format(
            $"{{0,-{nameWidth}}}  {{1,-{descWidth}}}  {{2,-9}}  {{3,-{versionWidth}}}  {{4}}",
            "Tool", "Description", "Installed", "Version", "Via");
        sb.AppendLine(header);
        sb.AppendLine(new string('-', header.Length));

        foreach (ToolStatus status in statuses)
        {
            string desc = descriptions.TryGetValue(status.Name, out string? d) ? d : "";
            if (desc.Length > 50) { desc = desc[..47] + "..."; }

            string installedStr = status.IsInstalled ? "yes" : "no";
            string versionStr = status.Version ?? "-";
            string pmStr = status.PackageManager ?? "-";

            string line = string.Format(
                $"{{0,-{nameWidth}}}  {{1,-{descWidth}}}  {{2,-9}}  {{3,-{versionWidth}}}  {{4}}",
                status.Name, desc, installedStr, versionStr, pmStr);
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats a dry-run line showing the command that would be executed.
    /// </summary>
    public static string FormatDryRun(string command, string[] arguments)
    {
        return $"[dry-run] {command} {string.Join(' ', arguments)}";
    }

    /// <summary>
    /// Formats the hint message shown when no tools are installed.
    /// </summary>
    public static string FormatNoToolsHint()
    {
        return "No Winix tools installed. Run 'winix install' to install all tools.";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/Formatting.cs tests/Winix.Winix.Tests/FormattingTests.cs
git commit -m "feat(winix): add output formatting for tool results, list table, and status summary"
```

---

## Task 10: SuiteManager orchestration

**Files:**
- Create: `src/Winix.Winix/SuiteManager.cs`
- Create: `tests/Winix.Winix.Tests/SuiteManagerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Winix.Winix.Tests/SuiteManagerTests.cs
using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class SuiteManagerTests
{
    private static ToolManifest CreateTestManifest()
    {
        string json = """
            {
              "version": "0.2.0",
              "tools": {
                "timeit": {
                  "description": "Time a command.",
                  "packages": { "winget": "Winix.TimeIt", "scoop": "timeit", "dotnet": "Winix.TimeIt" }
                },
                "squeeze": {
                  "description": "Compress files.",
                  "packages": { "winget": "Winix.Squeeze", "scoop": "squeeze", "dotnet": "Winix.Squeeze" }
                }
              }
            }
            """;
        return ToolManifest.Parse(json);
    }

    [Fact]
    public async Task InstallAll_AllSucceed_ReturnsZero()
    {
        var adapter = new FullFakeAdapter("winget", available: true, installExitCode: 0);
        var manager = new SuiteManager(CreateTestManifest(), adapter);
        var results = new List<string>();

        int exitCode = await manager.InstallAsync(
            toolNames: null, dryRun: false, useColor: false,
            output: line => results.Add(line));

        Assert.Equal(0, exitCode);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains("\u2713", r)); // ✓
    }

    [Fact]
    public async Task InstallAll_OneFails_ReturnsOne()
    {
        int callCount = 0;
        var adapter = new FullFakeAdapter("winget", available: true, installExitCode: 0,
            installOverride: packageId =>
            {
                callCount++;
                // Fail on the second tool
                int code = callCount == 2 ? 1 : 0;
                return Task.FromResult(new ProcessResult(code, "", code == 1 ? "error" : ""));
            });
        var manager = new SuiteManager(CreateTestManifest(), adapter);
        var results = new List<string>();

        int exitCode = await manager.InstallAsync(
            toolNames: null, dryRun: false, useColor: false,
            output: line => results.Add(line));

        Assert.Equal(1, exitCode);
        Assert.Contains(results, r => r.Contains("\u2713")); // ✓
        Assert.Contains(results, r => r.Contains("\u2717")); // ✗
    }

    [Fact]
    public async Task InstallSpecificTools_OnlyInstallsNamed()
    {
        var adapter = new FullFakeAdapter("winget", available: true, installExitCode: 0);
        var manager = new SuiteManager(CreateTestManifest(), adapter);
        var results = new List<string>();

        int exitCode = await manager.InstallAsync(
            toolNames: new[] { "timeit" }, dryRun: false, useColor: false,
            output: line => results.Add(line));

        Assert.Equal(0, exitCode);
        Assert.Single(results);
        Assert.Contains("timeit", results[0]);
    }

    [Fact]
    public async Task InstallDryRun_DoesNotCallAdapter()
    {
        var adapter = new FullFakeAdapter("winget", available: true, installExitCode: 0);
        var manager = new SuiteManager(CreateTestManifest(), adapter);
        var results = new List<string>();

        int exitCode = await manager.InstallAsync(
            toolNames: null, dryRun: true, useColor: false,
            output: line => results.Add(line));

        Assert.Equal(0, exitCode);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains("[dry-run]", r));
        Assert.Equal(0, adapter.InstallCallCount);
    }

    [Fact]
    public async Task ListAsync_ShowsAllTools()
    {
        var adapter = new FullFakeAdapter("winget", available: true, installExitCode: 0,
            installedPackages: new HashSet<string> { "Winix.TimeIt" },
            versions: new Dictionary<string, string> { { "Winix.TimeIt", "0.2.0" } });
        var adapters = new Dictionary<string, IPackageManagerAdapter> { { "winget", adapter } };
        var manager = new SuiteManager(CreateTestManifest(), adapters, PlatformId.Windows);

        var statuses = await manager.ListAsync();

        Assert.Equal(2, statuses.Count);

        ToolStatus timeit = statuses.First(s => s.Name == "timeit");
        Assert.True(timeit.IsInstalled);
        Assert.Equal("0.2.0", timeit.Version);
        Assert.Equal("winget", timeit.PackageManager);

        ToolStatus squeeze = statuses.First(s => s.Name == "squeeze");
        Assert.False(squeeze.IsInstalled);
    }

    [Fact]
    public async Task UninstallAll_AllSucceed_ReturnsZero()
    {
        var adapter = new FullFakeAdapter("winget", available: true, installExitCode: 0,
            installedPackages: new HashSet<string> { "Winix.TimeIt", "Winix.Squeeze" });
        var adapters = new Dictionary<string, IPackageManagerAdapter> { { "winget", adapter } };
        var manager = new SuiteManager(CreateTestManifest(), adapters, PlatformId.Windows);
        var results = new List<string>();

        int exitCode = await manager.UninstallAsync(
            toolNames: null, dryRun: false, useColor: false,
            output: line => results.Add(line));

        Assert.Equal(0, exitCode);
    }

    /// <summary>Full test double for integration-level SuiteManager tests.</summary>
    private sealed class FullFakeAdapter : IPackageManagerAdapter
    {
        private readonly bool _available;
        private readonly int _installExitCode;
        private readonly Func<string, Task<ProcessResult>>? _installOverride;
        private readonly HashSet<string> _installedPackages;
        private readonly Dictionary<string, string> _versions;

        public string Name { get; }
        public int InstallCallCount { get; private set; }

        public FullFakeAdapter(
            string name,
            bool available,
            int installExitCode,
            Func<string, Task<ProcessResult>>? installOverride = null,
            HashSet<string>? installedPackages = null,
            Dictionary<string, string>? versions = null)
        {
            Name = name;
            _available = available;
            _installExitCode = installExitCode;
            _installOverride = installOverride;
            _installedPackages = installedPackages ?? new HashSet<string>();
            _versions = versions ?? new Dictionary<string, string>();
        }

        public bool IsAvailable() => _available;

        public Task<bool> IsInstalled(string packageId)
        {
            return Task.FromResult(_installedPackages.Contains(packageId));
        }

        public Task<string?> GetInstalledVersion(string packageId)
        {
            _versions.TryGetValue(packageId, out string? version);
            return Task.FromResult(version);
        }

        public Task<ProcessResult> Install(string packageId)
        {
            InstallCallCount++;
            if (_installOverride is not null)
            {
                return _installOverride(packageId);
            }
            return Task.FromResult(new ProcessResult(_installExitCode, "", ""));
        }

        public Task<ProcessResult> Update(string packageId)
        {
            return Task.FromResult(new ProcessResult(_installExitCode, "", ""));
        }

        public Task<ProcessResult> Uninstall(string packageId)
        {
            return Task.FromResult(new ProcessResult(0, "", ""));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: FAIL — `SuiteManager` does not exist.

- [ ] **Step 3: Implement SuiteManager**

```csharp
// src/Winix.Winix/SuiteManager.cs
namespace Winix.Winix;

/// <summary>
/// Orchestrates install/update/uninstall/list/status operations across all Winix tools
/// by delegating to a selected <see cref="IPackageManagerAdapter"/>.
/// </summary>
public sealed class SuiteManager
{
    private readonly ToolManifest _manifest;
    private readonly IDictionary<string, IPackageManagerAdapter> _adapters;
    private readonly PlatformId _platform;

    /// <summary>
    /// Creates a SuiteManager with a single pre-selected adapter (for install/update when PM is already resolved).
    /// </summary>
    public SuiteManager(ToolManifest manifest, IPackageManagerAdapter adapter)
    {
        _manifest = manifest;
        _adapters = new Dictionary<string, IPackageManagerAdapter> { { adapter.Name, adapter } };
        _platform = PlatformDetector.GetCurrentPlatform();
    }

    /// <summary>
    /// Creates a SuiteManager with all available adapters (for list/status which probe all PMs).
    /// </summary>
    public SuiteManager(ToolManifest manifest, IDictionary<string, IPackageManagerAdapter> adapters, PlatformId platform)
    {
        _manifest = manifest;
        _adapters = adapters;
        _platform = platform;
    }

    /// <summary>
    /// Installs tools. If <paramref name="toolNames"/> is null or empty, installs all tools in the manifest.
    /// </summary>
    /// <param name="toolNames">Specific tools to install, or null for all.</param>
    /// <param name="dryRun">If true, show what would be executed without running anything.</param>
    /// <param name="useColor">Whether to include ANSI colour in output.</param>
    /// <param name="output">Callback for each result line (written to stderr by the console app).</param>
    /// <returns>0 if all succeeded, 1 if any failed.</returns>
    public async Task<int> InstallAsync(string[]? toolNames, bool dryRun, bool useColor, Action<string> output)
    {
        return await ExecuteAsync(toolNames, "install", dryRun, useColor, output).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates tools. Same signature as <see cref="InstallAsync"/>.
    /// </summary>
    public async Task<int> UpdateAsync(string[]? toolNames, bool dryRun, bool useColor, Action<string> output)
    {
        return await ExecuteAsync(toolNames, "update", dryRun, useColor, output).ConfigureAwait(false);
    }

    /// <summary>
    /// Uninstalls tools. Probes all PMs to find which one owns each tool.
    /// </summary>
    public async Task<int> UninstallAsync(string[]? toolNames, bool dryRun, bool useColor, Action<string> output)
    {
        string[] targets = ResolveTargets(toolNames);
        bool anyFailed = false;

        foreach (string toolName in targets)
        {
            if (!_manifest.Tools.TryGetValue(toolName, out ToolEntry? entry))
            {
                output(Formatting.FormatToolResult(toolName, "?", success: false,
                    error: "unknown tool", useColor));
                anyFailed = true;
                continue;
            }

            // Find which PM owns this tool
            IPackageManagerAdapter? ownerAdapter = null;
            string? packageId = null;
            string[] chain = PlatformDetector.GetDefaultChain(_platform);
            foreach (string pmName in chain)
            {
                if (_adapters.TryGetValue(pmName, out IPackageManagerAdapter? adapter))
                {
                    string? pkgId = entry.GetPackageId(pmName);
                    if (pkgId is not null && await adapter.IsInstalled(pkgId).ConfigureAwait(false))
                    {
                        ownerAdapter = adapter;
                        packageId = pkgId;
                        break;
                    }
                }
            }

            if (ownerAdapter is null || packageId is null)
            {
                output(Formatting.FormatToolResult(toolName, "-", success: false,
                    error: "not installed", useColor));
                continue;
            }

            if (dryRun)
            {
                output(Formatting.FormatDryRun(ownerAdapter.Name,
                    new[] { "uninstall", packageId }));
                continue;
            }

            ProcessResult result = await ownerAdapter.Uninstall(packageId).ConfigureAwait(false);
            bool success = result.ExitCode == 0;
            if (!success) { anyFailed = true; }
            output(Formatting.FormatToolResult(toolName, ownerAdapter.Name, success,
                success ? null : $"{ownerAdapter.Name} returned exit code {result.ExitCode}: {result.Stderr}",
                useColor));
        }

        return anyFailed ? 1 : 0;
    }

    /// <summary>
    /// Lists all tools with their install status, version, and owning PM.
    /// Probes all available PMs in the platform default chain.
    /// </summary>
    public async Task<List<ToolStatus>> ListAsync()
    {
        var statuses = new List<ToolStatus>();
        string[] chain = PlatformDetector.GetDefaultChain(_platform);

        foreach (var (toolName, entry) in _manifest.Tools)
        {
            bool found = false;
            foreach (string pmName in chain)
            {
                if (_adapters.TryGetValue(pmName, out IPackageManagerAdapter? adapter))
                {
                    string? packageId = entry.GetPackageId(pmName);
                    if (packageId is not null && await adapter.IsInstalled(packageId).ConfigureAwait(false))
                    {
                        string? version = await adapter.GetInstalledVersion(packageId).ConfigureAwait(false);
                        statuses.Add(new ToolStatus(toolName, true, version, pmName));
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                statuses.Add(new ToolStatus(toolName, false, null, null));
            }
        }

        return statuses;
    }

    private async Task<int> ExecuteAsync(
        string[]? toolNames, string action, bool dryRun, bool useColor, Action<string> output)
    {
        // Use the first (only) adapter for install/update
        IPackageManagerAdapter adapter = _adapters.Values.First();
        string[] targets = ResolveTargets(toolNames);
        bool anyFailed = false;

        foreach (string toolName in targets)
        {
            if (!_manifest.Tools.TryGetValue(toolName, out ToolEntry? entry))
            {
                output(Formatting.FormatToolResult(toolName, adapter.Name, success: false,
                    error: "unknown tool", useColor));
                anyFailed = true;
                continue;
            }

            string? packageId = entry.GetPackageId(adapter.Name);
            if (packageId is null)
            {
                output(Formatting.FormatToolResult(toolName, adapter.Name, success: false,
                    error: $"no package ID for {adapter.Name}", useColor));
                anyFailed = true;
                continue;
            }

            if (dryRun)
            {
                output(Formatting.FormatDryRun(adapter.Name,
                    new[] { action, packageId }));
                continue;
            }

            ProcessResult result = action == "install"
                ? await adapter.Install(packageId).ConfigureAwait(false)
                : await adapter.Update(packageId).ConfigureAwait(false);

            bool success = result.ExitCode == 0;
            if (!success) { anyFailed = true; }
            output(Formatting.FormatToolResult(toolName, adapter.Name, success,
                success ? null : $"{adapter.Name} returned exit code {result.ExitCode}: {result.Stderr}",
                useColor));
        }

        return anyFailed ? 1 : 0;
    }

    private string[] ResolveTargets(string[]? toolNames)
    {
        if (toolNames is null || toolNames.Length == 0)
        {
            return _manifest.GetToolNames();
        }
        return toolNames;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Winix.Tests`
Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/SuiteManager.cs tests/Winix.Winix.Tests/SuiteManagerTests.cs
git commit -m "feat(winix): add SuiteManager orchestration for install/update/uninstall/list"
```

---

## Task 11: Console app (Program.cs)

**Files:**
- Modify: `src/winix/Program.cs`

- [ ] **Step 1: Implement the full Program.cs**

```csharp
// src/winix/Program.cs
using System.Reflection;
using Winix.Winix;
using Yort.ShellKit;

namespace Winix;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        string version = GetVersion();

        var parser = new CommandLineParser("winix", version)
            .Description("Install, update, and manage the Winix CLI tool suite.")
            .StandardFlags()
            .Option("--via", null, "PM", "Package manager to use: winget, scoop, brew, dotnet")
            .Flag("--dry-run", "Show what would be executed, don't run anything")
            .Positional("command [tool...]")
            .Platform("cross-platform",
                Array.Empty<string>(),
                "Delegates to winget/scoop on Windows",
                "Delegates to brew on macOS, dotnet tool on Linux")
            .StdinDescription("Not used")
            .StdoutDescription("Not used (all output goes to stderr)")
            .StderrDescription("Progress, results, list table, status summary, errors")
            .Example("winix install", "Install all Winix tools")
            .Example("winix install timeit peep", "Install specific tools")
            .Example("winix update", "Update all installed tools")
            .Example("winix uninstall", "Uninstall all tools")
            .Example("winix list", "Show all tools and their install status")
            .Example("winix status", "Show summary of installed tools")
            .Example("winix install --via scoop", "Install all tools via Scoop")
            .Example("winix install --dry-run", "Preview install commands without running them")
            .ExitCodes(
                (0, "All operations succeeded"),
                (1, "One or more tools failed"),
                (ExitCode.UsageError, "Usage error (bad arguments)"),
                (ExitCode.NotExecutable, "Cannot execute (no PM found, network error)"),
                (ExitCode.NotFound, "Internal error"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        // --- Extract command and tool names from positionals ---
        string[] positionals = result.Positionals;
        if (positionals.Length == 0)
        {
            return result.WriteError("expected a command: install, update, uninstall, list, or status", Console.Error);
        }

        string command = positionals[0].ToLowerInvariant();
        string[]? toolNames = positionals.Length > 1 ? positionals[1..] : null;

        // --- Validate command ---
        if (command != "install" && command != "update" && command != "uninstall" &&
            command != "list" && command != "status")
        {
            return result.WriteError($"unknown command: '{command}'. Expected install, update, uninstall, list, or status.", Console.Error);
        }

        // --- Resolve flags ---
        string? via = result.Has("--via") ? result.GetString("--via") : null;
        bool dryRun = result.Has("--dry-run");
        bool useColor = result.ResolveColor(checkStdErr: true);

        // --- Validate --via value ---
        if (via is not null && via != "winget" && via != "scoop" && via != "brew" && via != "dotnet")
        {
            return result.WriteError($"invalid --via value: '{via}'. Expected winget, scoop, brew, or dotnet.", Console.Error);
        }

        // --- Fetch manifest ---
        ToolManifest manifest;
        try
        {
            manifest = await ManifestLoader.LoadAsync().ConfigureAwait(false);
        }
        catch (ManifestParseException ex)
        {
            Console.Error.WriteLine($"winix: {ex.Message}");
            return ExitCode.NotExecutable;
        }

        // --- Build adapter map ---
        var adapters = new Dictionary<string, IPackageManagerAdapter>
        {
            { "winget", new WingetAdapter() },
            { "scoop", new ScoopAdapter() },
            { "brew", new BrewAdapter() },
            { "dotnet", new DotnetToolAdapter() },
        };

        PlatformId platform = PlatformDetector.GetCurrentPlatform();
        Action<string> writeLine = line => Console.Error.WriteLine(line);

        // --- list and status: probe all PMs ---
        if (command == "list" || command == "status")
        {
            var manager = new SuiteManager(manifest, adapters, platform);
            List<ToolStatus> statuses = await manager.ListAsync().ConfigureAwait(false);

            if (command == "list")
            {
                var descriptions = new Dictionary<string, string>();
                foreach (var (name, entry) in manifest.Tools)
                {
                    descriptions[name] = entry.Description;
                }

                Console.Error.Write(
                    Formatting.FormatListTable(statuses, descriptions, useColor));

                if (statuses.Count > 0 && statuses.All(s => !s.IsInstalled))
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(Formatting.FormatNoToolsHint());
                }
            }
            else
            {
                Console.Error.WriteLine(
                    Formatting.FormatStatusSummary(statuses, manifest.Tools.Count));

                if (statuses.All(s => !s.IsInstalled))
                {
                    Console.Error.WriteLine(Formatting.FormatNoToolsHint());
                }
            }

            return 0;
        }

        // --- install, update, uninstall: resolve single PM ---
        IPackageManagerAdapter? selectedAdapter = PlatformDetector.ResolveAdapter(
            via, adapters, platform);

        if (selectedAdapter is null)
        {
            string pmList = string.Join(", ", PlatformDetector.GetDefaultChain(platform));
            if (via is not null)
            {
                Console.Error.WriteLine($"winix: --via {via}: {via} is not available on PATH.");
            }
            else
            {
                Console.Error.WriteLine(
                    $"winix: no supported package manager found on PATH. Install one of: {pmList}");
            }
            return ExitCode.NotExecutable;
        }

        // --- Uninstall probes all PMs to find which one owns each tool ---
        if (command == "uninstall")
        {
            var uninstallManager = new SuiteManager(manifest, adapters, platform);
            return await uninstallManager.UninstallAsync(
                toolNames, dryRun, useColor, writeLine).ConfigureAwait(false);
        }

        // --- Auto-setup (bucket/tap) for scoop and brew on install ---
        if (command == "install")
        {
            if (selectedAdapter is ScoopAdapter scoopAdapter)
            {
                await scoopAdapter.EnsureBucket().ConfigureAwait(false);
            }
            else if (selectedAdapter is BrewAdapter brewAdapter)
            {
                await brewAdapter.EnsureTap().ConfigureAwait(false);
            }
        }

        var suiteManager = new SuiteManager(manifest, selectedAdapter);

        return command switch
        {
            "install" => await suiteManager.InstallAsync(toolNames, dryRun, useColor, writeLine).ConfigureAwait(false),
            "update" => await suiteManager.UpdateAsync(toolNames, dryRun, useColor, writeLine).ConfigureAwait(false),
            _ => ExitCode.UsageError,
        };
    }

    private static string GetVersion()
    {
        return typeof(SuiteManager).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
```

- [ ] **Step 2: Build the solution**

Run: `dotnet build Winix.sln`
Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Run all tests**

Run: `dotnet test Winix.sln`
Expected: All tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/winix/Program.cs
git commit -m "feat(winix): implement console app with full command surface"
```

---

## Task 12: README and AI discoverability

**Files:**
- Create: `src/winix/README.md`
- Create: `docs/ai/winix.md`
- Modify: `llms.txt`

- [ ] **Step 1: Check existing README pattern**

Read `src/treex/README.md` and `docs/ai/treex.md` for the format to follow.

- [ ] **Step 2: Create `src/winix/README.md`**

Follow the existing tool README pattern: description, install sections (scoop, winget, dotnet tool, direct download), usage examples, options table, exit codes. Specific content for winix:

- Description: Cross-platform installer for the Winix CLI tool suite. Installs, updates, and uninstalls all Winix tools by delegating to your platform's native package manager.
- Install sections: same channels as other tools
- Usage: `winix install`, `winix update`, `winix list`, `winix status`, `winix uninstall`, `winix install --via scoop`, `winix install timeit peep`
- Options: `--via`, `--dry-run`, `--describe`, `--no-color`, `--help`, `--version`
- Exit codes: 0, 1, 125, 126, 127

- [ ] **Step 3: Create `docs/ai/winix.md`**

Follow the existing AI guide pattern. Read `docs/ai/treex.md` for format.

- [ ] **Step 4: Update `llms.txt`**

Read the existing `llms.txt` and add a winix entry following the same pattern as other tools.

- [ ] **Step 5: Commit**

```bash
git add src/winix/README.md docs/ai/winix.md llms.txt
git commit -m "docs: add winix README, AI guide, and llms.txt entry"
```

---

## Task 13: Update CLAUDE.md and solution conventions

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update the project layout section**

Add `Winix.Winix` and `winix` to the project layout in `CLAUDE.md`:

```
src/Winix.Winix/           — class library (PM adapters, manifest, orchestration)
src/winix/                 — console app entry point (suite installer)
tests/Winix.Winix.Tests/   — xUnit tests
```

- [ ] **Step 2: Update the NuGet package IDs list**

Add `Winix.Winix` to the NuGet package IDs line.

- [ ] **Step 3: Update the scoop bucket description**

Add `winix.json` to the scoop manifests list if not already there (it already exists but the behaviour is changing).

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: add winix installer to CLAUDE.md project layout and conventions"
```

---

## Task 14: Scoop manifest update

**Files:**
- Modify: `bucket/winix.json`

- [ ] **Step 1: Read current `bucket/winix.json`**

The current manifest bundles all tool binaries in a combined zip. It needs to change to: install only the `winix` binary, then run `winix install --via scoop` as a post-install hook.

- [ ] **Step 2: Update `bucket/winix.json`**

```json
{
  "version": "0.2.0",
  "description": "Winix CLI tool suite installer — installs, updates, and manages all Winix tools.",
  "homepage": "https://github.com/Yortw/winix",
  "license": "MIT",
  "architecture": {
    "64bit": {
      "url": "https://github.com/Yortw/winix/releases/download/v0.2.0/winix-win-x64.zip",
      "hash": ""
    }
  },
  "bin": "winix.exe",
  "post_install": "winix install --via scoop",
  "checkver": "github",
  "autoupdate": {
    "architecture": {
      "64bit": {
        "url": "https://github.com/Yortw/winix/releases/download/v$version/winix-win-x64.zip"
      }
    }
  }
}
```

Note: The hash will be empty until a release is built. The URL pattern changes from `winix-win-x64.zip` (combined) to the per-tool zip for just the `winix` binary. The `bin` array becomes a single `winix.exe`. The `post_install` field is the key addition.

- [ ] **Step 3: Commit**

```bash
git add bucket/winix.json
git commit -m "feat(scoop): update winix.json to installer-based approach with post_install hook"
```

---

## Task 15: Release pipeline updates

**Files:**
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Read the current release workflow**

Read `.github/workflows/release.yml` to understand the current structure. Key sections to modify:
- `pack-nuget` job: add `winix` to NuGet pack
- `publish-aot` job: add `winix` to AOT publish and zip steps
- Combined zip step: update to include `winix.exe` OR replace combined zip with just `winix`
- `generate-winget-manifests` job: add `winix` manifest generation
- Scoop update step: `winix.json` now points to per-tool zip, not combined zip
- New step: generate `winix-manifest.json` and upload as release asset

- [ ] **Step 2: Add winix to NuGet pack step**

Add a `dotnet pack` line for `src/winix/winix.csproj` following the pattern of the other tools.

- [ ] **Step 3: Add winix to AOT publish step**

Add `dotnet publish src/winix/winix.csproj` and zip creation following the pattern of the other tools.

- [ ] **Step 4: Update combined zip step**

The combined zip (`winix-win-x64.zip`) previously contained all tool binaries. Now it should contain only `winix.exe` (since the other tools are installed by `winix install`). Update the combined zip step accordingly.

- [ ] **Step 5: Add manifest generation step**

Add a step that generates `winix-manifest.json` from the tool list:

```bash
cat > winix-manifest.json << 'MANIFEST'
{
  "version": "${VERSION}",
  "tools": {
    "timeit": { "description": "Time a command — wall clock, CPU time, peak memory, exit code.", "packages": { "winget": "Winix.TimeIt", "scoop": "timeit", "brew": "timeit", "dotnet": "Winix.TimeIt" } },
    "squeeze": { "description": "Compress and decompress files using gzip, brotli, or zstd.", "packages": { "winget": "Winix.Squeeze", "scoop": "squeeze", "brew": "squeeze", "dotnet": "Winix.Squeeze" } },
    "peep": { "description": "Run a command repeatedly and display output on a refreshing screen.", "packages": { "winget": "Winix.Peep", "scoop": "peep", "brew": "peep", "dotnet": "Winix.Peep" } },
    "wargs": { "description": "Cross-platform xargs replacement with sane defaults.", "packages": { "winget": "Winix.Wargs", "scoop": "wargs", "brew": "wargs", "dotnet": "Winix.Wargs" } },
    "files": { "description": "Find files by name, size, date, type, and content.", "packages": { "winget": "Winix.Files", "scoop": "files", "brew": "files", "dotnet": "Winix.Files" } },
    "treex": { "description": "Enhanced directory tree with colour, filtering, size rollups, and clickable hyperlinks.", "packages": { "winget": "Winix.TreeX", "scoop": "treex", "brew": "treex", "dotnet": "Winix.TreeX" } }
  }
}
MANIFEST
```

Use `envsubst` or `sed` to replace `${VERSION}` with the release version.

- [ ] **Step 6: Upload manifest as release asset**

Add `winix-manifest.json` to the release asset upload step.

- [ ] **Step 7: Add winix to winget manifest generation**

Add: `generate_manifests "winix" "Winix" "Cross-platform installer for the Winix CLI tool suite."`

- [ ] **Step 8: Update scoop update step**

Update the `update_manifest bucket/winix.json` call. The hash source changes from the combined zip to the per-tool `winix-win-x64.zip`.

- [ ] **Step 9: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "feat(ci): add winix to release pipeline with manifest generation"
```

---

## Task 16: Final integration build and test

- [ ] **Step 1: Build the full solution**

Run: `dotnet build Winix.sln`
Expected: 0 errors, 0 warnings across all projects.

- [ ] **Step 2: Run all tests**

Run: `dotnet test Winix.sln`
Expected: All tests pass (existing 658 + new winix tests).

- [ ] **Step 3: Verify AOT publish works**

Run: `dotnet publish src/winix/winix.csproj -c Release -r win-x64`
Expected: Produces a native binary at `src/winix/bin/Release/net10.0/win-x64/publish/winix.exe`.

- [ ] **Step 4: Smoke test the binary**

Run: `src/winix/bin/Release/net10.0/win-x64/publish/winix.exe --help`
Expected: Shows help text with all commands and flags.

Run: `src/winix/bin/Release/net10.0/win-x64/publish/winix.exe --describe`
Expected: Outputs JSON metadata.

- [ ] **Step 5: Commit any fixes if needed**

Only if the integration steps above revealed issues.

---

## Summary

| Task | What it builds | Estimated tests |
|------|----------------|----------------|
| 1 | Project scaffolding | 0 |
| 2 | Manifest model + parsing | 7 |
| 3 | ProcessHelper | 5 |
| 4 | IPackageManagerAdapter + PlatformDetector | 7 |
| 5 | WingetAdapter | 8 |
| 6 | ScoopAdapter | 9 |
| 7 | BrewAdapter | 9 |
| 8 | DotnetToolAdapter | 7 |
| 9 | Output formatting | 8 |
| 10 | SuiteManager orchestration | 6 |
| 11 | Console app | 0 (covered by unit tests) |
| 12 | README + AI docs | 0 |
| 13 | CLAUDE.md updates | 0 |
| 14 | Scoop manifest | 0 |
| 15 | Release pipeline | 0 |
| 16 | Integration verification | 0 |
| **Total** | | **~66 new tests** |
