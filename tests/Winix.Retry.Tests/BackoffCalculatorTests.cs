using Xunit;
using Winix.Retry;

namespace Winix.Retry.Tests;

public class BackoffCalculatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void Fixed_ReturnsSameDelay_RegardlessOfAttempt(int attempt)
    {
        var delay = BackoffCalculator.Calculate(
            TimeSpan.FromSeconds(2), attempt, BackoffStrategy.Fixed, jitter: false, random: null);

        Assert.Equal(TimeSpan.FromSeconds(2), delay);
    }

    [Theory]
    [InlineData(1, 2.0)]
    [InlineData(2, 4.0)]
    [InlineData(3, 6.0)]
    public void Linear_ReturnsDelayTimesAttempt(int attempt, double expectedSeconds)
    {
        var delay = BackoffCalculator.Calculate(
            TimeSpan.FromSeconds(2), attempt, BackoffStrategy.Linear, jitter: false, random: null);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Theory]
    [InlineData(1, 2.0)]   // 2 * 2^0 = 2
    [InlineData(2, 4.0)]   // 2 * 2^1 = 4
    [InlineData(3, 8.0)]   // 2 * 2^2 = 8
    [InlineData(4, 16.0)]  // 2 * 2^3 = 16
    public void Exponential_ReturnsDelayTimesPowerOfTwo(int attempt, double expectedSeconds)
    {
        var delay = BackoffCalculator.Calculate(
            TimeSpan.FromSeconds(2), attempt, BackoffStrategy.Exponential, jitter: false, random: null);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Fact]
    public void Jitter_ReducesDelay_WithinExpectedRange()
    {
        var random = new Random(42);
        var delays = new List<TimeSpan>();

        for (int i = 0; i < 100; i++)
        {
            delays.Add(BackoffCalculator.Calculate(
                TimeSpan.FromSeconds(10), 1, BackoffStrategy.Fixed, jitter: true, random: random));
        }

        // All delays should be in [5.0, 10.0) — [0.5, 1.0) * 10s
        foreach (var delay in delays)
        {
            Assert.InRange(delay.TotalSeconds, 5.0, 10.0);
            Assert.True(delay.TotalSeconds < 10.0, "Jitter factor is [0.5, 1.0) — must be strictly less than base");
        }
    }

    [Fact]
    public void Jitter_WithExponential_AppliesAfterExponentialCalculation()
    {
        var random = new Random(42);

        var delay = BackoffCalculator.Calculate(
            TimeSpan.FromSeconds(2), 3, BackoffStrategy.Exponential, jitter: true, random: random);

        // Base exponential for attempt 3: 2 * 2^2 = 8s. With jitter: [4.0, 8.0)
        Assert.InRange(delay.TotalSeconds, 4.0, 8.0);
    }

    [Fact]
    public void ZeroDelay_ReturnsZero_RegardlessOfStrategy()
    {
        Assert.Equal(TimeSpan.Zero, BackoffCalculator.Calculate(
            TimeSpan.Zero, 1, BackoffStrategy.Fixed, jitter: false, random: null));
        Assert.Equal(TimeSpan.Zero, BackoffCalculator.Calculate(
            TimeSpan.Zero, 3, BackoffStrategy.Exponential, jitter: false, random: null));
    }

    [Fact]
    public void SubSecondDelay_WorksCorrectly()
    {
        var delay = BackoffCalculator.Calculate(
            TimeSpan.FromMilliseconds(200), 3, BackoffStrategy.Exponential, jitter: false, random: null);

        // 200ms * 2^2 = 800ms
        Assert.Equal(TimeSpan.FromMilliseconds(800), delay);
    }

    // --- Round-1 review additions: overflow clamp, enum exhaustiveness, jitter lower-bound ---

    [Fact]
    public void Exponential_LargeDelayLargeAttempt_ClampsToMaxDelay()
    {
        // C3 regression: `retry --backoff exp --delay 1d --times 50` computes 2^49 * 86_400_000 ms,
        // which overflows TimeSpan.FromMilliseconds (max ~9.22e15 ms). Prior to the round-1 fix
        // this crashed with uncaught OverflowException; now it's clamped to 1 hour.
        var delay = BackoffCalculator.Calculate(
            TimeSpan.FromDays(1), attempt: 50, BackoffStrategy.Exponential, jitter: false, random: null);

        Assert.Equal(TimeSpan.FromHours(1), delay);
    }

    [Fact]
    public void Exponential_ReasonableInputThatStillOverflows_ClampsToMaxDelay()
    {
        // Even sane-looking inputs hit the wall. `--delay 1m --backoff exp --times 35` gives
        // 60_000 * 2^34 ≈ 1.03e15 ms — not quite overflow but well past the MaxDelay ceiling.
        var delay = BackoffCalculator.Calculate(
            TimeSpan.FromMinutes(1), attempt: 35, BackoffStrategy.Exponential, jitter: false, random: null);

        Assert.Equal(TimeSpan.FromHours(1), delay);
    }

    [Fact]
    public void Linear_LargeAttempt_DoesNotOverflow()
    {
        // Linear scales by attempt number, not 2^attempt — much less overflow-prone, but the
        // clamp should still cover any combination that exceeds MaxDelay.
        var delay = BackoffCalculator.Calculate(
            TimeSpan.FromHours(1), attempt: 100, BackoffStrategy.Linear, jitter: false, random: null);

        // 1h * 100 = 100 hours, well past the 1-hour cap.
        Assert.Equal(TimeSpan.FromHours(1), delay);
    }

    [Fact]
    public void UnknownStrategy_ThrowsArgumentOutOfRange()
    {
        // Prior round-1 code had `_ => 1.0` which silently degraded unknown strategies to Fixed.
        // Now throws so a future enum addition that forgets to extend the switch fails loudly
        // in testing rather than silently mis-calculating in production.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BackoffCalculator.Calculate(
                TimeSpan.FromSeconds(1), attempt: 1, (BackoffStrategy)999, jitter: false, random: null));
    }

    [Fact]
    public void Jitter_LowerBoundIsActuallyReachable()
    {
        // Prior test asserted `InRange(5.0, 10.0)` but would pass even if the jitter range was
        // accidentally narrowed to [7.0, 10.0). Verify the documented [0.5, 1.0) lower bound is
        // actually reached in practice. With 500 samples and uniform distribution we expect the
        // minimum to land close to 5.0 — pick 6.0 as a threshold (loose enough to avoid flakes,
        // tight enough to catch a regression that narrowed the range).
        var random = new Random(42);
        var delays = new List<TimeSpan>();

        for (int i = 0; i < 500; i++)
        {
            delays.Add(BackoffCalculator.Calculate(
                TimeSpan.FromSeconds(10), 1, BackoffStrategy.Fixed, jitter: true, random: random));
        }

        double min = delays.Min(d => d.TotalSeconds);
        double max = delays.Max(d => d.TotalSeconds);
        Assert.True(min < 6.0, $"expected jitter lower bound near 5.0; min was {min:F3}");
        Assert.True(max > 9.0, $"expected jitter upper bound near 10.0; max was {max:F3}");
        // All in range — the existing guarantee.
        Assert.All(delays, d => Assert.InRange(d.TotalSeconds, 5.0, 10.0));
    }

    [Fact]
    public void ZeroBaseDelay_LargeAttempt_DoesNotClampToMaxDelay()
    {
        // Round-4 M1: with baseDelay=Zero and a large attempt count, `0 * Math.Pow(2, N-1)` for
        // large N evaluates to `0 * Infinity = NaN` because Math.Pow overflows double. The
        // existing NaN clamp returned MaxDelay (1h) — a silent contract flip where "zero delay"
        // becomes "1-hour delay" at large attempt numbers. Fix short-circuits at the top of
        // Calculate when baseDelay is Zero.
        var delay = BackoffCalculator.Calculate(
            TimeSpan.Zero, attempt: 2000, BackoffStrategy.Exponential, jitter: false, random: null);

        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void Jitter_ZeroBaseDelay_ReturnsZero_NoThrow()
    {
        // Jitter's `totalMs > 0` guard: when baseDelay is zero, the jitter branch is skipped
        // entirely. Without the guard, `0 * factor` is still 0 but the shape of the code matters
        // if a future refactor introduces floating-point noise.
        var random = new Random(42);
        var delay = BackoffCalculator.Calculate(
            TimeSpan.Zero, 3, BackoffStrategy.Exponential, jitter: true, random: random);

        Assert.Equal(TimeSpan.Zero, delay);
    }
}
