using Winix.Peep;
using Xunit;

namespace Winix.Peep.Tests;

// R4 TA C1: GitIgnoreChecker holds process-global mutable state
// (_gitDisabled, _gitDisabledWarned, FailureWriter). Without this collection
// attribute xUnit runs GitIgnoreCheckerTests and GitIgnoreCheckerDisableTests
// in parallel; the Disable tests' transient mutation of _gitDisabled corrupts
// the cache-clear test's reads, producing an order-dependent flake.
// CI flake #2 (2026-06-07, macos, 58ms/no-fallback): the checker ALSO depends on
// process CWD — `git check-ignore` inherits it, and a parallel FileWatcherTests/
// IntegrationTests SetCurrentDirectory(tempDir) between two IsIgnored calls makes
// git exit 128 (not a repo) → false, with no fallback engaged, so the Skip can't
// fire. CWD mutators and CWD readers must share ONE collection — hence the merged
// "PeepProcessGlobals" name across all four classes.
[Collection("PeepProcessGlobals")]
public class GitIgnoreCheckerTests
{
    [Fact]
    public void ClearCache_DoesNotThrow()
    {
        // Verify ClearCache doesn't throw when cache has entries.
        // Prime the cache with a query, then clear.
        _ = GitIgnoreChecker.IsIgnored("some-test-path.txt");
        GitIgnoreChecker.ClearCache();
    }

    [Fact]
    public void ClearCache_CalledMultipleTimes_DoesNotThrow()
    {
        // Verify double-clear is safe (empty dictionary clear is a no-op)
        GitIgnoreChecker.ClearCache();
        GitIgnoreChecker.ClearCache();
    }

    [SkippableFact]
    public void ClearCache_SubsequentQueryReEvaluates()
    {
        // After clearing, the same path should be re-evaluated (not served from cache).
        // We can't directly observe the cache, but we can verify the method still works
        // after clear without throwing.
        GitIgnoreChecker.ResetForTests();
        string testPath = $"clear-cache-test-{Guid.NewGuid():N}.txt";

        bool firstResult = GitIgnoreChecker.IsIgnored(testPath);
        GitIgnoreChecker.ClearCache();
        bool secondResult = GitIgnoreChecker.IsIgnored(testPath);

        // CI flake 2026-06-06 (windows-latest, 3s vs ~215ms): transient git-spawn slowness
        // engaged the process-global _gitDisabled fallback BETWEEN the two calls, making
        // them legitimately disagree (first evaluated via git; second returned the
        // disabled-fallback false). When the fallback engaged mid-test the consistency
        // assertion is INCONCLUSIVE, not failed — skip honestly. Healthy-git runs (the
        // overwhelming majority) still assert equality, so a real cache-consistency
        // regression is still caught.
        Skip.If(GitIgnoreChecker.IsDisabledForTests,
            "git transiently unavailable — _gitDisabled fallback engaged mid-test; consistency check inconclusive");

        // Both calls should return the same result for the same non-existent file
        Assert.Equal(firstResult, secondResult);
    }
}
