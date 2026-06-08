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
    public void FindBlockVersion_MalformedVersionToken_NoGarbage(string file, string? expected)
    {
        Assert.Equal(expected, AgentsManager.FindBlockVersion(file));
    }
}
