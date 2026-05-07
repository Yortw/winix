#nullable enable

using Winix.Less;
using Xunit;

namespace Winix.Less.Tests;

/// <summary>
/// Unit tests for <see cref="Pager.SelectDumpStrategy"/> — the pure-function decision about
/// whether to dump content direct to stdout vs run the interactive pager loop. Extracted to a
/// helper specifically so the F1 non-tty branch is unit-testable without spawning a process.
/// </summary>
public sealed class PagerStrategyTests
{
    // Tier-2 baseline 2026-05-07 finding F1: pre-fix, any path that entered the interactive
    // pager loop with redirected stdout crashed with "The handle is invalid" because Screen
    // calls Console.SetCursorPosition on a non-tty handle. SelectDumpStrategy is the pure
    // decision point that prevents the crash by detecting redirected stdout up front and
    // dumping content direct, matching real GNU less's behaviour for the same case.

    private static LessOptions OptsWithF(bool quitIfOneScreen) =>
        LessOptions.Resolve([], lessEnvVar: quitIfOneScreen ? "F" : "");

    [Fact]
    public void Strategy_TtyStdout_ContentFitsAndF_DumpAndExit()
    {
        // Quit-if-one-screen path: F is on, content fits → dump.
        bool dump = Pager.SelectDumpStrategy(OptsWithF(true), isOutputRedirected: false, displayRows: 5, viewHeight: 23);

        Assert.True(dump);
    }

    [Fact]
    public void Strategy_TtyStdout_ContentFitsButNoF_RunPager()
    {
        // Without -F, even fitting content runs the pager so the user can interact (search, etc.).
        bool dump = Pager.SelectDumpStrategy(OptsWithF(false), isOutputRedirected: false, displayRows: 5, viewHeight: 23);

        Assert.False(dump);
    }

    [Fact]
    public void Strategy_TtyStdout_ContentTooLargeAndF_RunPager()
    {
        // -F is on but content doesn't fit → user wants to scroll → run pager.
        bool dump = Pager.SelectDumpStrategy(OptsWithF(true), isOutputRedirected: false, displayRows: 100, viewHeight: 23);

        Assert.False(dump);
    }

    [Fact]
    public void Strategy_RedirectedStdout_ContentTooLarge_DumpAndExit()
    {
        // F1 case: stdout is a pipe/file, content larger than screen. Pre-fix this entered the
        // pager loop and crashed. Post-fix dumps direct.
        bool dump = Pager.SelectDumpStrategy(OptsWithF(false), isOutputRedirected: true, displayRows: 100, viewHeight: 23);

        Assert.True(dump);
    }

    [Fact]
    public void Strategy_RedirectedStdout_NoF_DumpAndExit()
    {
        // F1 case: LESS=NiR (no F) with redirected stdout — pre-fix crashed with handle-invalid,
        // post-fix dumps direct.
        bool dump = Pager.SelectDumpStrategy(OptsWithF(false), isOutputRedirected: true, displayRows: 100, viewHeight: 23);

        Assert.True(dump);
    }

    [Fact]
    public void Strategy_RedirectedStdout_TinyContent_DumpAndExit()
    {
        // Trivial sanity — redirected stdout with content that fits under -F also dumps.
        bool dump = Pager.SelectDumpStrategy(OptsWithF(true), isOutputRedirected: true, displayRows: 1, viewHeight: 23);

        Assert.True(dump);
    }
}
