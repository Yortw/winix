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
}
