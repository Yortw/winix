#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class WaitEngineTests
{
    // A check that returns a fixed Ok value, optionally flipping to Ok after a given cycle count.
    private sealed class ScriptedCheck : IReadinessCheck
    {
        private readonly Queue<bool> _results;
        public ScriptedCheck(params bool[] results) => _results = new Queue<bool>(results);
        public Task<CheckResult> RunAsync(CancellationToken ct)
        {
            bool ok = _results.Count > 1 ? _results.Dequeue() : _results.Peek();
            return Task.FromResult(new CheckResult("test", null, ok, ok ? "ok" : "down"));
        }
    }

    private static (WaitEngine engine, Func<int> sleepCount) BuildEngine()
    {
        int sleeps = 0;
        var clock = new FakeClock();
        var engine = new WaitEngine(
            now: () => clock.Now,
            sleep: (d, _) => { sleeps++; clock.Advance(d); return Task.CompletedTask; });
        return (engine, () => sleeps);
    }

    private sealed class FakeClock
    {
        public DateTimeOffset Now { get; private set; } = DateTimeOffset.UnixEpoch;
        public void Advance(TimeSpan d) => Now += d;
    }

    private static OnlineOptions Opts(TimeSpan? timeout = null, bool once = false)
        => new(
            checkInternet: false,
            urls: Array.Empty<string>(),
            status: StatusSpec.Default,
            endpoints: Array.Empty<string>(),
            timeout: timeout ?? TimeSpan.FromMinutes(10),
            interval: TimeSpan.FromSeconds(2),
            probeTimeout: TimeSpan.FromSeconds(3),
            once: once,
            verbose: false);

    [Fact]
    public async Task Ready_first_cycle_returns_ready_with_no_sleep()
    {
        (WaitEngine engine, Func<int> sleeps) = BuildEngine();
        WaitResult r = await engine.RunAsync(new IReadinessCheck[] { new ScriptedCheck(true) }, Opts(), null, CancellationToken.None);
        Assert.True(r.Ready);
        Assert.False(r.TimedOut);
        Assert.Equal(1, r.Attempts);
        Assert.Equal(0, sleeps());
    }

    [Fact]
    public async Task Ready_after_three_cycles_sleeps_twice()
    {
        (WaitEngine engine, Func<int> sleeps) = BuildEngine();
        var check = new ScriptedCheck(false, false, true);  // dequeues false, false, then peeks true
        WaitResult r = await engine.RunAsync(new IReadinessCheck[] { check }, Opts(), null, CancellationToken.None);
        Assert.True(r.Ready);
        Assert.Equal(3, r.Attempts);
        Assert.Equal(2, sleeps());  // N-1 sleeps for N cycles
    }

    [Fact]
    public async Task Deadline_exceeded_returns_timed_out_124_shape()
    {
        (WaitEngine engine, _) = BuildEngine();
        // 5s budget, 2s interval, never ready → cycles at t=0,2,4 then t=6 deadline passed.
        WaitResult r = await engine.RunAsync(
            new IReadinessCheck[] { new ScriptedCheck(false) },
            Opts(timeout: TimeSpan.FromSeconds(5)), null, CancellationToken.None);
        Assert.False(r.Ready);
        Assert.True(r.TimedOut);
    }

    [Fact]
    public async Task Once_ready_returns_ready()
    {
        (WaitEngine engine, Func<int> sleeps) = BuildEngine();
        WaitResult r = await engine.RunAsync(new IReadinessCheck[] { new ScriptedCheck(true) }, Opts(once: true), null, CancellationToken.None);
        Assert.True(r.Ready);
        Assert.Equal(0, sleeps());
    }

    [Fact]
    public async Task Once_not_ready_returns_not_ready_not_timed_out()
    {
        (WaitEngine engine, Func<int> sleeps) = BuildEngine();
        WaitResult r = await engine.RunAsync(new IReadinessCheck[] { new ScriptedCheck(false) }, Opts(once: true), null, CancellationToken.None);
        Assert.False(r.Ready);
        Assert.False(r.TimedOut);   // a single-probe miss is NOT a timeout (exit 1, not 124)
        Assert.Equal(1, r.Attempts);
        Assert.Equal(0, sleeps());
    }

    [Fact]
    public async Task And_invariant_one_failing_check_keeps_gate_closed()
    {
        (WaitEngine engine, _) = BuildEngine();
        var checks = new IReadinessCheck[] { new ScriptedCheck(true), new ScriptedCheck(false) };
        WaitResult r = await engine.RunAsync(checks, Opts(once: true), null, CancellationToken.None);
        Assert.False(r.Ready);   // requirement: ALL must pass; one passing is not enough
    }

    // F5 — cancellation: a pre-cancelled token must throw, not return a misleading not-ready result.
    [Fact]
    public async Task Cancelled_token_throws_rather_than_returning_a_result()
    {
        (WaitEngine engine, _) = BuildEngine();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.RunAsync(new IReadinessCheck[] { new ScriptedCheck(false) }, Opts(), null, cts.Token));
    }

    // F10 — infinite budget (--timeout 0) under --once: one cycle, no hang, never times out.
    [Fact]
    public async Task Timeout_zero_with_once_returns_after_one_cycle()
    {
        (WaitEngine engine, Func<int> sleeps) = BuildEngine();
        WaitResult r = await engine.RunAsync(
            new IReadinessCheck[] { new ScriptedCheck(false) },
            Opts(timeout: TimeSpan.Zero, once: true), null, CancellationToken.None);
        Assert.False(r.Ready);
        Assert.False(r.TimedOut);   // infinite budget can never produce a timeout
        Assert.Equal(1, r.Attempts);
        Assert.Equal(0, sleeps());
    }

    // F10 — infinite budget (not once): never times out; becomes ready when the check flips.
    [Fact]
    public async Task Timeout_zero_never_times_out_and_eventually_ready()
    {
        (WaitEngine engine, _) = BuildEngine();
        var check = new ScriptedCheck(false, false, true);
        WaitResult r = await engine.RunAsync(
            new IReadinessCheck[] { check }, Opts(timeout: TimeSpan.Zero), null, CancellationToken.None);
        Assert.True(r.Ready);
        Assert.False(r.TimedOut);
    }

    // FIX: a backward wall-clock step must not produce a negative Elapsed (FormatDuration throws on negative).
    [Fact]
    public async Task Backward_clock_step_clamps_elapsed_to_non_negative()
    {
        // now() returns a LATER time first (start), then an EARLIER time (backward step) for the elapsed read.
        var times = new Queue<DateTimeOffset>(new[]
        {
            DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(10),  // start
            DateTimeOffset.UnixEpoch,                             // elapsed read — 10s earlier
        });
        var engine = new WaitEngine(
            now: () => times.Count > 1 ? times.Dequeue() : times.Peek(),
            sleep: (_, _) => Task.CompletedTask);

        WaitResult r = await engine.RunAsync(
            new IReadinessCheck[] { new ScriptedCheck(true) }, Opts(), null, CancellationToken.None);

        Assert.True(r.Ready);
        Assert.True(r.Elapsed >= TimeSpan.Zero);   // clamped, not negative
    }

    [Fact]
    public async Task OnAttempt_fires_once_per_cycle_with_sequential_numbers()
    {
        (WaitEngine engine, _) = BuildEngine();
        var seen = new List<int>();
        var check = new ScriptedCheck(false, false, true);  // ready on cycle 3
        WaitResult r = await engine.RunAsync(
            new IReadinessCheck[] { check },
            Opts(),
            onAttempt: (attempt, results) =>
            {
                seen.Add(attempt);
                Assert.Single(results);          // the cycle's results list is passed through
            },
            CancellationToken.None);

        Assert.True(r.Ready);
        Assert.Equal(new[] { 1, 2, 3 }, seen);   // fired once per cycle, 1-based, in order
    }
}
