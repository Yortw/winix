#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Online;

/// <summary>
/// Polls a set of <see cref="IReadinessCheck"/>s until all pass, the deadline is reached, or (in
/// <c>--once</c> mode) a single cycle completes. The clock and sleep are injected so the loop is
/// unit-tested with no real waiting (precedent: <c>RetryRunner</c>'s delay seam).
/// </summary>
public sealed class WaitEngine
{
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _sleep;

    /// <summary>Creates the engine with injected clock and sleep seams.</summary>
    public WaitEngine(Func<DateTimeOffset> now, Func<TimeSpan, CancellationToken, Task> sleep)
    {
        _now = now;
        _sleep = sleep;
    }

    /// <summary>
    /// Runs the poll loop. The gate opens only when EVERY check passes in the same cycle.
    /// </summary>
    /// <param name="checks">Checks to AND-combine.</param>
    /// <param name="options">Timing and mode options (<c>--timeout</c> 0 ⇒ no deadline).</param>
    /// <param name="onAttempt">Optional per-cycle callback (cycle number + that cycle's results) for verbose output.</param>
    /// <param name="cancellationToken">User cancel (Ctrl+C).</param>
    public async Task<WaitResult> RunAsync(
        IReadOnlyList<IReadinessCheck> checks,
        OnlineOptions options,
        Action<int, IReadOnlyList<CheckResult>>? onAttempt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset start = _now();
        DateTimeOffset? deadline = options.Timeout == TimeSpan.Zero ? null : start + options.Timeout;
        int attempts = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;

            var results = new List<CheckResult>(checks.Count);
            bool allOk = true;
            foreach (IReadinessCheck check in checks)
            {
                CheckResult result = await check.RunAsync(cancellationToken);
                results.Add(result);
                if (!result.Ok)
                {
                    allOk = false;
                }
            }

            onAttempt?.Invoke(attempts, results);

            if (allOk)
            {
                return new WaitResult(true, false, attempts, _now() - start, results);
            }
            if (options.Once)
            {
                // A single-probe miss is a normal negative, NOT a timeout — distinct exit code.
                return new WaitResult(false, false, attempts, _now() - start, results);
            }
            if (deadline.HasValue && _now() >= deadline.Value)
            {
                return new WaitResult(false, true, attempts, _now() - start, results);
            }

            await _sleep(options.Interval, cancellationToken);
        }
    }
}
