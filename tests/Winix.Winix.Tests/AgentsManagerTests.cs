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
    // I1: a missing end-marker `-->` causes the scan to walk into the next `<!--` — the `<`
    // stop plus the `end` bound must clamp the token to the clean prefix, never return garbage.
    [InlineData("<!-- winix:start v=0.4.0<!-- winix:end -->\nbody\n<!-- winix:end -->", "0.4.0")]
    public void FindBlockVersion_MalformedVersionToken_NoGarbage(string file, string? expected)
    {
        Assert.Equal(expected, AgentsManager.FindBlockVersion(file));
    }

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
            // F1/M2: the atomic temp+move must not leave any sidecar behind on success,
            // regardless of the random suffix used by this call.
            Assert.Empty(Directory.GetFiles(dir, ".AGENTS.md.winix-*"));
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

    // ── ResolveInitTargets (option B) ─────────────────────────────────────────────

    private static AgentsManager.AgentsOptions Opts(string verb, string baseDir, bool forceClaude = false,
        bool dryRun = false, bool json = false, string version = "0.4.0")
    {
        return new AgentsManager.AgentsOptions(verb, baseDir, forceClaude, dryRun, json, version);
    }

    [Fact]
    public void ResolveInitTargets_NoClaudeFile_AgentsMdOnly()
    {
        var fs = new InMemoryFs();
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
}
