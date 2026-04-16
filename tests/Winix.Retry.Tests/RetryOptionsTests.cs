using Xunit;
using Winix.Retry;

namespace Winix.Retry.Tests;

public class RetryOptionsTests
{
    [Fact]
    public void Construct_WithDefaults_SetsExpectedValues()
    {
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1));

        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), options.Delay);
        Assert.Equal(BackoffStrategy.Fixed, options.Backoff);
        Assert.False(options.Jitter);
        Assert.Null(options.RetryOnCodes);
        Assert.Null(options.StopOnCodes);
    }

    [Fact]
    public void Construct_NegativeRetries_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetryOptions(maxRetries: -1, delay: TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Construct_NegativeDelay_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Construct_BothRetryOnAndStopOn_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new RetryOptions(
                maxRetries: 3,
                delay: TimeSpan.FromSeconds(1),
                retryOnCodes: new HashSet<int> { 1 },
                stopOnCodes: new HashSet<int> { 0 }));
    }

    [Fact]
    public void Construct_ZeroRetries_IsValid()
    {
        var options = new RetryOptions(maxRetries: 0, delay: TimeSpan.FromSeconds(1));
        Assert.Equal(0, options.MaxRetries);
    }

    [Fact]
    public void Construct_ZeroDelay_IsValid()
    {
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, options.Delay);
    }

    [Fact]
    public void Construct_WithRetryOnCodes_SetsCorrectly()
    {
        var codes = new HashSet<int> { 1, 2, 3 };
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1), retryOnCodes: codes);

        Assert.NotNull(options.RetryOnCodes);
        Assert.Contains(1, options.RetryOnCodes);
        Assert.Contains(2, options.RetryOnCodes);
        Assert.Contains(3, options.RetryOnCodes);
        Assert.Null(options.StopOnCodes);
    }

    [Fact]
    public void Construct_WithStopOnCodes_SetsCorrectly()
    {
        var codes = new HashSet<int> { 0, 1 };
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1), stopOnCodes: codes);

        Assert.NotNull(options.StopOnCodes);
        Assert.Contains(0, options.StopOnCodes);
        Assert.Contains(1, options.StopOnCodes);
        Assert.Null(options.RetryOnCodes);
    }

    [Fact]
    public void ShouldRetry_Default_ReturnsTrueForNonZero()
    {
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1));

        Assert.True(options.ShouldRetry(1));
        Assert.True(options.ShouldRetry(2));
        Assert.True(options.ShouldRetry(137));
        Assert.False(options.ShouldRetry(0));
    }

    [Fact]
    public void ShouldRetry_WithRetryOnCodes_ReturnsTrueOnlyForListed()
    {
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1),
            retryOnCodes: new HashSet<int> { 1, 2 });

        Assert.True(options.ShouldRetry(1));
        Assert.True(options.ShouldRetry(2));
        Assert.False(options.ShouldRetry(3));
        Assert.False(options.ShouldRetry(0));
    }

    [Fact]
    public void ShouldRetry_WithStopOnCodes_ReturnsFalseForListed()
    {
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1),
            stopOnCodes: new HashSet<int> { 0, 1 });

        Assert.False(options.ShouldRetry(0));
        Assert.False(options.ShouldRetry(1));
        Assert.True(options.ShouldRetry(2));
        Assert.True(options.ShouldRetry(137));
    }

    [Fact]
    public void ShouldRetry_WithUntilWithout0_ReturnsTrueFor0()
    {
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1),
            stopOnCodes: new HashSet<int> { 1 });

        Assert.True(options.ShouldRetry(0));
        Assert.False(options.ShouldRetry(1));
    }
}
