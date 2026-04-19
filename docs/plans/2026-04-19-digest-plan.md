# digest Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `digest`, a cross-platform cryptographic hashing and HMAC CLI. Default output is SHA-256 hex; supports SHA-2 / SHA-3 / SHA-1 / MD5 / BLAKE2b and HMAC variants; four key-input modes with correct-by-default security posture; sha256sum-compatible multi-file output with the `*` binary-mode marker.

**Architecture:** Three-project pattern: `Winix.Digest` class library (options, generators, parser, formatting), `digest` thin console app (parse → run → stdout → exit), `Winix.Digest.Tests` xUnit. Extends the existing `Winix.Codec` shared library with three files (Hex, Base64, ConstantTimeCompare) — the first extension since ids shipped it. All hash primitives from .NET BCL `System.Security.Cryptography` except BLAKE2b (via `Blake2Fast` NuGet, the first external crypto dep in Winix — see ADR §3). HMAC key resolution has four sources (env/file/stdin/literal) with the unsafe literal emitting a non-suppressible stderr warning. Positional arguments are always file paths (no auto-detect); literal-string hashing uses explicit `--string VALUE`.

**Tech Stack:** .NET 10, AOT, xUnit, ShellKit (`CommandLineParser`, `ExitCode`, `ConsoleEnv`, `JsonHelper`), Winix.Codec (Crockford base32 + new Hex/Base64/ConstantTimeCompare), `SauceControl.Blake2Fast` NuGet package (namespace `SauceControl.Blake2Fast`, class `Blake2b`).

**Related:**
- Design: `docs/plans/2026-04-19-digest-design.md`
- ADR: `docs/plans/2026-04-19-digest-adr.md`

---

### Task 1: Project scaffolding

Create the three new projects, add to solution, stub Program.cs. Add `Blake2Fast` NuGet reference up-front so it's available for Task 4.

**Files:**
- Create: `src/Winix.Digest/Winix.Digest.csproj`
- Create: `src/digest/digest.csproj`
- Create: `src/digest/Program.cs` (stub)
- Create: `src/digest/README.md` (placeholder — real README in Task 12)
- Create: `tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj`
- Modify: `Winix.sln`

- [ ] **Step 1: Create `Winix.Digest` csproj**

```xml
<!-- src/Winix.Digest/Winix.Digest.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
    <IsPackable>false</IsPackable>
    <PackageId>Winix.Digest.Library</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
    <ProjectReference Include="..\Winix.Codec\Winix.Codec.csproj" />
    <PackageReference Include="SauceControl.Blake2Fast" Version="2.*" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `digest` console app csproj**

```xml
<!-- src/digest/digest.csproj -->
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
    <ToolCommandName>digest</ToolCommandName>
    <PackageId>Winix.Digest</PackageId>
    <Description>Cross-platform cryptographic hashing and HMAC — SHA-2/SHA-3/BLAKE2b, safe HMAC key handling.</Description>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.Digest\Winix.Digest.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

Note: the `<Content Include="man\man1\digest.1" …/>` line is added in Task 12 when the man page exists.

- [ ] **Step 3: Create stub `Program.cs`**

```csharp
// src/digest/Program.cs
using Yort.ShellKit;

namespace Digest;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        Console.Error.WriteLine("digest: not yet implemented");
        return ExitCode.UsageError;
    }
}
```

- [ ] **Step 4: Create placeholder README**

```markdown
See [main project README](../../README.md). Full tool README populated in a later commit.
```

File: `src/digest/README.md`. This prevents `dotnet pack` from failing on the `<PackageReadmeFile>` reference; Task 12 overwrites it with real content.

- [ ] **Step 5: Create `Winix.Digest.Tests` csproj**

```xml
<!-- tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj -->
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
    <ProjectReference Include="..\..\src\Winix.Digest\Winix.Digest.csproj" />
    <ProjectReference Include="..\..\src\Winix.Codec\Winix.Codec.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Add all three projects to the solution**

```
dotnet sln Winix.sln add src/Winix.Digest/Winix.Digest.csproj --solution-folder src
dotnet sln Winix.sln add src/digest/digest.csproj --solution-folder src
dotnet sln Winix.sln add tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --solution-folder tests
```

- [ ] **Step 7: Verify build**

Run: `dotnet build Winix.sln`
Expected: 0 warnings, 0 errors. `Blake2Fast` restores on first build.

- [ ] **Step 8: Commit**

```
git add src/Winix.Digest src/digest tests/Winix.Digest.Tests Winix.sln
git commit -m "feat(digest): add project scaffolding"
```

---

### Task 2: Winix.Codec extensions (Hex, Base64, ConstantTimeCompare)

Three small primitives added to the existing shared library. Each gets its own file + test class. All three are pure functions — no state, no I/O.

**Files:**
- Create: `src/Winix.Codec/Hex.cs`
- Create: `src/Winix.Codec/Base64.cs`
- Create: `src/Winix.Codec/ConstantTimeCompare.cs`
- Create: `tests/Winix.Codec.Tests/HexTests.cs`
- Create: `tests/Winix.Codec.Tests/Base64Tests.cs`
- Create: `tests/Winix.Codec.Tests/ConstantTimeCompareTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Winix.Codec.Tests/HexTests.cs
using System;
using Xunit;
using Winix.Codec;

namespace Winix.Codec.Tests;

public class HexTests
{
    [Theory]
    [InlineData(new byte[] { }, "")]
    [InlineData(new byte[] { 0x00 }, "00")]
    [InlineData(new byte[] { 0xff }, "ff")]
    [InlineData(new byte[] { 0x01, 0x23, 0x45, 0x67 }, "01234567")]
    [InlineData(new byte[] { 0xde, 0xad, 0xbe, 0xef }, "deadbeef")]
    public void Encode_KnownVectors_LowercaseByDefault(byte[] input, string expected)
    {
        Assert.Equal(expected, Hex.Encode(input));
    }

    [Fact]
    public void Encode_Uppercase_ProducesUppercaseHex()
    {
        Assert.Equal("DEADBEEF", Hex.Encode(new byte[] { 0xde, 0xad, 0xbe, 0xef }, upper: true));
    }

    [Fact]
    public void Decode_RoundTripsEncode()
    {
        byte[] original = new byte[32];
        new Random(42).NextBytes(original);
        Assert.Equal(original, Hex.Decode(Hex.Encode(original)));
    }

    [Theory]
    [InlineData("AbCdEf")]
    [InlineData("abcdef")]
    [InlineData("ABCDEF")]
    public void Decode_MixedCase_Accepted(string input)
    {
        Assert.Equal(new byte[] { 0xab, 0xcd, 0xef }, Hex.Decode(input));
    }

    [Theory]
    [InlineData("abc")]        // odd length
    [InlineData("zz")]         // non-hex chars
    [InlineData("ab cd")]      // whitespace
    public void Decode_Invalid_Throws(string input)
    {
        Assert.Throws<FormatException>(() => Hex.Decode(input));
    }
}
```

```csharp
// tests/Winix.Codec.Tests/Base64Tests.cs
using System;
using Xunit;
using Winix.Codec;

namespace Winix.Codec.Tests;

public class Base64Tests
{
    [Theory]
    [InlineData(new byte[] { }, "")]
    [InlineData(new byte[] { 0x66, 0x6f }, "Zm8=")]
    [InlineData(new byte[] { 0x66, 0x6f, 0x6f }, "Zm9v")]
    [InlineData(new byte[] { 0x66, 0x6f, 0x6f, 0x62 }, "Zm9vYg==")]
    public void Encode_StandardAlphabet_MatchesRfc4648(byte[] input, string expected)
    {
        Assert.Equal(expected, Base64.Encode(input, urlSafe: false));
    }

    [Theory]
    [InlineData(new byte[] { 0xfb }, "-w==", "+w==")]
    [InlineData(new byte[] { 0xff, 0xef }, "_-8=", "/+8=")]
    public void Encode_UrlSafe_UsesDashAndUnderscore(byte[] input, string urlSafe, string standard)
    {
        Assert.Equal(urlSafe, Base64.Encode(input, urlSafe: true));
        Assert.Equal(standard, Base64.Encode(input, urlSafe: false));
    }

    [Fact]
    public void RoundTrip_RandomBytes_MatchesOriginal()
    {
        byte[] original = new byte[64];
        new Random(7).NextBytes(original);
        Assert.Equal(original, Base64.Decode(Base64.Encode(original, urlSafe: false)));
        Assert.Equal(original, Base64.Decode(Base64.Encode(original, urlSafe: true)));
    }
}
```

```csharp
// tests/Winix.Codec.Tests/ConstantTimeCompareTests.cs
using System;
using Xunit;
using Winix.Codec;

namespace Winix.Codec.Tests;

public class ConstantTimeCompareTests
{
    [Fact]
    public void BytesEqual_IdenticalContent_ReturnsTrue()
    {
        byte[] a = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        byte[] b = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        Assert.True(ConstantTimeCompare.BytesEqual(a, b));
    }

    [Fact]
    public void BytesEqual_DifferentContent_ReturnsFalse()
    {
        byte[] a = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        byte[] b = new byte[] { 0x01, 0x02, 0x03, 0x05 };
        Assert.False(ConstantTimeCompare.BytesEqual(a, b));
    }

    [Fact]
    public void BytesEqual_DifferentLengths_ReturnsFalse()
    {
        byte[] a = new byte[] { 0x01, 0x02, 0x03 };
        byte[] b = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        Assert.False(ConstantTimeCompare.BytesEqual(a, b));
    }

    [Theory]
    [InlineData("abc", "abc", true, true)]
    [InlineData("ABC", "abc", false, true)]  // case-insensitive requested
    [InlineData("ABC", "abc", false, false)] // case-sensitive, differs
    [InlineData("abc", "ABC", true, true)]   // case-insensitive requested
    [InlineData("abc", "xyz", false, false)]
    public void StringEquals_HandlesCase(string a, string b, bool caseInsensitive_StillFalseForDifferentContent, bool caseInsensitive)
    {
        bool result = ConstantTimeCompare.StringEquals(a, b, caseInsensitive);
        // For this table: when strings are equal modulo case and caseInsensitive=true, expect true.
        // When strings are genuinely different, expect false regardless.
        bool expected = caseInsensitive
            ? string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
            : string.Equals(a, b, StringComparison.Ordinal);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void StringEquals_NullInput_ReturnsFalse()
    {
        Assert.False(ConstantTimeCompare.StringEquals(null!, "abc", caseInsensitive: false));
        Assert.False(ConstantTimeCompare.StringEquals("abc", null!, caseInsensitive: false));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Codec.Tests/Winix.Codec.Tests.csproj`
Expected: compile errors (types don't exist).

- [ ] **Step 3: Implement `Hex.cs`**

```csharp
// src/Winix.Codec/Hex.cs
using System;

namespace Winix.Codec;

/// <summary>
/// Hex encoding/decoding for cryptographic output. Lowercase by default;
/// <c>upper: true</c> for uppercase. Decode is case-insensitive.
/// </summary>
public static class Hex
{
    private const string LowerAlphabet = "0123456789abcdef";
    private const string UpperAlphabet = "0123456789ABCDEF";

    /// <summary>Encodes bytes as a hex string.</summary>
    public static string Encode(ReadOnlySpan<byte> input, bool upper = false)
    {
        if (input.IsEmpty) return string.Empty;
        string alphabet = upper ? UpperAlphabet : LowerAlphabet;
        Span<char> chars = input.Length <= 128
            ? stackalloc char[input.Length * 2]
            : new char[input.Length * 2];
        for (int i = 0; i < input.Length; i++)
        {
            byte b = input[i];
            chars[i * 2] = alphabet[b >> 4];
            chars[i * 2 + 1] = alphabet[b & 0x0F];
        }
        return new string(chars);
    }

    /// <summary>
    /// Decodes a hex string to bytes. Case-insensitive.
    /// Throws <see cref="FormatException"/> on odd length or non-hex characters.
    /// </summary>
    public static byte[] Decode(string input)
    {
        if (string.IsNullOrEmpty(input)) return Array.Empty<byte>();
        if ((input.Length & 1) != 0)
            throw new FormatException("hex string must have even length");

        byte[] result = new byte[input.Length / 2];
        for (int i = 0; i < result.Length; i++)
        {
            int high = DigitValue(input[i * 2]);
            int low = DigitValue(input[i * 2 + 1]);
            result[i] = (byte)((high << 4) | low);
        }
        return result;
    }

    private static int DigitValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => throw new FormatException($"invalid hex character: '{c}'"),
    };
}
```

- [ ] **Step 4: Implement `Base64.cs`**

```csharp
// src/Winix.Codec/Base64.cs
using System;

namespace Winix.Codec;

/// <summary>
/// Base64 encoding/decoding. Standard alphabet (RFC 4648 §4) or URL-safe variant
/// (RFC 4648 §5, <c>+</c>/<c>/</c> replaced with <c>-</c>/<c>_</c>). Both variants
/// use <c>=</c> padding.
/// </summary>
public static class Base64
{
    /// <summary>Encodes bytes as base64.</summary>
    public static string Encode(ReadOnlySpan<byte> input, bool urlSafe = false)
    {
        string standard = Convert.ToBase64String(input);
        return urlSafe ? standard.Replace('+', '-').Replace('/', '_') : standard;
    }

    /// <summary>Decodes a base64 string (auto-detects URL-safe or standard alphabet).</summary>
    public static byte[] Decode(string input)
    {
        if (string.IsNullOrEmpty(input)) return Array.Empty<byte>();
        // Normalise URL-safe variants back to standard before Convert.FromBase64String.
        string normalised = input.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(normalised);
    }
}
```

- [ ] **Step 5: Implement `ConstantTimeCompare.cs`**

```csharp
// src/Winix.Codec/ConstantTimeCompare.cs
using System;

namespace Winix.Codec;

/// <summary>
/// Timing-safe equality comparison for cryptographic values (HMAC verify, auth tokens).
/// All overloads process the full input length regardless of where differences occur,
/// so timing does not leak which byte position first differed.
/// </summary>
public static class ConstantTimeCompare
{
    /// <summary>
    /// Compares two byte sequences for equality in constant time with respect to
    /// the minimum of the two lengths. Returns false if lengths differ.
    /// </summary>
    public static bool BytesEqual(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }

    /// <summary>
    /// Compares two strings for equality in constant time. Optionally case-insensitive
    /// via ASCII case folding (fast, safe for hex; do not use for general Unicode).
    /// Returns false for null inputs.
    /// </summary>
    public static bool StringEquals(string a, string b, bool caseInsensitive)
    {
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            char ca = a[i];
            char cb = b[i];
            if (caseInsensitive)
            {
                if (ca >= 'A' && ca <= 'Z') ca = (char)(ca + 32);
                if (cb >= 'A' && cb <= 'Z') cb = (char)(cb + 32);
            }
            diff |= ca ^ cb;
        }
        return diff == 0;
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Codec.Tests/Winix.Codec.Tests.csproj`
Expected: all pass (19 pre-existing + ~18 new = ~37 total).

- [ ] **Step 7: Commit**

```
git add src/Winix.Codec/Hex.cs src/Winix.Codec/Base64.cs src/Winix.Codec/ConstantTimeCompare.cs tests/Winix.Codec.Tests/HexTests.cs tests/Winix.Codec.Tests/Base64Tests.cs tests/Winix.Codec.Tests/ConstantTimeCompareTests.cs
git commit -m "feat(codec): add Hex, Base64, and ConstantTimeCompare primitives"
```

---

### Task 3: Core types + test fakes

Pure data types and test doubles. No TDD — types are too simple to warrant it; tests land in later tasks that consume them.

**Files:**
- Create: `src/Winix.Digest/HashAlgorithm.cs`
- Create: `src/Winix.Digest/OutputFormat.cs`
- Create: `src/Winix.Digest/DigestOptions.cs`
- Create: `src/Winix.Digest/IHasher.cs`
- Create: `src/Winix.Digest/InputSource.cs`
- Create: `tests/Winix.Digest.Tests/Fakes/FakeTextReader.cs`

- [ ] **Step 1: Create `HashAlgorithm.cs`**

```csharp
// src/Winix.Digest/HashAlgorithm.cs
namespace Winix.Digest;

/// <summary>Supported hash algorithms.</summary>
public enum HashAlgorithm
{
    /// <summary>SHA-256 (default). Modern, widely supported.</summary>
    Sha256,

    /// <summary>SHA-384. SHA-2 family, 384-bit output.</summary>
    Sha384,

    /// <summary>SHA-512. SHA-2 family, 512-bit output.</summary>
    Sha512,

    /// <summary>SHA-1. Cryptographically broken for collision resistance; warning emitted on use.</summary>
    Sha1,

    /// <summary>MD5. Cryptographically broken; warning emitted on use.</summary>
    Md5,

    /// <summary>SHA3-256. Requires OS crypto backend with SHA-3 support (.NET 8+, newer OSes).</summary>
    Sha3_256,

    /// <summary>SHA3-512.</summary>
    Sha3_512,

    /// <summary>BLAKE2b-512. Provided by the Blake2Fast NuGet package.</summary>
    Blake2b,
}
```

- [ ] **Step 2: Create `OutputFormat.cs`**

```csharp
// src/Winix.Digest/OutputFormat.cs
namespace Winix.Digest;

/// <summary>Output encoding for hash bytes.</summary>
public enum OutputFormat
{
    /// <summary>Hex encoding. Lowercase by default; <see cref="DigestOptions.Uppercase"/> produces uppercase.</summary>
    Hex,

    /// <summary>Base64 with standard alphabet (RFC 4648 §4).</summary>
    Base64,

    /// <summary>Base64 URL-safe variant (RFC 4648 §5).</summary>
    Base64Url,

    /// <summary>Crockford base32 (uppercase, no padding).</summary>
    Base32,
}
```

- [ ] **Step 3: Create `DigestOptions.cs`**

```csharp
// src/Winix.Digest/DigestOptions.cs
using System.Collections.Generic;

namespace Winix.Digest;

/// <summary>
/// Parsed command-line options for <c>digest</c>. Constructed by <see cref="ArgParser"/>
/// after validation; properties are immutable.
/// </summary>
public sealed record DigestOptions(
    HashAlgorithm Algorithm,
    bool IsHmac,
    byte[]? HmacKey,
    OutputFormat Format,
    bool Uppercase,
    InputSource Source,
    string? VerifyExpected,
    bool Json)
{
    /// <summary>Default options for ad-hoc construction in tests.</summary>
    public static DigestOptions Defaults => new(
        Algorithm: HashAlgorithm.Sha256,
        IsHmac: false,
        HmacKey: null,
        Format: OutputFormat.Hex,
        Uppercase: false,
        Source: new StdinInput(),
        VerifyExpected: null,
        Json: false);
}
```

- [ ] **Step 4: Create `IHasher.cs`**

```csharp
// src/Winix.Digest/IHasher.cs
using System;
using System.IO;

namespace Winix.Digest;

/// <summary>
/// Computes a hash (or HMAC) over bytes or a stream. Implementations wrap
/// .NET BCL primitives (SHA-2, SHA-3, SHA-1, MD5, HMAC*) or third-party
/// implementations (BLAKE2b via Blake2Fast).
/// </summary>
public interface IHasher
{
    /// <summary>Hashes the given byte span in one shot.</summary>
    byte[] Hash(ReadOnlySpan<byte> input);

    /// <summary>Hashes the given stream incrementally (no full buffering).</summary>
    byte[] Hash(Stream input);
}
```

- [ ] **Step 5: Create `InputSource.cs`**

```csharp
// src/Winix.Digest/InputSource.cs
using System.Collections.Generic;

namespace Winix.Digest;

/// <summary>
/// Where the bytes to hash come from. One of:
/// <see cref="StringInput"/>, <see cref="StdinInput"/>, <see cref="SingleFileInput"/>, <see cref="MultiFileInput"/>.
/// </summary>
public abstract record InputSource;

/// <summary>Hash a UTF-8 encoded literal string (from <c>--string VALUE</c>).</summary>
public sealed record StringInput(string Value) : InputSource;

/// <summary>Hash bytes read from standard input.</summary>
public sealed record StdinInput : InputSource;

/// <summary>Hash the contents of a single file.</summary>
public sealed record SingleFileInput(string Path) : InputSource;

/// <summary>Hash each file in turn; emit one sha256sum-compatible line per file.</summary>
public sealed record MultiFileInput(IReadOnlyList<string> Paths) : InputSource;
```

- [ ] **Step 6: Create `FakeTextReader.cs`**

```csharp
// tests/Winix.Digest.Tests/Fakes/FakeTextReader.cs
using System.IO;
using System.Text;

namespace Winix.Digest.Tests.Fakes;

/// <summary>
/// A <see cref="TextReader"/> backed by in-memory bytes, for injecting
/// test stdin content without touching real stdin.
/// </summary>
public sealed class FakeTextReader : StringReader
{
    public FakeTextReader(string content) : base(content) { }

    /// <summary>Convenience constructor for raw bytes interpreted as UTF-8.</summary>
    public static FakeTextReader FromBytes(byte[] bytes) =>
        new(Encoding.UTF8.GetString(bytes));
}
```

- [ ] **Step 7: Verify build**

Run: `dotnet build Winix.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 8: Commit**

```
git add src/Winix.Digest/HashAlgorithm.cs src/Winix.Digest/OutputFormat.cs src/Winix.Digest/DigestOptions.cs src/Winix.Digest/IHasher.cs src/Winix.Digest/InputSource.cs tests/Winix.Digest.Tests/Fakes/FakeTextReader.cs
git commit -m "feat(digest): add core types and test fakes"
```

---

### Task 4: HashFactory with RFC test vectors

Dispatch `HashAlgorithm` → `IHasher` using BCL primitives for SHA-2/SHA-3/SHA-1/MD5 and `Blake2Fast` for BLAKE2b. Test with known vectors for each algorithm.

**Files:**
- Create: `src/Winix.Digest/HashFactory.cs`
- Create: `tests/Winix.Digest.Tests/HashFactoryTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Winix.Digest.Tests/HashFactoryTests.cs
using System;
using System.IO;
using System.Text;
using Xunit;
using Winix.Codec;
using Winix.Digest;

namespace Winix.Digest.Tests;

public class HashFactoryTests
{
    // SHA-256 RFC test vectors (NIST FIPS 180-4)
    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("The quick brown fox jumps over the lazy dog",
                "d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592")]
    public void Sha256_KnownVectors(string input, string expectedHex)
    {
        var hasher = HashFactory.Create(HashAlgorithm.Sha256);
        byte[] hash = hasher.Hash(Encoding.UTF8.GetBytes(input));
        Assert.Equal(expectedHex, Hex.Encode(hash));
    }

    [Theory]
    [InlineData("", "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e")]
    [InlineData("abc", "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f")]
    public void Sha512_KnownVectors(string input, string expectedHex)
    {
        var hasher = HashFactory.Create(HashAlgorithm.Sha512);
        byte[] hash = hasher.Hash(Encoding.UTF8.GetBytes(input));
        Assert.Equal(expectedHex, Hex.Encode(hash));
    }

    [Fact]
    public void Sha1_KnownVector()
    {
        var hasher = HashFactory.Create(HashAlgorithm.Sha1);
        byte[] hash = hasher.Hash(Encoding.UTF8.GetBytes("abc"));
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", Hex.Encode(hash));
    }

    [Fact]
    public void Md5_KnownVector()
    {
        var hasher = HashFactory.Create(HashAlgorithm.Md5);
        byte[] hash = hasher.Hash(Encoding.UTF8.GetBytes("abc"));
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", Hex.Encode(hash));
    }

    [Fact]
    public void Sha3_256_KnownVector()
    {
        if (!System.Security.Cryptography.SHA3_256.IsSupported)
        {
            return; // Skip on platforms without SHA-3 support.
        }
        var hasher = HashFactory.Create(HashAlgorithm.Sha3_256);
        byte[] hash = hasher.Hash(Encoding.UTF8.GetBytes("abc"));
        Assert.Equal("3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532", Hex.Encode(hash));
    }

    [Fact]
    public void Blake2b_KnownVector()
    {
        // RFC 7693 test vector for BLAKE2b of "abc" (512-bit output).
        var hasher = HashFactory.Create(HashAlgorithm.Blake2b);
        byte[] hash = hasher.Hash(Encoding.UTF8.GetBytes("abc"));
        Assert.Equal(
            "ba80a53f981c4d0d6a2797b69f12f6e94c212f14685ac4b74b12bb6fdbffa2d17d87c5392aab792dc252d5de4533cc9518d38aa8dbf1925ab92386edd4009923",
            Hex.Encode(hash));
    }

    [Fact]
    public void Hash_StreamMatches_Hash_Bytes()
    {
        byte[] input = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");
        var hasher = HashFactory.Create(HashAlgorithm.Sha256);

        byte[] bytesHash = hasher.Hash(input);
        byte[] streamHash;
        using (var stream = new MemoryStream(input))
        {
            streamHash = hasher.Hash(stream);
        }

        Assert.Equal(bytesHash, streamHash);
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --filter FullyQualifiedName~HashFactoryTests`
Expected: `HashFactory` doesn't exist.

- [ ] **Step 3: Implement `HashFactory.cs`**

```csharp
// src/Winix.Digest/HashFactory.cs
using System;
using System.IO;
using System.Security.Cryptography;
using SauceControl.Blake2Fast;
using CryptoAlgo = System.Security.Cryptography;

namespace Winix.Digest;

/// <summary>Creates <see cref="IHasher"/> instances for the supported hash algorithms.</summary>
public static class HashFactory
{
    /// <summary>Creates a hasher for the given algorithm.</summary>
    /// <exception cref="PlatformNotSupportedException">SHA-3 not available on this platform.</exception>
    public static IHasher Create(HashAlgorithm algorithm) => algorithm switch
    {
        HashAlgorithm.Sha256   => new BclHasher(CryptoAlgo.SHA256.HashData,   static s => CryptoAlgo.SHA256.HashData(s)),
        HashAlgorithm.Sha384   => new BclHasher(CryptoAlgo.SHA384.HashData,   static s => CryptoAlgo.SHA384.HashData(s)),
        HashAlgorithm.Sha512   => new BclHasher(CryptoAlgo.SHA512.HashData,   static s => CryptoAlgo.SHA512.HashData(s)),
        HashAlgorithm.Sha1     => new BclHasher(CryptoAlgo.SHA1.HashData,     static s => CryptoAlgo.SHA1.HashData(s)),
        HashAlgorithm.Md5      => new BclHasher(CryptoAlgo.MD5.HashData,      static s => CryptoAlgo.MD5.HashData(s)),
        HashAlgorithm.Sha3_256 => CryptoAlgo.SHA3_256.IsSupported
            ? new BclHasher(CryptoAlgo.SHA3_256.HashData, static s => CryptoAlgo.SHA3_256.HashData(s))
            : throw new PlatformNotSupportedException("SHA-3 is not available on this platform"),
        HashAlgorithm.Sha3_512 => CryptoAlgo.SHA3_512.IsSupported
            ? new BclHasher(CryptoAlgo.SHA3_512.HashData, static s => CryptoAlgo.SHA3_512.HashData(s))
            : throw new PlatformNotSupportedException("SHA-3 is not available on this platform"),
        HashAlgorithm.Blake2b  => new Blake2bHasher(),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
    };

    private sealed class BclHasher : IHasher
    {
        private readonly Func<byte[], byte[]> _bytesFn;
        private readonly Func<Stream, byte[]> _streamFn;
        public BclHasher(Func<byte[], byte[]> bytesFn, Func<Stream, byte[]> streamFn)
        {
            _bytesFn = bytesFn;
            _streamFn = streamFn;
        }
        public byte[] Hash(ReadOnlySpan<byte> input) => _bytesFn(input.ToArray());
        public byte[] Hash(Stream input) => _streamFn(input);
    }

    private sealed class Blake2bHasher : IHasher
    {
        public byte[] Hash(ReadOnlySpan<byte> input) => Blake2b.ComputeHash(input.ToArray());
        public byte[] Hash(Stream input)
        {
            var hasher = Blake2b.CreateIncrementalHasher();
            Span<byte> buffer = stackalloc byte[8192];
            int n;
            while ((n = input.Read(buffer)) > 0)
            {
                hasher.Update(buffer[..n]);
            }
            return hasher.Finish();
        }
    }
}
```

**Verification note:** the exact `Blake2b.ComputeHash` / `Blake2b.CreateIncrementalHasher` / `IBlake2Incremental.Update`/`Finish` method names above are my best guess at the `Blake2Fast` API. **Before running tests, verify against the Blake2Fast NuGet docs on nuget.org** (search "Blake2Fast" → "readme") and adjust. If the API is different, update the `Blake2bHasher` implementation — the rest of the class is independent.

The `BclHasher` uses `Func<byte[], byte[]>` for `Hash(ReadOnlySpan<byte>)` because BCL's `HashData(byte[])` takes an array; for zero-copy, pass `stackalloc`-backed spans, but the `.ToArray()` is acceptable for CLI-sized inputs.

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --filter FullyQualifiedName~HashFactoryTests`
Expected: 10+ tests pass (depending on how Theory rows count).

- [ ] **Step 5: Commit**

```
git add src/Winix.Digest/HashFactory.cs tests/Winix.Digest.Tests/HashFactoryTests.cs
git commit -m "feat(digest): add HashFactory with RFC test vectors for all 8 algorithms"
```

---

### Task 5: HmacFactory with RFC 4231 test vectors

Key + algorithm → HMAC-capable `IHasher`. BCL `HMACSHA256.HashData(key, data)` etc. handle the construction.

**Files:**
- Create: `src/Winix.Digest/HmacFactory.cs`
- Create: `tests/Winix.Digest.Tests/HmacFactoryTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Winix.Digest.Tests/HmacFactoryTests.cs
using System;
using System.IO;
using System.Text;
using Xunit;
using Winix.Codec;
using Winix.Digest;

namespace Winix.Digest.Tests;

public class HmacFactoryTests
{
    // RFC 4231 test case 1: key = 0x0b × 20, data = "Hi There"
    [Fact]
    public void HmacSha256_Rfc4231_TestCase1()
    {
        byte[] key = new byte[20];
        Array.Fill(key, (byte)0x0b);
        byte[] data = Encoding.UTF8.GetBytes("Hi There");

        var hasher = HmacFactory.Create(HashAlgorithm.Sha256, key);
        byte[] hash = hasher.Hash(data);

        Assert.Equal(
            "b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7",
            Hex.Encode(hash));
    }

    // RFC 4231 test case 1 for SHA-512.
    [Fact]
    public void HmacSha512_Rfc4231_TestCase1()
    {
        byte[] key = new byte[20];
        Array.Fill(key, (byte)0x0b);
        byte[] data = Encoding.UTF8.GetBytes("Hi There");

        var hasher = HmacFactory.Create(HashAlgorithm.Sha512, key);
        byte[] hash = hasher.Hash(data);

        Assert.Equal(
            "87aa7cdea5ef619d4ff0b4241a1d6cb02379f4e2ce4ec2787ad0b30545e17cdedaa833b7d6b8a702038b274eaea3f4e4be9d914eeb61f1702e696c203a126854",
            Hex.Encode(hash));
    }

    // RFC 4231 test case 2: key = "Jefe", data = "what do ya want for nothing?"
    [Fact]
    public void HmacSha256_Rfc4231_TestCase2_ShortKey()
    {
        byte[] key = Encoding.UTF8.GetBytes("Jefe");
        byte[] data = Encoding.UTF8.GetBytes("what do ya want for nothing?");

        var hasher = HmacFactory.Create(HashAlgorithm.Sha256, key);
        byte[] hash = hasher.Hash(data);

        Assert.Equal(
            "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843",
            Hex.Encode(hash));
    }

    // RFC 2202 test case 1 for HMAC-SHA-1.
    [Fact]
    public void HmacSha1_Rfc2202_TestCase1()
    {
        byte[] key = new byte[20];
        Array.Fill(key, (byte)0x0b);
        byte[] data = Encoding.UTF8.GetBytes("Hi There");

        var hasher = HmacFactory.Create(HashAlgorithm.Sha1, key);
        byte[] hash = hasher.Hash(data);

        Assert.Equal("b617318655057264e28bc0b6fb378c8ef146be00", Hex.Encode(hash));
    }

    // RFC 2104 test case for HMAC-MD5.
    [Fact]
    public void HmacMd5_Rfc2104_TestCase()
    {
        byte[] key = Encoding.ASCII.GetBytes("Jefe");
        byte[] data = Encoding.ASCII.GetBytes("what do ya want for nothing?");

        var hasher = HmacFactory.Create(HashAlgorithm.Md5, key);
        byte[] hash = hasher.Hash(data);

        Assert.Equal("750c783e6ab0b503eaa86e310a5db738", Hex.Encode(hash));
    }

    [Fact]
    public void LongKey_HashedFirstPerSpec()
    {
        // RFC 4231 test case 4: key longer than block size (SHA-256 block = 64 bytes).
        byte[] key = new byte[131];
        Array.Fill(key, (byte)0xaa);
        byte[] data = Encoding.UTF8.GetBytes("Test Using Larger Than Block-Size Key - Hash Key First");

        var hasher = HmacFactory.Create(HashAlgorithm.Sha256, key);
        byte[] hash = hasher.Hash(data);

        Assert.Equal(
            "60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54",
            Hex.Encode(hash));
    }

    [Fact]
    public void HmacSha256_StreamMatches_Bytes()
    {
        byte[] key = Encoding.UTF8.GetBytes("my-secret-key");
        byte[] data = Encoding.UTF8.GetBytes("payload bytes here");

        var hasher = HmacFactory.Create(HashAlgorithm.Sha256, key);
        byte[] bytesHash = hasher.Hash(data);
        byte[] streamHash;
        using (var stream = new MemoryStream(data))
        {
            streamHash = hasher.Hash(stream);
        }

        Assert.Equal(bytesHash, streamHash);
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --filter FullyQualifiedName~HmacFactoryTests`
Expected: compile error.

- [ ] **Step 3: Implement `HmacFactory.cs`**

```csharp
// src/Winix.Digest/HmacFactory.cs
using System;
using System.IO;
using System.Security.Cryptography;
using SauceControl.Blake2Fast;

namespace Winix.Digest;

/// <summary>
/// Creates HMAC-capable <see cref="IHasher"/> instances. The key is copied
/// on construction; callers can discard their reference afterwards.
/// </summary>
public static class HmacFactory
{
    /// <summary>Creates an HMAC hasher using the given hash algorithm and key.</summary>
    /// <exception cref="PlatformNotSupportedException">SHA-3 not available on this platform.</exception>
    public static IHasher Create(HashAlgorithm algorithm, byte[] key) => algorithm switch
    {
        HashAlgorithm.Sha256   => new BclHmac(key, HMACSHA256.HashData, (k, s) => HMACSHA256.HashData(k, s)),
        HashAlgorithm.Sha384   => new BclHmac(key, HMACSHA384.HashData, (k, s) => HMACSHA384.HashData(k, s)),
        HashAlgorithm.Sha512   => new BclHmac(key, HMACSHA512.HashData, (k, s) => HMACSHA512.HashData(k, s)),
        HashAlgorithm.Sha1     => new BclHmac(key, HMACSHA1.HashData,   (k, s) => HMACSHA1.HashData(k, s)),
        HashAlgorithm.Md5      => new BclHmac(key, HMACMD5.HashData,    (k, s) => HMACMD5.HashData(k, s)),
        HashAlgorithm.Sha3_256 => HMACSHA3_256.IsSupported
            ? new BclHmac(key, HMACSHA3_256.HashData, (k, s) => HMACSHA3_256.HashData(k, s))
            : throw new PlatformNotSupportedException("HMAC-SHA-3 is not available on this platform"),
        HashAlgorithm.Sha3_512 => HMACSHA3_512.IsSupported
            ? new BclHmac(key, HMACSHA3_512.HashData, (k, s) => HMACSHA3_512.HashData(k, s))
            : throw new PlatformNotSupportedException("HMAC-SHA-3 is not available on this platform"),
        HashAlgorithm.Blake2b  => new Blake2bKeyedHasher(key),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
    };

    private sealed class BclHmac : IHasher
    {
        private readonly byte[] _key;
        private readonly Func<byte[], byte[], byte[]> _bytesFn;
        private readonly Func<byte[], Stream, byte[]> _streamFn;
        public BclHmac(byte[] key, Func<byte[], byte[], byte[]> bytesFn, Func<byte[], Stream, byte[]> streamFn)
        {
            _key = (byte[])key.Clone();
            _bytesFn = bytesFn;
            _streamFn = streamFn;
        }
        public byte[] Hash(ReadOnlySpan<byte> input) => _bytesFn(_key, input.ToArray());
        public byte[] Hash(Stream input) => _streamFn(_key, input);
    }

    private sealed class Blake2bKeyedHasher : IHasher
    {
        private readonly byte[] _key;
        public Blake2bKeyedHasher(byte[] key) => _key = (byte[])key.Clone();

        public byte[] Hash(ReadOnlySpan<byte> input) => Blake2b.ComputeHash(64, _key, input.ToArray());
        public byte[] Hash(Stream input)
        {
            var hasher = Blake2b.CreateIncrementalHasher(64, _key);
            Span<byte> buffer = stackalloc byte[8192];
            int n;
            while ((n = input.Read(buffer)) > 0)
            {
                hasher.Update(buffer[..n]);
            }
            return hasher.Finish();
        }
    }
}
```

**Verification note:** as with Task 4, the `Blake2b.ComputeHash(outputLength, key, data)` and `Blake2b.CreateIncrementalHasher(outputLength, key)` overloads are my best guess at the `Blake2Fast` API. Verify and adjust if the method signatures differ.

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --filter FullyQualifiedName~HmacFactoryTests`
Expected: 7+ tests pass.

- [ ] **Step 5: Commit**

```
git add src/Winix.Digest/HmacFactory.cs tests/Winix.Digest.Tests/HmacFactoryTests.cs
git commit -m "feat(digest): add HmacFactory with RFC 4231 and RFC 2202 test vectors"
```

---

### Task 6: KeyResolver + KeyFilePermissionsCheck

Resolves HMAC key from one of four sources (`--key-env`, `--key-file`, `--key-stdin`, `--key` literal) with precedence, conflict detection, and security warnings.

**Files:**
- Create: `src/Winix.Digest/KeyResolver.cs`
- Create: `src/Winix.Digest/KeyFilePermissionsCheck.cs`
- Create: `tests/Winix.Digest.Tests/KeyResolverTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Winix.Digest.Tests/KeyResolverTests.cs
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;
using Winix.Digest;
using Winix.Digest.Tests.Fakes;

namespace Winix.Digest.Tests;

public class KeyResolverTests
{
    [Fact]
    public void ResolveFromEnv_ReadsVariable()
    {
        Environment.SetEnvironmentVariable("DIGEST_TEST_KEY_1", "my-secret");
        try
        {
            var stderr = new StringWriter();
            byte[]? key = KeyResolver.Resolve(
                source: KeySource.EnvVariable("DIGEST_TEST_KEY_1"),
                stdin: new FakeTextReader(""),
                stripTrailingNewline: true,
                stderr: stderr,
                out string? error);
            Assert.Null(error);
            Assert.Equal(Encoding.UTF8.GetBytes("my-secret"), key);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DIGEST_TEST_KEY_1", null);
        }
    }

    [Fact]
    public void ResolveFromEnv_MissingVariable_Errors()
    {
        var stderr = new StringWriter();
        byte[]? key = KeyResolver.Resolve(
            source: KeySource.EnvVariable("DIGEST_TEST_KEY_DOES_NOT_EXIST_12345"),
            stdin: new FakeTextReader(""),
            stripTrailingNewline: true,
            stderr: stderr,
            out string? error);
        Assert.NotNull(error);
        Assert.Contains("not set", error);
        Assert.Null(key);
    }

    [Fact]
    public void ResolveFromFile_StripsTrailingNewline()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "my-secret\n");
            var stderr = new StringWriter();
            byte[]? key = KeyResolver.Resolve(
                source: KeySource.File(path),
                stdin: new FakeTextReader(""),
                stripTrailingNewline: true,
                stderr: stderr,
                out string? error);
            Assert.Null(error);
            Assert.Equal(Encoding.UTF8.GetBytes("my-secret"), key);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ResolveFromFile_KeyRaw_PreservesBytes()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "my-secret\n");
            var stderr = new StringWriter();
            byte[]? key = KeyResolver.Resolve(
                source: KeySource.File(path),
                stdin: new FakeTextReader(""),
                stripTrailingNewline: false,
                stderr: stderr,
                out string? error);
            Assert.Null(error);
            Assert.Equal(Encoding.UTF8.GetBytes("my-secret\n"), key);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ResolveFromStdin_StripsTrailingNewline()
    {
        var stderr = new StringWriter();
        byte[]? key = KeyResolver.Resolve(
            source: KeySource.Stdin(),
            stdin: new FakeTextReader("stdin-secret\n"),
            stripTrailingNewline: true,
            stderr: stderr,
            out string? error);
        Assert.Null(error);
        Assert.Equal(Encoding.UTF8.GetBytes("stdin-secret"), key);
    }

    [Fact]
    public void ResolveFromLiteral_EmitsWarning()
    {
        var stderr = new StringWriter();
        byte[]? key = KeyResolver.Resolve(
            source: KeySource.Literal("literal-secret"),
            stdin: new FakeTextReader(""),
            stripTrailingNewline: true,
            stderr: stderr,
            out string? error);
        Assert.Null(error);
        Assert.Equal(Encoding.UTF8.GetBytes("literal-secret"), key);
        string stderrText = stderr.ToString();
        Assert.Contains("--key exposes the key", stderrText);
        Assert.Contains("ps", stderrText);
    }

    [Fact]
    public void ResolveFromFile_MissingFile_Errors()
    {
        var stderr = new StringWriter();
        byte[]? key = KeyResolver.Resolve(
            source: KeySource.File("/nonexistent/path/to/secret-file-12345"),
            stdin: new FakeTextReader(""),
            stripTrailingNewline: true,
            stderr: stderr,
            out string? error);
        Assert.NotNull(error);
        Assert.Contains("not found", error);
        Assert.Null(key);
    }

    [Fact]
    public void ResolveFromFile_GroupReadable_Unix_EmitsWarning()
    {
        // Skip on Windows — UnixFileMode not meaningful there.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "my-secret");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            var stderr = new StringWriter();
            byte[]? key = KeyResolver.Resolve(
                source: KeySource.File(path),
                stdin: new FakeTextReader(""),
                stripTrailingNewline: true,
                stderr: stderr,
                out string? error);
            Assert.Null(error);
            Assert.Contains("readable by group/other", stderr.ToString());
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run tests — expect compile errors**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --filter FullyQualifiedName~KeyResolverTests`
Expected: compile error (`KeyResolver`, `KeySource` don't exist).

- [ ] **Step 3: Implement `KeyFilePermissionsCheck.cs`**

```csharp
// src/Winix.Digest/KeyFilePermissionsCheck.cs
using System.IO;
using System.Runtime.InteropServices;

namespace Winix.Digest;

/// <summary>
/// Unix-only helper that returns a warning message when an HMAC key file is
/// readable by group or other (modes 0x40 = group read, 0x04 = other read).
/// No-op on Windows — ACLs are harder to check succinctly and DPAPI is the
/// better long-term answer there (see future <c>protect</c>/<c>unprotect</c> tool).
/// </summary>
public static class KeyFilePermissionsCheck
{
    /// <summary>Returns a warning message if the file is group/other readable; otherwise null.</summary>
    public static string? GetWarningOrNull(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        try
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0)
            {
                return null;
            }

            // Format the mode as an octal string like "0644".
            int bits = (int)mode & 0x1FF;
            string octal = System.Convert.ToString(bits, 8).PadLeft(3, '0');

            return $"digest: warning: {path} has mode 0{octal} and is readable by group/other.{System.Environment.NewLine}" +
                   $"        Consider 'chmod 0600 {path}'.";
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Implement `KeyResolver.cs`**

```csharp
// src/Winix.Digest/KeyResolver.cs
using System;
using System.IO;
using System.Text;

namespace Winix.Digest;

/// <summary>Discriminates where an HMAC key comes from.</summary>
public abstract record KeySource
{
    public static KeySource EnvVariable(string name) => new EnvSource(name);
    public static KeySource File(string path) => new FileSource(path);
    public static KeySource Stdin() => new StdinSource();
    public static KeySource Literal(string value) => new LiteralSource(value);

    internal sealed record EnvSource(string Name) : KeySource;
    internal sealed record FileSource(string Path) : KeySource;
    internal sealed record StdinSource : KeySource;
    internal sealed record LiteralSource(string Value) : KeySource;
}

/// <summary>
/// Resolves an HMAC key byte sequence from one of four sources (env, file, stdin, literal),
/// emitting security warnings to stderr where appropriate.
/// </summary>
public static class KeyResolver
{
    private const string LiteralWarning =
        "digest: warning: --key exposes the key via 'ps', shell history, and process listings.\n" +
        "        Prefer --key-env, --key-file, or --key-stdin for non-ephemeral scripts.";

    /// <summary>
    /// Resolves the key bytes. Returns null on error; the <paramref name="error"/> out-param
    /// contains the user-facing message (for the console app to format + exit with code 125).
    /// </summary>
    public static byte[]? Resolve(
        KeySource source,
        TextReader stdin,
        bool stripTrailingNewline,
        TextWriter stderr,
        out string? error)
    {
        error = null;

        switch (source)
        {
            case KeySource.EnvSource env:
                string? value = Environment.GetEnvironmentVariable(env.Name);
                if (value is null)
                {
                    error = $"environment variable '{env.Name}' is not set";
                    return null;
                }
                return Encoding.UTF8.GetBytes(value);

            case KeySource.FileSource file:
                if (!File.Exists(file.Path))
                {
                    error = $"key file '{file.Path}' not found";
                    return null;
                }
                string? permWarning = KeyFilePermissionsCheck.GetWarningOrNull(file.Path);
                if (permWarning is not null)
                {
                    stderr.WriteLine(permWarning);
                }
                byte[] fileBytes = File.ReadAllBytes(file.Path);
                return stripTrailingNewline ? StripOneTrailingNewline(fileBytes) : fileBytes;

            case KeySource.StdinSource:
                string stdinText = stdin.ReadToEnd();
                byte[] stdinBytes = Encoding.UTF8.GetBytes(stdinText);
                return stripTrailingNewline ? StripOneTrailingNewline(stdinBytes) : stdinBytes;

            case KeySource.LiteralSource literal:
                stderr.WriteLine(LiteralWarning);
                return Encoding.UTF8.GetBytes(literal.Value);

            default:
                throw new ArgumentOutOfRangeException(nameof(source), source, null);
        }
    }

    private static byte[] StripOneTrailingNewline(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[^2] == (byte)'\r' && bytes[^1] == (byte)'\n')
        {
            return bytes[..^2];
        }
        if (bytes.Length >= 1 && bytes[^1] == (byte)'\n')
        {
            return bytes[..^1];
        }
        return bytes;
    }
}
```

- [ ] **Step 5: Run tests — expect pass**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --filter FullyQualifiedName~KeyResolverTests`
Expected: all pass (8 tests; one skipped on Windows).

- [ ] **Step 6: Commit**

```
git add src/Winix.Digest/KeyResolver.cs src/Winix.Digest/KeyFilePermissionsCheck.cs tests/Winix.Digest.Tests/KeyResolverTests.cs
git commit -m "feat(digest): add KeyResolver for four HMAC key sources with Unix permission check"
```

---

### Task 7: HashRunner

Orchestrates `InputSource` → `IHasher` → hash bytes. Four input modes (string, stdin, single file, multi-file), all-or-nothing validation on multi-file.

**Files:**
- Create: `src/Winix.Digest/HashRunner.cs`
- Create: `tests/Winix.Digest.Tests/HashRunnerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Winix.Digest.Tests/HashRunnerTests.cs
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;
using Winix.Codec;
using Winix.Digest;
using Winix.Digest.Tests.Fakes;

namespace Winix.Digest.Tests;

public class HashRunnerTests
{
    [Fact]
    public void RunString_ProducesExpectedHash()
    {
        var hasher = HashFactory.Create(HashAlgorithm.Sha256);
        var results = HashRunner.Run(
            source: new StringInput("abc"),
            hasher: hasher,
            stdin: new FakeTextReader(""),
            out string? error);
        Assert.Null(error);
        Assert.Single(results);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            Hex.Encode(results[0].Hash));
        Assert.Null(results[0].Path);
    }

    [Fact]
    public void RunStdin_ProducesExpectedHash()
    {
        var hasher = HashFactory.Create(HashAlgorithm.Sha256);
        var results = HashRunner.Run(
            source: new StdinInput(),
            hasher: hasher,
            stdin: new FakeTextReader("abc"),
            out string? error);
        Assert.Null(error);
        Assert.Single(results);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            Hex.Encode(results[0].Hash));
    }

    [Fact]
    public void RunSingleFile_ProducesExpectedHash()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("abc"));
            var hasher = HashFactory.Create(HashAlgorithm.Sha256);
            var results = HashRunner.Run(
                source: new SingleFileInput(path),
                hasher: hasher,
                stdin: new FakeTextReader(""),
                out string? error);
            Assert.Null(error);
            Assert.Single(results);
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                Hex.Encode(results[0].Hash));
            Assert.Equal(path, results[0].Path);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RunMultiFile_ProducesOneResultPerFile_InOrder()
    {
        string p1 = Path.GetTempFileName();
        string p2 = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(p1, Encoding.UTF8.GetBytes("abc"));
            File.WriteAllBytes(p2, Encoding.UTF8.GetBytes("xyz"));
            var hasher = HashFactory.Create(HashAlgorithm.Sha256);
            var results = HashRunner.Run(
                source: new MultiFileInput(new[] { p1, p2 }),
                hasher: hasher,
                stdin: new FakeTextReader(""),
                out string? error);
            Assert.Null(error);
            Assert.Equal(2, results.Count);
            Assert.Equal(p1, results[0].Path);
            Assert.Equal(p2, results[1].Path);
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                Hex.Encode(results[0].Hash));
        }
        finally { File.Delete(p1); File.Delete(p2); }
    }

    [Fact]
    public void RunMultiFile_MissingFile_ErrorsBeforeAnyOutput()
    {
        string p1 = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(p1, Encoding.UTF8.GetBytes("abc"));
            string pMissing = "/this/file/definitely/does/not/exist-12345";
            var hasher = HashFactory.Create(HashAlgorithm.Sha256);
            var results = HashRunner.Run(
                source: new MultiFileInput(new[] { p1, pMissing }),
                hasher: hasher,
                stdin: new FakeTextReader(""),
                out string? error);
            Assert.NotNull(error);
            Assert.Contains("not found", error);
            Assert.Empty(results);
        }
        finally { File.Delete(p1); }
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --filter FullyQualifiedName~HashRunnerTests`
Expected: compile error.

- [ ] **Step 3: Implement `HashRunner.cs`**

```csharp
// src/Winix.Digest/HashRunner.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Winix.Digest;

/// <summary>One hash result — raw bytes + optional filename (for multi-file mode).</summary>
public readonly record struct HashResult(byte[] Hash, string? Path);

/// <summary>
/// Orchestrates hash computation from an <see cref="InputSource"/>. Returns a list
/// of <see cref="HashResult"/> (exactly one for string/stdin/single-file; one per
/// file for multi-file). On error, returns an empty list and sets <paramref name="error"/>.
/// Multi-file mode validates all paths up front (all-or-nothing).
/// </summary>
public static class HashRunner
{
    public static IReadOnlyList<HashResult> Run(
        InputSource source,
        IHasher hasher,
        TextReader stdin,
        out string? error)
    {
        error = null;
        return source switch
        {
            StringInput s => HashString(s.Value, hasher),
            StdinInput => HashStdin(stdin, hasher),
            SingleFileInput f => HashSingleFile(f.Path, hasher, out error),
            MultiFileInput m => HashMultiFile(m.Paths, hasher, out error),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };
    }

    private static IReadOnlyList<HashResult> HashString(string value, IHasher hasher)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        return new[] { new HashResult(hasher.Hash(bytes), null) };
    }

    private static IReadOnlyList<HashResult> HashStdin(TextReader stdin, IHasher hasher)
    {
        string text = stdin.ReadToEnd();
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return new[] { new HashResult(hasher.Hash(bytes), null) };
    }

    private static IReadOnlyList<HashResult> HashSingleFile(string path, IHasher hasher, out string? error)
    {
        error = null;
        if (!File.Exists(path))
        {
            error = $"'{path}' not found";
            return Array.Empty<HashResult>();
        }
        using var stream = File.OpenRead(path);
        return new[] { new HashResult(hasher.Hash(stream), path) };
    }

    private static IReadOnlyList<HashResult> HashMultiFile(IReadOnlyList<string> paths, IHasher hasher, out string? error)
    {
        error = null;
        // All-or-nothing validation up front.
        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                error = $"'{path}' not found";
                return Array.Empty<HashResult>();
            }
        }
        var results = new List<HashResult>(paths.Count);
        foreach (string path in paths)
        {
            using var stream = File.OpenRead(path);
            results.Add(new HashResult(hasher.Hash(stream), path));
        }
        return results;
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --filter FullyQualifiedName~HashRunnerTests`
Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```
git add src/Winix.Digest/HashRunner.cs tests/Winix.Digest.Tests/HashRunnerTests.cs
git commit -m "feat(digest): add HashRunner with all-or-nothing multi-file validation"
```

---

### Task 8: Verifier

Compares a computed hash against an expected value in constant time. Returns a verdict (match/mismatch) and a human-readable error string.

**Files:**
- Create: `src/Winix.Digest/Verifier.cs`
- Create: `tests/Winix.Digest.Tests/VerifierTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Winix.Digest.Tests/VerifierTests.cs
using Xunit;
using Winix.Digest;

namespace Winix.Digest.Tests;

public class VerifierTests
{
    [Fact]
    public void Verify_HexMatch_ReturnsTrue()
    {
        string expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        byte[] computed = Winix.Codec.Hex.Decode(expected);
        Assert.True(Verifier.Verify(computed, expected, OutputFormat.Hex));
    }

    [Fact]
    public void Verify_HexMismatch_ReturnsFalse()
    {
        string expected = "0000000000000000000000000000000000000000000000000000000000000000";
        byte[] computed = Winix.Codec.Hex.Decode("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        Assert.False(Verifier.Verify(computed, expected, OutputFormat.Hex));
    }

    [Fact]
    public void Verify_HexCaseInsensitive()
    {
        byte[] computed = new byte[] { 0xab, 0xcd, 0xef };
        Assert.True(Verifier.Verify(computed, "abcdef", OutputFormat.Hex));
        Assert.True(Verifier.Verify(computed, "ABCDEF", OutputFormat.Hex));
        Assert.True(Verifier.Verify(computed, "AbCdEf", OutputFormat.Hex));
    }

    [Fact]
    public void Verify_Base64_CaseSensitive()
    {
        byte[] computed = new byte[] { 0x66, 0x6f, 0x6f };
        Assert.True(Verifier.Verify(computed, "Zm9v", OutputFormat.Base64));
        Assert.False(Verifier.Verify(computed, "zm9v", OutputFormat.Base64));  // case differs, not equal
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --filter FullyQualifiedName~VerifierTests`
Expected: compile error.

- [ ] **Step 3: Implement `Verifier.cs`**

```csharp
// src/Winix.Digest/Verifier.cs
using Winix.Codec;

namespace Winix.Digest;

/// <summary>
/// Compares a computed hash against an expected string in constant time.
/// Hex comparison is case-insensitive; base64/base32 are case-sensitive.
/// </summary>
public static class Verifier
{
    /// <summary>Returns true if the computed bytes match the expected encoded value.</summary>
    public static bool Verify(byte[] computed, string expected, OutputFormat format)
    {
        if (expected is null) return false;
        string computedStr = format switch
        {
            OutputFormat.Hex => Hex.Encode(computed),
            OutputFormat.Base64 => Base64.Encode(computed, urlSafe: false),
            OutputFormat.Base64Url => Base64.Encode(computed, urlSafe: true),
            OutputFormat.Base32 => Base32Crockford.Encode(computed),
            _ => "",
        };
        bool caseInsensitive = format == OutputFormat.Hex;
        return ConstantTimeCompare.StringEquals(computedStr, expected, caseInsensitive);
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --filter FullyQualifiedName~VerifierTests`
Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```
git add src/Winix.Digest/Verifier.cs tests/Winix.Digest.Tests/VerifierTests.cs
git commit -m "feat(digest): add Verifier with constant-time comparison"
```

---

### Task 9: Formatting

Produces the final output string — single-input plain text, multi-file sha256sum-compatible (with `*` marker), and JSON (single object or array).

**Files:**
- Create: `src/Winix.Digest/Formatting.cs`
- Create: `tests/Winix.Digest.Tests/FormattingTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Winix.Digest.Tests/FormattingTests.cs
using System;
using System.Text.Json;
using Xunit;
using Winix.Digest;

namespace Winix.Digest.Tests;

public class FormattingTests
{
    // "abc" SHA-256 hash bytes.
    private static readonly byte[] AbcSha256 = Winix.Codec.Hex.Decode(
        "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");

    [Fact]
    public void Plain_Single_HexLowercase_Default()
    {
        string line = Formatting.PlainSingle(AbcSha256, OutputFormat.Hex, uppercase: false);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", line);
    }

    [Fact]
    public void Plain_Single_HexUppercase()
    {
        string line = Formatting.PlainSingle(AbcSha256, OutputFormat.Hex, uppercase: true);
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", line);
    }

    [Fact]
    public void Plain_Single_Base64()
    {
        string line = Formatting.PlainSingle(AbcSha256, OutputFormat.Base64, uppercase: false);
        Assert.Equal("uoFrv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=", line);
    }

    [Fact]
    public void Plain_MultiLine_UsesBinaryMarker()
    {
        string line = Formatting.PlainMultiLine(AbcSha256, "/path/file.bin", OutputFormat.Hex, uppercase: false);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad */path/file.bin", line);
    }

    [Fact]
    public void Json_Single_HasExpectedShape()
    {
        var opts = DigestOptions.Defaults with { Algorithm = HashAlgorithm.Sha256 };
        string json = Formatting.JsonElement(AbcSha256, path: null, opts);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("sha256", doc.RootElement.GetProperty("algorithm").GetString());
        Assert.Equal("hex", doc.RootElement.GetProperty("format").GetString());
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            doc.RootElement.GetProperty("hash").GetString());
        Assert.Equal("string", doc.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public void Json_MultiFile_IncludesPath()
    {
        var opts = DigestOptions.Defaults;
        string json = Formatting.JsonElement(AbcSha256, path: "/path/file.bin", opts);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("file", doc.RootElement.GetProperty("source").GetString());
        Assert.Equal("/path/file.bin", doc.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public void Json_Hmac_UsesHmacAlgorithmPrefix()
    {
        var opts = DigestOptions.Defaults with { Algorithm = HashAlgorithm.Sha256, IsHmac = true };
        string json = Formatting.JsonElement(AbcSha256, path: null, opts);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("hmac-sha256", doc.RootElement.GetProperty("algorithm").GetString());
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --filter FullyQualifiedName~FormattingTests`
Expected: compile error.

- [ ] **Step 3: Implement `Formatting.cs`**

```csharp
// src/Winix.Digest/Formatting.cs
using System;
using Winix.Codec;
using Yort.ShellKit;

namespace Winix.Digest;

/// <summary>Pure functions composing digest's output lines and JSON elements.</summary>
public static class Formatting
{
    /// <summary>Encodes hash bytes as a string according to the requested format.</summary>
    public static string Encode(byte[] hash, OutputFormat format, bool uppercase)
    {
        return format switch
        {
            OutputFormat.Hex => Hex.Encode(hash, uppercase),
            OutputFormat.Base64 => Base64.Encode(hash, urlSafe: false),
            OutputFormat.Base64Url => Base64.Encode(hash, urlSafe: true),
            OutputFormat.Base32 => Base32Crockford.Encode(hash),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
    }

    /// <summary>Single-input plain text: just the encoded hash (no trailing newline; caller adds it).</summary>
    public static string PlainSingle(byte[] hash, OutputFormat format, bool uppercase)
    {
        return Encode(hash, format, uppercase);
    }

    /// <summary>
    /// Multi-file plain text: sha256sum-compatible <c>&lt;hash&gt; *&lt;filename&gt;</c> with the
    /// binary-mode marker. <c>*</c> signals that the hash was computed over raw bytes
    /// (no CR/LF translation) — honest to digest's behaviour and compatible with
    /// <c>sha256sum -c</c> verification flows.
    /// </summary>
    public static string PlainMultiLine(byte[] hash, string filename, OutputFormat format, bool uppercase)
    {
        return $"{Encode(hash, format, uppercase)} *{filename}";
    }

    /// <summary>
    /// JSON element for one hash result. When multiple results are emitted (multi-file
    /// mode), caller wraps these in a JSON array.
    /// </summary>
    public static string JsonElement(byte[] hash, string? path, DigestOptions options)
    {
        var (w, buffer) = JsonHelper.CreateWriter();
        using (w)
        {
            w.WriteStartObject();
            w.WriteString("algorithm", FormatAlgorithmName(options));
            w.WriteString("format", FormatName(options.Format));
            w.WriteString("hash", Encode(hash, options.Format, options.Uppercase));
            w.WriteString("source", path is null ? InferSource(options) : "file");
            if (path is not null)
            {
                w.WriteString("path", path);
            }
            w.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }

    private static string FormatAlgorithmName(DigestOptions options)
    {
        string algo = options.Algorithm switch
        {
            HashAlgorithm.Sha256 => "sha256",
            HashAlgorithm.Sha384 => "sha384",
            HashAlgorithm.Sha512 => "sha512",
            HashAlgorithm.Sha1 => "sha1",
            HashAlgorithm.Md5 => "md5",
            HashAlgorithm.Sha3_256 => "sha3-256",
            HashAlgorithm.Sha3_512 => "sha3-512",
            HashAlgorithm.Blake2b => "blake2b",
            _ => "unknown",
        };
        return options.IsHmac ? $"hmac-{algo}" : algo;
    }

    private static string FormatName(OutputFormat format) => format switch
    {
        OutputFormat.Hex => "hex",
        OutputFormat.Base64 => "base64",
        OutputFormat.Base64Url => "base64url",
        OutputFormat.Base32 => "base32",
        _ => "unknown",
    };

    private static string InferSource(DigestOptions options) => options.Source switch
    {
        StringInput => "string",
        StdinInput => "stdin",
        SingleFileInput => "file",
        MultiFileInput => "file",
        _ => "unknown",
    };
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --filter FullyQualifiedName~FormattingTests`
Expected: 7 tests pass.

- [ ] **Step 5: Commit**

```
git add src/Winix.Digest/Formatting.cs tests/Winix.Digest.Tests/FormattingTests.cs
git commit -m "feat(digest): add Formatting for plain-text and JSON output"
```

---

### Task 10: ArgParser + compatibility matrix

Parse argv via ShellKit, validate the Q-matrix, build a `DigestOptions`.

**Files:**
- Create: `src/Winix.Digest/ArgParser.cs`
- Create: `tests/Winix.Digest.Tests/ArgParserTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Winix.Digest.Tests/ArgParserTests.cs
using System;
using Xunit;
using Winix.Digest;

namespace Winix.Digest.Tests;

public class ArgParserTests
{
    [Fact]
    public void Parse_NoArgs_DefaultsToSha256StdinHex()
    {
        var r = ArgParser.Parse(Array.Empty<string>());
        Assert.True(r.Success);
        Assert.Equal(HashAlgorithm.Sha256, r.Options!.Algorithm);
        Assert.False(r.Options.IsHmac);
        Assert.Equal(OutputFormat.Hex, r.Options.Format);
    }

    [Theory]
    [InlineData("--sha256", HashAlgorithm.Sha256)]
    [InlineData("--sha384", HashAlgorithm.Sha384)]
    [InlineData("--sha512", HashAlgorithm.Sha512)]
    [InlineData("--sha1", HashAlgorithm.Sha1)]
    [InlineData("--md5", HashAlgorithm.Md5)]
    [InlineData("--sha3-256", HashAlgorithm.Sha3_256)]
    [InlineData("--sha3-512", HashAlgorithm.Sha3_512)]
    [InlineData("--blake2b", HashAlgorithm.Blake2b)]
    public void Parse_IndividualAlgorithmFlags(string flag, HashAlgorithm expected)
    {
        var r = ArgParser.Parse(new[] { flag, "-s", "abc" });
        Assert.True(r.Success);
        Assert.Equal(expected, r.Options!.Algorithm);
    }

    [Theory]
    [InlineData("sha256", HashAlgorithm.Sha256)]
    [InlineData("sha3-256", HashAlgorithm.Sha3_256)]
    [InlineData("blake2b", HashAlgorithm.Blake2b)]
    public void Parse_AlgoFlag(string value, HashAlgorithm expected)
    {
        var r = ArgParser.Parse(new[] { "--algo", value, "-s", "abc" });
        Assert.True(r.Success);
        Assert.Equal(expected, r.Options!.Algorithm);
    }

    [Fact]
    public void Parse_MultipleAlgorithmFlags_Errors()
    {
        var r = ArgParser.Parse(new[] { "--sha256", "--sha512", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("multiple algorithms", r.Error);
    }

    [Fact]
    public void Parse_UnknownAlgo_Errors()
    {
        var r = ArgParser.Parse(new[] { "--algo", "weirdhash", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("unknown algorithm", r.Error);
    }

    [Fact]
    public void Parse_Hmac_RequiresKeySource()
    {
        var r = ArgParser.Parse(new[] { "--hmac", "sha256", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("--hmac requires", r.Error);
    }

    [Fact]
    public void Parse_Hmac_WithMultipleKeySources_Errors()
    {
        var r = ArgParser.Parse(new[] {
            "--hmac", "sha256",
            "--key-env", "MY_KEY",
            "--key-file", "/tmp/k",
            "-s", "abc"
        });
        Assert.False(r.Success);
        Assert.Contains("exactly one of", r.Error);
    }

    [Fact]
    public void Parse_Hmac_WithAlgorithmFlag_Errors()
    {
        var r = ArgParser.Parse(new[] { "--hmac", "sha256", "--sha512", "--key-env", "K", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("--hmac carries its own algorithm", r.Error);
    }

    [Fact]
    public void Parse_String_WithPositional_Errors()
    {
        var r = ArgParser.Parse(new[] { "-s", "hello", "file.txt" });
        Assert.False(r.Success);
        Assert.Contains("--string cannot be combined with file arguments", r.Error);
    }

    [Fact]
    public void Parse_MultipleStrings_Errors()
    {
        var r = ArgParser.Parse(new[] { "-s", "hello", "--string", "world" });
        Assert.False(r.Success);
        Assert.Contains("--string can only be specified once", r.Error);
    }

    [Fact]
    public void Parse_MultipleOutputFormats_Errors()
    {
        var r = ArgParser.Parse(new[] { "--hex", "--base64", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("multiple output formats", r.Error);
    }

    [Theory]
    [InlineData("--hex", OutputFormat.Hex)]
    [InlineData("--base64", OutputFormat.Base64)]
    [InlineData("--base64-url", OutputFormat.Base64Url)]
    [InlineData("--base32", OutputFormat.Base32)]
    public void Parse_OutputFormatFlags(string flag, OutputFormat expected)
    {
        var r = ArgParser.Parse(new[] { flag, "-s", "abc" });
        Assert.True(r.Success);
        Assert.Equal(expected, r.Options!.Format);
    }

    [Fact]
    public void Parse_StringFlag_ProducesStringInput()
    {
        var r = ArgParser.Parse(new[] { "-s", "hello world" });
        Assert.True(r.Success);
        Assert.IsType<StringInput>(r.Options!.Source);
        Assert.Equal("hello world", ((StringInput)r.Options.Source).Value);
    }

    [Fact]
    public void Parse_VerifyWithMultiFile_Errors()
    {
        // Need real files for multi-file mode; use self-file.
        string self = typeof(ArgParserTests).Assembly.Location;
        var r = ArgParser.Parse(new[] { "--verify", "abc", self, self });
        Assert.False(r.Success);
        Assert.Contains("--verify is not supported with multiple files", r.Error);
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --filter FullyQualifiedName~ArgParserTests`
Expected: compile error.

- [ ] **Step 3: Implement `ArgParser.cs`**

Before writing: open `src/Yort.ShellKit/CommandLineParser.cs` to confirm the fluent API (the `ids` tool's `ArgParser.cs` is a good reference for the integration pattern — see `src/Winix.Ids/ArgParser.cs`).

```csharp
// src/Winix.Digest/ArgParser.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.Digest;

/// <summary>
/// Parses <c>digest</c> command-line arguments into <see cref="DigestOptions"/>,
/// enforcing the Q-matrix of flag compatibility rules.
/// </summary>
public static class ArgParser
{
    public sealed record Result(
        DigestOptions? Options,
        string? Error,
        bool IsHandled,
        int HandledExitCode,
        bool UseColor,
        KeySource? KeySourceForHmac,
        bool StripKeyNewline)
    {
        public bool Success => Options is not null;
    }

    public static Result Parse(string[] argv)
    {
        var parser = BuildParser();
        var parsed = parser.Parse(argv);

        bool useColor = parsed.ResolveColor(checkStdErr: false);

        Result Fail(string error) => new(null, error, false, 0, useColor, null, false);

        if (parsed.IsHandled)
        {
            return new Result(null, null, true, parsed.ExitCode, useColor, null, false);
        }
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors[0]);
        }

        // --- Algorithm resolution ---
        bool[] algoFlags = new[]
        {
            parsed.Has("--sha256"), parsed.Has("--sha384"), parsed.Has("--sha512"),
            parsed.Has("--sha1"), parsed.Has("--md5"),
            parsed.Has("--sha3-256"), parsed.Has("--sha3-512"),
            parsed.Has("--blake2b"),
        };
        int algoCount = 0;
        foreach (bool f in algoFlags) if (f) algoCount++;
        bool algoFromFlag = algoCount == 1;
        bool algoExplicit = algoFromFlag || parsed.Has("--algo");

        if (algoCount > 1)
        {
            return Fail("multiple algorithms specified — choose one");
        }
        if (algoFromFlag && parsed.Has("--algo"))
        {
            return Fail("multiple algorithms specified — choose one");
        }

        HashAlgorithm algorithm = HashAlgorithm.Sha256;
        if (parsed.Has("--sha256")) algorithm = HashAlgorithm.Sha256;
        else if (parsed.Has("--sha384")) algorithm = HashAlgorithm.Sha384;
        else if (parsed.Has("--sha512")) algorithm = HashAlgorithm.Sha512;
        else if (parsed.Has("--sha1")) algorithm = HashAlgorithm.Sha1;
        else if (parsed.Has("--md5")) algorithm = HashAlgorithm.Md5;
        else if (parsed.Has("--sha3-256")) algorithm = HashAlgorithm.Sha3_256;
        else if (parsed.Has("--sha3-512")) algorithm = HashAlgorithm.Sha3_512;
        else if (parsed.Has("--blake2b")) algorithm = HashAlgorithm.Blake2b;
        else if (parsed.Has("--algo"))
        {
            string algoStr = parsed.GetString("--algo");
            if (!TryParseAlgo(algoStr, out algorithm))
            {
                return Fail($"unknown algorithm '{algoStr}' (expected: sha256, sha384, sha512, sha1, md5, sha3-256, sha3-512, blake2b)");
            }
        }

        // --- HMAC resolution ---
        bool isHmac = parsed.Has("--hmac");
        HashAlgorithm hmacAlgorithm = algorithm;
        if (isHmac)
        {
            if (algoExplicit)
            {
                return Fail("--hmac carries its own algorithm; do not combine with --sha256 / --algo / etc.");
            }
            string hmacStr = parsed.GetString("--hmac");
            if (!TryParseAlgo(hmacStr, out hmacAlgorithm))
            {
                return Fail($"unknown algorithm '{hmacStr}' (expected: sha256, sha384, sha512, sha1, md5, sha3-256, sha3-512, blake2b)");
            }
            algorithm = hmacAlgorithm;
        }

        // --- HMAC key source resolution ---
        KeySource? keySource = null;
        if (isHmac)
        {
            int keyCount = 0;
            if (parsed.Has("--key-env")) keyCount++;
            if (parsed.Has("--key-file")) keyCount++;
            if (parsed.Has("--key-stdin")) keyCount++;
            if (parsed.Has("--key")) keyCount++;

            if (keyCount == 0)
            {
                return Fail("--hmac requires one of --key-env, --key-file, --key-stdin, --key");
            }
            if (keyCount > 1)
            {
                return Fail("exactly one of --key-env, --key-file, --key-stdin, --key must be specified");
            }
            if (parsed.Has("--key-env")) keySource = KeySource.EnvVariable(parsed.GetString("--key-env"));
            else if (parsed.Has("--key-file")) keySource = KeySource.File(parsed.GetString("--key-file"));
            else if (parsed.Has("--key-stdin")) keySource = KeySource.Stdin();
            else if (parsed.Has("--key")) keySource = KeySource.Literal(parsed.GetString("--key"));
        }
        bool stripKeyNewline = !parsed.Has("--key-raw");

        // --- Output format resolution ---
        int formatCount = 0;
        if (parsed.Has("--hex")) formatCount++;
        if (parsed.Has("--base64")) formatCount++;
        if (parsed.Has("--base64-url")) formatCount++;
        if (parsed.Has("--base32")) formatCount++;
        if (formatCount > 1)
        {
            return Fail("multiple output formats specified — choose one");
        }
        OutputFormat format = OutputFormat.Hex;
        if (parsed.Has("--base64")) format = OutputFormat.Base64;
        else if (parsed.Has("--base64-url")) format = OutputFormat.Base64Url;
        else if (parsed.Has("--base32")) format = OutputFormat.Base32;

        bool uppercase = parsed.Has("--uppercase");

        // --- Input source resolution ---
        bool hasString = parsed.Has("--string");
        int stringCount = parsed.GetStringCount("--string");  // NOTE: may need adapting if ShellKit API differs
        string[] positionals = parsed.Positionals;

        InputSource source;
        if (stringCount > 1)
        {
            return Fail("--string can only be specified once");
        }
        if (hasString)
        {
            if (positionals.Length > 0)
            {
                return Fail("--string cannot be combined with file arguments");
            }
            source = new StringInput(parsed.GetString("--string"));
        }
        else if (positionals.Length == 0)
        {
            source = new StdinInput();
        }
        else if (positionals.Length == 1 && positionals[0] == "-")
        {
            source = new StdinInput();
        }
        else if (positionals.Length == 1)
        {
            if (!System.IO.File.Exists(positionals[0]))
            {
                return Fail($"'{positionals[0]}' not found — use --string to hash as a literal, or pass a valid file path");
            }
            source = new SingleFileInput(positionals[0]);
        }
        else
        {
            source = new MultiFileInput(positionals);
        }

        // --- Verify mode ---
        string? verify = parsed.Has("--verify") ? parsed.GetString("--verify") : null;
        if (verify is not null && source is MultiFileInput)
        {
            return Fail("--verify is not supported with multiple files");
        }

        bool json = parsed.Has("--json");

        var options = new DigestOptions(
            Algorithm: algorithm,
            IsHmac: isHmac,
            HmacKey: null,  // resolved later in Program.cs via KeyResolver
            Format: format,
            Uppercase: uppercase,
            Source: source,
            VerifyExpected: verify,
            Json: json);

        return new Result(options, null, false, 0, useColor, keySource, stripKeyNewline);
    }

    private static bool TryParseAlgo(string value, out HashAlgorithm algo)
    {
        switch (value)
        {
            case "sha256": algo = HashAlgorithm.Sha256; return true;
            case "sha384": algo = HashAlgorithm.Sha384; return true;
            case "sha512": algo = HashAlgorithm.Sha512; return true;
            case "sha1": algo = HashAlgorithm.Sha1; return true;
            case "md5": algo = HashAlgorithm.Md5; return true;
            case "sha3-256": algo = HashAlgorithm.Sha3_256; return true;
            case "sha3-512": algo = HashAlgorithm.Sha3_512; return true;
            case "blake2b": algo = HashAlgorithm.Blake2b; return true;
            default: algo = default; return false;
        }
    }

    private static CommandLineParser BuildParser()
    {
        return new CommandLineParser("digest", ResolveVersion())
            .Description("Cross-platform cryptographic hashing and HMAC — SHA-2/SHA-3/BLAKE2b, safe HMAC key handling.")
            .StandardFlags()
            .Platform("cross-platform",
                replaces: new[] { "sha256sum", "md5sum", "openssl dgst" },
                valueOnWindows: "Native gap-fill — Windows has no first-class HMAC CLI; certutil and Get-FileHash don't cover HMAC; openssl requires a separate install.",
                valueOnUnix: "Consistent flag surface across sha256sum / md5sum variants, plus built-in HMAC, base64/base32 output, and --describe metadata.")
            .ExitCodes(
                (0, "Success"),
                (ExitCode.UsageError, "Usage error: bad flags, unknown value, or flag conflict"),
                (1, "Verification failed (digest --verify mismatch)"),
                (ExitCode.NotExecutable, "Runtime error (file read failure, SHA-3 unavailable)"))
            .StdinDescription("Payload to hash (default when no positional files), or key with --key-stdin")
            .StdoutDescription("Hash (plain single line, sha256sum-compatible multi-file lines, or JSON)")
            .StderrDescription("Warnings (legacy algorithms, insecure --key literal, group-readable key files) and errors")
            .Example("digest file.iso", "SHA-256 of a file")
            .Example("digest *.txt", "Hash every matching file, sha256sum-compatible output")
            .Example("digest --sha512 -s \"hello\"", "SHA-512 of a literal string")
            .Example("digest --hmac sha256 --key-env API_SECRET -s \"payload\"", "HMAC-SHA-256 with key from env var")
            .Example("digest --hmac sha256 --key-file ~/.secret file.bin", "HMAC of a file with key from file")
            .Example("age --decrypt key.age | digest --hmac sha256 --key-stdin -s \"msg\"", "HMAC with key piped from age")
            .Example("digest --verify \"abc123...\" file.bin", "Exit 0 if hash matches")
            .ComposesWith("clip", "digest file.bin | clip", "Copy a hash to the clipboard")
            .ComposesWith("age", "age --decrypt key.age | digest --hmac sha256 --key-stdin ...", "Read HMAC key from an age-encrypted file")
            .ComposesWith("pass", "pass show mykey | digest --hmac sha256 --key-stdin ...", "Read HMAC key from passwordstore")
            .JsonField("algorithm", "string", "Hash algorithm (sha256, sha3-256, hmac-sha256, etc.)")
            .JsonField("format", "string", "Output encoding (hex, base64, base64url, base32)")
            .JsonField("hash", "string", "The encoded hash value")
            .JsonField("source", "string", "Input source (string, stdin, file)")
            .JsonField("path", "string", "(file source only) file path")
            // Algorithm flags
            .Flag("--sha256", "SHA-256 (default)")
            .Flag("--sha384", "SHA-384")
            .Flag("--sha512", "SHA-512")
            .Flag("--sha1", "SHA-1 (legacy; emits warning)")
            .Flag("--md5", "MD5 (legacy; emits warning)")
            .Flag("--sha3-256", "SHA3-256")
            .Flag("--sha3-512", "SHA3-512")
            .Flag("--blake2b", "BLAKE2b-512")
            .Option("--algo", "-a", "ALGO", "Alternative to individual algorithm flags: sha256, sha384, sha512, sha1, md5, sha3-256, sha3-512, blake2b")
            // HMAC
            .Option("--hmac", null, "ALGO", "HMAC mode using the given hash algorithm (requires a key source)")
            .Option("--key-env", null, "VAR", "Read HMAC key from environment variable")
            .Option("--key-file", null, "PATH", "Read HMAC key from file (Unix permission warning if group/other readable)")
            .Flag("--key-stdin", "Read HMAC key from stdin")
            .Option("--key", null, "KEY", "HMAC key as literal argument (emits warning about process visibility)")
            .Flag("--key-raw", "Preserve bytes on --key-file / --key-stdin (skip trailing-newline strip)")
            // Output format
            .Flag("--hex", "Hex output (default, lowercase)")
            .Flag("--base64", "Base64 output (standard alphabet)")
            .Flag("--base64-url", "Base64 URL-safe variant")
            .Flag("--base32", "Crockford base32 output")
            .Flag("--uppercase", "-u", "Uppercase hex output")
            // Input mode
            .Option("--string", "-s", "VALUE", "Hash the literal string VALUE (UTF-8 bytes). Exclusive with positional file args.")
            // Verify
            .Option("--verify", null, "EXPECTED", "Compare output (constant-time); exit 0 if match, 1 if mismatch");
    }

    private static string ResolveVersion()
    {
        string raw = typeof(ArgParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw[..plus] : raw;
    }
}
```

**Implementation notes for the engineer:**

1. **`parsed.GetStringCount("--string")`** may not exist exactly as shown — it's the hypothetical way to count how many times a given option was supplied. If ShellKit's `ParseResult` doesn't expose a "how many times was this option given" counter, you'll need to either (a) check the raw argv for multiple occurrences of `--string`/`-s`, or (b) add such a method to ShellKit. The simpler path is (a): count `--string` and `-s` occurrences in the original argv array before passing to ShellKit. Adapt as needed — the test `Parse_MultipleStrings_Errors` locks the behaviour.
2. **`parsed.Positionals`** is ShellKit's name for the non-flag positional args. If the property is named differently, adapt.
3. Before implementing, open `src/Winix.Ids/ArgParser.cs` for the working reference pattern from a recently-shipped tool.

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj --filter FullyQualifiedName~ArgParserTests`
Expected: all tests pass.

- [ ] **Step 5: Commit**

```
git add src/Winix.Digest/ArgParser.cs tests/Winix.Digest.Tests/ArgParserTests.cs
git commit -m "feat(digest): add ArgParser with compatibility matrix validation"
```

---

### Task 11: Console app Program.cs

Wire it all together. Parse → resolve key (if HMAC) → build hasher → run → format → output. Handle the verify path, the legacy-hash warnings, and broken-pipe.

**Files:**
- Modify: `src/digest/Program.cs` (replace stub)

- [ ] **Step 1: Write `Program.cs`**

```csharp
// src/digest/Program.cs
using System;
using System.IO;
using Winix.Codec;
using Winix.Digest;
using Yort.ShellKit;

namespace Digest;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        var r = ArgParser.Parse(args);

        if (r.IsHandled) return r.HandledExitCode;
        if (!r.Success)
        {
            Console.Error.WriteLine($"digest: {r.Error}");
            Console.Error.WriteLine("Run 'digest --help' for usage.");
            return ExitCode.UsageError;
        }

        var opts = r.Options!;

        // Legacy hash warnings.
        if (opts.Algorithm == HashAlgorithm.Md5)
        {
            Console.Error.WriteLine("digest: warning: MD5 is cryptographically broken; do not use for security-sensitive purposes.");
        }
        else if (opts.Algorithm == HashAlgorithm.Sha1)
        {
            Console.Error.WriteLine("digest: warning: SHA-1 is broken for collision resistance; HMAC-SHA-1 is still acceptable for signing but prefer HMAC-SHA-256 for new systems.");
        }

        try
        {
            // Resolve HMAC key if needed. Payload-vs-key stdin conflict detection:
            // if the source is StdinInput AND we need to read a key from stdin, error.
            byte[]? key = null;
            if (opts.IsHmac)
            {
                if (r.KeySourceForHmac is KeySource.StdinSource && opts.Source is StdinInput)
                {
                    Console.Error.WriteLine("digest: --key-stdin cannot be combined with stdin payload");
                    return ExitCode.UsageError;
                }
                key = KeyResolver.Resolve(
                    source: r.KeySourceForHmac!,
                    stdin: Console.In,
                    stripTrailingNewline: r.StripKeyNewline,
                    stderr: Console.Error,
                    out string? keyError);
                if (keyError is not null)
                {
                    Console.Error.WriteLine($"digest: {keyError}");
                    return ExitCode.UsageError;
                }
            }

            // Build hasher.
            IHasher hasher;
            try
            {
                hasher = opts.IsHmac
                    ? HmacFactory.Create(opts.Algorithm, key!)
                    : HashFactory.Create(opts.Algorithm);
            }
            catch (PlatformNotSupportedException)
            {
                Console.Error.WriteLine("digest: SHA-3 is not available on this platform (OS crypto backend missing)");
                return ExitCode.NotExecutable;
            }

            // Run.
            var results = HashRunner.Run(
                source: opts.Source,
                hasher: hasher,
                stdin: Console.In,
                out string? runError);
            if (runError is not null)
            {
                Console.Error.WriteLine($"digest: {runError}");
                return ExitCode.UsageError;
            }

            // Verify mode.
            if (opts.VerifyExpected is not null)
            {
                // Verifier uses constant-time compare. Multi-file already blocked at parse time.
                bool match = Verifier.Verify(results[0].Hash, opts.VerifyExpected, opts.Format);
                if (!match)
                {
                    Console.Error.WriteLine("digest: verification failed");
                    return 1;
                }
                return ExitCode.Success;
            }

            // Output.
            if (opts.Json)
            {
                if (results.Count == 1)
                {
                    Console.Out.WriteLine(Formatting.JsonElement(results[0].Hash, results[0].Path, opts));
                }
                else
                {
                    Console.Out.Write('[');
                    for (int i = 0; i < results.Count; i++)
                    {
                        if (i > 0) Console.Out.Write(',');
                        Console.Out.Write(Formatting.JsonElement(results[i].Hash, results[i].Path, opts));
                    }
                    Console.Out.WriteLine(']');
                }
            }
            else
            {
                if (results.Count == 1 && results[0].Path is null)
                {
                    // String or stdin input — plain hash, no filename marker.
                    Console.Out.WriteLine(Formatting.PlainSingle(results[0].Hash, opts.Format, opts.Uppercase));
                }
                else
                {
                    // Single file OR multi-file — sha256sum-compatible lines with binary-mode marker.
                    foreach (var result in results)
                    {
                        Console.Out.WriteLine(Formatting.PlainMultiLine(result.Hash, result.Path!, opts.Format, opts.Uppercase));
                    }
                }
            }

            return ExitCode.Success;
        }
        catch (IOException)
        {
            // Downstream reader closed the pipe (e.g. `digest *.log | head`).
            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"digest: error: {ex.Message}");
            return 1;
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build Winix.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Full test suite regression**

Run: `dotnet test Winix.sln`
Expected: all tests pass.

- [ ] **Step 4: Manual smoke tests**

```
dotnet run --project src/digest/digest.csproj -- -s "abc"
# expected: ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad

dotnet run --project src/digest/digest.csproj -- --sha512 -s "hello"
# expected: 9b71d224bd62f3785d96d46ad3ea3d73319bfbc2890caadae2dff72519673ca72323c3d99ba5c11d7c7acc6e14b8c5da0c4663475c2e5c3adef46f73bcdec043

dotnet run --project src/digest/digest.csproj -- --md5 -s "hi"
# expected: stderr warning about MD5; stdout 49f68a5c8493ec2c0bf489821c21fc3b

dotnet run --project src/digest/digest.csproj -- --hmac sha256 --key "secret" -s "payload"
# expected: stderr --key warning; stdout 3b8227501c37c6d0dfabfa04b5fc73ad2b0f3e5aa4fa7f2aa2c8ebd2cf5f0f1f (approx.)

dotnet run --project src/digest/digest.csproj -- --verify e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 -s ""
# expected: exit 0, no output

dotnet run --project src/digest/digest.csproj -- --verify wronghash -s ""
# expected: stderr "digest: verification failed", exit 1

dotnet run --project src/digest/digest.csproj -- --describe
# expected: JSON with name=digest, 8 algorithms, HMAC flags, examples

dotnet run --project src/digest/digest.csproj -- src/digest/digest.csproj
# expected: <hash> *src/digest/digest.csproj

dotnet run --project src/digest/digest.csproj -- nonexistent.file
# expected: stderr "'nonexistent.file' not found — use --string to hash as a literal...", exit 125
```

**If any smoke test produces unexpected output, stop and investigate before committing.**

- [ ] **Step 5: Commit**

```
git add src/digest/Program.cs
git commit -m "feat(digest): implement console app Program.cs"
```

---

### Task 12: Docs — README, man page, AI guide, llms.txt

Match the patterns from `src/ids/` and `src/clip/`.

**Files:**
- Replace: `src/digest/README.md` (overwrite the Task 1 placeholder)
- Create: `src/digest/man/man1/digest.1`
- Modify: `src/digest/digest.csproj` (add the man-page `<Content Include>`)
- Create: `docs/ai/digest.md`
- Modify: `llms.txt` (add digest entry after `ids`)

- [ ] **Step 1: Read reference files first**

Before writing, open: `src/ids/README.md`, `src/ids/man/man1/ids.1`, `docs/ai/ids.md`, `src/ids/ids.csproj` (for the `<Content Include>` pattern), and `llms.txt`.

- [ ] **Step 2: Write `src/digest/README.md`**

Match the structure of `src/ids/README.md`. Sections:
- H1 `# digest` with one-line description.
- Install — Scoop / Winget / .NET Tool / Direct Download (standard Winix copy).
- Usage with synopsis: `digest [options] [file ...]` or `digest -s VALUE`.
- Examples — every flag combination shown in the design doc's CLI Interface section, plus the composability examples (`age | digest --key-stdin`, `pass show | digest --key-stdin`, `digest | clip`).
- Options — full table matching the design doc's flags table.
- Algorithms — table listing all 8 algorithms with bit length, family, and status (modern/legacy/warning).
- HMAC key handling — **dedicated section**, the "killer feature". Show all four modes with threat-model notes. Include the composition pattern explicitly.
- **Encrypted-at-rest key files** — short paragraph pointing at the `--key-stdin` + external-tool pattern (age, gpg, pass, Keychain/secret-tool).
- Input modes — explain that positionals are always files, `-s`/`--string` for literals. Call out the "missing-file errors rather than silently hashing the string" behaviour as a safety feature.
- Output formats — hex/base64/base64url/base32 table with one example each.
- Verify mode — how `--verify` works, exit codes.
- Multi-file output format — explain the `*` marker.
- Exit codes — 0/1/125/126 table.
- Differences from sha256sum — short list (all-or-nothing validation, binary-mode marker default, built-in HMAC, base64/base32 output).
- Related tools — `clip` (`digest file | clip`), `ids`, future `protect`/`unprotect`.
- See Also — `man digest`, `digest --describe`.

- [ ] **Step 3: Write `src/digest/man/man1/digest.1`**

Groff man page modelled on `src/ids/man/man1/ids.1`. TH header: `.TH DIGEST 1 "2026-04-19" "Winix" "User Commands"`. Sections: NAME, SYNOPSIS, DESCRIPTION, OPTIONS, EXIT STATUS, EXAMPLES, SECURITY (the HMAC key handling notes — this is a digest-specific section not in other tools' man pages), ENVIRONMENT, SEE ALSO.

- [ ] **Step 4: Update `src/digest/digest.csproj` to include the man page**

Add to the csproj, alongside the existing `<None Include="README.md" …/>`:

```xml
<ItemGroup>
  <Content Include="man\man1\digest.1" CopyToPublishDirectory="PreserveNewest" Link="share\man\man1\digest.1" />
</ItemGroup>
```

- [ ] **Step 5: Write `docs/ai/digest.md`**

AI agent guide, ~100-150 lines, modelled on `docs/ai/ids.md`. Cover: what it does, when to use / when not to use, basic invocation examples, HMAC invocation examples (highlight the four key modes), JSON output shape, platform notes (cross-platform, SHA-3 requires newer OS), composability (pipes to clip, from age/gpg/pass/secret-tool), `--describe` reference.

- [ ] **Step 6: Add `digest` to `llms.txt`**

Add after the `ids` entry:

```
- [digest](docs/ai/digest.md): Cryptographic hashing and HMAC — SHA-2/SHA-3/BLAKE2b with safe HMAC key handling (env/file/stdin/literal). Cross-platform replacement for sha256sum/md5sum/openssl dgst with first-class HMAC on Windows.
```

- [ ] **Step 7: Verify build + publish output**

Run: `dotnet build Winix.sln`
Expected: 0 warnings, 0 errors.

Run: `dotnet publish src/digest/digest.csproj -c Release -r win-x64`
Verify: `src/digest/bin/Release/net10.0/win-x64/publish/share/man/man1/digest.1` exists.

- [ ] **Step 8: Commit**

```
git add src/digest/README.md src/digest/man src/digest/digest.csproj docs/ai/digest.md llms.txt
git commit -m "docs(digest): add README, man page, AI guide, llms.txt entry"
```

---

### Task 13: Pipeline — scoop manifest, release workflows, CLAUDE.md

Wire `digest` into the release infrastructure. Pure infrastructure, no library code.

**Files:**
- Create: `bucket/digest.json`
- Modify: `.github/workflows/release.yml` — 6 insertions
- Modify: `.github/workflows/post-publish.yml` — 2 insertions
- Modify: `CLAUDE.md` — 3 sections

- [ ] **Step 1: Read reference files**

Open: `bucket/ids.json` (template), `.github/workflows/release.yml` (find every `ids` reference), `.github/workflows/post-publish.yml` (same), `CLAUDE.md` (project layout, NuGet package IDs, scoop manifests).

- [ ] **Step 2: Create `bucket/digest.json`**

Model on `bucket/ids.json`. Set `version` to the current v0.4.0 placeholder (release pipeline rewrites it). URL points at `releases/download/v$version/digest-win-x64.zip`, `bin` is `digest.exe`.

```json
{
  "version": "0.4.0",
  "description": "Cross-platform cryptographic hashing and HMAC — SHA-2/SHA-3/BLAKE2b with safe key handling.",
  "homepage": "https://github.com/Yortw/winix",
  "license": "MIT",
  "architecture": {
    "64bit": {
      "url": "https://github.com/Yortw/winix/releases/download/v0.4.0/digest-win-x64.zip",
      "hash": "",
      "bin": "digest.exe"
    }
  },
  "checkver": "github",
  "autoupdate": {
    "architecture": {
      "64bit": {
        "url": "https://github.com/Yortw/winix/releases/download/v$version/digest-win-x64.zip"
      }
    }
  }
}
```

- [ ] **Step 3: Modify `.github/workflows/release.yml`**

Find every occurrence of `ids` (the most recent tool) and add a parallel `digest` entry in the same place. Six places to update:

1. `dotnet pack` step — `Pack digest` after `Pack ids`
2. `dotnet publish` per-RID step — `Publish digest` after `Publish ids`
3. Linux/macOS zip staging — `digest-${{ matrix.rid }}.zip` line after the `ids` zip line
4. Windows zip staging — `Compress-Archive` for `digest` after `ids`
5. Combined Winix zip — `Copy-Item digest.exe` after `ids.exe`
6. Tools map in the JSON metadata generation:
   ```
   digest: { description: "Cryptographic hashing and HMAC — SHA-2/SHA-3/BLAKE2b, HMAC on Windows.", packages: { winget: "Winix.Digest", scoop: "digest", brew: "digest", dotnet: "Winix.Digest" } }
   ```

- [ ] **Step 4: Modify `.github/workflows/post-publish.yml`**

Two insertions:

1. `update_manifest bucket/digest.json aot/digest-win-x64.zip` after the `ids` line
2. `generate_manifests "digest" "Digest" "Cross-platform cryptographic hashing and HMAC — SHA-2/SHA-3/BLAKE2b with safe key handling"` after the `ids` line

- [ ] **Step 5: Modify `CLAUDE.md`**

Three sections:

1. Project layout — add:
   ```
   src/Winix.Digest/          — class library (hashers, HMAC, key resolver, formatting)
   src/digest/                — console app entry point
   tests/Winix.Digest.Tests/  — xUnit tests
   ```
   Insert alphabetically/positionally near the other tool entries.

2. NuGet package IDs list — add `Winix.Digest` at the end.

3. Scoop manifests list — add `digest.json` at the end.

- [ ] **Step 6: Verify build**

Run: `dotnet build Winix.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```
git add bucket/digest.json .github/workflows/release.yml .github/workflows/post-publish.yml CLAUDE.md
git commit -m "ci(digest): integrate digest into release and post-publish pipelines"
```

---

## Self-Review

Ran against the spec with fresh eyes.

**1. Spec coverage.**

- Overview / motivation → Task 12 (README, AI guide).
- Project structure (§Project Structure) → Task 1 (scaffolding), Task 3 (core types), Tasks 2–11 (library implementation), Task 12 (docs), Task 13 (distribution).
- CLI interface (§CLI Interface) → Task 10 (ArgParser).
- Input mode resolution → Task 7 (HashRunner) + Task 10 (ArgParser classifies the positionals into `InputSource`).
- Algorithms & HMAC dispatch (§Algorithms and HMAC) → Tasks 4, 5 (HashFactory, HmacFactory).
- Key resolution (§Key resolution) → Task 6 (KeyResolver + KeyFilePermissionsCheck).
- Output format composition (§Output format composition) → Task 9 (Formatting).
- Verify mode (§Verify mode) → Task 8 (Verifier) + Task 11 (Program.cs wiring).
- Error handling (§Error Handling) → Task 10 (ArgParser 125s) + Task 11 (Program.cs runtime handling).
- Testing (§Testing) → Tasks 2, 4, 5, 6, 7, 8, 9, 10 each include the listed tests.
- Distribution (§Distribution) → Task 13.

All spec sections have a task.

**2. Placeholder scan.**

Three "verification point" notes remain — Task 4 (Blake2Fast API confirmation), Task 5 (same, with key), Task 10 (ShellKit ArgParser API confirmation including the `GetStringCount` hypothetical). These are *verification points*, not TODOs — each names the exact file/package to cross-reference.

No "implement later" / "TBD" / "add appropriate X" placeholders.

**3. Type consistency.**

- `DigestOptions` record (Task 3): 8 properties (`Algorithm`, `IsHmac`, `HmacKey`, `Format`, `Uppercase`, `Source`, `VerifyExpected`, `Json`). Used in Tasks 9, 10, 11.
- `HashAlgorithm` enum: 8 values. Used consistently in Tasks 4, 5, 10.
- `InputSource` types (`StringInput`, `StdinInput`, `SingleFileInput`, `MultiFileInput`): used in Tasks 7, 10, 11.
- `IHasher` interface: `Hash(ReadOnlySpan<byte>)` and `Hash(Stream)`. Consistent across Tasks 4, 5, 7, 11.
- `KeySource` discriminated union: four factory methods. Used in Tasks 6, 10, 11.
- `HashResult` record: `(byte[] Hash, string? Path)`. Used in Tasks 7, 11.
- `ArgParser.Result` record: 7 fields (`Options`, `Error`, `IsHandled`, `HandledExitCode`, `UseColor`, `KeySourceForHmac`, `StripKeyNewline`). Used in Task 10 test + Task 11 consumer.

No drift. The `HmacKey` field on `DigestOptions` is set by ArgParser as `null` (bytes aren't resolved yet — Program.cs calls KeyResolver before building the hasher). Documented in Task 3 code comments and used consistently.

**4. Other observations:**

- The ArgParser's `GetStringCount` is a hypothetical method. The engineer may need to fall back to scanning argv directly if ShellKit doesn't expose the count. The test `Parse_MultipleStrings_Errors` pins the required behaviour.
- `Blake2Fast` API calls (`Blake2b.ComputeHash`, `Blake2b.CreateIncrementalHasher`) are my best guess at the package's surface. Verify at implementation time.
- The `InferSource` helper in Formatting (Task 9) reads `options.Source` to decide the `"source"` JSON field. Tasks that call `JsonElement` with `path != null` override to `"file"`, so single-file multi-file mode behaves correctly.

---

## Execution Handoff

Plan complete and saved to `docs/plans/2026-04-19-digest-plan.md`. Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Good fit for a 13-task plan with TDD discipline and the two "verify the external API" points in Tasks 4/5/10.

2. **Inline Execution** — I execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
