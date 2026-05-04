#nullable enable
using System;
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
        // Prove parallelism via interval overlap, not wall-clock elapsed time. Sequential
        // dispatch would force b2 to start only after b1 finishes; we assert the opposite
        // (b2 started before b1 ended). This is immune to machine-load jitter, which made
        // the previous Stopwatch-range assertion flaky under suite contention.
        var b1 = new FakeBackend("a") { DelayMs = 200 };
        var b2 = new FakeBackend("b") { DelayMs = 200 };
        await Dispatcher.SendAsync(new IBackend[] { b1, b2 }, Msg(), CancellationToken.None);

        Assert.NotNull(b1.StartedAt);
        Assert.NotNull(b1.EndedAt);
        Assert.NotNull(b2.StartedAt);
        Assert.True(
            b2.StartedAt < b1.EndedAt,
            $"Expected overlap: b2 started at {b2.StartedAt:O} but b1 ended at {b1.EndedAt:O} — backends ran sequentially.");
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

    // -- Round-1 review SFH-I3 — defense-in-depth: a backend that violates the never-throw
    //    contract must NOT corrupt the batch. Pre-fix, Task.WhenAll would fault and discard
    //    sibling backends' successful BackendResults — so a typo'd ntfy server URL would
    //    silently mask a successful desktop notification with a process-crash error. --
    [Fact]
    public async Task Dispatch_OneBackendThrows_OtherBackendResultPreserved()
    {
        var ok = new FakeBackend("desktop") { ShouldSucceed = true };
        var bad = new FakeBackend("ntfy") { ShouldThrow = new System.UriFormatException("Invalid URI: bogus") };
        var results = await Dispatcher.SendAsync(new IBackend[] { ok, bad }, Msg(), CancellationToken.None);
        Assert.Equal(2, results.Count);
        Assert.True(results[0].Ok);                 // desktop success preserved
        Assert.Equal("desktop", results[0].BackendName);
        Assert.False(results[1].Ok);                // ntfy converted to typed result
        Assert.Equal("ntfy", results[1].BackendName);
        Assert.NotNull(results[1].Error);
        Assert.Contains("UriFormatException", results[1].Error, StringComparison.Ordinal);
        Assert.Contains("never-throw contract", results[1].Error, StringComparison.Ordinal);
    }

    // -- Round-2 review R2-I1 — when the dispatcher's cancellation token fires (its own
    //    timeout, not user Ctrl-C), an in-flight backend's OperationCanceledException must
    //    be CONVERTED to a per-backend failure result, NOT propagated as an OCE that
    //    Task.WhenAll faults on. The fault would discard sibling backends' already-completed
    //    successes — meaning a 15s timeout that fires while ntfy is mid-flight would also
    //    wipe the successful desktop notification result the user already received. --
    [Fact]
    public async Task Dispatch_TimeoutCancellation_PreservesSiblingSuccess()
    {
        using var cts = new CancellationTokenSource();
        var fast = new FakeBackend("desktop") { ShouldSucceed = true };
        // Slow backend will throw OCE because its delay exceeds the cancel-after below.
        var slow = new FakeBackend("ntfy") { DelayMs = 5000, ShouldSucceed = true };
        cts.CancelAfter(50);

        var results = await Dispatcher.SendAsync(new IBackend[] { fast, slow }, Msg(), cts.Token);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Ok);                  // desktop preserved
        Assert.Equal("desktop", results[0].BackendName);
        Assert.False(results[1].Ok);                 // ntfy converted to typed result
        Assert.Equal("ntfy", results[1].BackendName);
        Assert.NotNull(results[1].Error);
        Assert.Contains("cancelled", results[1].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dispatch_BackendThrowsOperationCanceledOnRequestedCancellation_ConvertsToFailureResult()
    {
        // Round-2 R2-I1 update: was previously testing OCE propagation; now we pin that
        // OCE is CONVERTED to a per-backend failure regardless. The original "cooperative
        // cancellation propagates" shape was speculative (no caller passes an external ct)
        // and caused partial-success loss on dispatcher-internal timeouts.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var b = new FakeBackend("ntfy") { ShouldThrow = new System.OperationCanceledException(cts.Token) };
        var results = await Dispatcher.SendAsync(new IBackend[] { b }, Msg(), cts.Token);
        Assert.Single(results);
        Assert.False(results[0].Ok);
        Assert.NotNull(results[0].Error);
        Assert.Contains("cancelled", results[0].Error, StringComparison.OrdinalIgnoreCase);
    }
}
