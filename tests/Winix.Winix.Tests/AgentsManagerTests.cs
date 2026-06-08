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
