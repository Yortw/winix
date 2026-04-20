# protect / unprotect Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `protect` and `unprotect`, paired cross-platform CLIs for encrypt-at-rest using native OS key-storage primitives. Windows uses raw DPAPI; macOS uses AES-256-GCM with a Keychain-stored key; Linux uses AES-256-GCM with a libsecret-stored key. Zero user key management.

**Architecture:** Two class libraries. `Winix.SecretStore` (new shared) abstracts named key-value storage via `advapi32` CredRead/Write/Delete on Windows, `security` CLI on macOS, `secret-tool` CLI on Linux. `Winix.Protect` owns the CLI shape: `ArgParser`, `QrOptions`-equivalent `ProtectOptions`, backends (`DpapiBackend` on Windows, `AeadKeychainBackend` on macOS, `AeadLibsecretBackend` on Linux), stream orchestration (`ChunkWriter`/`ChunkReader`), in-place safety (`InPlaceExecutor` + `RoundTripVerifier`). Two thin console apps (`protect`, `unprotect`) both reference the library; `Cli.Run(args, invocationName)` lives in the library and dispatches Protect vs Unprotect from the invocation name.

**Tech Stack:** .NET 10, AOT-compiled, xUnit, nullable reference types, warnings-as-errors. `System.Security.Cryptography.ProtectedData` package for DPAPI. `System.Security.Cryptography.AesGcm` (BCL) for AEAD. `System.Security.Cryptography.IncrementalHash` for round-trip SHA-256. Classic `advapi32.dll` P/Invoke (NOT WinRT PasswordVault — that requires MSIX packaging). Shell-out to `security` / `secret-tool` for Mac/Linux secret stores. Project conventions: file-level `#nullable enable`, full braces, no range/index expressions, warnings-as-errors.

**Reference docs:**
- Design: `docs/plans/2026-04-20-protect-design.md`
- ADR: `docs/plans/2026-04-20-protect-adr.md`
- Most-recent comparable tool (qr): `src/Winix.Qr/`, `src/Winix.QrCode/`, `src/qr/`, `tests/Winix.Qr.Tests/`
- Subcommand / library-entry-point precedents: `src/Winix.Url/ArgParser.cs`, `src/url/Program.cs`
- Shared-lib precedent (extracted-on-day-one): `src/Winix.QrCode/`
- Suite-wide conventions: `CLAUDE.md` at repo root
- DPAPI docs: https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata
- advapi32 Credential Management: https://learn.microsoft.com/en-us/windows/win32/api/wincred/
- secret-tool: `man secret-tool` (libsecret-tools package)

---

## Task 1: Project scaffolding

Create `Winix.SecretStore` (shared lib), `Winix.Protect` (tool-local lib), `protect` + `unprotect` console apps, and both test projects. Wire into the solution. Stub `Cli.Run` returning exit 125.

**Files:**
- Create: `src/Winix.SecretStore/Winix.SecretStore.csproj`
- Create: `src/Winix.Protect/Winix.Protect.csproj`
- Create: `src/Winix.Protect/Cli.cs`
- Create: `src/protect/protect.csproj`
- Create: `src/protect/Program.cs`
- Create: `src/protect/README.md` (placeholder)
- Create: `src/protect/man/man1/protect.1` (placeholder)
- Create: `src/unprotect/unprotect.csproj`
- Create: `src/unprotect/Program.cs`
- Create: `src/unprotect/README.md` (placeholder)
- Create: `src/unprotect/man/man1/unprotect.1` (placeholder)
- Create: `tests/Winix.SecretStore.Tests/Winix.SecretStore.Tests.csproj`
- Create: `tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj`
- Modify: `Winix.sln`

- [ ] **Step 1: Create `src/Winix.SecretStore/Winix.SecretStore.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
    <IsPackable>false</IsPackable>
    <PackageId>Winix.SecretStore.Library</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Winix.SecretStore.Tests" />
    <InternalsVisibleTo Include="Winix.Protect" />
    <InternalsVisibleTo Include="Winix.Protect.Tests" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `src/Winix.Protect/Winix.Protect.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
    <IsPackable>false</IsPackable>
    <PackageId>Winix.Protect.Library</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
    <ProjectReference Include="..\Winix.SecretStore\Winix.SecretStore.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="9.0.*" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Winix.Protect.Tests" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create `src/Winix.Protect/Cli.cs` stub**

```csharp
#nullable enable
using System;
using Yort.ShellKit;

namespace Winix.Protect;

/// <summary>
/// Entry point invoked by both the <c>protect</c> and <c>unprotect</c> console apps.
/// Dispatches Protect vs Unprotect based on <paramref name="invocationName"/>.
/// </summary>
public static class Cli
{
    /// <summary>Run the CLI. Returns a process exit code.</summary>
    /// <param name="args">Command-line arguments (without argv[0]).</param>
    /// <param name="invocationName">Either "protect" or "unprotect". Determines the subcommand dispatch.</param>
    public static int Run(string[] args, string invocationName)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        Console.Error.WriteLine($"{invocationName}: not yet implemented");
        return ExitCode.UsageError;
    }
}
```

- [ ] **Step 4: Create `src/protect/protect.csproj`**

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
    <ToolCommandName>protect</ToolCommandName>
    <PackageId>Winix.Protect</PackageId>
    <Description>Cross-platform encrypt-at-rest CLI wrapping native OS key-storage primitives (DPAPI on Windows, Keychain on macOS, libsecret on Linux).</Description>
    <PackageTags>cli;command-line;cross-platform;windows;macos;linux;aot;dotnet-tool;winix;encryption;dpapi;keychain;libsecret;at-rest;crypto</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.Protect\Winix.Protect.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Content Include="man\man1\protect.1" CopyToPublishDirectory="PreserveNewest" Link="share\man\man1\protect.1" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Create `src/protect/Program.cs`**

```csharp
#nullable enable
namespace Protect;

internal sealed class Program
{
    static int Main(string[] args) => Winix.Protect.Cli.Run(args, "protect");
}
```

- [ ] **Step 6: Create `src/protect/README.md` placeholder**

```markdown
See [main project README](../../README.md). Full tool README populated in a later commit.
```

- [ ] **Step 7: Create `src/protect/man/man1/protect.1` placeholder**

```
.TH PROTECT 1 "2026" "Winix" "User Commands"
.SH NAME
protect \- cross-platform encrypt-at-rest CLI
.SH SYNOPSIS
.B protect
[\fIOPTIONS\fR] [\fIFILE\fR]
.SH DESCRIPTION
Placeholder; full man page in a later commit.
```

- [ ] **Step 8: Create `src/unprotect/unprotect.csproj`**

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
    <ToolCommandName>unprotect</ToolCommandName>
    <PackageId>Winix.Unprotect</PackageId>
    <Description>Companion tool to protect — decrypts files that protect encrypted, using the same native OS key-storage primitives.</Description>
    <PackageTags>cli;command-line;cross-platform;windows;macos;linux;aot;dotnet-tool;winix;decryption;dpapi;keychain;libsecret;at-rest;crypto</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.Protect\Winix.Protect.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Content Include="man\man1\unprotect.1" CopyToPublishDirectory="PreserveNewest" Link="share\man\man1\unprotect.1" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

- [ ] **Step 9: Create `src/unprotect/Program.cs`**

```csharp
#nullable enable
namespace Unprotect;

internal sealed class Program
{
    static int Main(string[] args) => Winix.Protect.Cli.Run(args, "unprotect");
}
```

- [ ] **Step 10: Create `src/unprotect/README.md` placeholder**

```markdown
See [main project README](../../README.md). Full tool README populated in a later commit.
```

- [ ] **Step 11: Create `src/unprotect/man/man1/unprotect.1` placeholder**

```
.TH UNPROTECT 1 "2026" "Winix" "User Commands"
.SH NAME
unprotect \- decrypt files encrypted by protect
.SH SYNOPSIS
.B unprotect
[\fIOPTIONS\fR] [\fIFILE\fR]
.SH DESCRIPTION
Placeholder; full man page in a later commit.
```

- [ ] **Step 12: Create `tests/Winix.SecretStore.Tests/Winix.SecretStore.Tests.csproj`**

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
    <ProjectReference Include="..\..\src\Winix.SecretStore\Winix.SecretStore.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 13: Create `tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj`**

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
    <ProjectReference Include="..\..\src\Winix.Protect\Winix.Protect.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 14: Add projects to solution**

Run:
```
dotnet sln d:/projects/winix/Winix.sln add src/Winix.SecretStore/Winix.SecretStore.csproj src/Winix.Protect/Winix.Protect.csproj src/protect/protect.csproj src/unprotect/unprotect.csproj tests/Winix.SecretStore.Tests/Winix.SecretStore.Tests.csproj tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj
```

- [ ] **Step 15: Build**

Run: `dotnet build d:/projects/winix/Winix.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 16: Smoke both stubs**

Run: `dotnet run --project d:/projects/winix/src/protect/protect.csproj`
Expected: stderr `protect: not yet implemented`, exit 125.

Run: `dotnet run --project d:/projects/winix/src/unprotect/unprotect.csproj`
Expected: stderr `unprotect: not yet implemented`, exit 125.

- [ ] **Step 17: Commit**

```
git -C d:/projects/winix add src/Winix.SecretStore src/Winix.Protect src/protect src/unprotect tests/Winix.SecretStore.Tests tests/Winix.Protect.Tests Winix.sln
git -C d:/projects/winix commit -m "feat(protect): add project scaffolding for protect/unprotect"
```

---

## Task 2: Core types in `Winix.Protect`

Enums and the `ProtectOptions` record. Pure data — no logic.

**Files:**
- Create: `src/Winix.Protect/SubCommand.cs`
- Create: `src/Winix.Protect/Scope.cs`
- Create: `src/Winix.Protect/PlatformMarker.cs`
- Create: `src/Winix.Protect/ProtectOptions.cs`

- [ ] **Step 1: Create `SubCommand.cs`**

```csharp
#nullable enable
namespace Winix.Protect;

/// <summary>Which operation the tool is performing. Derived from the invocation name (protect vs unprotect).</summary>
public enum SubCommand
{
    Protect,
    Unprotect,
}
```

- [ ] **Step 2: Create `Scope.cs`**

```csharp
#nullable enable
namespace Winix.Protect;

/// <summary>Key-derivation scope. Windows: DPAPI CurrentUser vs LocalMachine. macOS: login vs System Keychain. Linux: user only (machine fails fast).</summary>
public enum Scope
{
    User,
    Machine,
}
```

- [ ] **Step 3: Create `PlatformMarker.cs`**

```csharp
#nullable enable
namespace Winix.Protect;

/// <summary>
/// Platform-marker byte embedded in the .prot file header. Identifies which backend produced the file
/// so <c>unprotect</c> can fail helpfully if a file is moved between platforms or scopes.
/// </summary>
public enum PlatformMarker : byte
{
    WindowsDpapiUser    = 0x01,
    WindowsDpapiMachine = 0x02,
    MacKeychainUser     = 0x10,
    MacKeychainMachine  = 0x11,
    LinuxLibsecretUser  = 0x20,
    // 0x21 reserved for Linux systemd-creds (machine scope, v2).
}
```

- [ ] **Step 4: Create `ProtectOptions.cs`**

```csharp
#nullable enable
namespace Winix.Protect;

/// <summary>Parsed CLI options produced by <see cref="ArgParser"/>.</summary>
public sealed record ProtectOptions(
    SubCommand SubCommand,
    // Input: file path or null for stdin streaming.
    string? InputPath,
    // Output: file path or null for stdout streaming. Mutually exclusive with InPlace.
    string? OutputPath,
    bool InPlace,
    bool RemoveSource,
    Scope Scope,
    bool NoVerify);
```

- [ ] **Step 5: Build**

Run: `dotnet build d:/projects/winix/src/Winix.Protect/Winix.Protect.csproj`
Expected: 0 warnings, 0 errors. (`ArgParser` cref is an XML doc forward reference — does not fail the build.)

- [ ] **Step 6: Commit**

```
git -C d:/projects/winix add src/Winix.Protect/SubCommand.cs src/Winix.Protect/Scope.cs src/Winix.Protect/PlatformMarker.cs src/Winix.Protect/ProtectOptions.cs
git -C d:/projects/winix commit -m "feat(protect): add SubCommand, Scope, PlatformMarker, ProtectOptions"
```

---

## Task 3: Header read/write

Pure functions: `Header.Write(Stream, PlatformMarker)` and `Header.Read(Stream) → (byte version, PlatformMarker)`. 6 bytes total: 4-byte magic `"WPRT"`, 1-byte version `0x01`, 1-byte platform marker.

**Files:**
- Create: `src/Winix.Protect/Header.cs`
- Create: `tests/Winix.Protect.Tests/HeaderTests.cs`

- [ ] **Step 1: Write failing tests at `tests/Winix.Protect.Tests/HeaderTests.cs`**

```csharp
#nullable enable
using System;
using System.IO;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

public class HeaderTests
{
    [Fact]
    public void Write_EmitsMagicVersionAndMarker()
    {
        using MemoryStream stream = new();
        Header.Write(stream, PlatformMarker.WindowsDpapiUser);
        byte[] bytes = stream.ToArray();
        Assert.Equal(6, bytes.Length);
        Assert.Equal((byte)'W', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'R', bytes[2]);
        Assert.Equal((byte)'T', bytes[3]);
        Assert.Equal((byte)0x01, bytes[4]);
        Assert.Equal((byte)PlatformMarker.WindowsDpapiUser, bytes[5]);
    }

    [Theory]
    [InlineData(PlatformMarker.WindowsDpapiUser)]
    [InlineData(PlatformMarker.WindowsDpapiMachine)]
    [InlineData(PlatformMarker.MacKeychainUser)]
    [InlineData(PlatformMarker.MacKeychainMachine)]
    [InlineData(PlatformMarker.LinuxLibsecretUser)]
    public void RoundTrip_AllMarkers(PlatformMarker marker)
    {
        using MemoryStream stream = new();
        Header.Write(stream, marker);
        stream.Position = 0;
        Header.ReadResult result = Header.Read(stream);
        Assert.Equal(1, result.Version);
        Assert.Equal(marker, result.Marker);
    }

    [Fact]
    public void Read_BadMagic_Throws()
    {
        using MemoryStream stream = new(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x01 });
        FormatException ex = Assert.Throws<FormatException>(() => Header.Read(stream));
        Assert.Contains("magic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_UnsupportedVersion_Throws()
    {
        // Magic + version 0xFF + marker.
        using MemoryStream stream = new(new byte[] { (byte)'W', (byte)'P', (byte)'R', (byte)'T', 0xFF, 0x01 });
        FormatException ex = Assert.Throws<FormatException>(() => Header.Read(stream));
        Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_UnknownMarker_Throws()
    {
        using MemoryStream stream = new(new byte[] { (byte)'W', (byte)'P', (byte)'R', (byte)'T', 0x01, 0xFE });
        FormatException ex = Assert.Throws<FormatException>(() => Header.Read(stream));
        Assert.Contains("platform", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_TruncatedHeader_Throws()
    {
        using MemoryStream stream = new(new byte[] { (byte)'W', (byte)'P' });
        Assert.Throws<EndOfStreamException>(() => Header.Read(stream));
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

Run: `dotnet test d:/projects/winix/tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter FullyQualifiedName~HeaderTests`
Expected: compile error (`Header` not defined).

- [ ] **Step 3: Implement `src/Winix.Protect/Header.cs`**

```csharp
#nullable enable
using System;
using System.IO;

namespace Winix.Protect;

/// <summary>Reads and writes the 6-byte <c>protect</c> file header: magic | version | platform marker.</summary>
public static class Header
{
    private static readonly byte[] Magic = [(byte)'W', (byte)'P', (byte)'R', (byte)'T'];
    private const byte CurrentVersion = 0x01;

    /// <summary>Output of <see cref="Read"/>.</summary>
    public readonly record struct ReadResult(byte Version, PlatformMarker Marker);

    /// <summary>The full header length in bytes.</summary>
    public const int Length = 6;

    /// <summary>Write a v1 header with the given platform marker.</summary>
    public static void Write(Stream stream, PlatformMarker marker)
    {
        stream.Write(Magic, 0, Magic.Length);
        stream.WriteByte(CurrentVersion);
        stream.WriteByte((byte)marker);
    }

    /// <summary>Read and validate the header.</summary>
    /// <exception cref="FormatException">Magic, version, or marker is invalid.</exception>
    /// <exception cref="EndOfStreamException">Stream is shorter than <see cref="Length"/> bytes.</exception>
    public static ReadResult Read(Stream stream)
    {
        byte[] buffer = new byte[Length];
        int read = 0;
        while (read < Length)
        {
            int n = stream.Read(buffer, read, Length - read);
            if (n == 0)
            {
                throw new EndOfStreamException($"Expected {Length} header bytes; got {read}.");
            }
            read += n;
        }

        for (int i = 0; i < Magic.Length; i++)
        {
            if (buffer[i] != Magic[i])
            {
                throw new FormatException("Bad magic — not a protect file.");
            }
        }

        byte version = buffer[4];
        if (version != CurrentVersion)
        {
            throw new FormatException($"Unsupported version: 0x{version:X2}. This build understands version 0x{CurrentVersion:X2}.");
        }

        byte markerByte = buffer[5];
        if (!IsKnownMarker(markerByte))
        {
            throw new FormatException($"Unknown platform marker: 0x{markerByte:X2}.");
        }

        return new ReadResult(version, (PlatformMarker)markerByte);
    }

    private static bool IsKnownMarker(byte b)
    {
        return b == (byte)PlatformMarker.WindowsDpapiUser
            || b == (byte)PlatformMarker.WindowsDpapiMachine
            || b == (byte)PlatformMarker.MacKeychainUser
            || b == (byte)PlatformMarker.MacKeychainMachine
            || b == (byte)PlatformMarker.LinuxLibsecretUser;
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test d:/projects/winix/tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter FullyQualifiedName~HeaderTests`
Expected: 9 tests pass (4 facts + 5-row theory).

- [ ] **Step 5: Commit**

```
git -C d:/projects/winix add src/Winix.Protect/Header.cs tests/Winix.Protect.Tests/HeaderTests.cs
git -C d:/projects/winix commit -m "feat(protect): add Header read/write with magic/version/marker"
```

---

## Task 4: `ISecretStore` interface + `NullSecretStore`

Named KEY=VALUE store abstraction; in-memory implementation for tests.

**Files:**
- Create: `src/Winix.SecretStore/ISecretStore.cs`
- Create: `src/Winix.SecretStore/NullSecretStore.cs`
- Create: `tests/Winix.SecretStore.Tests/NullSecretStoreTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Winix.SecretStore.Tests/NullSecretStoreTests.cs
#nullable enable
using System;
using Xunit;
using Winix.SecretStore;

namespace Winix.SecretStore.Tests;

public class NullSecretStoreTests
{
    [Fact]
    public void SetAndGet_RoundTrips()
    {
        NullSecretStore store = new();
        byte[] value = [1, 2, 3, 4];
        store.Set("ns", "key", value);
        byte[]? got = store.Get("ns", "key");
        Assert.NotNull(got);
        Assert.Equal(value, got);
    }

    [Fact]
    public void Get_Missing_ReturnsNull()
    {
        NullSecretStore store = new();
        Assert.Null(store.Get("ns", "nope"));
    }

    [Fact]
    public void Delete_Removes()
    {
        NullSecretStore store = new();
        store.Set("ns", "key", [9]);
        bool removed = store.Delete("ns", "key");
        Assert.True(removed);
        Assert.Null(store.Get("ns", "key"));
    }

    [Fact]
    public void Delete_MissingKey_ReturnsFalse()
    {
        NullSecretStore store = new();
        Assert.False(store.Delete("ns", "nope"));
    }

    [Fact]
    public void Set_OverwritesExistingValue()
    {
        NullSecretStore store = new();
        store.Set("ns", "key", [1]);
        store.Set("ns", "key", [2, 3]);
        Assert.Equal(new byte[] { 2, 3 }, store.Get("ns", "key"));
    }

    [Fact]
    public void Namespace_IsolatesKeys()
    {
        NullSecretStore store = new();
        store.Set("a", "k", [1]);
        store.Set("b", "k", [2]);
        Assert.Equal(new byte[] { 1 }, store.Get("a", "k"));
        Assert.Equal(new byte[] { 2 }, store.Get("b", "k"));
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

Run: `dotnet test d:/projects/winix/tests/Winix.SecretStore.Tests/Winix.SecretStore.Tests.csproj --filter FullyQualifiedName~NullSecretStoreTests`

- [ ] **Step 3: Implement `ISecretStore.cs`**

```csharp
// src/Winix.SecretStore/ISecretStore.cs
#nullable enable
namespace Winix.SecretStore;

/// <summary>
/// Named key-value store backed by an OS-native secret-storage primitive
/// (Windows Credential Manager, macOS Keychain, Linux libsecret).
/// </summary>
public interface ISecretStore
{
    /// <summary>Store <paramref name="value"/> under <paramref name="namespace_"/>/<paramref name="key"/>, replacing any existing entry.</summary>
    void Set(string namespace_, string key, byte[] value);

    /// <summary>Retrieve a previously-stored value. Returns null if the key does not exist.</summary>
    byte[]? Get(string namespace_, string key);

    /// <summary>Delete an entry. Returns true if an entry was removed; false if no such entry existed.</summary>
    bool Delete(string namespace_, string key);
}
```

- [ ] **Step 4: Implement `NullSecretStore.cs`**

```csharp
// src/Winix.SecretStore/NullSecretStore.cs
#nullable enable
using System.Collections.Generic;

namespace Winix.SecretStore;

/// <summary>In-memory <see cref="ISecretStore"/> for tests. Not persistent.</summary>
public sealed class NullSecretStore : ISecretStore
{
    private readonly Dictionary<string, byte[]> _entries = new();

    public void Set(string namespace_, string key, byte[] value)
    {
        _entries[Compose(namespace_, key)] = (byte[])value.Clone();
    }

    public byte[]? Get(string namespace_, string key)
    {
        return _entries.TryGetValue(Compose(namespace_, key), out byte[]? value)
            ? (byte[])value.Clone()
            : null;
    }

    public bool Delete(string namespace_, string key)
    {
        return _entries.Remove(Compose(namespace_, key));
    }

    private static string Compose(string namespace_, string key) => $"{namespace_} {key}";
}
```

- [ ] **Step 5: Run tests — expect pass**

Expected: 6 tests pass.

- [ ] **Step 6: Commit**

```
git -C d:/projects/winix add src/Winix.SecretStore/ISecretStore.cs src/Winix.SecretStore/NullSecretStore.cs tests/Winix.SecretStore.Tests/NullSecretStoreTests.cs
git -C d:/projects/winix commit -m "feat(secretstore): add ISecretStore interface and NullSecretStore"
```

---

## Task 5: `WindowsCredentialManagerStore` (advapi32 P/Invoke)

Windows-only backend using classic `CredRead`/`CredWrite`/`CredDelete`. Tested via integration smoke test only — NOT in CI unit tests.

**Files:**
- Create: `src/Winix.SecretStore/WindowsCredentialManagerStore.cs`

- [ ] **Step 1: Implement `WindowsCredentialManagerStore.cs`**

```csharp
#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Winix.SecretStore;

/// <summary>
/// Windows Credential Manager backend. Uses the classic Win32 Credential Management API
/// (<c>CredReadW</c>/<c>CredWriteW</c>/<c>CredDeleteW</c> via <c>advapi32.dll</c>). Works from
/// unpackaged console apps (unlike WinRT <c>PasswordVault</c> which requires MSIX packaging).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialManagerStore : ISecretStore
{
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;
    private const int ERROR_NOT_FOUND = 1168;

    public void Set(string namespace_, string key, byte[] value)
    {
        string target = Compose(namespace_, key);
        IntPtr blobPtr = Marshal.AllocHGlobal(value.Length);
        try
        {
            Marshal.Copy(value, 0, blobPtr, value.Length);

            CREDENTIAL cred = new()
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                CredentialBlobSize = (uint)value.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = Environment.UserName,
            };

            if (!CredWriteW(ref cred, 0))
            {
                int err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err, $"CredWriteW failed for target '{target}' (0x{err:X}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    public byte[]? Get(string namespace_, string key)
    {
        string target = Compose(namespace_, key);
        if (!CredReadW(target, CRED_TYPE_GENERIC, 0, out IntPtr credPtr))
        {
            int err = Marshal.GetLastWin32Error();
            if (err == ERROR_NOT_FOUND)
            {
                return null;
            }
            throw new Win32Exception(err, $"CredReadW failed for target '{target}' (0x{err:X}).");
        }

        try
        {
            CREDENTIAL cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            byte[] value = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, value, 0, (int)cred.CredentialBlobSize);
            return value;
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public bool Delete(string namespace_, string key)
    {
        string target = Compose(namespace_, key);
        if (CredDeleteW(target, CRED_TYPE_GENERIC, 0))
        {
            return true;
        }

        int err = Marshal.GetLastWin32Error();
        if (err == ERROR_NOT_FOUND)
        {
            return false;
        }
        throw new Win32Exception(err, $"CredDeleteW failed for target '{target}' (0x{err:X}).");
    }

    private static string Compose(string namespace_, string key) => $"{namespace_}/{key}";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, uint type, uint reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string target, uint type, uint reservedFlag);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build d:/projects/winix/src/Winix.SecretStore/Winix.SecretStore.csproj`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Commit**

```
git -C d:/projects/winix add src/Winix.SecretStore/WindowsCredentialManagerStore.cs
git -C d:/projects/winix commit -m "feat(secretstore): add WindowsCredentialManagerStore via advapi32 P/Invoke"
```

---

## Task 6: `MacOsKeychainStore` (security CLI shell-out)

macOS backend using the built-in `security` CLI.

**Files:**
- Create: `src/Winix.SecretStore/MacOsKeychainStore.cs`

- [ ] **Step 1: Implement `MacOsKeychainStore.cs`**

```csharp
#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace Winix.SecretStore;

/// <summary>
/// macOS Keychain backend via the built-in <c>security</c> CLI. Uses generic-password items
/// with <c>-s &lt;service&gt; -a &lt;account&gt;</c> as the identity. Values are stored as raw bytes
/// encoded hex-safe (hex) because <c>security</c>'s <c>-w</c> (password) expects text — we hex-encode
/// so binary keys round-trip without line-ending or encoding issues.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacOsKeychainStore : ISecretStore
{
    private readonly bool _useSystemKeychain;

    public MacOsKeychainStore(bool useSystemKeychain)
    {
        _useSystemKeychain = useSystemKeychain;
    }

    public void Set(string namespace_, string key, byte[] value)
    {
        // Delete first so we don't hit "already exists"; ignore failure.
        Delete(namespace_, key);

        string hex = Convert.ToHexString(value);
        string[] args =
        [
            "add-generic-password",
            "-s", namespace_,
            "-a", key,
            "-w", hex,
        ];
        if (_useSystemKeychain)
        {
            args = [.. args, "/Library/Keychains/System.keychain"];
        }

        RunSecurity(args, allowError: false);
    }

    public byte[]? Get(string namespace_, string key)
    {
        string[] args =
        [
            "find-generic-password",
            "-s", namespace_,
            "-a", key,
            "-w",
        ];
        if (_useSystemKeychain)
        {
            args = [.. args, "/Library/Keychains/System.keychain"];
        }

        (int exit, string stdout, string _) = RunSecurity(args, allowError: true);
        if (exit == 44)
        {
            // "The specified item could not be found in the keychain."
            return null;
        }
        if (exit != 0)
        {
            throw new InvalidOperationException($"security find-generic-password failed (exit {exit}).");
        }

        string hex = stdout.Trim();
        return Convert.FromHexString(hex);
    }

    public bool Delete(string namespace_, string key)
    {
        string[] args =
        [
            "delete-generic-password",
            "-s", namespace_,
            "-a", key,
        ];
        if (_useSystemKeychain)
        {
            args = [.. args, "/Library/Keychains/System.keychain"];
        }

        (int exit, string _, string _) = RunSecurity(args, allowError: true);
        return exit == 0;
    }

    private static (int exitCode, string stdout, string stderr) RunSecurity(string[] args, bool allowError)
    {
        ProcessStartInfo psi = new()
        {
            FileName = "security",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start `security`.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!allowError && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"security failed (exit {process.ExitCode}): {stderr.Trim()}");
        }
        return (process.ExitCode, stdout, stderr);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build d:/projects/winix/src/Winix.SecretStore/Winix.SecretStore.csproj`
Expected: clean.

- [ ] **Step 3: Commit**

```
git -C d:/projects/winix add src/Winix.SecretStore/MacOsKeychainStore.cs
git -C d:/projects/winix commit -m "feat(secretstore): add MacOsKeychainStore via security CLI"
```

---

## Task 7: `LinuxLibsecretStore` (secret-tool shell-out)

Linux backend using `secret-tool` (libsecret-tools package).

**Files:**
- Create: `src/Winix.SecretStore/LinuxLibsecretStore.cs`

- [ ] **Step 1: Implement `LinuxLibsecretStore.cs`**

```csharp
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;

namespace Winix.SecretStore;

/// <summary>
/// Linux libsecret backend via the <c>secret-tool</c> CLI. Values are hex-encoded so binary
/// payloads round-trip safely through <c>secret-tool</c>'s text-oriented pipes.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxLibsecretStore : ISecretStore
{
    public void Set(string namespace_, string key, byte[] value)
    {
        AssertAvailable();
        string hex = Convert.ToHexString(value);

        ProcessStartInfo psi = new()
        {
            FileName = "secret-tool",
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in new[] { "store", "--label", $"winix:{namespace_}/{key}", "service", namespace_, "key", key })
        {
            psi.ArgumentList.Add(a);
        }

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start secret-tool.");
        process.StandardInput.Write(hex);
        process.StandardInput.Close();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"secret-tool store failed (exit {process.ExitCode}): {stderr.Trim()}");
        }
    }

    public byte[]? Get(string namespace_, string key)
    {
        AssertAvailable();
        (int exit, string stdout, string _) = RunSecretTool(["lookup", "service", namespace_, "key", key]);
        if (exit != 0)
        {
            // secret-tool returns non-zero when the item is not found (no distinct code).
            return null;
        }
        string hex = stdout.Trim();
        return string.IsNullOrEmpty(hex) ? null : Convert.FromHexString(hex);
    }

    public bool Delete(string namespace_, string key)
    {
        AssertAvailable();
        (int exit, string _, string _) = RunSecretTool(["clear", "service", namespace_, "key", key]);
        return exit == 0;
    }

    private static void AssertAvailable()
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "secret-tool",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("--help");
            using Process p = Process.Start(psi) ?? throw new FileNotFoundException();
            p.WaitForExit();
        }
        catch
        {
            throw new InvalidOperationException(
                "secret-tool is not installed. Install with: 'sudo apt install libsecret-tools' (Debian/Ubuntu), "
                + "'sudo dnf install libsecret' (Fedora), 'sudo pacman -S libsecret' (Arch), or equivalent.");
        }
    }

    private static (int exitCode, string stdout, string stderr) RunSecretTool(string[] args)
    {
        ProcessStartInfo psi = new()
        {
            FileName = "secret-tool",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start secret-tool.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}
```

- [ ] **Step 2: Build and commit**

Run: `dotnet build d:/projects/winix/src/Winix.SecretStore/Winix.SecretStore.csproj`
Expected: clean.

```
git -C d:/projects/winix add src/Winix.SecretStore/LinuxLibsecretStore.cs
git -C d:/projects/winix commit -m "feat(secretstore): add LinuxLibsecretStore via secret-tool"
```

---

## Task 8: `SecretStoreFactory`

Selects the appropriate backend based on the current OS.

**Files:**
- Create: `src/Winix.SecretStore/SecretStoreFactory.cs`

- [ ] **Step 1: Implement `SecretStoreFactory.cs`**

```csharp
#nullable enable
using System;
using System.Runtime.InteropServices;

namespace Winix.SecretStore;

/// <summary>Selects an <see cref="ISecretStore"/> implementation appropriate to the current OS.</summary>
public static class SecretStoreFactory
{
    /// <summary>Create a user-scope store for the current OS.</summary>
    public static ISecretStore CreateUserStore()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsCredentialManagerStore();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsKeychainStore(useSystemKeychain: false);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxLibsecretStore();
        }
        throw new PlatformNotSupportedException("Unsupported OS.");
    }

    /// <summary>Create a machine-scope store. Throws on Linux (no native primitive in v1).</summary>
    public static ISecretStore CreateMachineStore()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsCredentialManagerStore();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsKeychainStore(useSystemKeychain: true);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException(
                "Machine scope is not supported on Linux. Use user scope, or install systemd-creds (Linux machine scope is a v2 feature).");
        }
        throw new PlatformNotSupportedException("Unsupported OS.");
    }
}
```

- [ ] **Step 2: Build and commit**

Run: `dotnet build d:/projects/winix/src/Winix.SecretStore/Winix.SecretStore.csproj`
Expected: clean.

```
git -C d:/projects/winix add src/Winix.SecretStore/SecretStoreFactory.cs
git -C d:/projects/winix commit -m "feat(secretstore): add SecretStoreFactory with per-OS dispatch"
```

---

## Task 9: `IProtectBackend` interface

Chunk-level crypto contract. Consumed by `ChunkWriter` and `ChunkReader`.

**Files:**
- Create: `src/Winix.Protect/IProtectBackend.cs`
- Create: `src/Winix.Protect/AadContext.cs`

- [ ] **Step 1: Create `AadContext.cs`**

```csharp
#nullable enable
namespace Winix.Protect;

/// <summary>Context fed as additional-authenticated-data for each chunk on the AEAD path. Ignored by the DPAPI backend.</summary>
public readonly record struct AadContext(byte[] HeaderBytes, long ChunkIndex, bool IsFinal);
```

- [ ] **Step 2: Create `IProtectBackend.cs`**

```csharp
#nullable enable
namespace Winix.Protect;

/// <summary>
/// Per-chunk encrypt/decrypt contract. Called by <see cref="ChunkWriter"/> / <see cref="ChunkReader"/>.
/// Implementations are Windows DPAPI (keyless) or AES-GCM-with-SecretStore-key (Mac/Linux).
/// </summary>
public interface IProtectBackend
{
    /// <summary>The platform marker for files produced by this backend.</summary>
    PlatformMarker Marker { get; }

    /// <summary>Encrypt a single chunk of plaintext. <paramref name="isFinal"/> must be folded into the ciphertext integrity.</summary>
    byte[] EncryptChunk(byte[] plaintext, AadContext aad, bool isFinal);

    /// <summary>Decrypt a single chunk. Returns (plaintext, isFinal). Must throw on any tamper / integrity failure.</summary>
    (byte[] plaintext, bool isFinal) DecryptChunk(byte[] chunkPayload, AadContext aad);
}
```

- [ ] **Step 3: Build and commit**

```
git -C d:/projects/winix add src/Winix.Protect/AadContext.cs src/Winix.Protect/IProtectBackend.cs
git -C d:/projects/winix commit -m "feat(protect): add IProtectBackend + AadContext types"
```

---

## Task 10: `DpapiBackend` (Windows)

Wraps `ProtectedData.Protect` / `Unprotect`. The plaintext fed to DPAPI is prefixed with a 1-byte `is_final` flag so truncation is detected (DPAPI provides integrity, so the flag is authenticated).

**Files:**
- Create: `src/Winix.Protect/DpapiBackend.cs`
- Create: `tests/Winix.Protect.Tests/DpapiBackendTests.cs`

- [ ] **Step 1: Write tests (Windows-only; skipped on non-Windows via `RuntimeInformation` guard)**

```csharp
#nullable enable
using System;
using System.Runtime.InteropServices;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

public class DpapiBackendTests
{
    private static bool OnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [Fact]
    public void EncryptDecrypt_RoundTrips()
    {
        if (!OnWindows) return;
        DpapiBackend backend = new(Scope.User);
        byte[] plaintext = System.Text.Encoding.UTF8.GetBytes("hello world");
        AadContext aad = new([0x57, 0x50, 0x52, 0x54, 0x01, 0x01], 0, true);
        byte[] chunk = backend.EncryptChunk(plaintext, aad, isFinal: true);
        (byte[] decrypted, bool isFinal) = backend.DecryptChunk(chunk, aad);
        Assert.Equal(plaintext, decrypted);
        Assert.True(isFinal);
    }

    [Fact]
    public void Marker_UserScope_IsDpapiUser()
    {
        if (!OnWindows) return;
        Assert.Equal(PlatformMarker.WindowsDpapiUser, new DpapiBackend(Scope.User).Marker);
    }

    [Fact]
    public void Marker_MachineScope_IsDpapiMachine()
    {
        if (!OnWindows) return;
        Assert.Equal(PlatformMarker.WindowsDpapiMachine, new DpapiBackend(Scope.Machine).Marker);
    }

    [Fact]
    public void IsFinal_FlagRoundTrips()
    {
        if (!OnWindows) return;
        DpapiBackend backend = new(Scope.User);
        AadContext aad = new([0x57, 0x50, 0x52, 0x54, 0x01, 0x01], 5, false);
        byte[] chunk = backend.EncryptChunk([1, 2, 3], aad, isFinal: false);
        (_, bool isFinal) = backend.DecryptChunk(chunk, aad);
        Assert.False(isFinal);
    }

    [Fact]
    public void TamperedChunk_ThrowsOnDecrypt()
    {
        if (!OnWindows) return;
        DpapiBackend backend = new(Scope.User);
        AadContext aad = new([0x57, 0x50, 0x52, 0x54, 0x01, 0x01], 0, true);
        byte[] chunk = backend.EncryptChunk([1, 2, 3, 4], aad, isFinal: true);
        chunk[chunk.Length - 1] ^= 0x01;
        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => backend.DecryptChunk(chunk, aad));
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

- [ ] **Step 3: Implement `DpapiBackend.cs`**

```csharp
#nullable enable
using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Winix.Protect;

[SupportedOSPlatform("windows")]
public sealed class DpapiBackend : IProtectBackend
{
    private readonly DataProtectionScope _scope;

    public DpapiBackend(Scope scope)
    {
        _scope = scope == Scope.Machine
            ? DataProtectionScope.LocalMachine
            : DataProtectionScope.CurrentUser;
        Marker = scope == Scope.Machine
            ? PlatformMarker.WindowsDpapiMachine
            : PlatformMarker.WindowsDpapiUser;
    }

    public PlatformMarker Marker { get; }

    public byte[] EncryptChunk(byte[] plaintext, AadContext aad, bool isFinal)
    {
        byte[] framed = new byte[plaintext.Length + 1];
        framed[0] = isFinal ? (byte)1 : (byte)0;
        Array.Copy(plaintext, 0, framed, 1, plaintext.Length);
        return ProtectedData.Protect(framed, optionalEntropy: null, _scope);
    }

    public (byte[] plaintext, bool isFinal) DecryptChunk(byte[] chunkPayload, AadContext aad)
    {
        byte[] framed = ProtectedData.Unprotect(chunkPayload, optionalEntropy: null, _scope);
        if (framed.Length < 1)
        {
            throw new CryptographicException("DPAPI payload too short (missing is_final byte).");
        }
        bool isFinal = framed[0] == 1;
        byte[] plaintext = new byte[framed.Length - 1];
        Array.Copy(framed, 1, plaintext, 0, plaintext.Length);
        return (plaintext, isFinal);
    }
}
```

- [ ] **Step 4: Run tests — expect 5 pass on Windows**

- [ ] **Step 5: Commit**

Run: `git -C d:/projects/winix add src/Winix.Protect/DpapiBackend.cs tests/Winix.Protect.Tests/DpapiBackendTests.cs`
Then: `git -C d:/projects/winix commit -m "feat(protect): add DpapiBackend for Windows encrypt-at-rest"`

---

## Task 11: `AeadBackend` abstract base

AES-256-GCM template; subclasses inject the platform-specific `ISecretStore` and the service/key names.

**Files:**
- Create: `src/Winix.Protect/AeadBackend.cs`
- Create: `tests/Winix.Protect.Tests/AeadBackendTests.cs`

- [ ] **Step 1: Write tests (platform-independent; uses `NullSecretStore`)**

```csharp
#nullable enable
using System;
using Xunit;
using Winix.Protect;
using Winix.SecretStore;

namespace Winix.Protect.Tests;

public class AeadBackendTests
{
    private sealed class TestAeadBackend : AeadBackend
    {
        public TestAeadBackend(ISecretStore store) : base(store, PlatformMarker.MacKeychainUser, "test-namespace", "test-key") { }
    }

    [Fact]
    public void EncryptDecrypt_RoundTrips()
    {
        TestAeadBackend backend = new(new NullSecretStore());
        byte[] plaintext = System.Text.Encoding.UTF8.GetBytes("hello");
        AadContext aad = new([0x57, 0x50, 0x52, 0x54, 0x01, 0x10], 0, true);
        byte[] chunk = backend.EncryptChunk(plaintext, aad, isFinal: true);
        (byte[] decrypted, bool isFinal) = backend.DecryptChunk(chunk, aad);
        Assert.Equal(plaintext, decrypted);
        Assert.True(isFinal);
    }

    [Fact]
    public void EncryptChunk_LayoutIsFinalFlagIvLengthCiphertextTag()
    {
        TestAeadBackend backend = new(new NullSecretStore());
        byte[] plaintext = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        AadContext aad = new([0x57, 0x50, 0x52, 0x54, 0x01, 0x10], 0, true);
        byte[] chunk = backend.EncryptChunk(plaintext, aad, isFinal: true);

        Assert.Equal(1, chunk[0]);
        int length = (chunk[13] << 24) | (chunk[14] << 16) | (chunk[15] << 8) | chunk[16];
        Assert.Equal(4, length);
        Assert.Equal(1 + 12 + 4 + 4 + 16, chunk.Length);
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        TestAeadBackend backend = new(new NullSecretStore());
        AadContext aad = new([0x57, 0x50, 0x52, 0x54, 0x01, 0x10], 0, true);
        byte[] chunk = backend.EncryptChunk([1, 2, 3], aad, isFinal: true);
        chunk[17] ^= 0x01;
        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
            () => backend.DecryptChunk(chunk, aad));
    }

    [Fact]
    public void Decrypt_WrongAad_Throws()
    {
        TestAeadBackend backend = new(new NullSecretStore());
        AadContext encryptAad = new([0x57, 0x50, 0x52, 0x54, 0x01, 0x10], 0, true);
        AadContext decryptAad = new([0x57, 0x50, 0x52, 0x54, 0x01, 0x10], 1, true);
        byte[] chunk = backend.EncryptChunk([1, 2, 3], encryptAad, isFinal: true);
        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
            () => backend.DecryptChunk(chunk, decryptAad));
    }

    [Fact]
    public void Key_IsGeneratedOnFirstUseAndReusedAfter()
    {
        NullSecretStore shared = new();
        TestAeadBackend one = new(shared);
        TestAeadBackend two = new(shared);
        byte[] plaintext = [1, 2, 3, 4];
        AadContext aad = new([0x57, 0x50, 0x52, 0x54, 0x01, 0x10], 0, true);
        byte[] chunk = one.EncryptChunk(plaintext, aad, isFinal: true);
        (byte[] decrypted, _) = two.DecryptChunk(chunk, aad);
        Assert.Equal(plaintext, decrypted);
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

- [ ] **Step 3: Implement `AeadBackend.cs`**

```csharp
#nullable enable
using System;
using System.Security.Cryptography;
using Winix.SecretStore;

namespace Winix.Protect;

public abstract class AeadBackend : IProtectBackend
{
    private const int IvSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly ISecretStore _store;
    private readonly string _namespace;
    private readonly string _keyName;
    private byte[]? _cachedKey;

    protected AeadBackend(ISecretStore store, PlatformMarker marker, string namespace_, string keyName)
    {
        _store = store;
        Marker = marker;
        _namespace = namespace_;
        _keyName = keyName;
    }

    public PlatformMarker Marker { get; }

    public byte[] EncryptChunk(byte[] plaintext, AadContext aad, bool isFinal)
    {
        byte[] key = GetOrCreateKey();
        byte[] iv = new byte[IvSize];
        RandomNumberGenerator.Fill(iv);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];
        byte[] aadBytes = BuildAadBytes(aad, isFinal);

        using AesGcm gcm = new(key, TagSize);
        gcm.Encrypt(iv, plaintext, ciphertext, tag, aadBytes);

        byte[] chunk = new byte[1 + IvSize + 4 + ciphertext.Length + TagSize];
        chunk[0] = isFinal ? (byte)1 : (byte)0;
        Array.Copy(iv, 0, chunk, 1, IvSize);
        chunk[13] = (byte)(ciphertext.Length >> 24);
        chunk[14] = (byte)(ciphertext.Length >> 16);
        chunk[15] = (byte)(ciphertext.Length >> 8);
        chunk[16] = (byte)ciphertext.Length;
        Array.Copy(ciphertext, 0, chunk, 17, ciphertext.Length);
        Array.Copy(tag, 0, chunk, 17 + ciphertext.Length, TagSize);
        return chunk;
    }

    public (byte[] plaintext, bool isFinal) DecryptChunk(byte[] chunkPayload, AadContext aad)
    {
        if (chunkPayload.Length < 1 + IvSize + 4 + TagSize)
        {
            throw new FormatException("Chunk too short.");
        }

        bool isFinal = chunkPayload[0] == 1;
        byte[] iv = new byte[IvSize];
        Array.Copy(chunkPayload, 1, iv, 0, IvSize);
        int length = (chunkPayload[13] << 24) | (chunkPayload[14] << 16) | (chunkPayload[15] << 8) | chunkPayload[16];
        if (chunkPayload.Length != 1 + IvSize + 4 + length + TagSize)
        {
            throw new FormatException("Chunk length field does not match payload length.");
        }
        byte[] ciphertext = new byte[length];
        Array.Copy(chunkPayload, 17, ciphertext, 0, length);
        byte[] tag = new byte[TagSize];
        Array.Copy(chunkPayload, 17 + length, tag, 0, TagSize);

        byte[] key = GetOrCreateKey();
        byte[] plaintext = new byte[length];
        byte[] aadBytes = BuildAadBytes(aad, isFinal);
        using AesGcm gcm = new(key, TagSize);
        gcm.Decrypt(iv, ciphertext, tag, plaintext, aadBytes);
        return (plaintext, isFinal);
    }

    private byte[] GetOrCreateKey()
    {
        if (_cachedKey is not null) return _cachedKey;
        byte[]? existing = _store.Get(_namespace, _keyName);
        if (existing is not null && existing.Length == KeySize)
        {
            _cachedKey = existing;
            return existing;
        }
        byte[] fresh = new byte[KeySize];
        RandomNumberGenerator.Fill(fresh);
        _store.Set(_namespace, _keyName, fresh);
        _cachedKey = fresh;
        return fresh;
    }

    private static byte[] BuildAadBytes(AadContext aad, bool isFinal)
    {
        byte[] buffer = new byte[aad.HeaderBytes.Length + 8 + 1];
        Array.Copy(aad.HeaderBytes, 0, buffer, 0, aad.HeaderBytes.Length);
        long idx = aad.ChunkIndex;
        int off = aad.HeaderBytes.Length;
        buffer[off + 0] = (byte)(idx >> 56);
        buffer[off + 1] = (byte)(idx >> 48);
        buffer[off + 2] = (byte)(idx >> 40);
        buffer[off + 3] = (byte)(idx >> 32);
        buffer[off + 4] = (byte)(idx >> 24);
        buffer[off + 5] = (byte)(idx >> 16);
        buffer[off + 6] = (byte)(idx >> 8);
        buffer[off + 7] = (byte)idx;
        buffer[off + 8] = isFinal ? (byte)1 : (byte)0;
        return buffer;
    }
}
```

- [ ] **Step 4: Run tests — expect 5 pass**

- [ ] **Step 5: Commit**

Run: `git -C d:/projects/winix add src/Winix.Protect/AeadBackend.cs tests/Winix.Protect.Tests/AeadBackendTests.cs`
Then: `git -C d:/projects/winix commit -m "feat(protect): add AeadBackend (AES-256-GCM) with AAD binding"`

---

## Task 12: `AeadKeychainBackend` + `AeadLibsecretBackend`

Thin subclasses.

**Files:**
- Create: `src/Winix.Protect/AeadKeychainBackend.cs`
- Create: `src/Winix.Protect/AeadLibsecretBackend.cs`

- [ ] **Step 1: Create `AeadKeychainBackend.cs`**

```csharp
#nullable enable
using System.Runtime.Versioning;
using Winix.SecretStore;

namespace Winix.Protect;

[SupportedOSPlatform("macos")]
public sealed class AeadKeychainBackend : AeadBackend
{
    public AeadKeychainBackend(Scope scope)
        : base(
            new MacOsKeychainStore(useSystemKeychain: scope == Scope.Machine),
            scope == Scope.Machine ? PlatformMarker.MacKeychainMachine : PlatformMarker.MacKeychainUser,
            "winix-protect",
            scope == Scope.Machine ? "default-machine-v1" : "default-user-v1")
    {
    }
}
```

- [ ] **Step 2: Create `AeadLibsecretBackend.cs`**

```csharp
#nullable enable
using System.Runtime.Versioning;
using Winix.SecretStore;

namespace Winix.Protect;

[SupportedOSPlatform("linux")]
public sealed class AeadLibsecretBackend : AeadBackend
{
    public AeadLibsecretBackend()
        : base(
            new LinuxLibsecretStore(),
            PlatformMarker.LinuxLibsecretUser,
            "winix-protect",
            "default-user-v1")
    {
    }
}
```

- [ ] **Step 3: Build and commit**

Run: `dotnet build d:/projects/winix/src/Winix.Protect/Winix.Protect.csproj`
Run: `git -C d:/projects/winix add src/Winix.Protect/AeadKeychainBackend.cs src/Winix.Protect/AeadLibsecretBackend.cs`
Run: `git -C d:/projects/winix commit -m "feat(protect): add Mac/Linux AEAD backends"`

---

## Task 13: `BackendFactory`

Dispatches by scope + platform; throws helpful errors for unsupported combos.

**Files:**
- Create: `src/Winix.Protect/BackendFactory.cs`
- Create: `tests/Winix.Protect.Tests/BackendFactoryTests.cs`

- [ ] **Step 1: Write tests**

```csharp
#nullable enable
using System;
using System.Runtime.InteropServices;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

public class BackendFactoryTests
{
    private static bool OnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static bool OnMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    private static bool OnLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    [Fact]
    public void Create_UserScope_ReturnsPlatformBackend()
    {
        IProtectBackend backend = BackendFactory.Create(Scope.User);
        if (OnWindows) Assert.Equal(PlatformMarker.WindowsDpapiUser, backend.Marker);
        else if (OnMac) Assert.Equal(PlatformMarker.MacKeychainUser, backend.Marker);
        else if (OnLinux) Assert.Equal(PlatformMarker.LinuxLibsecretUser, backend.Marker);
    }

    [Fact]
    public void Create_MachineScope_Linux_Throws()
    {
        if (!OnLinux) return;
        PlatformNotSupportedException ex = Assert.Throws<PlatformNotSupportedException>(
            () => BackendFactory.Create(Scope.Machine));
        Assert.Contains("Linux", ex.Message);
    }

    [Fact]
    public void CreateForMarker_WrongPlatform_Throws()
    {
        if (!OnWindows) return;
        PlatformNotSupportedException ex = Assert.Throws<PlatformNotSupportedException>(
            () => BackendFactory.CreateForMarker(PlatformMarker.MacKeychainUser));
        Assert.Contains("macOS", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Implement `BackendFactory.cs`**

```csharp
#nullable enable
using System;
using System.Runtime.InteropServices;

namespace Winix.Protect;

public static class BackendFactory
{
    public static IProtectBackend Create(Scope scope)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#pragma warning disable CA1416
            return new DpapiBackend(scope);
#pragma warning restore CA1416
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
#pragma warning disable CA1416
            return new AeadKeychainBackend(scope);
#pragma warning restore CA1416
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (scope == Scope.Machine)
            {
                throw new PlatformNotSupportedException(
                    "Machine scope is not supported on Linux. Use user scope, or install systemd-creds (v2 feature).");
            }
#pragma warning disable CA1416
            return new AeadLibsecretBackend();
#pragma warning restore CA1416
        }
        throw new PlatformNotSupportedException("Unsupported OS.");
    }

    public static IProtectBackend CreateForMarker(PlatformMarker marker)
    {
#pragma warning disable CA1416
        return marker switch
        {
            PlatformMarker.WindowsDpapiUser when RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                => new DpapiBackend(Scope.User),
            PlatformMarker.WindowsDpapiMachine when RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                => new DpapiBackend(Scope.Machine),
            PlatformMarker.MacKeychainUser when RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                => new AeadKeychainBackend(Scope.User),
            PlatformMarker.MacKeychainMachine when RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                => new AeadKeychainBackend(Scope.Machine),
            PlatformMarker.LinuxLibsecretUser when RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                => new AeadLibsecretBackend(),
            _ => throw new PlatformNotSupportedException(
                $"This file was encrypted on {PlatformOfMarker(marker)} and cannot be decrypted on this machine."),
        };
#pragma warning restore CA1416
    }

    private static string PlatformOfMarker(PlatformMarker marker) => marker switch
    {
        PlatformMarker.WindowsDpapiUser or PlatformMarker.WindowsDpapiMachine => "Windows",
        PlatformMarker.MacKeychainUser or PlatformMarker.MacKeychainMachine => "macOS",
        PlatformMarker.LinuxLibsecretUser => "Linux",
        _ => "an unknown platform",
    };
}
```

- [ ] **Step 3: Run tests, commit**

Run: `dotnet test d:/projects/winix/tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter FullyQualifiedName~BackendFactoryTests`
Run: `git -C d:/projects/winix add src/Winix.Protect/BackendFactory.cs tests/Winix.Protect.Tests/BackendFactoryTests.cs`
Run: `git -C d:/projects/winix commit -m "feat(protect): add BackendFactory with per-platform dispatch"`

---

## Task 14-15: ChunkWriter + ChunkReader

See the design doc for full format details. These work as a pair; the tests exercise the round-trip.

**Files:**
- Create: `src/Winix.Protect/ChunkWriter.cs`
- Create: `src/Winix.Protect/ChunkReader.cs`
- Create: `tests/Winix.Protect.Tests/ChunkWriterReaderTests.cs`

- [ ] **Step 1: Write tests**

```csharp
#nullable enable
using System;
using System.IO;
using Xunit;
using Winix.Protect;
using Winix.SecretStore;

namespace Winix.Protect.Tests;

public class ChunkWriterReaderTests
{
    private sealed class TestAeadBackend : AeadBackend
    {
        public TestAeadBackend(ISecretStore store) : base(store, PlatformMarker.MacKeychainUser, "test-ns", "test-key") { }
    }

    private static (byte[] ciphertext, byte[] plaintext) EncodeThenRead(byte[] plaintext, int chunkSize = 64 * 1024)
    {
        NullSecretStore store = new();
        TestAeadBackend backend = new(store);
        byte[] header = [(byte)'W', (byte)'P', (byte)'R', (byte)'T', 0x01, (byte)PlatformMarker.MacKeychainUser];

        using MemoryStream cipherStream = new();
        using MemoryStream sourceStream = new(plaintext);
        ChunkWriter.Write(sourceStream, cipherStream, backend, header, chunkSize);

        byte[] encrypted = cipherStream.ToArray();
        using MemoryStream readStream = new(encrypted, 6, encrypted.Length - 6);
        using MemoryStream outStream = new();
        ChunkReader.Read(readStream, outStream, backend, header);
        return (encrypted, outStream.ToArray());
    }

    [Fact]
    public void RoundTrip_EmptyPayload_Works()
    {
        (byte[] _, byte[] decrypted) = EncodeThenRead(Array.Empty<byte>());
        Assert.Empty(decrypted);
    }

    [Fact]
    public void RoundTrip_SingleByte_Works()
    {
        byte[] input = [0x42];
        (byte[] _, byte[] decrypted) = EncodeThenRead(input);
        Assert.Equal(input, decrypted);
    }

    [Fact]
    public void RoundTrip_SmallPayload_OneChunk()
    {
        byte[] input = new byte[1024];
        Random.Shared.NextBytes(input);
        (byte[] _, byte[] decrypted) = EncodeThenRead(input);
        Assert.Equal(input, decrypted);
    }

    [Fact]
    public void RoundTrip_MultiChunkPayload_ViaSmallChunkSize()
    {
        byte[] input = new byte[200_000];
        Random.Shared.NextBytes(input);
        (byte[] _, byte[] decrypted) = EncodeThenRead(input, chunkSize: 64_000);
        Assert.Equal(input, decrypted);
    }

    [Fact]
    public void Truncation_FinalChunkDropped_Throws()
    {
        NullSecretStore store = new();
        TestAeadBackend backend = new(store);
        byte[] header = [(byte)'W', (byte)'P', (byte)'R', (byte)'T', 0x01, (byte)PlatformMarker.MacKeychainUser];

        byte[] input = new byte[100_000];
        Random.Shared.NextBytes(input);

        using MemoryStream cipherStream = new();
        using MemoryStream sourceStream = new(input);
        ChunkWriter.Write(sourceStream, cipherStream, backend, header, chunkSize: 50_000);

        byte[] encrypted = cipherStream.ToArray();
        byte[] truncated = new byte[encrypted.Length - 30_000];
        Array.Copy(encrypted, truncated, truncated.Length);

        using MemoryStream readStream = new(truncated, 6, truncated.Length - 6);
        using MemoryStream outStream = new();
        Assert.Throws<FormatException>(() => ChunkReader.Read(readStream, outStream, backend, header));
    }
}
```

- [ ] **Step 2: Implement `ChunkWriter.cs`**

```csharp
#nullable enable
using System;
using System.IO;

namespace Winix.Protect;

public static class ChunkWriter
{
    public const int DefaultChunkSize = 64 * 1024;

    public static void Write(Stream source, Stream destination, IProtectBackend backend, byte[] headerBytes, int chunkSize = DefaultChunkSize)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
        }

        destination.Write(headerBytes, 0, headerBytes.Length);

        byte[] buffer = new byte[chunkSize];
        long chunkIndex = 0;
        int buffered = 0;
        bool eofSeen = false;

        while (true)
        {
            int n = source.Read(buffer, buffered, chunkSize - buffered);
            if (n == 0)
            {
                eofSeen = true;
            }
            else
            {
                buffered += n;
            }

            if (buffered == chunkSize && !eofSeen)
            {
                byte[] oneByte = new byte[1];
                int peek = source.Read(oneByte, 0, 1);
                if (peek == 0)
                {
                    byte[] chunk = new byte[chunkSize];
                    Array.Copy(buffer, chunk, chunkSize);
                    AadContext aad = new(headerBytes, chunkIndex, true);
                    byte[] encrypted = backend.EncryptChunk(chunk, aad, true);
                    destination.Write(encrypted, 0, encrypted.Length);
                    return;
                }
                byte[] nonFinal = new byte[chunkSize];
                Array.Copy(buffer, nonFinal, chunkSize);
                AadContext nonFinalAad = new(headerBytes, chunkIndex++, false);
                byte[] encNonFinal = backend.EncryptChunk(nonFinal, nonFinalAad, false);
                destination.Write(encNonFinal, 0, encNonFinal.Length);
                buffer[0] = oneByte[0];
                buffered = 1;
            }
            else if (eofSeen)
            {
                byte[] finalChunk = new byte[buffered];
                Array.Copy(buffer, finalChunk, buffered);
                AadContext aad = new(headerBytes, chunkIndex, true);
                byte[] encrypted = backend.EncryptChunk(finalChunk, aad, true);
                destination.Write(encrypted, 0, encrypted.Length);
                return;
            }
        }
    }
}
```

- [ ] **Step 3: Implement `ChunkReader.cs`**

```csharp
#nullable enable
using System;
using System.IO;

namespace Winix.Protect;

public static class ChunkReader
{
    public static void Read(Stream source, Stream destination, IProtectBackend backend, byte[] headerBytes)
    {
        long chunkIndex = 0;
        while (true)
        {
            byte[] chunkPayload = ReadOneChunk(source, backend.Marker);
            if (chunkPayload.Length == 0)
            {
                throw new FormatException("Ciphertext is truncated (final chunk missing).");
            }

            bool aeadIsFinalGuess = IsAeadMarker(backend.Marker) && chunkPayload[0] == 1;
            AadContext aadForDecrypt = IsAeadMarker(backend.Marker)
                ? new AadContext(headerBytes, chunkIndex, aeadIsFinalGuess)
                : new AadContext(headerBytes, chunkIndex, false);

            (byte[] plaintext, bool isFinal) = backend.DecryptChunk(chunkPayload, aadForDecrypt);
            destination.Write(plaintext, 0, plaintext.Length);

            if (isFinal) return;
            chunkIndex++;
        }
    }

    private static bool IsAeadMarker(PlatformMarker marker)
        => marker == PlatformMarker.MacKeychainUser
        || marker == PlatformMarker.MacKeychainMachine
        || marker == PlatformMarker.LinuxLibsecretUser;

    private static byte[] ReadOneChunk(Stream source, PlatformMarker marker)
    {
        if (IsAeadMarker(marker))
        {
            byte[] prefix = new byte[17];
            int got = ReadExactlyOrPartial(source, prefix);
            if (got == 0) return Array.Empty<byte>();
            if (got < 17) throw new FormatException("Truncated chunk prefix (AEAD).");

            int length = (prefix[13] << 24) | (prefix[14] << 16) | (prefix[15] << 8) | prefix[16];
            byte[] tail = new byte[length + 16];
            int tailGot = ReadExactlyOrPartial(source, tail);
            if (tailGot < tail.Length) throw new FormatException("Truncated chunk body (AEAD).");

            byte[] chunk = new byte[17 + tail.Length];
            Array.Copy(prefix, chunk, 17);
            Array.Copy(tail, 0, chunk, 17, tail.Length);
            return chunk;
        }

        byte[] lengthBytes = new byte[4];
        int lenGot = ReadExactlyOrPartial(source, lengthBytes);
        if (lenGot == 0) return Array.Empty<byte>();
        if (lenGot < 4) throw new FormatException("Truncated DPAPI chunk length.");
        int blobLength = (lengthBytes[0] << 24) | (lengthBytes[1] << 16) | (lengthBytes[2] << 8) | lengthBytes[3];

        byte[] blob = new byte[blobLength];
        int blobGot = ReadExactlyOrPartial(source, blob);
        if (blobGot < blobLength) throw new FormatException("Truncated DPAPI chunk blob.");
        return blob;
    }

    private static int ReadExactlyOrPartial(Stream stream, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = stream.Read(buffer, total, buffer.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
```

- [ ] **Step 4: Run tests, commit**

Run: `dotnet test d:/projects/winix/tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter FullyQualifiedName~ChunkWriterReaderTests`
Expected: 5 tests pass.

Run: `git -C d:/projects/winix add src/Winix.Protect/ChunkWriter.cs src/Winix.Protect/ChunkReader.cs tests/Winix.Protect.Tests/ChunkWriterReaderTests.cs`
Run: `git -C d:/projects/winix commit -m "feat(protect): add ChunkWriter + ChunkReader with truncation detection"`

---

## Task 16: `RoundTripVerifier`

**Files:**
- Create: `src/Winix.Protect/RoundTripVerifier.cs`
- Create: `tests/Winix.Protect.Tests/RoundTripVerifierTests.cs`

- [ ] **Step 1: Write tests**

```csharp
#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;
using Winix.Protect;
using Winix.SecretStore;

namespace Winix.Protect.Tests;

public class RoundTripVerifierTests
{
    private sealed class TestAeadBackend : AeadBackend
    {
        public TestAeadBackend(ISecretStore store) : base(store, PlatformMarker.MacKeychainUser, "test-ns", "test-key") { }
    }

    [Fact]
    public void Verify_MatchingRoundTrip_Passes()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("hello world");
        NullSecretStore store = new();
        TestAeadBackend backend = new(store);
        byte[] header = [(byte)'W', (byte)'P', (byte)'R', (byte)'T', 0x01, (byte)PlatformMarker.MacKeychainUser];

        byte[] sourceHash;
        using (IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            hasher.AppendData(input);
            sourceHash = hasher.GetCurrentHash();
        }

        using MemoryStream encrypted = new();
        ChunkWriter.Write(new MemoryStream(input), encrypted, backend, header);
        encrypted.Position = 0;

        RoundTripVerifier.Verify(encrypted, backend, sourceHash);
    }

    [Fact]
    public void Verify_MismatchedHash_Throws()
    {
        byte[] input = new byte[] { 1, 2, 3 };
        NullSecretStore store = new();
        TestAeadBackend backend = new(store);
        byte[] header = [(byte)'W', (byte)'P', (byte)'R', (byte)'T', 0x01, (byte)PlatformMarker.MacKeychainUser];

        using MemoryStream encrypted = new();
        ChunkWriter.Write(new MemoryStream(input), encrypted, backend, header);
        encrypted.Position = 0;

        byte[] wrongHash = new byte[32];
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => RoundTripVerifier.Verify(encrypted, backend, wrongHash));
        Assert.Contains("integrity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Implement `RoundTripVerifier.cs`**

```csharp
#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;

namespace Winix.Protect;

public static class RoundTripVerifier
{
    public static void Verify(Stream encryptedStream, IProtectBackend backend, byte[] expectedSourceHash)
    {
        Header.ReadResult hdr = Header.Read(encryptedStream);
        if (hdr.Marker != backend.Marker)
        {
            throw new InvalidOperationException("Round-trip verification: header platform-marker mismatch.");
        }

        byte[] headerBytes = [(byte)'W', (byte)'P', (byte)'R', (byte)'T', hdr.Version, (byte)hdr.Marker];
        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using HashingStream sink = new(hasher);
        ChunkReader.Read(encryptedStream, sink, backend, headerBytes);

        byte[] actual = hasher.GetCurrentHash();
        if (!CryptographicOperations.FixedTimeEquals(actual, expectedSourceHash))
        {
            throw new InvalidOperationException(
                "Encryption integrity check failed — round-trip SHA-256 mismatch. Source file preserved. This is a bug; please report.");
        }
    }

    private sealed class HashingStream : Stream
    {
        private readonly IncrementalHash _hasher;
        public HashingStream(IncrementalHash hasher) { _hasher = hasher; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _hasher.AppendData(buffer, offset, count);
    }
}
```

- [ ] **Step 3: Run tests, commit**

Run: `git -C d:/projects/winix add src/Winix.Protect/RoundTripVerifier.cs tests/Winix.Protect.Tests/RoundTripVerifierTests.cs`
Run: `git -C d:/projects/winix commit -m "feat(protect): add RoundTripVerifier with streaming SHA-256"`

---

## Task 17: `InPlaceExecutor`

Temp + verify + atomic rename for `--in-place`.

**Files:**
- Create: `src/Winix.Protect/InPlaceExecutor.cs`
- Create: `tests/Winix.Protect.Tests/InPlaceExecutorTests.cs`

- [ ] **Step 1: Write tests**

```csharp
#nullable enable
using System;
using System.IO;
using System.Linq;
using Xunit;
using Winix.Protect;
using Winix.SecretStore;

namespace Winix.Protect.Tests;

public class InPlaceExecutorTests
{
    private sealed class TestAeadBackend : AeadBackend
    {
        public TestAeadBackend(ISecretStore store) : base(store, PlatformMarker.MacKeychainUser, "test-ns", "test-key") { }
    }

    private static string MakeTempFile(string contents)
    {
        string path = Path.Combine(Path.GetTempPath(), $"winix-protect-test-{Guid.NewGuid():N}");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void Encrypt_InPlace_ReplacesFile()
    {
        string path = MakeTempFile("hello world");
        try
        {
            TestAeadBackend backend = new(new NullSecretStore());
            InPlaceExecutor.ExecuteEncrypt(path, backend, verify: true);
            byte[] onDisk = File.ReadAllBytes(path);
            Assert.Equal((byte)'W', onDisk[0]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Encrypt_InPlace_LeavesNoTempFile()
    {
        string path = MakeTempFile("hello");
        try
        {
            TestAeadBackend backend = new(new NullSecretStore());
            InPlaceExecutor.ExecuteEncrypt(path, backend, verify: true);
            string leftover = Directory.GetFiles(Path.GetDirectoryName(path)!, "*.winix-tmp.*")
                .FirstOrDefault(f => Path.GetFileName(f).StartsWith(Path.GetFileName(path)))
                ?? string.Empty;
            Assert.Equal(string.Empty, leftover);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
```

- [ ] **Step 2: Implement `InPlaceExecutor.cs`**

```csharp
#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;

namespace Winix.Protect;

public static class InPlaceExecutor
{
    public static void ExecuteEncrypt(string targetPath, IProtectBackend backend, bool verify)
    {
        string targetAbs = Path.GetFullPath(targetPath);
        string directory = Path.GetDirectoryName(targetAbs) ?? ".";
        string tempPath = Path.Combine(directory, $"{Path.GetFileName(targetAbs)}.winix-tmp.{Environment.ProcessId}.{Guid.NewGuid():N}");

        byte[] sourceHash;

        try
        {
            using (FileStream source = new(targetAbs, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream dest = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] header = [(byte)'W', (byte)'P', (byte)'R', (byte)'T', 0x01, (byte)backend.Marker];
                using TeeReadStream teeSource = new(source, hasher);
                ChunkWriter.Write(teeSource, dest, backend, header);
                sourceHash = hasher.GetCurrentHash();
            }

            if (verify)
            {
                using FileStream encrypted = new(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                RoundTripVerifier.Verify(encrypted, backend, sourceHash);
            }

            File.Move(tempPath, targetAbs, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    public static void ExecuteDecrypt(string targetPath, IProtectBackend backend)
    {
        string targetAbs = Path.GetFullPath(targetPath);
        string directory = Path.GetDirectoryName(targetAbs) ?? ".";
        string tempPath = Path.Combine(directory, $"{Path.GetFileName(targetAbs)}.winix-tmp.{Environment.ProcessId}.{Guid.NewGuid():N}");

        try
        {
            using (FileStream source = new(targetAbs, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream dest = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Header.ReadResult hdr = Header.Read(source);
                byte[] headerBytes = [(byte)'W', (byte)'P', (byte)'R', (byte)'T', hdr.Version, (byte)hdr.Marker];
                ChunkReader.Read(source, dest, backend, headerBytes);
            }
            File.Move(tempPath, targetAbs, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    private sealed class TeeReadStream : Stream
    {
        private readonly Stream _underlying;
        private readonly IncrementalHash _hasher;

        public TeeReadStream(Stream underlying, IncrementalHash hasher)
        {
            _underlying = underlying;
            _hasher = hasher;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _underlying.Length;
        public override long Position { get => _underlying.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _underlying.Read(buffer, offset, count);
            if (n > 0) _hasher.AppendData(buffer, offset, n);
            return n;
        }
    }
}
```

- [ ] **Step 3: Run tests, commit**

Run: `git -C d:/projects/winix add src/Winix.Protect/InPlaceExecutor.cs tests/Winix.Protect.Tests/InPlaceExecutorTests.cs`
Run: `git -C d:/projects/winix commit -m "feat(protect): add InPlaceExecutor with temp+verify+atomic-rename"`

---

## Task 18: `ArgParser`

**Files:**
- Create: `src/Winix.Protect/ArgParser.cs`
- Create: `tests/Winix.Protect.Tests/ArgParserTests.cs`

- [ ] **Step 1: Write tests**

```csharp
#nullable enable
using System.IO;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

public class ArgParserTests
{
    [Fact]
    public void Parse_NoArgs_NullInputOutput_StreamingMode()
    {
        ArgParser.Result r = ArgParser.Parse([], SubCommand.Protect);
        Assert.Null(r.Error);
        Assert.Null(r.Options!.InputPath);
        Assert.Null(r.Options.OutputPath);
        Assert.False(r.Options.InPlace);
        Assert.False(r.Options.RemoveSource);
        Assert.Equal(Scope.User, r.Options.Scope);
        Assert.False(r.Options.NoVerify);
    }

    [Fact]
    public void Parse_FilePositional_SetsInputPath()
    {
        ArgParser.Result r = ArgParser.Parse(["file.txt"], SubCommand.Protect);
        Assert.Equal("file.txt", r.Options!.InputPath);
    }

    [Fact]
    public void Parse_OutputFlag()
    {
        ArgParser.Result r = ArgParser.Parse(["in.txt", "-o", "out.prot"], SubCommand.Protect);
        Assert.Equal("in.txt", r.Options!.InputPath);
        Assert.Equal("out.prot", r.Options.OutputPath);
    }

    [Fact]
    public void Parse_InPlaceFlag()
    {
        ArgParser.Result r = ArgParser.Parse(["file.txt", "--in-place"], SubCommand.Protect);
        Assert.True(r.Options!.InPlace);
    }

    [Fact]
    public void Parse_RemoveSourceFlag()
    {
        ArgParser.Result r = ArgParser.Parse(["file.txt", "--rm"], SubCommand.Protect);
        Assert.True(r.Options!.RemoveSource);
    }

    [Fact]
    public void Parse_MachineScope()
    {
        ArgParser.Result r = ArgParser.Parse(["--scope", "machine"], SubCommand.Protect);
        Assert.Equal(Scope.Machine, r.Options!.Scope);
    }

    [Fact]
    public void Parse_NoVerifyFlag()
    {
        ArgParser.Result r = ArgParser.Parse(["--no-verify"], SubCommand.Protect);
        Assert.True(r.Options!.NoVerify);
    }

    [Fact]
    public void Parse_InputEqualsOutput_Errors()
    {
        string abs = Path.Combine(Path.GetTempPath(), "x.txt");
        ArgParser.Result r = ArgParser.Parse([abs, "-o", abs], SubCommand.Protect);
        Assert.NotNull(r.Error);
        Assert.Contains("same", r.Error!);
    }

    [Fact]
    public void Parse_InPlaceAndOutput_Errors()
    {
        ArgParser.Result r = ArgParser.Parse(["file.txt", "--in-place", "-o", "other.prot"], SubCommand.Protect);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Parse_UnknownScope_Errors()
    {
        ArgParser.Result r = ArgParser.Parse(["--scope", "process"], SubCommand.Protect);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Parse_Help_Flag()
    {
        ArgParser.Result r = ArgParser.Parse(["--help"], SubCommand.Protect);
        Assert.True(r.ShowHelp);
    }

    [Fact]
    public void Parse_Version_Flag()
    {
        ArgParser.Result r = ArgParser.Parse(["--version"], SubCommand.Protect);
        Assert.True(r.ShowVersion);
    }

    [Fact]
    public void Parse_Describe_Flag()
    {
        ArgParser.Result r = ArgParser.Parse(["--describe"], SubCommand.Protect);
        Assert.True(r.ShowDescribe);
    }
}
```

- [ ] **Step 2: Implement `ArgParser.cs`**

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace Winix.Protect;

public static class ArgParser
{
    public sealed record Result(
        ProtectOptions? Options,
        string? Error,
        bool ShowHelp,
        bool ShowVersion,
        bool ShowDescribe);

    public static Result Parse(IReadOnlyList<string> argv, SubCommand subCommand)
    {
        foreach (string a in argv)
        {
            if (a == "--help" || a == "-h") return new Result(null, null, true, false, false);
            if (a == "--version") return new Result(null, null, false, true, false);
            if (a == "--describe") return new Result(null, null, false, false, true);
        }

        string? inputPath = null;
        string? outputPath = null;
        bool inPlace = false;
        bool removeSource = false;
        Scope scope = Scope.User;
        bool noVerify = false;

        for (int i = 0; i < argv.Count; i++)
        {
            string a = argv[i];
            switch (a)
            {
                case "-o":
                case "--output":
                    if (++i >= argv.Count) return Err($"{a} requires a value");
                    outputPath = argv[i];
                    continue;
                case "--in-place":
                    inPlace = true;
                    continue;
                case "--rm":
                case "--remove-source":
                    removeSource = true;
                    continue;
                case "--keep":
                case "-k":
                    // Redundant with default, but accepted.
                    continue;
                case "--scope":
                    if (++i >= argv.Count) return Err("--scope requires a value");
                    scope = argv[i] switch
                    {
                        "user" => Scope.User,
                        "machine" => Scope.Machine,
                        _ => throw new ArgumentException($"unknown --scope value: {argv[i]}"),
                    };
                    continue;
                case "--no-verify":
                    noVerify = true;
                    continue;
                case "--color":
                case "--no-color":
                    continue;
            }

            if (!a.StartsWith('-'))
            {
                if (inputPath is not null) return Err($"unexpected positional argument: {a}");
                inputPath = a;
                continue;
            }

            return Err($"unknown option: {a}");
        }

        if (inPlace && outputPath is not null)
        {
            return Err("--in-place and --output are mutually exclusive");
        }
        if (inputPath is not null && outputPath is not null)
        {
            string inAbs = Path.GetFullPath(inputPath);
            string outAbs = Path.GetFullPath(outputPath);
            if (string.Equals(inAbs, outAbs, StringComparison.OrdinalIgnoreCase))
            {
                return Err("input and output paths are the same. Use '-o different-path' or '--in-place'");
            }
        }

        ProtectOptions options = new(subCommand, inputPath, outputPath, inPlace, removeSource, scope, noVerify);
        return new Result(options, null, false, false, false);
    }

    private static Result Err(string msg)
        => new(null, msg, false, false, false);
}
```

- [ ] **Step 3: Run tests, commit**

Run: `dotnet test d:/projects/winix/tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter FullyQualifiedName~ArgParserTests`
Expected: 13 tests pass.

Run: `git -C d:/projects/winix add src/Winix.Protect/ArgParser.cs tests/Winix.Protect.Tests/ArgParserTests.cs`
Run: `git -C d:/projects/winix commit -m "feat(protect): add ArgParser with flags and subcommand dispatch"`

---

## Task 19: `Formatting`

**Files:**
- Create: `src/Winix.Protect/Formatting.cs`
- Create: `tests/Winix.Protect.Tests/FormattingTests.cs`

- [ ] **Step 1: Write tests**

```csharp
#nullable enable
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

public class FormattingTests
{
    [Fact]
    public void UsageError_Protect_Prefix()
    {
        Assert.Equal("protect: bad flag", Formatting.UsageError("protect", "bad flag"));
    }

    [Fact]
    public void UsageError_Unprotect_Prefix()
    {
        Assert.Equal("unprotect: bad flag", Formatting.UsageError("unprotect", "bad flag"));
    }

    [Fact]
    public void RuntimeError_IncludesPrefix()
    {
        Assert.Equal("protect: decryption failed", Formatting.RuntimeError("protect", "decryption failed"));
    }
}
```

- [ ] **Step 2: Implement `Formatting.cs`**

```csharp
#nullable enable
namespace Winix.Protect;

public static class Formatting
{
    public static string UsageError(string invocationName, string message) => $"{invocationName}: {message}";
    public static string RuntimeError(string invocationName, string message) => $"{invocationName}: {message}";
}
```

- [ ] **Step 3: Run tests, commit**

Run: `git -C d:/projects/winix add src/Winix.Protect/Formatting.cs tests/Winix.Protect.Tests/FormattingTests.cs`
Run: `git -C d:/projects/winix commit -m "feat(protect): add Formatting helper for error messages"`

---

## Task 20: `Cli.Run` — orchestrator

Replace the stub from Task 1 with the full orchestrator.

**File:**
- Modify: `src/Winix.Protect/Cli.cs`

- [ ] **Step 1: Replace `Cli.cs`**

```csharp
#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using Yort.ShellKit;

namespace Winix.Protect;

public static class Cli
{
    private const int RuntimeErrorExit = 126;

    public static int Run(string[] args, string invocationName)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        SubCommand subCommand = invocationName == "unprotect" ? SubCommand.Unprotect : SubCommand.Protect;
        ArgParser.Result parsed = ArgParser.Parse(args, subCommand);

        if (parsed.ShowHelp) { PrintHelp(invocationName); return ExitCode.Success; }
        if (parsed.ShowVersion) { Console.Out.WriteLine($"{invocationName} {typeof(Cli).Assembly.GetName().Version}"); return ExitCode.Success; }
        if (parsed.ShowDescribe) { PrintDescribe(invocationName); return ExitCode.Success; }

        if (parsed.Error is not null)
        {
            Console.Error.WriteLine(Formatting.UsageError(invocationName, parsed.Error));
            return ExitCode.UsageError;
        }

        ProtectOptions opts = parsed.Options!;

        try
        {
            if (subCommand == SubCommand.Protect)
            {
                return RunProtect(opts, invocationName);
            }
            return RunUnprotect(opts, invocationName);
        }
        catch (PlatformNotSupportedException ex)
        {
            Console.Error.WriteLine(Formatting.UsageError(invocationName, ex.Message));
            return ExitCode.UsageError;
        }
        catch (CryptographicException ex)
        {
            Console.Error.WriteLine(Formatting.RuntimeError(invocationName,
                $"decryption failed — this file was encrypted by a different user or on a different machine ({ex.GetType().Name})."));
            return RuntimeErrorExit;
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine(Formatting.RuntimeError(invocationName, ex.Message));
            return RuntimeErrorExit;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(Formatting.RuntimeError(invocationName, ex.Message));
            return RuntimeErrorExit;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine(Formatting.RuntimeError(invocationName, ex.Message));
            return RuntimeErrorExit;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Formatting.RuntimeError(invocationName, $"error: {ex.Message}"));
            return 1;
        }
    }

    private static int RunProtect(ProtectOptions opts, string invocationName)
    {
        IProtectBackend backend = BackendFactory.Create(opts.Scope);

        if (opts.InPlace)
        {
            if (opts.InputPath is null)
            {
                Console.Error.WriteLine(Formatting.UsageError(invocationName, "--in-place requires a file argument"));
                return ExitCode.UsageError;
            }
            InPlaceExecutor.ExecuteEncrypt(opts.InputPath, backend, verify: !opts.NoVerify);
            // Source was replaced in-place; --rm is implied. Nothing more to do.
            return ExitCode.Success;
        }

        // File-operand or streaming mode.
        Stream input = opts.InputPath is not null
            ? new FileStream(opts.InputPath, FileMode.Open, FileAccess.Read, FileShare.Read)
            : Console.OpenStandardInput();

        string? outputPath = opts.OutputPath ?? (opts.InputPath is not null ? opts.InputPath + ".prot" : null);

        using (input)
        {
            if (outputPath is not null)
            {
                byte[] sourceHash;
                using (FileStream dest = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    byte[] header = [(byte)'W', (byte)'P', (byte)'R', (byte)'T', 0x01, (byte)backend.Marker];
                    using TeeStream tee = new(input, hasher);
                    ChunkWriter.Write(tee, dest, backend, header);
                    sourceHash = hasher.GetCurrentHash();
                }

                if (!opts.NoVerify)
                {
                    using FileStream encrypted = new(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    RoundTripVerifier.Verify(encrypted, backend, sourceHash);
                }
            }
            else
            {
                byte[] header = [(byte)'W', (byte)'P', (byte)'R', (byte)'T', 0x01, (byte)backend.Marker];
                using Stream stdout = Console.OpenStandardOutput();
                ChunkWriter.Write(input, stdout, backend, header);
            }
        }

        if (opts.RemoveSource && opts.InputPath is not null)
        {
            File.Delete(opts.InputPath);
        }
        else if (opts.InputPath is not null)
        {
            Console.Error.WriteLine($"{invocationName}: plaintext retained at {opts.InputPath}. Use --rm to remove after encryption.");
        }

        return ExitCode.Success;
    }

    private static int RunUnprotect(ProtectOptions opts, string invocationName)
    {
        // Open input + read header to determine backend.
        Stream input = opts.InputPath is not null
            ? new FileStream(opts.InputPath, FileMode.Open, FileAccess.Read, FileShare.Read)
            : Console.OpenStandardInput();

        using (input)
        {
            Header.ReadResult hdr = Header.Read(input);
            IProtectBackend backend = BackendFactory.CreateForMarker(hdr.Marker);
            byte[] headerBytes = [(byte)'W', (byte)'P', (byte)'R', (byte)'T', hdr.Version, (byte)hdr.Marker];

            string? outputPath = opts.OutputPath;
            if (outputPath is null && opts.InputPath is not null && opts.InputPath.EndsWith(".prot"))
            {
                outputPath = opts.InputPath.Substring(0, opts.InputPath.Length - ".prot".Length);
            }

            if (opts.InPlace)
            {
                // Use a fresh InPlaceExecutor pass after we've already consumed the header — easier to redo.
                // Close current stream first.
                input.Dispose();
                InPlaceExecutor.ExecuteDecrypt(opts.InputPath!, backend);
                return ExitCode.Success;
            }

            if (outputPath is not null)
            {
                using FileStream dest = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                ChunkReader.Read(input, dest, backend, headerBytes);
            }
            else
            {
                using Stream stdout = Console.OpenStandardOutput();
                ChunkReader.Read(input, stdout, backend, headerBytes);
            }
        }

        if (opts.RemoveSource && opts.InputPath is not null)
        {
            File.Delete(opts.InputPath);
        }

        return ExitCode.Success;
    }

    private static void PrintHelp(string invocationName)
    {
        Console.Out.WriteLine($"""
            {invocationName} — cross-platform encrypt-at-rest CLI.

            Usage:
              {invocationName} [OPTIONS] FILE         File-operand mode (produces FILE.prot on protect)
              {invocationName} [OPTIONS] FILE --in-place    Replace FILE atomically (temp + verify + rename)
              {invocationName} [OPTIONS] < stream > out     Streaming mode

            Options:
              -o PATH, --output PATH            Output path
              --in-place                        Encrypt/decrypt over the input file
              --rm, --remove-source             Delete source after successful operation
              --keep, -k                        Keep source (explicit default)
              --scope {{user,machine}}           Key-derivation scope (default user).
                                                Windows: DPAPI CurrentUser / LocalMachine.
                                                macOS: login / System Keychain (sudo for machine).
                                                Linux: user only (machine fails fast).
              --no-verify                       Skip round-trip integrity check (encrypt path only)
              --describe, --help, --version     Suite-standard flags.
              --color, --no-color               Colour control (respects NO_COLOR).

            Exit codes: 0 success; 125 usage error; 126 runtime error.
            """);
    }

    private static void PrintDescribe(string invocationName)
    {
        Console.Out.WriteLine($$"""
            {
              "name": "{{invocationName}}",
              "description": "Cross-platform {{(invocationName == "protect" ? "encrypt-at-rest" : "decrypt-at-rest")}} CLI wrapping native OS key-storage primitives.",
              "platforms": {
                "windows": "DPAPI (CurrentUser / LocalMachine)",
                "macos": "Keychain (login / System) + AES-256-GCM",
                "linux": "libsecret + AES-256-GCM (user scope only in v1)"
              }
            }
            """);
    }

    private sealed class TeeStream : Stream
    {
        private readonly Stream _underlying;
        private readonly IncrementalHash _hasher;
        public TeeStream(Stream underlying, IncrementalHash hasher) { _underlying = underlying; _hasher = hasher; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _underlying.Length;
        public override long Position { get => _underlying.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _underlying.Read(buffer, offset, count);
            if (n > 0) _hasher.AppendData(buffer, offset, n);
            return n;
        }
    }
}
```

- [ ] **Step 2: Build + smoke test**

Run: `dotnet build d:/projects/winix/Winix.sln`
Expected: 0 warnings, 0 errors.

Smoke 1: `echo hello | dotnet run --project d:/projects/winix/src/protect/protect.csproj -- -o d:/projects/winix/tmp/test.prot`
Expected: exit 0. File exists at `tmp/test.prot` with `WPRT` magic (check with `xxd d:/projects/winix/tmp/test.prot | head -1`).

Smoke 2: `dotnet run --project d:/projects/winix/src/unprotect/unprotect.csproj -- d:/projects/winix/tmp/test.prot -o d:/projects/winix/tmp/test.decrypted`
Expected: exit 0. Decrypted file contains `hello`.

Smoke 3 (in-place): Create a file, encrypt in-place, decrypt in-place, verify content matches original.

- [ ] **Step 3: Full-suite run**

Run: `dotnet test d:/projects/winix/Winix.sln 2>&1 | tail -10`
Expected: all prior tests still pass. New protect tests pass.

- [ ] **Step 4: Commit**

Run: `git -C d:/projects/winix add src/Winix.Protect/Cli.cs`
Run: `git -C d:/projects/winix commit -m "feat(protect): wire Cli.Run orchestrator for protect + unprotect"`

---

## Task 21: README + man pages

Replace placeholders from Task 1 with full documentation. Reference: `src/qr/README.md` and `src/url/README.md` for the pattern.

**Files:**
- Modify: `src/protect/README.md`
- Modify: `src/unprotect/README.md`
- Modify: `src/protect/man/man1/protect.1`
- Modify: `src/unprotect/man/man1/unprotect.1`

- [ ] **Step 1: Replace `src/protect/README.md`**

```markdown
# protect

Cross-platform encrypt-at-rest CLI. Wraps each OS's native secret-store primitive:
- **Windows**: DPAPI (CurrentUser or LocalMachine)
- **macOS**: AES-256-GCM with a key stored in the login or System Keychain
- **Linux**: AES-256-GCM with a key stored via libsecret (user scope only)

Files are **not portable** between machines or users — scoped to "this user on this machine" by default, or "this machine" with `--scope machine`.

## Why

Windows' DPAPI has no convenient CLI. macOS and Linux have credential vaults but no native "encrypt this file with my login" tool. `protect` is that missing tool.

## Install

### Scoop (Windows)
```
scoop bucket add winix https://github.com/Yortw/winix
scoop install protect unprotect
```

### .NET global tool
```
dotnet tool install --global Winix.Protect
dotnet tool install --global Winix.Unprotect
```

### Native binary (GitHub Releases)
Download from [releases](https://github.com/Yortw/winix/releases).

## Usage

```bash
protect FILE                              # FILE.prot alongside; keeps FILE
protect FILE --rm                         # FILE.prot alongside; removes FILE
protect FILE --in-place                   # replace FILE atomically
protect FILE -o out.prot                  # explicit output
protect < plain > out.prot                # streaming

protect FILE --scope machine              # Windows/macOS: service-account scenarios
unprotect FILE.prot                       # decrypts to FILE
unprotect FILE.prot -o /tmp/plain.txt     # explicit output
unprotect FILE.prot --in-place            # decrypt over .prot file
```

## Options

| Flag | Default | Description |
|---|---|---|
| `-o PATH` / `--output PATH` | stdout, or `FILE.prot` | Explicit output path. |
| `--in-place` | off | Replace the input atomically (temp + verify + rename). |
| `--rm` / `--remove-source` | off | Delete source after success. |
| `--keep` / `-k` | default | Explicit keep (redundant; useful in scripts). |
| `--scope {user,machine}` | `user` | Key-derivation scope. Linux: machine fails fast. |
| `--no-verify` | off | Skip round-trip verification (encrypt only). |
| `--describe`, `--help`, `--version` | — | Standard introspection. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 125 | Usage error. |
| 126 | Runtime error (decrypt failure, wrong platform/scope, key store unavailable). |

## Security

- Files encrypted on machine A cannot be decrypted on machine B — this is by design.
- `--in-place` writes to a temp file first, verifies the round-trip, then atomically renames. Source is preserved on any failure.
- Round-trip verification catches bugs in our chunking/framing code that would otherwise silently corrupt. Disable with `--no-verify` if you've measured and want the speed.
- Shell redirection to the same file (`protect < f > f`) is a known Unix gotcha — the shell truncates `f` before `protect` reads it. Use `-o` or file-operand form.

## Colour

`protect`/`unprotect` emit no ANSI by default — this is a streaming/file tool. `NO_COLOR` respected for consistency.
```

- [ ] **Step 2: Replace `src/unprotect/README.md`**

```markdown
# unprotect

Companion tool to `protect`. Decrypts files that `protect` encrypted, using the same native OS primitives.

See [protect](https://github.com/Yortw/winix/blob/main/src/protect/README.md) for the full usage and install guide.

## Usage

```bash
unprotect FILE.prot                       # decrypts to FILE (strips .prot)
unprotect FILE.prot -o OUT                # explicit output
unprotect FILE.prot --in-place            # decrypt over the input file
unprotect < encrypted.prot > plain        # streaming
```

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 125 | Usage error. |
| 126 | Runtime error (decryption failure, wrong-platform file, wrong-user/wrong-scope file). |
```

- [ ] **Step 3: Replace `src/protect/man/man1/protect.1`**

```groff
.TH PROTECT 1 "2026" "Winix" "User Commands"
.SH NAME
protect \- cross-platform encrypt-at-rest CLI using native OS key stores
.SH SYNOPSIS
.B protect
[\fIOPTIONS\fR] [\fIFILE\fR]
.SH DESCRIPTION
.B protect
encrypts data at rest using the operating system's native key-storage primitive: DPAPI on Windows, Keychain on macOS, libsecret on Linux. The user never manages keys; the OS provides them. Files are not portable between machines or users.
.SH OPTIONS
.TP
\fB-o\fR \fIPATH\fR, \fB--output\fR \fIPATH\fR
Explicit output path. Defaults to stdout (streaming) or \fIFILE.prot\fR (file-operand).
.TP
\fB--in-place\fR
Encrypt over the input file atomically (temp + verify + rename).
.TP
\fB--rm\fR, \fB--remove-source\fR
Delete source file after successful encryption.
.TP
\fB--scope\fR \fI{user,machine}\fR
Key scope. \fBuser\fR (default) ties the key to this user on this machine. \fBmachine\fR (Windows + macOS only; requires sudo on macOS) uses a machine-wide key.
.TP
\fB--no-verify\fR
Skip round-trip verification. Faster but loses the integrity check.
.SH EXIT STATUS
.TP
.B 0
Success.
.TP
.B 125
Usage error (bad flags, path collision, unsupported platform combination).
.TP
.B 126
Runtime error (decryption failure, OS key store unavailable, permission denied).
.SH SEE ALSO
.BR unprotect (1),
.BR digest (1),
.BR clip (1).
```

- [ ] **Step 4: Replace `src/unprotect/man/man1/unprotect.1`**

```groff
.TH UNPROTECT 1 "2026" "Winix" "User Commands"
.SH NAME
unprotect \- decrypt files encrypted by protect
.SH SYNOPSIS
.B unprotect
[\fIOPTIONS\fR] [\fIFILE\fR]
.SH DESCRIPTION
.B unprotect
decrypts files produced by \fBprotect\fR. Uses the platform-marker byte in the file header to select the appropriate backend and fail helpfully if a file was moved between platforms or scopes.
.SH OPTIONS
See \fBprotect\fR(1) for the shared flag surface.
.SH EXIT STATUS
Same as \fBprotect\fR(1).
.SH SEE ALSO
.BR protect (1).
```

- [ ] **Step 5: Commit**

Run: `git -C d:/projects/winix add src/protect/README.md src/unprotect/README.md src/protect/man/man1/protect.1 src/unprotect/man/man1/unprotect.1`
Run: `git -C d:/projects/winix commit -m "docs(protect): add README and man pages for protect + unprotect"`

---

## Task 22: AI guide + `llms.txt`

**Files:**
- Create: `docs/ai/protect.md`
- Create: `docs/ai/unprotect.md`
- Modify: `llms.txt`

- [ ] **Step 1: Read an existing AI guide for pattern reference**

Read `docs/ai/qr.md` or `docs/ai/digest.md`.

- [ ] **Step 2: Create `docs/ai/protect.md`**

```markdown
# protect — AI agent guide

## TL;DR

`protect` encrypts a file or stream at rest using the OS-native key store (DPAPI on Windows, Keychain on macOS, libsecret on Linux). The user never sees or manages keys.

## Typical invocations

```
protect config.json                            # config.json.prot alongside
protect config.json --rm                       # removes plaintext
protect config.json --in-place                 # replaces in place
cat secret.txt | protect > secret.prot         # streaming
protect config.json --scope machine            # Windows/macOS only; sudo on macOS
```

## Key constraints for agents

- **Files are not portable.** A .prot file works only for the user who encrypted it on the machine that encrypted it. Do not recommend `protect` for cross-machine sharing — recommend `age`, `gpg`, or `sops`.
- **Windows machine scope** is useful for service-account scenarios (encrypt as admin, decrypt as service). **Linux machine scope is not supported** — it fails fast with a clear message.
- **`--in-place` is safe** via temp + round-trip verify + atomic rename. If anything fails, the source is preserved.
- **Round-trip verify is on by default.** `--no-verify` only if you've measured and need the speed.

## Exit-code contract

- 0 — success
- 125 — usage error
- 126 — runtime error (decryption failure, wrong scope/platform, OS key store unavailable)

## Composition

`protect` is meant to compose. Common patterns:
```
# Encrypted HMAC key via digest:
unprotect < api.key.prot | digest --hmac sha256 --key-stdin "payload"

# Encrypt + clip-pipe:
cat secret.txt | protect | clip

# In-place on a config file:
protect ~/.config/myapp/config.json --in-place
```

## What to suggest when users need portability

`protect` does not do portable encryption — redirect them to `age` / `gpg` / `sops` depending on their workflow.
```

- [ ] **Step 3: Create `docs/ai/unprotect.md`**

```markdown
# unprotect — AI agent guide

Companion to `protect`. Decrypts `.prot` files using the same native OS primitives.

See `docs/ai/protect.md` for the shared behaviour. Usage is symmetric.

## Typical invocations

```
unprotect config.json.prot                     # creates config.json
unprotect config.json.prot -o config.json.new
unprotect < stream.prot > plain
unprotect config.json.prot --in-place
```

## Decrypt-time error modes

- **Wrong platform**: file was created on Windows, you're on Linux → exit 126, helpful message.
- **Wrong user/scope**: file was encrypted under user scope by user A; user B cannot decrypt even on the same machine → exit 126.
- **Truncated file**: final chunk missing → exit 126 with "ciphertext is truncated".
- **Tampered file**: AEAD tag or DPAPI integrity fails → exit 126.
```

- [ ] **Step 4: Add entries to `llms.txt`**

Read `llms.txt` to understand format. Insert alphabetically:

```
- `protect` — cross-platform encrypt-at-rest using OS-native key stores (DPAPI/Keychain/libsecret). Offline, no user key management, files are NOT portable between machines. See `docs/ai/protect.md`.
- `unprotect` — companion to `protect`; decrypts .prot files. See `docs/ai/unprotect.md`.
```

- [ ] **Step 5: Commit**

Run: `git -C d:/projects/winix add docs/ai/protect.md docs/ai/unprotect.md llms.txt`
Run: `git -C d:/projects/winix commit -m "docs(protect): add AI agent guides and llms.txt entries"`

---

## Task 23: Pipeline wiring

Add `protect` and `unprotect` to scoop, release workflow, and post-publish workflow. Mirrors the `qr` pattern.

**Files:**
- Create: `bucket/protect.json`
- Create: `bucket/unprotect.json`
- Modify: `.github/workflows/release.yml`
- Modify: `.github/workflows/post-publish.yml`

- [ ] **Step 1: Read existing patterns**

Run: `grep -n "qr" d:/projects/winix/.github/workflows/release.yml`
Run: `grep -n "qr" d:/projects/winix/.github/workflows/post-publish.yml`
Read: `d:/projects/winix/bucket/qr.json`

- [ ] **Step 2: Create `bucket/protect.json` (mirror `bucket/qr.json`)**

Use `qr.json` as a template; substitute `qr` → `protect`, update description to "Cross-platform encrypt-at-rest CLI using OS-native key stores." Preserve placeholder version/hash (pipeline overwrites).

- [ ] **Step 3: Create `bucket/unprotect.json`**

Same approach. Description: "Companion tool to protect — decrypts files that protect encrypted."

- [ ] **Step 4: Modify `.github/workflows/release.yml`**

For every location where `qr` appears, add equivalent `protect` AND `unprotect` entries:
1. Per-RID `dotnet publish` — add `src/protect/protect.csproj` and `src/unprotect/unprotect.csproj`
2. Per-tool `dotnet pack` — add two pack steps
3. Per-tool zip step — add two zip steps
4. Combined-zip `Copy-Item` — add two lines
5. Tool-map `tools: { … }` entry — add `protect` and `unprotect`

- [ ] **Step 5: Modify `.github/workflows/post-publish.yml`**

Add two lines each for `update_manifest` and `generate_manifests`:

```
update_manifest bucket/protect.json aot/protect-win-x64.zip
update_manifest bucket/unprotect.json aot/unprotect-win-x64.zip
generate_manifests "protect" "Protect" "Cross-platform encrypt-at-rest CLI using OS-native key stores." "encryption,dpapi,keychain,libsecret,at-rest,crypto"
generate_manifests "unprotect" "Unprotect" "Companion tool to protect — decrypts files that protect encrypted." "decryption,dpapi,keychain,libsecret,at-rest,crypto"
```

- [ ] **Step 6: Commit**

Run: `git -C d:/projects/winix add bucket/protect.json bucket/unprotect.json .github/workflows/release.yml .github/workflows/post-publish.yml`
Run: `git -C d:/projects/winix commit -m "ci(protect): wire protect + unprotect into release and post-publish pipelines"`

---

## Task 24: CLAUDE.md updates

**File:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update NuGet package IDs list**

Append `Winix.Protect` and `Winix.Unprotect` to the comma-separated list.

- [ ] **Step 2: Update scoop manifests list**

Append `protect.json` and `unprotect.json`.

- [ ] **Step 3: Update project-layout section**

Add entries (match existing indentation):

```
src/Winix.SecretStore/     — shared library (ISecretStore abstraction; Cred Manager / Keychain / libsecret backends)
src/Winix.Protect/         — class library (backends, chunk stream orchestration, in-place executor)
src/protect/               — console app entry point (PackageId Winix.Protect)
src/unprotect/             — console app entry point (PackageId Winix.Unprotect, same library)
```

Test directories:

```
tests/Winix.SecretStore.Tests/ — xUnit tests
tests/Winix.Protect.Tests/     — xUnit tests
```

- [ ] **Step 4: Commit**

Run: `git -C d:/projects/winix add CLAUDE.md`
Run: `git -C d:/projects/winix commit -m "docs(protect): update CLAUDE.md with protect/unprotect layout"`

---

## Task 25: Full-suite verification + AOT publish

- [ ] **Step 1: Full test run**

Run: `dotnet test d:/projects/winix/Winix.sln 2>&1 | tail -20`
Expected: all tests pass, 0 warnings, 0 errors. New contributions:
- `Winix.SecretStore.Tests`: ~6 tests (NullSecretStore)
- `Winix.Protect.Tests`: ~50 tests (Header 9 + DPAPI 5 conditional + AEAD 5 + BackendFactory 3 + ChunkWriter/Reader 5 + RoundTripVerifier 2 + InPlaceExecutor 2 + ArgParser 13 + Formatting 3)

- [ ] **Step 2: AOT publish (both binaries)**

Run: `dotnet publish d:/projects/winix/src/protect/protect.csproj -c Release -r win-x64 -o d:/projects/winix/artifacts/protect-win-x64`
Run: `dotnet publish d:/projects/winix/src/unprotect/unprotect.csproj -c Release -r win-x64 -o d:/projects/winix/artifacts/unprotect-win-x64`
Expected: 0 warnings, 0 errors. Note: `System.Security.Cryptography.ProtectedData` is AOT-safe on .NET 8+; verify no trim warnings.

- [ ] **Step 3: Run the AOT binaries**

- Encrypt a file:
  ```
  echo hello > d:/projects/winix/tmp/plain.txt
  d:/projects/winix/artifacts/protect-win-x64/protect.exe d:/projects/winix/tmp/plain.txt
  xxd d:/projects/winix/tmp/plain.txt.prot | head -1   # should show 'WPRT' magic
  ```
- Decrypt it:
  ```
  d:/projects/winix/artifacts/unprotect-win-x64/unprotect.exe d:/projects/winix/tmp/plain.txt.prot -o d:/projects/winix/tmp/plain.roundtrip.txt
  diff d:/projects/winix/tmp/plain.txt d:/projects/winix/tmp/plain.roundtrip.txt   # should be identical
  ```
- In-place:
  ```
  echo "secret" > d:/projects/winix/tmp/secret.txt
  d:/projects/winix/artifacts/protect-win-x64/protect.exe d:/projects/winix/tmp/secret.txt --in-place
  xxd d:/projects/winix/tmp/secret.txt | head -1   # now WPRT magic
  d:/projects/winix/artifacts/unprotect-win-x64/unprotect.exe d:/projects/winix/tmp/secret.txt --in-place
  cat d:/projects/winix/tmp/secret.txt   # back to "secret"
  ```
- `--describe`:
  ```
  d:/projects/winix/artifacts/protect-win-x64/protect.exe --describe   # JSON
  d:/projects/winix/artifacts/unprotect-win-x64/unprotect.exe --describe   # JSON
  ```

- [ ] **Step 4: Git status clean**

Run: `git -C d:/projects/winix status`
Expected: working tree clean (apart from `artifacts/`, `dttest/`, `tmp/`).

- [ ] **Step 5: Commit log summary**

Run: `git -C d:/projects/winix log --oneline release/v0.3.0..HEAD`
Report the full list of protect-related commits in order.

