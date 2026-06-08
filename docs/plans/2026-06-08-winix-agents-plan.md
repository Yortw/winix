# `winix agents <verb>` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `winix agents <verb>` subcommand (`init`/`remove`/`status`) that writes a marker-delimited, version-pinned Winix discoverability block into a project's `AGENTS.md`/`CLAUDE.md`.

**Architecture:** A new pure-logic core `AgentsManager` (block render, marker parse, merge, drift) in `Winix.Winix`, with a tiny injected file-I/O seam (`IAgentsFileSystem`) so orchestration is unit-testable without disk. `Cli.RunAsync` gains an `agents` command that dispatches *before* manifest load (the block delegates to runtime `winix list`, so it needs no manifest). The `winix` `CommandLineParser` gains `--path` and `--claude`; its `--describe` snapshot is regenerated.

**Tech Stack:** .NET 10, C#, xUnit, `Yort.ShellKit.CommandLineParser`, AOT-compatible (no unconstrained reflection; `Utf8JsonWriter` for `--json`).

---

## Design references

- Spec: `docs/plans/2026-06-08-winix-agents-design.md`
- ADR: `docs/plans/2026-06-08-winix-agents-adr.md`

## File structure

- **Create** `src/Winix.Winix/AgentsManager.cs` — block template, `UrlRef`, `RenderBlock`, `FindBlockVersion`, `MergeBlock`, `RemoveBlock`, the `IAgentsFileSystem` seam + default impl, `AgentsOptions`, and `Run`/`RunInit`/`RunStatus`/`RunRemove`.
- **Modify** `src/Winix.Winix/Cli.cs` — add `agents` to the command whitelist, the `--path`/`--claude` parser options, `agents` examples, and the early `agents` dispatch.
- **Create** `tests/Winix.Winix.Tests/AgentsManagerTests.cs` — pure-logic + orchestration tests (in-memory + throwing fakes).
- **Modify** `tests/Winix.Winix.Tests/CliTests.cs` — `agents` wiring tests.
- **Modify** `tests/Winix.Contract.Tests/snapshots/winix.describe.json` — regenerated.
- **Modify** docs: `AGENTS.md`, `docs/plans/2026-06-06-agent-adoption-hardening-design.md`, `src/winix/README.md`, `src/winix/man/man1/winix.1` (check for `.1.md` source first), `docs/ai/winix.md`, `llms.txt`, `src/winix/CHANGELOG.md`.
- **Modify** `.github/workflows/post-publish.yml` — version-pinned-URL HTTP-200 guard.

**Test invariant for all Cli `agents` tests:** always pass `--path <tempdir>` — never rely on `Directory.GetCurrentDirectory()`. The `Winix.Winix.Tests` project has a documented parallel-CWD-flip hazard (peep flake, commit `d8a954c`); injecting an explicit base dir sidesteps it entirely.

---

### Task 1: URL ref builder (stable → `v{version}`, pre-release → `main`)

**Files:**
- Create: `src/Winix.Winix/AgentsManager.cs`
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Winix.Winix;
using Xunit;

namespace Winix.Winix.Tests;

public sealed class AgentsManagerTests
{
    // ── UrlRef ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0.4.0", "v0.4.0")]
    [InlineData("1.2.3", "v1.2.3")]
    public void UrlRef_StableVersion_PrependsV(string version, string expected)
    {
        Assert.Equal(expected, AgentsManager.UrlRef(version));
    }

    [Theory]
    [InlineData("0.4.0-dev")]
    [InlineData("0.5.0-alpha.1")]
    [InlineData("0.4.0-rc2")]
    public void UrlRef_PreReleaseVersion_FallsBackToMain(string version)
    {
        Assert.Equal("main", AgentsManager.UrlRef(version));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.UrlRef"`
Expected: FAIL — `AgentsManager` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Create `src/Winix.Winix/AgentsManager.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Winix.Winix;

/// <summary>
/// Reads, writes, and reports on the marker-delimited Winix discoverability block that
/// <c>winix agents</c> manages inside a project's <c>AGENTS.md</c> / <c>CLAUDE.md</c>.
/// </summary>
/// <remarks>
/// The block delegates "what's installed" to the runtime <c>winix list</c> pointer and to a
/// version-pinned repo URL, so the rendered content depends only on the running binary's
/// version — never on the tool manifest. That keeps this type free of manifest coupling and
/// fully unit-testable through the <see cref="IAgentsFileSystem"/> seam.
/// </remarks>
public static class AgentsManager
{
    /// <summary>
    /// Maps a binary version to the Git ref used in the version-pinned <c>AGENTS.md</c> URL.
    /// Stable releases (no <c>-</c> in the version string) pin to the matching <c>vX.Y.Z</c>
    /// tag; pre-release / dev builds (which have no tag) fall back to <c>main</c>. This is the
    /// same stable/pre-release discriminator the winget manifest generation keys on.
    /// </summary>
    internal static string UrlRef(string version)
    {
        return version.Contains('-') ? "main" : $"v{version}";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.UrlRef"`
Expected: PASS (5 cases).

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(winix): AgentsManager.UrlRef version->git-ref mapping"
```

---

### Task 2: Block template + `RenderBlock`

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs`
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
    // ── RenderBlock ───────────────────────────────────────────────────────────────

    [Fact]
    public void RenderBlock_StableVersion_EmitsMarkersVersionAndPinnedUrl()
    {
        string block = AgentsManager.RenderBlock("0.4.0");

        Assert.StartsWith("<!-- winix:start v=0.4.0 ", block, StringComparison.Ordinal);
        Assert.EndsWith("<!-- winix:end -->", block, StringComparison.Ordinal);
        Assert.Contains("https://github.com/Yortw/winix/blob/v0.4.0/AGENTS.md", block, StringComparison.Ordinal);
        Assert.Contains("`winix list`", block, StringComparison.Ordinal);
        // The honest-framing restraint must be present — this is the behaviour-changing core.
        Assert.Contains("not by", block, StringComparison.Ordinal);
        Assert.Contains("use the default", block, StringComparison.Ordinal);
        // F4: the exit-code convention line must be true for EVERY tool — no specific runtime
        // code (winix itself returns 127, not the suite-generic 126). Pin the exact wording.
        Assert.Contains("non-zero on failure (per-tool codes in `--describe`)", block, StringComparison.Ordinal);
        Assert.DoesNotContain("126", block, StringComparison.Ordinal);
        // Block body uses LF only (EOL normalisation happens at merge time).
        Assert.DoesNotContain("\r", block, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBlock_PreReleaseVersion_UrlUsesMainButMarkerKeepsExactVersion()
    {
        string block = AgentsManager.RenderBlock("0.4.0-dev");

        Assert.Contains("<!-- winix:start v=0.4.0-dev ", block, StringComparison.Ordinal);
        Assert.Contains("https://github.com/Yortw/winix/blob/main/AGENTS.md", block, StringComparison.Ordinal);
        Assert.DoesNotContain("/blob/v0.4.0-dev/", block, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.RenderBlock"`
Expected: FAIL — `RenderBlock` not defined.

- [ ] **Step 3: Write minimal implementation**

Add to `AgentsManager`:

```csharp
    /// <summary>The fixed prefix of the block's opening marker (an HTML comment, invisible in rendered Markdown).</summary>
    internal const string StartMarkerPrefix = "<!-- winix:start";

    /// <summary>The block's closing marker.</summary>
    internal const string EndMarker = "<!-- winix:end -->";

    /// <summary>
    /// Renders the full managed block (opening marker through closing marker) for the given
    /// version, joined with LF. EOL normalisation to the target file's convention happens in
    /// <see cref="MergeBlock"/>; this method always emits LF so its output is deterministic.
    /// </summary>
    internal static string RenderBlock(string version)
    {
        string urlRef = UrlRef(version);
        string[] lines =
        {
            $"<!-- winix:start v={version} — managed by `winix agents init`; edits between markers are overwritten -->",
            "## Winix CLI tools (installed on this machine)",
            "",
            "Prefer a Winix tool only when it's genuinely the better choice for the task — not by",
            "default. If you can't say why it beats the platform default (`find`, `time`, `tree`,",
            "`date`, PowerShell, …), use the default.",
            "",
            "- **What's installed, flags, JSON shapes:** `winix list` and `<tool> --describe`",
            "  (structured JSON — authoritative for this machine).",
            "- **Full guidance (when to prefer each tool, what it replaces):**",
            $"  https://github.com/Yortw/winix/blob/{urlRef}/AGENTS.md",
            "- Conventions: every tool has `--describe` + `--json`; exit 0 = success, 125 = usage",
            "  error, non-zero on failure (per-tool codes in `--describe`); summaries go to stderr so",
            "  stdout stays pipe-clean; `NO_COLOR` respected.",
            "<!-- winix:end -->",
        };
        return string.Join("\n", lines);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.RenderBlock"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(winix): AgentsManager block template + RenderBlock"
```

---

### Task 3: `FindBlockVersion` (marker parse — present / absent / malformed)

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs`
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
    // ── FindBlockVersion ──────────────────────────────────────────────────────────

    [Fact]
    public void FindBlockVersion_PresentBlock_ReturnsVersion()
    {
        string file = "# Project\n\n" + AgentsManager.RenderBlock("0.4.0") + "\n";
        Assert.Equal("0.4.0", AgentsManager.FindBlockVersion(file));
    }

    [Fact]
    public void FindBlockVersion_PreReleaseMarker_ReturnsExactVersion()
    {
        string file = AgentsManager.RenderBlock("0.4.0-dev");
        Assert.Equal("0.4.0-dev", AgentsManager.FindBlockVersion(file));
    }

    [Fact]
    public void FindBlockVersion_NoBlock_ReturnsNull()
    {
        Assert.Null(AgentsManager.FindBlockVersion("# Just a project\n\nNothing here.\n"));
    }

    [Fact]
    public void FindBlockVersion_StartWithoutEnd_ReturnsNull()
    {
        // A hand-edit that deleted the end marker must read as "no valid block",
        // so init appends a fresh one rather than corrupting the file.
        string file = "<!-- winix:start v=0.4.0 — managed... -->\n## Winix\nsome text, no end marker\n";
        Assert.Null(AgentsManager.FindBlockVersion(file));
    }

    [Theory]
    // F5: a mangled marker must never yield a garbage non-null version that status would
    // render as `stale (v-->)` and act on. Terminate the token at whitespace or `--`.
    [InlineData("<!-- winix:start v=--> -->\nbody\n<!-- winix:end -->", null)]
    [InlineData("<!-- winix:start v=0.4.0--> -->\nbody\n<!-- winix:end -->", "0.4.0")]
    [InlineData("<!-- winix:start v= -->\nbody\n<!-- winix:end -->", null)]
    public void FindBlockVersion_MalformedVersionToken_NoGarbage(string file, string? expected)
    {
        Assert.Equal(expected, AgentsManager.FindBlockVersion(file));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.FindBlockVersion"`
Expected: FAIL — `FindBlockVersion` not defined.

- [ ] **Step 3: Write minimal implementation**

Add to `AgentsManager`:

```csharp
    /// <summary>
    /// Extracts the version recorded in a block's opening marker (<c>v=...</c>), or
    /// <see langword="null"/> when the content has no syntactically-complete block. A start
    /// marker with no matching end marker is treated as "no valid block" so callers append a
    /// fresh block rather than splicing into a half-deleted one.
    /// </summary>
    internal static string? FindBlockVersion(string content)
    {
        int start = content.IndexOf(StartMarkerPrefix, StringComparison.Ordinal);
        if (start < 0) { return null; }

        int end = content.IndexOf(EndMarker, start, StringComparison.Ordinal);
        if (end < 0) { return null; }

        int vIdx = content.IndexOf("v=", start, StringComparison.Ordinal);
        if (vIdx < 0 || vIdx > end) { return null; }

        vIdx += 2;
        int e = vIdx;
        // F5: terminate at whitespace OR at the start of "-->" so a mangled marker like
        // `v=-->` yields null (not the garbage token "-->"). A single "-" is kept — real
        // pre-release versions contain one (e.g. "0.4.0-dev").
        while (e < content.Length
            && !char.IsWhiteSpace(content[e])
            && !(content[e] == '-' && e + 1 < content.Length && content[e + 1] == '-'))
        {
            e++;
        }
        return e > vIdx ? content.Substring(vIdx, e - vIdx) : null;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.FindBlockVersion"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(winix): AgentsManager.FindBlockVersion marker parse"
```

---

### Task 4: `MergeBlock` (replace-or-append, EOL-preserving, idempotent)

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs`
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
    // ── MergeBlock ────────────────────────────────────────────────────────────────

    [Fact]
    public void MergeBlock_EmptyFile_WritesBlockPlusTrailingNewline()
    {
        string merged = AgentsManager.MergeBlock(string.Empty, "0.4.0");
        Assert.Equal(AgentsManager.RenderBlock("0.4.0") + "\n", merged);
    }

    [Fact]
    public void MergeBlock_ExistingContent_AppendsWithBlankLineSeparator()
    {
        string merged = AgentsManager.MergeBlock("# Project\n", "0.4.0");
        Assert.StartsWith("# Project\n\n<!-- winix:start", merged, StringComparison.Ordinal);
        Assert.EndsWith("<!-- winix:end -->\n", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void MergeBlock_ExistingBlock_ReplacesInPlacePreservingSurroundingText()
    {
        string before = "# Project\n\n" + AgentsManager.RenderBlock("0.3.0") + "\n\n## Other section\n";
        string merged = AgentsManager.MergeBlock(before, "0.4.0");

        Assert.Contains("blob/v0.4.0/AGENTS.md", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("blob/v0.3.0/AGENTS.md", merged, StringComparison.Ordinal);
        Assert.StartsWith("# Project\n", merged, StringComparison.Ordinal);
        Assert.Contains("## Other section", merged, StringComparison.Ordinal);
        // Exactly one block.
        int count = CountOccurrences(merged, AgentsManager.StartMarkerPrefix);
        Assert.Equal(1, count);
    }

    [Fact]
    public void MergeBlock_ReRunSameVersion_IsByteStable()
    {
        // Negative invariant: re-running init at the same version must not change the file.
        string once = AgentsManager.MergeBlock("# Project\n", "0.4.0");
        string twice = AgentsManager.MergeBlock(once, "0.4.0");
        Assert.Equal(once, twice);
    }

    [Fact]
    public void MergeBlock_CrlfFile_PreservesCrlfEol()
    {
        string crlf = "# Project\r\n";
        string merged = AgentsManager.MergeBlock(crlf, "0.4.0");
        Assert.Contains("\r\n", merged, StringComparison.Ordinal);
        // No bare LF block lines smuggled into a CRLF file.
        Assert.DoesNotContain("respected.\n<!-- winix:end", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void MergeBlock_LiteralMarkerPairInProse_FirstPairWins()
    {
        // F6 (documented limitation): the FIRST start..end pair is the managed block. A user
        // who has the literal marker pair in their own content has that span refreshed.
        // Pinned so the behaviour is intentional, not accidental.
        string prose = "Example:\n<!-- winix:start v=9.9.9 -->\nfake\n<!-- winix:end -->\n";
        string merged = AgentsManager.MergeBlock(prose, "0.4.0");
        Assert.Equal(1, CountOccurrences(merged, AgentsManager.StartMarkerPrefix));
        Assert.Contains("blob/v0.4.0/AGENTS.md", merged, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.MergeBlock"`
Expected: FAIL — `MergeBlock` not defined.

- [ ] **Step 3: Write minimal implementation**

Add to `AgentsManager`:

```csharp
    /// <summary>
    /// Returns <paramref name="content"/> with the managed block inserted or refreshed: an
    /// existing complete block is replaced in place (surrounding text untouched); otherwise a
    /// fresh block is appended after exactly one blank line. The result's line endings match
    /// the file's existing convention (CRLF if the file already uses it, LF otherwise), so the
    /// operation is byte-stable on re-run at the same version.
    /// </summary>
    internal static string MergeBlock(string content, string version)
    {
        string eol = DetectEol(content);
        string block = NormalizeEol(RenderBlock(version), eol);

        int start = content.IndexOf(StartMarkerPrefix, StringComparison.Ordinal);
        if (start >= 0)
        {
            int end = content.IndexOf(EndMarker, start, StringComparison.Ordinal);
            if (end >= 0)
            {
                int endFull = end + EndMarker.Length;
                string before = content.Substring(0, start);
                string after = content.Substring(endFull);
                return before + block + after;
            }
            // Start marker with no end: fall through and append a fresh, well-formed block.
        }

        if (content.Length == 0)
        {
            return block + eol;
        }

        string trimmed = content.TrimEnd('\r', '\n');
        return trimmed + eol + eol + block + eol;
    }

    /// <summary>Returns <c>"\r\n"</c> if the content already uses Windows line endings, else <c>"\n"</c>.</summary>
    private static string DetectEol(string content)
    {
        return content.Contains("\r\n") ? "\r\n" : "\n";
    }

    /// <summary>Rewrites every LF in <paramref name="text"/> (which uses LF only) to <paramref name="eol"/>.</summary>
    private static string NormalizeEol(string text, string eol)
    {
        return eol == "\n" ? text : text.Replace("\n", eol);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.MergeBlock"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(winix): AgentsManager.MergeBlock replace-or-append, EOL-preserving"
```

---

### Task 5: `RemoveBlock` (strip block, preserve other content, never corrupt)

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs`
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
    // ── RemoveBlock ───────────────────────────────────────────────────────────────

    [Fact]
    public void RemoveBlock_OnlyBlock_ReturnsEmpty()
    {
        string file = AgentsManager.MergeBlock(string.Empty, "0.4.0");
        Assert.Equal(string.Empty, AgentsManager.RemoveBlock(file));
    }

    [Fact]
    public void RemoveBlock_BlockAmongContent_LeavesOtherContent()
    {
        string file = AgentsManager.MergeBlock("# Project\n", "0.4.0");
        string stripped = AgentsManager.RemoveBlock(file);
        Assert.Contains("# Project", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain(AgentsManager.StartMarkerPrefix, stripped, StringComparison.Ordinal);
        Assert.DoesNotContain(AgentsManager.EndMarker, stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveBlock_NoBlock_ReturnsUnchanged()
    {
        string file = "# Project\n\nNo winix here.\n";
        Assert.Equal(file, AgentsManager.RemoveBlock(file));
    }

    [Fact]
    public void RemoveBlock_StartWithoutEnd_ReturnsUnchanged()
    {
        // Malformed: do not touch — removing a half-block could eat user text.
        string file = "<!-- winix:start v=0.4.0 -->\nhalf a block, no end\n";
        Assert.Equal(file, AgentsManager.RemoveBlock(file));
    }

    [Fact]
    public void RemoveBlock_TwoBlocks_RemovesAll()
    {
        // F6: remove strips EVERY managed block so `status` can't keep reporting a leftover.
        string two = AgentsManager.RenderBlock("0.4.0") + "\n\n# mid\n\n" + AgentsManager.RenderBlock("0.4.0") + "\n";
        string stripped = AgentsManager.RemoveBlock(two);
        Assert.DoesNotContain(AgentsManager.StartMarkerPrefix, stripped, StringComparison.Ordinal);
        Assert.Contains("# mid", stripped, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.RemoveBlock"`
Expected: FAIL — `RemoveBlock` not defined.

- [ ] **Step 3: Write minimal implementation**

Add to `AgentsManager`:

```csharp
    /// <summary>
    /// Returns <paramref name="content"/> with the managed block removed. The blank-line
    /// separator introduced on append is collapsed so the surrounding text rejoins cleanly.
    /// Content with no complete block (absent, or a start marker with no end) is returned
    /// unchanged — removing a half-block could delete user text.
    /// </summary>
    internal static string RemoveBlock(string content)
    {
        string eol = DetectEol(content);
        string current = content;

        // F6: strip EVERY complete block (a duplicate is an anomaly from a bad merge; leaving
        // one behind would let `status` keep reporting a block after `remove`). A start marker
        // with no matching end is left untouched — removing a half-block could eat user text.
        while (true)
        {
            int start = current.IndexOf(StartMarkerPrefix, StringComparison.Ordinal);
            if (start < 0) { return current; }

            int end = current.IndexOf(EndMarker, start, StringComparison.Ordinal);
            if (end < 0) { return current; }

            int endFull = end + EndMarker.Length;
            string before = current.Substring(0, start).TrimEnd('\r', '\n');
            string after = current.Substring(endFull).TrimStart('\r', '\n');

            if (before.Length == 0)
            {
                current = after;
            }
            else if (after.Length == 0)
            {
                current = before + eol;
            }
            else
            {
                current = before + eol + eol + after;
            }
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.RemoveBlock"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(winix): AgentsManager.RemoveBlock strip-block, preserve surrounding text"
```

---

### Task 6: `IAgentsFileSystem` seam + default impl + test fakes

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs`
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
    // ── IAgentsFileSystem default impl + fakes ────────────────────────────────────

    [Fact]
    public void DefaultAgentsFileSystem_RoundTripsTempFile()
    {
        var fs = AgentsManager.CreateDefaultFileSystem();
        string dir = Path.Combine(Path.GetTempPath(), "winix-agents-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "AGENTS.md");
            Assert.False(fs.FileExists(path));
            Assert.True(fs.DirectoryExists(dir));
            fs.WriteAllText(path, "hello");
            Assert.True(fs.FileExists(path));
            Assert.Equal("hello", fs.ReadAllText(path));
            // F1: the atomic temp+move must not leave a sidecar behind on success.
            Assert.False(File.Exists(Path.Combine(dir, ".AGENTS.md.winix-tmp")));
            // Overwrite is a complete replace.
            fs.WriteAllText(path, "world");
            Assert.Equal("world", fs.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // In-memory fake shared by later orchestration tests.
    private sealed class InMemoryFs : IAgentsFileSystem
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Dirs { get; } = new(StringComparer.Ordinal);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public bool DirectoryExists(string path) => Dirs.Contains(path);
        public string ReadAllText(string path) => Files[path];
        public void WriteAllText(string path, string content) => Files[path] = content;
    }

    private sealed class ThrowingFs : IAgentsFileSystem
    {
        public bool FileExists(string path) => false;
        public bool DirectoryExists(string path) => true;
        public string ReadAllText(string path) => string.Empty;
        public void WriteAllText(string path, string content) => throw new IOException("disk full");
    }

    // Succeeds on every target except CLAUDE.md — models a multi-file partial write failure.
    private sealed class PartialFailFs : IAgentsFileSystem
    {
        public bool FileExists(string path) => false;
        public bool DirectoryExists(string path) => true;
        public string ReadAllText(string path) => string.Empty;
        public void WriteAllText(string path, string content)
        {
            if (path.EndsWith("CLAUDE.md", StringComparison.Ordinal))
            {
                throw new IOException("nope");
            }
        }
    }

    // File exists but the read throws — models a permission-denied read for the F8 status path.
    private sealed class ReadFailFs : IAgentsFileSystem
    {
        public bool FileExists(string path) => true;
        public bool DirectoryExists(string path) => true;
        public string ReadAllText(string path) => throw new UnauthorizedAccessException("denied");
        public void WriteAllText(string path, string content) => throw new UnauthorizedAccessException("denied");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.DefaultAgentsFileSystem"`
Expected: FAIL — `IAgentsFileSystem` / `CreateDefaultFileSystem` not defined.

- [ ] **Step 3: Write minimal implementation**

Add to the `Winix.Winix` namespace (in `AgentsManager.cs`):

```csharp
/// <summary>
/// File-I/O seam for <see cref="AgentsManager"/>. Production uses a thin wrapper over
/// <see cref="System.IO.File"/>; tests inject in-memory or fault-injecting fakes so the
/// orchestration paths (target resolution, dry-run, drift, write-failure) are exercisable
/// without touching disk.
/// </summary>
public interface IAgentsFileSystem
{
    /// <summary>Returns whether a file exists at <paramref name="path"/>.</summary>
    bool FileExists(string path);

    /// <summary>Returns whether a directory exists at <paramref name="path"/>.</summary>
    bool DirectoryExists(string path);

    /// <summary>Reads the entire file at <paramref name="path"/> as text.</summary>
    string ReadAllText(string path);

    /// <summary>Writes <paramref name="content"/> to <paramref name="path"/>, overwriting.</summary>
    void WriteAllText(string path, string content);
}
```

And add a factory to `AgentsManager`:

```csharp
    /// <summary>Returns the production file system (a thin wrapper over <see cref="System.IO.File"/>).</summary>
    public static IAgentsFileSystem CreateDefaultFileSystem()
    {
        return new DefaultAgentsFileSystem();
    }

    private sealed class DefaultAgentsFileSystem : IAgentsFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public string ReadAllText(string path) => File.ReadAllText(path);

        public void WriteAllText(string path, string content)
        {
            // F1: atomic per-file replace. Write a sibling temp on the same volume, then move
            // it over the target. A crash / Ctrl+C mid-write leaves the user's existing file
            // intact — a plain File.WriteAllText truncates in place and would lose their
            // content (init is explicitly designed to run against files that already exist).
            string? dir = Path.GetDirectoryName(path);
            string tempDir = string.IsNullOrEmpty(dir) ? "." : dir;
            string temp = Path.Combine(tempDir, "." + Path.GetFileName(path) + ".winix-tmp");
            File.WriteAllText(temp, content);
            try
            {
                File.Move(temp, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(temp); } catch { /* best-effort cleanup of the sidecar */ }
                throw;
            }
        }
    }
```

> **Verification level (F1):** the no-residue test above exercises the temp+move *mechanism*.
> True crash-atomicity (process killed *between* the temp write and the move) cannot be
> unit-tested without killing the process; it is provided by `File.Move(overwrite: true)`'s
> replace semantics (atomic rename on the same volume) and is asserted by argument, not by a
> test that exercises the kill. This is stated explicitly rather than implied as "tested."

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.DefaultAgentsFileSystem"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(winix): IAgentsFileSystem seam + default impl + test fakes"
```

---

### Task 7: `AgentsOptions` + `ResolveInitTargets` (option-B target selection)

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs`
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
    // ── ResolveInitTargets (option B) ─────────────────────────────────────────────

    private static AgentsManager.AgentsOptions Opts(string verb, string baseDir, bool forceClaude = false,
        bool dryRun = false, bool json = false, string version = "0.4.0")
    {
        return new AgentsManager.AgentsOptions(verb, baseDir, forceClaude, dryRun, json, version);
    }

    [Fact]
    public void ResolveInitTargets_NoClaudeFile_AgentsMdOnly()
    {
        var fs = new InMemoryFs { Dirs = { } };
        fs.Dirs.Add("/proj");
        var targets = AgentsManager.ResolveInitTargets(Opts("init", "/proj"), fs);
        Assert.Single(targets);
        Assert.EndsWith("AGENTS.md", targets[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveInitTargets_ExistingClaudeFile_BothTargets()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        fs.Files[Path.Combine("/proj", "CLAUDE.md")] = "# existing\n";
        var targets = AgentsManager.ResolveInitTargets(Opts("init", "/proj"), fs);
        Assert.Equal(2, targets.Count);
        Assert.Contains(targets, t => t.EndsWith("CLAUDE.md", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveInitTargets_ForceClaude_AddsClaudeEvenWhenAbsent()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        var targets = AgentsManager.ResolveInitTargets(Opts("init", "/proj", forceClaude: true), fs);
        Assert.Equal(2, targets.Count);
        Assert.Contains(targets, t => t.EndsWith("CLAUDE.md", StringComparison.Ordinal));
    }
```

> Note: the `InMemoryFs` initializer `{ Dirs = { } }` in the first test is illustrative; use `fs.Dirs.Add("/proj")` as shown. Keep the fake's `Dirs`/`Files` as get-only auto-properties with collection initialisers (already defined in Task 6).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.ResolveInitTargets"`
Expected: FAIL — `AgentsOptions` / `ResolveInitTargets` not defined.

- [ ] **Step 3: Write minimal implementation**

Add to `AgentsManager`:

```csharp
    /// <summary>
    /// Parsed inputs for an <c>agents</c> run. <paramref name="Verb"/> is the subcommand
    /// (<c>init</c>/<c>remove</c>/<c>status</c>), or <see langword="null"/> when none was given.
    /// </summary>
    public sealed record AgentsOptions(
        string? Verb,
        string BaseDir,
        bool ForceClaude,
        bool DryRun,
        bool Json,
        string Version);

    /// <summary>
    /// Resolves the files <c>init</c> would write (and that <c>status</c> evaluates): always
    /// <c>AGENTS.md</c>, plus <c>CLAUDE.md</c> when it already exists or <c>--claude</c> forces it.
    /// </summary>
    internal static List<string> ResolveInitTargets(AgentsOptions opts, IAgentsFileSystem fs)
    {
        var targets = new List<string> { Path.Combine(opts.BaseDir, "AGENTS.md") };
        string claude = Path.Combine(opts.BaseDir, "CLAUDE.md");
        if (opts.ForceClaude || fs.FileExists(claude))
        {
            targets.Add(claude);
        }
        return targets;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.ResolveInitTargets"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(winix): AgentsOptions + ResolveInitTargets (option-B targets)"
```

---

### Task 8: `RunInit` (write / dry-run / multi-file / bad-path / seam-failure)

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs`
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
    // ── RunInit ───────────────────────────────────────────────────────────────────

    [Fact]
    public void RunInit_NoExistingFiles_WritesAgentsMdAndReturnsSuccess()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunInit(Opts("init", "/proj"), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.Success, exit);
        string agents = fs.Files[Path.Combine("/proj", "AGENTS.md")];
        Assert.Contains("blob/v0.4.0/AGENTS.md", agents, StringComparison.Ordinal);
    }

    [Fact]
    public void RunInit_DryRun_WritesNothing()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunInit(Opts("init", "/proj", dryRun: true), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.Success, exit);
        Assert.Empty(fs.Files);
        Assert.Contains("would write", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunInit_BadPath_ReturnsUsageError()
    {
        // F2: covers both "path does not exist" and "path is a regular file" — both surface as
        // DirectoryExists == false. (An InMemoryFs with no dir registered models either.)
        var fs = new InMemoryFs(); // no dirs registered → DirectoryExists false
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunInit(Opts("init", "/nope"), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.UsageError, exit);
        Assert.Contains("not a directory", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunInit_WriteFails_ReturnsInternalErrorWithCleanMessage()
    {
        // F2: this is also the "target path is itself a directory" outcome — the write throws
        // and maps to InternalError with a clean message (no stack trace).
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunInit(Opts("init", "/proj"), new ThrowingFs(), stdout, stderr);

        Assert.Equal(WinixExitCode.InternalError, exit);
        // No raw framework message / stack trace leaked.
        Assert.DoesNotContain("Exception:", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunInit_SecondTargetWriteFails_NamesFailedFile()
    {
        // F3: AGENTS.md writes, CLAUDE.md throws → partial commit; the message must name the
        // file that failed so the user can tell which of the two is updated.
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunInit(
            Opts("init", "/proj", forceClaude: true), new PartialFailFs(), stdout, stderr);

        Assert.Equal(WinixExitCode.InternalError, exit);
        Assert.Contains("CLAUDE.md", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunInit_Json_GoesToStdout()
    {
        // F10: --json must produce a real envelope, not be silently ignored.
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunInit(Opts("init", "/proj", json: true), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.Success, exit);
        string json = stdout.ToString();
        Assert.Contains("\"action\":\"init\"", json, StringComparison.Ordinal);
        Assert.Contains("\"files\"", json, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.RunInit"`
Expected: FAIL — `RunInit` not defined.

- [ ] **Step 3: Write minimal implementation**

Add to `AgentsManager`:

```csharp
    /// <summary>
    /// Writes or refreshes the managed block in every applicable target. Returns
    /// <see cref="WinixExitCode.Success"/> on success, <see cref="WinixExitCode.UsageError"/>
    /// when the base directory does not exist, or <see cref="WinixExitCode.InternalError"/> on
    /// an I/O failure (reported as a clean one-line message — never a framework stack trace).
    /// </summary>
    internal static int RunInit(AgentsOptions opts, IAgentsFileSystem fs, TextWriter stdout, TextWriter stderr)
    {
        if (!fs.DirectoryExists(opts.BaseDir))
        {
            stderr.WriteLine($"winix: path '{opts.BaseDir}' is not a directory");
            return WinixExitCode.UsageError;
        }

        var changed = new List<string>();
        string current = string.Empty;
        try
        {
            foreach (string target in ResolveInitTargets(opts, fs))
            {
                current = target;
                string existing = fs.FileExists(target) ? fs.ReadAllText(target) : string.Empty;
                string merged = MergeBlock(existing, opts.Version);
                if (!opts.DryRun)
                {
                    fs.WriteAllText(target, merged);
                }
                changed.Add(target);
                if (!opts.Json)
                {
                    stderr.WriteLine(opts.DryRun ? $"winix: would write {target}" : $"winix: wrote {target}");
                }
            }

            if (opts.Json)
            {
                stdout.WriteLine(FormatActionJson("init", opts.DryRun, changed));
            }
            return WinixExitCode.Success;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // F3: name the file that failed so a partial multi-file write is diagnosable.
            // Don't pipe ex.Message under InvariantGlobalization — framework messages return
            // SR resource keys, not English. The type discriminator is the safe minimum.
            stderr.WriteLine($"winix: failed to write agents pointer to {current} ({ex.GetType().Name})");
            return WinixExitCode.InternalError;
        }
    }

    /// <summary>Builds the <c>--json</c> envelope for <c>init</c>/<c>remove</c> (AOT-safe).</summary>
    private static string FormatActionJson(string action, bool dryRun, List<string> files)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("action", action);
            writer.WriteBoolean("dryRun", dryRun);
            writer.WriteStartArray("files");
            foreach (string f in files)
            {
                writer.WriteStringValue(f);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.RunInit"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(winix): AgentsManager.RunInit write/dry-run/bad-path/seam-failure"
```

---

### Task 9: `RunStatus` (drift, multi-file worst-case, exit codes, `--json`)

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs`
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
    // ── RunStatus ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RunStatus_CurrentBlock_ReturnsSuccess()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        fs.Files[Path.Combine("/proj", "AGENTS.md")] = AgentsManager.MergeBlock(string.Empty, "0.4.0");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunStatus(Opts("status", "/proj"), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.Success, exit);
        Assert.Contains("current", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunStatus_Absent_ReturnsToolFailure()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunStatus(Opts("status", "/proj"), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.ToolFailure, exit);
        Assert.Contains("absent", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunStatus_StaleVersion_ReturnsToolFailure()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        fs.Files[Path.Combine("/proj", "AGENTS.md")] = AgentsManager.MergeBlock(string.Empty, "0.3.0");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunStatus(Opts("status", "/proj"), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.ToolFailure, exit);
        Assert.Contains("stale", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunStatus_AgentsCurrentButExistingClaudeMissingBlock_IsDrift()
    {
        // Multi-file worst-case: AGENTS.md current, but an existing CLAUDE.md has no block.
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        fs.Files[Path.Combine("/proj", "AGENTS.md")] = AgentsManager.MergeBlock(string.Empty, "0.4.0");
        fs.Files[Path.Combine("/proj", "CLAUDE.md")] = "# just my rules\n";
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunStatus(Opts("status", "/proj"), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.ToolFailure, exit);
    }

    [Fact]
    public void RunStatus_Json_GoesToStdout()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        fs.Files[Path.Combine("/proj", "AGENTS.md")] = AgentsManager.MergeBlock(string.Empty, "0.4.0");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunStatus(Opts("status", "/proj", json: true), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.Success, exit);
        string json = stdout.ToString();
        Assert.Contains("\"current\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"files\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RunStatus_BothStale_ReturnsToolFailure()
    {
        // F7: worst-case aggregation across two files, both stale.
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        fs.Files[Path.Combine("/proj", "AGENTS.md")] = AgentsManager.MergeBlock(string.Empty, "0.3.0");
        fs.Files[Path.Combine("/proj", "CLAUDE.md")] = AgentsManager.MergeBlock(string.Empty, "0.3.0");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunStatus(Opts("status", "/proj"), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.ToolFailure, exit);
    }

    [Fact]
    public void RunStatus_AgentsAbsentClaudeCurrent_ReturnsToolFailure()
    {
        // F7: AGENTS.md (always applicable) absent, existing CLAUDE.md current → worst case wins.
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        fs.Files[Path.Combine("/proj", "CLAUDE.md")] = AgentsManager.MergeBlock(string.Empty, "0.4.0");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunStatus(Opts("status", "/proj"), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.ToolFailure, exit);
    }

    [Fact]
    public void RunStatus_ForceClaudeAbsent_ReturnsToolFailure()
    {
        // F7: --claude makes CLAUDE.md applicable even when absent → drift.
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        fs.Files[Path.Combine("/proj", "AGENTS.md")] = AgentsManager.MergeBlock(string.Empty, "0.4.0");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunStatus(Opts("status", "/proj", forceClaude: true), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.ToolFailure, exit);
    }

    [Fact]
    public void RunStatus_ReadFails_ReturnsInternalError()
    {
        // F8: an unreadable existing target must not leak a stack trace.
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunStatus(Opts("status", "/proj"), new ReadFailFs(), stdout, stderr);

        Assert.Equal(WinixExitCode.InternalError, exit);
        Assert.DoesNotContain("   at ", stderr.ToString(), StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.RunStatus"`
Expected: FAIL — `RunStatus` not defined.

- [ ] **Step 3: Write minimal implementation**

Add to `AgentsManager`:

```csharp
    /// <summary>
    /// Reports the managed-block state of every applicable target (the same set
    /// <see cref="ResolveInitTargets"/> returns). Returns <see cref="WinixExitCode.Success"/>
    /// only when every applicable file carries a block at the current version; otherwise
    /// <see cref="WinixExitCode.ToolFailure"/> (the worst case across the set).
    /// </summary>
    internal static int RunStatus(AgentsOptions opts, IAgentsFileSystem fs, TextWriter stdout, TextWriter stderr)
    {
        if (!fs.DirectoryExists(opts.BaseDir))
        {
            stderr.WriteLine($"winix: path '{opts.BaseDir}' is not a directory");
            return WinixExitCode.UsageError;
        }

        var results = new List<(string Path, string State, string? Version)>();
        bool allCurrent = true;

        try
        {
            foreach (string target in ResolveInitTargets(opts, fs))
            {
                string? blockVer = fs.FileExists(target) ? FindBlockVersion(fs.ReadAllText(target)) : null;
                string state;
                if (blockVer == null)
                {
                    state = "absent";
                    allCurrent = false;
                }
                else if (string.Equals(blockVer, opts.Version, StringComparison.Ordinal))
                {
                    state = "current";
                }
                else
                {
                    state = "stale";
                    allCurrent = false;
                }
                results.Add((target, state, blockVer));
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // F8: an existing-but-unreadable target (permission denied) must not leak a stack
            // trace out of status — map to a clean InternalError like init/remove.
            stderr.WriteLine($"winix: failed to read agents pointer ({ex.GetType().Name})");
            return WinixExitCode.InternalError;
        }

        if (opts.Json)
        {
            stdout.WriteLine(FormatStatusJson(results, allCurrent));
        }
        else
        {
            foreach ((string path, string state, string? version) in results)
            {
                string suffix = version != null ? $" (v{version})" : string.Empty;
                stderr.WriteLine($"winix: {path}: {state}{suffix}");
            }
        }

        return allCurrent ? WinixExitCode.Success : WinixExitCode.ToolFailure;
    }

    /// <summary>Builds the <c>--json</c> status envelope with <see cref="Utf8JsonWriter"/> (AOT-safe).</summary>
    private static string FormatStatusJson(
        List<(string Path, string State, string? Version)> results, bool allCurrent)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("current", allCurrent);
            writer.WriteStartArray("files");
            foreach ((string path, string state, string? version) in results)
            {
                writer.WriteStartObject();
                writer.WriteString("path", path);
                writer.WriteString("state", state);
                if (version != null) { writer.WriteString("version", version); }
                else { writer.WriteNull("version"); }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.RunStatus"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(winix): AgentsManager.RunStatus drift-sensitive exit + --json to stdout"
```

---

### Task 10: `RunRemove` (strip across files, dry-run, seam-failure)

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs`
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
    // ── RunRemove ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RunRemove_StripsBlockFromExistingFiles()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        string agentsPath = Path.Combine("/proj", "AGENTS.md");
        fs.Files[agentsPath] = AgentsManager.MergeBlock("# Project\n", "0.4.0");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunRemove(Opts("remove", "/proj"), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.Success, exit);
        Assert.DoesNotContain(AgentsManager.StartMarkerPrefix, fs.Files[agentsPath], StringComparison.Ordinal);
        Assert.Contains("# Project", fs.Files[agentsPath], StringComparison.Ordinal);
    }

    [Fact]
    public void RunRemove_NoBlock_LeavesFileUntouchedAndSucceeds()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        string agentsPath = Path.Combine("/proj", "AGENTS.md");
        fs.Files[agentsPath] = "# Project\nno block\n";
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunRemove(Opts("remove", "/proj"), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.Success, exit);
        Assert.Equal("# Project\nno block\n", fs.Files[agentsPath]);
    }

    [Fact]
    public void RunRemove_DryRun_WritesNothing()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        string agentsPath = Path.Combine("/proj", "AGENTS.md");
        string original = AgentsManager.MergeBlock("# Project\n", "0.4.0");
        fs.Files[agentsPath] = original;
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunRemove(Opts("remove", "/proj", dryRun: true), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.Success, exit);
        Assert.Equal(original, fs.Files[agentsPath]);
        Assert.Contains("would update", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunRemove_Json_GoesToStdout()
    {
        // F10: --json must produce a real envelope for remove too.
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        string agentsPath = Path.Combine("/proj", "AGENTS.md");
        fs.Files[agentsPath] = AgentsManager.MergeBlock("# Project\n", "0.4.0");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunRemove(Opts("remove", "/proj", json: true), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.Success, exit);
        Assert.Contains("\"action\":\"remove\"", stdout.ToString(), StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.RunRemove"`
Expected: FAIL — `RunRemove` not defined.

- [ ] **Step 3: Write minimal implementation**

Add to `AgentsManager`:

```csharp
    /// <summary>
    /// Strips the managed block from <c>AGENTS.md</c> and <c>CLAUDE.md</c> wherever each
    /// exists and actually contains a block. Files are never deleted (an emptied file is left
    /// empty). Returns <see cref="WinixExitCode.Success"/>, or
    /// <see cref="WinixExitCode.InternalError"/> on an I/O failure.
    /// </summary>
    internal static int RunRemove(AgentsOptions opts, IAgentsFileSystem fs, TextWriter stdout, TextWriter stderr)
    {
        if (!fs.DirectoryExists(opts.BaseDir))
        {
            stderr.WriteLine($"winix: path '{opts.BaseDir}' is not a directory");
            return WinixExitCode.UsageError;
        }

        string[] candidates =
        {
            Path.Combine(opts.BaseDir, "AGENTS.md"),
            Path.Combine(opts.BaseDir, "CLAUDE.md"),
        };

        var changed = new List<string>();
        string current = string.Empty;
        try
        {
            foreach (string target in candidates)
            {
                current = target;
                if (!fs.FileExists(target)) { continue; }
                string existing = fs.ReadAllText(target);
                if (FindBlockVersion(existing) == null) { continue; }

                if (!opts.DryRun)
                {
                    fs.WriteAllText(target, RemoveBlock(existing));
                }
                changed.Add(target);
                if (!opts.Json)
                {
                    stderr.WriteLine(opts.DryRun ? $"winix: would update {target}" : $"winix: removed block from {target}");
                }
            }

            if (opts.Json)
            {
                stdout.WriteLine(FormatActionJson("remove", opts.DryRun, changed));
            }
            return WinixExitCode.Success;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // F3: name the file that failed for a diagnosable partial update.
            stderr.WriteLine($"winix: failed to update agents pointer to {current} ({ex.GetType().Name})");
            return WinixExitCode.InternalError;
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.RunRemove"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(winix): AgentsManager.RunRemove strip-across-files, never delete"
```

---

### Task 11: `AgentsManager.Run` verb dispatch (missing / unknown → 125)

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs`
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
    // ── Run dispatch ──────────────────────────────────────────────────────────────

    [Fact]
    public void Run_NoVerb_ReturnsUsageErrorListingVerbs()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.Run(Opts(null!, "/proj") with { Verb = null }, stdout, stderr, fs);

        Assert.Equal(WinixExitCode.UsageError, exit);
        Assert.Contains("init", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("remove", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("status", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_UnknownVerb_ReturnsUsageError()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.Run(Opts("frobnicate", "/proj"), stdout, stderr, fs);

        Assert.Equal(WinixExitCode.UsageError, exit);
        Assert.Contains("'frobnicate'", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_InitVerb_DispatchesToRunInit()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.Run(Opts("init", "/proj"), stdout, stderr, fs);

        Assert.Equal(WinixExitCode.Success, exit);
        Assert.True(fs.FileExists(Path.Combine("/proj", "AGENTS.md")));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.Run_"`
Expected: FAIL — `Run` not defined.

- [ ] **Step 3: Write minimal implementation**

Add to `AgentsManager`:

```csharp
    /// <summary>
    /// Entry point for the <c>winix agents</c> command. Dispatches on
    /// <see cref="AgentsOptions.Verb"/> to init / remove / status, returning
    /// <see cref="WinixExitCode.UsageError"/> for a missing or unknown verb.
    /// </summary>
    /// <param name="fs">File-I/O seam; production passes <see langword="null"/> to use the default.</param>
    public static int Run(AgentsOptions opts, TextWriter stdout, TextWriter stderr, IAgentsFileSystem? fs = null)
    {
        IAgentsFileSystem resolved = fs ?? CreateDefaultFileSystem();

        switch (opts.Verb)
        {
            case "init":
                return RunInit(opts, resolved, stdout, stderr);
            case "remove":
                return RunRemove(opts, resolved, stdout, stderr);
            case "status":
                return RunStatus(opts, resolved, stdout, stderr);
            case null:
            case "":
                stderr.WriteLine("winix: missing agents verb (expected init, remove, or status)");
                return WinixExitCode.UsageError;
            default:
                stderr.WriteLine($"winix: unknown agents verb '{opts.Verb}' (expected init, remove, or status)");
                return WinixExitCode.UsageError;
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManagerTests.Run_"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(winix): AgentsManager.Run verb dispatch"
```

---

### Task 12: Wire `agents` into `Cli.RunAsync` (whitelist + flags + examples + dispatch)

**Files:**
- Modify: `src/Winix.Winix/Cli.cs`
- Test: `tests/Winix.Winix.Tests/CliTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `CliTests` (uses a real temp dir via `--path`; never relies on CWD per the parallel-CWD hazard):

```csharp
    // ── agents subcommand wiring ──────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_AgentsInit_WritesAgentsMdToPath()
    {
        string dir = Path.Combine(Path.GetTempPath(), "winix-agents-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var (stdout, stderr) = Sinks();
            int exit = await Cli.RunAsync(
                new[] { "agents", "init", "--path", dir },
                stdout, stderr,
                adapters: new Dictionary<string, IPackageManagerAdapter>(),
                platform: PlatformId.Linux,
                // A throwing loader proves agents never touches the manifest.
                manifestLoader: ThrowingLoader("manifest must not be loaded for agents"));

            Assert.Equal(WinixExitCode.Success, exit);
            Assert.True(File.Exists(Path.Combine(dir, "AGENTS.md")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_AgentsStatusAbsent_ReturnsToolFailure()
    {
        string dir = Path.Combine(Path.GetTempPath(), "winix-agents-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var (stdout, stderr) = Sinks();
            int exit = await Cli.RunAsync(
                new[] { "agents", "status", "--path", dir },
                stdout, stderr,
                adapters: new Dictionary<string, IPackageManagerAdapter>(),
                platform: PlatformId.Linux,
                manifestLoader: ThrowingLoader("unused"));

            Assert.Equal(WinixExitCode.ToolFailure, exit);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_AgentsNoVerb_ReturnsUsageError()
    {
        var (stdout, stderr) = Sinks();
        int exit = await Cli.RunAsync(
            new[] { "agents" },
            stdout, stderr,
            adapters: new Dictionary<string, IPackageManagerAdapter>(),
            platform: PlatformId.Linux,
            manifestLoader: ThrowingLoader("unused"));

        Assert.Equal(WinixExitCode.UsageError, exit);
        Assert.Contains("missing agents verb", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AgentsStatusCurrent_ReturnsSuccess()
    {
        // F7: pin the 0-path end-to-end through the real dispatch — init then status at the
        // same binary version must report current (exit 0).
        string dir = Path.Combine(Path.GetTempPath(), "winix-agents-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var (stdout1, stderr1) = Sinks();
            await Cli.RunAsync(
                new[] { "agents", "init", "--path", dir },
                stdout1, stderr1,
                adapters: new Dictionary<string, IPackageManagerAdapter>(),
                platform: PlatformId.Linux,
                manifestLoader: ThrowingLoader("unused"));

            var (stdout2, stderr2) = Sinks();
            int exit = await Cli.RunAsync(
                new[] { "agents", "status", "--path", dir },
                stdout2, stderr2,
                adapters: new Dictionary<string, IPackageManagerAdapter>(),
                platform: PlatformId.Linux,
                manifestLoader: ThrowingLoader("unused"));

            Assert.Equal(WinixExitCode.Success, exit);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~CliTests.RunAsync_Agents"`
Expected: FAIL — `agents` is an unknown command (returns `UsageError` with "unknown command", and the init test fails its `Success` assertion).

- [ ] **Step 3: Write minimal implementation**

In `src/Winix.Winix/Cli.cs`:

(a) Add the two parser options. After the existing `.Flag("--dry-run", …)` line (around line 73):

```csharp
            .Flag("--dry-run", "Show commands that would be run without executing them")
            .Flag("--claude", "agents: include CLAUDE.md even when it does not already exist")
            .Option("--path", null, "DIR", "agents: project directory to operate on (default: current directory)")
```

(b) Add `agents` examples. After the existing `.Example("winix status", …)` line:

```csharp
            .Example("winix agents init", "Write the Winix discoverability pointer into AGENTS.md (and CLAUDE.md if present)")
            .Example("winix agents status", "Report whether the pointer block is present and current (exit 1 if not)")
            .Example("winix agents remove", "Remove the Winix discoverability pointer block")
```

(c) Add `agents` to the command whitelist and both error messages (around lines 109–115):

```csharp
        if (command != "install" && command != "update" && command != "uninstall"
            && command != "list" && command != "status" && command != "agents")
        {
            return result.WriteError(
                $"unknown command '{command}' (expected install, update, uninstall, list, status, or agents)",
                stderr);
        }
```

Also update the no-command message (line 103):

```csharp
            return result.WriteError("missing command (expected install, update, uninstall, list, status, or agents)", stderr);
```

(d) Dispatch `agents` BEFORE the `--via` validation and manifest load. Insert immediately after the whitelist check above (before the `string? viaOverride = …` line):

```csharp
        if (command == "agents")
        {
            // agents never needs the tool manifest (the block delegates "what's installed" to
            // the runtime `winix list` pointer), so dispatch before --via validation and the
            // manifest fetch — neither applies, and a manifest fetch failure must not block it.
            string? verb = result.Positionals.Length > 1 ? result.Positionals[1] : null;
            string baseDir = result.Has("--path")
                ? result.GetString("--path")!
                : Directory.GetCurrentDirectory();

            var agentsOptions = new AgentsManager.AgentsOptions(
                Verb: verb,
                BaseDir: baseDir,
                ForceClaude: result.Has("--claude"),
                DryRun: result.Has("--dry-run"),
                Json: result.Has("--json"),
                Version: version);

            return AgentsManager.Run(agentsOptions, stdout, stderr);
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~CliTests.RunAsync_Agents"`
Expected: PASS.

- [ ] **Step 5: Run the full winix test project to confirm no regression**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj`
Expected: PASS (all existing + new tests).

- [ ] **Step 6: Commit**

```bash
git add src/Winix.Winix/Cli.cs tests/Winix.Winix.Tests/CliTests.cs
git commit -m "feat(winix): wire agents subcommand into Cli (whitelist, --path/--claude, dispatch pre-manifest)"
```

---

### Task 13: Regenerate the `winix` `--describe` contract snapshot

**Files:**
- Modify: `tests/Winix.Contract.Tests/snapshots/winix.describe.json`

- [ ] **Step 1: Run the contract test to confirm it now fails (drift detected)**

Run: `dotnet test tests/Winix.Contract.Tests/Winix.Contract.Tests.csproj --filter "FullyQualifiedName~winix"`
Expected: FAIL — the `winix` snapshot no longer matches (new `--path`/`--claude` options + `agents` examples).

- [ ] **Step 2: Regenerate the snapshot in update mode**

Run (PowerShell):
```powershell
$env:WINIX_UPDATE_SNAPSHOTS = "1"; dotnet test tests/Winix.Contract.Tests/Winix.Contract.Tests.csproj --filter "FullyQualifiedName~winix"; Remove-Item Env:\WINIX_UPDATE_SNAPSHOTS
```
Expected: the run writes the updated `tests/Winix.Contract.Tests/snapshots/winix.describe.json` AND fails (update mode always fails so CI can never self-update).

- [ ] **Step 3: Inspect the diff to confirm only the intended additions**

Run: `git diff tests/Winix.Contract.Tests/snapshots/winix.describe.json`
Expected: only the new `--path` (string, placeholder `DIR`) and `--claude` (flag) options and the three `winix agents …` examples were added; nothing else changed (schema_version, maturity, exit_codes, platform, io unchanged).

- [ ] **Step 4: Run the contract test normally to confirm green**

Run: `dotnet test tests/Winix.Contract.Tests/Winix.Contract.Tests.csproj --filter "FullyQualifiedName~winix"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/Winix.Contract.Tests/snapshots/winix.describe.json
git commit -m "test(contract): regenerate winix --describe snapshot for agents subcommand"
```

---

### Task 14: Docs & bookkeeping

**Files:**
- Modify: `AGENTS.md`
- Modify: `docs/plans/2026-06-06-agent-adoption-hardening-design.md`
- Modify: `src/winix/README.md`
- Modify: `src/winix/man/man1/winix.1` (check for a `.1.md` source first)
- Modify: `docs/ai/winix.md`
- Modify: `llms.txt`
- Modify: `src/winix/CHANGELOG.md`

- [ ] **Step 1: Update `AGENTS.md` (the "Known limitation" section)**

Replace the final paragraph (lines ~52–54, "The intended fix for that case is a `winix init` subcommand … not yet shipped …") with:

```markdown
The fix for that case is the `winix agents init` subcommand: it writes a short, marker-delimited
pointer block into the project's `AGENTS.md` (and `CLAUDE.md` if present) so any agent loading
that project picks up the guidance. Run `winix agents init` in a project root; `winix agents
status` reports whether the block is present and current, and `winix agents remove` strips it.
```

- [ ] **Step 2: Update the parent design doc's Rec 4 status**

In `docs/plans/2026-06-06-agent-adoption-hardening-design.md`, change the Rec 4 heading and status note from "`winix init`" to "`winix agents init`" and mark it shipped, referencing `docs/plans/2026-06-08-winix-agents-design.md`. (Edit the line at the top "Rec 4 (`winix init`) is the next piece of work — not yet started." to point at the new design/ADR and say it is now designed + implemented.)

- [ ] **Step 3: Add the `agents` subcommand to `src/winix/README.md`**

Add a section documenting the three verbs, the `--path`/`--claude`/`--dry-run`/`--json` flags, the exit codes (0 success, 1 status-drift, 125 usage, 127 I/O), and the managed-block contract (marker-delimited, version-pinned URL, "edits between markers are overwritten"). Follow the existing README's section style.

- [ ] **Step 4: Update the man page**

Run: `git ls-files '*.1.md'`
- If `src/winix/winix.1.md` exists: edit the `.md`, regenerate with `pandoc -s -t man src/winix/winix.1.md -o src/winix/man/man1/winix.1`, then `git diff` the `.1` to confirm only the agents addition + reflow changed.
- If no `.1.md`: edit `src/winix/man/man1/winix.1` (groff) directly — add an `agents` subsection under the commands.

- [ ] **Step 5: Update `docs/ai/winix.md`**

Document `winix agents`:
- the three verbs and the managed-block contract;
- the `status || init` bootstrap idiom;
- the version-pinned URL behaviour: the URL pins to `v{version}/AGENTS.md` (the tag matching the installed binary); **pre-release versions (the version string contains `-`) fall back to `main`**. (Phrase it as "pre-release → main", NOT "dev → main" — see the note in Step 8.)
- `--json` is honoured on **all three** verbs (init/remove emit `{"action","dryRun","files"}`; status emits `{"current","files":[{"path","state","version"}]}`);
- **the marker limitation (F6):** the first `<!-- winix:start … -->` … `<!-- winix:end -->` pair is the managed block; do not place that literal marker pair in your own prose, or `init`/`remove` will treat it as the block. A start marker with no end is ignored (init appends a fresh block).

- [ ] **Step 6: Update `llms.txt`**

Note that `winix` now self-installs its own project-level discoverability pointer via `winix agents init`.

- [ ] **Step 7: Add a `CHANGELOG.md` entry**

In `src/winix/CHANGELOG.md`, add under an `Added` heading for the next version: `- `winix agents init|remove|status`: write/refresh/remove a marker-delimited, version-pinned discoverability pointer in a project's AGENTS.md/CLAUDE.md.`

- [ ] **Step 8: Doc↔behaviour reconciliation (verification oracle)**

For every claim added above, run the command that demonstrates it and confirm the output matches:
```bash
dotnet run --project src/winix -- agents init --path /tmp/winix-doc-check
dotnet run --project src/winix -- agents status --path /tmp/winix-doc-check   # expect exit 0
dotnet run --project src/winix -- agents remove --path /tmp/winix-doc-check
dotnet run --project src/winix -- agents status --path /tmp/winix-doc-check   # expect exit 1
dotnet run --project src/winix -- agents --help
```
Confirm the generated block's URL line matches the version-pinning rule for whatever version the dev build reports. **NOTE (corrected):** this repo's dev `<Version>` is `0.1.0` (`Directory.Build.props`, no `-`), so a local dev build emits `/blob/v0.1.0/AGENTS.md` — which may 404 because `AGENTS.md` postdates the `v0.1.0` tag. That is a dev-build-only artifact and is expected. Shipped stable binaries report their real version (e.g. `0.4.0`), whose tag has `AGENTS.md`; only pre-release versions (string contains `-`) fall back to `/blob/main/`. **Report the actual URL observed — do NOT assert `/blob/main/` for the dev build.** (The release-time HTTP-200 guard in Task 15 only checks stable releases, which are correct.)

- [ ] **Step 9: Commit**

```bash
git add AGENTS.md docs/plans/2026-06-06-agent-adoption-hardening-design.md src/winix/README.md src/winix/man/man1/winix.1 docs/ai/winix.md llms.txt src/winix/CHANGELOG.md
# include src/winix/winix.1.md in the add if a pandoc source exists
git commit -m "docs(winix): document agents subcommand across all surfaces"
```

---

### Task 15: Release-pipeline guard — version-pinned URL resolves (HTTP 200)

**Files:**
- Modify: `.github/workflows/post-publish.yml`

- [ ] **Step 1: Read the workflow to find the right job/step placement**

Run: `git show HEAD:.github/workflows/post-publish.yml`
Identify a job that runs after the tag exists and has network access (the same context the scoop/winget manifest jobs run in), and the variable holding the release version/tag.

- [ ] **Step 2: Add a guard step**

Add a step (stable releases only — skip when the version contains `-`) that fetches the version-pinned `AGENTS.md` and fails the job on non-200. Example shape (adapt variable names to the workflow's existing version variable):

```yaml
      - name: Verify version-pinned agents URL resolves
        shell: bash
        run: |
          ver="${VERSION}"   # adapt to the workflow's actual version/tag variable
          case "$ver" in
            *-*) echo "Pre-release ($ver) — block uses /main/, skipping pinned-URL check"; exit 0 ;;
          esac
          url="https://raw.githubusercontent.com/Yortw/winix/v${ver}/AGENTS.md"
          code="$(curl -s -o /dev/null -w '%{http_code}' "$url")"
          echo "GET $url -> $code"
          if [ "$code" != "200" ]; then
            echo "::error::version-pinned agents URL did not resolve ($code): $url"
            exit 1
          fi
```

- [ ] **Step 3: Validate the workflow YAML locally**

Run: `python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/post-publish.yml')); print('ok')"`
Expected: `ok` (no YAML syntax error).

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/post-publish.yml
git commit -m "ci(post-publish): guard that the version-pinned agents AGENTS.md URL resolves"
```

---

## Final verification (run after all tasks)

- [ ] **Full solution build + test**

Run: `dotnet build Winix.sln` then `dotnet test Winix.sln`
Expected: 0 warnings (warnings-as-errors), all tests pass (the 4 clip clipboard-busy env flakes may fail locally only — they pass headless in CI).

- [ ] **Native AOT smoke (the real binary, per `feedback_cli_auto_defaults`)**

Run:
```powershell
dotnet publish src/winix/winix.csproj -c Release -r win-x64
# then, against the published winix.exe:
winix agents init --path $env:TEMP\winix-aot-check
winix agents status --path $env:TEMP\winix-aot-check   # expect exit 0
winix agents init --path $env:TEMP\winix-aot-check      # re-run: file must be byte-identical
winix agents remove --path $env:TEMP\winix-aot-check
winix agents status --path $env:TEMP\winix-aot-check   # expect exit 1
```
Confirm the block renders, the re-run is byte-stable, and the status exit codes match.

---

## Self-review notes (author)

- **Spec coverage:** every design section maps to a task — block content (T2), version-pinned URL + invariant C (T1) + invariant B-via-pipeline (T15), verbs (T8/9/10/11), option-B targets (T7), exit codes (T8/9/11/12), marker mechanics (T3/4/5), error handling (T8/10), `--describe` (T13), docs incl. the AGENTS.md/Rec-4 rename (T14). Invariant A is free-by-construction (noted, no task). The standalone "AGENTS.md at repo root" guard is folded into T2 (template asserts `/AGENTS.md`) + T15 (live resolution) rather than a fragile repo-root filesystem walk.
- **No placeholders:** all code steps show complete code; the only "adapt to your workflow" item is T15's version-variable name, which genuinely depends on reading the existing YAML in T15 Step 1.
- **Type consistency:** `AgentsOptions`, `IAgentsFileSystem`, `RunInit/RunStatus/RunRemove/Run`, `ResolveInitTargets`, `RenderBlock/MergeBlock/RemoveBlock/FindBlockVersion/UrlRef`, `FormatActionJson/FormatStatusJson` are used identically across tasks; `WinixExitCode.{Success,ToolFailure,UsageError,InternalError}` match the existing enum.

## Adversarial-review integration (2026-06-08)

A fresh-subagent adversarial review (15-category taxonomy) produced 3 blockers, 6 test gaps, 3 defers. All integrated:

- **F1 (blocker) — non-atomic write → data loss.** `DefaultAgentsFileSystem.WriteAllText` now writes a sibling temp + `File.Move(overwrite)` (Task 6). No-residue test added; true crash-atomicity stated as argued-not-tested.
- **F4 (blocker) — block advertised "126 (runtime)" but winix returns 127.** Block convention line changed to "non-zero on failure (per-tool codes in `--describe`)", true for every tool; pinned by a `RenderBlock` assertion (Task 2) and reflected in the design doc.
- **F3 — multi-file partial failure unnamed.** `RunInit`/`RunRemove` name the failed file; `PartialFailFs` test added (Tasks 8, 10).
- **F8 — `RunStatus` read uncaught.** Wrapped in try/catch → `InternalError`; `ReadFailFs` test (Task 9).
- **F10 — `--json` accepted-but-ignored on init/remove.** Minimal `FormatActionJson` envelope honoured; tests (Tasks 8, 10).
- **F5 — garbage version from malformed `v=`.** Token terminates at whitespace or `--`; theory test (Task 3).
- **F2 — path-is-file / target-is-directory.** Covered by the bad-path (DirectoryExists false) and write-fails (ThrowingFs) tests, annotated (Task 8).
- **F6 — literal marker / two blocks.** First-match pinned for merge (Task 4); `RemoveBlock` upgraded to strip *all* blocks (Task 5); limitation documented (Task 14, design, ADR).
- **F7 — status multi-file aggregation under-tested.** Both-stale, absent+current, forced-claude-absent, and a CLI init→status success-path test (Tasks 9, 12).
- **F9 (defer) — plan cut the design's repo-root guard test.** Recorded as a plan↔design divergence in the ADR deferred table; invariant B is enforced at release time by the `post-publish.yml` HTTP-200 check (Task 15), with the limitation that a `main`-pinned pre-release block is unverified until the next stable tag.
