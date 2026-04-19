#nullable enable
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Winix.Notify;
using Winix.Notify.Tests.Fakes;

namespace Winix.Notify.Tests;

public class DispatcherTests
{
    private static NotifyMessage Msg() => new("title", "body", Urgency.Normal, null);

    [Fact]
    public async Task Dispatch_OneBackend_Success_ReturnsOneOkResult()
    {
        var b = new FakeBackend("desktop");
        var results = await Dispatcher.SendAsync(new IBackend[] { b }, Msg(), CancellationToken.None);
        Assert.Single(results);
        Assert.True(results[0].Ok);
        Assert.Equal("desktop", results[0].BackendName);
        Assert.Single(b.Received);
    }

    [Fact]
    public async Task Dispatch_TwoBackends_BothSucceed_BothInResults()
    {
        var b1 = new FakeBackend("desktop");
        var b2 = new FakeBackend("ntfy");
        var results = await Dispatcher.SendAsync(new IBackend[] { b1, b2 }, Msg(), CancellationToken.None);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Ok));
    }

    [Fact]
    public async Task Dispatch_OneFails_BothInResults_FailureCarriesError()
    {
        var b1 = new FakeBackend("desktop") { ShouldSucceed = true };
        var b2 = new FakeBackend("ntfy") { ShouldSucceed = false, FailureMessage = "topic not found" };
        var results = await Dispatcher.SendAsync(new IBackend[] { b1, b2 }, Msg(), CancellationToken.None);
        Assert.Equal(2, results.Count);
        Assert.True(results[0].Ok);
        Assert.False(results[1].Ok);
        Assert.Equal("topic not found", results[1].Error);
    }

    [Fact]
    public async Task Dispatch_RunsBackendsInParallel_NotSequentially()
    {
        var b1 = new FakeBackend("a") { DelayMs = 100 };
        var b2 = new FakeBackend("b") { DelayMs = 100 };
        var sw = Stopwatch.StartNew();
        await Dispatcher.SendAsync(new IBackend[] { b1, b2 }, Msg(), CancellationToken.None);
        sw.Stop();
        // Sequential would be ~200ms; parallel ~100ms. Generous margin for CI noise.
        Assert.InRange(sw.ElapsedMilliseconds, 80, 180);
    }

    [Fact]
    public async Task Dispatch_NoBackends_ReturnsEmptyResults()
    {
        var results = await Dispatcher.SendAsync(System.Array.Empty<IBackend>(), Msg(), CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task Dispatch_PreservesBackendOrderInResults()
    {
        var b1 = new FakeBackend("first");
        var b2 = new FakeBackend("second");
        var b3 = new FakeBackend("third");
        var results = await Dispatcher.SendAsync(new IBackend[] { b1, b2, b3 }, Msg(), CancellationToken.None);
        Assert.Equal("first", results[0].BackendName);
        Assert.Equal("second", results[1].BackendName);
        Assert.Equal("third", results[2].BackendName);
    }
}
