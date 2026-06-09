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
        // F4: the exit-code convention line must be true for EVERY tool — no specific failure
        // code (less/squeeze use exit 2; winix uses 127). Lock the universal wording only.
        // The assertion spans two source lines joined by \n + indent, so check each fragment.
        Assert.Contains("exit 0 = success, non-zero on", block, StringComparison.Ordinal);
        Assert.Contains("usage/runtime codes vary by tool", block, StringComparison.Ordinal);
        Assert.DoesNotContain("125", block, StringComparison.Ordinal);
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

    [Fact]
    public void RenderBlock_UserScope_AssertsAvailabilityAndKeepsRestraint()
    {
        string block = AgentsManager.RenderBlock("0.4.0", AgentsManager.RenderMode.UserScope);

        Assert.Contains("## Winix CLI tools (available on this machine)", block, StringComparison.Ordinal);
        Assert.Contains("Prefer a Winix tool only when it's genuinely the better choice", block, StringComparison.Ordinal);
        Assert.DoesNotContain("If Winix is not", block, StringComparison.Ordinal); // no conditional escape hatch in user mode
        Assert.Contains("not by", block, StringComparison.Ordinal);
        Assert.Contains("use the default", block, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBlock_ProjectScope_UsesConditionalWordingNoInstallAssertion()
    {
        string block = AgentsManager.RenderBlock("0.4.0", AgentsManager.RenderMode.ProjectScope);

        Assert.Contains("## Winix CLI tools (if available in your environment)", block, StringComparison.Ordinal);
        Assert.Contains("If Winix tools are installed in your environment", block, StringComparison.Ordinal);
        Assert.Contains("If Winix is not", block, StringComparison.Ordinal); // explicit ignore-if-absent escape hatch
        Assert.DoesNotContain("(available on this machine)", block, StringComparison.Ordinal); // no machine-local assertion
        // Shared invariants hold in both modes:
        Assert.Contains("https://github.com/Yortw/winix/blob/v0.4.0/AGENTS.md", block, StringComparison.Ordinal);
        Assert.Contains("`winix list`", block, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBlock_DefaultModeIsUserScope()
    {
        Assert.Equal(
            AgentsManager.RenderBlock("0.4.0", AgentsManager.RenderMode.UserScope),
            AgentsManager.RenderBlock("0.4.0"));
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
    public void MergeBlock_CrlfFileWithExistingBlock_ReMergeIsByteStable()
    {
        string once = AgentsManager.MergeBlock("# Project\r\n", "0.4.0");
        string twice = AgentsManager.MergeBlock(once, "0.4.0");
        Assert.Equal(once, twice);
    }

    [Fact]
    public void MergeBlock_BlockAtPositionZero_ReplacesCleanlyNoLeadingBlank()
    {
        string file = AgentsManager.RenderBlock("0.3.0") + "\n";
        string merged = AgentsManager.MergeBlock(file, "0.4.0");
        Assert.StartsWith("<!-- winix:start", merged, StringComparison.Ordinal);
        Assert.Contains("blob/v0.4.0/AGENTS.md", merged, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(merged, AgentsManager.StartMarkerPrefix));
    }

    [Fact]
    public void MergeBlock_ProjectMode_InsertsConditionalBlock()
    {
        string merged = AgentsManager.MergeBlock("# Repo\n", "0.4.0", AgentsManager.RenderMode.ProjectScope);

        Assert.Contains("(if available in your environment)", merged, StringComparison.Ordinal);
        Assert.StartsWith("# Repo", merged, StringComparison.Ordinal); // surrounding text preserved
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

    [Fact] // The mode-agnostic marker parser still finds + removes a PROJECT-mode block — cross-mode
           // belt-and-braces over the existing parser tests (FindBlockVersion_StartWithoutEnd_ReturnsNull,
           // RemoveBlock duplicate-strip) which already pin malformed-marker handling.
    public void RemoveBlock_ProjectModeBlock_StrippedByModeAgnosticParser()
    {
        string file = "# Repo\n\n" + AgentsManager.RenderBlock("0.4.0", AgentsManager.RenderMode.ProjectScope) + "\n";
        Assert.Equal("0.4.0", AgentsManager.FindBlockVersion(file));
        string after = AgentsManager.RemoveBlock(file);
        Assert.DoesNotContain("winix:start", after, StringComparison.Ordinal);
        Assert.Contains("# Repo", after, StringComparison.Ordinal);
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

    // In-memory fake shared by orchestration tests (both project- and user-scope). Exposes the
    // user-home seam (ResolveHome/CreateDirectory) plus a fault-injection knob for write failures.
    private sealed class InMemoryFs : IAgentsFileSystem
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Dirs { get; } = new(StringComparer.Ordinal);
        public string Home { get; set; } = "/home/u";
        public List<string> Created { get; } = new();
        // Fault-injection knob: when set, WriteAllText throws for paths containing this substring,
        // so the IOException-surfacing contract in Run* is reachable by a deterministic test.
        public string? ThrowOnWritePath { get; set; }

        public bool FileExists(string path) => Files.ContainsKey(path);
        public bool DirectoryExists(string path) => Dirs.Contains(path);
        public string ReadAllText(string path) => Files[path];
        public void WriteAllText(string path, string content)
        {
            if (ThrowOnWritePath != null && path.Contains(ThrowOnWritePath, StringComparison.Ordinal))
            {
                throw new IOException("injected write failure");
            }
            Files[path] = content;
        }
        public string ResolveHome() => Home;
        public void CreateDirectory(string path) { Dirs.Add(path); Created.Add(path); }
    }

    private sealed class ThrowingFs : IAgentsFileSystem
    {
        public bool FileExists(string path) => false;
        public bool DirectoryExists(string path) => true;
        public string ReadAllText(string path) => string.Empty;
        public void WriteAllText(string path, string content) => throw new IOException("disk full");
        public string ResolveHome() => "/home/u";
        public void CreateDirectory(string path) { }
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
        public string ResolveHome() => "/home/u";
        public void CreateDirectory(string path) { }
    }

    // File exists but the read throws — models a permission-denied read for the F8 status path.
    private sealed class ReadFailFs : IAgentsFileSystem
    {
        public bool FileExists(string path) => true;
        public bool DirectoryExists(string path) => true;
        public string ReadAllText(string path) => throw new UnauthorizedAccessException("denied");
        public void WriteAllText(string path, string content) => throw new UnauthorizedAccessException("denied");
        public string ResolveHome() => "/home/u";
        public void CreateDirectory(string path) { }
    }

    // Reads AGENTS.md fine but throws on CLAUDE.md — exercises the second-file read diagnostic.
    private sealed class ReadFailSecondFs : IAgentsFileSystem
    {
        public bool FileExists(string path) => true;
        public bool DirectoryExists(string path) => true;
        public string ReadAllText(string path)
        {
            if (path.EndsWith("CLAUDE.md", StringComparison.Ordinal)) { throw new UnauthorizedAccessException("denied"); }
            return AgentsManager.MergeBlock(string.Empty, "0.4.0");
        }
        public void WriteAllText(string path, string content) { }
        public string ResolveHome() => "/home/u";
        public void CreateDirectory(string path) { }
    }

    // For RunRemove write-failure: AGENTS.md exists and contains a block, but the write throws.
    private sealed class RemoveWriteFailFs : IAgentsFileSystem
    {
        public bool FileExists(string path) => path.EndsWith("AGENTS.md", StringComparison.Ordinal);
        public bool DirectoryExists(string path) => true;
        public string ReadAllText(string path) => AgentsManager.RenderBlock("0.4.0");
        public void WriteAllText(string path, string content) => throw new IOException("disk full");
        public string ResolveHome() => "/home/u";
        public void CreateDirectory(string path) { }
    }

    // ── ResolveInitTargets (option B) ─────────────────────────────────────────────

    // Project-scope options for the pre-existing orchestration tests (committed repo files).
    private static AgentsManager.AgentsOptions Opts(string verb, string baseDir, bool forceClaude = false,
        bool dryRun = false, bool json = false, string version = "0.4.0")
    {
        return new AgentsManager.AgentsOptions(
            verb, AgentsManager.AgentsScope.Project, baseDir, forceClaude,
            ForceCodex: false, dryRun, json, version);
    }

    // User-scope options for the home-resolution / user-default tests.
    private static AgentsManager.AgentsOptions UserOpts(bool forceClaude = false, bool forceCodex = false) =>
        new("init", AgentsManager.AgentsScope.User, ".", forceClaude, forceCodex, false, false, "0.4.0");

    [Fact]
    public void AgentsOptions_CarriesScopeAndForceCodex()
    {
        var opts = new AgentsManager.AgentsOptions(
            Verb: "init", Scope: AgentsManager.AgentsScope.User, BaseDir: ".",
            ForceClaude: false, ForceCodex: true, DryRun: false, Json: false, Version: "0.4.0");

        Assert.Equal(AgentsManager.AgentsScope.User, opts.Scope);
        Assert.True(opts.ForceCodex);
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

    // ── ResolveUserTargets (user scope) ───────────────────────────────────────────

    [Fact]
    public void ResolveUserTargets_OnlyExistingHomes()
    {
        var fs = new InMemoryFs { Home = "/home/u" };
        fs.Dirs.Add(Path.Combine("/home/u", ".claude")); // codex dir absent

        var targets = AgentsManager.ResolveUserTargets(UserOpts(), fs);

        Assert.Equal(new[] { Path.Combine("/home/u", ".claude", "CLAUDE.md") }, targets);
    }

    [Fact]
    public void ResolveUserTargets_ForceCodexIncludesAbsentHome()
    {
        var fs = new InMemoryFs { Home = "/home/u" };
        fs.Dirs.Add(Path.Combine("/home/u", ".claude"));

        var targets = AgentsManager.ResolveUserTargets(UserOpts(forceCodex: true), fs);

        Assert.Contains(Path.Combine("/home/u", ".claude", "CLAUDE.md"), targets);
        Assert.Contains(Path.Combine("/home/u", ".codex", "AGENTS.md"), targets);
    }

    [Fact]
    public void ResolveUserTargets_NoHomesNoForce_Empty()
    {
        var fs = new InMemoryFs { Home = "/home/u" };
        Assert.Empty(AgentsManager.ResolveUserTargets(UserOpts(), fs));
    }

    // ── RunInit ───────────────────────────────────────────────────────────────────

    [Fact]
    public void RunInit_UserScope_WritesExistingHomesWithAssertWording()
    {
        var fs = new InMemoryFs { Home = "/home/u" };
        fs.Dirs.Add(Path.Combine("/home/u", ".claude"));
        var sw = new StringWriter();

        int code = AgentsManager.RunInit(UserOpts(), fs, sw, sw);

        Assert.Equal(WinixExitCode.Success, code);
        string written = fs.Files[Path.Combine("/home/u", ".claude", "CLAUDE.md")];
        Assert.Contains("(available on this machine)", written, StringComparison.Ordinal);
    }

    [Fact]
    public void RunInit_UserScope_ForceCodex_CreatesDirAndWrites()
    {
        var fs = new InMemoryFs { Home = "/home/u" }; // no homes exist
        var sw = new StringWriter();

        int code = AgentsManager.RunInit(UserOpts(forceCodex: true), fs, sw, sw);

        Assert.Equal(WinixExitCode.Success, code);
        // Compute the expected dir the way production does (Path.GetDirectoryName of the target),
        // so the assertion is robust to OS separator normalisation (/ → \ on Windows).
        string codexFile = Path.Combine("/home/u", ".codex", "AGENTS.md");
        Assert.Contains(Path.GetDirectoryName(codexFile)!, fs.Created);
        Assert.True(fs.Files.ContainsKey(codexFile));
    }

    [Fact]
    public void RunInit_UserScope_NoHomesNoForce_UsageErrorWritesNothing()
    {
        var fs = new InMemoryFs { Home = "/home/u" };
        var sw = new StringWriter();

        int code = AgentsManager.RunInit(UserOpts(), fs, sw, sw);

        Assert.Equal(WinixExitCode.UsageError, code);
        Assert.Empty(fs.Files);
        Assert.Contains("no agent home found", sw.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunInit_ProjectScope_WritesConditionalWording()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/repo");
        var opts = new AgentsManager.AgentsOptions(
            "init", AgentsManager.AgentsScope.Project, "/repo", false, false, false, false, "0.4.0");
        var sw = new StringWriter();

        int code = AgentsManager.RunInit(opts, fs, sw, sw);

        Assert.Equal(WinixExitCode.Success, code);
        Assert.Contains("(if available in your environment)",
            fs.Files[Path.Combine("/repo", "AGENTS.md")], StringComparison.Ordinal);
    }

    [Fact] // IOException mid-write surfaces InternalError + names the failing target.
    public void RunInit_WriteFailure_InternalErrorNamesTarget()
    {
        var fs = new InMemoryFs { Home = "/home/u", ThrowOnWritePath = ".claude" };
        fs.Dirs.Add(Path.Combine("/home/u", ".claude"));
        var sw = new StringWriter();

        int code = AgentsManager.RunInit(UserOpts(), fs, sw, sw);

        Assert.Equal(WinixExitCode.InternalError, code);
        Assert.Contains(Path.Combine("/home/u", ".claude", "CLAUDE.md"), sw.ToString(), StringComparison.Ordinal);
        // Resource-key history: the surfaced text must not leak a bare framework SR key.
        Assert.DoesNotContain("Arg_", sw.ToString(), StringComparison.Ordinal);
    }

    [Fact] // A non-existent WINIX_AGENTS_HOME resolves to empty-home, not a crash.
    public void RunInit_NonExistentHome_NoForce_EmptyHomeUsageError()
    {
        var fs = new InMemoryFs { Home = "/does/not/exist" }; // no dirs registered
        var sw = new StringWriter();

        int code = AgentsManager.RunInit(UserOpts(), fs, sw, sw);

        Assert.Equal(WinixExitCode.UsageError, code);
        Assert.Contains("no agent home found", sw.ToString(), StringComparison.Ordinal);
    }

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
    public void RunInit_ForceClaude_NoExistingFiles_WritesBothFiles()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunInit(Opts("init", "/proj", forceClaude: true), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.Success, exit);
        Assert.Contains("blob/v0.4.0/AGENTS.md", fs.Files[Path.Combine("/proj", "AGENTS.md")], StringComparison.Ordinal);
        Assert.Contains("blob/v0.4.0/AGENTS.md", fs.Files[Path.Combine("/proj", "CLAUDE.md")], StringComparison.Ordinal);
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
        Assert.Empty(stderr.ToString());
    }

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
        Assert.Empty(stderr.ToString());
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

    [Fact]
    public void RunStatus_SecondFileReadFails_NamesClaudeMd()
    {
        var (stdout, stderr) = (new StringWriter(), new StringWriter());
        int exit = AgentsManager.RunStatus(Opts("status", "/proj"), new ReadFailSecondFs(), stdout, stderr);
        Assert.Equal(WinixExitCode.InternalError, exit);
        Assert.Contains("CLAUDE.md", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunStatus_UserScope_NoHome_ReportsNotCurrent()
    {
        var fs = new InMemoryFs { Home = "/home/u" };
        var sw = new StringWriter();

        int code = AgentsManager.RunStatus(UserOpts(), fs, sw, sw);

        Assert.Equal(WinixExitCode.ToolFailure, code);
        Assert.Contains("no agent home found", sw.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunStatus_UserScope_CurrentBlock_Zero()
    {
        var fs = new InMemoryFs { Home = "/home/u" };
        string claudeDir = Path.Combine("/home/u", ".claude");
        fs.Dirs.Add(claudeDir);
        fs.Files[Path.Combine(claudeDir, "CLAUDE.md")] =
            AgentsManager.MergeBlock(string.Empty, "0.4.0", AgentsManager.RenderMode.UserScope);
        var sw = new StringWriter();

        Assert.Equal(WinixExitCode.Success, AgentsManager.RunStatus(UserOpts(), fs, sw, sw));
    }

    [Fact] // Pin the no-home --json shape so the no-home/absent-block ambiguity is a conscious,
           // documented choice rather than an accident. allCurrent must be false.
    public void RunStatus_UserScope_NoHome_Json_ShapePinned()
    {
        var fs = new InMemoryFs { Home = "/home/u" };
        var sw = new StringWriter();
        var opts = new AgentsManager.AgentsOptions(
            "status", AgentsManager.AgentsScope.User, ".", false, false, false, Json: true, "0.4.0");

        int code = AgentsManager.RunStatus(opts, fs, sw, sw);

        Assert.Equal(WinixExitCode.ToolFailure, code);
        Assert.Contains("\"current\":false", sw.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"files\":[]", sw.ToString(), StringComparison.Ordinal);
    }

    [Fact] // KNOWN LIMITATION: markers encode version only, not render mode. A current-version
           // block whose WORDING is the wrong mode reports "current" — status can't detect wording
           // drift. This test documents that behaviour so it's a conscious accepted limitation.
    public void RunStatus_UserScope_ProjectWordedBlockAtCurrentVersion_ReportsCurrent()
    {
        var fs = new InMemoryFs { Home = "/home/u" };
        string claudeDir = Path.Combine("/home/u", ".claude");
        fs.Dirs.Add(claudeDir);
        // A project-mode block (conditional wording) sitting in a USER home file at current version.
        fs.Files[Path.Combine(claudeDir, "CLAUDE.md")] =
            AgentsManager.MergeBlock(string.Empty, "0.4.0", AgentsManager.RenderMode.ProjectScope);
        var sw = new StringWriter();

        // Reported current despite wrong-mode wording — markers carry no mode token. Accepted.
        Assert.Equal(WinixExitCode.Success, AgentsManager.RunStatus(UserOpts(), fs, sw, sw));
    }

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
    public void RunRemove_UserScope_StripsBlock()
    {
        var fs = new InMemoryFs { Home = "/home/u" };
        string claudeDir = Path.Combine("/home/u", ".claude");
        fs.Dirs.Add(claudeDir);
        string file = Path.Combine(claudeDir, "CLAUDE.md");
        fs.Files[file] = AgentsManager.MergeBlock("# keep me\n", "0.4.0", AgentsManager.RenderMode.UserScope);
        var sw = new StringWriter();

        Assert.Equal(WinixExitCode.Success, AgentsManager.RunRemove(UserOpts(), fs, sw, sw));
        Assert.DoesNotContain("winix:start", fs.Files[file], StringComparison.Ordinal);
        Assert.Contains("# keep me", fs.Files[file], StringComparison.Ordinal);
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
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void RunRemove_BadPath_ReturnsUsageError()
    {
        var fs = new InMemoryFs(); // no dirs registered → DirectoryExists false
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunRemove(Opts("remove", "/nope"), fs, stdout, stderr);

        Assert.Equal(WinixExitCode.UsageError, exit);
        Assert.Contains("not a directory", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunRemove_WriteFails_ReturnsInternalErrorWithCleanMessage()
    {
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.RunRemove(Opts("remove", "/proj"), new RemoveWriteFailFs(), stdout, stderr);

        Assert.Equal(WinixExitCode.InternalError, exit);
        Assert.Contains("AGENTS.md", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", stderr.ToString(), StringComparison.Ordinal);
    }

    // ── Run dispatch ──────────────────────────────────────────────────────────────

    [Fact]
    public void Run_NoVerb_ReturnsUsageErrorListingVerbs()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        var opts = new AgentsManager.AgentsOptions(
            Verb: null, Scope: AgentsManager.AgentsScope.Project, BaseDir: "/proj",
            ForceClaude: false, ForceCodex: false, DryRun: false, Json: false, Version: "0.4.0");
        int exit = AgentsManager.Run(opts, stdout, stderr, fs);

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

    [Fact]
    public void Run_RemoveVerb_DispatchesToRunRemove()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj");
        // A file with a block so remove has something to strip and reports success.
        fs.Files[Path.Combine("/proj", "AGENTS.md")] = AgentsManager.MergeBlock(string.Empty, "0.4.0");
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.Run(Opts("remove", "/proj"), stdout, stderr, fs);

        Assert.Equal(WinixExitCode.Success, exit);
        Assert.DoesNotContain(AgentsManager.StartMarkerPrefix, fs.Files[Path.Combine("/proj", "AGENTS.md")], StringComparison.Ordinal);
    }

    [Fact]
    public void Run_StatusVerb_DispatchesToRunStatus()
    {
        var fs = new InMemoryFs();
        fs.Dirs.Add("/proj"); // AGENTS.md absent → status drift → ToolFailure
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        int exit = AgentsManager.Run(Opts("status", "/proj"), stdout, stderr, fs);

        Assert.Equal(WinixExitCode.ToolFailure, exit);
    }
}
