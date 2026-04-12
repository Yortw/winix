# whoholds Implementation Plan

**Date:** 2026-04-12
**Goal:** Implement the `whoholds` tool — a cross-platform CLI that shows which processes are holding a file lock or binding a network port.
**Design spec:** `docs/plans/2026-04-12-whoholds-design.md`

## Architecture

```
src/Winix.WhoHolds/        — class library (LockInfo, finders, formatting, argument parsing)
src/whoholds/              — thin console app (arg parsing via ShellKit, call library, exit code)
tests/Winix.WhoHolds.Tests/ — xUnit tests
```

**Tech stack:** .NET 10, AOT-compiled, P/Invoke for Windows APIs (rstrtmgr.dll, iphlpapi.dll), lsof delegation on Linux/macOS.

## File Structure

| File | Purpose |
|------|---------|
| `src/Winix.WhoHolds/Winix.WhoHolds.csproj` | Class library project |
| `src/Winix.WhoHolds/LockInfo.cs` | Result type for all finders |
| `src/Winix.WhoHolds/ArgumentParser.cs` | Detect file vs port from positional arg |
| `src/Winix.WhoHolds/FileLockFinder.cs` | Restart Manager P/Invoke (Windows) |
| `src/Winix.WhoHolds/PortLockFinder.cs` | IP Helper P/Invoke (Windows) |
| `src/Winix.WhoHolds/LsofFinder.cs` | lsof delegation (Linux/macOS) |
| `src/Winix.WhoHolds/ElevationDetector.cs` | Admin/root check |
| `src/Winix.WhoHolds/Formatting.cs` | Table, PID-only, JSON, warnings |
| `src/whoholds/whoholds.csproj` | Console app project |
| `src/whoholds/Program.cs` | Entry point |
| `tests/Winix.WhoHolds.Tests/Winix.WhoHolds.Tests.csproj` | Test project |
| `tests/Winix.WhoHolds.Tests/LockInfoTests.cs` | LockInfo unit tests |
| `tests/Winix.WhoHolds.Tests/ArgumentParserTests.cs` | Argument detection tests |
| `tests/Winix.WhoHolds.Tests/FormattingTests.cs` | Output formatting tests |
| `tests/Winix.WhoHolds.Tests/FileLockFinderTests.cs` | Integration tests (Windows) |
| `tests/Winix.WhoHolds.Tests/PortLockFinderTests.cs` | Integration tests (Windows) |
| `src/whoholds/README.md` | Tool documentation |
| `docs/ai/whoholds.md` | AI agent guide |
| `bucket/whoholds.json` | Scoop manifest |

---

## Task 1: Scaffolding

Create all three projects, add to solution, verify builds clean.

### Step 1.1 — Create `src/Winix.WhoHolds/Winix.WhoHolds.csproj`

Create file `src/Winix.WhoHolds/Winix.WhoHolds.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
    <IsPackable>false</IsPackable>
    <PackageId>Winix.WhoHolds.Library</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
  </ItemGroup>
</Project>
```

### Step 1.2 — Create placeholder `src/Winix.WhoHolds/LockInfo.cs`

Create file `src/Winix.WhoHolds/LockInfo.cs`:

```csharp
namespace Winix.WhoHolds;

/// <summary>
/// A process holding a resource (file lock or port binding).
/// Returned by all finder implementations.
/// </summary>
public sealed class LockInfo
{
    /// <summary>Process ID.</summary>
    public int ProcessId { get; }

    /// <summary>
    /// Process name (e.g. "devenv.exe").
    /// May be empty if the process exited before the name could be read.
    /// </summary>
    public string ProcessName { get; }

    /// <summary>
    /// The resource being held. For files: the queried file path.
    /// For ports: "TCP :8080" or "UDP :53".
    /// </summary>
    public string Resource { get; }

    /// <summary>Creates a new <see cref="LockInfo"/> instance.</summary>
    /// <param name="processId">The process ID holding the resource.</param>
    /// <param name="processName">The process name.</param>
    /// <param name="resource">Description of the held resource.</param>
    public LockInfo(int processId, string processName, string resource)
    {
        ProcessId = processId;
        ProcessName = processName ?? "";
        Resource = resource ?? "";
    }
}
```

### Step 1.3 — Create `src/whoholds/whoholds.csproj`

Create file `src/whoholds/whoholds.csproj`:

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
    <ToolCommandName>whoholds</ToolCommandName>
    <PackageId>Winix.WhoHolds</PackageId>
    <Description>Find which processes are holding a file lock or binding a network port.</Description>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.WhoHolds\Winix.WhoHolds.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

### Step 1.4 — Create placeholder `src/whoholds/Program.cs`

Create file `src/whoholds/Program.cs`:

```csharp
using System.Reflection;
using Winix.WhoHolds;
using Yort.ShellKit;

namespace WhoHolds;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        string version = GetVersion();

        var parser = new CommandLineParser("whoholds", version)
            .Description("Find which processes are holding a file lock or binding a network port.")
            .StandardFlags()
            .Positional("<file-or-port>")
            .Flag("--pid-only", "Force one-PID-per-line output (auto when piped)")
            .ExitCodes(
                (0, "Success (results found, or no results but query succeeded)"),
                (1, "Error (file not found, API failure, unsupported platform)"),
                (ExitCode.UsageError, "Usage error (bad arguments)"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        // TODO: implement in later tasks
        Console.Error.WriteLine("whoholds: not yet implemented");
        return 1;
    }

    /// <summary>
    /// Returns the informational version from the Winix.WhoHolds library assembly.
    /// </summary>
    private static string GetVersion()
    {
        return typeof(LockInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
```

### Step 1.5 — Create placeholder `src/whoholds/README.md`

Create file `src/whoholds/README.md`:

```markdown
# whoholds

Find which processes are holding a file lock or binding a network port.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/whoholds
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.WhoHolds
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).
```

### Step 1.6 — Create `tests/Winix.WhoHolds.Tests/Winix.WhoHolds.Tests.csproj`

Create file `tests/Winix.WhoHolds.Tests/Winix.WhoHolds.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\Winix.WhoHolds\Winix.WhoHolds.csproj" />
  </ItemGroup>
</Project>
```

### Step 1.7 — Create placeholder test file

Create file `tests/Winix.WhoHolds.Tests/LockInfoTests.cs`:

```csharp
using Winix.WhoHolds;

namespace Winix.WhoHolds.Tests;

public sealed class LockInfoTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var info = new LockInfo(1234, "devenv.exe", "D:\\test.dll");

        Assert.Equal(1234, info.ProcessId);
        Assert.Equal("devenv.exe", info.ProcessName);
        Assert.Equal("D:\\test.dll", info.Resource);
    }

    [Fact]
    public void Constructor_NullProcessName_DefaultsToEmpty()
    {
        var info = new LockInfo(1, null!, "resource");

        Assert.Equal("", info.ProcessName);
    }

    [Fact]
    public void Constructor_NullResource_DefaultsToEmpty()
    {
        var info = new LockInfo(1, "name", null!);

        Assert.Equal("", info.Resource);
    }
}
```

### Step 1.8 — Add projects to solution

```bash
cd D:\projects\winix
dotnet sln Winix.sln add src/Winix.WhoHolds/Winix.WhoHolds.csproj --solution-folder src
dotnet sln Winix.sln add src/whoholds/whoholds.csproj --solution-folder src
dotnet sln Winix.sln add tests/Winix.WhoHolds.Tests/Winix.WhoHolds.Tests.csproj --solution-folder tests
```

### Step 1.9 — Build and run tests

```bash
dotnet build Winix.sln
dotnet test tests/Winix.WhoHolds.Tests/Winix.WhoHolds.Tests.csproj
```

### Step 1.10 — Commit

```bash
git add src/Winix.WhoHolds/ src/whoholds/ tests/Winix.WhoHolds.Tests/ Winix.sln
git commit -m "feat(whoholds): scaffold projects, add to solution"
```

---

## Task 2: ArgumentParser

Implement the argument detection logic that resolves a positional argument to either a file path or a port number.

### Step 2.1 — Write ArgumentParser tests

Create file `tests/Winix.WhoHolds.Tests/ArgumentParserTests.cs`:

```csharp
using Winix.WhoHolds;

namespace Winix.WhoHolds.Tests;

public sealed class ArgumentParserTests
{
    [Fact]
    public void Parse_ColonPrefix_ReturnsPort()
    {
        var result = ArgumentParser.Parse(":8080");

        Assert.True(result.IsPort);
        Assert.Equal(8080, result.Port);
        Assert.Null(result.FilePath);
    }

    [Fact]
    public void Parse_ColonPrefixZero_ReturnsError()
    {
        var result = ArgumentParser.Parse(":0");

        Assert.True(result.IsError);
        Assert.Contains("invalid port", result.ErrorMessage);
    }

    [Fact]
    public void Parse_ColonPrefixNegative_ReturnsError()
    {
        var result = ArgumentParser.Parse(":-1");

        Assert.True(result.IsError);
        Assert.Contains("invalid port", result.ErrorMessage);
    }

    [Fact]
    public void Parse_ColonPrefixTooLarge_ReturnsError()
    {
        var result = ArgumentParser.Parse(":99999");

        Assert.True(result.IsError);
        Assert.Contains("invalid port", result.ErrorMessage);
    }

    [Fact]
    public void Parse_ColonPrefixNotANumber_ReturnsError()
    {
        var result = ArgumentParser.Parse(":abc");

        Assert.True(result.IsError);
        Assert.Contains("invalid port", result.ErrorMessage);
    }

    [Fact]
    public void Parse_ExistingFile_ReturnsFile()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var result = ArgumentParser.Parse(tempFile);

            Assert.True(result.IsFile);
            Assert.Equal(tempFile, result.FilePath);
            Assert.Equal(0, result.Port);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_ExistingDirectory_ReturnsFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"whoholds-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = ArgumentParser.Parse(tempDir);

            Assert.True(result.IsFile);
            Assert.Equal(tempDir, result.FilePath);
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public void Parse_BareNumber_NoSuchFile_ReturnsPort()
    {
        // "8080" with no file named "8080" on disk -> port
        var result = ArgumentParser.Parse("8080");

        Assert.True(result.IsPort);
        Assert.Equal(8080, result.Port);
    }

    [Fact]
    public void Parse_BareNumber_FileExists_ReturnsFile()
    {
        // Create a file literally named with a number
        string tempDir = Path.Combine(Path.GetTempPath(), $"whoholds-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        string numFile = Path.Combine(tempDir, "8080");
        File.WriteAllText(numFile, "");
        try
        {
            var result = ArgumentParser.Parse(numFile);

            Assert.True(result.IsFile);
        }
        finally
        {
            File.Delete(numFile);
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public void Parse_NonExistentFile_ReturnsError()
    {
        var result = ArgumentParser.Parse("/no/such/file/ever.dll");

        Assert.True(result.IsError);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsError()
    {
        var result = ArgumentParser.Parse("");

        Assert.True(result.IsError);
    }

    [Fact]
    public void Parse_BareNumberPortZero_ReturnsError()
    {
        var result = ArgumentParser.Parse("0");

        Assert.True(result.IsError);
        Assert.Contains("invalid port", result.ErrorMessage);
    }

    [Fact]
    public void Parse_BareNumberPortTooLarge_ReturnsError()
    {
        var result = ArgumentParser.Parse("70000");

        Assert.True(result.IsError);
        Assert.Contains("invalid port", result.ErrorMessage);
    }
}
```

### Step 2.2 — Verify tests fail

```bash
dotnet test tests/Winix.WhoHolds.Tests/Winix.WhoHolds.Tests.csproj
```

All ArgumentParser tests must fail (class does not exist yet).

### Step 2.3 — Implement ArgumentParser

Create file `src/Winix.WhoHolds/ArgumentParser.cs`:

```csharp
namespace Winix.WhoHolds;

/// <summary>
/// Detects whether a positional argument is a file path or a port number.
/// </summary>
public static class ArgumentParser
{
    /// <summary>
    /// Parses a positional argument into either a file path or port number.
    /// </summary>
    /// <param name="argument">The raw argument string from the command line.</param>
    /// <returns>A <see cref="ParsedArgument"/> indicating the detected resource type.</returns>
    /// <remarks>
    /// Resolution order:
    /// <list type="number">
    /// <item>If argument starts with <c>:</c> — explicit port lookup. Strip colon, parse as integer.</item>
    /// <item>If argument exists as a file or directory on disk — file lookup.</item>
    /// <item>If argument is a bare integer and no such file exists — port lookup.</item>
    /// <item>Otherwise — error (file not found).</item>
    /// </list>
    /// </remarks>
    public static ParsedArgument Parse(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return ParsedArgument.Error("no resource specified");
        }

        // 1. Explicit port prefix ":"
        if (argument.StartsWith(':'))
        {
            string portStr = argument.Substring(1);
            return TryParsePort(portStr, out int port)
                ? ParsedArgument.ForPort(port)
                : ParsedArgument.Error($"invalid port: '{portStr}' (must be 1-65535)");
        }

        // 2. Existing file or directory on disk
        if (File.Exists(argument) || Directory.Exists(argument))
        {
            return ParsedArgument.ForFile(argument);
        }

        // 3. Bare integer -> port (if no file with that name exists)
        if (int.TryParse(argument, out int barePort))
        {
            return IsValidPort(barePort)
                ? ParsedArgument.ForPort(barePort)
                : ParsedArgument.Error($"invalid port: '{argument}' (must be 1-65535)");
        }

        // 4. Not a file, not a number -> file not found
        return ParsedArgument.Error($"not found: '{argument}' (not a file, directory, or port number)");
    }

    private static bool TryParsePort(string s, out int port)
    {
        if (int.TryParse(s, out port) && IsValidPort(port))
        {
            return true;
        }

        port = 0;
        return false;
    }

    private static bool IsValidPort(int port)
    {
        return port >= 1 && port <= 65535;
    }
}
```

### Step 2.4 — Implement ParsedArgument

Create file `src/Winix.WhoHolds/ParsedArgument.cs`:

```csharp
namespace Winix.WhoHolds;

/// <summary>
/// The result of parsing a positional argument: file path, port number, or error.
/// </summary>
public sealed class ParsedArgument
{
    /// <summary>The detected file path, or null if this is a port lookup or error.</summary>
    public string? FilePath { get; }

    /// <summary>The detected port number. Zero if this is a file lookup or error.</summary>
    public int Port { get; }

    /// <summary>Error message when parsing failed, or null on success.</summary>
    public string? ErrorMessage { get; }

    /// <summary>True if the argument was resolved to a file path.</summary>
    public bool IsFile => FilePath is not null;

    /// <summary>True if the argument was resolved to a port number.</summary>
    public bool IsPort => Port > 0 && FilePath is null && ErrorMessage is null;

    /// <summary>True if argument parsing failed.</summary>
    public bool IsError => ErrorMessage is not null;

    private ParsedArgument(string? filePath, int port, string? errorMessage)
    {
        FilePath = filePath;
        Port = port;
        ErrorMessage = errorMessage;
    }

    /// <summary>Creates a result representing a file path lookup.</summary>
    internal static ParsedArgument ForFile(string filePath)
    {
        return new ParsedArgument(filePath, 0, null);
    }

    /// <summary>Creates a result representing a port lookup.</summary>
    internal static ParsedArgument ForPort(int port)
    {
        return new ParsedArgument(null, port, null);
    }

    /// <summary>Creates a result representing a parsing error.</summary>
    internal static ParsedArgument Error(string message)
    {
        return new ParsedArgument(null, 0, message);
    }
}
```

### Step 2.5 — Verify tests pass

```bash
dotnet test tests/Winix.WhoHolds.Tests/Winix.WhoHolds.Tests.csproj
```

All ArgumentParser and LockInfo tests must pass.

### Step 2.6 — Commit

```bash
git add src/Winix.WhoHolds/ArgumentParser.cs src/Winix.WhoHolds/ParsedArgument.cs tests/Winix.WhoHolds.Tests/ArgumentParserTests.cs
git commit -m "feat(whoholds): implement ArgumentParser with file-vs-port detection"
```

---

## Task 3: FileLockFinder (Restart Manager P/Invoke, Windows-only)

### Step 3.1 — Write FileLockFinder integration test

Create file `tests/Winix.WhoHolds.Tests/FileLockFinderTests.cs`:

```csharp
using Winix.WhoHolds;

namespace Winix.WhoHolds.Tests;

public sealed class FileLockFinderTests
{
    [Fact]
    public void Find_LockedFile_ReturnsCurrentProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string tempFile = Path.GetTempFileName();
        try
        {
            // Hold an exclusive lock on the file
            using var stream = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            List<LockInfo> results = FileLockFinder.Find(tempFile);

            // Our own process should appear in the results
            int myPid = Environment.ProcessId;
            Assert.Contains(results, r => r.ProcessId == myPid);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Find_UnlockedFile_ReturnsEmpty()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string tempFile = Path.GetTempFileName();
        try
        {
            // File exists but is not locked
            List<LockInfo> results = FileLockFinder.Find(tempFile);

            Assert.Empty(results);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Find_NonExistentFile_ReturnsEmpty()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        List<LockInfo> results = FileLockFinder.Find(@"C:\no\such\file\ever.xyz");

        Assert.Empty(results);
    }

    [Fact]
    public void Find_LockedFile_ResourceContainsFilePath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string tempFile = Path.GetTempFileName();
        try
        {
            using var stream = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            List<LockInfo> results = FileLockFinder.Find(tempFile);
            int myPid = Environment.ProcessId;
            LockInfo? mine = results.Find(r => r.ProcessId == myPid);

            Assert.NotNull(mine);
            Assert.Equal(tempFile, mine.Resource);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
```

### Step 3.2 — Verify tests fail

```bash
dotnet test tests/Winix.WhoHolds.Tests/Winix.WhoHolds.Tests.csproj
```

### Step 3.3 — Implement FileLockFinder

Create file `src/Winix.WhoHolds/FileLockFinder.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Winix.WhoHolds;

/// <summary>
/// Finds processes holding a file lock using the Windows Restart Manager API.
/// No admin privileges required — only sees the current user's processes.
/// </summary>
public static class FileLockFinder
{
    /// <summary>
    /// Returns processes holding a lock on the specified file.
    /// Windows-only. Returns an empty list on other platforms or on API failure.
    /// </summary>
    /// <param name="filePath">The full path to the file to check.</param>
    /// <returns>A list of <see cref="LockInfo"/> for each process holding the file.</returns>
    public static List<LockInfo> Find(string filePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new List<LockInfo>();
        }

        return FindWindows(filePath);
    }

    private static List<LockInfo> FindWindows(string filePath)
    {
        var results = new List<LockInfo>();

        int hr = RmStartSession(out uint sessionHandle, 0, Guid.NewGuid().ToString());
        if (hr != 0)
        {
            return results;
        }

        try
        {
            string[] resources = new[] { filePath };
            hr = RmRegisterResources(sessionHandle, (uint)resources.Length, resources, 0, null, 0, null);
            if (hr != 0)
            {
                return results;
            }

            // First call to get the required count
            uint needed = 0;
            uint count = 0;
            uint rebootReasons = 0;
            hr = RmGetList(sessionHandle, out needed, ref count, null, ref rebootReasons);

            // ERROR_MORE_DATA (234) means we need a bigger buffer; 0 means success with zero results
            if (hr == 0 && needed == 0)
            {
                return results;
            }

            if (hr != ErrorMoreData && hr != 0)
            {
                return results;
            }

            // Allocate and retry
            var processInfo = new RM_PROCESS_INFO[needed];
            count = needed;
            hr = RmGetList(sessionHandle, out needed, ref count, processInfo, ref rebootReasons);
            if (hr != 0)
            {
                return results;
            }

            for (int i = 0; i < count; i++)
            {
                RM_PROCESS_INFO pi = processInfo[i];
                results.Add(new LockInfo(
                    (int)pi.Process.dwProcessId,
                    pi.strAppName ?? "",
                    filePath));
            }
        }
        finally
        {
            RmEndSession(sessionHandle);
        }

        return results;
    }

    // --- Restart Manager P/Invoke ---

    private const int ErrorMoreData = 234;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public uint dwProcessId;
        public long ProcessStartTime; // FILETIME as int64
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;
        public uint ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int flags, string sessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint sessionHandle,
        uint nFiles, string[] files,
        uint nApps, RM_UNIQUE_PROCESS[]? apps,
        uint nServices, string[]? services);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint sessionHandle,
        out uint needed,
        ref uint count,
        [In, Out] RM_PROCESS_INFO[]? info,
        ref uint rebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);
}
```

### Step 3.4 — Verify tests pass

```bash
dotnet test tests/Winix.WhoHolds.Tests/Winix.WhoHolds.Tests.csproj
```

### Step 3.5 — Commit

```bash
git add src/Winix.WhoHolds/FileLockFinder.cs tests/Winix.WhoHolds.Tests/FileLockFinderTests.cs
git commit -m "feat(whoholds): implement FileLockFinder using Restart Manager API"
```

---

## Task 4: PortLockFinder (IP Helper P/Invoke, Windows-only)

### Step 4.1 — Write PortLockFinder integration test

Create file `tests/Winix.WhoHolds.Tests/PortLockFinderTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using Winix.WhoHolds;

namespace Winix.WhoHolds.Tests;

public sealed class PortLockFinderTests
{
    [Fact]
    public void Find_BoundPort_ReturnsCurrentProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Bind a TCP listener on a random available port
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        List<LockInfo> results = PortLockFinder.Find(port);

        int myPid = Environment.ProcessId;
        Assert.Contains(results, r => r.ProcessId == myPid);
    }

    [Fact]
    public void Find_BoundPort_ResourceShowsProtocol()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        List<LockInfo> results = PortLockFinder.Find(port);

        int myPid = Environment.ProcessId;
        LockInfo? mine = results.Find(r => r.ProcessId == myPid);

        Assert.NotNull(mine);
        Assert.Contains("TCP", mine.Resource);
        Assert.Contains($":{port}", mine.Resource);
    }

    [Fact]
    public void Find_UdpBoundPort_ReturnsCurrentProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Bind a UDP socket on a random available port
        using var udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)udpClient.Client.LocalEndPoint!).Port;

        List<LockInfo> results = PortLockFinder.Find(port);

        int myPid = Environment.ProcessId;
        Assert.Contains(results, r => r.ProcessId == myPid);
    }

    [Fact]
    public void Find_UnusedPort_ReturnsEmpty()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Find a port that is definitely not in use by binding then releasing
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        List<LockInfo> results = PortLockFinder.Find(port);

        // After stopping, our PID should not appear
        int myPid = Environment.ProcessId;
        Assert.DoesNotContain(results, r => r.ProcessId == myPid);
    }
}
```

### Step 4.2 — Verify tests fail

```bash
dotnet test tests/Winix.WhoHolds.Tests/Winix.WhoHolds.Tests.csproj
```

### Step 4.3 — Implement PortLockFinder

Create file `src/Winix.WhoHolds/PortLockFinder.cs`:

```csharp
using System.Net;
using System.Runtime.InteropServices;

namespace Winix.WhoHolds;

/// <summary>
/// Finds processes bound to a TCP or UDP port using the Windows IP Helper API.
/// No admin privileges required.
/// </summary>
public static class PortLockFinder
{
    /// <summary>
    /// Returns processes bound to the specified port (TCP and UDP, IPv4 and IPv6).
    /// Windows-only. Returns an empty list on other platforms.
    /// </summary>
    /// <param name="port">The port number to check (1-65535).</param>
    /// <returns>A list of <see cref="LockInfo"/> for each process binding the port.</returns>
    public static List<LockInfo> Find(int port)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new List<LockInfo>();
        }

        return FindWindows(port);
    }

    private static List<LockInfo> FindWindows(int port)
    {
        var results = new List<LockInfo>();
        var seen = new HashSet<int>(); // deduplicate PIDs that appear in multiple tables

        // Scan TCP IPv4
        FindTcp(port, AF_INET, results, seen);

        // Scan TCP IPv6
        FindTcp(port, AF_INET6, results, seen);

        // Scan UDP IPv4
        FindUdp(port, AF_INET, results, seen);

        // Scan UDP IPv6
        FindUdp(port, AF_INET6, results, seen);

        return results;
    }

    private static void FindTcp(int port, uint addressFamily, List<LockInfo> results, HashSet<int> seen)
    {
        uint size = 0;
        uint ret = GetExtendedTcpTable(IntPtr.Zero, ref size, false, addressFamily, TCP_TABLE_OWNER_PID_ALL, 0);
        if (ret != ERROR_INSUFFICIENT_BUFFER)
        {
            return;
        }

        IntPtr tablePtr = Marshal.AllocHGlobal((int)size);
        try
        {
            ret = GetExtendedTcpTable(tablePtr, ref size, false, addressFamily, TCP_TABLE_OWNER_PID_ALL, 0);
            if (ret != 0)
            {
                return;
            }

            int numEntries = Marshal.ReadInt32(tablePtr);
            int rowSize = addressFamily == AF_INET
                ? Marshal.SizeOf<MIB_TCPROW_OWNER_PID>()
                : Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>();
            IntPtr rowPtr = tablePtr + 4;

            for (int i = 0; i < numEntries; i++)
            {
                int localPort;
                int pid;

                if (addressFamily == AF_INET)
                {
                    var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                    localPort = NetworkToHostPort(row.dwLocalPort);
                    pid = (int)row.dwOwningPid;
                }
                else
                {
                    var row = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(rowPtr);
                    localPort = NetworkToHostPort(row.dwLocalPort);
                    pid = (int)row.dwOwningPid;
                }

                if (localPort == port && pid != 0 && seen.Add(pid))
                {
                    string processName = GetProcessName(pid);
                    results.Add(new LockInfo(pid, processName, $"TCP :{port}"));
                }

                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(tablePtr);
        }
    }

    private static void FindUdp(int port, uint addressFamily, List<LockInfo> results, HashSet<int> seen)
    {
        uint size = 0;
        uint ret = GetExtendedUdpTable(IntPtr.Zero, ref size, false, addressFamily, UDP_TABLE_OWNER_PID, 0);
        if (ret != ERROR_INSUFFICIENT_BUFFER)
        {
            return;
        }

        IntPtr tablePtr = Marshal.AllocHGlobal((int)size);
        try
        {
            ret = GetExtendedUdpTable(tablePtr, ref size, false, addressFamily, UDP_TABLE_OWNER_PID, 0);
            if (ret != 0)
            {
                return;
            }

            int numEntries = Marshal.ReadInt32(tablePtr);
            int rowSize = addressFamily == AF_INET
                ? Marshal.SizeOf<MIB_UDPROW_OWNER_PID>()
                : Marshal.SizeOf<MIB_UDP6ROW_OWNER_PID>();
            IntPtr rowPtr = tablePtr + 4;

            for (int i = 0; i < numEntries; i++)
            {
                int localPort;
                int pid;

                if (addressFamily == AF_INET)
                {
                    var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                    localPort = NetworkToHostPort(row.dwLocalPort);
                    pid = (int)row.dwOwningPid;
                }
                else
                {
                    var row = Marshal.PtrToStructure<MIB_UDP6ROW_OWNER_PID>(rowPtr);
                    localPort = NetworkToHostPort(row.dwLocalPort);
                    pid = (int)row.dwOwningPid;
                }

                if (localPort == port && pid != 0 && seen.Add(pid))
                {
                    string processName = GetProcessName(pid);
                    results.Add(new LockInfo(pid, processName, $"UDP :{port}"));
                }

                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(tablePtr);
        }
    }

    /// <summary>
    /// Converts a port number from network byte order (big-endian) to host byte order.
    /// The IP Helper API stores ports in network byte order, but only the low 16 bits are meaningful.
    /// </summary>
    private static int NetworkToHostPort(uint networkPort)
    {
        return (int)(ushort)IPAddress.NetworkToHostOrder((short)(networkPort & 0xFFFF));
    }

    /// <summary>
    /// Attempts to get the process name by PID. Returns an empty string if the process
    /// has exited or access is denied (common for system processes).
    /// </summary>
    private static string GetProcessName(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    // --- Constants ---

    private const uint AF_INET = 2;
    private const uint AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const int UDP_TABLE_OWNER_PID = 1;
    private const uint ERROR_INSUFFICIENT_BUFFER = 122;

    // --- TCP IPv4 ---

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    // --- TCP IPv6 ---

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ucLocalAddr;
        public uint dwLocalScopeId;
        public uint dwLocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ucRemoteAddr;
        public uint dwRemoteScopeId;
        public uint dwRemotePort;
        public uint dwState;
        public uint dwOwningPid;
    }

    // --- UDP IPv4 ---

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    // --- UDP IPv6 ---

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ucLocalAddr;
        public uint dwLocalScopeId;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    // --- P/Invoke ---

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(
        IntPtr table, ref uint size, bool order,
        uint af, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedUdpTable(
        IntPtr table, ref uint size, bool order,
        uint af, int tableClass, uint reserved);
}
```

### Step 4.4 — Verify tests pass

```bash
dotnet test tests/Winix.WhoHolds.Tests/Winix.WhoHolds.Tests.csproj
```

### Step 4.5 — Commit

```bash
git add src/Winix.WhoHolds/PortLockFinder.cs tests/Winix.WhoHolds.Tests/PortLockFinderTests.cs
git commit -m "feat(whoholds): implement PortLockFinder using IP Helper API"
```

---

## Task 5: LsofFinder (Linux/macOS delegation)

### Step 5.1 — Implement LsofFinder

Create file `src/Winix.WhoHolds/LsofFinder.cs`:

```csharp
using System.Diagnostics;

namespace Winix.WhoHolds;

/// <summary>
/// Delegates to the <c>lsof</c> command on Linux and macOS for file and port lookups.
/// Falls back when <c>lsof</c> is not available on PATH.
/// </summary>
public static class LsofFinder
{
    /// <summary>
    /// Returns true if <c>lsof</c> is available on PATH.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "lsof",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("-v");
            process.Start();
            process.WaitForExit(5000);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns processes holding a lock on the specified file using <c>lsof</c>.
    /// </summary>
    /// <param name="filePath">The full path to the file to check.</param>
    /// <returns>A list of <see cref="LockInfo"/> for each process holding the file.</returns>
    public static List<LockInfo> FindFile(string filePath)
    {
        // lsof <filePath>
        string output = RunLsof(new[] { filePath });
        return ParseOutput(output, filePath);
    }

    /// <summary>
    /// Returns processes bound to the specified port using <c>lsof -i</c>.
    /// </summary>
    /// <param name="port">The port number to check.</param>
    /// <returns>A list of <see cref="LockInfo"/> for each process binding the port.</returns>
    public static List<LockInfo> FindPort(int port)
    {
        // lsof -i :<port>
        string output = RunLsof(new[] { "-i", $":{port}" });
        return ParsePortOutput(output, port);
    }

    private static string RunLsof(string[] arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "lsof",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string arg in arguments)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            process.Start();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10000);
            return stdout;
        }
        catch
        {
            return "";
        }
    }

    private static List<LockInfo> ParseOutput(string output, string resource)
    {
        var results = new List<LockInfo>();
        var seen = new HashSet<int>();

        if (string.IsNullOrWhiteSpace(output))
        {
            return results;
        }

        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Skip header row
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // Columns: COMMAND PID USER FD TYPE DEVICE SIZE/OFF NODE NAME
            string[] parts = line.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            string command = parts[0];
            if (int.TryParse(parts[1], out int pid) && seen.Add(pid))
            {
                results.Add(new LockInfo(pid, command, resource));
            }
        }

        return results;
    }

    private static List<LockInfo> ParsePortOutput(string output, int port)
    {
        var results = new List<LockInfo>();
        var seen = new HashSet<int>();

        if (string.IsNullOrWhiteSpace(output))
        {
            return results;
        }

        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Skip header row
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // Columns: COMMAND PID USER FD TYPE DEVICE SIZE/OFF NODE NAME
            string[] parts = line.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 9)
            {
                continue;
            }

            string command = parts[0];
            // NODE column (index 7) contains the protocol: TCP, UDP
            string protocol = parts[7].ToUpperInvariant();
            if (protocol != "TCP" && protocol != "UDP")
            {
                protocol = "TCP"; // fallback
            }

            if (int.TryParse(parts[1], out int pid) && seen.Add(pid))
            {
                results.Add(new LockInfo(pid, command, $"{protocol} :{port}"));
            }
        }

        return results;
    }
}
```

### Step 5.2 — Commit

No unit tests for LsofFinder — it requires actual `lsof` on the system and is tested manually on Linux/macOS. The parsing logic is straightforward string splitting.

```bash
git add src/Winix.WhoHolds/LsofFinder.cs
git commit -m "feat(whoholds): implement LsofFinder for Linux/macOS lsof delegation"
```

---

## Task 6: ElevationDetector

### Step 6.1 — Implement ElevationDetector

Create file `src/Winix.WhoHolds/ElevationDetector.cs`:

```csharp
namespace Winix.WhoHolds;

/// <summary>
/// Checks whether the current process is running with elevated (admin/root) privileges.
/// Used to display a warning when results may be incomplete.
/// </summary>
public static class ElevationDetector
{
    /// <summary>
    /// Returns true if the process is running with admin/root privileges.
    /// On Windows: checks <see cref="Environment.IsPrivilegedProcess"/>.
    /// On Linux/macOS: checks <see cref="Environment.IsPrivilegedProcess"/> (euid == 0).
    /// </summary>
    public static bool IsElevated()
    {
        return Environment.IsPrivilegedProcess;
    }
}
```

### Step 6.2 — Commit

`Environment.IsPrivilegedProcess` (.NET 8+) handles the cross-platform check: `WindowsIdentity`+`WindowsPrincipal` on Windows, `geteuid() == 0` on Unix. No test needed — it is a single property call with no branching logic.

```bash
git add src/Winix.WhoHolds/ElevationDetector.cs
git commit -m "feat(whoholds): implement ElevationDetector using Environment.IsPrivilegedProcess"
```

---

## Task 7: Formatting

### Step 7.1 — Write Formatting tests

Create file `tests/Winix.WhoHolds.Tests/FormattingTests.cs`:

```csharp
using Winix.WhoHolds;

namespace Winix.WhoHolds.Tests;

public sealed class FormattingTests
{
    [Fact]
    public void FormatTable_SingleResult_ContainsPidAndName()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "devenv.exe", @"D:\test.dll")
        };

        string output = Formatting.FormatTable(results, useColor: false);

        Assert.Contains("1234", output);
        Assert.Contains("devenv.exe", output);
        Assert.Contains(@"D:\test.dll", output);
    }

    [Fact]
    public void FormatTable_MultipleResults_ContainsAllPids()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "devenv.exe", @"D:\test.dll"),
            new LockInfo(5678, "dotnet.exe", @"D:\test.dll")
        };

        string output = Formatting.FormatTable(results, useColor: false);

        Assert.Contains("1234", output);
        Assert.Contains("5678", output);
        Assert.Contains("devenv.exe", output);
        Assert.Contains("dotnet.exe", output);
    }

    [Fact]
    public void FormatTable_WithColor_ContainsAnsiEscapes()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "node.exe", "TCP :8080")
        };

        string output = Formatting.FormatTable(results, useColor: true);

        Assert.Contains("\x1b[", output);
    }

    [Fact]
    public void FormatTable_NoColor_NoAnsiEscapes()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "node.exe", "TCP :8080")
        };

        string output = Formatting.FormatTable(results, useColor: false);

        Assert.DoesNotContain("\x1b[", output);
    }

    [Fact]
    public void FormatPidOnly_SingleResult_OnePidPerLine()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "devenv.exe", @"D:\test.dll")
        };

        string output = Formatting.FormatPidOnly(results);

        Assert.Equal("1234", output.TrimEnd());
    }

    [Fact]
    public void FormatPidOnly_MultipleResults_OnePidPerLine()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "devenv.exe", @"D:\test.dll"),
            new LockInfo(5678, "dotnet.exe", @"D:\test.dll")
        };

        string output = Formatting.FormatPidOnly(results);
        string[] lines = output.TrimEnd().Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.Equal("1234", lines[0].TrimEnd());
        Assert.Equal("5678", lines[1].TrimEnd());
    }

    [Fact]
    public void FormatElevationWarning_WithColor_ContainsYellowEscape()
    {
        string output = Formatting.FormatElevationWarning(useColor: true);

        Assert.Contains("\x1b[33m", output); // yellow
        Assert.Contains("Not elevated", output);
    }

    [Fact]
    public void FormatElevationWarning_NoColor_PlainText()
    {
        string output = Formatting.FormatElevationWarning(useColor: false);

        Assert.DoesNotContain("\x1b[", output);
        Assert.Contains("Not elevated", output);
    }

    [Fact]
    public void FormatNoResults_ContainsResource()
    {
        string output = Formatting.FormatNoResults(@"D:\test.dll");

        Assert.Contains(@"D:\test.dll", output);
        Assert.Contains("No processes found", output);
    }

    [Fact]
    public void FormatNoResults_Port_ContainsPort()
    {
        string output = Formatting.FormatNoResults(":8080");

        Assert.Contains(":8080", output);
    }

    [Fact]
    public void FormatTable_PortResult_ShowsProtocol()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(4321, "node.exe", "TCP :8080")
        };

        string output = Formatting.FormatTable(results, useColor: false);

        Assert.Contains("TCP :8080", output);
    }

    [Fact]
    public void FormatJson_ReturnsValidJson()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "devenv.exe", @"D:\test.dll")
        };

        string json = Formatting.FormatJson(results, 0, "success", "whoholds", "1.0.0");

        Assert.Contains("\"tool\":\"whoholds\"", json);
        Assert.Contains("\"version\":\"1.0.0\"", json);
        Assert.Contains("\"exit_code\":0", json);
        Assert.Contains("\"processes\":", json);
        Assert.Contains("\"pid\":1234", json);
        Assert.Contains("\"name\":\"devenv.exe\"", json);
    }

    [Fact]
    public void FormatJsonError_ReturnsValidJson()
    {
        string json = Formatting.FormatJsonError(1, "file_not_found", "whoholds", "1.0.0");

        Assert.Contains("\"tool\":\"whoholds\"", json);
        Assert.Contains("\"exit_code\":1", json);
        Assert.Contains("\"exit_reason\":\"file_not_found\"", json);
    }
}
```

### Step 7.2 — Verify tests fail

```bash
dotnet test tests/Winix.WhoHolds.Tests/Winix.WhoHolds.Tests.csproj
```

### Step 7.3 — Implement Formatting

Create file `src/Winix.WhoHolds/Formatting.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Yort.ShellKit;

namespace Winix.WhoHolds;

/// <summary>
/// Output formatting for whoholds — human-readable tables, PID-only, JSON, and warnings.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Formats results as a human-readable table with PID, process name, and resource columns.
    /// </summary>
    /// <param name="results">The lock query results.</param>
    /// <param name="useColor">Whether to include ANSI colour escapes.</param>
    /// <returns>A formatted table string (no trailing newline).</returns>
    public static string FormatTable(IReadOnlyList<LockInfo> results, bool useColor)
    {
        string dim = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);

        // Calculate column widths
        int maxPid = 5; // minimum "PID" header width + padding
        int maxName = 7; // minimum "Process" header width
        foreach (LockInfo info in results)
        {
            int pidLen = info.ProcessId.ToString().Length;
            if (pidLen > maxPid) { maxPid = pidLen; }
            if (info.ProcessName.Length > maxName) { maxName = info.ProcessName.Length; }
        }

        var sb = new StringBuilder();

        // Header
        sb.Append($"  {dim}{"PID".PadRight(maxPid + 2)}{"Process".PadRight(maxName + 2)}Resource{reset}");

        // Rows
        foreach (LockInfo info in results)
        {
            sb.AppendLine();
            string pidStr = info.ProcessId.ToString().PadRight(maxPid + 2);
            string nameStr = info.ProcessName.PadRight(maxName + 2);
            sb.Append($"  {pidStr}{nameStr}{info.Resource}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats results as one PID per line (for piping).
    /// </summary>
    /// <param name="results">The lock query results.</param>
    /// <returns>One PID per line (no trailing newline).</returns>
    public static string FormatPidOnly(IReadOnlyList<LockInfo> results)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < results.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
            }
            sb.Append(results[i].ProcessId);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Formats the elevation warning shown on stderr when not running as admin.
    /// </summary>
    /// <param name="useColor">Whether to include ANSI colour escapes.</param>
    /// <returns>The warning string (no trailing newline).</returns>
    public static string FormatElevationWarning(bool useColor)
    {
        string yellow = AnsiColor.Yellow(useColor);
        string reset = AnsiColor.Reset(useColor);
        return $"{yellow}\u26a0{reset} Not elevated \u2014 only showing current user's processes.";
    }

    /// <summary>
    /// Formats the "no results" message.
    /// </summary>
    /// <param name="resource">The queried resource (file path or port string).</param>
    /// <returns>The message string (no trailing newline).</returns>
    public static string FormatNoResults(string resource)
    {
        return $"No processes found holding {resource}";
    }

    /// <summary>
    /// Formats results as a JSON object following Winix CLI conventions.
    /// </summary>
    /// <param name="results">The lock query results.</param>
    /// <param name="exitCode">Tool exit code.</param>
    /// <param name="exitReason">Machine-readable exit reason.</param>
    /// <param name="toolName">Tool name ("whoholds").</param>
    /// <param name="version">Tool version string.</param>
    /// <returns>A JSON string.</returns>
    public static string FormatJson(IReadOnlyList<LockInfo> results, int exitCode, string exitReason, string toolName, string version)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            writer.WriteNumber("count", results.Count);

            writer.WriteStartArray("processes");
            foreach (LockInfo info in results)
            {
                writer.WriteStartObject();
                writer.WriteNumber("pid", info.ProcessId);
                writer.WriteString("name", info.ProcessName);
                writer.WriteString("resource", info.Resource);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Formats an error as a JSON object following Winix CLI conventions.
    /// </summary>
    /// <param name="exitCode">Tool exit code.</param>
    /// <param name="exitReason">Machine-readable exit reason.</param>
    /// <param name="toolName">Tool name ("whoholds").</param>
    /// <param name="version">Tool version string.</param>
    /// <returns>A JSON string.</returns>
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
            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }
}
```

### Step 7.4 — Verify tests pass

```bash
dotnet test tests/Winix.WhoHolds.Tests/Winix.WhoHolds.Tests.csproj
```

### Step 7.5 — Commit

```bash
git add src/Winix.WhoHolds/Formatting.cs tests/Winix.WhoHolds.Tests/FormattingTests.cs
git commit -m "feat(whoholds): implement output formatting (table, PID-only, JSON, warnings)"
```

---

## Task 8: Console App (Program.cs)

Wire everything together in the thin console app entry point.

### Step 8.1 — Implement full Program.cs

Replace the placeholder `src/whoholds/Program.cs` with the full implementation:

```csharp
using System.Reflection;
using Winix.WhoHolds;
using Yort.ShellKit;

namespace WhoHolds;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        string version = GetVersion();

        var parser = new CommandLineParser("whoholds", version)
            .Description("Find which processes are holding a file lock or binding a network port.")
            .StandardFlags()
            .Positional("<file-or-port>")
            .Flag("--pid-only", "Force one-PID-per-line output (auto when piped)")
            .Platform("cross-platform",
                new[] { "handle.exe", "lsof" },
                "Windows has no built-in CLI for file/port locks — Resource Monitor is GUI-only, netstat requires PID cross-referencing",
                "Unified syntax for both files and ports; lsof delegation with clean output")
            .StdinDescription("Not used")
            .StdoutDescription("Table of PID / process / resource (terminal). One PID per line (piped or --pid-only).")
            .StderrDescription("Elevation warning, errors, and --json output.")
            .Example("whoholds myfile.dll", "Find what's locking a file")
            .Example("whoholds :8080", "Find what's binding port 8080")
            .Example("whoholds 8080", "Port lookup (if no file named '8080' exists)")
            .Example("whoholds myfile.dll --pid-only", "PIDs only (one per line)")
            .Example("whoholds :3000 --json", "JSON output for scripting")
            .Example("whoholds myfile.dll --pid-only | wargs taskkill /F /PID", "Kill all processes locking a file")
            .ComposesWith("wargs", "whoholds myfile.dll --pid-only | wargs taskkill /F /PID", "Find and kill processes locking a file")
            .JsonField("tool", "string", "Tool name (\"whoholds\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("exit_code", "int", "Tool exit code (0 = success)")
            .JsonField("exit_reason", "string", "Machine-readable exit reason")
            .JsonField("count", "int", "Number of processes found")
            .JsonField("processes", "array", "Array of {pid, name, resource} objects")
            .ExitCodes(
                (0, "Success (results found, or no results but query succeeded)"),
                (1, "Error (file not found, API failure, unsupported platform)"),
                (ExitCode.UsageError, "Usage error (bad arguments)"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        bool pidOnly = result.Has("--pid-only");
        bool jsonOutput = result.Has("--json");
        bool useColor = result.ResolveColor(checkStdErr: true);

        // Require exactly one positional argument
        if (result.Positionals.Length == 0)
        {
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "whoholds", version));
            }
            else
            {
                Console.Error.WriteLine("whoholds: no resource specified. Run 'whoholds --help' for usage.");
            }
            return ExitCode.UsageError;
        }

        if (result.Positionals.Length > 1)
        {
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "whoholds", version));
            }
            else
            {
                Console.Error.WriteLine("whoholds: expected one argument (file path or :port). Run 'whoholds --help' for usage.");
            }
            return ExitCode.UsageError;
        }

        // Parse the argument
        string argument = result.Positionals[0];
        ParsedArgument parsed = ArgumentParser.Parse(argument);

        if (parsed.IsError)
        {
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(1, "argument_error", "whoholds", version));
            }
            else
            {
                Console.Error.WriteLine($"whoholds: {parsed.ErrorMessage}");
            }
            return 1;
        }

        // Show elevation warning on stderr (always, when not elevated)
        if (!ElevationDetector.IsElevated())
        {
            // Use stderr colour — resolve from NO_COLOR and --color/--no-color
            bool stderrColor = result.ResolveColor(checkStdErr: true);
            Console.Error.WriteLine(Formatting.FormatElevationWarning(stderrColor));
        }

        // Execute the query
        List<LockInfo> results;
        string resourceDisplay;

        if (parsed.IsFile)
        {
            resourceDisplay = parsed.FilePath!;
            results = FindFileHolders(parsed.FilePath!);
        }
        else
        {
            resourceDisplay = $":{parsed.Port}";
            results = FindPortHolders(parsed.Port);
        }

        // Output results
        if (jsonOutput)
        {
            Console.Error.WriteLine(Formatting.FormatJson(results, 0, "success", "whoholds", version));
        }
        else if (results.Count == 0)
        {
            Console.Error.WriteLine(Formatting.FormatNoResults(resourceDisplay));
        }
        else if (pidOnly || Console.IsOutputRedirected)
        {
            Console.Out.WriteLine(Formatting.FormatPidOnly(results));
        }
        else
        {
            Console.Out.WriteLine(Formatting.FormatTable(results, useColor));
        }

        return 0;
    }

    /// <summary>
    /// Finds processes holding a file lock. Uses Restart Manager on Windows, lsof on Unix.
    /// </summary>
    private static List<LockInfo> FindFileHolders(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return FileLockFinder.Find(filePath);
        }

        if (LsofFinder.IsAvailable())
        {
            return LsofFinder.FindFile(filePath);
        }

        Console.Error.WriteLine("whoholds: lsof not found — install it via your package manager");
        return new List<LockInfo>();
    }

    /// <summary>
    /// Finds processes binding a port. Uses IP Helper on Windows, lsof on Unix.
    /// </summary>
    private static List<LockInfo> FindPortHolders(int port)
    {
        if (OperatingSystem.IsWindows())
        {
            return PortLockFinder.Find(port);
        }

        if (LsofFinder.IsAvailable())
        {
            return LsofFinder.FindPort(port);
        }

        Console.Error.WriteLine("whoholds: lsof not found — install it via your package manager");
        return new List<LockInfo>();
    }

    /// <summary>
    /// Returns the informational version from the Winix.WhoHolds library assembly.
    /// </summary>
    private static string GetVersion()
    {
        return typeof(LockInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
```

### Step 8.2 — Build and verify

```bash
dotnet build Winix.sln
dotnet test tests/Winix.WhoHolds.Tests/Winix.WhoHolds.Tests.csproj
```

### Step 8.3 — Commit

```bash
git add src/whoholds/Program.cs
git commit -m "feat(whoholds): implement full console app with all CLI flags"
```

---

## Task 9: Integration Tests — Self-locking and Self-binding

Verify the full stack works end-to-end (Windows-only).

### Step 9.1 — The integration tests were already created in Tasks 3 and 4

The tests in `FileLockFinderTests.cs` and `PortLockFinderTests.cs` are the integration tests. They:

- Create a temp file and hold a `FileStream` lock, then verify `FileLockFinder.Find` returns the current PID.
- Bind a `TcpListener` on a random port, then verify `PortLockFinder.Find` returns the current PID.
- Bind a `UdpClient` and verify UDP detection.

### Step 9.2 — Run full test suite

```bash
dotnet test Winix.sln
```

All tests must pass.

### Step 9.3 — Commit (if any fixes needed)

```bash
git add -A
git commit -m "fix(whoholds): integration test fixes"
```

Only commit if changes were made.

---

## Task 10: README, AI Guide, Pipeline, and CLAUDE.md Updates

### Step 10.1 — Complete the README

Replace `src/whoholds/README.md` with the full documentation:

```markdown
# whoholds

Find which processes are holding a file lock or binding a network port.

**Answers the two most common "who's using this?" questions:**
- "Why can't I delete/rename this file?" -> `whoholds myfile.dll`
- "What's listening on port 8080?" -> `whoholds :8080`

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/whoholds
```

### Winget (Windows, stable releases)

```bash
winget install Winix.WhoHolds
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.WhoHolds
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
whoholds <file-or-port> [options]
```

The argument is auto-detected as a file path or port number:
- If it starts with `:` -> port lookup (explicit)
- If it exists on disk -> file lookup
- If it is a bare integer and no such file exists -> port lookup
- Otherwise -> error

### Examples

```bash
# Find what's locking a file
whoholds myfile.dll

# Find what's binding port 8080
whoholds :8080

# Port lookup (if no file named "8080" exists)
whoholds 8080

# PIDs only (one per line, for piping)
whoholds myfile.dll --pid-only

# JSON output for scripting
whoholds :3000 --json

# Kill all processes locking a file
whoholds myfile.dll --pid-only | wargs taskkill /F /PID
```

## Options

| Flag | Description |
|------|-------------|
| `--pid-only` | Force one-PID-per-line output (auto when piped) |
| `--color` | Force coloured output |
| `--no-color` | Disable coloured output |
| `--json` | Structured JSON output to stderr |
| `--describe` | AI agent metadata |
| `-h`, `--help` | Show help |
| `--version` | Show version |

## Output

**Terminal (stdout is a tty):**
```
Warning: Not elevated - only showing current user's processes.
  PID   Process          Resource
  1234  devenv.exe       D:\projects\winix\bin\tool.dll
  5678  dotnet.exe       D:\projects\winix\bin\tool.dll
```

**Piped (stdout is not a tty) or --pid-only:**
```
1234
5678
```

One PID per line. Warning still goes to stderr.

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success (results found, or no results but query succeeded) |
| 1 | Error (file not found, API failure, unsupported platform) |
| 125 | Usage error (bad arguments) |

## Colour

Colour is enabled by default when stdout is a terminal. Controlled by:
- `--color` / `--no-color` flags (highest priority)
- `NO_COLOR` environment variable (see https://no-color.org)

## Platform Details

**Windows:** Uses native APIs via P/Invoke — Restart Manager (rstrtmgr.dll) for file locks, IP Helper (iphlpapi.dll) for port bindings. No admin required for the common case (current user's processes).

**Linux/macOS:** Delegates to `lsof` for both file and port lookups. Most distros include lsof by default.
```

### Step 10.2 — Create AI agent guide

Create file `docs/ai/whoholds.md`:

```markdown
# whoholds -- AI Agent Guide

## What This Tool Does

`whoholds` shows which processes are holding a file lock or binding a network port. Auto-detects whether the argument is a file path or port number.

## Platform Story

Cross-platform. On **Windows**, uses native APIs (Restart Manager for files, IP Helper for ports) via P/Invoke -- no admin required. On **Linux/macOS**, delegates to `lsof`.

## When to Use This

- A file operation fails with "file is in use" -- find which process holds it
- A port is already bound and you need to find the listener
- Before killing processes locking a resource (compose with `wargs`)
- Diagnosing "address already in use" errors in dev environments

## Common Patterns

**Find what's locking a file:**
```bash
whoholds myfile.dll
```

**Find what's on port 8080:**
```bash
whoholds :8080
```

**Port lookup by bare number:**
```bash
whoholds 8080
```

**JSON output:**
```bash
whoholds :3000 --json
```

**Kill all processes locking a file:**
```bash
whoholds myfile.dll --pid-only | wargs taskkill /F /PID
```

## Composing with Other Tools

**whoholds + wargs** -- find and kill:
```bash
whoholds myfile.dll --pid-only | wargs taskkill /F /PID
```

**whoholds + peep** -- watch for port binding changes:
```bash
peep -- whoholds :8080
```

## Important Notes for Agents

- Auto-detects file vs port: `:8080` is always a port, `myfile.dll` is always a file, bare `8080` checks disk first then falls back to port
- When piped (stdout is not a tty), output is one PID per line automatically
- `--pid-only` forces PID-only output even on a terminal
- Exit code 0 even when no results (query succeeded, resource just isn't locked)
- Elevation warning on stderr -- results may be incomplete without admin/root
- `--json` writes to stderr (standard Winix convention)
```

### Step 10.3 — Create Scoop manifest

Create file `bucket/whoholds.json`:

```json
{
  "version": "0.1.0",
  "description": "Find which processes are holding a file lock or binding a network port.",
  "homepage": "https://github.com/Yortw/winix",
  "license": "MIT",
  "architecture": {
    "64bit": {
      "url": "https://github.com/Yortw/winix/releases/download/v0.1.0/whoholds-win-x64.zip",
      "hash": "0000000000000000000000000000000000000000000000000000000000000000",
      "bin": "whoholds.exe"
    }
  },
  "checkver": "github",
  "autoupdate": {
    "architecture": {
      "64bit": {
        "url": "https://github.com/Yortw/winix/releases/download/v$version/whoholds-win-x64.zip"
      }
    }
  }
}
```

### Step 10.4 — Update `bucket/winix.json` bin array

Add `"whoholds.exe"` to the `bin` array in `bucket/winix.json`. The exact location depends on the existing format -- the `winix.json` uses a single `"bin"` string rather than an array, so this step should add a `"bin"` array that includes the existing `winix.exe` plus `whoholds.exe`. However, the `winix.json` scoop manifest's `bin` field only contains `winix.exe` because the winix installer manages individual tools. **Do not** modify `winix.json`'s bin field -- the installer handles individual tools via `winix install`. Instead, just confirm the standalone `bucket/whoholds.json` is correct.

### Step 10.5 — Update `llms.txt`

Add the whoholds entry to `llms.txt`, in alphabetical order among the tools list:

Add after the `less` line:
```
- [whoholds](docs/ai/whoholds.md): Find which processes are holding a file lock or binding a network port. Replaces `handle.exe` / `lsof` with a unified CLI.
```

### Step 10.6 — Update `CLAUDE.md` project layout

Add the whoholds entries to the project layout section in `CLAUDE.md`:

```
src/Winix.WhoHolds/        — class library (finders, formatting, argument parsing)
src/whoholds/               — console app entry point
tests/Winix.WhoHolds.Tests/ — xUnit tests
```

Also add to the NuGet package IDs list: `Winix.WhoHolds`.

### Step 10.7 — Update `.github/workflows/release.yml`

Add whoholds to every tool list in the release pipeline:

1. **pack-nuget job:** Add a "Pack whoholds" step:
   ```yaml
         - name: Pack whoholds
           run: dotnet pack src/whoholds/whoholds.csproj -c Release -o packages -p:Version=${{ needs.resolve-version.outputs.version }}
   ```

2. **publish-aot job:** Add a "Publish whoholds" step:
   ```yaml
         - name: Publish whoholds
           run: dotnet publish src/whoholds/whoholds.csproj -c Release -r ${{ matrix.rid }} -p:Version=${{ needs.resolve-version.outputs.version }}
   ```

3. **Zip binaries (Linux/macOS):** Add:
   ```bash
   cd src/whoholds/bin/Release/net10.0/${{ matrix.rid }}/publish && zip -j $GITHUB_WORKSPACE/whoholds-${{ matrix.rid }}.zip * && cd $GITHUB_WORKSPACE
   ```

4. **Zip binaries (Windows):** Add:
   ```powershell
   Compress-Archive -Path src/whoholds/bin/Release/net10.0/${{ matrix.rid }}/publish/* -DestinationPath whoholds-${{ matrix.rid }}.zip
   ```

5. **Create combined Winix zip (Windows):** Add:
   ```powershell
   Copy-Item src/whoholds/bin/Release/net10.0/${{ matrix.rid }}/publish/whoholds.exe winix-combined/
   ```

6. **Generate winix-manifest.json:** Add whoholds to the tools object:
   ```
   whoholds: { description: "Find which processes are holding a file lock or binding a network port.", packages: { winget: "Winix.WhoHolds", scoop: "whoholds", brew: "whoholds", dotnet: "Winix.WhoHolds" } }
   ```

7. **update-scoop-bucket job:** Add:
   ```bash
   update_manifest bucket/whoholds.json aot/whoholds-win-x64.zip
   ```

8. **generate-winget-manifests job:** Add:
   ```bash
   generate_manifests "whoholds" "WhoHolds" "Find which processes are holding a file lock or binding a network port."
   ```

### Step 10.8 — Commit

```bash
git add src/whoholds/README.md docs/ai/whoholds.md bucket/whoholds.json llms.txt CLAUDE.md .github/workflows/release.yml
git commit -m "docs(whoholds): add README, AI guide, scoop manifest, update pipeline"
```

---

## Task 11: Architecture Decision Record

### Step 11.1 — Create ADR

Create file `docs/plans/2026-04-12-whoholds-adr.md`:

```markdown
# ADR: whoholds Architecture Decisions

**Date:** 2026-04-12
**Status:** Proposed
**Context:** Implementation of the `whoholds` tool for the Winix CLI suite.
**Related design:** `docs/plans/2026-04-12-whoholds-design.md`

---

## Decision 1: Restart Manager API for file lock detection

### Context
Windows has no simple CLI or API to answer "which process holds this file?" Options include:
- Restart Manager API (rstrtmgr.dll)
- NtQuerySystemInformation (ntdll.dll)
- Performance counters

### Decision
Use the Restart Manager API via P/Invoke.

### Rationale
- Does not require admin privileges for the common case (current user's session)
- Designed for exactly this purpose (detecting processes holding resources)
- Well-documented, stable Win32 API
- Simple session-based lifecycle (start, register, query, end)

### Trade-offs Accepted
- Only sees processes in the current user's session (not system-wide)
- Cannot detect handles held by services running as SYSTEM
- NtQuerySystemInformation could see all handles but requires admin and undocumented structures

## Decision 2: IP Helper API for port detection

### Context
Need to find which process is bound to a specific TCP/UDP port. Options include:
- IP Helper API (iphlpapi.dll) — GetExtendedTcpTable / GetExtendedUdpTable
- netstat parsing
- WMI queries

### Decision
Use IP Helper API via P/Invoke.

### Rationale
- Direct system call, no process spawning overhead
- Returns PID directly (no cross-referencing needed)
- Covers both TCP and UDP, both IPv4 and IPv6
- Does not require admin
- AOT-compatible via DllImport with explicit struct layouts

### Trade-offs Accepted
- Separate struct definitions for IPv4 and IPv6 tables (code duplication)
- Network byte order conversion needed for port numbers

## Decision 3: lsof delegation on Linux/macOS

### Context
On Unix platforms, we need the same file and port lock detection. Options include:
- Direct /proc filesystem parsing (Linux only)
- Native syscalls via P/Invoke
- Delegate to lsof

### Decision
Delegate to `lsof` and parse its output.

### Rationale
- lsof is available by default on most Linux distros and macOS
- Handles both files and ports with simple flag differences
- Same delegation pattern used by `winix` for native package managers
- Avoids platform-specific /proc parsing that varies between Linux and macOS
- Simpler to maintain than cross-platform syscall P/Invoke

### Trade-offs Accepted
- Depends on external tool being on PATH
- Output parsing is fragile if lsof format changes (unlikely — format is stable)
- Process spawning overhead vs direct syscalls

## Decision 4: Auto-detection of file vs port argument

### Context
The tool takes a single positional argument that could be a file path or a port number.

### Decision
Resolution order: `:` prefix -> file existence check -> bare integer -> error.

### Rationale
- `:8080` is unambiguous — always a port
- File existence check catches the common case without user needing to think
- Bare integers fall through to port (most common intent for bare numbers)
- The `:` escape hatch handles the rare case of numeric filenames

### Trade-offs Accepted
- A file named "8080" that doesn't exist yet would be interpreted as a port
- Requires `:` prefix for explicit port when ambiguity exists

## Decision 5: Environment.IsPrivilegedProcess for elevation check

### Context
Need to detect admin/root to show the elevation warning.

### Decision
Use `Environment.IsPrivilegedProcess` (.NET 8+).

### Rationale
- Single property call, cross-platform
- Handles WindowsIdentity/WindowsPrincipal on Windows, geteuid on Unix
- AOT-compatible
- No P/Invoke needed

### Trade-offs Accepted
- Requires .NET 8+ (already our minimum via .NET 10)

---

## Decisions Explicitly Deferred

| Topic | Why Deferred |
|-------|-------------|
| `--system` flag (NtQuerySystemInformation) | Requires admin, undocumented API — v2 feature |
| Named pipes, mutexes, registry locks | Rarely needed, significant complexity |
| Multiple file arguments / glob patterns | Keep v1 simple — single resource only |
| Killing processes directly | Composable: `whoholds ... --pid-only \| wargs taskkill /PID` |
| /proc filesystem parsing on Linux | lsof delegation is simpler and covers macOS too |
```

### Step 11.2 — Commit

```bash
git add docs/plans/2026-04-12-whoholds-adr.md
git commit -m "docs: add whoholds architecture decision record"
```

---

## Task 12: Final Verification

### Step 12.1 — Build entire solution

```bash
dotnet build Winix.sln
```

Must build with zero warnings (TreatWarningsAsErrors is on).

### Step 12.2 — Run full test suite

```bash
dotnet test Winix.sln
```

All tests must pass.

### Step 12.3 — AOT publish (Windows)

```bash
dotnet publish src/whoholds/whoholds.csproj -c Release -r win-x64
```

Must produce a native binary without AOT warnings.

### Step 12.4 — Smoke test

```bash
# Help
src/whoholds/bin/Release/net10.0/win-x64/publish/whoholds.exe --help

# Describe (AI metadata)
src/whoholds/bin/Release/net10.0/win-x64/publish/whoholds.exe --describe

# Version
src/whoholds/bin/Release/net10.0/win-x64/publish/whoholds.exe --version

# File lock test — lock a temp file, query it
# (manual: open a file in another process, then run whoholds on it)

# Port test
src/whoholds/bin/Release/net10.0/win-x64/publish/whoholds.exe :80

# Non-existent file
src/whoholds/bin/Release/net10.0/win-x64/publish/whoholds.exe nosuchfile.xyz

# JSON output
src/whoholds/bin/Release/net10.0/win-x64/publish/whoholds.exe :80 --json
```

### Step 12.5 — Final commit (if any fixes)

```bash
git add -A
git commit -m "fix(whoholds): final verification fixes"
```

Only commit if changes were made during verification.
