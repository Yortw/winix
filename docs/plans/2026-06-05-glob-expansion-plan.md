# Windows Glob Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Opt-in Windows-only glob expansion (`*`, `?`) of positional args in `Yort.ShellKit.CommandLineParser`, honouring raw-command-line quoting, adopted by digest, squeeze, trash, less, treex, files.

**Architecture:** Three units in ShellKit — `RawCommandLineTokenizer` (pure CRT-rules splitter with per-token `WasQuoted`), `GlobArgExpander` (segment-walking enumerator filtering via the existing `GlobMatcher`, injectable FS seam), and a thin hook in `CommandLineParser.Parse()` behind a fluent `ExpandGlobPositionals()` opt-in. A tiny `ArgvEcho` test-oracle console app pins our tokenizer to the .NET runtime's actual argv splitting.

**Tech Stack:** .NET 10, xUnit (+ Xunit.SkippableFact for Windows-gated tests), existing `GlobMatcher`/`JsonHelper`. No new packages in shipping code.

**Design doc:** `docs/plans/2026-06-05-glob-expansion-design.md` — **ADR:** `docs/plans/2026-06-05-glob-expansion-adr.md`. Read both before starting.

**Branch:** all work on `feature/glob-expansion` (already created off `release/v0.4.0`).

**Conventions that apply to every task** (from CLAUDE.md — repeated here so no task relies on ambient knowledge):
- Full braces always; nullable enabled; warnings as errors; XML doc comments on all public/internal members.
- TDD: write the failing test, see it fail, implement, see it pass, commit.
- Never `Directory.GetFiles(dir, pattern)` — OS-level pattern matching has the 8.3 short-name trap.
- Platform-gated tests use `SkippableFact` + `Skip.IfNot(OperatingSystem.IsWindows(), "...")` plus a redundant `if (!OperatingSystem.IsWindows()) { return; }` (deliberate, for CA1416 — comment it).
- Run tests with: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj` (or the named tool's test project).

---

## File map

| File | Action | Responsibility |
|---|---|---|
| `tests/tools/ArgvEcho/ArgvEcho.csproj` + `Program.cs` | Create | argv-echo oracle console app (test asset, added to solution, never shipped) |
| `src/Yort.ShellKit/RawCommandLineTokenizer.cs` | Create | Raw command line → tokens + WasQuoted |
| `src/Yort.ShellKit/GlobArgExpander.cs` | Create | One arg → expansion result (segment walk over FS seam) |
| `src/Yort.ShellKit/CommandLineParser.cs` | Modify | `ExpandGlobPositionals()` opt-in; positional argv-index tracking; expansion hook; help section; describe field |
| `tests/Yort.ShellKit.Tests/RawCommandLineTokenizerTests.cs` | Create | Pure vector tests |
| `tests/Yort.ShellKit.Tests/RawCommandLineOracleTests_Windows.cs` | Create | Oracle alignment (SkippableFact) |
| `tests/Yort.ShellKit.Tests/GlobArgExpanderTests.cs` | Create | Engine tests (fake FS) |
| `tests/Yort.ShellKit.Tests/GlobArgExpanderRealFsTests_Windows.cs` | Create | Real-FS integration (SkippableFact) |
| `tests/Yort.ShellKit.Tests/GlobExpansionParserTests.cs` | Create | Parser hook tests (seams) |
| `src/Winix.Digest/ArgParser.cs:370` ff. | Modify | One-line opt-in (same for the 5 below) |
| `src/Winix.Squeeze/Cli.cs:234` ff. | Modify | opt-in |
| `src/Winix.Trash/ArgParser.cs:154` ff. | Modify | opt-in |
| `src/Winix.Less/Cli.cs:230` ff. | Modify | opt-in |
| `src/Winix.TreeX/Cli.cs:442` ff. | Modify | opt-in |
| `src/Winix.Files/Cli.cs:402` ff. | Modify | opt-in |
| Per adopter: `src/{tool}/README.md`, `src/{tool}/man/man1/{tool}.1`, `docs/ai/{tool}.md` | Modify | Wildcards documentation |

---

### Task 1: ArgvEcho oracle project

The tokenizer must match the .NET runtime's own argv splitting exactly. Docs describe the CRT rules, but the `""`-inside-quotes rule has version history — so we pin behaviour empirically with a child process that echoes its parsed argv. This is the oracle for Task 3.

**Files:**
- Create: `tests/tools/ArgvEcho/ArgvEcho.csproj`
- Create: `tests/tools/ArgvEcho/Program.cs`
- Modify: `Winix.sln` (add project)
- Modify: `tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj` (ProjectReference)

- [ ] **Step 1: Create the project files**

`tests/tools/ArgvEcho/ArgvEcho.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- Test asset only: never packed, never published, no analyzers beyond defaults. -->
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
```

`tests/tools/ArgvEcho/Program.cs`:
```csharp
namespace ArgvEcho;

/// <summary>
/// Test oracle: prints each parsed argv element on its own line. Used by
/// Yort.ShellKit.Tests to pin RawCommandLineTokenizer to the .NET runtime's
/// actual command-line splitting. Vectors must not contain newlines.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        foreach (string arg in args)
        {
            System.Console.WriteLine(arg);
        }

        return 0;
    }
}
```

- [ ] **Step 2: Add to solution and reference from the test project**

Run: `dotnet sln Winix.sln add tests/tools/ArgvEcho/ArgvEcho.csproj`

In `tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj`, add alongside the existing ProjectReference(s):
```xml
<ProjectReference Include="..\tools\ArgvEcho\ArgvEcho.csproj" />
```
The .NET SDK copies the referenced console app's apphost (`ArgvEcho.exe`) into the test output directory, so tests locate it via `Path.Combine(AppContext.BaseDirectory, "ArgvEcho.exe")`. **Verify after build** that the exe is present in `tests/Yort.ShellKit.Tests/bin/Debug/net10.0/`; if the SDK only copied the dll, instead set `<ReferenceOutputAssembly>false</ReferenceOutputAssembly>` + `<OutputItemType>None</OutputItemType>` on the reference and locate the exe via `..\..\..\..\tools\ArgvEcho\bin\$(Configuration)\net10.0\ArgvEcho.exe` — record whichever was needed in the commit message.

- [ ] **Step 3: Build and verify**

Run: `dotnet build tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj`
Expected: success; `ArgvEcho.exe` in the test output dir (on Windows).

- [ ] **Step 4: Commit**

```bash
git add tests/tools/ArgvEcho Winix.sln tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj
git commit -m "test(shellkit): add ArgvEcho oracle for raw command-line tokenizer pinning"
```

---

### Task 2: RawCommandLineTokenizer (pure vectors, TDD)

**Files:**
- Create: `tests/Yort.ShellKit.Tests/RawCommandLineTokenizerTests.cs`
- Create: `src/Yort.ShellKit/RawCommandLineTokenizer.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Yort.ShellKit.Tests/RawCommandLineTokenizerTests.cs`:
```csharp
using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class RawCommandLineTokenizerTests
{
    private static (string Text, bool WasQuoted)[] Tok(string raw)
        => RawCommandLineTokenizer.Tokenize(raw).Select(t => (t.Text, t.WasQuoted)).ToArray();

    [Fact]
    public void Argv0_Unquoted_EndsAtWhitespace()
    {
        var t = Tok(@"C:\bin\tool.exe a b");
        Assert.Equal((@"C:\bin\tool.exe", false), t[0]);
        Assert.Equal(3, t.Length);
    }

    [Fact]
    public void Argv0_Quoted_NoEscapeProcessing_EndsAtQuote()
    {
        // argv[0] rule is simpler: backslashes are literal, token runs to the closing quote.
        var t = Tok("\"C:\\Program Files\\tool.exe\" x");
        Assert.Equal(("C:\\Program Files\\tool.exe", true), t[0]);
        Assert.Equal(("x", false), t[1]);
    }

    [Fact]
    public void PlainArgs_SplitOnSpacesAndTabs()
    {
        var t = Tok("t.exe a\tb  c");
        Assert.Equal(new[] { ("t.exe", false), ("a", false), ("b", false), ("c", false) }, t);
    }

    [Fact]
    public void QuotedArg_PreservesSpaces_FlagsQuoted()
    {
        var t = Tok("t.exe \"a b\" c");
        Assert.Equal(("a b", true), t[1]);
        Assert.Equal(("c", false), t[2]);
    }

    [Fact]
    public void PartiallyQuotedArg_IsQuoted()
    {
        // foo"*"bar — any quoted region marks the whole token quoted (suppression-safe).
        var t = Tok("t.exe foo\"*\"bar");
        Assert.Equal(("foo*bar", true), t[1]);
    }

    [Fact]
    public void BackslashesNotBeforeQuote_AreLiteral()
    {
        var t = Tok(@"t.exe a\\\b dir\");
        Assert.Equal((@"a\\\b", false), t[1]);
        Assert.Equal((@"dir\", false), t[2]);
    }

    [Fact]
    public void OddBackslashesBeforeQuote_EscapeTheQuote()
    {
        // 2n+1 backslashes + " → n backslashes + literal quote.  a\"b stays one token.
        var t = Tok("t.exe a\\\"b");
        Assert.Equal(("a\"b", false), t[1]);
    }

    [Fact]
    public void EvenBackslashesBeforeQuote_QuoteToggles()
    {
        // 2n backslashes + " → n backslashes, quote is a delimiter:  a\\"b c" → a\b c
        var t = Tok("t.exe a\\\\\"b c\"");
        Assert.Equal(("a\\b c", true), t[1]);
    }

    [Fact]
    public void EmptyQuotedArg_YieldsEmptyToken()
    {
        var t = Tok("t.exe \"\" x");
        Assert.Equal(("", true), t[1]);
        Assert.Equal(("x", false), t[2]);
    }

    [Fact]
    public void DoubledQuoteInsideQuotes_EmitsLiteralQuote()
    {
        // Post-2008 CRT rule — VERIFIED against the runtime by the Task 3 oracle test.
        var t = Tok("t.exe \"a\"\"b\"");
        Assert.Equal(("a\"b", true), t[1]);
    }

    [Fact]
    public void UnterminatedQuote_RunsToEnd()
    {
        var t = Tok("t.exe \"open ended");
        Assert.Equal(("open ended", true), t[1]);
        Assert.Equal(2, t.Length);
    }

    [Fact]
    public void TrailingWhitespace_NoEmptyToken()
    {
        var t = Tok("t.exe a  ");
        Assert.Equal(2, t.Length);
    }

    [Fact]
    public void GlobUseCase_QuotedVsUnquoted()
    {
        var t = Tok("digest.exe *.txt \"*.txt\"");
        Assert.Equal(("*.txt", false), t[1]);
        Assert.Equal(("*.txt", true), t[2]);
    }

    [Fact]
    public void EmptyRawLine_ReturnsEmpty()
    {
        Assert.Empty(RawCommandLineTokenizer.Tokenize(""));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --filter RawCommandLineTokenizerTests`
Expected: FAIL — `RawCommandLineTokenizer` does not exist (compile error).

- [ ] **Step 3: Implement**

`src/Yort.ShellKit/RawCommandLineTokenizer.cs`:
```csharp
using System.Text;

namespace Yort.ShellKit;

/// <summary>
/// A token parsed from a raw Windows command line: the text as the CRT rules resolve it,
/// plus whether any part of the token was enclosed in double quotes.
/// </summary>
/// <param name="Text">The parsed token text (quotes and escapes resolved).</param>
/// <param name="WasQuoted">True if any portion of the token was inside double quotes.</param>
public readonly record struct CommandLineToken(string Text, bool WasQuoted);

/// <summary>
/// Splits a raw Windows command line (as returned by <c>Environment.CommandLine</c> /
/// <c>GetCommandLineW</c>) into tokens using the same rules the .NET runtime uses to build
/// <c>Main</c>'s <c>args[]</c>, additionally tracking per-token quoting. Quoting information
/// is what lets glob expansion honour <c>tool "*.txt"</c> as a literal in cmd.exe.
/// </summary>
/// <remarks>
/// Rules implemented (CRT post-2008 / <c>CommandLineToArgvW</c>, mirrored by the runtime):
/// argv[0] has no escape processing (quoted → runs to closing quote; else to whitespace);
/// for later args, 2n backslashes before a quote emit n backslashes and the quote toggles
/// quoted mode, 2n+1 emit n backslashes plus a literal quote, backslashes not before a
/// quote are literal, and a doubled quote inside a quoted region emits one literal quote.
/// The doubled-quote rule is pinned empirically by RawCommandLineOracleTests_Windows.
/// </remarks>
public static class RawCommandLineTokenizer
{
    /// <summary>Tokenizes a raw command line. Returns an empty list for an empty input.</summary>
    /// <param name="rawCommandLine">The raw command line, including the program path (argv[0]).</param>
    public static IReadOnlyList<CommandLineToken> Tokenize(string rawCommandLine)
    {
        var tokens = new List<CommandLineToken>();
        int i = 0;
        int n = rawCommandLine.Length;

        if (n == 0)
        {
            return tokens;
        }

        // argv[0]: simpler rule — no backslash escaping.
        {
            var sb = new StringBuilder();
            bool quoted = false;
            if (rawCommandLine[i] == '"')
            {
                quoted = true;
                i++;
                while (i < n && rawCommandLine[i] != '"')
                {
                    sb.Append(rawCommandLine[i]);
                    i++;
                }
                if (i < n)
                {
                    i++; // skip closing quote
                }
            }
            else
            {
                while (i < n && rawCommandLine[i] != ' ' && rawCommandLine[i] != '\t')
                {
                    sb.Append(rawCommandLine[i]);
                    i++;
                }
            }
            tokens.Add(new CommandLineToken(sb.ToString(), quoted));
        }

        while (true)
        {
            while (i < n && (rawCommandLine[i] == ' ' || rawCommandLine[i] == '\t'))
            {
                i++;
            }
            if (i >= n)
            {
                break;
            }

            var sb = new StringBuilder();
            bool sawQuote = false;
            bool inQuotes = false;
            while (i < n)
            {
                char c = rawCommandLine[i];

                if (c == '\\')
                {
                    int backslashes = 0;
                    while (i < n && rawCommandLine[i] == '\\')
                    {
                        backslashes++;
                        i++;
                    }
                    if (i < n && rawCommandLine[i] == '"')
                    {
                        sb.Append('\\', backslashes / 2);
                        if (backslashes % 2 == 1)
                        {
                            sb.Append('"'); // escaped literal quote; quote char consumed
                            i++;
                        }
                        // even count: quote left in place — handled as delimiter next iteration
                    }
                    else
                    {
                        sb.Append('\\', backslashes);
                    }
                    continue;
                }

                if (c == '"')
                {
                    sawQuote = true;
                    if (inQuotes && i + 1 < n && rawCommandLine[i + 1] == '"')
                    {
                        sb.Append('"'); // "" inside quotes → literal quote (oracle-pinned)
                        i += 2;
                        continue;
                    }
                    inQuotes = !inQuotes;
                    i++;
                    continue;
                }

                if (!inQuotes && (c == ' ' || c == '\t'))
                {
                    break;
                }

                sb.Append(c);
                i++;
            }
            tokens.Add(new CommandLineToken(sb.ToString(), sawQuote));
        }

        return tokens;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --filter RawCommandLineTokenizerTests`
Expected: PASS (14 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Yort.ShellKit/RawCommandLineTokenizer.cs tests/Yort.ShellKit.Tests/RawCommandLineTokenizerTests.cs
git commit -m "feat(shellkit): raw command-line tokenizer with per-token quoting (CRT rules)"
```

---

### Task 3: Oracle alignment tests (Windows)

Pins the tokenizer to the runtime's actual splitting. If any oracle case fails, the **tokenizer** (and the corresponding Task 2 vector) is what gets fixed — the oracle is ground truth.

**Files:**
- Create: `tests/Yort.ShellKit.Tests/RawCommandLineOracleTests_Windows.cs`

- [ ] **Step 1: Write the tests**

```csharp
using System.Diagnostics;
using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class RawCommandLineOracleTests_Windows
{
    // Raw argument tails fed verbatim to ArgvEcho. No newlines (oracle prints line-per-arg).
    public static TheoryData<string> RawTails() => new()
    {
        "plain a b",
        "\"quoted arg\" x",
        "foo\"*\"bar",
        "a\\\\\\b dir\\",
        "a\\\"b",
        "a\\\\\"b c\" d",
        "\"\" x",
        "\"a\"\"b\"",
        "\"unterminated",
        "*.txt \"*.txt\"",
        "mixed\ttabs and  spaces",
    };

    [SkippableTheory]
    [MemberData(nameof(RawTails))]
    public void Tokenizer_Matches_DotnetArgvSplitting(string rawTail)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Raw command line semantics are Windows-only");
        if (!OperatingSystem.IsWindows()) { return; } // deliberate redundancy for CA1416

        string exe = Path.Combine(AppContext.BaseDirectory, "ArgvEcho.exe");
        Assert.True(File.Exists(exe), $"oracle missing: {exe}");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            // Deliberate use of the string Arguments property (not ArgumentList): the whole
            // point is to hand the child an exact raw tail so its argv shows how the runtime
            // splits it. ArgumentList would re-quote and defeat the oracle.
            Arguments = rawTail,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        using var proc = Process.Start(psi)!;
        string output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        string[] childArgv = output.Length == 0
            ? Array.Empty<string>()
            : output.TrimEnd('\r', '\n').Split(Environment.NewLine);

        // The child's raw command line is "<exe>" + " " + rawTail (built by Process.Start).
        var tokens = RawCommandLineTokenizer.Tokenize($"\"{exe}\" {rawTail}");
        string[] ourArgv = tokens.Skip(1).Select(t => t.Text).ToArray();

        Assert.Equal(childArgv, ourArgv);
    }

    [SkippableFact]
    public void Tokenizer_Matches_CurrentProcess()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Raw command line semantics are Windows-only");
        if (!OperatingSystem.IsWindows()) { return; } // deliberate redundancy for CA1416

        // The test host's own command line is a free real-world vector.
        var tokens = RawCommandLineTokenizer.Tokenize(Environment.CommandLine);
        string[] runtimeArgv = Environment.GetCommandLineArgs();
        Assert.Equal(runtimeArgv, tokens.Select(t => t.Text).ToArray());
    }
}
```

If `Xunit.SkippableFact` is not yet referenced by `Yort.ShellKit.Tests.csproj`, add the same package reference used by `tests/Winix.EnvVault.Tests` (check its csproj for the exact version).

- [ ] **Step 2: Run on Windows**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --filter RawCommandLineOracleTests`
Expected: PASS (12 results). **If any theory case fails: the oracle is right.** Diff the child argv against ours, fix `Tokenize`, update the matching Task 2 vector, re-run both filters.

- [ ] **Step 3: Commit**

```bash
git add tests/Yort.ShellKit.Tests/RawCommandLineOracleTests_Windows.cs tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj
git commit -m "test(shellkit): pin tokenizer to runtime argv splitting via ArgvEcho oracle"
```

---

### Task 4: GlobArgExpander (engine, fake FS, TDD)

**Files:**
- Create: `tests/Yort.ShellKit.Tests/GlobArgExpanderTests.cs`
- Create: `src/Yort.ShellKit/GlobArgExpander.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class GlobArgExpanderTests
{
    // Fake FS: dir path → entries. Keys are the exact strings the expander passes
    // ("." for the current dir; otherwise the verbatim candidate path).
    private static GlobArgExpander Fake(Dictionary<string, GlobArgExpander.FsEntry[]> fs)
        => new(dir => fs.TryGetValue(dir, out var e) ? new List<GlobArgExpander.FsEntry>(e) : new List<GlobArgExpander.FsEntry>());

    private static GlobArgExpander.FsEntry F(string name) => new(name, IsDirectory: false);
    private static GlobArgExpander.FsEntry D(string name) => new(name, IsDirectory: true);

    [Fact]
    public void NoMetachars_NotAPattern()
    {
        var x = Fake(new() { ["."] = new[] { F("a.txt") } });
        Assert.Equal(GlobExpansionKind.NotAPattern, x.Expand("a.txt").Kind);
        Assert.Equal(GlobExpansionKind.NotAPattern, x.Expand("report[1].txt").Kind); // [...] is literal
        Assert.Equal(GlobExpansionKind.NotAPattern, x.Expand("-").Kind);
    }

    [Fact]
    public void DoubleStar_Unsupported()
    {
        var x = Fake(new());
        Assert.Equal(GlobExpansionKind.UnsupportedRecursive, x.Expand("**/*.cs").Kind);
        Assert.Equal(GlobExpansionKind.UnsupportedRecursive, x.Expand("a**b").Kind);
    }

    [Fact]
    public void Star_MatchesSorted()
    {
        var x = Fake(new() { ["."] = new[] { F("b.txt"), F("a.TXT"), F("c.log") } });
        var r = x.Expand("*.txt");
        Assert.Equal(GlobExpansionKind.Expanded, r.Kind);
        Assert.Equal(new[] { "a.TXT", "b.txt" }, r.Matches); // case-insensitive match, ordinal-ci sort
    }

    [Fact]
    public void QuestionMark_ExactlyOneChar()
    {
        var x = Fake(new() { ["."] = new[] { F("a1.txt"), F("a12.txt") } });
        var r = x.Expand("a?.txt");
        Assert.Equal(new[] { "a1.txt" }, r.Matches);
    }

    [Fact]
    public void NoMatch_ReturnsNoMatch()
    {
        var x = Fake(new() { ["."] = new[] { F("a.txt") } });
        Assert.Equal(GlobExpansionKind.NoMatch, x.Expand("*.zip").Kind);
    }

    [Fact]
    public void DotfileRule_StarSkipsLeadingDot_DotPatternMatches()
    {
        var x = Fake(new() { ["."] = new[] { F(".hidden"), F("shown") } });
        Assert.Equal(new[] { "shown" }, x.Expand("*").Matches);
        Assert.Equal(new[] { ".hidden" }, x.Expand(".*").Matches);
        Assert.Equal(GlobExpansionKind.NoMatch, x.Expand("?hidden").Kind); // ? doesn't match the dot either
    }

    [Fact]
    public void FinalSegment_MatchesFilesAndDirectories()
    {
        var x = Fake(new() { ["."] = new[] { F("a.x"), D("b.x") } });
        Assert.Equal(new[] { "a.x", "b.x" }, x.Expand("*.x").Matches);
    }

    [Fact]
    public void TrailingSeparator_DirsOnly_SeparatorPreserved()
    {
        var x = Fake(new() { ["."] = new[] { F("a.x"), D("b.x") } });
        Assert.Equal(new[] { "b.x\\" }, x.Expand("*.x\\").Matches);
        Assert.Equal(new[] { "b.x/" }, x.Expand("*.x/").Matches);
    }

    [Fact]
    public void LiteralPrefix_KeptVerbatim_TypedSeparatorReused()
    {
        var x = Fake(new() { ["src"] = new[] { F("a.cs"), F("b.cs") } });
        Assert.Equal(new[] { "src\\a.cs", "src\\b.cs" }, x.Expand("src\\*.cs").Matches);
        Assert.Equal(new[] { "src/a.cs", "src/b.cs" }, x.Expand("src/*.cs").Matches);
    }

    [Fact]
    public void IntermediateWildcard_OnlyDescendsDirectories()
    {
        var x = Fake(new()
        {
            ["."] = new[] { D("one"), D("two"), F("decoy") },
            ["one"] = new[] { F("hit.log") },
            ["two"] = new[] { F("other.txt") },
        });
        Assert.Equal(new[] { "one\\hit.log" }, x.Expand("*\\hit.log").Matches);
    }

    [Fact]
    public void FinalLiteralAfterWildcard_CaseInsensitive_DiskCasingReturned()
    {
        var x = Fake(new()
        {
            ["."] = new[] { D("Proj") },
            ["Proj"] = new[] { F("README.md") },
        });
        Assert.Equal(new[] { "Proj\\README.md" }, x.Expand("*\\readme.md").Matches);
    }

    [Fact]
    public void DriveRootPrefix_KeepsRootSeparator()
    {
        var x = Fake(new() { ["C:\\"] = new[] { F("pagefile.sys"), D("Windows") } });
        Assert.Equal(new[] { "C:\\pagefile.sys", "C:\\Windows" }, x.Expand("C:\\*").Matches);
    }

    [Fact]
    public void RelativeParent_WalksLiteralPrefix()
    {
        var x = Fake(new() { [".."] = new[] { F("up.txt") } });
        Assert.Equal(new[] { "..\\up.txt" }, x.Expand("..\\*.txt").Matches);
    }

    [Fact]
    public void EnumerationFailure_IsNoMatch()
    {
        var boom = new GlobArgExpander(_ => throw new UnauthorizedAccessException("nope"));
        // The DEFAULT enumerator swallows; the seam contract is "throw = let it surface"?
        // No: the expander itself must guard, so a custom seam may throw too.
        Assert.Equal(GlobExpansionKind.NoMatch, boom.Expand("*.txt").Kind);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --filter GlobArgExpanderTests`
Expected: FAIL (compile error — types missing).

- [ ] **Step 3: Implement**

`src/Yort.ShellKit/GlobArgExpander.cs`:
```csharp
namespace Yort.ShellKit;

/// <summary>Classifies the outcome of expanding one argument.</summary>
internal enum GlobExpansionKind
{
    /// <summary>The argument contains no <c>*</c> or <c>?</c> — not a pattern; leave untouched.</summary>
    NotAPattern,
    /// <summary>The pattern matched at least one entry; <see cref="GlobExpansionResult.Matches"/> holds them.</summary>
    Expanded,
    /// <summary>A pattern, but nothing matched — pass the literal through (bash nullglob-off).</summary>
    NoMatch,
    /// <summary>The argument contains <c>**</c>, which argument expansion does not support.</summary>
    UnsupportedRecursive,
}

/// <summary>Result of expanding one argument: the outcome kind plus matches when expanded.</summary>
internal readonly record struct GlobExpansionResult(GlobExpansionKind Kind, IReadOnlyList<string> Matches)
{
    /// <summary>Singleton for non-pattern arguments.</summary>
    public static GlobExpansionResult NotAPattern { get; } = new(GlobExpansionKind.NotAPattern, Array.Empty<string>());
    /// <summary>Singleton for patterns with no matches.</summary>
    public static GlobExpansionResult NoMatch { get; } = new(GlobExpansionKind.NoMatch, Array.Empty<string>());
    /// <summary>Singleton for unsupported <c>**</c> patterns.</summary>
    public static GlobExpansionResult UnsupportedRecursive { get; } = new(GlobExpansionKind.UnsupportedRecursive, Array.Empty<string>());
}

/// <summary>
/// Expands a single command-line argument containing <c>*</c>/<c>?</c> wildcards against the
/// filesystem, with bash-compatible semantics (dotfile rule, no-match passthrough, files and
/// directories both match, trailing separator restricts to directories). Used by
/// <see cref="CommandLineParser"/> on Windows for tools that opt in via
/// <c>ExpandGlobPositionals()</c>.
/// </summary>
/// <remarks>
/// Matching is done in-process via <see cref="GlobMatcher"/> against enumerated entry names —
/// never via <c>Directory.GetFiles(dir, pattern)</c>, whose OS-level matching also matches 8.3
/// short names (so <c>*.log</c> would match <c>*.log2</c>). The literal prefix before the first
/// wildcard segment is kept verbatim as typed; matched components use on-disk casing.
/// </remarks>
internal sealed class GlobArgExpander
{
    /// <summary>One enumerated directory entry: leaf name plus whether it is a directory.</summary>
    internal readonly record struct FsEntry(string Name, bool IsDirectory);

    private static readonly char[] Metachars = { '*', '?' };

    private readonly Func<string, List<FsEntry>> _enumerate;

    /// <summary>Creates an expander over the real filesystem.</summary>
    public GlobArgExpander()
        : this(DefaultEnumerate)
    {
    }

    /// <summary>Test seam: creates an expander over a custom directory enumerator.</summary>
    /// <param name="enumerate">Maps a directory path ("." for the current directory) to its entries.</param>
    internal GlobArgExpander(Func<string, List<FsEntry>> enumerate)
    {
        _enumerate = enumerate;
    }

    /// <summary>Expands one argument. See <see cref="GlobExpansionKind"/> for outcomes.</summary>
    public GlobExpansionResult Expand(string arg)
    {
        if (arg.IndexOfAny(Metachars) < 0)
        {
            return GlobExpansionResult.NotAPattern;
        }
        if (arg.Contains("**", StringComparison.Ordinal))
        {
            return GlobExpansionResult.UnsupportedRecursive;
        }

        // Trailing separator(s) → directories only; preserved verbatim on output.
        int end = arg.Length;
        while (end > 0 && (arg[end - 1] == '\\' || arg[end - 1] == '/'))
        {
            end--;
        }
        string trailing = arg[end..];
        string body = arg[..end];
        bool dirsOnly = trailing.Length > 0;

        // Split into segments, remembering each segment's start offset so we can recover
        // both the verbatim literal prefix and the typed separator preceding each segment.
        var segments = new List<(string Text, int Start)>();
        int segStart = 0;
        for (int i = 0; i <= body.Length; i++)
        {
            if (i == body.Length || body[i] == '\\' || body[i] == '/')
            {
                segments.Add((body[segStart..i], segStart));
                segStart = i + 1;
            }
        }

        int firstWild = segments.FindIndex(s => s.Text.IndexOfAny(Metachars) >= 0);
        // firstWild >= 0 is guaranteed: metachars exist and separators are not metachars.

        // Verbatim text before the first wildcard segment, INCLUDING its trailing separator
        // ("C:\" must stay a root, not become drive-relative "C:").
        string prefix = firstWild == 0 ? "" : body[..segments[firstWild].Start];

        var candidates = new List<string> { prefix };
        for (int s = firstWild; s < segments.Count; s++)
        {
            (string segText, int start) = segments[s];
            bool isLast = s == segments.Count - 1;
            bool needDir = !isLast || dirsOnly;
            bool segHasWild = segText.IndexOfAny(Metachars) >= 0;
            GlobMatcher? matcher = segHasWild ? new GlobMatcher(new[] { segText }, caseInsensitive: true) : null;
            bool matchesDotEntries = segText.StartsWith('.');
            char sep = start > 0 ? body[start - 1] : '\\';

            var next = new List<string>();
            foreach (string candidate in candidates)
            {
                string dir = candidate.Length == 0 ? "." : candidate;
                List<FsEntry> entries;
                try
                {
                    entries = _enumerate(dir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                    or System.Security.SecurityException or ArgumentException or NotSupportedException)
                {
                    // bash parity: an unreadable/invalid directory contributes no matches;
                    // the pattern falls through as a literal. Deliberately not an error.
                    continue;
                }

                foreach (FsEntry entry in entries)
                {
                    if (needDir && !entry.IsDirectory)
                    {
                        continue;
                    }

                    bool isMatch;
                    if (matcher is not null)
                    {
                        if (!matchesDotEntries && entry.Name.StartsWith('.'))
                        {
                            continue; // bash dotfile rule
                        }
                        isMatch = matcher.IsMatch(entry.Name);
                    }
                    else
                    {
                        // Literal segment after a wildcard: case-insensitive equality,
                        // emitting on-disk casing.
                        isMatch = string.Equals(entry.Name, segText, StringComparison.OrdinalIgnoreCase);
                    }

                    if (isMatch)
                    {
                        if (candidate.Length == 0)
                        {
                            next.Add(entry.Name);
                        }
                        else if (candidate[^1] is '\\' or '/')
                        {
                            next.Add(candidate + entry.Name);
                        }
                        else
                        {
                            next.Add(candidate + sep + entry.Name);
                        }
                    }
                }
            }

            candidates = next;
            if (candidates.Count == 0)
            {
                break;
            }
        }

        if (candidates.Count == 0)
        {
            return GlobExpansionResult.NoMatch;
        }

        candidates.Sort(StringComparer.OrdinalIgnoreCase);
        if (trailing.Length > 0)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                candidates[i] += trailing;
            }
        }
        return new GlobExpansionResult(GlobExpansionKind.Expanded, candidates);
    }

    private static List<FsEntry> DefaultEnumerate(string dir)
    {
        var result = new List<FsEntry>();
        // Hidden/system attributes deliberately NOT filtered: bash/Git Bash on Windows match
        // them, and attribute filtering would be a silent data-dependent divergence (see ADR).
        foreach (string path in Directory.EnumerateFileSystemEntries(dir))
        {
            result.Add(new FsEntry(Path.GetFileName(path), Directory.Exists(path)));
        }
        return result;
    }
}
```

Note: `DefaultEnumerate` does NOT catch — the `Expand` loop catches around the seam call so *custom* seams get the same guarantee (locked by `EnumerationFailure_IsNoMatch`).

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --filter GlobArgExpanderTests`
Expected: PASS (14 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Yort.ShellKit/GlobArgExpander.cs tests/Yort.ShellKit.Tests/GlobArgExpanderTests.cs
git commit -m "feat(shellkit): glob argument expander (segment walk, bash semantics, FS seam)"
```

---

### Task 5: Real-filesystem integration tests (Windows)

**Files:**
- Create: `tests/Yort.ShellKit.Tests/GlobArgExpanderRealFsTests_Windows.cs`

- [ ] **Step 1: Write the tests**

```csharp
using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class GlobArgExpanderRealFsTests_Windows : IDisposable
{
    private readonly string _root;
    private readonly string _originalCwd;

    public GlobArgExpanderRealFsTests_Windows()
    {
        _root = Path.Combine(Path.GetTempPath(), "shellkit-glob-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCwd = Directory.GetCurrentDirectory();
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalCwd);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [SkippableFact]
    public void RealFs_StarPattern_MatchesIncludingHiddenAttribute()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "real-FS glob semantics under test are Windows-only");
        if (!OperatingSystem.IsWindows()) { return; } // deliberate redundancy for CA1416

        File.WriteAllText(Path.Combine(_root, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "b");
        File.WriteAllText(Path.Combine(_root, "c.log"), "c");
        string hidden = Path.Combine(_root, "h.txt");
        File.WriteAllText(hidden, "h");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.WriteAllText(Path.Combine(_root, ".dot.txt"), "d");
        Directory.SetCurrentDirectory(_root);

        var r = new GlobArgExpander().Expand("*.txt");

        Assert.Equal(GlobExpansionKind.Expanded, r.Kind);
        // Hidden-ATTRIBUTE file matches (bash parity); leading-DOT file does not.
        Assert.Equal(new[] { "a.txt", "b.txt", "h.txt" }, r.Matches);
    }

    [SkippableFact]
    public void RealFs_IntermediateWildcard_And_NoMatch()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "real-FS glob semantics under test are Windows-only");
        if (!OperatingSystem.IsWindows()) { return; } // deliberate redundancy for CA1416

        Directory.CreateDirectory(Path.Combine(_root, "one"));
        Directory.CreateDirectory(Path.Combine(_root, "two"));
        File.WriteAllText(Path.Combine(_root, "one", "hit.log"), "x");
        Directory.SetCurrentDirectory(_root);

        var hit = new GlobArgExpander().Expand("*\\hit.log");
        Assert.Equal(new[] { "one\\hit.log" }, hit.Matches);

        Assert.Equal(GlobExpansionKind.NoMatch, new GlobArgExpander().Expand("*.zip").Kind);
        // Nonexistent literal prefix → enumeration failure → NoMatch, not an exception.
        Assert.Equal(GlobExpansionKind.NoMatch, new GlobArgExpander().Expand("missing-dir\\*.txt").Kind);
    }
}
```

- [ ] **Step 2: Run on Windows**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --filter GlobArgExpanderRealFsTests`
Expected: PASS (2 tests).

- [ ] **Step 3: Commit**

```bash
git add tests/Yort.ShellKit.Tests/GlobArgExpanderRealFsTests_Windows.cs
git commit -m "test(shellkit): real-FS glob expander integration (hidden-attr, dotfile, walk)"
```

---

### Task 6: Parser opt-in + expansion hook (TDD)

**Files:**
- Modify: `src/Yort.ShellKit/CommandLineParser.cs`
- Create: `tests/Yort.ShellKit.Tests/GlobExpansionParserTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class GlobExpansionParserTests
{
    private static readonly Dictionary<string, GlobArgExpander.FsEntry[]> Fs = new()
    {
        ["."] = new GlobArgExpander.FsEntry[]
        {
            new("a.txt", false), new("b.txt", false), new("c.log", false),
        },
    };

    // raw == null → simulate "raw line unavailable" (provider returns null).
    private static CommandLineParser NewParser(string? raw, bool windows = true, int skipFirst = 0)
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags().ExpandGlobPositionals(skipFirst);
        p.GlobWindowsGateOverride = () => windows;
        p.GlobRawCommandLineProvider = () => raw;
        p.GlobExpanderOverride = new GlobArgExpander(
            dir => Fs.TryGetValue(dir, out var e) ? new List<GlobArgExpander.FsEntry>(e) : new List<GlobArgExpander.FsEntry>());
        return p;
    }

    [Fact]
    public void NotOptedIn_PatternUntouched()
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags();
        var r = p.Parse(new[] { "*.txt" });
        Assert.Equal(new[] { "*.txt" }, r.Positionals);
    }

    [Fact]
    public void OptedIn_NonWindows_Untouched()
    {
        var r = NewParser("\"t.exe\" *.txt", windows: false).Parse(new[] { "*.txt" });
        Assert.Equal(new[] { "*.txt" }, r.Positionals);
    }

    [Fact]
    public void UnquotedPattern_ExpandsSortedInPlace()
    {
        var r = NewParser("\"t.exe\" first *.txt last").Parse(new[] { "first", "*.txt", "last" });
        Assert.Equal(new[] { "first", "a.txt", "b.txt", "last" }, r.Positionals);
    }

    [Fact]
    public void QuotedPattern_NotExpanded()
    {
        var r = NewParser("\"t.exe\" \"*.txt\"").Parse(new[] { "*.txt" });
        Assert.Equal(new[] { "*.txt" }, r.Positionals);
    }

    [Fact]
    public void RawLineUnavailable_FailsOpen_Expands()
    {
        var r = NewParser(raw: null).Parse(new[] { "*.txt" });
        Assert.Equal(new[] { "a.txt", "b.txt" }, r.Positionals);
    }

    [Fact]
    public void RawLineMisaligned_FailsOpen_Expands()
    {
        // Raw has an extra token (dotnet-style host) → count mismatch → expand everything.
        var r = NewParser("\"dotnet.exe\" tool.dll \"*.txt\"").Parse(new[] { "*.txt" });
        Assert.Equal(new[] { "a.txt", "b.txt" }, r.Positionals);
    }

    [Fact]
    public void NoMatch_LiteralPassthrough()
    {
        var r = NewParser("\"t.exe\" *.zip").Parse(new[] { "*.zip" });
        Assert.Equal(new[] { "*.zip" }, r.Positionals);
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void DoubleStar_UsageError_LiteralKept()
    {
        var r = NewParser("\"t.exe\" **/*.txt").Parse(new[] { "**/*.txt" });
        Assert.Equal(new[] { "**/*.txt" }, r.Positionals);
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("**", StringComparison.Ordinal));
    }

    [Fact]
    public void AfterDoubleDash_StillExpanded()
    {
        var r = NewParser("\"t.exe\" -- *.txt").Parse(new[] { "--", "*.txt" });
        Assert.Equal(new[] { "a.txt", "b.txt" }, r.Positionals);
    }

    [Fact]
    public void SkipFirst_LeavesVerbUntouched()
    {
        // Subcommand-style: positional 0 is a verb (or a cron field!) — never expanded.
        var r = NewParser("\"t.exe\" * *.txt", skipFirst: 1).Parse(new[] { "*", "*.txt" });
        Assert.Equal("*", r.Positionals[0]);
        Assert.Equal(new[] { "*", "a.txt", "b.txt" }, r.Positionals);
    }

    [Fact]
    public void CommandMode_NeverExpands()
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags().CommandMode().ExpandGlobPositionals();
        p.GlobWindowsGateOverride = () => true;
        p.GlobRawCommandLineProvider = () => "\"t.exe\" child *.txt";
        var r = p.Parse(new[] { "child", "*.txt" });
        Assert.Equal(new[] { "child", "*.txt" }, r.Command);
    }

    [Fact]
    public void HelpRequested_SkipsExpansion()
    {
        int calls = 0;
        var p = new CommandLineParser("tool", "1.0").StandardFlags().ExpandGlobPositionals();
        p.GlobWindowsGateOverride = () => true;
        p.GlobRawCommandLineProvider = () => "\"t.exe\" --help *.txt";
        p.GlobExpanderOverride = new GlobArgExpander(dir => { calls++; return new List<GlobArgExpander.FsEntry>(); });
        var r = p.Parse(new[] { "--help", "*.txt" });
        Assert.True(r.IsHandled);
        Assert.Equal(0, calls); // no FS work when help/version/describe short-circuits
    }

    [Fact]
    public void ExpandAfterParse_Throws()
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags();
        p.Parse(Array.Empty<string>());
        Assert.Throws<InvalidOperationException>(() => p.ExpandGlobPositionals());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --filter GlobExpansionParserTests`
Expected: FAIL (compile error — `ExpandGlobPositionals` and the seam properties don't exist).

- [ ] **Step 3: Implement the parser changes**

In `src/Yort.ShellKit/CommandLineParser.cs`:

(a) New state fields, next to the existing private fields (~line 45):
```csharp
    private bool _expandGlobPositionals;
    private int _globSkipFirst;

    // Test seams for glob expansion (precedent: ResolveColorCore). Null → production behaviour.
    internal Func<bool>? GlobWindowsGateOverride;
    internal Func<string?>? GlobRawCommandLineProvider;
    internal GlobArgExpander? GlobExpanderOverride;
```

(b) The builder method, near `Positional` (~line 183):
```csharp
    /// <summary>
    /// Opts this tool into Windows glob expansion of positional arguments. On Windows
    /// (cmd.exe and PowerShell do not expand wildcards), positionals containing
    /// <c>*</c> or <c>?</c> are expanded in-process before they reach
    /// <see cref="ParseResult.Positionals"/>; on other platforms this is a no-op (the
    /// shell has already expanded). A pattern matching nothing passes through literally;
    /// <c>[...]</c> is never treated as a pattern; <c>**</c> produces a usage error.
    /// Arguments quoted on the raw command line are not expanded (effective from cmd.exe;
    /// PowerShell strips quotes before the process starts). Only opt in when ALL
    /// positionals (after <paramref name="skipFirst"/>) are file paths — never for
    /// subcommand verbs or cron expressions.
    /// </summary>
    /// <param name="skipFirst">Number of leading positionals to exempt (e.g. 1 for a subcommand verb).</param>
    public CommandLineParser ExpandGlobPositionals(int skipFirst = 0)
    {
        ThrowIfParsed();
        _expandGlobPositionals = true;
        _globSkipFirst = skipFirst;
        return this;
    }
```

(c) Track positional argv indices in `Parse()`. After `var positionals = new List<string>();` (~line 300) add:
```csharp
        var positionalArgvIndices = new List<int>();
```
At the two positional-collection sites:
```csharp
                    else
                    {
                        positionals.Add(args[j]);
                        positionalArgvIndices.Add(j);
                    }
```
(inside the `--` loop, ~line 320) and
```csharp
                else
                {
                    positionals.Add(arg);
                    positionalArgvIndices.Add(i);
                    continue;
                }
```
(~line 340).

(d) The expansion hook. Insert **after** the `--help`/`--version`/`--describe` handling block (after ~line 549) and **before** `return new ParseResult(...)`:
```csharp
        if (_expandGlobPositionals && !_commandMode && !isHandled
            && positionals.Count > _globSkipFirst
            && (GlobWindowsGateOverride?.Invoke() ?? OperatingSystem.IsWindows()))
        {
            ExpandGlobPositionalsInPlace(args, positionals, positionalArgvIndices, errors);
        }
```

(e) The private methods (place after `BuildLookups()`):
```csharp
    private void ExpandGlobPositionalsInPlace(string[] args, List<string> positionals,
        List<int> argvIndices, List<string> errors)
    {
        bool[]? quoted = TryGetQuotedFlags(args);
        GlobArgExpander expander = GlobExpanderOverride ?? new GlobArgExpander();
        var result = new List<string>(positionals.Count);

        for (int p = 0; p < positionals.Count; p++)
        {
            string value = positionals[p];
            if (p < _globSkipFirst)
            {
                result.Add(value);
                continue;
            }

            // quoted == null → raw line unavailable/misaligned → fail OPEN (expand). A quoted
            // glob falling back to expansion is benign (a '*'-bearing literal can never name a
            // real Windows file); silently disabling expansion would be an invisible outage.
            if (quoted is not null && quoted[argvIndices[p]])
            {
                result.Add(value);
                continue;
            }

            GlobExpansionResult expansion = expander.Expand(value);
            switch (expansion.Kind)
            {
                case GlobExpansionKind.Expanded:
                    result.AddRange(expansion.Matches);
                    break;
                case GlobExpansionKind.UnsupportedRecursive:
                    errors.Add($"recursive glob '**' is not supported in argument expansion: {value} (use the tool's recursive matching options instead)");
                    result.Add(value);
                    break;
                default: // NotAPattern, NoMatch → literal passthrough
                    result.Add(value);
                    break;
            }
        }

        positionals.Clear();
        positionals.AddRange(result);
    }

    /// <summary>
    /// Maps each argv index to whether that token was quoted on the raw command line.
    /// Returns null (caller fails open) when the raw line is unavailable or does not
    /// align with args — e.g. `dotnet tool.dll args` hosting, or a tokenizer gap.
    /// </summary>
    private bool[]? TryGetQuotedFlags(string[] args)
    {
        string? raw = GlobRawCommandLineProvider is not null
            ? GlobRawCommandLineProvider()
            : Environment.CommandLine;
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        IReadOnlyList<CommandLineToken> tokens = RawCommandLineTokenizer.Tokenize(raw);
        if (tokens.Count != args.Length + 1) // + argv[0]
        {
            return null;
        }
        for (int i = 0; i < args.Length; i++)
        {
            // Text alignment too, not just count — belt-and-braces against rule drift.
            if (!string.Equals(tokens[i + 1].Text, args[i], StringComparison.Ordinal))
            {
                return null;
            }
        }

        var quoted = new bool[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            quoted[i] = tokens[i + 1].WasQuoted;
        }
        return quoted;
    }
```

- [ ] **Step 4: Run the new tests AND the full ShellKit suite**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj`
Expected: all PASS, including every pre-existing test untouched (this locks the "non-adopters byte-identical" claim).

- [ ] **Step 5: Commit**

```bash
git add src/Yort.ShellKit/CommandLineParser.cs tests/Yort.ShellKit.Tests/GlobExpansionParserTests.cs
git commit -m "feat(shellkit): ExpandGlobPositionals opt-in — Windows glob expansion with quoting"
```

---

### Task 7: --help section + --describe field (TDD)

**Files:**
- Modify: `src/Yort.ShellKit/CommandLineParser.cs` (`GenerateHelp`, `GenerateDescribe`)
- Modify: `tests/Yort.ShellKit.Tests/GlobExpansionParserTests.cs` (append tests)

- [ ] **Step 1: Write the failing tests** (append to `GlobExpansionParserTests.cs`)

```csharp
    [Fact]
    public void GenerateHelp_OptedIn_IncludesWildcardsSection()
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags().ExpandGlobPositionals();
        string help = p.GenerateHelp();
        Assert.Contains("Wildcards (Windows):", help, StringComparison.Ordinal);
        Assert.Contains("'**' is not supported", help, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateHelp_NotOptedIn_OmitsWildcardsSection()
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags();
        Assert.DoesNotContain("Wildcards (Windows):", p.GenerateHelp(), StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateDescribe_OptedIn_IncludesGlobExpansion()
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags().ExpandGlobPositionals(skipFirst: 1);
        string json = p.GenerateDescribe();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var glob = doc.RootElement.GetProperty("glob_expansion");
        Assert.True(glob.GetProperty("positionals").GetBoolean());
        Assert.Equal(1, glob.GetProperty("skip_first").GetInt32());
        Assert.True(glob.GetProperty("windows_only").GetBoolean());
        Assert.Equal("literal passthrough", glob.GetProperty("no_match").GetString());
    }

    [Fact]
    public void GenerateDescribe_NotOptedIn_OmitsGlobExpansion()
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags();
        using var doc = System.Text.Json.JsonDocument.Parse(p.GenerateDescribe());
        Assert.False(doc.RootElement.TryGetProperty("glob_expansion", out _));
    }
```

(`GenerateHelp`/`GenerateDescribe` are `internal`; `InternalsVisibleTo` for the test project already exists in the csproj.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj --filter GlobExpansionParserTests`
Expected: 4 new tests FAIL.

- [ ] **Step 3: Implement**

In `GenerateHelp()`, immediately after the custom-sections loop (after ~line 721, before the Examples block):
```csharp
        if (_expandGlobPositionals && !_commandMode)
        {
            sb.AppendLine();
            sb.AppendLine("Wildcards (Windows):");
            sb.AppendLine($"  On Windows, * and ? in path arguments are expanded by {_toolName} itself");
            sb.AppendLine("  (cmd and PowerShell don't expand them). [...] is matched literally and");
            sb.AppendLine("  '**' is not supported. A pattern matching nothing is passed through");
            sb.AppendLine("  unchanged. In cmd, quote the pattern to suppress expansion.");
        }
```

In `GenerateDescribe()`, after the `platform` object (locate the block ending near line 818; add at the same nesting level as `platform`, before `usage` is written at ~line 834 or directly after the platform block — match surrounding style):
```csharp
            if (_expandGlobPositionals)
            {
                writer.WriteStartObject("glob_expansion");
                writer.WriteBoolean("positionals", true);
                if (_globSkipFirst > 0)
                {
                    writer.WriteNumber("skip_first", _globSkipFirst);
                }
                writer.WriteString("syntax", "* and ? in any path segment");
                writer.WriteStartArray("not_patterns");
                writer.WriteStringValue("[...] (matched literally; legal Windows filename chars)");
                writer.WriteStringValue("** (rejected with usage error)");
                writer.WriteEndArray();
                writer.WriteString("no_match", "literal passthrough");
                writer.WriteString("quoting", "quoted args not expanded (effective from cmd; PowerShell strips quotes)");
                writer.WriteBoolean("windows_only", true);
                writer.WriteEndObject();
            }
```

- [ ] **Step 4: Run the full ShellKit suite**

Run: `dotnet test tests/Yort.ShellKit.Tests/Yort.ShellKit.Tests.csproj`
Expected: all PASS (pre-existing `DescribeTests` untouched).

- [ ] **Step 5: Commit**

```bash
git add src/Yort.ShellKit/CommandLineParser.cs tests/Yort.ShellKit.Tests/GlobExpansionParserTests.cs
git commit -m "feat(shellkit): advertise glob expansion in --help and --describe when opted in"
```

---

## Adoption tasks (8–13)

Shared facts for all six (repeated inline per task so tasks stand alone):
- The opt-in line goes **immediately after `.StandardFlags()`** in the fluent chain.
- Run the tool's full test project after the change. **If a help-text/describe snapshot test fails because of the new Wildcards section / glob_expansion field, updating that test is intentional and correct** — the contract change is this feature (record it in the commit message).
- The smoke MUST use the **built apphost exe** (`src\{tool}\bin\Debug\net10.0\{tool}.exe`), NOT `dotnet run` — under `dotnet run` the raw command line belongs to dotnet and quoting detection deliberately fails open, so the quoted-suppression smoke would falsely fail.
- Smoke fixture (PowerShell, once, reused by all six):
```powershell
$g = "$env:TEMP\globsmoke"; New-Item -ItemType Directory -Force $g | Out-Null
Set-Content "$g\a.txt" 'aaa'; Set-Content "$g\b.txt" 'bbb'; Set-Content "$g\c.log" 'ccc'
```
- Man-page edit: check `ls src/{tool}/man/man1/` first. If a `{tool}.1.md` pandoc source exists, edit the `.md` and regenerate the `.1` the same way the repo did originally (check git log for that file); if only `{tool}.1` exists, edit the groff directly. **Editing only the rendered `.1` when a `.md` source exists is the known 3-strike build trap — don't be strike 4.**

Canonical README section (insert before the tool's "Colour"/"Exit codes" section; same text every tool, with the example line swapped):
```markdown
## Wildcards on Windows

cmd.exe and PowerShell don't expand `*`/`?` wildcards before starting programs, so
{tool} expands them itself on Windows — `{smoke-example}` works the same as in bash.
`*` and `?` work in any path segment. `[...]` is matched literally (brackets are legal
Windows filename characters), and `**` is rejected with an error — use the tool's own
recursive options instead. A pattern that matches nothing is passed through unchanged,
so you get the normal "not found" error. In cmd, quoting a pattern (`"{quoted-example}"`)
suppresses expansion; PowerShell removes quotes before {tool} sees them, so use `--%`
there if you need a literal. On Linux/macOS your shell expands wildcards as usual and
{tool} does nothing extra.
```

Canonical `docs/ai/{tool}.md` addition (under usage/flags content):
```markdown
## Glob expansion on Windows

{tool} expands `*`/`?` in path positionals itself on Windows (cmd/pwsh don't).
Support matrix: `*` and `?` in any segment — yes; `[...]` — matched literally
(legal filename chars); `**` — usage error (use recursive flags instead); no
match — literal passthrough (normal "not found" follows). Quoted args are not
expanded when launched from cmd; PowerShell strips quotes before launch, so
prefer explicit paths there if a literal is required. On Unix the shell expands;
the tool adds nothing. `--describe` exposes this as `glob_expansion`.
```

### Task 8: digest adoption

**Files:**
- Modify: `src/Winix.Digest/ArgParser.cs` (~line 370)
- Modify: `src/digest/README.md`, `src/digest/man/man1/digest.1` (or `.1.md` source), `docs/ai/digest.md`

- [ ] **Step 1: Add the opt-in**

In `src/Winix.Digest/ArgParser.cs` (~line 372, after `.StandardFlags()`):
```csharp
            new CommandLineParser("digest", ResolveVersion())
                .Description(description)
                .StandardFlags()
                .ExpandGlobPositionals()
                .Platform("cross-platform",
```

- [ ] **Step 2: Build + run digest tests**

Run: `dotnet test tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj`
Expected: PASS. If a `--help`/`--describe` snapshot test fails on the new section/field, update it (intentional contract change — note in commit).

- [ ] **Step 3: Smoke (cmd + pwsh + quoted + `**`), capture output**

```powershell
dotnet build src/digest/digest.csproj
$exe = (Resolve-Path "src\digest\bin\Debug\net10.0\digest.exe").Path
$g = "$env:TEMP\globsmoke"
cmd /c "cd /d $g && `"$exe`" *.txt"            # EXPECT: two hash lines (a.txt, b.txt)
cmd /c "cd /d $g && `"$exe`" `"*.txt`""         # EXPECT: not-found error (quoted suppresses), exit != 0
cmd /c "cd /d $g && `"$exe`" *.zip"            # EXPECT: not-found error naming *.zip (literal passthrough)
cmd /c "cd /d $g && `"$exe`" **\*.txt"          # EXPECT: usage error mentioning '**'
Push-Location $g; & $exe *.txt; Pop-Location    # pwsh: EXPECT two hash lines
```
Record actual outputs in `artifacts/v0.4-smoke/glob/digest.txt` (create dir; artifacts/ is the established smoke-capture location). Also run `& $exe --help` and confirm the Wildcards section renders, and `& $exe --describe` shows `glob_expansion`.

- [ ] **Step 4: Docs**

Apply the canonical README section to `src/digest/README.md` with `{smoke-example}` = `digest *.dll` and `{quoted-example}` = `"*.dll"`. Apply the canonical docs/ai addition to `docs/ai/digest.md`. Update the man page (check for `.1.md` source first — see shared note).

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Digest/ArgParser.cs src/digest/README.md src/digest/man docs/ai/digest.md tests/Winix.Digest.Tests
git commit -m "feat(digest): expand * and ? wildcards on Windows (ExpandGlobPositionals)"
```

### Task 9: squeeze adoption

**Files:** `src/Winix.Squeeze/Cli.cs` (~line 234), `src/squeeze/README.md`, `src/squeeze/man/man1/squeeze.1` (or source), `docs/ai/squeeze.md`

- [ ] **Step 1: Opt-in** — in `src/Winix.Squeeze/Cli.cs` (~line 236):
```csharp
            new CommandLineParser("squeeze", version)
                .Description("Compress and decompress files using gzip, brotli, or zstd.")
                .StandardFlags()
                .ExpandGlobPositionals()
                .Flag("--decompress", "-d", "Decompress (auto-detects format)")
```
- [ ] **Step 2: Tests** — Run: `dotnet test tests/Winix.Squeeze.Tests/Winix.Squeeze.Tests.csproj`. Expected: PASS (update help/describe snapshots if the new section trips them — intentional).
- [ ] **Step 3: Smoke** (fresh sub-dir so created `.gz` files don't pollute later smokes):
```powershell
dotnet build src/squeeze/squeeze.csproj
$exe = (Resolve-Path "src\squeeze\bin\Debug\net10.0\squeeze.exe").Path
$s = "$env:TEMP\globsmoke\sq"; New-Item -ItemType Directory -Force $s | Out-Null
Set-Content "$s\a.txt" 'aaa'; Set-Content "$s\b.txt" 'bbb'
cmd /c "cd /d $s && `"$exe`" *.txt"             # EXPECT: a.txt.gz and b.txt.gz created
cmd /c "cd /d $s && `"$exe`" *.nope"            # EXPECT: not-found error naming *.nope
cmd /c "cd /d $s && `"$exe`" **\*.txt"           # EXPECT: usage error mentioning '**'
```
Capture to `artifacts/v0.4-smoke/glob/squeeze.txt`. Verify `--help` section + `--describe` field.
- [ ] **Step 4: Docs** — canonical README section ({smoke-example} = `squeeze *.log`, {quoted-example} = `"*.log"`), docs/ai addition, man page (check `.1.md` first).
- [ ] **Step 5: Commit**
```bash
git add src/Winix.Squeeze/Cli.cs src/squeeze/README.md src/squeeze/man docs/ai/squeeze.md tests/Winix.Squeeze.Tests
git commit -m "feat(squeeze): expand * and ? wildcards on Windows (ExpandGlobPositionals)"
```

### Task 10: trash adoption (+ closes trash finding #6)

**Files:** `src/Winix.Trash/ArgParser.cs` (~line 154), `src/trash/README.md`, `src/trash/man/man1/trash.1` (or source), `docs/ai/trash.md`

- [ ] **Step 1: Opt-in** — in `src/Winix.Trash/ArgParser.cs` (~line 156):
```csharp
            new CommandLineParser("trash", version)
                .Description("Move files and directories to the recycle bin / Trash. Also lists and empties the trash.")
                .StandardFlags()
                .ExpandGlobPositionals()
```
- [ ] **Step 2: Tests** — Run: `dotnet test tests/Winix.Trash.Tests/Winix.Trash.Tests.csproj`. Expected: PASS (snapshot updates intentional if tripped).
- [ ] **Step 3: Smoke** (uses throwaway temp files; recycle bin receives them — acceptable):
```powershell
dotnet build src/trash/trash.csproj
$exe = (Resolve-Path "src\trash\bin\Debug\net10.0\trash.exe").Path
$t = "$env:TEMP\globsmoke\tr"; New-Item -ItemType Directory -Force $t | Out-Null
Set-Content "$t\x.log" 'x'; Set-Content "$t\y.log" 'y'; Set-Content "$t\keep.txt" 'k'
cmd /c "cd /d $t && `"$exe`" *.log"             # EXPECT: x.log + y.log trashed; keep.txt remains
cmd /c "cd /d $t && `"$exe`" *.log"             # EXPECT: not-found (already gone; literal passthrough)
```
Capture to `artifacts/v0.4-smoke/glob/trash.txt`. Verify `--help` + `--describe`.
- [ ] **Step 4: Docs + finding #6** — canonical README section ({smoke-example} = `trash *.log`, {quoted-example} = `"*.log"`); docs/ai addition; man page (check `.1.md` first). **Then re-check the `trash --help` examples**: the `trash *.log` example (open finding #6 from the trash review) is now correct on Windows — ensure the example exists and, if a caveat note was added near it during review, replace the caveat with a pointer to the Wildcards section. Record "closes trash review finding #6" in the commit message.
- [ ] **Step 5: Commit**
```bash
git add src/Winix.Trash/ArgParser.cs src/trash/README.md src/trash/man docs/ai/trash.md tests/Winix.Trash.Tests
git commit -m "feat(trash): expand * and ? wildcards on Windows (closes trash review finding #6)"
```

### Task 11: less adoption

**Files:** `src/Winix.Less/Cli.cs` (~line 230), `src/less/README.md`, `src/less/man/man1/less.1` (or source), `docs/ai/less.md`

- [ ] **Step 1: Opt-in** — in `src/Winix.Less/Cli.cs` (~line 232):
```csharp
            new CommandLineParser("less", version)
                .Description("Display file contents one screen at a time with scrolling, search, and ANSI colour passthrough.")
                .StandardFlags()
                .ExpandGlobPositionals()
                .Flag("-N", "Show line numbers")
```
- [ ] **Step 2: Tests** — Run: `dotnet test tests/Winix.Less.Tests/Winix.Less.Tests.csproj`. Expected: PASS (snapshot updates intentional if tripped).
- [ ] **Step 3: Smoke** (non-tty → less dumps content; piping makes that deterministic):
```powershell
dotnet build src/less/less.csproj
$exe = (Resolve-Path "src\less\bin\Debug\net10.0\less.exe").Path
$g = "$env:TEMP\globsmoke"
cmd /c "cd /d $g && `"$exe`" *.txt | findstr aaa"   # EXPECT: 'aaa' found (a.txt content dumped)
cmd /c "cd /d $g && `"$exe`" *.nope"                # EXPECT: not-found error naming *.nope
```
Capture to `artifacts/v0.4-smoke/glob/less.txt`. Verify `--help` + `--describe`.
- [ ] **Step 4: Docs** — canonical README section ({smoke-example} = `less *.log`, {quoted-example} = `"*.log"`), docs/ai addition, man page (check `.1.md` first — less docs had 3-surface drift history; update README, man, AND docs/ai together).
- [ ] **Step 5: Commit**
```bash
git add src/Winix.Less/Cli.cs src/less/README.md src/less/man docs/ai/less.md tests/Winix.Less.Tests
git commit -m "feat(less): expand * and ? wildcards on Windows (ExpandGlobPositionals)"
```

### Task 12: treex adoption

**Files:** `src/Winix.TreeX/Cli.cs` (~line 442), `src/treex/README.md`, `src/treex/man/man1/treex.1` (or source), `docs/ai/treex.md`

- [ ] **Step 1: Opt-in** — in `src/Winix.TreeX/Cli.cs` (~line 444):
```csharp
            new CommandLineParser("treex", version)
                .Description("Enhanced directory tree with colour, filtering, size rollups, and clickable hyperlinks.")
                .StandardFlags()
                .ExpandGlobPositionals()
                .Flag("--ndjson", "Streaming NDJSON output")
```
Note: treex's `--glob`/`-g` LIST OPTION values are untouched by design — only positional roots expand. Mention this in the README section ("`--glob` patterns are matched by treex itself and are never expanded").
- [ ] **Step 2: Tests** — Run: `dotnet test tests/Winix.TreeX.Tests/Winix.TreeX.Tests.csproj`. Expected: PASS (snapshot updates intentional if tripped).
- [ ] **Step 3: Smoke**:
```powershell
dotnet build src/treex/treex.csproj
$exe = (Resolve-Path "src\treex\bin\Debug\net10.0\treex.exe").Path
$x = "$env:TEMP\globsmoke\tx"; New-Item -ItemType Directory -Force "$x\one","$x\two" | Out-Null
Set-Content "$x\one\f.txt" 'f'
cmd /c "cd /d $x && `"$exe`" *"                  # EXPECT: trees for one/ and two/ (dirs as roots)
cmd /c "cd /d $x && `"$exe`" --glob *.txt one"   # EXPECT: --glob value NOT expanded (treex filters), one/ walked
```
Capture to `artifacts/v0.4-smoke/glob/treex.txt`. Verify `--help` + `--describe`.
- [ ] **Step 4: Docs** — canonical README section ({smoke-example} = `treex src*`, {quoted-example} = `"src*"`) plus the `--glob`-not-expanded sentence; docs/ai addition; man page (check `.1.md` first).
- [ ] **Step 5: Commit**
```bash
git add src/Winix.TreeX/Cli.cs src/treex/README.md src/treex/man docs/ai/treex.md tests/Winix.TreeX.Tests
git commit -m "feat(treex): expand * and ? wildcards in root positionals on Windows"
```

### Task 13: files adoption

**Files:** `src/Winix.Files/Cli.cs` (~line 402), `src/files/README.md`, `src/files/man/man1/files.1` (or source), `docs/ai/files.md`

- [ ] **Step 1: Opt-in** — in `src/Winix.Files/Cli.cs` (~line 404):
```csharp
            new CommandLineParser("files", version)
                .Description("Find files by name, size, date, type, and content.")
                .StandardFlags()
                .ExpandGlobPositionals()
                .Positional("paths...")
```
Same note as treex: `--glob`/`-g` and `--regex` values are never expanded — files matches those itself. Say so explicitly in the README section.
- [ ] **Step 2: Tests** — Run: `dotnet test tests/Winix.Files.Tests/Winix.Files.Tests.csproj`. Expected: PASS (snapshot updates intentional if tripped).
- [ ] **Step 3: Smoke**:
```powershell
dotnet build src/files/files.csproj
$exe = (Resolve-Path "src\files\bin\Debug\net10.0\files.exe").Path
$x = "$env:TEMP\globsmoke\tx"   # reuse treex fixture (one/, two/, one\f.txt)
cmd /c "cd /d $x && `"$exe`" *"                  # EXPECT: walks one/ and two/ as roots
cmd /c "cd /d $x && `"$exe`" --glob *.txt one"   # EXPECT: --glob value NOT expanded; finds one\f.txt
cmd /c "cd /d $x && `"$exe`" **"                  # EXPECT: usage error mentioning '**'
```
Capture to `artifacts/v0.4-smoke/glob/files.txt`. Verify `--help` + `--describe`.
- [ ] **Step 4: Docs** — canonical README section ({smoke-example} = `files src*`, {quoted-example} = `"src*"`) plus the `--glob`-not-expanded sentence; docs/ai addition; man page (check `.1.md` first — files had the original pandoc-trap incident; its `.1.md` source almost certainly exists).
- [ ] **Step 5: Commit**
```bash
git add src/Winix.Files/Cli.cs src/files/README.md src/files/man docs/ai/files.md tests/Winix.Files.Tests
git commit -m "feat(files): expand * and ? wildcards in root positionals on Windows"
```

---

### Task 14: Suite verification, Git Bash idempotency, CI, wrap-up

**Files:**
- Modify: `CLAUDE.md` (convention note)
- Modify: `llms.txt` only if it documents per-tool flag surfaces (check; likely no change)

- [ ] **Step 1: Full solution test run**

Run: `dotnet test Winix.sln`
Expected: 0 failures (platform-skips OK). This proves non-adopting tools are untouched.

- [ ] **Step 2: Git Bash idempotency spot-check**

From Git Bash (bash expands first; the tool must not double-mangle):
```bash
cd "$TMP/globsmoke" 2>/dev/null || cd /tmp/globsmoke
/d/projects/winix/src/digest/bin/Debug/net10.0/digest.exe *.txt   # EXPECT: two hash lines, same as cmd
/d/projects/winix/src/digest/bin/Debug/net10.0/digest.exe "*.zip" # EXPECT: bash passes literal; tool may expand → still not-found (no *.zip files). Record actual behaviour in artifacts.
```
Capture to `artifacts/v0.4-smoke/glob/gitbash.txt`.

- [ ] **Step 3: AOT publish sanity (one adopter)**

Run: `dotnet publish src/digest/digest.csproj -c Release -r win-x64`
Expected: publish succeeds with zero trim/AOT warnings from the new ShellKit code (tokenizer/expander use no reflection). Run the published exe once in the smoke dir: `digest.exe *.txt` → two hash lines.

- [ ] **Step 4: CLAUDE.md convention note**

In `CLAUDE.md`, in the "When adding a new tool" bullet list, add after the arg-parsing bullet:
```markdown
  - If ALL the tool's positionals are file paths, opt into Windows glob expansion with `.ExpandGlobPositionals()` (after `.StandardFlags()`). Never opt in for subcommand verbs, cron expressions, or other positionals that legitimately contain `*` (use `skipFirst:` if mixing). See docs/plans/2026-06-05-glob-expansion-design.md.
```

- [ ] **Step 5: Commit, push, 3-OS CI**

```bash
git add CLAUDE.md artifacts/v0.4-smoke/glob
git commit -m "docs: glob-expansion convention note + smoke captures"
git push -u origin feature/glob-expansion
gh workflow run ci.yml --ref feature/glob-expansion
```
Watch the run (`gh run list --branch feature/glob-expansion`); expected green on Windows/Linux/macOS (Linux/macOS exercise the engine fake-FS tests + non-Windows no-op gate; oracle/real-FS tests skip).

- [ ] **Step 6: Hand back for review + merge decision**

Do NOT merge to `release/v0.4.0` yet — per project workflow, a fresh-eyes review (4-reviewer dispatch incl. docs-auditor) follows implementation, then `--no-ff` merge (mkauth precedent).

---

## Self-review notes (done at authoring time)

- **Spec coverage:** every design-doc rule-table row has a test (tokenizer/engine/parser layer) or a smoke step; help/describe advertisement → Task 7; six adopters → Tasks 8–13; trash #6 → Task 10; AOT → Task 14.
- **Verified-against-source:** parser internals (positional collection sites ~lines 320/340, help sections loop ~line 721, describe writer style ~lines 787–909), adopter chain locations (Explore agent, 2026-06-05), net10.0 TFM, existing `InternalsVisibleTo`.
- **Marked verify-at-implementation:** the CRT `""`-inside-quotes rule (Task 2 vector exists but Task 3's oracle is ground truth — fix tokenizer to match oracle, never vice versa); ArgvEcho apphost copy behaviour (Task 1 Step 2 has the fallback); exact line numbers may have drifted by the time of execution — match on the quoted context, not the number.
