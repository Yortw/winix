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

    [SkippableFact]
    public void BuildPagerProcessStartInfo_UsesCmdShellOnWindows()
    {
        // Round-1 fresh-eyes 2026-05-09 pr-test-analyzer Q1: pre-fix this test used early-return
        // on non-Windows, which xunit reports as Passed — a CI false positive. Switch to
        // SkippableFact + Skip.IfNot for proper Skipped status. Keep the redundant guard for
        // CA1416 (deliberate per CLAUDE.md convention).
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only path");
        if (!OperatingSystem.IsWindows()) return;  // CA1416 silencer — reachable only as guard

        ProcessStartInfo psi = PagerChain.BuildPagerProcessStartInfo("less -R");

        Assert.Equal("cmd.exe", psi.FileName);
        Assert.Equal(2, psi.ArgumentList.Count);
        Assert.Equal("/c", psi.ArgumentList[0]);
        Assert.Equal("less -R", psi.ArgumentList[1]);
    }

    [SkippableFact]
    public void BuildPagerProcessStartInfo_UsesShOnUnixLikePlatforms()
    {
        Skip.If(OperatingSystem.IsWindows(), "Unix-only path");
        if (OperatingSystem.IsWindows()) return;  // CA1416 silencer — reachable only as guard

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

    // Round-1 fresh-eyes 2026-05-09 SFH I1 (with reproducer at tmp/pagerprobe/): when
    // MANPAGER points to a binary the shell can't find (typo, removed binary, missing
    // PATH entry), the SHELL itself starts successfully and returns command-not-found
    // exit code (9009 on cmd.exe, 127 on sh). Process.Start does NOT throw — pre-fix the
    // catch block missed this entirely, so TryRunExternalPager returned true, the user
    // saw the shell's "not recognized" diagnostic + an empty page + man exit 0, with
    // the built-in pager fallback skipped.
    //
    // Fix: check process.ExitCode after WaitForExit. Treat any non-zero exit other than
    // 130 (SIGINT user-quit) as launch failure → return false → caller falls back.

    [SkippableFact]
    public void TryRunExternalPager_NonZeroExitCode_ReturnsFalseSoCallerFallsBack()
    {
        // Use the canonical "exit non-zero" command for each shell. The pager-dispatch shell
        // wrapping (cmd /c on Windows, /bin/sh -c elsewhere) is exercised by BuildPager
        // ProcessStartInfo and tested above — here we test only the exit-code-check delta.
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only — uses cmd.exe exit syntax");
        if (!OperatingSystem.IsWindows()) return;

        bool ran = PagerChain.TryRunExternalPager("exit 7", "irrelevant content");

        // Pre-fix this returned true (success was inferred from Process.Start not throwing).
        // Post-fix the non-zero exit is detected and the caller falls back to built-in.
        Assert.False(ran);
    }

    [SkippableFact]
    public void TryRunExternalPager_NonExistentBinary_ReturnsFalseSoCallerFallsBack()
    {
        // The original SFH I1 reproducer scenario: MANPAGER='this_binary_does_not_exist'.
        // cmd /c starts cleanly, prints "not recognized" to its own stderr, exits 9009.
        // Pre-fix this returned true; post-fix it returns false.
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only — cmd.exe error code semantics");
        if (!OperatingSystem.IsWindows()) return;

        bool ran = PagerChain.TryRunExternalPager(
            "this_binary_does_not_exist_xyz_123_winix_test",
            "irrelevant content");

        Assert.False(ran);
    }
}
