#nullable enable

using Xunit;
using Winix.Clip;

namespace Winix.Clip.Tests;

public class ClipboardRetryPolicyTests
{
    [Fact]
    public void TryOpenWithRetry_FirstAttemptSucceeds_ReturnsTrueWithoutSleeping()
    {
        int sleepCount = 0;
        int openCount = 0;

        bool result = ClipboardRetryPolicy.TryOpenWithRetry(
            tryOpen: () => { openCount++; return true; },
            sleep: _ => sleepCount++,
            out int attempts);

        Assert.True(result);
        Assert.Equal(1, openCount);
        Assert.Equal(0, sleepCount);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void TryOpenWithRetry_AllAttemptsFail_ReturnsFalseAfterFullBudget()
    {
        int sleepCount = 0;
        int openCount = 0;

        bool result = ClipboardRetryPolicy.TryOpenWithRetry(
            tryOpen: () => { openCount++; return false; },
            sleep: _ => sleepCount++,
            out int attempts);

        Assert.False(result);
        // Pinned budget contract — a future PR shrinking OpenAttempts would break this
        // test, signalling the documented "20 attempts at 50ms = 1s budget" contract is
        // changing intentionally rather than by accident.
        Assert.Equal(20, ClipboardRetryPolicy.OpenAttempts);
        Assert.Equal(20, openCount);
        Assert.Equal(20, sleepCount);
        Assert.Equal(20, attempts);
    }

    [Fact]
    public void TryOpenWithRetry_SucceedsOnLastAttempt_ReturnsTrue()
    {
        int openCount = 0;

        bool result = ClipboardRetryPolicy.TryOpenWithRetry(
            tryOpen: () =>
            {
                openCount++;
                return openCount == ClipboardRetryPolicy.OpenAttempts;
            },
            sleep: _ => { },
            out int attempts);

        Assert.True(result);
        Assert.Equal(ClipboardRetryPolicy.OpenAttempts, attempts);
    }

    [Fact]
    public void TryOpenWithRetry_SleepBudgetMatchesDelayConstant()
    {
        int? observedDelay = null;

        ClipboardRetryPolicy.TryOpenWithRetry(
            tryOpen: () => false,
            sleep: ms => observedDelay = ms,
            out _);

        Assert.Equal(ClipboardRetryPolicy.OpenRetryDelayMs, observedDelay);
        Assert.Equal(50, ClipboardRetryPolicy.OpenRetryDelayMs);
    }
}
