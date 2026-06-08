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
}
