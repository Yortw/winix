#nullable enable

using System.Diagnostics;
using Winix.Man;
using Xunit;

namespace Winix.Man.Tests;

/// <summary>
/// Unit tests for <see cref="PagerChain"/>'s pure-function helpers. The Process.Start path
/// itself is integration-tier (needs a real terminal) and is exercised by the smoke harness
/// in artifacts/baseline-2026-05-07/man.
/// </summary>
public sealed class PagerChainTests
{
    // Tier-2 baseline 2026-05-07 finding F5: pre-fix, ProcessStartInfo.FileName received the
    // raw $MANPAGER value, so MANPAGER='less -R' (the canonical Linux/macOS configuration)
    // was treated as the literal filename "less -R" and silently failed, falling back to the
    // built-in pager. Real man-db dispatches through /bin/sh -c "$MANPAGER" so users can set
    // any shell command line. These tests pin the dispatch shape — Windows uses cmd.exe /c,
    // other platforms use /bin/sh -c.

    [Fact]
    public void BuildPagerProcessStartInfo_OnAnyPlatform_RedirectsStandardInput()
    {
        ProcessStartInfo psi = PagerChain.BuildPagerProcessStartInfo("less -R");

        Assert.True(psi.RedirectStandardInput, "Pager must receive content via stdin pipe");
        Assert.False(psi.UseShellExecute, "UseShellExecute=true would prevent stdin redirection");
    }

    [Fact]
    public void BuildPagerProcessStartInfo_PassesPagerCommandUnsplit()
    {
        // The shell does the splitting; we pass the raw env-var value through as one argument.
        // If we whitespace-split here we'd reintroduce the F5 bug shape on the shell side.
        ProcessStartInfo psi = PagerChain.BuildPagerProcessStartInfo("less -R");

        Assert.Contains("less -R", psi.ArgumentList);
    }

    [Fact]
    public void BuildPagerProcessStartInfo_UsesCmdShellOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProcessStartInfo psi = PagerChain.BuildPagerProcessStartInfo("less -R");

        Assert.Equal("cmd.exe", psi.FileName);
        Assert.Equal(2, psi.ArgumentList.Count);
        Assert.Equal("/c", psi.ArgumentList[0]);
        Assert.Equal("less -R", psi.ArgumentList[1]);
    }

    [Fact]
    public void BuildPagerProcessStartInfo_UsesShOnUnixLikePlatforms()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        ProcessStartInfo psi = PagerChain.BuildPagerProcessStartInfo("less -R");

        Assert.Equal("/bin/sh", psi.FileName);
        Assert.Equal(2, psi.ArgumentList.Count);
        Assert.Equal("-c", psi.ArgumentList[0]);
        Assert.Equal("less -R", psi.ArgumentList[1]);
    }

    [Theory]
    [InlineData("less")]
    [InlineData("less -R")]
    [InlineData("more")]
    [InlineData("/usr/bin/less -R --ignore-case")]
    [InlineData("less -R | tee log.txt")]
    public void BuildPagerProcessStartInfo_CommonPagerStrings_RoundTripIntoArgList(string pagerCommand)
    {
        // No transformation should be applied to the env-var value — the shell parses it.
        ProcessStartInfo psi = PagerChain.BuildPagerProcessStartInfo(pagerCommand);

        Assert.Contains(pagerCommand, psi.ArgumentList);
    }

    [Fact]
    public void BuildPagerProcessStartInfo_DoesNotRedirectStdoutOrStderr()
    {
        // Pager output must reach the user's terminal directly. Redirecting stdout/stderr
        // would either lose pager output to a buffer or break interactive features (less's
        // status bar, search prompt, etc.).
        ProcessStartInfo psi = PagerChain.BuildPagerProcessStartInfo("less");

        Assert.False(psi.RedirectStandardOutput);
        Assert.False(psi.RedirectStandardError);
    }
}
