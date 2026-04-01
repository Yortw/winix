using Winix.Peep;
using Xunit;

namespace Winix.Peep.Tests;

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

    [Fact]
    public void ClearCache_SubsequentQueryReEvaluates()
    {
        // After clearing, the same path should be re-evaluated (not served from cache).
        // We can't directly observe the cache, but we can verify the method still works
        // after clear without throwing.
        string testPath = $"clear-cache-test-{Guid.NewGuid():N}.txt";

        bool firstResult = GitIgnoreChecker.IsIgnored(testPath);
        GitIgnoreChecker.ClearCache();
        bool secondResult = GitIgnoreChecker.IsIgnored(testPath);

        // Both calls should return the same result for the same non-existent file
        Assert.Equal(firstResult, secondResult);
    }
}
