# trash Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `trash` — a cross-platform CLI that moves files/dirs to the OS recycle bin/Trash, lists the bin, and empties it.

**Architecture:** `Winix.Trash` class library (logic + formatting + three `ITrashBackend` implementations) behind a thin `trash` console app. `Cli.Run` takes an injectable `ITrashBackend?` seam (mirrors mksecret's `ISecureRandom?`) so orchestration is unit-testable with a fake backend; pure wire-format helpers (`.trashinfo`, Windows `$I`) are unit-tested against literal fixtures; real backends are covered by platform-gated `SkippableFact` integration tests.

**Tech Stack:** .NET 10, C#, AOT, `Yort.ShellKit.CommandLineParser`, `LibraryImport` P/Invoke (Win32 Shell + `libobjc`), xUnit + Xunit.SkippableFact.

**Conventions:** TDD (write failing test → verify fail → implement → verify pass → commit). Full braces, `#nullable enable`, warnings-as-errors. `--json` to stdout, summary to stderr. `SkippableFact` + `Skip.IfNot` + redundant `if (!IsX()) return;` for platform tests. Stage files using the casing shown by `git status`.

**Reference docs:** [design](2026-05-29-trash-design.md), [ADR](2026-05-29-trash-adr.md).

> **Native-interop verification gates (read before Tasks 9, 12, 15):** The Win32 `SHFILEOPSTRUCTW` layout/flags, the Windows `$I` binary format, the FreeDesktop `.trashinfo` text format, and the `objc_msgSend`/`trashItem` selector signatures below are written from documented specs but MUST be verified at implementation against MS Learn / Apple headers / the FreeDesktop spec, and proven by the platform integration tests (Tasks 11, 14, 17). Where a value is marked **⚠VERIFY**, confirm it before relying on it.

---

## File structure

```
src/Winix.Trash/
  TrashedItem.cs          — record: Name, OriginalPath?, DeletedUtc, SizeBytes?, TrashLocation
  TrashResult.cs          — per-path outcomes + AllSucceeded/AnyFailed
  EmptyResult.cs          — count emptied
  ITrashBackend.cs        — Trash / List / Empty
  TrashBackendFactory.cs  — OS dispatch → concrete backend
  ArgParser.cs            — ShellKit parser, flag-mode, mode mutual-exclusion
  Formatting.cs           — list table, JSON envelopes, stderr summary (pure)
  Cli.cs                  — Run(args, stdout, stderr, backend?) orchestration + --empty gating
  TrashInfo.cs            — FreeDesktop .trashinfo writer + parser (pure)
  RecycleMetadata.cs      — Windows $I parser (pure)
  MountResolver.cs        — Linux mount-point/top-dir resolution (pure, injectable)
  LinuxFreeDesktopBackend.cs
  WindowsRecycleBinBackend.cs   + WindowsRecycleBinBackend.Interop.cs
  MacOsTrashBackend.cs          + MacOsTrashBackend.Interop.cs
src/trash/
  Program.cs              — thin shim
  README.md, man/man1/trash.1
tests/Winix.Trash.Tests/
  ArgParserTests.cs, FormattingTests.cs, CliTests.cs (fake backend),
  TrashInfoTests.cs, RecycleMetadataTests.cs, MountResolverTests.cs,
  FakeTrashBackend.cs,
  IntegrationTests_Windows.cs, IntegrationTests_Linux.cs, IntegrationTests_MacOS.cs
docs/ai/trash.md, llms.txt entry, bucket/trash.json, CLAUDE.md updates,
.github/workflows/release.yml + post-publish.yml entries
```

---

## Task 1: Project scaffold (lib + console + tests)

**Files:**
- Create: `src/Winix.Trash/Winix.Trash.csproj`, `src/trash/trash.csproj`, `tests/Winix.Trash.Tests/Winix.Trash.Tests.csproj`
- Modify: `Winix.sln`

- [ ] **Step 1: Create the library csproj** — copy `src/Winix.MkSecret/Winix.MkSecret.csproj` and adapt:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
    <IsPackable>false</IsPackable>
    <PackageId>Winix.Trash.Library</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
    <InternalsVisibleTo Include="Winix.Trash.Tests" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the console csproj** — copy `src/mksecret/mksecret.csproj`, change `ToolCommandName`/`PackageId` to `trash`/`Winix.Trash`, set `<Description>` to `Cross-platform safe-delete to the OS recycle bin / Trash — list and empty included.`, `<PackageTags>` to the shared baseline `cli;command-line;cross-platform;windows;macos;linux;aot;dotnet-tool;winix` plus `trash;recycle-bin;rm;delete;undo`, reference `..\Winix.Trash\Winix.Trash.csproj` + `..\Yort.ShellKit\Yort.ShellKit.csproj`, and include the man page `Content Include="man\man1\trash.1"`.

- [ ] **Step 3: Create the test csproj** — copy `tests/Winix.MkSecret.Tests/Winix.MkSecret.Tests.csproj`, set `InvariantGlobalization=true`, reference `..\..\src\Winix.Trash\Winix.Trash.csproj`, and add the `Xunit.SkippableFact` PackageReference (copy the version used in `tests/Winix.EnvVault.Tests/*.csproj`).

- [ ] **Step 4: Add all three projects to the solution**

Run: `dotnet sln Winix.sln add src/Winix.Trash/Winix.Trash.csproj src/trash/trash.csproj tests/Winix.Trash.Tests/Winix.Trash.Tests.csproj`

- [ ] **Step 5: Verify it builds (empty projects)**

Run: `dotnet build src/Winix.Trash/Winix.Trash.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Commit**

```
git add src/Winix.Trash src/trash tests/Winix.Trash.Tests Winix.sln
git commit -m "feat(trash): project scaffold (lib + console + tests)"
```

---

## Task 2: Core types

**Files:**
- Create: `src/Winix.Trash/TrashedItem.cs`, `TrashResult.cs`, `EmptyResult.cs`, `ITrashBackend.cs`

- [ ] **Step 1: Write `TrashedItem.cs`**

```csharp
namespace Winix.Trash;

/// <summary>One item residing in the trash, as surfaced by <see cref="ITrashBackend.List"/>.</summary>
/// <param name="Name">The item's display name in the trash.</param>
/// <param name="OriginalPath">The absolute path the item was deleted from, or null when the
/// backend cannot recover it (macOS — the Put-Back source is in the private store).</param>
/// <param name="DeletedUtc">When the item was trashed (UTC), or null if unknown.</param>
/// <param name="SizeBytes">Size in bytes, or null if not cheaply available.</param>
/// <param name="TrashLocation">Which trash holds it: "home" or a mount path (Linux), drive (Windows).</param>
public sealed record TrashedItem(
    string Name,
    string? OriginalPath,
    System.DateTime? DeletedUtc,
    long? SizeBytes,
    string TrashLocation);
```

- [ ] **Step 2: Write `TrashResult.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Winix.Trash;

/// <summary>Outcome of a trash operation over one or more paths.</summary>
public sealed class TrashResult
{
    /// <summary>Per-path outcomes in input order.</summary>
    public required IReadOnlyList<PathOutcome> Outcomes { get; init; }

    /// <summary>Count of paths trashed successfully.</summary>
    public int SuccessCount => Outcomes.Count(o => o.Error is null);

    /// <summary>True when at least one path failed for an operational reason.</summary>
    public bool AnyFailed => Outcomes.Any(o => o.Error is not null);
}

/// <summary>The outcome for a single input path. <see cref="Error"/> is null on success.</summary>
public sealed record PathOutcome(string Path, string? Error);
```

- [ ] **Step 3: Write `EmptyResult.cs`**

```csharp
namespace Winix.Trash;

/// <summary>Outcome of emptying the trash.</summary>
/// <param name="ItemsRemoved">Number of top-level items permanently removed.</param>
public sealed record EmptyResult(int ItemsRemoved);
```

- [ ] **Step 4: Write `ITrashBackend.cs`**

```csharp
using System.Collections.Generic;

namespace Winix.Trash;

/// <summary>Abstraction over the per-OS recycle bin / Trash. Implementations must not throw for
/// per-path operational failures (missing path, permission) — they record those in the returned
/// <see cref="TrashResult"/>. They MAY throw for catastrophic backend failure (OS API error),
/// which <see cref="Cli"/> maps to exit 126.</summary>
public interface ITrashBackend
{
    /// <summary>Sends each path to the trash. Recoverable; never prompts.</summary>
    TrashResult Trash(IReadOnlyList<string> paths);

    /// <summary>Enumerates the items currently in the trash.</summary>
    IReadOnlyList<TrashedItem> List();

    /// <summary>Permanently empties the trash. Returns the number of items removed.</summary>
    EmptyResult Empty();
}
```

- [ ] **Step 5: Build** — Run: `dotnet build src/Winix.Trash/Winix.Trash.csproj` — Expected: succeeded, 0 warnings.

- [ ] **Step 6: Commit**

```
git add src/Winix.Trash/TrashedItem.cs src/Winix.Trash/TrashResult.cs src/Winix.Trash/EmptyResult.cs src/Winix.Trash/ITrashBackend.cs
git commit -m "feat(trash): core types + ITrashBackend interface"
```

---

## Task 3: FreeDesktop `.trashinfo` writer + parser (pure)

**Files:**
- Create: `src/Winix.Trash/TrashInfo.cs`, `tests/Winix.Trash.Tests/TrashInfoTests.cs`

> **⚠VERIFY** the format against the FreeDesktop Trash spec at implementation. Documented format:
> a `[Trash Info]` group, `Path=` (absolute path, percent-encoded per RFC 2396 — all bytes except
> unreserved chars and `/` are `%XX`-escaped), `DeletionDate=` in **local** time as
> `YYYY-MM-DDThh:mm:ss` (no timezone suffix).

- [ ] **Step 1: Write failing tests** (`TrashInfoTests.cs`)

```csharp
using System;
using Winix.Trash;
using Xunit;

namespace Winix.Trash.Tests;

public class TrashInfoTests
{
    [Fact]
    public void Write_ProducesSpecFormat_WithPercentEncodedPathAndLocalDate()
    {
        // Path keeps '/' literal; space → %20. Date is local, no timezone suffix.
        string info = TrashInfo.Write("/home/u/My File.txt", new DateTime(2024, 8, 31, 22, 46, 31, DateTimeKind.Local));
        Assert.Equal(
            "[Trash Info]\nPath=/home/u/My%20File.txt\nDeletionDate=2024-08-31T22:46:31\n",
            info);
    }

    [Fact]
    public void Parse_RoundTripsPathAndDate_FromLiteralSpecText()
    {
        // Literal fixture (NOT produced by Write) so a wrong codec on either side is detectable.
        string fixture = "[Trash Info]\nPath=/var/tmp/a%2Bb.bin\nDeletionDate=2023-01-02T03:04:05\n";
        TrashInfoRecord r = TrashInfo.Parse(fixture);
        Assert.Equal("/var/tmp/a+b.bin", r.OriginalPath);     // %2B → '+'
        Assert.Equal(new DateTime(2023, 1, 2, 3, 4, 5), r.DeletionLocal);
    }

    [Fact]
    public void Parse_ReturnsNull_OnMissingPathKey()
    {
        Assert.Null(TrashInfo.Parse("[Trash Info]\nDeletionDate=2023-01-02T03:04:05\n"));
    }
}
```

- [ ] **Step 2: Run — Expected: FAIL** (`TrashInfo`/`TrashInfoRecord` not defined). Run: `dotnet test tests/Winix.Trash.Tests/Winix.Trash.Tests.csproj --filter "FullyQualifiedName~TrashInfo" -v q`

- [ ] **Step 3: Implement `TrashInfo.cs`**

```csharp
using System;
using System.Globalization;
using System.Text;

namespace Winix.Trash;

/// <summary>Parsed contents of a FreeDesktop <c>.trashinfo</c> file.</summary>
/// <param name="OriginalPath">Decoded absolute original path.</param>
/// <param name="DeletionLocal">Deletion timestamp as written (local, no timezone).</param>
public sealed record TrashInfoRecord(string OriginalPath, DateTime DeletionLocal);

/// <summary>Reads and writes FreeDesktop <c>.trashinfo</c> files. Path values are percent-encoded
/// per RFC 2396 (every byte except RFC-2396 unreserved chars and the path separator <c>/</c>);
/// DeletionDate is local time formatted <c>yyyy-MM-ddTHH:mm:ss</c> with no timezone suffix.</summary>
public static class TrashInfo
{
    // RFC 2396 unreserved set, plus '/' which the spec keeps literal in Path.
    private const string Unreserved =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.!~*'()/";

    /// <summary>Serialises a <c>.trashinfo</c> body (LF line endings, trailing newline).</summary>
    public static string Write(string originalPath, DateTime deletionLocal)
    {
        var sb = new StringBuilder("[Trash Info]\n");
        sb.Append("Path=").Append(Encode(originalPath)).Append('\n');
        sb.Append("DeletionDate=")
          .Append(deletionLocal.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))
          .Append('\n');
        return sb.ToString();
    }

    /// <summary>Parses a <c>.trashinfo</c> body, or null if Path/DeletionDate are missing/invalid.</summary>
    public static TrashInfoRecord? Parse(string body)
    {
        string? path = null;
        DateTime? date = null;
        foreach (string raw in body.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("Path=", StringComparison.Ordinal))
            {
                path = Decode(line.Substring("Path=".Length));
            }
            else if (line.StartsWith("DeletionDate=", StringComparison.Ordinal))
            {
                if (DateTime.TryParseExact(line.Substring("DeletionDate=".Length),
                        "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime d))
                {
                    date = d;
                }
            }
        }
        if (path is null || date is null) { return null; }
        return new TrashInfoRecord(path, date.Value);
    }

    private static string Encode(string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        var sb = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
        {
            char c = (char)b;
            if (b < 0x80 && Unreserved.IndexOf(c) >= 0) { sb.Append(c); }
            else { sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture)); }
        }
        return sb.ToString();
    }

    private static string Decode(string s)
    {
        var bytes = new System.Collections.Generic.List<byte>(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '%' && i + 2 < s.Length + 1 && i + 2 <= s.Length - 0 && i + 2 < s.Length + 1)
            {
                bytes.Add(Convert.ToByte(s.Substring(i + 1, 2), 16));
                i += 2;
            }
            else { bytes.Add((byte)s[i]); }
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
```

> **Note for implementer:** the `Decode` bounds check above is deliberately written to be corrected — simplify to `if (s[i] == '%' && i + 2 < s.Length)`. Verify decode of a trailing bare `%` does not throw (add a test if needed).

- [ ] **Step 4: Run — Expected: PASS.** Fix the `Decode` bounds expression until green.

- [ ] **Step 5: Commit**

```
git add src/Winix.Trash/TrashInfo.cs tests/Winix.Trash.Tests/TrashInfoTests.cs
git commit -m "feat(trash): FreeDesktop .trashinfo writer + parser"
```

---

## Task 4: Windows `$I` metadata parser (pure)

**Files:**
- Create: `src/Winix.Trash/RecycleMetadata.cs`, `tests/Winix.Trash.Tests/RecycleMetadataTests.cs`

> **⚠VERIFY** the `$I` v2 format against MS docs / a real captured `$I` file. Documented layout:
> bytes 0-7 = header (`0x02` little-endian Int64 for Win10+); 8-15 = original file size (Int64 LE);
> 16-23 = deletion time as Windows `FILETIME` (Int64 LE, 100ns ticks since 1601-01-01 UTC);
> 24-27 = path length in **UTF-16 chars including the null terminator** (Int32 LE); 28.. =
> original path as null-terminated UTF-16LE. **Capture a real `$I` fixture** (trash a file on
> Windows, read `C:\$Recycle.Bin\<SID>\$I*`) and pin the test bytes to it.

- [ ] **Step 1: Write failing test** with a hand-built v2 byte buffer (replace with a captured fixture at implementation if it differs):

```csharp
using System;
using Winix.Trash;
using Xunit;

namespace Winix.Trash.Tests;

public class RecycleMetadataTests
{
    [Fact]
    public void Parse_V2_ReadsSizeDeletionTimeAndPath()
    {
        // ⚠VERIFY against a real $I capture. FILETIME for 2024-01-01T00:00:00Z.
        long filetime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        string path = @"C:\Users\u\note.txt";
        var buf = new System.IO.MemoryStream();
        var w = new System.IO.BinaryWriter(buf);
        w.Write(2L);                       // header version
        w.Write(1234L);                    // original size
        w.Write(filetime);                 // deletion FILETIME
        w.Write(path.Length + 1);          // chars incl. null
        foreach (char c in path) { w.Write((ushort)c); }
        w.Write((ushort)0);                // null terminator
        w.Flush();

        RecycleEntry e = RecycleMetadata.ParseIFile(buf.ToArray());
        Assert.Equal(path, e.OriginalPath);
        Assert.Equal(1234L, e.SizeBytes);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), e.DeletedUtc);
    }
}
```

- [ ] **Step 2: Run — Expected: FAIL** (types missing).

- [ ] **Step 3: Implement `RecycleMetadata.cs`**

```csharp
using System;
using System.Buffers.Binary;
using System.Text;

namespace Winix.Trash;

/// <summary>One parsed Windows Recycle Bin <c>$I</c> metadata record.</summary>
public sealed record RecycleEntry(string OriginalPath, long SizeBytes, DateTime DeletedUtc);

/// <summary>Parses Windows Recycle Bin <c>$I</c> metadata files (format version 2, Win10+).
/// Pure byte-level parsing — no Shell COM — so it is AOT-clean and unit-testable on any OS.</summary>
public static class RecycleMetadata
{
    /// <summary>Parses one <c>$I</c> file's bytes.</summary>
    /// <exception cref="FormatException">If the buffer is too short or malformed.</exception>
    public static RecycleEntry ParseIFile(byte[] bytes)
    {
        if (bytes.Length < 28) { throw new FormatException("$I file too short"); }
        long size = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(8, 8));
        long filetime = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(16, 8));
        int charCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24, 4));
        int pathByteLen = (charCount - 1) * 2; // exclude null terminator
        if (pathByteLen < 0 || 28 + pathByteLen > bytes.Length) { throw new FormatException("$I path length invalid"); }
        string path = Encoding.Unicode.GetString(bytes, 28, pathByteLen);
        return new RecycleEntry(path, size, DateTime.FromFileTimeUtc(filetime));
    }
}
```

- [ ] **Step 4: Run — Expected: PASS.**
- [ ] **Step 5: Commit** — `git commit -m "feat(trash): Windows \$I recycle-metadata parser"`

---

## Task 5: Argument parser (ShellKit, flag-mode)

**Files:**
- Create: `src/Winix.Trash/ArgParser.cs`, `tests/Winix.Trash.Tests/ArgParserTests.cs`

**Mode rules:** bare positionals → Trash mode; `--list` → List mode; `--empty` → Empty mode.
`--list`/`--empty` are mutually exclusive with each other and with positional paths. `--yes`,
`--json` are flags. Build on `Yort.ShellKit.CommandLineParser` with `.StandardFlags()` (which
already registers `--json` — **do NOT re-add it**, per the mksecret duplicate-`--json` fix).

- [ ] **Step 1: Write failing tests** (cover: bare paths→Trash; `--list`→List; `--empty`→Empty;
  `--empty` + path → usage error; `--list --empty` → usage error; `--yes` parsed; unknown flag → error).
  Model the test shape on `tests/Winix.MkSecret.Tests/ArgParserTests.cs`. (Implementer: write one
  `[Fact]`/`[Theory]` per rule above with explicit asserts on the parsed mode + flags.)

- [ ] **Step 2: Run — Expected: FAIL.**

- [ ] **Step 3: Implement `ArgParser.cs`** — mirror `src/Winix.MkSecret/ArgParser.cs` structure: a
  `Result` record `(TrashMode Mode, IReadOnlyList<string> Paths, bool Yes, bool Json, string? Error,
  bool IsHandled, int ExitCode, bool UseColor)`; an enum `TrashMode { Trash, List, Empty }`; a single
  `CommonShell` parser via `.StandardFlags()` + `.Platform(...)` (replaces: `rm -i`, `trash-cli`,
  `macos-trash`, PowerShell `Remove-Item`) + `.ExitCodes((0,"Success ..."),(125,"Usage error ..."),
  (1,"One or more paths failed ..."),(126,"Backend failure ..."))` + `.Flag("--list",...)`,
  `.Flag("--empty",...)`, `.Flag("--yes","-y",...)` + Examples + a `Storing`-style Section is not
  needed. After parse: if `Has("--list")` && `Has("--empty")` → Fail("--list and --empty are mutually
  exclusive"); if `(Has("--list")||Has("--empty"))` && `Positionals.Length>0` → Fail; if neither mode
  flag && `Positionals.Length==0` → Fail("no paths given; see --help"). Reuse `ResolveVersion()` from
  mksecret verbatim.

- [ ] **Step 4: Run — Expected: PASS.**
- [ ] **Step 5: Commit** — `git commit -m "feat(trash): ShellKit arg parser with flag-mode dispatch"`

---

## Task 6: Formatting (pure)

**Files:**
- Create: `src/Winix.Trash/Formatting.cs`, `tests/Winix.Trash.Tests/FormattingTests.cs`

- [ ] **Step 1: Write failing tests** for: `TrashSummary(int n)` → `"trash: moved 3 item(s) to trash"`;
  `ListTable(items)` → aligned columns (name, deleted date, original path or "—"); `ListJson(items)`
  → `{"items":[{"name":"a","original_path":"/x/a","deleted":"2024-01-01T00:00:00Z","size":12,"trash":"home"}]}`
  with `original_path`/`size` omitted when null; `EmptyJson(n)` → `{"emptied":5}`. Pin exact JSON
  strings (reuse mksecret's `AppendJsonString` escaping approach — copy that helper).

- [ ] **Step 2: Run — Expected: FAIL.**
- [ ] **Step 3: Implement `Formatting.cs`** — pure string builders; UTC ISO-8601 (`"yyyy-MM-ddTHH:mm:ssZ"`,
  InvariantCulture) for `deleted`; copy the JSON-string escaper from `src/Winix.MkSecret/Formatting.cs`.
- [ ] **Step 4: Run — Expected: PASS.**
- [ ] **Step 5: Commit** — `git commit -m "feat(trash): list table + JSON envelope formatting"`

---

## Task 7: Fake backend + Cli orchestration (seam, no real trash)

**Files:**
- Create: `tests/Winix.Trash.Tests/FakeTrashBackend.cs`, `src/Winix.Trash/Cli.cs`, `tests/Winix.Trash.Tests/CliTests.cs`

- [ ] **Step 1: Write `FakeTrashBackend.cs`** — records calls; returns scripted `TrashResult`/items/
  `EmptyResult`; a constructor flag makes `Empty()`/`Trash()` throw to exercise the 126 path.

- [ ] **Step 2: Write failing `CliTests.cs`** covering, all via the fake backend:
  - Trash mode: summary to stderr, exit 0; `--json` → trash envelope to stdout.
  - Trash with one failed path → exit 1; all-fail → exit 1.
  - `--list` → table to stdout (exit 0); `--json` → items envelope to stdout, nothing to stderr.
  - `--empty` with `--yes` → calls `Empty()`, summary, exit 0.
  - `--empty` non-interactive **without** `--yes` → does NOT call `Empty()`, exit 0 with a stderr
    notice ("refusing to empty without --yes when not interactive"). (Interactive prompt path is
    covered by manual smoke — see Task 18; the unit test pins the non-interactive gate.)
  - Backend throws → exit 126, `trash: error:` on stderr, nothing on stdout.
  - Usage error from ArgParser → exit 125.

- [ ] **Step 3: Run — Expected: FAIL.**

- [ ] **Step 4: Implement `Cli.cs`** — signature
  `public static int Run(string[] args, TextWriter stdout, TextWriter stderr, ITrashBackend? backendOverride = null, Func<bool>? isInteractiveOverride = null, Func<string?>? readLineOverride = null)`.
  Parse; handle `IsHandled`/usage (exit 125). Resolve backend = `backendOverride ?? TrashBackendFactory.Create()`.
  Dispatch on mode. For Empty: determine interactivity via `isInteractiveOverride ?? (() => !Console.IsInputRedirected)`;
  if interactive, prompt to stderr + read a line (via `readLineOverride ?? Console.In.ReadLine`) and require y/Y;
  if non-interactive, require `r.Yes` else print the refusal notice and return 0 without calling `Empty()`.
  Wrap backend calls in `try { ... } catch (IOException) { return 0; } catch (Exception ex) { stderr.WriteLine($"trash: error: {ex.Message}"); return 126; }`.
  **Apply the mksecret IOException lesson:** only swallow `IOException` as success for the *write* path
  (a closed downstream pipe) — do NOT let it mask a backend failure; structure so backend exceptions go
  through the `catch (Exception)` → 126 arm. (Implementer: keep the backend call outside the narrow
  pipe-write catch, mirroring `src/Winix.MkSecret/Cli.cs`.)
  Exit code for Trash mode: `result.AnyFailed ? 1 : 0`.

- [ ] **Step 5: Run — Expected: PASS.**
- [ ] **Step 6: Commit** — `git commit -m "feat(trash): Cli orchestration seam + --empty safety gating"`

---

## Task 8: Backend factory + Program.cs

**Files:**
- Create: `src/Winix.Trash/TrashBackendFactory.cs`, `src/trash/Program.cs`

- [ ] **Step 1:** Implement `TrashBackendFactory.Create()` dispatching on `OperatingSystem.IsWindows/IsLinux/IsMacOS()`
  to the three backends (added in Tasks 9/12/15); throw `PlatformNotSupportedException` otherwise.
  For now, have the not-yet-written backends referenced as `throw new NotImplementedException()` stubs in
  their files (created in later tasks) OR gate the factory so only the current-OS backend must exist.
  (Implementer: create minimal stub backend classes returning `NotImplementedException` so the solution
  compiles; each is fleshed out in its task.)

- [ ] **Step 2:** Write `Program.cs` (copy `src/mksecret/Program.cs`): `ConsoleEnv.EnableAnsiIfNeeded()`,
  `ConsoleEnv.UseUtf8Streams()`, `return Cli.Run(args, Console.Out, Console.Error);`.

- [ ] **Step 3: Build the console** — Run: `dotnet build src/trash/trash.csproj` — Expected: succeeded.
- [ ] **Step 4: Commit** — `git commit -m "feat(trash): backend factory + console shim"`

---

## Task 9: Linux backend — trash + the home-volume path

**Files:**
- Create: `src/Winix.Trash/LinuxFreeDesktopBackend.cs`, `src/Winix.Trash/MountResolver.cs`, `tests/Winix.Trash.Tests/MountResolverTests.cs`

- [ ] **Step 1:** Write failing `MountResolverTests` for a pure helper `MountResolver.HomeTrashDir()` →
  `$XDG_DATA_HOME/Trash` or `~/.local/share/Trash`, and `MountResolver.ResolveTrashDir(string filePath,
  Func<string,ulong> deviceIdOf, string homeTrashDir, ulong homeDeviceId)` → returns the home trash when
  the file's device == home device, else `$topdir/.Trash-$uid` (computed from the mount). Inject
  `deviceIdOf` so the logic is testable without real mounts.
- [ ] **Step 2: Run — FAIL.**
- [ ] **Step 3:** Implement `MountResolver` (pure; uses injected device lookup). Real device id comes
  from `stat`'s `st_dev` via `File.GetUnixFileInfo`/`LibraryImport stat` in the backend (Task 12 covers
  multi-volume integration; home-volume uses `Directory.Move`/`File.Move`).
- [ ] **Step 4:** Implement `LinuxFreeDesktopBackend.Trash` for the **home-volume** case: ensure
  `files/` + `info/` exist; pick a collision-free name; write `<name>.trashinfo` via `TrashInfo.Write`
  with `DateTime.Now`; move the file into `files/`. On per-path failure, record in `TrashResult` (no throw).
  `List()` reads every `.trashinfo` in `info/` → `TrashedItem`. `Empty()` deletes `files/*` + `info/*`,
  returns count.
- [ ] **Step 5: Run — Expected: PASS** (MountResolver tests; backend covered by Task 11 integration).
- [ ] **Step 6: Commit** — `git commit -m "feat(trash): Linux FreeDesktop backend (home volume) + mount resolver"`

---

## Task 10: Linux backend — multi-volume (top-dir trash)

- [ ] **Step 1:** Extend `LinuxFreeDesktopBackend` to use `MountResolver.ResolveTrashDir` so a file on
  another mount goes to `$topdir/.Trash-$uid` (create with `0700`; honor the spec's `$topdir/.Trash`
  sticky-bit/symlink checks — **⚠VERIFY** against the spec). `List()` scans home trash **and** known
  top-dir trashes; tag `TrashLocation` accordingly.
- [ ] **Step 2:** No new unit test (logic is in `MountResolver`, already tested); multi-volume is
  proven by the Task 11 integration test on a bind-mount/tmpfs.
- [ ] **Step 3: Commit** — `git commit -m "feat(trash): Linux multi-volume top-dir trash"`

---

## Task 11: Linux integration tests (`SkippableFact`)

**Files:** Create `tests/Winix.Trash.Tests/IntegrationTests_Linux.cs`

- [ ] **Step 1:** Write `SkippableFact` tests (`Skip.IfNot(OperatingSystem.IsLinux(), ...)` + redundant
  `if (!IsLinux()) return;`): create a temp file under `$HOME`, trash it, assert (a) gone from origin,
  (b) present in `files/`, (c) a `.trashinfo` exists with the correct decoded original path; then
  self-clean (remove the trashed file + its `.trashinfo`). A second test: `List()` includes the item.
  A third (multi-volume): create a tmpfs/bind-mount under a temp dir if creatable (else `Skip`), trash a
  file on it, assert `$topdir/.Trash-$uid` used. **Deterministic, self-cleaning, no timing/signals.**
- [ ] **Step 2: Run in WSL** — Run: `wsl bash -lc "dotnet test /mnt/d/projects/winix/tests/Winix.Trash.Tests/Winix.Trash.Tests.csproj --filter FullyQualifiedName~IntegrationTests_Linux -v q"` — Expected: PASS.
- [ ] **Step 3: Commit** — `git commit -m "test(trash): Linux integration tests (trash/list/multi-volume)"`

---

## Task 12: Windows backend (SHFileOperation + $I list + empty)

**Files:** Create `src/Winix.Trash/WindowsRecycleBinBackend.cs` + `WindowsRecycleBinBackend.Interop.cs`

> **⚠VERIFY** all Win32 signatures against MS Learn. Documented essentials:
> `FO_DELETE = 3`; `FOF_SILENT=0x4`, `FOF_NOCONFIRMATION=0x10`, `FOF_ALLOWUNDO=0x40`,
> `FOF_NOERRORUI=0x400`. `SHFILEOPSTRUCTW.pFrom` is a **double-null-terminated** wide string
> (each path null-terminated, list terminated by an extra null). `SHFileOperationW` returns 0 on
> success. `SHEmptyRecycleBinW(IntPtr hwnd, string? rootPath, uint flags)` with
> `SHERB_NOCONFIRMATION=0x1 | SHERB_NOPROGRESSUI=0x2 | SHERB_NOSOUND=0x4`.

- [ ] **Step 1:** Implement `WindowsRecycleBinBackend.Interop.cs` with `LibraryImport`s for
  `SHFileOperationW` and `SHEmptyRecycleBinW` and the `SHFILEOPSTRUCTW` struct. **⚠VERIFY** the struct
  layout + that AOT marshalling of the double-null `pFrom` works (build the buffer manually as a
  `char[]`/`string` with embedded `\0` + trailing `\0\0`; pin it). Document why string `pFrom` is used
  rather than `ArgumentList` (this is a Win32 struct field, not a process arg — the CLAUDE.md
  `ArgumentList` rule is about child processes and does not apply).
- [ ] **Step 2:** Implement `Trash(paths)`: resolve each to a full path; build the double-null buffer;
  call `SHFileOperationW` with the four flags; on non-zero return, record a per-path error (the API is
  batch — if it fails, attribute to the batch). `Empty()`: `SHEmptyRecycleBinW(IntPtr.Zero, null, flags)`,
  return a best-effort count (count items via `List()` before emptying). `List()`: enumerate
  `<drive>\$Recycle.Bin\<currentSID>\$I*` across fixed drives, `RecycleMetadata.ParseIFile` each, pair
  with `TrashedItem` (Name from the `$R` sibling). Skip drives/dirs that throw access-denied.
- [ ] **Step 3:** Get current SID via `System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value`
  (**⚠VERIFY** AOT/trim-safe; if not, enumerate readable `<SID>` subfolders).
- [ ] **Step 4: Build** — `dotnet build src/Winix.Trash/Winix.Trash.csproj` — Expected: succeeded.
- [ ] **Step 5: Commit** — `git commit -m "feat(trash): Windows recycle-bin backend (SHFileOperation + \$I list)"`

---

## Task 13: (folded into Task 12) — n/a

## Task 14: Windows integration tests (`SkippableFact`)

**Files:** Create `tests/Winix.Trash.Tests/IntegrationTests_Windows.cs`

- [ ] **Step 1:** `SkippableFact` (`Skip.IfNot(OperatingSystem.IsWindows())` + redundant guard): write a
  uniquely-named temp file, `Trash` it, assert (a) gone from origin, (b) a matching `$I`/`$R` pair exists
  in `$Recycle.Bin\<SID>` whose parsed original path equals the temp file; then **self-clean** by deleting
  that specific `$I`/`$R` pair (NOT `SHEmptyRecycleBin`). Second test: `List()` includes the item.
- [ ] **Step 2: Run** — `dotnet test tests/Winix.Trash.Tests/Winix.Trash.Tests.csproj --filter FullyQualifiedName~IntegrationTests_Windows -v q` — Expected: PASS.
- [ ] **Step 3: Commit** — `git commit -m "test(trash): Windows integration tests (trash/list, self-cleaning)"`

---

## Task 15: macOS backend (NSFileManager.trashItem via objc interop)

**Files:** Create `src/Winix.Trash/MacOsTrashBackend.cs` + `MacOsTrashBackend.Interop.cs`

> **⚠VERIFY** the Obj-C interop on the macOS CI runner — this is the suite's first native Obj-C
> interop. Documented call chain: `objc_getClass("NSFileManager")` → send `defaultManager` →
> `objc_getClass("NSURL")` send `fileURLWithPath:` (with an `NSString` from
> `stringWithUTF8String:`) → send `trashItemAtURL:resultingItemURL:error:` (BOOL return, `NSError**`
> out). Declare a distinct `LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint="objc_msgSend")`
> per call signature (NativeAOT requires concrete signatures — no variadic). `sel_registerName`
> for each selector. **⚠VERIFY** each selector name + signature; wrap the whole thing so a failure
> surfaces as a thrown exception the backend converts to a per-path error.

- [ ] **Step 1:** Implement `MacOsTrashBackend.Interop.cs`: `LibraryImport`s for `objc_getClass`,
  `sel_registerName`, and the needed `objc_msgSend` overloads (IntPtr-returning for the object sends;
  a BOOL-returning overload with an `out IntPtr error` for `trashItemAtURL:...`). A helper
  `TrashViaFoundation(string fullPath) -> (bool ok, string? error)`.
- [ ] **Step 2:** Implement `MacOsTrashBackend.Trash(paths)`: for each, call `TrashViaFoundation`;
  record per-path error on failure (no throw). `List()`: enumerate `~/.Trash` (+ `/Volumes/*/.Trashes/<uid>`
  if readable) → `TrashedItem` with `OriginalPath = null` (documented limitation), `DeletedUtc` from the
  file's mtime, `SizeBytes` from the entry. `Empty()`: delete contents of `~/.Trash` (+ volume `.Trashes/<uid>`),
  return count.
- [ ] **Step 3: Build** — `dotnet build src/Winix.Trash/Winix.Trash.csproj` — Expected: succeeded (interop
  compiles on any OS; only runs on macOS).
- [ ] **Step 4: Commit** — `git commit -m "feat(trash): macOS backend (NSFileManager.trashItem via objc interop)"`

---

## Task 16: (folded into Task 15) — n/a

## Task 17: macOS integration tests (`SkippableFact`, CI-only verification)

**Files:** Create `tests/Winix.Trash.Tests/IntegrationTests_MacOS.cs`

- [ ] **Step 1:** `SkippableFact` (`Skip.IfNot(OperatingSystem.IsMacOS())` + redundant guard): create a
  uniquely-named temp file under `$HOME`, `Trash` it, assert (a) gone from origin, (b) a same-named entry
  appears in `~/.Trash`; then self-clean (remove it from `~/.Trash`). Second test: `List()` includes it
  with `OriginalPath == null`. **This is the proof the Obj-C interop works** — it runs on macOS CI.
- [ ] **Step 2:** Push and confirm the macOS CI job is green for this test (cannot run locally on Windows).
- [ ] **Step 3: Commit** — `git commit -m "test(trash): macOS integration tests (trashItem interop, CI-verified)"`

---

## Task 18: Manual smoke + full-suite verification

- [ ] **Step 1:** Build the real binary: `dotnet run --project src/trash -- <tempfile>`; confirm the file
  lands in the Windows Recycle Bin (visible in Explorer). Run `trash --list` and eyeball the table.
  Run `trash --empty` interactively and confirm the `[y/N]` prompt; run `echo n | trash --empty` and
  confirm it refuses without `--yes`; run `trash --empty --yes` and confirm it empties. Capture output.
- [ ] **Step 2:** Full Windows solution: `dotnet test Winix.sln` — Expected: 0 failed.
- [ ] **Step 3:** WSL Linux suite: `wsl bash -lc "dotnet test /mnt/d/projects/winix/tests/Winix.Trash.Tests/Winix.Trash.Tests.csproj -v q"` — Expected: 0 failed.
- [ ] **Step 4: Commit** any fixes from smoke; otherwise proceed.

---

## Task 19: Docs + suite wiring

**Files:** Create `src/trash/README.md`, `src/trash/man/man1/trash.1`, `docs/ai/trash.md`; modify
`llms.txt`, `bucket/trash.json`, `.github/workflows/release.yml`, `.github/workflows/post-publish.yml`, `CLAUDE.md`.

- [ ] **Step 1:** `README.md` (follow `src/mksecret/README.md`): description, install (scoop/nuget/winget),
  usage/examples (trash, `--list`, `--empty`, `--yes`, `--json`), options table, exit codes table (0/125/1/126
  with the closed-pipe-is-0 note), colour section, **Known limitations** (macOS `--list` no original paths;
  Windows glob; no `--restore` in v1), and acknowledge `trash-cli`/`macos-trash` as the established native tools.
- [ ] **Step 2:** `man/man1/trash.1` (groff; follow `src/mksecret/man/man1/mksecret.1`). Keep exit codes +
  limitations in sync with README + `--describe`.
- [ ] **Step 3:** `docs/ai/trash.md` (follow `docs/ai/mksecret.md`) and add the `llms.txt` entry.
- [ ] **Step 4:** `bucket/trash.json` (copy `bucket/mksecret.json`, adjust name/binary).
- [ ] **Step 5:** `release.yml`: add `trash` to the publish/pack/zip steps + the `tools:` map (copy the
  `mksecret` lines). `post-publish.yml`: add `update_manifest bucket/trash.json …` and
  `generate_manifests "trash" "Trash" "…" "recycle-bin,delete,undo,rm"`.
- [ ] **Step 6:** `CLAUDE.md`: add to project layout, NuGet package IDs list, and scoop manifests list.
- [ ] **Step 7: Commit** — `git commit -m "docs(trash): README, man page, AI guide, llms.txt, scoop + release wiring, CLAUDE.md"`

---

## Task 20: Cross-surface consistency + AOT smoke

- [ ] **Step 1:** Run `dotnet run --project src/trash -- --help` and `--describe`; confirm `--json` appears
  once (not duplicated — the mksecret StandardFlags lesson), examples render (the ShellKit examples fix),
  defaults/limits/exit-codes match README/man/AI-guide. Fix any drift.
- [ ] **Step 2:** AOT publish smoke: `dotnet publish src/trash/trash.csproj -c Release -r win-x64` —
  Expected: succeeds, no AOT trim warnings. Run the published binary once.
- [ ] **Step 3: Commit** any fixes — `git commit -m "fix(trash): cross-surface consistency + AOT smoke"`

---

## Self-review notes (author)

- **Spec coverage:** scope (T5/T7/T9-17), three backends (T9-17), `--empty` safety (T7), `--json`/stderr
  routing (T6/T7), testing strategy incl. fixtures-not-round-trip (T3/T4) + fake-backend seam (T7) +
  platform integration (T11/14/17) + `--empty` deliberate gate (T7 unit + T18 manual), suite wiring (T19),
  known limitations docs (T19). Covered.
- **Native interop is the risk surface** — every native signature/format is flagged **⚠VERIFY** and gated
  behind a real-OS integration test (macOS via CI). The plan's native code is scaffolding to be confirmed
  at implementation, NOT trusted as-written.
- **Open verification items for adversarial review:** $I format version handling (v1 vs v2); SHFILEOPSTRUCT
  AOT marshalling of double-null `pFrom`; objc_msgSend signature correctness; FreeDesktop sticky-bit checks;
  WindowsIdentity AOT-safety.
```
