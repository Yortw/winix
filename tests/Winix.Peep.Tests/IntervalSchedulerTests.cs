using Xunit;
using Winix.Peep;

namespace Winix.Peep.Tests;

public class IntervalSchedulerTests
{
    [Fact]
    public async Task WaitForNextTickAsync_FiresAfterInterval()
    {
        using var scheduler = new IntervalScheduler(TimeSpan.FromMilliseconds(50));

        bool ticked = await scheduler.WaitForNextTickAsync();

        Assert.True(ticked);
    }

    [Fact]
    public async Task WaitForNextTickAsync_CancellationReturnsFalse()
    {
        using var scheduler = new IntervalScheduler(TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        bool ticked = await scheduler.WaitForNextTickAsync(cts.Token);

        Assert.False(ticked);
    }

    [Fact]
    public async Task Reset_RestartsInterval()
    {
        using var scheduler = new IntervalScheduler(TimeSpan.FromMilliseconds(200));

        // Wait 150ms (most of the interval), then reset
        await Task.Delay(150);
        scheduler.Reset();

        // If reset works, the next tick should be ~200ms from now, not ~50ms.
        // We verify by checking that it takes at least 100ms from reset to tick.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool ticked = await scheduler.WaitForNextTickAsync();
        sw.Stop();

        Assert.True(ticked);
        Assert.True(sw.ElapsedMilliseconds >= 100,
            $"Expected at least 100ms after reset, but got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Dispose_CausesWaitToReturnFalse()
    {
        var scheduler = new IntervalScheduler(TimeSpan.FromSeconds(30));

        // Dispose on a background thread after a short delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            scheduler.Dispose();
        });

        bool ticked = await scheduler.WaitForNextTickAsync();

        // PeriodicTimer.WaitForNextTickAsync returns false when disposed
        Assert.False(ticked);
    }

    [Fact]
    public void Interval_ReturnsConfiguredValue()
    {
        var interval = TimeSpan.FromSeconds(5);
        using var scheduler = new IntervalScheduler(interval);

        Assert.Equal(interval, scheduler.Interval);
    }
}
