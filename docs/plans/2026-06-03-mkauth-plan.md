# mkauth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `mkauth`, a cross-platform CLI that computes an HTTP `Authorization` header and prints it to stdout, with per-scheme subcommands `basic` / `bearer` / `oauth1` / `jwt` / `azure-storage`.

**Architecture:** Class library `Winix.MkAuth` (all logic, testable) + thin console app `src/mkauth`. One builder/signer per scheme, each pure given (inputs, injected `IClock`, injected `INonceSource`). Secrets resolve through a shared `SecretResolver` over a `SecretRef` (`env:`/`file:`/`vault:`/`stdin`/`literal:`), with `vault:` backed by `Winix.SecretStore.ISecretStore`. Arg parsing via `Yort.ShellKit.CommandLineParser` with `positional[0]` subcommand dispatch. `Cli.Run(args, stdout, stderr, stdin, deps?)` seam mirrors `Winix.MkSecret.Cli`.

**Tech Stack:** .NET 10, NativeAOT, xUnit. Reuses `Winix.Codec` (`Base64`, `Hex`), `Winix.SecretStore` (`ISecretStore`), `Yort.ShellKit` (`CommandLineParser`, `SafeError`, `ExitCode`, `DurationParser`). Crypto via `System.Security.Cryptography` (`HMACSHA1/256/384/512`, `RSA`, `ECDsa`). No new external packages.

**Spec:** [design](2026-06-03-mkauth-design.md) · [ADR](2026-06-03-mkauth-adr.md)

---

## File Structure

```
src/Winix.MkAuth/
  AuthScheme.cs            enum Basic|Bearer|OAuth1|Jwt|AzureStorage
  IClock.cs                UtcNow seam            (SystemClock : IClock)
  INonceSource.cs          NextNonce() seam       (RandomNonceSource : INonceSource)
  MkAuthDeps.cs            bundle: IClock, INonceSource, ISecretStore
  SecretRef.cs             parse "env:/file:/vault:/stdin/-/literal:" -> kind+value
  SecretResolver.cs        resolve a SecretRef -> string (vault via ISecretStore); literal warns
  PercentEncoder.cs        RFC 3986 percent-encoding (manual, unreserved set only)
  Base64Url.cs             EncodeNoPad(bytes) -> base64url without '='
  HeaderResult.cs          record(HeaderName, HeaderValue, BaseString?)
  BasicAuthBuilder.cs      (user, password) -> HeaderResult
  BearerAuthBuilder.cs     (token) -> HeaderResult
  OAuth1Signer.cs          base string, signing key, HMAC/PLAINTEXT, oauth_* header
  JwtSigner.cs             header+payload build, HS/RS/ES sign, compact JWS
  AzureStorageSigner.cs    SharedKey StringToSign (Blob/Queue/File) -> header
  Formatting.cs            plain line / --value-only / --json envelope
  ArgParser.cs             ShellKit parser; subcommand dispatch; per-scheme option binding
  Cli.cs                   Run(args, stdout, stderr, stdin, deps?)

src/mkauth/
  Program.cs               thin shim -> Cli.Run
  mkauth.csproj            AOT, PackAsTool, PackageId=Winix.MkAuth
  README.md
  man/man1/mkauth.1
  CHANGELOG.md

tests/Winix.MkAuth.Tests/
  Winix.MkAuth.Tests.csproj
  FixedClock.cs            IClock returning a pinned instant
  FixedNonce.cs            INonceSource returning a pinned nonce
  InMemorySecretStore.cs   ISecretStore test double
  SecretRefTests.cs
  SecretResolverTests.cs
  PercentEncoderTests.cs
  Base64UrlTests.cs
  BasicAuthBuilderTests.cs
  BearerAuthBuilderTests.cs
  OAuth1SignerTests.cs
  JwtSignerTests.cs
  AzureStorageSignerTests.cs
  FormattingTests.cs
  ArgParserTests.cs
  CliTests.cs
```

> **Note on `Winix.SecretStore.ISecretStore`:** before Task 2, the implementer MUST open
> `src/Winix.SecretStore/` and confirm the exact interface name and the get-method signature
> (name, parameters, return type, and how "namespace/key" maps to its API). The plan assumes a
> method shaped like `bool TryGet(string @namespace, string key, out string value)` but the real
> signature governs — adapt `SecretResolver` and `InMemorySecretStore` to whatever exists. This is
> the "verify the shared library API before depending on it" step.

---

## Task 1: Project scaffold

**Files:**
- Create: `src/Winix.MkAuth/Winix.MkAuth.csproj`
- Create: `src/mkauth/mkauth.csproj`
- Create: `tests/Winix.MkAuth.Tests/Winix.MkAuth.Tests.csproj`

- [ ] **Step 1: Create the class-library csproj**

`src/Winix.MkAuth/Winix.MkAuth.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.Codec\Winix.Codec.csproj" />
    <ProjectReference Include="..\Winix.SecretStore\Winix.SecretStore.csproj" />
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
  </ItemGroup>
</Project>
```
(Nullable, ImplicitUsings, warnings-as-errors inherited from `Directory.Build.props`.)

- [ ] **Step 2: Create the console-app csproj**

`src/mkauth/mkauth.csproj` — copy `src/mksecret/mksecret.csproj` verbatim, then change: `<PackageId>` to `Winix.MkAuth`, `<Description>` to `"Compute HTTP Authorization headers (OAuth 1.0a, JWT, Basic, Bearer, Azure Storage) for curl and scripts."`, `<PackageTags>` to the shared baseline (`cli;command-line;cross-platform;windows;macos;linux;aot;dotnet-tool;winix`) plus `oauth;oauth1;jwt;http;authorization;curl`, the `<Content Include="man\man1\mkauth.1" ... />` line, and the `ProjectReference` to `..\Winix.MkAuth\Winix.MkAuth.csproj`. Keep `UseSystemResourceKeys`, `PublishAot`, `PackAsTool`, `StackTraceSupport=false` exactly as mksecret has them.

- [ ] **Step 3: Create the test csproj**

`tests/Winix.MkAuth.Tests/Winix.MkAuth.Tests.csproj` — copy `tests/Winix.MkSecret.Tests/Winix.MkSecret.Tests.csproj` verbatim, change the `ProjectReference` to `..\..\src\Winix.MkAuth\Winix.MkAuth.csproj`. **Keep `<UseSystemResourceKeys>true</UseSystemResourceKeys>`** (mirror the app — required to reproduce SR-key leaks, per the suite convention).

- [ ] **Step 4: Add all three projects to the solution**

Run: `dotnet sln Winix.sln add src/Winix.MkAuth/Winix.MkAuth.csproj src/mkauth/mkauth.csproj tests/Winix.MkAuth.Tests/Winix.MkAuth.Tests.csproj`
Expected: "Project added to the solution" ×3.

- [ ] **Step 5: Verify it builds empty**

Run: `dotnet build Winix.sln`
Expected: build succeeds (no source files yet beyond csprojs).

- [ ] **Step 6: Commit**

```bash
git add src/Winix.MkAuth src/mkauth tests/Winix.MkAuth.Tests Winix.sln
git commit -m "feat(mkauth): project scaffold (lib + app + tests)"
```

---

## Task 2: Secret references + resolver

**Files:**
- Create: `src/Winix.MkAuth/SecretRef.cs`, `src/Winix.MkAuth/SecretResolver.cs`
- Create: `tests/Winix.MkAuth.Tests/SecretRefTests.cs`, `tests/Winix.MkAuth.Tests/SecretResolverTests.cs`, `tests/Winix.MkAuth.Tests/InMemorySecretStore.cs`

- [ ] **Step 1: Write failing tests for `SecretRef.Parse`**

`SecretRefTests.cs`:
```csharp
using Winix.MkAuth;
using Xunit;

public class SecretRefTests
{
    [Theory]
    [InlineData("env:TOK", SecretRefKind.Env, "TOK")]
    [InlineData("file:/etc/secret", SecretRefKind.File, "/etc/secret")]
    [InlineData("vault:api/consumer", SecretRefKind.Vault, "api/consumer")]
    [InlineData("literal:hunter2", SecretRefKind.Literal, "hunter2")]
    [InlineData("stdin", SecretRefKind.Stdin, "")]
    [InlineData("-", SecretRefKind.Stdin, "")]
    public void Parse_recognises_each_scheme(string input, SecretRefKind kind, string value)
    {
        var r = SecretRef.Parse(input);
        Assert.Equal(kind, r.Kind);
        Assert.Equal(value, r.Value);
    }

    [Fact]
    public void Parse_unknown_scheme_throws_FormatException()
    {
        Assert.Throws<FormatException>(() => SecretRef.Parse("bogus:x"));
    }

    [Fact]
    public void Parse_bare_value_without_scheme_throws()
    {
        // No implicit literal — a bare value is ambiguous and rejected.
        Assert.Throws<FormatException>(() => SecretRef.Parse("justavalue"));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter SecretRefTests`
Expected: FAIL (types not defined).

- [ ] **Step 3: Implement `SecretRef`**

`src/Winix.MkAuth/SecretRef.cs`:
```csharp
namespace Winix.MkAuth;

/// <summary>The kind of source a <see cref="SecretRef"/> points at.</summary>
public enum SecretRefKind { Env, File, Vault, Stdin, Literal }

/// <summary>
/// A parsed secret reference. Secrets are never required on argv; a reference names where the
/// value comes from. Syntax: <c>env:NAME</c>, <c>file:PATH</c>, <c>vault:NS/KEY</c>,
/// <c>literal:VALUE</c>, or <c>stdin</c>/<c>-</c>. A bare value (no scheme) is rejected as
/// ambiguous.
/// </summary>
public readonly record struct SecretRef(SecretRefKind Kind, string Value)
{
    /// <summary>Parses a reference string. Throws <see cref="FormatException"/> on an unknown or
    /// missing scheme.</summary>
    public static SecretRef Parse(string input)
    {
        if (input is "stdin" or "-")
        {
            return new SecretRef(SecretRefKind.Stdin, "");
        }

        int colon = input.IndexOf(':');
        if (colon <= 0)
        {
            throw new FormatException(
                $"Secret reference '{input}' has no scheme. Use env:NAME, file:PATH, vault:NS/KEY, literal:VALUE, or stdin.");
        }

        string scheme = input.Substring(0, colon);
        string value = input.Substring(colon + 1);
        return scheme switch
        {
            "env" => new SecretRef(SecretRefKind.Env, value),
            "file" => new SecretRef(SecretRefKind.File, value),
            "vault" => new SecretRef(SecretRefKind.Vault, value),
            "literal" => new SecretRef(SecretRefKind.Literal, value),
            _ => throw new FormatException(
                $"Unknown secret-reference scheme '{scheme}'. Use env, file, vault, literal, or stdin."),
        };
    }
}
```

- [ ] **Step 4: Run to verify SecretRef tests pass**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter SecretRefTests`
Expected: PASS.

- [ ] **Step 5: Add the `ISecretStore` test double**

`InMemorySecretStore.cs` — **adapt to the real `ISecretStore` interface confirmed in the File-Structure note.** Shape assuming `TryGet(namespace, key, out value)`:
```csharp
using Winix.SecretStore;

public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<(string, string), string> _values = new();
    public void Put(string ns, string key, string value) => _values[(ns, key)] = value;

    public bool TryGet(string @namespace, string key, out string value)
        => _values.TryGetValue((@namespace, key), out value!);

    // Implement any other ISecretStore members as throwing NotSupportedException — only TryGet is used here.
}
```

- [ ] **Step 6: Write failing tests for `SecretResolver`**

`SecretResolverTests.cs`:
```csharp
using Winix.MkAuth;
using Xunit;

public class SecretResolverTests
{
    [Fact]
    public void Resolves_env()
    {
        Environment.SetEnvironmentVariable("MKAUTH_TEST_SECRET", "s3cret");
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader(""));
        var warnings = new List<string>();
        Assert.Equal("s3cret", sut.Resolve(SecretRef.Parse("env:MKAUTH_TEST_SECRET"), warnings.Add));
        Assert.Empty(warnings);
    }

    [Fact]
    public void Resolves_vault_via_store()
    {
        var store = new InMemorySecretStore();
        store.Put("api", "consumer", "vaulted");
        var sut = new SecretResolver(store, stdin: new StringReader(""));
        Assert.Equal("vaulted", sut.Resolve(SecretRef.Parse("vault:api/consumer"), _ => { }));
    }

    [Fact]
    public void Literal_resolves_and_warns()
    {
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader(""));
        var warnings = new List<string>();
        Assert.Equal("plain", sut.Resolve(SecretRef.Parse("literal:plain"), warnings.Add));
        Assert.Single(warnings); // ps/history exposure warning
    }

    [Fact]
    public void Stdin_is_single_use()
    {
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader("piped\n"));
        Assert.Equal("piped", sut.Resolve(SecretRef.Parse("stdin"), _ => { }));
        Assert.Throws<InvalidOperationException>(() => sut.Resolve(SecretRef.Parse("stdin"), _ => { }));
    }

    [Fact]
    public void Vault_miss_throws()
    {
        var sut = new SecretResolver(new InMemorySecretStore(), stdin: new StringReader(""));
        Assert.Throws<InvalidOperationException>(() => sut.Resolve(SecretRef.Parse("vault:no/such"), _ => { }));
    }
}
```

- [ ] **Step 7: Implement `SecretResolver`**

`src/Winix.MkAuth/SecretResolver.cs`:
```csharp
using Winix.SecretStore;

namespace Winix.MkAuth;

/// <summary>
/// Resolves a <see cref="SecretRef"/> to its secret value. <c>stdin</c> may be used at most once
/// per resolver instance. <c>literal:</c> emits a warning (the value is visible in argv / shell
/// history). <c>vault:</c> reads from the OS keychain via <see cref="ISecretStore"/>.
/// </summary>
public sealed class SecretResolver
{
    private readonly ISecretStore _store;
    private readonly TextReader _stdin;
    private bool _stdinConsumed;

    public SecretResolver(ISecretStore store, TextReader stdin)
    {
        _store = store;
        _stdin = stdin;
    }

    /// <param name="warn">Invoked with a human-readable warning for exposure-prone sources.</param>
    public string Resolve(SecretRef reference, Action<string> warn)
    {
        switch (reference.Kind)
        {
            case SecretRefKind.Env:
                return Environment.GetEnvironmentVariable(reference.Value)
                    ?? throw new InvalidOperationException($"Environment variable '{reference.Value}' is not set.");

            case SecretRefKind.File:
                return File.ReadAllText(reference.Value).TrimEnd('\r', '\n');

            case SecretRefKind.Vault:
                int slash = reference.Value.IndexOf('/');
                if (slash <= 0)
                {
                    throw new FormatException($"vault reference '{reference.Value}' must be NS/KEY.");
                }
                string ns = reference.Value.Substring(0, slash);
                string key = reference.Value.Substring(slash + 1);
                if (!_store.TryGet(ns, key, out string value))
                {
                    throw new InvalidOperationException($"No vault entry '{ns}/{key}'.");
                }
                return value;

            case SecretRefKind.Stdin:
                if (_stdinConsumed)
                {
                    throw new InvalidOperationException("stdin can supply only one secret per invocation.");
                }
                _stdinConsumed = true;
                return _stdin.ReadToEnd().TrimEnd('\r', '\n');

            case SecretRefKind.Literal:
                warn("a literal secret is visible in argv / shell history; prefer env:, file:, vault:, or stdin.");
                return reference.Value;

            default:
                throw new InvalidOperationException($"Unhandled secret-ref kind {reference.Kind}.");
        }
    }
}
```

- [ ] **Step 8: Run all Task-2 tests**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter "SecretRefTests|SecretResolverTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Winix.MkAuth/SecretRef.cs src/Winix.MkAuth/SecretResolver.cs tests/Winix.MkAuth.Tests
git commit -m "feat(mkauth): secret references (env/file/vault/stdin/literal) + resolver"
```

---

## Task 3: PercentEncoder (RFC 3986) + Base64Url

**Files:**
- Create: `src/Winix.MkAuth/PercentEncoder.cs`, `src/Winix.MkAuth/Base64Url.cs`
- Create: `tests/Winix.MkAuth.Tests/PercentEncoderTests.cs`, `tests/Winix.MkAuth.Tests/Base64UrlTests.cs`

- [ ] **Step 1: Write failing PercentEncoder tests (independent of OAuth1 — pins the encoding directly)**

`PercentEncoderTests.cs`:
```csharp
using Winix.MkAuth;
using Xunit;

public class PercentEncoderTests
{
    [Theory]
    [InlineData("abcDEF123", "abcDEF123")]   // unreserved alnum untouched
    [InlineData("-._~", "-._~")]              // RFC 3986 unreserved symbols untouched
    [InlineData(" ", "%20")]                  // space is %20, NOT '+'
    [InlineData("!*'()", "%21%2A%27%28%29")] // sub-delims that OAuth1 requires encoded
    [InlineData("a+b=c&d", "a%2Bb%3Dc%26d")]
    [InlineData("å/ä", "%C3%A5%2F%C3%A4")]   // UTF-8 bytes, uppercase hex
    public void Encodes_per_rfc3986(string input, string expected)
    {
        Assert.Equal(expected, PercentEncoder.Encode(input));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter PercentEncoderTests`
Expected: FAIL.

- [ ] **Step 3: Implement `PercentEncoder`**

`src/Winix.MkAuth/PercentEncoder.cs`:
```csharp
using System.Text;

namespace Winix.MkAuth;

/// <summary>
/// RFC 3986 percent-encoding (the form OAuth 1.0a §3.6 requires). Only the unreserved set
/// <c>A-Z a-z 0-9 - . _ ~</c> is left literal; every other byte (of the UTF-8 encoding) becomes
/// <c>%XX</c> in upper-case hex. This is deliberately stricter than URL form-encoding (space is
/// <c>%20</c>, never <c>+</c>).
/// </summary>
public static class PercentEncoder
{
    private const string Unreserved =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

    public static string Encode(string value)
    {
        var sb = new StringBuilder(value.Length * 3);
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            char c = (char)b;
            if (Unreserved.IndexOf(c) >= 0)
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2"));
            }
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run to verify PercentEncoder passes**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter PercentEncoderTests`
Expected: PASS.

- [ ] **Step 5: Write failing Base64Url tests**

`Base64UrlTests.cs`:
```csharp
using System.Text;
using Winix.MkAuth;
using Xunit;

public class Base64UrlTests
{
    [Theory]
    // base64url of ASCII, no padding, '-'/'_' instead of '+'/'/'
    [InlineData("", "")]
    [InlineData("f", "Zg")]
    [InlineData("fo", "Zm8")]
    [InlineData("foo", "Zm9v")]
    [InlineData("foob", "Zm9vYg")]
    public void Encodes_no_padding(string ascii, string expected)
    {
        Assert.Equal(expected, Base64Url.EncodeNoPad(Encoding.ASCII.GetBytes(ascii)));
    }

    [Fact]
    public void Uses_url_alphabet()
    {
        // 0xFF 0xFE 0xFD -> standard base64 "//79" -> url "__79"
        Assert.Equal("__79", Base64Url.EncodeNoPad(new byte[] { 0xFF, 0xFE, 0xFD }));
    }
}
```

> **Verify at implementation:** confirm these expected strings with an independent base64url
> reference (e.g. `python3 -c "import base64;print(base64.urlsafe_b64encode(b'foob').rstrip(b'=').decode())"`).
> They are the canonical RFC 4648 §5 vectors but pin them against a real encoder, not this plan.

- [ ] **Step 6: Implement `Base64Url`**

`src/Winix.MkAuth/Base64Url.cs` — reuse `Winix.Codec.Base64` if it already offers a URL-safe mode (check `src/Winix.Codec/`); otherwise:
```csharp
namespace Winix.MkAuth;

/// <summary>base64url (RFC 4648 §5) without <c>=</c> padding — the JOSE encoding for JWT parts.</summary>
public static class Base64Url
{
    public static string EncodeNoPad(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

- [ ] **Step 7: Run to verify Base64Url passes**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter Base64UrlTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Winix.MkAuth/PercentEncoder.cs src/Winix.MkAuth/Base64Url.cs tests/Winix.MkAuth.Tests/PercentEncoderTests.cs tests/Winix.MkAuth.Tests/Base64UrlTests.cs
git commit -m "feat(mkauth): RFC 3986 percent-encoder + base64url helper"
```

---

## Task 4: Seams (IClock, INonceSource, MkAuthDeps) + HeaderResult

**Files:**
- Create: `src/Winix.MkAuth/IClock.cs`, `INonceSource.cs`, `MkAuthDeps.cs`, `HeaderResult.cs`
- Create: `tests/Winix.MkAuth.Tests/FixedClock.cs`, `FixedNonce.cs`

- [ ] **Step 1: Implement the seams and record (no test — exercised via signers/Cli)**

`src/Winix.MkAuth/IClock.cs`:
```csharp
namespace Winix.MkAuth;

/// <summary>Time source seam so timestamp-bearing schemes are deterministic under test.</summary>
public interface IClock { DateTimeOffset UtcNow { get; } }

/// <summary>Production clock.</summary>
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
```

`src/Winix.MkAuth/INonceSource.cs`:
```csharp
using Winix.Codec;

namespace Winix.MkAuth;

/// <summary>Nonce source seam (OAuth 1.0a <c>oauth_nonce</c>).</summary>
public interface INonceSource { string NextNonce(); }

/// <summary>Production nonce — a CSPRNG-backed URL-safe token.</summary>
public sealed class RandomNonceSource : INonceSource
{
    // Use the suite CSPRNG; confirm the exact Winix.Codec RNG/encoder names when implementing.
    public string NextNonce()
    {
        byte[] buf = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(buf);
        return Convert.ToHexString(buf).ToLowerInvariant();
    }
}
```

`src/Winix.MkAuth/MkAuthDeps.cs`:
```csharp
using Winix.SecretStore;

namespace Winix.MkAuth;

/// <summary>Injectable dependencies for <see cref="Cli"/>. Defaults are the production seams.</summary>
public sealed class MkAuthDeps
{
    public IClock Clock { get; init; } = new SystemClock();
    public INonceSource Nonce { get; init; } = new RandomNonceSource();
    public ISecretStore? SecretStore { get; init; }  // resolved lazily; only needed for vault: refs
}
```

`src/Winix.MkAuth/HeaderResult.cs`:
```csharp
namespace Winix.MkAuth;

/// <summary>The computed header. <paramref name="BaseString"/> is set only when a signing scheme
/// ran with base-string debug enabled.</summary>
public readonly record struct HeaderResult(string HeaderName, string HeaderValue, string? BaseString = null);
```

- [ ] **Step 2: Implement the test doubles**

`FixedClock.cs`:
```csharp
using Winix.MkAuth;
public sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; } = now; }
```
`FixedNonce.cs`:
```csharp
using Winix.MkAuth;
public sealed class FixedNonce(string nonce) : INonceSource { public string NextNonce() => nonce; }
```

- [ ] **Step 3: Build**

Run: `dotnet build Winix.sln`
Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Winix.MkAuth/IClock.cs src/Winix.MkAuth/INonceSource.cs src/Winix.MkAuth/MkAuthDeps.cs src/Winix.MkAuth/HeaderResult.cs tests/Winix.MkAuth.Tests/FixedClock.cs tests/Winix.MkAuth.Tests/FixedNonce.cs
git commit -m "feat(mkauth): clock/nonce/deps seams + HeaderResult"
```

---

## Task 5: Basic + Bearer builders

**Files:**
- Create: `src/Winix.MkAuth/BasicAuthBuilder.cs`, `BearerAuthBuilder.cs`
- Create: `tests/Winix.MkAuth.Tests/BasicAuthBuilderTests.cs`, `BearerAuthBuilderTests.cs`

- [ ] **Step 1: Write failing Basic tests**

`BasicAuthBuilderTests.cs`:
```csharp
using Winix.MkAuth;
using Xunit;

public class BasicAuthBuilderTests
{
    [Fact]
    public void Builds_rfc7617_header()
    {
        // base64("Aladdin:open sesame") == "QWxhZGRpbjpvcGVuIHNlc2FtZQ==" (RFC 7617 example)
        var r = BasicAuthBuilder.Build("Aladdin", "open sesame");
        Assert.Equal("Authorization", r.HeaderName);
        Assert.Equal("Basic QWxhZGRpbjpvcGVuIHNlc2FtZQ==", r.HeaderValue);
    }

    [Fact]
    public void Username_with_colon_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => BasicAuthBuilder.Build("a:b", "pw"));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter BasicAuthBuilderTests`
Expected: FAIL.

- [ ] **Step 3: Implement `BasicAuthBuilder`**

`src/Winix.MkAuth/BasicAuthBuilder.cs`:
```csharp
using System.Text;

namespace Winix.MkAuth;

/// <summary>RFC 7617 Basic authentication header.</summary>
public static class BasicAuthBuilder
{
    /// <exception cref="ArgumentException">The username contains a colon (the user:password delimiter).</exception>
    public static HeaderResult Build(string user, string password)
    {
        if (user.Contains(':'))
        {
            throw new ArgumentException("A Basic-auth username cannot contain ':'.", nameof(user));
        }
        string token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
        return new HeaderResult("Authorization", $"Basic {token}");
    }
}
```

- [ ] **Step 4: Run Basic tests**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter BasicAuthBuilderTests`
Expected: PASS.

- [ ] **Step 5: Write failing Bearer tests**

`BearerAuthBuilderTests.cs`:
```csharp
using Winix.MkAuth;
using Xunit;

public class BearerAuthBuilderTests
{
    [Fact]
    public void Builds_bearer_header()
    {
        var r = BearerAuthBuilder.Build("abc.def.ghi");
        Assert.Equal("Authorization", r.HeaderName);
        Assert.Equal("Bearer abc.def.ghi", r.HeaderValue);
    }
}
```

- [ ] **Step 6: Implement `BearerAuthBuilder`**

`src/Winix.MkAuth/BearerAuthBuilder.cs`:
```csharp
namespace Winix.MkAuth;

/// <summary>RFC 6750 Bearer token header. Pure passthrough of the resolved token.</summary>
public static class BearerAuthBuilder
{
    public static HeaderResult Build(string token) => new("Authorization", $"Bearer {token}");
}
```

- [ ] **Step 7: Run Bearer tests + commit**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter BearerAuthBuilderTests`
Expected: PASS.
```bash
git add src/Winix.MkAuth/BasicAuthBuilder.cs src/Winix.MkAuth/BearerAuthBuilder.cs tests/Winix.MkAuth.Tests/BasicAuthBuilderTests.cs tests/Winix.MkAuth.Tests/BearerAuthBuilderTests.cs
git commit -m "feat(mkauth): basic + bearer header builders"
```

---

## Task 6: OAuth1Signer (flagship)

**Files:**
- Create: `src/Winix.MkAuth/OAuth1Signer.cs`
- Create: `tests/Winix.MkAuth.Tests/OAuth1SignerTests.cs`

- [ ] **Step 1: Write the failing options + base-string test**

`OAuth1SignerTests.cs`:
```csharp
using Winix.MkAuth;
using Xunit;

public class OAuth1SignerTests
{
    private static OAuth1Request Sample() => new()
    {
        Method = "GET",
        Url = "https://api.example.com/1/statuses.json?count=5&page=2",
        ConsumerKey = "ck",
        ConsumerSecret = "cs",
        Token = "tk",
        TokenSecret = "ts",
        SignatureMethod = OAuth1SignatureMethod.HmacSha1,
        Timestamp = 1318622958,
        Nonce = "kYjzVBB8Y0ZFabxSWbWovY3uYSQ2pTgmZeNu2VS4cg",
    };

    [Fact]
    public void Base_string_normalizes_sorts_and_double_encodes()
    {
        var sig = OAuth1Signer.Sign(Sample());
        // The base string must: upper-case method; strip query for the URL part; merge query +
        // oauth_* params; percent-encode; sort; re-encode the joined param block.
        Assert.StartsWith("GET&https%3A%2F%2Fapi.example.com%2F1%2Fstatuses.json&", sig.BaseString);
        Assert.Contains("count%3D5", sig.BaseString);
        Assert.Contains("oauth_nonce%3D", sig.BaseString);
        Assert.DoesNotContain("page=2", sig.BaseString); // raw form must not survive
    }

    [Fact]
    public void Header_contains_all_oauth_params_quoted_and_encoded()
    {
        var r = OAuth1Signer.Sign(Sample());
        Assert.Equal("Authorization", r.Header.HeaderName);
        Assert.StartsWith("OAuth ", r.Header.HeaderValue);
        Assert.Contains("oauth_consumer_key=\"ck\"", r.Header.HeaderValue);
        Assert.Contains("oauth_signature_method=\"HMAC-SHA1\"", r.Header.HeaderValue);
        Assert.Contains("oauth_version=\"1.0\"", r.Header.HeaderValue);
        Assert.Contains("oauth_signature=\"", r.Header.HeaderValue);
    }
}
```

- [ ] **Step 2: Add the PINNED reference-vector test (wire correctness)**

Append to `OAuth1SignerTests.cs`:
```csharp
    [Fact]
    public void Matches_published_reference_vector()
    {
        // VERIFY AT IMPLEMENTATION: use the widely-published Twitter OAuth1 example
        // (consumer key/secret, token/secret, method=POST, url, params, fixed timestamp+nonce)
        // whose documented oauth_signature is "tnnArxj06cWHq44gCs1OSKk/jLY=".
        // Build the EXACT request from that reference and assert the signature equals the
        // published value. Do NOT trust the strings below until reproduced from the source doc;
        // correct them to the reference at implementation time.
        var req = new OAuth1Request { /* fill from the cited Twitter example */ };
        var r = OAuth1Signer.Sign(req);
        Assert.Equal("tnnArxj06cWHq44gCs1OSKk/jLY=", r.Signature);
    }
```
> **Per `feedback_plan_to_code_divergence` + the plan-assumptions rule:** this is the one test that
> proves wire correctness against a real counterpart's published bytes. The exact request fields and
> the signature MUST be reconciled against the cited reference during implementation; the placeholder
> request is intentionally empty so it cannot pass spuriously.

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter OAuth1SignerTests`
Expected: FAIL (types not defined).

- [ ] **Step 4: Implement `OAuth1Signer`**

`src/Winix.MkAuth/OAuth1Signer.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;

namespace Winix.MkAuth;

public enum OAuth1SignatureMethod { HmacSha1, HmacSha256, Plaintext }

/// <summary>Inputs for an OAuth 1.0a signature. Timestamp/Nonce are pre-resolved by the caller
/// (from <see cref="IClock"/>/<see cref="INonceSource"/> or explicit flags) so signing is pure.</summary>
public sealed class OAuth1Request
{
    public required string Method { get; init; }
    public required string Url { get; init; }
    public required string ConsumerKey { get; init; }
    public required string ConsumerSecret { get; init; }
    public string? Token { get; init; }
    public string TokenSecret { get; init; } = "";
    public OAuth1SignatureMethod SignatureMethod { get; init; } = OAuth1SignatureMethod.HmacSha1;
    public IReadOnlyList<KeyValuePair<string, string>> ExtraParams { get; init; } = Array.Empty<KeyValuePair<string, string>>();
    public string? Realm { get; init; }
    public required long Timestamp { get; init; }
    public required string Nonce { get; init; }
}

/// <summary>The signing result.</summary>
public sealed class OAuth1Result
{
    public required string BaseString { get; init; }
    public required string Signature { get; init; }
    public required HeaderResult Header { get; init; }
}

/// <summary>OAuth 1.0a request signer (RFC 5849, §3.4).</summary>
public static class OAuth1Signer
{
    public static OAuth1Result Sign(OAuth1Request req)
    {
        var uri = new Uri(req.Url);

        // oauth_* params (signature method name on the wire)
        string sigMethodName = req.SignatureMethod switch
        {
            OAuth1SignatureMethod.HmacSha1 => "HMAC-SHA1",
            OAuth1SignatureMethod.HmacSha256 => "HMAC-SHA256",
            OAuth1SignatureMethod.Plaintext => "PLAINTEXT",
            _ => throw new ArgumentOutOfRangeException(),
        };

        var oauthParams = new List<KeyValuePair<string, string>>
        {
            new("oauth_consumer_key", req.ConsumerKey),
            new("oauth_nonce", req.Nonce),
            new("oauth_signature_method", sigMethodName),
            new("oauth_timestamp", req.Timestamp.ToString()),
            new("oauth_version", "1.0"),
        };
        if (!string.IsNullOrEmpty(req.Token))
        {
            oauthParams.Add(new("oauth_token", req.Token!));
        }

        // All params that go into the signature base: URL query + extra/body + oauth_* (NOT realm/signature)
        var allParams = new List<KeyValuePair<string, string>>();
        allParams.AddRange(ParseQuery(uri.Query));
        allParams.AddRange(req.ExtraParams);
        allParams.AddRange(oauthParams);

        string normalizedParams = string.Join("&", allParams
            .Select(p => new KeyValuePair<string, string>(PercentEncoder.Encode(p.Key), PercentEncoder.Encode(p.Value)))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}"));

        string baseUrl = NormalizeBaseUrl(uri);
        string baseString = string.Concat(
            req.Method.ToUpperInvariant(), "&",
            PercentEncoder.Encode(baseUrl), "&",
            PercentEncoder.Encode(normalizedParams));

        string signingKey = $"{PercentEncoder.Encode(req.ConsumerSecret)}&{PercentEncoder.Encode(req.TokenSecret)}";

        string signature = req.SignatureMethod switch
        {
            OAuth1SignatureMethod.Plaintext => signingKey,
            OAuth1SignatureMethod.HmacSha1 => HmacBase64<HMACSHA1>(signingKey, baseString),
            OAuth1SignatureMethod.HmacSha256 => HmacBase64<HMACSHA256>(signingKey, baseString),
            _ => throw new ArgumentOutOfRangeException(),
        };

        // Header: realm (not signed) + oauth_* + oauth_signature, each value pct-encoded and quoted.
        var headerParams = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrEmpty(req.Realm))
        {
            headerParams.Add(new("realm", req.Realm!));
        }
        headerParams.AddRange(oauthParams);
        headerParams.Add(new("oauth_signature", signature));

        string headerValue = "OAuth " + string.Join(", ",
            headerParams.Select(p => $"{PercentEncoder.Encode(p.Key)}=\"{PercentEncoder.Encode(p.Value)}\""));

        return new OAuth1Result
        {
            BaseString = baseString,
            Signature = signature,
            Header = new HeaderResult("Authorization", headerValue, baseString),
        };
    }

    private static string HmacBase64<T>(string key, string data) where T : HMAC, new()
    {
        using var h = new T { Key = Encoding.UTF8.GetBytes(key) };
        return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }

    private static string NormalizeBaseUrl(Uri uri)
    {
        string scheme = uri.Scheme.ToLowerInvariant();
        string host = uri.Host.ToLowerInvariant();
        bool defaultPort = (scheme == "http" && uri.Port == 80) || (scheme == "https" && uri.Port == 443);
        string authority = defaultPort ? host : $"{host}:{uri.Port}";
        return $"{scheme}://{authority}{uri.AbsolutePath}";
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            yield break;
        }
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            // Query arrives percent-encoded on the URL; decode so it is encoded exactly once in the base string.
            yield return eq < 0
                ? new(Uri.UnescapeDataString(pair), "")
                : new(Uri.UnescapeDataString(pair[..eq]), Uri.UnescapeDataString(pair[(eq + 1)..]));
        }
    }
}
```

- [ ] **Step 5: Run all OAuth1 tests**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter OAuth1SignerTests`
Expected: structural tests PASS; the pinned-vector test PASS **after** the implementer fills the reference request and confirms the signature. (It MUST NOT be left as an empty-request no-op.)

- [ ] **Step 6: Commit**

```bash
git add src/Winix.MkAuth/OAuth1Signer.cs tests/Winix.MkAuth.Tests/OAuth1SignerTests.cs
git commit -m "feat(mkauth): OAuth 1.0a signer (flagship) with pinned reference vector"
```

---

## Task 7: JwtSigner

**Files:**
- Create: `src/Winix.MkAuth/JwtSigner.cs`
- Create: `tests/Winix.MkAuth.Tests/JwtSignerTests.cs`

> **AOT claim-serialization spike (ADR §8) — do this step FIRST, before writing JwtSigner:** write a
> 20-line throwaway that builds a `System.Text.Json.Nodes.JsonObject` from mixed-type claims, calls
> `.ToJsonString()`, and `dotnet publish -r <rid>` the `mkauth` app referencing it. Confirm **zero**
> AOT/trim warnings for `JsonNode`. If `JsonNode` warns, fall back to building the JSON object string
> manually (ordered `StringBuilder` with proper escaping). Record the outcome in the commit message.
>
> **F2 — numeric claims MUST serialize as JSON numbers, not strings.** The spike's SECOND job: confirm
> that a numeric registered claim (`exp`/`iat`/`nbf`, RFC 7519 NumericDate) round-trips as a JSON
> **number** (`"exp":1700000000`), NOT a quoted string (`"exp":"1700000000"`) — a string NumericDate
> is rejected by virtually every JWT verifier (silent-wrong-output). Because `Claims` is
> `Dictionary<string, object?>`, `JsonValue.Create(kv.Value)` on a boxed `long` may not emit a number
> depending on how the boxed value is handled under AOT. `JwtSigner` MUST therefore **type-switch on
> the runtime value** when building the payload: `long`→number, `bool`→bool, `JsonNode`→inlined,
> everything else→string. Verify the emitted JSON in the spike and pin it with the test in Step 1a.

- [ ] **Step 1: Write the failing HS256 deterministic test**

`JwtSignerTests.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using Winix.MkAuth;
using Xunit;

public class JwtSignerTests
{
    [Fact]
    public void Hs256_token_has_three_parts_and_verifies()
    {
        var req = new JwtRequest
        {
            Algorithm = "HS256",
            Key = Encoding.UTF8.GetBytes("supersecretkey"),
            Claims = new() { ["iss"] = "me", ["sub"] = "42" },
        };
        var jwt = JwtSigner.Sign(req).Token;

        string[] parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);

        // Independently verify the signature (do NOT reuse the impl's base64url helper here).
        string signingInput = parts[0] + "." + parts[1];
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes("supersecretkey"));
        byte[] expected = h.ComputeHash(Encoding.ASCII.GetBytes(signingInput));
        string expectedSig = Convert.ToBase64String(expected).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expectedSig, parts[2]);
    }

    [Fact]                                              // F2: NumericDate must be a JSON number
    public void Numeric_exp_serializes_as_json_number()
    {
        var req = new JwtRequest
        {
            Algorithm = "HS256",
            Key = Encoding.UTF8.GetBytes("k"),
            Claims = new() { ["exp"] = 1700000000L, ["sub"] = "x" },
        };
        string payload = Encoding.UTF8.GetString(Base64UrlDecode(JwtSigner.Sign(req).Token.Split('.')[1]));
        Assert.Contains("\"exp\":1700000000", payload);     // number, no quotes
        Assert.DoesNotContain("\"exp\":\"1700000000\"", payload);
        Assert.Contains("\"sub\":\"x\"", payload);          // string claim stays quoted
    }

    [Fact]
    public void Header_line_wraps_as_bearer()
    {
        var req = new JwtRequest { Algorithm = "HS256", Key = Encoding.UTF8.GetBytes("k"), Claims = new() };
        var r = JwtSigner.Sign(req);
        Assert.Equal("Authorization", r.Header.HeaderName);
        Assert.StartsWith("Bearer ", r.Header.HeaderValue);
    }

    [Fact]
    public void Rs256_signs_and_verifies_with_public_key()
    {
        using var rsa = RSA.Create(2048);
        var req = new JwtRequest { Algorithm = "RS256", KeyPem = rsa.ExportRSAPrivateKeyPem(), Claims = new() { ["sub"] = "x" } };
        var jwt = JwtSigner.Sign(req).Token;
        string[] parts = jwt.Split('.');
        byte[] sig = Base64UrlDecode(parts[2]);
        Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void Es256_signs_in_ieee_p1363_and_verifies()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new JwtRequest { Algorithm = "ES256", KeyPem = ec.ExportECPrivateKeyPem(), Claims = new() { ["sub"] = "x" } };
        var jwt = JwtSigner.Sign(req).Token;
        string[] parts = jwt.Split('.');
        byte[] sig = Base64UrlDecode(parts[2]);
        Assert.Equal(64, sig.Length); // P-256 r||s is fixed 64 bytes (the JOSE requirement)
        Assert.True(ec.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]                                              // F4: HS alg given a PEM
    public void Hs_alg_without_raw_key_is_clear_error()
    {
        var req = new JwtRequest { Algorithm = "HS256", KeyPem = "-----BEGIN PRIVATE KEY-----\n...", Claims = new() };
        var ex = Assert.Throws<ArgumentException>(() => JwtSigner.Sign(req));
        Assert.Contains("secret", ex.Message, StringComparison.OrdinalIgnoreCase); // readable, not an SR key
    }

    [Fact]                                              // F4: RS alg given raw bytes
    public void Rs_alg_without_pem_is_clear_error()
    {
        var req = new JwtRequest { Algorithm = "RS256", Key = Encoding.UTF8.GetBytes("notapem"), Claims = new() };
        var ex = Assert.Throws<ArgumentException>(() => JwtSigner.Sign(req));
        Assert.Contains("PEM", ex.Message);
    }

    private static byte[] Base64UrlDecode(string s)
    {
        string b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }
}
```
> **Note on the protocol-fake rule:** the HS256 test *does* recompute the signature, but it
> deliberately uses the **stdlib** (`HMACSHA256` + inline base64url) as the independent oracle, not
> `JwtSigner`'s own helpers — so a bug in `Base64Url`/`JwtSigner` is still caught. RS/ES tests verify
> with the public key (a genuinely independent operation).

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter JwtSignerTests`
Expected: FAIL.

- [ ] **Step 3: Implement `JwtSigner`**

`src/Winix.MkAuth/JwtSigner.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Winix.MkAuth;

/// <summary>JWT minting inputs. Provide <see cref="Key"/> for HS* (raw secret bytes) OR
/// <see cref="KeyPem"/> for RS*/ES* (PEM private key).</summary>
public sealed class JwtRequest
{
    public required string Algorithm { get; init; }      // HS256/384/512, RS256/384/512, ES256/384/512
    public byte[]? Key { get; init; }                    // HS*
    public string? KeyPem { get; init; }                 // RS*/ES*
    public Dictionary<string, object?> Claims { get; init; } = new();
    public Dictionary<string, string> HeaderParams { get; init; } = new(); // kid, etc.
}

/// <summary>The minted token.</summary>
public sealed class JwtResult
{
    public required string Token { get; init; }
    public required HeaderResult Header { get; init; }
}

/// <summary>Mints a signed compact JWS (RFC 7515/7519). Hand-built to stay AOT-clean.</summary>
public static class JwtSigner
{
    public static JwtResult Sign(JwtRequest req)
    {
        RequireKeyForAlg(req); // F4: HS* needs Key, RS*/ES* needs KeyPem — clear message, not an NRE

        var header = new JsonObject { ["alg"] = req.Algorithm, ["typ"] = "JWT" };
        foreach (var kv in req.HeaderParams)
        {
            header[kv.Key] = kv.Value;
        }
        var payload = new JsonObject();
        foreach (var kv in req.Claims)
        {
            payload[kv.Key] = ToJsonNode(kv.Value); // F2: preserve JSON type (number/bool/string)
        }

        string encodedHeader = Base64Url.EncodeNoPad(Encoding.UTF8.GetBytes(header.ToJsonString()));
        string encodedPayload = Base64Url.EncodeNoPad(Encoding.UTF8.GetBytes(payload.ToJsonString()));
        string signingInput = $"{encodedHeader}.{encodedPayload}";
        byte[] signingBytes = Encoding.ASCII.GetBytes(signingInput);

        byte[] sig = req.Algorithm switch
        {
            "HS256" => Hmac<HMACSHA256>(req.Key!, signingBytes),
            "HS384" => Hmac<HMACSHA384>(req.Key!, signingBytes),
            "HS512" => Hmac<HMACSHA512>(req.Key!, signingBytes),
            "RS256" => Rsa(req.KeyPem!, signingBytes, HashAlgorithmName.SHA256),
            "RS384" => Rsa(req.KeyPem!, signingBytes, HashAlgorithmName.SHA384),
            "RS512" => Rsa(req.KeyPem!, signingBytes, HashAlgorithmName.SHA512),
            "ES256" => Ec(req.KeyPem!, signingBytes, HashAlgorithmName.SHA256),
            "ES384" => Ec(req.KeyPem!, signingBytes, HashAlgorithmName.SHA384),
            "ES512" => Ec(req.KeyPem!, signingBytes, HashAlgorithmName.SHA512),
            _ => throw new ArgumentException($"Unsupported JWT algorithm '{req.Algorithm}'."),
        };

        string token = $"{signingInput}.{Base64Url.EncodeNoPad(sig)}";
        return new JwtResult { Token = token, Header = new HeaderResult("Authorization", $"Bearer {token}") };
    }

    private static byte[] Hmac<T>(byte[] key, byte[] data) where T : HMAC, new()
    {
        using var h = new T { Key = key };
        return h.ComputeHash(data);
    }

    private static byte[] Rsa(string pem, byte[] data, HashAlgorithmName hash)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa.SignData(data, hash, RSASignaturePadding.Pkcs1);
    }

    private static byte[] Ec(string pem, byte[] data, HashAlgorithmName hash)
    {
        using var ec = ECDsa.Create();
        ec.ImportFromPem(pem);
        return ec.SignData(data, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation); // JOSE requires raw r||s
    }

    // F2: preserve the JSON type of a claim value (so NumericDate stays a number).
    private static JsonNode? ToJsonNode(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        long l => JsonValue.Create(l),
        int i => JsonValue.Create((long)i),
        double d => JsonValue.Create(d),
        JsonNode n => n,
        _ => JsonValue.Create(value.ToString()),
    };

    // F4: fail fast with a clear message when the key kind doesn't match the algorithm family.
    private static void RequireKeyForAlg(JwtRequest req)
    {
        bool hmac = req.Algorithm.StartsWith("HS", StringComparison.Ordinal);
        if (hmac && req.Key is null)
        {
            throw new ArgumentException($"Algorithm {req.Algorithm} needs a raw secret key (e.g. --key env:SECRET), not a PEM.");
        }
        if (!hmac && req.KeyPem is null)
        {
            throw new ArgumentException($"Algorithm {req.Algorithm} needs a PEM private key (e.g. --key file:key.pem), not a raw secret.");
        }
    }
}
```

- [ ] **Step 4: Run JWT tests**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter JwtSignerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.MkAuth/JwtSigner.cs tests/Winix.MkAuth.Tests/JwtSignerTests.cs
git commit -m "feat(mkauth): JWT signer (HS/RS/ES families), hand-built AOT-clean JWS"
```

---

## Task 8: AzureStorageSigner (spike-gated — ADR §7)

**Files:**
- Create: `src/Winix.MkAuth/AzureStorageSigner.cs`
- Create: `tests/Winix.MkAuth.Tests/AzureStorageSignerTests.cs`

> **Spike gate (do this BEFORE writing the signer):** in a throwaway console referencing
> `Azure.Storage.Blobs`/`Azure.Storage.Common`, construct a `StorageSharedKeyCredential` and sign a
> known request (fixed account, fixed base64 key, fixed `x-ms-date`, a couple of `x-ms-*` headers,
> a GET on a blob URL with a query). Capture the resulting `Authorization: SharedKey acct:sig`
> value. That captured value is the pinned fixture for Step 1. **If reproducing the StringToSign to
> match the captured signature takes more than a bounded effort (≈ half a day) — e.g. the
> Content-Length-empty-vs-"0" or x-ms-date-vs-Date rules won't line up — STOP, remove the
> subcommand from this batch, and record the deferral in the ADR (move §7 to the Deferred table).**

- [ ] **Step 1: Write the failing pinned-fixture test**

`AzureStorageSignerTests.cs`:
```csharp
using Winix.MkAuth;
using Xunit;

public class AzureStorageSignerTests
{
    [Fact]
    public void Matches_azure_sdk_reference_signature()
    {
        // VERIFY AT IMPLEMENTATION: replace the placeholders below with the EXACT request and the
        // captured StorageSharedKeyCredential signature from the spike. Empty placeholders keep this
        // from passing spuriously.
        var req = new AzureStorageRequest
        {
            Account = "",            // from spike
            KeyBase64 = "",          // from spike (a test-only base64 key)
            Method = "GET",
            Url = "",                // from spike
            XmsDate = "",            // from spike (RFC1123 GMT)
            XmsVersion = "",         // from spike
            Headers = new(),         // any extra x-ms-* used in the spike
        };
        var r = AzureStorageSigner.Sign(req);
        Assert.Equal("Authorization", r.Header.HeaderName);
        Assert.Equal(/* captured */ "SharedKey :", r.Header.HeaderValue);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter AzureStorageSignerTests`
Expected: FAIL.

- [ ] **Step 3: Implement `AzureStorageSigner` (Blob/Queue/File SharedKey)**

`src/Winix.MkAuth/AzureStorageSigner.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;

namespace Winix.MkAuth;

/// <summary>Azure Storage SharedKey signing inputs (Blob/Queue/File). Header values must match what
/// the client actually sends, since they are part of the signature.</summary>
public sealed class AzureStorageRequest
{
    public required string Account { get; init; }
    public required string KeyBase64 { get; init; }
    public required string Method { get; init; }
    public required string Url { get; init; }
    public required string XmsDate { get; init; }      // value sent as x-ms-date
    public required string XmsVersion { get; init; }   // value sent as x-ms-version
    public Dictionary<string, string> Headers { get; init; } = new(); // additional x-ms-* and content headers
}

/// <summary>The SharedKey signing result.</summary>
public sealed class AzureStorageResult
{
    public required string StringToSign { get; init; }
    public required HeaderResult Header { get; init; }
}

/// <summary>
/// Azure Storage Shared Key authorization for Blob/Queue/File (NOT Table; NOT SharedKeyLite).
/// StringToSign layout per Microsoft "Authorize with Shared Key".
/// </summary>
public static class AzureStorageSigner
{
    public static AzureStorageResult Sign(AzureStorageRequest req)
    {
        var uri = new Uri(req.Url);

        // x-ms-* header map (case-insensitive); x-ms-date/x-ms-version always present.
        var xms = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["x-ms-date"] = req.XmsDate,
            ["x-ms-version"] = req.XmsVersion,
        };
        foreach (var kv in req.Headers)
        {
            string name = kv.Key.ToLowerInvariant();
            if (name.StartsWith("x-ms-"))
            {
                xms[name] = kv.Value.Trim();
            }
        }
        string canonicalizedHeaders = string.Concat(xms.Select(kv => $"{kv.Key}:{kv.Value}\n"));

        // The 12 fixed StringToSign header slots. Content-Length is "" when zero (x-ms-version 2014-02-14+).
        // When x-ms-date is present, the Date slot is empty.
        string Get(string h) => req.Headers.TryGetValue(h, out var v) ? v : "";
        string contentLength = Get("Content-Length") is "0" or "" ? "" : Get("Content-Length");

        string stringToSign = string.Join("\n",
            req.Method.ToUpperInvariant(),
            Get("Content-Encoding"),
            Get("Content-Language"),
            contentLength,
            Get("Content-MD5"),
            Get("Content-Type"),
            "",                                   // Date (empty — using x-ms-date)
            Get("If-Modified-Since"),
            Get("If-Match"),
            Get("If-None-Match"),
            Get("If-Unmodified-Since"),
            Get("Range"))
            + "\n" + canonicalizedHeaders + CanonicalizedResource(uri, req.Account);

        using var hmac = new HMACSHA256(Convert.FromBase64String(req.KeyBase64));
        string sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
        string headerValue = $"SharedKey {req.Account}:{sig}";

        return new AzureStorageResult
        {
            StringToSign = stringToSign,
            Header = new HeaderResult("Authorization", headerValue, stringToSign),
        };
    }

    private static string CanonicalizedResource(Uri uri, string account)
    {
        var sb = new StringBuilder($"/{account}{uri.AbsolutePath}");
        // query params: lowercase name, sorted ordinal, "\nname:value" (comma-join duplicate values).
        var q = uri.Query.TrimStart('?');
        if (q.Length > 0)
        {
            var byName = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string name = (eq < 0 ? pair : pair[..eq]).ToLowerInvariant();
                string val = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..]);
                (byName.TryGetValue(name, out var l) ? l : byName[name] = new()).Add(val);
            }
            foreach (var kv in byName)
            {
                kv.Value.Sort(StringComparer.Ordinal);
                sb.Append('\n').Append(kv.Key).Append(':').Append(string.Join(",", kv.Value));
            }
        }
        return sb.ToString();
    }
}
```
> **Verify at implementation:** the `Date`-empty-when-x-ms-date and `Content-Length`-empty-when-zero
> rules are version-sensitive; reconcile against the captured SDK signature. If they don't line up
> within the spike budget, defer per the spike gate.

- [ ] **Step 4: Run Azure tests (after fixture capture)**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter AzureStorageSignerTests`
Expected: PASS once the captured fixture is in place — OR the subcommand is removed and this task struck.

- [ ] **Step 5: Commit (or commit the documented deferral)**

```bash
git add src/Winix.MkAuth/AzureStorageSigner.cs tests/Winix.MkAuth.Tests/AzureStorageSignerTests.cs
git commit -m "feat(mkauth): Azure Storage SharedKey signer (Blob/Queue/File) with SDK-captured vector"
```

---

## Task 9: Formatting (plain / value-only / json)

**Files:**
- Create: `src/Winix.MkAuth/Formatting.cs`
- Create: `tests/Winix.MkAuth.Tests/FormattingTests.cs`

- [ ] **Step 1: Write failing tests**

`FormattingTests.cs`:
```csharp
using Winix.MkAuth;
using Xunit;

public class FormattingTests
{
    private static readonly HeaderResult Sample = new("Authorization", "Bearer xyz", BaseString: "BASE");

    [Fact]
    public void Plain_emits_full_header_line()
        => Assert.Equal("Authorization: Bearer xyz", Formatting.Plain(Sample, valueOnly: false));

    [Fact]
    public void Value_only_emits_bare_value()
        => Assert.Equal("Bearer xyz", Formatting.Plain(Sample, valueOnly: true));

    [Fact]
    public void Json_envelope_shape()
    {
        string json = Formatting.Json(new HeaderResult("Authorization", "OAuth abc"), scheme: "oauth1", includeBaseString: false);
        Assert.Contains("\"scheme\":\"oauth1\"", json);
        Assert.Contains("\"header_name\":\"Authorization\"", json);
        Assert.Contains("\"header_value\":\"OAuth abc\"", json);
        Assert.DoesNotContain("base_string", json);
    }

    [Fact]
    public void Json_includes_base_string_when_requested()
    {
        string json = Formatting.Json(Sample, scheme: "jwt", includeBaseString: true);
        Assert.Contains("\"base_string\":\"BASE\"", json);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter FormattingTests`
Expected: FAIL.

- [ ] **Step 3: Implement `Formatting`** (use `Yort.ShellKit`'s JSON helper if present — check `src/Yort.ShellKit/`; otherwise minimal manual escaping)

`src/Winix.MkAuth/Formatting.cs`:
```csharp
using System.Text;

namespace Winix.MkAuth;

/// <summary>Output shaping. The header is the tool's own data → stdout.</summary>
public static class Formatting
{
    public static string Plain(HeaderResult r, bool valueOnly)
        => valueOnly ? r.HeaderValue : $"{r.HeaderName}: {r.HeaderValue}";

    public static string Json(HeaderResult r, string scheme, bool includeBaseString)
    {
        var sb = new StringBuilder("{");
        sb.Append("\"scheme\":").Append(Quote(scheme)).Append(',');
        sb.Append("\"header_name\":").Append(Quote(r.HeaderName)).Append(',');
        sb.Append("\"header_value\":").Append(Quote(r.HeaderValue));
        if (includeBaseString && r.BaseString is not null)
        {
            sb.Append(",\"base_string\":").Append(Quote(r.BaseString));
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
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
                    if (c < 0x20) { sb.Append("\\u").Append(((int)c).ToString("x4")); }
                    else { sb.Append(c); }
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
```

- [ ] **Step 4: Run + commit**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter FormattingTests`
Expected: PASS.
```bash
git add src/Winix.MkAuth/Formatting.cs tests/Winix.MkAuth.Tests/FormattingTests.cs
git commit -m "feat(mkauth): output formatting (plain/value-only/json)"
```

---

## Task 10: ArgParser (subcommand dispatch + per-scheme binding)

**Files:**
- Create: `src/Winix.MkAuth/ArgParser.cs`
- Create: `tests/Winix.MkAuth.Tests/ArgParserTests.cs`

> **Before writing:** read `src/url/` or `src/qr/`'s arg-parsing for the ShellKit
> `CommandLineParser` subcommand-dispatch pattern (`positional[0]`), and how they wire
> `--help/--version/--describe`, `.Example()`, `.ComposesWith()`, and `.Section()`. Mirror it.
> The parser produces a discriminated set of per-scheme option records; this task tests the
> *parsing/validation*, not the signing (already covered).

- [ ] **Step 1: Write failing tests for dispatch + validation**

`ArgParserTests.cs`:
```csharp
using Winix.MkAuth;
using Xunit;

public class ArgParserTests
{
    [Fact]
    public void Unknown_subcommand_is_usage_error()
    {
        var result = ArgParser.Parse(new[] { "frobnicate" });
        Assert.False(result.Ok);
        Assert.Equal(AuthScheme.Basic, default); // sanity: enum exists
    }

    [Fact]
    public void Basic_requires_user_and_password()
    {
        Assert.False(ArgParser.Parse(new[] { "basic", "--user", "bob" }).Ok); // missing --password
        Assert.True(ArgParser.Parse(new[] { "basic", "--user", "bob", "--password", "env:P" }).Ok);
    }

    [Fact]
    public void Oauth1_rejects_unknown_signature_method()
    {
        var r = ArgParser.Parse(new[] { "oauth1", "--method", "GET", "--url", "https://x/y",
            "--consumer-key", "k", "--consumer-secret", "literal:s", "--signature-method", "BOGUS" });
        Assert.False(r.Ok);
    }

    [Fact]
    public void Jwt_rejects_unknown_alg()
    {
        var r = ArgParser.Parse(new[] { "jwt", "--alg", "ZZ9", "--key", "literal:k" });
        Assert.False(r.Ok);
    }

    [Fact]                                              // G1: k=v value may contain '=' (split on first only)
    public void Param_value_may_contain_equals()
    {
        var r = ArgParser.Parse(new[] { "oauth1", "--method", "GET", "--url", "https://x/y",
            "--consumer-key", "k", "--consumer-secret", "literal:s", "--param", "state=a=b" });
        Assert.True(r.Ok);
        // The implementer asserts the parsed param is key "state", value "a=b" against whatever
        // shape ParseResult exposes the params in (e.g. r.OAuth1.ExtraParams).
    }

    [Fact]                                              // G1/F5: k=v with no '=' is a usage error
    public void Param_without_equals_is_usage_error()
    {
        var r = ArgParser.Parse(new[] { "oauth1", "--method", "GET", "--url", "https://x/y",
            "--consumer-key", "k", "--consumer-secret", "literal:s", "--param", "novalue" });
        Assert.False(r.Ok);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter ArgParserTests`
Expected: FAIL.

- [ ] **Step 3: Implement `ArgParser` and `AuthScheme`**

Create `src/Winix.MkAuth/AuthScheme.cs`:
```csharp
namespace Winix.MkAuth;
/// <summary>The auth schemes mkauth can compute, selected by positional[0].</summary>
public enum AuthScheme { Basic, Bearer, OAuth1, Jwt, AzureStorage }
```

Create `src/Winix.MkAuth/ArgParser.cs` using `Yort.ShellKit.CommandLineParser`:
- Dispatch on `positional[0]` to one of the five schemes (`basic|bearer|oauth1|jwt|azure-storage`); anything else → `ParseResult` with `Ok=false` and a usage message.
- Bind global flags: `--value-only`, `--json`, `--show-base-string`, plus ShellKit's standard `--help/--version/--describe/--color/--no-color`.
- Per-scheme required/optional flags exactly as the design's option tables specify. Validate: unknown `--signature-method` (allowed: `HMAC-SHA1|HMAC-SHA256|PLAINTEXT`), unknown `--alg` (allowed: the 9 HS/RS/ES names), missing required flags → `Ok=false`. **F5/G1:** every repeated `k=v` flag (`--param`, `--header`, `--claim`, `--claim-num`, `--claim-json`) splits on the **first** `=` only — the key is everything before it, the value is the full remainder (which MAY contain further `=`, e.g. base64 padding or `state=a=b`). A token with **no** `=` at all → `Ok=false` with `expected k=v, got '<value>'`. Pin both halves with `ArgParserTests` cases.
- Expose a `ParseResult` carrying `Ok`, an error message, the chosen `AuthScheme`, the global output flags, and a per-scheme options object (e.g. `BasicOptions`, `OAuth1Options`, …) holding the raw flag values **including the unresolved `SecretRef` strings** (resolution happens in `Cli`, which owns stdin + the secret store).
- Register `.Example(...)` and `.ComposesWith(...)` entries: the curl pipe and the `digest` recipes for the deliberately-excluded HMAC cases (verify the `digest`/`curl`/`envvault` flags against their `--help` before pinning — `feedback_composes_with_snippets_must_be_verified`).

  Follow the exact `ParseResult`/builder shape used by `src/url` so error formatting and exit codes match the suite.

- [ ] **Step 4: Run + commit**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter ArgParserTests`
Expected: PASS.
```bash
git add src/Winix.MkAuth/AuthScheme.cs src/Winix.MkAuth/ArgParser.cs tests/Winix.MkAuth.Tests/ArgParserTests.cs
git commit -m "feat(mkauth): arg parser with per-scheme subcommand dispatch + validation"
```

---

## Task 11: Cli.Run seam

**Files:**
- Create: `src/Winix.MkAuth/Cli.cs`
- Create: `tests/Winix.MkAuth.Tests/CliTests.cs`

- [ ] **Step 1: Write failing end-to-end Cli tests**

`CliTests.cs`:
```csharp
using System.Text;
using Winix.MkAuth;
using Xunit;

public class CliTests
{
    private static (int code, string outp, string err) Run(string[] args, string stdin = "", MkAuthDeps? deps = null)
    {
        var so = new StringWriter(); var se = new StringWriter();
        int code = Cli.Run(args, so, se, new StringReader(stdin), deps);
        return (code, so.ToString(), se.ToString());
    }

    [Fact]
    public void Bearer_from_stdin_prints_full_header()
    {
        var (code, outp, _) = Run(new[] { "bearer", "--token", "stdin" }, stdin: "tok123\n");
        Assert.Equal(0, code);
        Assert.Equal("Authorization: Bearer tok123", outp.Trim());
    }

    [Fact]
    public void Value_only_prints_bare_value()
    {
        var (_, outp, _) = Run(new[] { "bearer", "--token", "literal:t", "--value-only" });
        Assert.Equal("Bearer t", outp.Trim());
    }

    [Fact]
    public void Json_output_shape()
    {
        var (_, outp, _) = Run(new[] { "bearer", "--token", "literal:t", "--json" });
        Assert.Contains("\"scheme\":\"bearer\"", outp);
        Assert.Contains("\"header_value\":\"Bearer t\"", outp);
    }

    [Fact]
    public void Oauth1_is_deterministic_under_fixed_clock_and_nonce()
    {
        var deps = new MkAuthDeps { Clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(1318622958)), Nonce = new FixedNonce("NONCE") };
        var (code, outp, _) = Run(new[] { "oauth1", "--method", "GET", "--url", "https://x/y?a=1",
            "--consumer-key", "ck", "--consumer-secret", "literal:cs" }, deps: deps);
        Assert.Equal(0, code);
        Assert.Contains("oauth_nonce=\"NONCE\"", outp);
        Assert.Contains("oauth_timestamp=\"1318622958\"", outp);
    }

    [Fact]
    public void Literal_secret_warns_on_stderr()
    {
        var (_, _, err) = Run(new[] { "bearer", "--token", "literal:t" });
        Assert.Contains("literal", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_subcommand_is_usage_error_code()
    {
        var (code, _, _) = Run(new[] { "nope" });
        Assert.Equal(125, code); // ShellKit ExitCode.UsageError
    }

    [Fact]
    public void Show_base_string_goes_to_stderr_in_plain_mode()
    {
        var deps = new MkAuthDeps { Clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(1)), Nonce = new FixedNonce("N") };
        var (_, outp, err) = Run(new[] { "oauth1", "--method", "GET", "--url", "https://x/y",
            "--consumer-key", "k", "--consumer-secret", "literal:s", "--show-base-string" }, deps: deps);
        Assert.StartsWith("Authorization:", outp.Trim());
        Assert.Contains("GET&https%3A", err); // base string on stderr, not polluting stdout
    }

    [Theory]                                            // F1: header-injection guard
    [InlineData("tok\r\nX-Evil: 1")]
    [InlineData("tok\nX-Evil: 1")]
    public void Newline_in_computed_header_is_refused(string token)
    {
        var (code, outp, err) = Run(new[] { "bearer", "--token", "literal:" + token });
        Assert.NotEqual(0, code); // ExitCode.Error — pin the exact value against ShellKit.ExitCode at implementation
        Assert.DoesNotContain("X-Evil", outp); // nothing emitted to stdout
        Assert.Contains("newline", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]                                              // F3: stdin can't be both key and claims body
    public void Jwt_key_stdin_plus_claims_stdin_is_usage_error()
    {
        var (code, _, err) = Run(new[] { "jwt", "--alg", "HS256", "--key", "stdin", "--claims-stdin" }, stdin: "k");
        Assert.Equal(125, code); // ExitCode.UsageError
        Assert.Contains("stdin", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]                                              // F8: PLAINTEXT over non-HTTPS warns but still emits
    public void Oauth1_plaintext_over_http_warns()
    {
        var deps = new MkAuthDeps { Clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(1)), Nonce = new FixedNonce("N") };
        var (code, outp, err) = Run(new[] { "oauth1", "--signature-method", "PLAINTEXT", "--method", "GET",
            "--url", "http://x/y", "--consumer-key", "k", "--consumer-secret", "literal:s" }, deps: deps);
        Assert.Equal(0, code);
        Assert.StartsWith("Authorization:", outp.Trim());
        Assert.Contains("https", err, StringComparison.OrdinalIgnoreCase); // PLAINTEXT-should-be-HTTPS warning
    }

    [Fact]                                              // F5: malformed --url is a clean error, not an SR-key leak
    public void Oauth1_malformed_url_is_clean_error()
    {
        var (code, _, err) = Run(new[] { "oauth1", "--method", "GET", "--url", "not-a-url",
            "--consumer-key", "k", "--consumer-secret", "literal:s" });
        Assert.NotEqual(0, code);
        Assert.False(string.IsNullOrWhiteSpace(err));
        Assert.DoesNotContain("_Name", err); // no bare SR resource key (e.g. Arg_ParamName_Name)
    }

    [Fact]                                              // G2: typed --exp wins over a string --claim exp= AND stays numeric
    public void Explicit_exp_overrides_claim_and_stays_a_number()
    {
        var deps = new MkAuthDeps { Clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(1000)) };
        // --claim exp=123 (string) and --exp 60s (now+60 = 1060 as a number). Explicit wins, as a number.
        var (code, outp, _) = Run(new[] { "jwt", "--alg", "HS256", "--key", "literal:k",
            "--claim", "exp=123", "--exp", "60s", "--value-only" }, deps: deps);
        Assert.Equal(0, code);
        string payload = System.Text.Encoding.UTF8.GetString(B64UrlDecode(outp.Trim().Split('.')[1]));
        Assert.Contains("\"exp\":1060", payload);          // numeric, from --exp
        Assert.DoesNotContain("\"exp\":\"123\"", payload); // string --claim did not win and did not leak
    }

    private static byte[] B64UrlDecode(string s)
    {
        string b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter CliTests`
Expected: FAIL.

- [ ] **Step 3: Implement `Cli`**

`src/Winix.MkAuth/Cli.cs` — responsibilities:
- `public static int Run(string[] args, TextWriter stdout, TextWriter stderr, TextReader stdin, MkAuthDeps? deps = null)`.
- `deps ??= new MkAuthDeps();`.
- Call `ArgParser.Parse(args)`. If `IsHandled` (`--help/--version/--describe`) → write and return `ExitCode.Success`. If `!Ok` → write the error to stderr, return `ExitCode.UsageError` (125).
- Build a `SecretResolver(deps.SecretStore ?? <real ISecretStore factory>, stdin)`. **Confirm the real ISecretStore construction** from `envvault`/`protect` usage; for non-vault refs the store is never touched, so a null-object store is acceptable when no `vault:` ref is present.
- Resolve the scheme's secret refs via the resolver, passing a `warn` callback that writes `mkauth: warning: …` to stderr. Catch `FormatException`/`InvalidOperationException`/`IOException` from resolution → stderr message via `SafeError.Describe`, return `ExitCode.Error`.
- Dispatch to the builder/signer:
  - `basic` → `BasicAuthBuilder.Build(user, password)`.
  - `bearer` → `BearerAuthBuilder.Build(token)`.
  - `oauth1` → build `OAuth1Request` (timestamp = explicit flag ?? `deps.Clock.UtcNow.ToUnixTimeSeconds()`; nonce = explicit flag ?? `deps.Nonce.NextNonce()`); warn if `PLAINTEXT` and URL scheme != https; `OAuth1Signer.Sign(...)`.
  - `jwt` → **F3 stdin arbiter:** claims-from-stdin is requested by the explicit flag `--claims-stdin` (NOT implicit-if-piped, so the arbiter is unambiguous). stdin can supply EITHER the key (`--key stdin`) OR the claims body (`--claims-stdin`), never both — if both are requested, return `ExitCode.UsageError` with `stdin can supply either the key or the claims body, not both`. Otherwise: merge `--claim`/`--claims-file`/`--claims-stdin` claims (bare `--claim k=v` → string; **F2:** `--claim-num k=v`, `--exp`, `--iat`, `--nbf` inject a `long` so they serialize as JSON numbers; `--claim-json k=v` parses `v` as a `JsonNode`); apply `--iss/--sub/--aud/--exp/--iat/--nbf` using `deps.Clock` + ShellKit `DurationParser` (`exp`/`nbf` = `now + DURATION` as Unix seconds `long`; `iat` = `now` as `long`); `JwtSigner.Sign(...)`. Claim precedence: an explicit `--exp/--iat/--nbf/--iss/--sub/--aud` overrides any same-named `--claim` (document this; pin with a test).
  - `azure-storage` → x-ms-date = explicit flag ?? `deps.Clock.UtcNow.ToString("R")`; `AzureStorageSigner.Sign(...)`.
- **Header-injection guard (F1):** BEFORE any output, validate that the computed `HeaderResult.HeaderName` and `HeaderResult.HeaderValue` contain no `\r` or `\n`. If either does, write `mkauth: refusing to emit a header containing a newline (possible header injection)` to stderr and return `ExitCode.Error` — do NOT print the header. This protects the dominant `curl -H "$(mkauth …)"` path from a resolved secret / token / claim / realm that contains an embedded CRLF (e.g. a multi-line `file:` secret). Applies to both plain and `--json` output.
- Output: if `--json` → `Formatting.Json(...)` to stdout. Else → `Formatting.Plain(..., valueOnly)` to stdout; if `--show-base-string` and a base string exists → write it to stderr.
- Wrap the whole body so a final `catch (IOException)` (pipe close) returns `ExitCode.Success` and a catch-all returns `ExitCode.Error` with `SafeError.Describe(ex)` — mirror `Winix.MkSecret.Cli`.

- [ ] **Step 4: Run Cli tests**

Run: `dotnet test tests/Winix.MkAuth.Tests --filter CliTests`
Expected: PASS.

- [ ] **Step 5: Full library test run + commit**

Run: `dotnet test tests/Winix.MkAuth.Tests`
Expected: all PASS.
```bash
git add src/Winix.MkAuth/Cli.cs tests/Winix.MkAuth.Tests/CliTests.cs
git commit -m "feat(mkauth): Cli.Run seam (resolve secrets, dispatch, output routing)"
```

---

## Task 12: Console app (Program.cs)

**Files:**
- Create: `src/mkauth/Program.cs`

- [ ] **Step 1: Implement the thin shim**

`src/mkauth/Program.cs` — copy `src/mksecret/Program.cs`'s structure (proper `namespace`/`class Program`/`static int Main`, UTF-8 console encoding set up front per the suite's Windows-encoding convention), calling:
```csharp
return Winix.MkAuth.Cli.Run(args, Console.Out, Console.Error, Console.In);
```

- [ ] **Step 2: Build + manual smoke**

Run: `dotnet run --project src/mkauth -- bearer --token literal:hello`
Expected stdout: `Authorization: Bearer hello`; stderr carries the literal-secret warning.

Run: `dotnet run --project src/mkauth -- oauth1 --method GET --url https://api.example.com/x --consumer-key k --consumer-secret literal:s`
Expected: an `Authorization: OAuth …` line on stdout.

- [ ] **Step 3: Commit**

```bash
git add src/mkauth/Program.cs
git commit -m "feat(mkauth): console app entry point"
```

---

## Task 13: AOT publish verification

- [ ] **Step 1: Publish native for the host RID**

Run: `dotnet publish src/mkauth/mkauth.csproj -c Release -r win-x64`
Expected: succeeds with **zero trim/AOT warnings**. If `JsonNode` (JWT) or `RSA/ECDsa ImportFromPem` produce warnings, resolve per the Task-7 spike before proceeding.

- [ ] **Step 2: Smoke the native binary**

Run the published `mkauth.exe`: `bearer --token literal:x`, `oauth1 …`, `jwt --alg HS256 --key literal:k --claim sub=1`.
Expected: correct headers; binary size in the ~1–2 MB sibling range.

- [ ] **Step 3: Commit any csproj trim adjustments (if needed)**

```bash
git add src/mkauth/mkauth.csproj
git commit -m "build(mkauth): AOT publish clean"
```

---

## Task 14: Docs + distribution wiring

**Files:**
- Create: `src/mkauth/README.md`, `src/mkauth/man/man1/mkauth.1`, `src/mkauth/CHANGELOG.md`, `docs/ai/mkauth.md`, `bucket/mkauth.json`
- Create: `tests/.../run-smokes.sh` fixture location per the suite (see an existing tool's smoke fixture)
- Modify: `llms.txt`, `CLAUDE.md`, `.github/workflows/release.yml`, `.github/workflows/post-publish.yml`, `.github/workflows/manual-smoke.yml`

- [ ] **Step 1: README** — follow the sibling pattern (description, install [scoop/nuget/winget], usage/examples per subcommand, options tables, exit codes, colour/NO_COLOR, the `curl -H "$(mkauth …)"` composition, and a **"For other HMAC schemes, use `digest`"** section with verified recipes). **In the secret-references table, document (F6/F7):** `vault:NS/KEY` splits on the **first** `/` — a namespace cannot contain `/`, but a key may; and `file:`/`stdin` secrets have a single trailing `\r\n`/`\n` run trimmed, while all other bytes (including trailing spaces) are preserved verbatim. Mirror both notes in the `SecretRef`/`SecretResolver` XML doc comments.

- [ ] **Step 2: man page** `src/mkauth/man/man1/mkauth.1` (groff) — mirror an existing tool's `.1`; add the `<Content Include="man\man1\mkauth.1" …>` line to the csproj (already added in Task 1 Step 2 — verify present).

- [ ] **Step 3: `docs/ai/mkauth.md`** agent guide + add the tool to `llms.txt`.

- [ ] **Step 4: `bucket/mkauth.json`** scoop manifest — copy `bucket/mksecret.json`, retarget name/binary/description. **Do NOT edit `bucket/winix.json`.**

- [ ] **Step 5: `release.yml`** — add `dotnet publish` per `matrix.rid`, `dotnet pack`, per-tool zip steps (Linux/macOS + Windows), combined-zip `Copy-Item`, and the `tools: { … }` map entry (in the symbol-splitting loop).

- [ ] **Step 6: `post-publish.yml`** — add `update_manifest bucket/mkauth.json …` and `generate_manifests "mkauth" "MkAuth" "…desc…" "oauth,jwt,http,authorization,curl"`.

- [ ] **Step 7: `manual-smoke.yml`** — add `mkauth` to the tool list + `runner_for` map + sed retarget rule; author a `run-smokes.sh` fixture deriving cases from the README option/exit-code surface (include a `curl -H "$(mkauth oauth1 …)"` real-request case as the wire-correctness smoke).

- [ ] **Step 8: `CLAUDE.md`** — add to project layout, NuGet package IDs list, scoop manifests list.

- [ ] **Step 9: `CHANGELOG.md`** — `- Initial release.` (single entry; first stable tag).

- [ ] **Step 10: Build whole solution + commit**

Run: `dotnet build Winix.sln`
Expected: succeeds.
```bash
git add src/mkauth bucket/mkauth.json docs/ai/mkauth.md llms.txt CLAUDE.md .github/workflows
git commit -m "docs(mkauth): README, man, agent guide, scoop + release pipeline wiring"
```

---

## Task 15: Ship-gate verification

- [ ] **Step 1: Full solution test run**

Run: `dotnet test Winix.sln`
Expected: zero failures across the whole suite.

- [ ] **Step 2: Real-counterpart wire-correctness smoke (the protocol-fake ship gate)**

Run a real `curl -H "$(mkauth oauth1 …)"` against an endpoint that validates the OAuth1 signature (or capture the request and diff the `Authorization` header against a reference signer). Record the result. This is the explicit ship gate the protocol-fake rule requires — protocol-fake tests prove shape, not wire correctness.

- [ ] **Step 3: Manual CLI smoke (new-tool first-pass)**

Exercise every subcommand + `--json`, `--value-only`, `--show-base-string`, `--help`, `--version`, `--describe` per `feedback_cli_auto_defaults`. Confirm `--describe` composes-with snippets run against real `curl`/`digest`/`envvault`.

- [ ] **Step 4: Final commit**

```bash
git commit --allow-empty -m "test(mkauth): ship-gate verification complete (suite green, wire-correctness smoke passed)"
```

---

## Self-review (run by the plan author)

- **Spec coverage:** basic/bearer/oauth1/jwt/azure-storage subcommands (Tasks 5–8); secret refs incl. `vault:` (Task 2); secret-ref `literal:` warning + stdin single-use (Tasks 2, 11); output default/value-only/json/show-base-string (Tasks 9, 11); per-scheme dispatch + validation (Task 10); Cli seam with injectable clock/nonce/store (Tasks 4, 11); AOT (Task 13); full new-tool checklist (Task 14); wire-correctness ship gate (Task 15). OAuth2 runner correctly **absent** (separate spec).
- **Spike gates recorded:** JWT AOT claim serialization (Task 7 preamble); Azure SharedKey reference capture + defer criterion (Task 8 preamble).
- **No `Assert.True(true)` placeholders:** the two vector tests (OAuth1, Azure) ship with **empty** request objects specifically so they fail until reconciled to the cited reference — they cannot pass spuriously. Flagged "verify at implementation" per the plan-assumptions rule rather than baking unverified expected bytes.
- **Type consistency:** `HeaderResult(HeaderName, HeaderValue, BaseString?)`, `SecretRef`/`SecretRefKind`, `OAuth1Request`/`OAuth1Result`, `JwtRequest`/`JwtResult`, `AzureStorageRequest`/`AzureStorageResult`, `MkAuthDeps{Clock,Nonce,SecretStore}`, `Cli.Run(args,stdout,stderr,stdin,deps?)` used consistently across tasks.
- **External-API verification flagged:** `ISecretStore` signature (File-Structure note + Task 2), `Winix.Codec` URL-safe base64 (Task 3), ShellKit parser/JSON-helper shapes (Tasks 9, 10) — all marked "confirm against source," not assumed.

## Adversarial-review integration (2026-06-03)

A fresh-subagent `adversarial-plan-review` pass produced 8 findings; all integrated into this plan:

| ID | Bucket | Where integrated |
|----|--------|------------------|
| F1 | Plan blocker | CRLF header-injection guard in `Cli` before output + `CliTests.Newline_in_computed_header_is_refused` (Task 11). |
| F2 | Plan blocker | Numeric NumericDate claims serialize as JSON numbers — `JwtSigner.ToJsonNode` type-switch + spike check + `JwtSignerTests.Numeric_exp_serializes_as_json_number` + Cli injects `--exp/--iat/--nbf/--claim-num` as `long` (Tasks 7, 11). |
| F3 | Plan blocker | `jwt` stdin arbiter (`--key stdin` xor `--claims-stdin`) → usage error + `CliTests.Jwt_key_stdin_plus_claims_stdin_is_usage_error` (Task 11; design table updated). |
| F4 | Test gap | `JwtSigner.RequireKeyForAlg` guard + two mismatch tests (Task 7). |
| F5 | Test gap | Malformed `--url` clean-error test + no-`=` `k=v` flag validation (Tasks 10, 11). |
| F6 | Explicit defer | `vault:NS/KEY` first-slash split documented in README + XML docs (Task 14; design updated). |
| F7 | Explicit defer | `file:`/`stdin` trailing-trim semantics documented in README + XML docs (Task 14; design updated). |
| F8 | Test gap | PLAINTEXT-over-non-HTTPS warning test (Task 11). |

**Confirming pass (2026-06-03, second/final — review caps at two):** F1–F4, F6–F8 confirmed CLOSED;
F5 was PARTIALLY closed (malformed-url test present; the no-`=` validation test was described but not
written). Two new test gaps, no new blockers — all integrated:

| ID | Bucket | Where integrated |
|----|--------|------------------|
| G1 | Test gap (closes F5) | `k=v` flags split on the **first** `=` (value may contain `=`); no-`=` token → usage error. `ArgParserTests.Param_value_may_contain_equals` + `Param_without_equals_is_usage_error` (Task 10). |
| G2 | Test gap | Explicit `--exp` overrides a string `--claim exp=` AND stays a JSON number (guards against F2 re-entering via claim precedence). `CliTests.Explicit_exp_overrides_claim_and_stays_a_number` (Task 11). |
