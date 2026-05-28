# mksecret Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `mksecret`, a cross-platform CLI that generates random secrets in three subcommand modes — `password` (random characters), `phrase` (EFF diceware), `key` (encoded high-entropy bytes) — over one CSPRNG-bounded core.

**Architecture:** Class library `Winix.MkSecret` (all logic, testable) + thin console app `src/mksecret`. One `ISecretGenerator` per mode, each taking an injected `Winix.Codec.ISecureRandom`; unbiased selection via a shared internal rejection-sampling helper (the `NanoidGenerator` pattern generalised to arbitrary range sizes). Arg parsing via `Yort.ShellKit.CommandLineParser` with `positional[0]` subcommand dispatch. `Cli.Run(args, stdout, stderr, randomOverride?)` seam mirrors `Winix.Ids.Cli`.

**Tech Stack:** .NET 10, NativeAOT, xUnit. Reuses `Winix.Codec` (`ISecureRandom`, `Hex`, `Base64`, `Base32Crockford`) unchanged. No new dependencies.

**Spec:** [design](2026-05-28-mksecret-design.md) · [ADR](2026-05-28-mksecret-adr.md)

---

## File Structure

```
src/Winix.MkSecret/
  SecretMode.cs            enum Password|Phrase|Key
  Charset.cs               enum + Charsets.ToChars(): the 5 named character sets
  KeyEncoding.cs           enum Hex|Base64|Base64Url|Base32
  MkSecretOptions.cs       immutable options record + Defaults
  Sampling.cs              internal UniformIndex(rng, count) — rejection sampling, any range
  ISecretGenerator.cs      Generate(MkSecretOptions) -> string
  PasswordGenerator.cs     random chars from a Charset
  PhraseGenerator.cs       diceware words from the embedded EFF list
  KeyGenerator.cs          random bytes -> Hex/Base64/Base64Url(unpadded)/Base32
  EffWordList.cs           static readonly string[7776] (generated, committed)
  SecretGeneratorFactory.cs  SecretMode -> ISecretGenerator
  Entropy.cs               BitsFor(MkSecretOptions) -> double
  Formatting.cs            entropy note + JSON envelope
  ArgParser.cs             ShellKit parser, subcommand dispatch, validation
  Cli.cs                   Run(args, stdout, stderr, randomOverride?)

src/mksecret/
  Program.cs               thin shim -> Cli.Run
  mksecret.csproj          AOT, PackAsTool, PackageId=Winix.MkSecret
  README.md
  man/man1/mksecret.1
  CHANGELOG.md

tests/Winix.MkSecret.Tests/
  Winix.MkSecret.Tests.csproj
  SequenceRandom.cs        ISecureRandom test double (scripted byte stream)
  CharsetTests.cs
  SamplingTests.cs
  PasswordGeneratorTests.cs
  PhraseGeneratorTests.cs
  KeyGeneratorTests.cs
  EffWordListTests.cs
  EntropyTests.cs
  ArgParserTests.cs
  FormattingTests.cs
  CliTests.cs
  RealRandomLivenessTests.cs
```

---

## Task 1: Project scaffold

**Files:**
- Create: `src/Winix.MkSecret/Winix.MkSecret.csproj`
- Create: `src/mksecret/mksecret.csproj`
- Create: `tests/Winix.MkSecret.Tests/Winix.MkSecret.Tests.csproj`

- [ ] **Step 1: Create the class-library csproj**

`src/Winix.MkSecret/Winix.MkSecret.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.Codec\Winix.Codec.csproj" />
  </ItemGroup>
</Project>
```
(Nullable, ImplicitUsings, and warnings-as-errors are inherited from `Directory.Build.props`.)

- [ ] **Step 2: Create the console csproj** (mirrors `src/ids/ids.csproj`)

`src/mksecret/mksecret.csproj`:
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
    <ToolCommandName>mksecret</ToolCommandName>
    <PackageId>Winix.MkSecret</PackageId>
    <Description>Cross-platform secret generator — random passwords, EFF diceware passphrases, and encoded high-entropy keys.</Description>
    <PackageTags>cli;command-line;cross-platform;windows;macos;linux;aot;dotnet-tool;winix;password;passphrase;diceware;secret;token;crypto</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.MkSecret\Winix.MkSecret.csproj" />
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
  <ItemGroup>
    <Content Include="man\man1\mksecret.1" CopyToPublishDirectory="PreserveNewest" Link="share\man\man1\mksecret.1" />
  </ItemGroup>
</Project>
```

> Note: `ArgParser`/`Cli` live in the library and use ShellKit, so the **library** also needs the ShellKit reference. Add it to `Winix.MkSecret.csproj` too:
> ```xml
> <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
> ```
> (Check how `Winix.Ids.csproj` references ShellKit and mirror exactly — the library, not just the console app, references it there.)

- [ ] **Step 3: Create a placeholder README + man + CHANGELOG so the csproj content includes resolve**

Create `src/mksecret/README.md` with a single line `# mksecret` (fleshed out in Task 14).
Create `src/mksecret/man/man1/mksecret.1` with `.TH MKSECRET 1` (fleshed out in Task 14).
Create `src/mksecret/CHANGELOG.md` with `# Changelog` (fleshed out in Task 15).

- [ ] **Step 4: Create the test csproj** (xUnit; `InvariantGlobalization=true` per `feedback_invariant_globalization_resource_keys`)

`tests/Winix.MkSecret.Tests/Winix.MkSecret.Tests.csproj` — copy `tests/Winix.Ids.Tests/Winix.Ids.Tests.csproj` verbatim, then change every `Ids` to `MkSecret` (PackageReference list, `<ProjectReference>` target, RootNamespace if present). Confirm it contains `<InvariantGlobalization>true</InvariantGlobalization>`.

- [ ] **Step 5: Add all three projects to the solution**

```bash
dotnet sln Winix.sln add src/Winix.MkSecret/Winix.MkSecret.csproj
dotnet sln Winix.sln add src/mksecret/mksecret.csproj
dotnet sln Winix.sln add tests/Winix.MkSecret.Tests/Winix.MkSecret.Tests.csproj
```

- [ ] **Step 6: Verify the solution builds**

Run: `dotnet build Winix.sln`
Expected: build succeeds (empty projects compile).

- [ ] **Step 7: Commit**

```bash
git add src/Winix.MkSecret src/mksecret tests/Winix.MkSecret.Tests Winix.sln
git commit -m "feat(mksecret): project scaffold (lib + console + tests)"
```

---

## Task 2: Enums and the charset table

**Files:**
- Create: `src/Winix.MkSecret/SecretMode.cs`, `Charset.cs`, `KeyEncoding.cs`
- Test: `tests/Winix.MkSecret.Tests/CharsetTests.cs`

- [ ] **Step 1: Write the failing charset test**

`CharsetTests.cs`:
```csharp
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class CharsetTests
{
    [Theory]
    [InlineData(Charset.Alphanumeric, 62)]
    [InlineData(Charset.Full, 94)]
    [InlineData(Charset.Alpha, 52)]
    [InlineData(Charset.Digits, 10)]
    [InlineData(Charset.Safe, 56)]
    public void ToChars_has_expected_size(Charset cs, int expected)
    {
        Assert.Equal(expected, Charsets.ToChars(cs).Length);
    }

    [Fact]
    public void Safe_excludes_visually_ambiguous_chars()
    {
        string safe = Charsets.ToChars(Charset.Safe);
        foreach (char c in "l1IO0o")
        {
            Assert.DoesNotContain(c, safe);
        }
    }

    [Fact]
    public void Full_is_printable_ascii_33_to_126()
    {
        string full = Charsets.ToChars(Charset.Full);
        for (char c = (char)33; c <= 126; c++)
        {
            Assert.Contains(c, full);
        }
    }

    [Fact]
    public void No_charset_has_duplicate_characters()
    {
        foreach (Charset cs in System.Enum.GetValues<Charset>())
        {
            string chars = Charsets.ToChars(cs);
            Assert.Equal(chars.Length, new System.Collections.Generic.HashSet<char>(chars).Count);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter CharsetTests`
Expected: FAIL (types don't exist).

- [ ] **Step 3: Create the enums**

`SecretMode.cs`:
```csharp
namespace Winix.MkSecret;

/// <summary>The three generation modes, selected by the first positional subcommand.</summary>
public enum SecretMode
{
    /// <summary>Random-character password.</summary>
    Password,
    /// <summary>Diceware passphrase from the EFF long wordlist.</summary>
    Phrase,
    /// <summary>Encoded high-entropy random bytes (machine secret / key).</summary>
    Key,
}
```

`KeyEncoding.cs`:
```csharp
namespace Winix.MkSecret;

/// <summary>Output encoding for <see cref="SecretMode.Key"/>.</summary>
public enum KeyEncoding
{
    /// <summary>Lowercase hex.</summary>
    Hex,
    /// <summary>Standard base64 (RFC 4648 §4), padded.</summary>
    Base64,
    /// <summary>URL-safe base64 (RFC 4648 §5), padding stripped.</summary>
    Base64Url,
    /// <summary>Crockford base32 (uppercase, unpadded, ambiguity-free).</summary>
    Base32,
}
```

- [ ] **Step 4: Create the charset table**

`Charset.cs`:
```csharp
using System;

namespace Winix.MkSecret;

/// <summary>Named character sets for <see cref="SecretMode.Password"/>.</summary>
public enum Charset
{
    /// <summary>A–Z a–z 0–9 (62 chars).</summary>
    Alphanumeric,
    /// <summary>All printable ASCII, code points 33–126 (94 chars, includes symbols).</summary>
    Full,
    /// <summary>A–Z a–z (52 chars).</summary>
    Alpha,
    /// <summary>0–9 (10 chars).</summary>
    Digits,
    /// <summary>Alphanumeric minus visually-ambiguous l 1 I O 0 o (56 chars).</summary>
    Safe,
}

/// <summary>Resolves a <see cref="Charset"/> to its concrete character string. Order is fixed so
/// that injected-RNG tests can pin exact output.</summary>
public static class Charsets
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Dig = "0123456789";
    private const string Alphanum = Upper + Lower + Dig;

    /// <summary>Returns the character set for <paramref name="charset"/>.</summary>
    public static string ToChars(Charset charset) => charset switch
    {
        Charset.Alphanumeric => Alphanum,
        Charset.Full => BuildFull(),
        Charset.Alpha => Upper + Lower,
        Charset.Digits => Dig,
        Charset.Safe => RemoveChars(Alphanum, "l1IO0o"),
        _ => throw new ArgumentOutOfRangeException(nameof(charset)),
    };

    private static string BuildFull()
    {
        char[] c = new char[94];
        for (int i = 0; i < 94; i++) { c[i] = (char)(33 + i); }
        return new string(c);
    }

    private static string RemoveChars(string source, string remove)
    {
        Span<char> buf = stackalloc char[source.Length];
        int n = 0;
        foreach (char c in source)
        {
            if (remove.IndexOf(c) < 0) { buf[n++] = c; }
        }
        return new string(buf.Slice(0, n));
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter CharsetTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.MkSecret/SecretMode.cs src/Winix.MkSecret/Charset.cs src/Winix.MkSecret/KeyEncoding.cs tests/Winix.MkSecret.Tests/CharsetTests.cs
git commit -m "feat(mksecret): SecretMode/KeyEncoding enums + named charset table"
```

---

## Task 3: Sampling helper (unbiased uniform index) + test double

**Files:**
- Create: `src/Winix.MkSecret/Sampling.cs`
- Create: `tests/Winix.MkSecret.Tests/SequenceRandom.cs`
- Test: `tests/Winix.MkSecret.Tests/SamplingTests.cs`

- [ ] **Step 1: Create the test double**

`SequenceRandom.cs`:
```csharp
using System;
using Winix.Codec;

namespace Winix.MkSecret.Tests;

/// <summary>ISecureRandom that yields a fixed, scripted byte sequence so generators produce
/// deterministic output. Throws if the script is exhausted (tests must supply enough bytes).</summary>
public sealed class SequenceRandom : ISecureRandom
{
    private readonly byte[] _bytes;
    private int _pos;

    public SequenceRandom(params byte[] bytes) => _bytes = bytes;

    public void Fill(Span<byte> destination)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            if (_pos >= _bytes.Length)
            {
                throw new InvalidOperationException("SequenceRandom exhausted: supply more scripted bytes.");
            }
            destination[i] = _bytes[_pos++];
        }
    }
}
```

- [ ] **Step 2: Write the failing sampling test**

`SamplingTests.cs`:
```csharp
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class SamplingTests
{
    [Fact]
    public void UniformIndex_returns_masked_value_in_range()
    {
        // count=62 -> mask=63 -> 1 byte. Byte 5 is in [0,62) -> index 5.
        var rng = new SequenceRandom(5);
        Assert.Equal(5, Sampling.UniformIndex(rng, 62));
    }

    [Fact]
    public void UniformIndex_rejects_out_of_range_draws_no_modulo_bias()
    {
        // count=62 -> mask=63. Bytes 62 and 63 survive the mask but are >= 62, so they MUST be
        // rejected (a modulo-folding bug would map them to 0 and 1). Then 7 -> index 7.
        var rng = new SequenceRandom(62, 63, 7);
        Assert.Equal(7, Sampling.UniformIndex(rng, 62));
    }

    [Fact]
    public void UniformIndex_handles_ranges_above_one_byte()
    {
        // count=7776 -> mask=8191 -> 2 bytes, big-endian. 0x00,0x05 -> 5.
        var rng = new SequenceRandom(0x00, 0x05);
        Assert.Equal(5, Sampling.UniformIndex(rng, 7776));
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter SamplingTests`
Expected: FAIL (`Sampling` not defined).

- [ ] **Step 4: Implement `Sampling`**

`Sampling.cs`:
```csharp
using System;
using Winix.Codec;

namespace Winix.MkSecret;

/// <summary>Unbiased uniform selection of an index in <c>[0, count)</c> from a CSPRNG.
/// Generalises <c>Winix.Ids.NanoidGenerator</c>'s rejection-sampling-against-a-power-of-two-mask
/// to ranges larger than one byte (the EFF wordlist needs 13 bits). Rejection — not modulo —
/// is what removes bias.</summary>
public static class Sampling
{
    /// <summary>Returns a uniformly-random index in <c>[0, count)</c>. <paramref name="count"/> must be ≥ 1.</summary>
    public static int UniformIndex(ISecureRandom random, int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 1) return 0;

        int mask = NextPowerOfTwo(count) - 1;
        int bytesNeeded = mask < 0x100 ? 1 : (mask < 0x10000 ? 2 : 4);
        Span<byte> buf = stackalloc byte[4];

        while (true)
        {
            random.Fill(buf.Slice(0, bytesNeeded));
            int v = 0;
            for (int i = 0; i < bytesNeeded; i++) { v = (v << 8) | buf[i]; }
            v &= mask;
            if (v < count) { return v; }
            // else reject: masked value landed in [count, 2^k) — draw again to avoid modulo bias.
        }
    }

    private static int NextPowerOfTwo(int n)
    {
        int p = 1;
        while (p < n) { p <<= 1; }
        return p;
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter SamplingTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.MkSecret/Sampling.cs tests/Winix.MkSecret.Tests/SequenceRandom.cs tests/Winix.MkSecret.Tests/SamplingTests.cs
git commit -m "feat(mksecret): unbiased UniformIndex sampler + scripted-RNG test double"
```

---

## Task 4: Options record + generator interface

**Files:**
- Create: `src/Winix.MkSecret/MkSecretOptions.cs`, `ISecretGenerator.cs`

- [ ] **Step 1: Create the options record**

`MkSecretOptions.cs`:
```csharp
namespace Winix.MkSecret;

/// <summary>Parsed, validated options. Per-mode fields are only meaningful for their mode;
/// <see cref="ArgParser"/> only populates the relevant ones.</summary>
public sealed record MkSecretOptions(
    SecretMode Mode,
    int Length,
    Charset Charset,
    int Words,
    string Separator,
    bool Capitalize,
    bool Number,
    int Bytes,
    KeyEncoding Encoding,
    int Count,
    bool Json,
    bool Quiet)
{
    /// <summary>Default values applied before per-mode flags. Immutable shared singleton.</summary>
    public static readonly MkSecretOptions Defaults = new(
        Mode: SecretMode.Password,
        Length: 20,
        Charset: Charset.Alphanumeric,
        Words: 6,
        Separator: "-",
        Capitalize: false,
        Number: false,
        Bytes: 32,
        Encoding: KeyEncoding.Base64Url,
        Count: 1,
        Json: false,
        Quiet: false);
}
```

- [ ] **Step 2: Create the generator interface**

`ISecretGenerator.cs`:
```csharp
namespace Winix.MkSecret;

/// <summary>Generates one secret string for its mode. Implementations take an injected
/// <see cref="Winix.Codec.ISecureRandom"/> so tests can pin output.</summary>
public interface ISecretGenerator
{
    /// <summary>Generates a single secret using the mode-relevant fields of <paramref name="options"/>.</summary>
    string Generate(MkSecretOptions options);
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/Winix.MkSecret`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add src/Winix.MkSecret/MkSecretOptions.cs src/Winix.MkSecret/ISecretGenerator.cs
git commit -m "feat(mksecret): options record + ISecretGenerator interface"
```

---

## Task 5: PasswordGenerator

**Files:**
- Create: `src/Winix.MkSecret/PasswordGenerator.cs`
- Test: `tests/Winix.MkSecret.Tests/PasswordGeneratorTests.cs`

- [ ] **Step 1: Write the failing test**

`PasswordGeneratorTests.cs`:
```csharp
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class PasswordGeneratorTests
{
    private static MkSecretOptions Opts(int length, Charset cs) =>
        MkSecretOptions.Defaults with { Mode = SecretMode.Password, Length = length, Charset = cs };

    [Fact]
    public void Generate_maps_bytes_to_charset_indices()
    {
        // alphanumeric = "ABC...Zabc...z0..9". Indices 0,1,5 -> 'A','B','F'.
        var rng = new SequenceRandom(0, 1, 5);
        var gen = new PasswordGenerator(rng);
        Assert.Equal("ABF", gen.Generate(Opts(3, Charset.Alphanumeric)));
    }

    [Fact]
    public void Generate_only_emits_charset_members()
    {
        var rng = new SequenceRandom(new byte[64]); // all zeros -> index 0 repeatedly
        var gen = new PasswordGenerator(rng);
        string pw = gen.Generate(Opts(20, Charset.Digits));
        Assert.Equal(20, pw.Length);
        Assert.All(pw, c => Assert.Contains(c, Charsets.ToChars(Charset.Digits)));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter PasswordGeneratorTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

`PasswordGenerator.cs`:
```csharp
using System;
using Winix.Codec;

namespace Winix.MkSecret;

/// <summary>Generates a random-character password: each character is an independent unbiased
/// draw from the selected <see cref="Charset"/> (pure random — no forced class composition,
/// per ADR §5).</summary>
public sealed class PasswordGenerator : ISecretGenerator
{
    private readonly ISecureRandom _random;

    /// <summary>Constructs a generator over an injectable CSPRNG.</summary>
    /// <exception cref="ArgumentNullException">If <paramref name="random"/> is null.</exception>
    public PasswordGenerator(ISecureRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    /// <inheritdoc/>
    public string Generate(MkSecretOptions options)
    {
        string alphabet = Charsets.ToChars(options.Charset);
        char[] output = new char[options.Length];
        for (int i = 0; i < output.Length; i++)
        {
            output[i] = alphabet[Sampling.UniformIndex(_random, alphabet.Length)];
        }
        return new string(output);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter PasswordGeneratorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.MkSecret/PasswordGenerator.cs tests/Winix.MkSecret.Tests/PasswordGeneratorTests.cs
git commit -m "feat(mksecret): PasswordGenerator (pure-random chars via UniformIndex)"
```

---

## Task 6: Embed the EFF long wordlist

**Files:**
- Create: `src/Winix.MkSecret/EffWordList.cs` (generated)
- Test: `tests/Winix.MkSecret.Tests/EffWordListTests.cs`

- [ ] **Step 1: Download the canonical EFF long wordlist and generate the C# array**

The canonical file is `eff_large_wordlist.txt` from the EFF (7776 lines, each `NNNNN<TAB>word`).
Run (bash):
```bash
curl -fsSL https://www.eff.org/files/2016/07/18/eff_large_wordlist.txt -o tmp/eff_large_wordlist.txt
wc -l tmp/eff_large_wordlist.txt    # expect 7776
```
If the URL is unreachable, obtain the file from the EFF diceware page and place it at `tmp/eff_large_wordlist.txt`. Do not hand-type words.

- [ ] **Step 2: Transform the file into `EffWordList.cs`**

Run (bash) — strips the `NNNNN<TAB>` prefix, emits a C# initializer:
```bash
{
  echo 'namespace Winix.MkSecret;'
  echo ''
  echo '/// <summary>The EFF "large" diceware wordlist (7776 words). Embedded as a compiled array'
  echo '/// (trim/AOT-safe, no resource-manifest lookup). Source: EFF eff_large_wordlist.txt.</summary>'
  echo 'public static class EffWordList'
  echo '{'
  echo '    /// <summary>The 7776 words, index-aligned to diceware order.</summary>'
  echo '    public static readonly string[] Words ='
  echo '    {'
  cut -f2 tmp/eff_large_wordlist.txt | sed 's/.*/        "&",/'
  echo '    };'
  echo '}'
} > src/Winix.MkSecret/EffWordList.cs
```
Then `rm tmp/eff_large_wordlist.txt`.

- [ ] **Step 3: Write the integrity test**

`EffWordListTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class EffWordListTests
{
    [Fact]
    public void Has_exactly_7776_words()
        => Assert.Equal(7776, EffWordList.Words.Length);

    [Fact]
    public void Words_are_unique()
        => Assert.Equal(EffWordList.Words.Length, new HashSet<string>(EffWordList.Words).Count);

    [Fact]
    public void Words_are_lowercase_ascii_no_whitespace()
    {
        foreach (string w in EffWordList.Words)
        {
            Assert.False(string.IsNullOrWhiteSpace(w));
            Assert.All(w, c => Assert.True(c >= 'a' && c <= 'z', $"unexpected char in '{w}'"));
        }
    }
}
```

- [ ] **Step 4: Build + run the test**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter EffWordListTests`
Expected: PASS (7776 unique lowercase words).

- [ ] **Step 5: Commit**

```bash
git add src/Winix.MkSecret/EffWordList.cs tests/Winix.MkSecret.Tests/EffWordListTests.cs
git commit -m "feat(mksecret): embed EFF long wordlist (7776 words) + integrity tests"
```

---

## Task 7: PhraseGenerator

**Files:**
- Create: `src/Winix.MkSecret/PhraseGenerator.cs`
- Test: `tests/Winix.MkSecret.Tests/PhraseGeneratorTests.cs`

- [ ] **Step 1: Write the failing test**

`PhraseGeneratorTests.cs`:
```csharp
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class PhraseGeneratorTests
{
    private static MkSecretOptions Opts(int words, string sep, bool cap = false, bool num = false) =>
        MkSecretOptions.Defaults with
        { Mode = SecretMode.Phrase, Words = words, Separator = sep, Capitalize = cap, Number = num };

    [Fact]
    public void Generate_selects_words_by_index_and_joins_with_separator()
    {
        // count=7776 -> 2 bytes each. {0,0}->index 0, {0,5}->index 5.
        var rng = new SequenceRandom(0, 0, 0, 5);
        var gen = new PhraseGenerator(rng);
        string expected = EffWordList.Words[0] + "-" + EffWordList.Words[5];
        Assert.Equal(expected, gen.Generate(Opts(2, "-")));
    }

    [Fact]
    public void Capitalize_uppercases_each_word_initial()
    {
        var rng = new SequenceRandom(0, 0, 0, 5);
        var gen = new PhraseGenerator(rng);
        string w0 = EffWordList.Words[0], w5 = EffWordList.Words[5];
        string expected = char.ToUpperInvariant(w0[0]) + w0.Substring(1) + " " +
                          char.ToUpperInvariant(w5[0]) + w5.Substring(1);
        Assert.Equal(expected, gen.Generate(Opts(2, " ", cap: true)));
    }

    [Fact]
    public void Number_appends_a_single_digit()
    {
        // two words (4 bytes) then one byte for the digit: 7 -> '7'.
        var rng = new SequenceRandom(0, 0, 0, 5, 7);
        var gen = new PhraseGenerator(rng);
        string result = gen.Generate(Opts(2, "-", num: true));
        Assert.EndsWith("7", result);
        Assert.Equal(EffWordList.Words[0] + "-" + EffWordList.Words[5] + "7", result);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter PhraseGeneratorTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

`PhraseGenerator.cs`:
```csharp
using System;
using System.Text;
using Winix.Codec;

namespace Winix.MkSecret;

/// <summary>Generates a diceware passphrase: <c>Words</c> words drawn unbiasedly from the EFF long
/// list, joined by <c>Separator</c>. Optional initial-capitalisation and a trailing random digit.</summary>
public sealed class PhraseGenerator : ISecretGenerator
{
    private const string Digits = "0123456789";
    private readonly ISecureRandom _random;

    /// <summary>Constructs a generator over an injectable CSPRNG.</summary>
    /// <exception cref="ArgumentNullException">If <paramref name="random"/> is null.</exception>
    public PhraseGenerator(ISecureRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    /// <inheritdoc/>
    public string Generate(MkSecretOptions options)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < options.Words; i++)
        {
            if (i > 0) { sb.Append(options.Separator); }
            string word = EffWordList.Words[Sampling.UniformIndex(_random, EffWordList.Words.Length)];
            if (options.Capitalize && word.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(word[0])).Append(word, 1, word.Length - 1);
            }
            else
            {
                sb.Append(word);
            }
        }
        if (options.Number)
        {
            sb.Append(Digits[Sampling.UniformIndex(_random, Digits.Length)]);
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter PhraseGeneratorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.MkSecret/PhraseGenerator.cs tests/Winix.MkSecret.Tests/PhraseGeneratorTests.cs
git commit -m "feat(mksecret): PhraseGenerator (diceware, separator/capitalize/number)"
```

---

## Task 8: KeyGenerator

**Files:**
- Create: `src/Winix.MkSecret/KeyGenerator.cs`
- Test: `tests/Winix.MkSecret.Tests/KeyGeneratorTests.cs`

- [ ] **Step 1: Write the failing test**

`KeyGeneratorTests.cs`:
```csharp
using Winix.Codec;
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class KeyGeneratorTests
{
    private static MkSecretOptions Opts(int bytes, KeyEncoding enc) =>
        MkSecretOptions.Defaults with { Mode = SecretMode.Key, Bytes = bytes, Encoding = enc };

    [Fact]
    public void Hex_encodes_the_drawn_bytes()
    {
        var rng = new SequenceRandom(0xDE, 0xAD, 0xBE, 0xEF);
        var gen = new KeyGenerator(rng);
        Assert.Equal("deadbeef", gen.Generate(Opts(4, KeyEncoding.Hex)));
    }

    [Fact]
    public void Base64Url_has_no_padding()
    {
        var rng = new SequenceRandom(new byte[32]); // 32 zero bytes
        var gen = new KeyGenerator(rng);
        string s = gen.Generate(Opts(32, KeyEncoding.Base64Url));
        Assert.DoesNotContain('=', s);
        Assert.DoesNotContain('+', s);
        Assert.DoesNotContain('/', s);
    }

    [Theory]
    [InlineData(KeyEncoding.Hex)]
    [InlineData(KeyEncoding.Base64)]
    [InlineData(KeyEncoding.Base64Url)]
    [InlineData(KeyEncoding.Base32)]
    public void Round_trips_back_to_the_original_bytes(KeyEncoding enc)
    {
        byte[] src = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var gen = new KeyGenerator(new SequenceRandom(src));
        string s = gen.Generate(Opts(src.Length, enc));
        byte[] back = enc switch
        {
            KeyEncoding.Hex => Hex.Decode(s),
            KeyEncoding.Base64 => Base64.Decode(s),
            KeyEncoding.Base64Url => Base64.Decode(s),
            KeyEncoding.Base32 => Base32Crockford.Decode(s),
            _ => throw new System.NotSupportedException(),
        };
        Assert.Equal(src, back);
    }
}
```

> Note: `Base32Crockford.Decode` returns `inputLen*5/8` bytes; for 16 input bytes the round-trip is exact. If a future `--bytes` value makes base32 length non-byte-aligned, the decode may append/trim — out of scope here (we only encode for output; we never decode in production).

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter KeyGeneratorTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

`KeyGenerator.cs`:
```csharp
using System;
using Winix.Codec;

namespace Winix.MkSecret;

/// <summary>Generates an encoded high-entropy key: draws <c>Bytes</c> CSPRNG bytes and renders
/// them in the requested <see cref="KeyEncoding"/>. base64url is emitted unpadded (ADR §7).</summary>
public sealed class KeyGenerator : ISecretGenerator
{
    private readonly ISecureRandom _random;

    /// <summary>Constructs a generator over an injectable CSPRNG.</summary>
    /// <exception cref="ArgumentNullException">If <paramref name="random"/> is null.</exception>
    public KeyGenerator(ISecureRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    /// <inheritdoc/>
    public string Generate(MkSecretOptions options)
    {
        byte[] bytes = new byte[options.Bytes];
        _random.Fill(bytes);
        return options.Encoding switch
        {
            KeyEncoding.Hex => Hex.Encode(bytes),
            KeyEncoding.Base64 => Base64.Encode(bytes),
            KeyEncoding.Base64Url => Base64.Encode(bytes, urlSafe: true).TrimEnd('='),
            KeyEncoding.Base32 => Base32Crockford.Encode(bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter KeyGeneratorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.MkSecret/KeyGenerator.cs tests/Winix.MkSecret.Tests/KeyGeneratorTests.cs
git commit -m "feat(mksecret): KeyGenerator (hex/base64/base64url-unpadded/base32)"
```

---

## Task 9: Generator factory + Entropy

**Files:**
- Create: `src/Winix.MkSecret/SecretGeneratorFactory.cs`, `Entropy.cs`
- Test: `tests/Winix.MkSecret.Tests/EntropyTests.cs`

- [ ] **Step 1: Create the factory**

`SecretGeneratorFactory.cs`:
```csharp
using System;
using Winix.Codec;

namespace Winix.MkSecret;

/// <summary>Creates the <see cref="ISecretGenerator"/> for a mode, over a given CSPRNG.</summary>
public static class SecretGeneratorFactory
{
    /// <summary>Returns the generator for <paramref name="mode"/>.</summary>
    public static ISecretGenerator Create(SecretMode mode, ISecureRandom random) => mode switch
    {
        SecretMode.Password => new PasswordGenerator(random),
        SecretMode.Phrase => new PhraseGenerator(random),
        SecretMode.Key => new KeyGenerator(random),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
```

- [ ] **Step 2: Write the failing entropy test**

`EntropyTests.cs`:
```csharp
using System;
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class EntropyTests
{
    [Fact]
    public void Password_bits_is_length_times_log2_charset()
    {
        var o = MkSecretOptions.Defaults with { Mode = SecretMode.Password, Length = 20, Charset = Charset.Alphanumeric };
        Assert.Equal(20 * Math.Log2(62), Entropy.BitsFor(o), 3);
    }

    [Fact]
    public void Key_bits_is_bytes_times_eight()
    {
        var o = MkSecretOptions.Defaults with { Mode = SecretMode.Key, Bytes = 32 };
        Assert.Equal(256.0, Entropy.BitsFor(o), 3);
    }

    [Fact]
    public void Phrase_bits_is_words_times_log2_wordcount_plus_digit()
    {
        var o = MkSecretOptions.Defaults with { Mode = SecretMode.Phrase, Words = 6, Number = false };
        Assert.Equal(6 * Math.Log2(7776), Entropy.BitsFor(o), 3);
        var withNum = o with { Number = true };
        Assert.Equal(6 * Math.Log2(7776) + Math.Log2(10), Entropy.BitsFor(withNum), 3);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter EntropyTests`
Expected: FAIL.

- [ ] **Step 4: Implement**

`Entropy.cs`:
```csharp
using System;

namespace Winix.MkSecret;

/// <summary>Computes the entropy (in bits) of a generated secret from its parameters. Reported to
/// the user as guidance; never affects generation.</summary>
public static class Entropy
{
    /// <summary>Returns the entropy in bits for the given options.</summary>
    public static double BitsFor(MkSecretOptions o) => o.Mode switch
    {
        SecretMode.Password => o.Length * Math.Log2(Charsets.ToChars(o.Charset).Length),
        SecretMode.Phrase => o.Words * Math.Log2(EffWordList.Words.Length) + (o.Number ? Math.Log2(10) : 0),
        SecretMode.Key => o.Bytes * 8.0,
        _ => 0,
    };
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter EntropyTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.MkSecret/SecretGeneratorFactory.cs src/Winix.MkSecret/Entropy.cs tests/Winix.MkSecret.Tests/EntropyTests.cs
git commit -m "feat(mksecret): generator factory + entropy calculation"
```

---

## Task 10: Formatting (entropy note + JSON envelope)

**Files:**
- Create: `src/Winix.MkSecret/Formatting.cs`
- Test: `tests/Winix.MkSecret.Tests/FormattingTests.cs`

- [ ] **Step 1: Write the failing test**

`FormattingTests.cs`:
```csharp
using System.Collections.Generic;
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class FormattingTests
{
    [Fact]
    public void EntropyNote_rounds_to_whole_bits()
    {
        Assert.Equal("mksecret: ≈ 119 bits", Formatting.EntropyNote(119.08));
    }

    [Fact]
    public void Json_envelope_has_mode_bits_and_values()
    {
        var o = MkSecretOptions.Defaults with { Mode = SecretMode.Password };
        string json = Formatting.JsonEnvelope(o, new List<string> { "abc", "def" }, 119.08);
        Assert.Contains("\"mode\":\"password\"", json);
        Assert.Contains("\"bits\":119.1", json);   // one decimal place
        Assert.Contains("\"values\":[\"abc\",\"def\"]", json);
    }

    [Fact]
    public void Json_escapes_special_characters_in_values()
    {
        var o = MkSecretOptions.Defaults with { Mode = SecretMode.Password };
        string json = Formatting.JsonEnvelope(o, new List<string> { "a\"b\\c" }, 1.0);
        Assert.Contains("\"a\\\"b\\\\c\"", json);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter FormattingTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

`Formatting.cs`:
```csharp
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Winix.MkSecret;

/// <summary>Renders user-facing output: the stderr entropy note and the stdout JSON envelope.
/// Plain output (the secret itself, one per line) is written directly by <see cref="Cli"/>.</summary>
public static class Formatting
{
    /// <summary>The stderr entropy note, e.g. <c>mksecret: ≈ 119 bits</c>. Whole bits.</summary>
    public static string EntropyNote(double bits)
        => $"mksecret: ≈ {(int)System.Math.Round(bits)} bits";

    /// <summary>JSON envelope: <c>{"mode":"password","bits":119.1,"values":[...]}</c>.</summary>
    public static string JsonEnvelope(MkSecretOptions options, IReadOnlyList<string> values, double bits)
    {
        var sb = new StringBuilder();
        sb.Append("{\"mode\":\"").Append(options.Mode.ToString().ToLowerInvariant()).Append("\",");
        sb.Append("\"bits\":").Append(System.Math.Round(bits, 1).ToString("0.0#", CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"values\":[");
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0) { sb.Append(','); }
            AppendJsonString(sb, values[i]);
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static void AppendJsonString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) { sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); }
                    else { sb.Append(c); }
                    break;
            }
        }
        sb.Append('"');
    }
}
```

> The `"bits":119.1` assertion expects `Math.Round(119.08,1)=119.1` formatted as `119.1`. Confirm the `"0.0#"` format yields `119.1` (not `119.10`); if the test sees `119.1` it passes.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter FormattingTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.MkSecret/Formatting.cs tests/Winix.MkSecret.Tests/FormattingTests.cs
git commit -m "feat(mksecret): entropy note + JSON envelope formatting"
```

---

## Task 11: ArgParser

**Files:**
- Create: `src/Winix.MkSecret/ArgParser.cs`
- Test: `tests/Winix.MkSecret.Tests/ArgParserTests.cs`

- [ ] **Step 1: Write the failing test**

`ArgParserTests.cs`:
```csharp
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class ArgParserTests
{
    [Fact]
    public void Bare_invocation_defaults_to_password_mode()
    {
        var r = ArgParser.Parse(new string[] { });
        Assert.True(r.Success);
        Assert.Equal(SecretMode.Password, r.Options!.Mode);
        Assert.Equal(20, r.Options.Length);
        Assert.Equal(Charset.Alphanumeric, r.Options.Charset);
    }

    [Fact]
    public void Phrase_subcommand_selects_phrase_mode_with_defaults()
    {
        var r = ArgParser.Parse(new[] { "phrase" });
        Assert.True(r.Success);
        Assert.Equal(SecretMode.Phrase, r.Options!.Mode);
        Assert.Equal(6, r.Options.Words);
        Assert.Equal("-", r.Options.Separator);
    }

    [Fact]
    public void Key_subcommand_defaults_to_32_bytes_base64url()
    {
        var r = ArgParser.Parse(new[] { "key" });
        Assert.True(r.Success);
        Assert.Equal(SecretMode.Key, r.Options!.Mode);
        Assert.Equal(32, r.Options.Bytes);
        Assert.Equal(KeyEncoding.Base64Url, r.Options.Encoding);
    }

    [Fact]
    public void Password_length_and_charset_flags_parse()
    {
        var r = ArgParser.Parse(new[] { "password", "--length", "12", "--charset", "full" });
        Assert.True(r.Success);
        Assert.Equal(12, r.Options!.Length);
        Assert.Equal(Charset.Full, r.Options.Charset);
    }

    [Fact]
    public void Unknown_charset_is_a_usage_error()
    {
        var r = ArgParser.Parse(new[] { "password", "--charset", "klingon" });
        Assert.False(r.Success);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Unknown_encoding_is_a_usage_error()
    {
        var r = ArgParser.Parse(new[] { "key", "--encoding", "rot13" });
        Assert.False(r.Success);
    }

    [Theory]
    [InlineData("password", "--length", "0")]
    [InlineData("key", "--bytes", "0")]
    [InlineData("phrase", "--words", "0")]
    [InlineData("password", "--count", "0")]
    public void Non_positive_sizes_are_usage_errors(params string[] args)
    {
        var r = ArgParser.Parse(args);
        Assert.False(r.Success);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter ArgParserTests`
Expected: FAIL.

- [ ] **Step 3: Implement** (mirrors `Winix.Qr.ArgParser` structure: subcommand dispatch, one parser per subcommand, `ResolveVersion`)

`ArgParser.cs`:
```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.MkSecret;

/// <summary>Parses argv into <see cref="MkSecretOptions"/>. Dispatches on the first positional
/// (<c>password</c>/<c>phrase</c>/<c>key</c>); bare invocation defaults to <c>password</c>. One
/// <see cref="CommandLineParser"/> per mode so <c>mksecret key --help</c> shows key-specific flags.</summary>
public static class ArgParser
{
    /// <summary>Parse outcome: <see cref="Options"/> on success, <see cref="Error"/> on usage error,
    /// or <see cref="IsHandled"/> when ShellKit already emitted help/version/describe.</summary>
    public sealed record Result(MkSecretOptions? Options, string? Error, bool IsHandled, int ExitCode, bool UseColor)
    {
        /// <summary>True when options parsed cleanly.</summary>
        public bool Success => Options is not null && Error is null && !IsHandled;
    }

    private static readonly Dictionary<string, SecretMode> Subcommands = new(StringComparer.Ordinal)
    {
        ["password"] = SecretMode.Password,
        ["phrase"] = SecretMode.Phrase,
        ["key"] = SecretMode.Key,
    };

    /// <summary>Parse argv (without the executable name).</summary>
    public static Result Parse(IReadOnlyList<string> argv)
    {
        SecretMode mode = SecretMode.Password;
        int start = 0;
        if (argv.Count > 0 && Subcommands.TryGetValue(argv[0], out SecretMode m))
        {
            mode = m;
            start = 1;
        }

        string[] slice = new string[argv.Count - start];
        for (int i = 0; i < slice.Length; i++) { slice[i] = argv[start + i]; }

        CommandLineParser parser = BuildParser(mode);
        ParseResult parsed = parser.Parse(slice);
        bool useColor = parsed.ResolveColor(checkStdErr: true);

        if (parsed.IsHandled) { return new Result(null, null, true, parsed.ExitCode, useColor); }
        if (parsed.HasErrors) { return Fail(parsed.Errors[0], useColor); }
        if (parsed.Positionals.Length > 0)
        {
            return Fail($"unexpected positional argument: {parsed.Positionals[0]}", useColor);
        }

        MkSecretOptions o = MkSecretOptions.Defaults with { Mode = mode };

        // Shared flags.
        if (parsed.Has("--count")) { o = o with { Count = parsed.GetInt("--count") }; }
        o = o with { Json = parsed.Has("--json"), Quiet = parsed.Has("--quiet") };

        switch (mode)
        {
            case SecretMode.Password:
                if (parsed.Has("--length")) { o = o with { Length = parsed.GetInt("--length") }; }
                if (parsed.Has("--charset"))
                {
                    if (!TryParseCharset(parsed.GetString("--charset"), out Charset cs))
                    {
                        return Fail($"unknown --charset value: {parsed.GetString("--charset")}", useColor);
                    }
                    o = o with { Charset = cs };
                }
                break;
            case SecretMode.Phrase:
                if (parsed.Has("--words")) { o = o with { Words = parsed.GetInt("--words") }; }
                if (parsed.Has("--sep")) { o = o with { Separator = parsed.GetString("--sep") }; }
                o = o with { Capitalize = parsed.Has("--capitalize"), Number = parsed.Has("--number") };
                break;
            case SecretMode.Key:
                if (parsed.Has("--bytes")) { o = o with { Bytes = parsed.GetInt("--bytes") }; }
                if (parsed.Has("--encoding"))
                {
                    if (!TryParseEncoding(parsed.GetString("--encoding"), out KeyEncoding enc))
                    {
                        return Fail($"unknown --encoding value: {parsed.GetString("--encoding")}", useColor);
                    }
                    o = o with { Encoding = enc };
                }
                break;
        }

        return new Result(o, null, false, 0, useColor);
    }

    private static CommandLineParser BuildParser(SecretMode mode)
    {
        string version = ResolveVersion();
        return mode switch
        {
            SecretMode.Phrase => BuildPhraseParser(version),
            SecretMode.Key => BuildKeyParser(version),
            _ => BuildPasswordParser(version),
        };
    }

    private static CommandLineParser CommonShell(string toolName, string version, string description)
        => new CommandLineParser(toolName, version)
            .Description(description)
            .StandardFlags()
            .Platform("cross-platform",
                replaces: new[] { "pwgen", "openssl rand", "diceware/xkcdpass", "PowerShell Get-SecureRandom" },
                valueOnWindows: "Windows ships no secure generator out of the box; the common Get-Random idiom is non-cryptographic. mksecret is secure-by-default with no PowerShell-version dependency.",
                valueOnUnix: "One self-contained binary covering passwords, diceware passphrases (missing on every OS), and encoded keys — secure-by-default (no pwgen -s footgun, no Python runtime for diceware).")
            .ExitCodes(
                (0, "Success"),
                (ExitCode.UsageError, "Usage error: unknown subcommand flag, bad --charset/--encoding value, non-positive --length/--bytes/--words/--count, unexpected positional"),
                (ExitCode.NotExecutable, "Runtime error: OS CSPRNG failure or output write failure"))
            .StdinDescription("Not used.")
            .StdoutDescription("The generated secret(s), one per line; or a JSON envelope under --json.")
            .StderrDescription("Entropy note (≈ N bits) unless --quiet/--json; errors.")
            .Option("--count", "-n", "N", "Number of secrets to emit, one per line. Default 1.")
            .Flag("--json", "Emit a JSON envelope to stdout instead of plain lines.")
            .Flag("--quiet", "Suppress the stderr entropy note.");

    private static CommandLineParser BuildPasswordParser(string version)
        => CommonShell("mksecret", version,
                "Generate a random secret. Default mode: a random-character password. Subcommands: phrase, key.")
            .IntOption("--length", "-l", "N", "Password length in characters. Default 20.",
                v => v > 0 ? null : "must be a positive integer")
            .Option("--charset", "-c", "NAME", "Character set: alphanumeric (default), full, alpha, digits, safe.")
            .Example("mksecret", "20-char alphanumeric password")
            .Example("mksecret --length 32 --charset full", "32 chars including symbols")
            .Example("mksecret --charset safe", "Avoids visually-ambiguous characters")
            .Example("mksecret --count 5", "Five passwords, one per line")
            .ComposesWith("clip", "mksecret | clip", "Copy a generated password to the clipboard without it touching the terminal")
            .Section("Subcommands",
                "mksecret password [--length N] [--charset NAME]   (default)\n" +
                "mksecret phrase   [--words N] [--sep S] [--capitalize] [--number]\n" +
                "mksecret key      [--bytes N] [--encoding hex|base64|base64url|base32]\n\n" +
                "Run 'mksecret SUBCOMMAND --help' for mode-specific flags.");

    private static CommandLineParser BuildPhraseParser(string version)
        => CommonShell("mksecret phrase", version,
                "Generate a diceware passphrase from the EFF long wordlist (7776 words, ~12.9 bits/word).")
            .IntOption("--words", "-w", "N", "Number of words. Default 6 (~77 bits).",
                v => v > 0 ? null : "must be a positive integer")
            .Option("--sep", "-s", "STR", "Separator between words. Default '-'.")
            .Flag("--capitalize", "Capitalise the first letter of each word.")
            .Flag("--number", "Append a random digit to the passphrase.")
            .Example("mksecret phrase", "Six-word passphrase, hyphen-separated")
            .Example("mksecret phrase --words 8 --sep ' '", "Eight words, space-separated")
            .Example("mksecret phrase --capitalize --number", "Title-cased with a trailing digit")
            .ComposesWith("clip", "mksecret phrase | clip", "Copy a generated passphrase to the clipboard");

    private static CommandLineParser BuildKeyParser(string version)
        => CommonShell("mksecret key", version,
                "Generate an encoded high-entropy key (API key, OAuth secret, HMAC key) from random bytes.")
            .IntOption("--bytes", "-b", "N", "Number of random bytes. Default 32 (256-bit).",
                v => v > 0 ? null : "must be a positive integer")
            .Option("--encoding", "-e", "NAME", "Encoding: base64url (default, unpadded), base64, hex, base32.")
            .Example("mksecret key", "32 random bytes as unpadded base64url")
            .Example("mksecret key --bytes 64 --encoding hex", "64 bytes as hex")
            .Example("mksecret key --encoding base32", "Crockford base32 (ambiguity-free)")
            .Section("Storing a key for reuse",
                "An HMAC/signing key must be PERSISTED to stay verifiable. Generate then store, e.g.:\n" +
                "  mksecret key --bytes 32 > signing.key\n" +
                "  digest --hmac sha256 --key-file signing.key \"payload\"\n" +
                "Do NOT pipe a generated key straight into digest --key-stdin — the key vanishes and the MAC is unverifiable.");

    private static bool TryParseCharset(string s, out Charset cs)
    {
        switch (s)
        {
            case "alphanumeric": cs = Charset.Alphanumeric; return true;
            case "full": cs = Charset.Full; return true;
            case "alpha": cs = Charset.Alpha; return true;
            case "digits": cs = Charset.Digits; return true;
            case "safe": cs = Charset.Safe; return true;
            default: cs = Charset.Alphanumeric; return false;
        }
    }

    private static bool TryParseEncoding(string s, out KeyEncoding enc)
    {
        switch (s)
        {
            case "hex": enc = KeyEncoding.Hex; return true;
            case "base64": enc = KeyEncoding.Base64; return true;
            case "base64url": enc = KeyEncoding.Base64Url; return true;
            case "base32": enc = KeyEncoding.Base32; return true;
            default: enc = KeyEncoding.Base64Url; return false;
        }
    }

    private static string ResolveVersion()
    {
        string? info = typeof(ArgParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            int plus = info.IndexOf('+');
            return plus >= 0 ? info.Substring(0, plus) : info;
        }
        return typeof(ArgParser).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static Result Fail(string msg, bool useColor) => new(null, msg, false, 0, useColor);
}
```

> Verify against the real ShellKit API while implementing: confirm `IntOption(long, short, metavar, help, validator)`, `Option(long, short, metavar, help)`, `Flag(long, help)`, `.Section(title, body)`, `ParseResult.GetInt/GetString/Has/Positionals/HasErrors/Errors/IsHandled/ExitCode/ResolveColor` all match the signatures used in `Winix.Qr.ArgParser`. If `--count`'s `IntOption` validator is the place to reject `< 1`, add `v => v > 0 ? null : "must be positive"` there so the `--count 0` test passes via the parser rather than a manual check.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter ArgParserTests`
Expected: PASS. If `--count 0` / `--length 0` slip through, add the `v > 0` validator to each `IntOption` (and for `--count`, register it as an `IntOption` with that validator instead of a bare `Option`).

- [ ] **Step 5: Commit**

```bash
git add src/Winix.MkSecret/ArgParser.cs tests/Winix.MkSecret.Tests/ArgParserTests.cs
git commit -m "feat(mksecret): ShellKit arg parser with subcommand dispatch + validation"
```

---

## Task 12: Cli.Run orchestration seam

**Files:**
- Create: `src/Winix.MkSecret/Cli.cs`
- Test: `tests/Winix.MkSecret.Tests/CliTests.cs`

- [ ] **Step 1: Write the failing test**

`CliTests.cs`:
```csharp
using System.IO;
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class CliTests
{
    private static (int code, string outText, string errText) Run(string[] args, Winix.Codec.ISecureRandom? rng = null)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int code = Cli.Run(args, so, se, rng);
        return (code, so.ToString(), se.ToString());
    }

    [Fact]
    public void Password_writes_secret_to_stdout_and_entropy_to_stderr()
    {
        var rng = new SequenceRandom(new byte[64]); // index 0 each time
        var (code, outText, errText) = Run(new[] { "password", "--length", "8" }, rng);
        Assert.Equal(0, code);
        Assert.Equal("AAAAAAAA", outText.Trim());      // alphanumeric[0]='A'
        Assert.Contains("bits", errText);
    }

    [Fact]
    public void Quiet_suppresses_entropy_note()
    {
        var rng = new SequenceRandom(new byte[64]);
        var (_, _, errText) = Run(new[] { "password", "--length", "4", "--quiet" }, rng);
        Assert.Equal("", errText.Trim());
    }

    [Fact]
    public void Json_emits_envelope_to_stdout_and_nothing_to_stderr()
    {
        var rng = new SequenceRandom(new byte[64]);
        var (code, outText, errText) = Run(new[] { "password", "--length", "4", "--json" }, rng);
        Assert.Equal(0, code);
        Assert.Contains("\"mode\":\"password\"", outText);
        Assert.Equal("", errText.Trim());
    }

    [Fact]
    public void Count_emits_multiple_lines()
    {
        var rng = new SequenceRandom(new byte[64]);
        var (_, outText, _) = Run(new[] { "password", "--length", "2", "--count", "3" }, rng);
        Assert.Equal(3, outText.Trim().Split('\n').Length);
    }

    [Fact]
    public void Usage_error_returns_usage_exit_code()
    {
        var (code, _, errText) = Run(new[] { "password", "--charset", "nope" });
        Assert.Equal(ExitCode.UsageError, code);
        Assert.Contains("mksecret:", errText);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter CliTests`
Expected: FAIL.

- [ ] **Step 3: Implement** (mirrors `Winix.Ids.Cli.Run`)

`Cli.cs`:
```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Winix.Codec;
using Yort.ShellKit;

namespace Winix.MkSecret;

/// <summary>Library entry point. <c>Program.cs</c> is a thin shim around <see cref="Run"/> so the
/// JSON shape, pipe-close handling, and error path are unit-testable. The <paramref name="randomOverride"/>
/// seam lets tests inject a deterministic CSPRNG; production passes null and the real
/// <see cref="SecureRandom"/> is used.</summary>
public static class Cli
{
    /// <summary>Runs the pipeline: parse, generate, format, return exit code.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, ISecureRandom? randomOverride = null)
    {
        ArgParser.Result r = ArgParser.Parse(args);

        if (r.IsHandled) { return r.ExitCode; }
        if (!r.Success)
        {
            stderr.WriteLine($"mksecret: {r.Error}");
            stderr.WriteLine("Run 'mksecret --help' for usage.");
            return ExitCode.UsageError;
        }

        MkSecretOptions o = r.Options!;

        try
        {
            ISecureRandom rng = randomOverride ?? new SecureRandom();
            ISecretGenerator gen = SecretGeneratorFactory.Create(o.Mode, rng);

            var values = new List<string>(o.Count);
            for (int i = 0; i < o.Count; i++) { values.Add(gen.Generate(o)); }

            if (o.Json)
            {
                stdout.WriteLine(Formatting.JsonEnvelope(o, values, Entropy.BitsFor(o)));
            }
            else
            {
                foreach (string v in values) { stdout.WriteLine(v); }
                if (!o.Quiet)
                {
                    stderr.WriteLine(Formatting.EntropyNote(Entropy.BitsFor(o)));
                }
            }
            return ExitCode.Success;
        }
        catch (IOException)
        {
            // Downstream reader closed the pipe (e.g. `mksecret --count 100000 | head -1`). Not our error.
            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            // Unexpected (OS CSPRNG failure, OOM). Short message — AOT has StackTraceSupport=false.
            stderr.WriteLine($"mksecret: error: {ex.Message}");
            return ExitCode.NotExecutable;
        }
    }
}
```

> Confirm `SecureRandom` has a public parameterless constructor (it's the default `ISecureRandom`). If it's exposed differently (e.g. a static `SecureRandom.Shared`), use that instead — check `src/Winix.Codec/SecureRandom.cs`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter CliTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.MkSecret/Cli.cs tests/Winix.MkSecret.Tests/CliTests.cs
git commit -m "feat(mksecret): Cli.Run orchestration seam (json/quiet/count/errors)"
```

---

## Task 13: Real-CSPRNG liveness / no-stub guard (ADR §8)

**Files:**
- Test: `tests/Winix.MkSecret.Tests/RealRandomLivenessTests.cs`

- [ ] **Step 1: Write the test that exercises the production CSPRNG**

`RealRandomLivenessTests.cs`:
```csharp
using System.Collections.Generic;
using System.IO;
using Winix.Codec;
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class RealRandomLivenessTests
{
    private static string RunReal(string[] args)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        Cli.Run(args, so, se, randomOverride: null); // null => production SecureRandom
        return so.ToString();
    }

    [Fact]
    public void Real_generator_produces_distinct_values_not_a_constant()
    {
        // 50 real 32-byte keys must all differ. A constant/seeded/stubbed production RNG fails here.
        string outText = RunReal(new[] { "key", "--bytes", "32", "--count", "50" });
        string[] lines = outText.Trim().Split('\n');
        Assert.Equal(50, lines.Length);
        Assert.Equal(50, new HashSet<string>(lines).Count);
    }

    [Fact]
    public void Real_password_sample_covers_the_full_charset()
    {
        // Generate enough characters that every alphanumeric member should appear (coupon-collector
        // expectation ~256 chars; 2000 makes a miss astronomically unlikely for a real CSPRNG).
        string outText = RunReal(new[] { "password", "--length", "2000", "--quiet" });
        string sample = outText.Trim();
        foreach (char c in Charsets.ToChars(Charset.Alphanumeric))
        {
            Assert.Contains(c, sample);
        }
    }

    [Fact]
    public void Production_factory_default_random_is_SecureRandom()
    {
        // Guard: nobody has swapped a stub in as the default ISecureRandom.
        Assert.IsType<SecureRandom>(new SecureRandom());
    }
}
```

> The third test is a low-value placeholder if `SecureRandom` is the only `ISecureRandom` the factory can use. If `Cli.Run` constructs `new SecureRandom()` directly (as written in Task 12), the real guard is tests 1–2 exercising the real path. Keep test 3 only if it compiles cleanly; otherwise delete it and rely on 1–2.

- [ ] **Step 2: Run to verify it passes**

Run: `dotnet test tests/Winix.MkSecret.Tests --filter RealRandomLivenessTests`
Expected: PASS (non-flaky — collision/miss probabilities are astronomically small).

- [ ] **Step 3: Commit**

```bash
git add tests/Winix.MkSecret.Tests/RealRandomLivenessTests.cs
git commit -m "test(mksecret): real-CSPRNG liveness/no-stub guard (ADR section 8)"
```

---

## Task 14: Console shim + full test run + AOT smoke

**Files:**
- Create/replace: `src/mksecret/Program.cs`

- [ ] **Step 1: Write `Program.cs`** (thin shim — mirrors `src/ids/Program.cs`)

```csharp
using System;
using Winix.MkSecret;
using Yort.ShellKit;

namespace MkSecret;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        return Cli.Run(args, Console.Out, Console.Error);
    }
}
```

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test tests/Winix.MkSecret.Tests`
Expected: all green.

- [ ] **Step 3: Run the JIT binary manually** (per `feedback_cli_auto_defaults` — new tool needs first-pass CLI validation)

```bash
dotnet run --project src/mksecret -- --length 24
dotnet run --project src/mksecret -- phrase --words 5
dotnet run --project src/mksecret -- key --bytes 16 --encoding hex
dotnet run --project src/mksecret -- key --json
dotnet run --project src/mksecret -- --help
dotnet run --project src/mksecret -- key --help
dotnet run --project src/mksecret -- --describe
dotnet run --project src/mksecret -- --version    # expect clean X.Y.Z (no +sha)
```
Confirm: secret on stdout, entropy on stderr, `--json` clean stdout, base64url has no `=`, help shows subcommands, describe is valid JSON, `mksecret key | head` doesn't error.

- [ ] **Step 4: AOT publish smoke** (the real distribution mode)

```bash
dotnet publish src/mksecret/mksecret.csproj -c Release -r win-x64
```
Expected: publishes with **no trim/AOT warnings**; binary ~1–1.5 MB. Run the published exe through the same checks as Step 3.

- [ ] **Step 5: Commit**

```bash
git add src/mksecret/Program.cs
git commit -m "feat(mksecret): console shim; full suite + AOT smoke verified"
```

---

## Task 15: Documentation

**Files:**
- Replace: `src/mksecret/README.md`, `src/mksecret/man/man1/mksecret.1`, `src/mksecret/CHANGELOG.md`
- Create: `docs/ai/mksecret.md`
- Modify: `llms.txt`

- [ ] **Step 1: Write `src/mksecret/README.md`** following the existing pattern (use `src/ids/README.md` as the template): description, install (scoop/winget/nuget/dotnet-tool/GitHub), usage per subcommand, options tables, exit codes, the `| clip` copy pattern **with the clipboard-security caveats**, and the generate→store→`digest --key-file` note for keys. Do NOT include a `mksecret key | digest --key-stdin` example.

- [ ] **Step 2: Write the man page** `src/mksecret/man/man1/mksecret.1` (groff) following `src/ids/man/man1/ids.1`: NAME, SYNOPSIS (three subcommands), DESCRIPTION, per-mode OPTIONS, EXIT STATUS, EXAMPLES, the clipboard caveat, SEE ALSO (digest(1), clip(1)).

- [ ] **Step 3: Write `docs/ai/mksecret.md`** following `docs/ai/ids.md`: when an agent should choose mksecret, the three modes, secure-by-default, and the composition rules (`| clip` good; key-into-digest-via-pipe bad — must persist).

- [ ] **Step 4: Add the `llms.txt` entry** for mksecret mirroring the format of the adjacent tool entries.

- [ ] **Step 5: Write `src/mksecret/CHANGELOG.md`** (Keep-a-Changelog) with a single `- Initial release.` under the first version heading (per the CLAUDE.md rule: CHANGELOG only at first stable tag).

- [ ] **Step 6: Verify the man page packages into publish output**

```bash
dotnet publish src/mksecret/mksecret.csproj -c Release -p:PublishAot=false -o tmp/mksecret-pubcheck
```
Confirm `tmp/mksecret-pubcheck/share/man/man1/mksecret.1` exists, then `rm -rf tmp/mksecret-pubcheck`.

- [ ] **Step 7: Verify the `--describe` composes-with snippets actually run** (per `feedback_composes_with_snippets_must_be_verified`)

```bash
dotnet run --project src/mksecret -- --length 12 | dotnet run --project src/clip
```
Confirm the `mksecret | clip` snippet round-trips. (The `digest` relationship is intentionally not a pipe — nothing to execute there.)

- [ ] **Step 8: Commit**

```bash
git add src/mksecret/README.md src/mksecret/man/man1/mksecret.1 src/mksecret/CHANGELOG.md docs/ai/mksecret.md llms.txt
git commit -m "docs(mksecret): README, man page, AI guide, llms.txt, CHANGELOG"
```

---

## Task 16: Release & distribution wiring

**Files:**
- Create: `bucket/mksecret.json`
- Modify: `.github/workflows/release.yml`, `.github/workflows/post-publish.yml`, `CLAUDE.md`

- [ ] **Step 1: Create the scoop manifest** `bucket/mksecret.json` by copying `bucket/ids.json` and changing the tool name, binary, description, and (the release pipeline fills hash/url, but match the structural shape exactly).

- [ ] **Step 2: Add mksecret to `.github/workflows/release.yml`**

Add `mksecret` to: the per-`matrix.rid` `dotnet publish` step, the `dotnet pack` step, the per-tool zip arrays in **both** the Linux/macOS and Windows zip loops (the symbol-splitting loops added on this branch — append `mksecret` to the `TOOLS=` string and the `$tools = @(...)` array), and the `tools: { … }` map. Use the exact tool-list ordering already present.

- [ ] **Step 3: Add mksecret to `.github/workflows/post-publish.yml`**

Add `update_manifest bucket/mksecret.json …` and `generate_manifests "mksecret" "MkSecret" "<one-line description>" "password,passphrase,diceware,secret,token"` (4th arg = winget domain tags; baseline `cli,developer-tools,portable,winix` is added automatically).

- [ ] **Step 4: Update `CLAUDE.md`**

Add to: the NuGet package IDs list (`Winix.MkSecret`), the scoop manifests list (`mksecret.json`), and the project-layout block (`src/Winix.MkSecret/`, `src/mksecret/`, `tests/Winix.MkSecret.Tests/`).

- [ ] **Step 5: Validate the workflow YAML parses**

Run: `dotnet build Winix.sln` (sanity) and a YAML lint if available; otherwise visually diff the new lines against an existing tool's lines for structural parity.

- [ ] **Step 6: Commit**

```bash
git add bucket/mksecret.json .github/workflows/release.yml .github/workflows/post-publish.yml CLAUDE.md
git commit -m "build(mksecret): scoop manifest, release + post-publish wiring, CLAUDE.md"
```

---

## Self-Review

**Spec coverage:**
- Three modes (password/phrase/key) → Tasks 5, 7, 8. ✓
- Subcommand dispatch + bare default → Task 11. ✓
- Charsets incl. `safe` → Task 2. ✓
- EFF long wordlist, embedded array, integrity → Task 6. ✓
- Key encodings, base64url unpadded → Task 8. ✓
- Pure random (no forced composition) → PasswordGenerator (Task 5), no composition logic present. ✓
- Unbiased selection → Sampling + rejection test (Task 3). ✓
- Entropy note + JSON envelope, stdout/stderr routing, --quiet → Tasks 10, 12. ✓
- No --copy; document `| clip` + caveats → Tasks 14, 15. ✓
- Cli.Run seam, IOException, catch-all, AOT no-stack-trace → Task 12. ✓
- Real-CSPRNG liveness/no-stub guard + trust boundary → Task 13 (ADR §8). ✓
- `--describe` composes-with (only sound ones), verified → Tasks 11, 15 step 7. ✓
- Full new-tool checklist (scoop, release.yml, post-publish.yml, README, man, docs/ai, llms.txt, CLAUDE.md, tags, CHANGELOG) → Tasks 15, 16. ✓
- InvariantGlobalization on test csproj → Task 1. ✓

**Placeholder scan:** Task 6 (wordlist) is a generate-from-source step, not a placeholder — the mechanism is fully specified and the integrity test is concrete. Task 13 test 3 and several "verify against the real ShellKit/Codec API" notes are explicit verification instructions, not deferred code. Docs tasks (14–16) reference sibling templates rather than inlining a 200-line README/groff page — acceptable since the pattern is established and copying-then-adapting is the documented suite convention.

**Type consistency:** `MkSecretOptions` fields, `Charsets.ToChars`, `Sampling.UniformIndex(rng,count)`, `ISecretGenerator.Generate(options)`, `Entropy.BitsFor`, `Formatting.EntropyNote/JsonEnvelope`, `ArgParser.Result.Success`, `Cli.Run(args,out,err,override?)` are used identically across tasks. ✓

**Known verification points to confirm during execution (not gaps, but pin them):** exact ShellKit method signatures (`IntOption` validator arg, `.Section`, `ResolveColor`), `SecureRandom` construction shape, the `"0.0#"` bits formatting yielding `119.1`, and the EFF file URL/availability.
