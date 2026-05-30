#nullable enable
using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Hosting;

namespace Winix.HCat;

/// <summary>Coordinates the CI stop lifecycle for inspect/pipe modes: writes each captured request as a JSONL
/// line, consults the <see cref="CaptureController"/> for the stop condition, runs an optional <c>--timeout</c>
/// timer, and arbitrates a SINGLE outcome between "stop condition satisfied" and "timeout elapsed".</summary>
/// <remarks>
/// <para>F6 (in-flight response): the request that triggers the stop must finish writing its own response
/// before the host shuts down. <see cref="OnRecord"/> only sets <see cref="StopRequested"/>; the actual
/// <see cref="IHostApplicationLifetime.StopApplication"/> call is made by a middleware step that runs AFTER the
/// terminal handler returns (see <see cref="HCatServer.BuildApp"/>). <c>StopApplication()</c> lets in-flight
/// requests drain by default, so the triggering response is never truncated.</para>
/// <para>F9 (single-outcome latch): <see cref="_outcome"/> is set exactly once via
/// <see cref="Interlocked.CompareExchange(ref int, int, int)"/>. Whichever of {stop-satisfied, timeout-elapsed}
/// gets there first wins. Stop-satisfied → 0; timeout-without-satisfaction → 1.</para>
/// </remarks>
internal sealed class CaptureLifecycle : IDisposable
{
    // Outcome latch sentinel values. -1 = undecided; 0 = stop condition satisfied; 1 = timeout, unmet.
    private const int OutcomeUndecided = -1;
    private const int OutcomeSatisfied = 0;
    private const int OutcomeTimedOut = 1;

    private readonly CaptureController _controller;
    private readonly TextWriter? _jsonSink;
    private readonly object _sinkLock = new();
    private Timer? _timer;
    private int _outcome = OutcomeUndecided;
    private int _stopRequested;

    /// <summary>True once a request has satisfied the stop condition. The middleware tail polls this AFTER the
    /// terminal handler returns and then calls <see cref="IHostApplicationLifetime.StopApplication"/>.</summary>
    public bool StopRequested => Volatile.Read(ref _stopRequested) != 0;

    /// <summary>Creates the lifecycle.</summary>
    /// <param name="controller">The stop-condition tracker (<c>--capture</c>/<c>--exit-on</c>).</param>
    /// <param name="jsonSink">When non-null, each captured record is written here as a JSONL line (stdout under
    /// <c>--json</c>). Null suppresses JSONL output.</param>
    public CaptureLifecycle(CaptureController controller, TextWriter? jsonSink)
    {
        _controller = controller;
        _jsonSink = jsonSink;
    }

    /// <summary>Record sink invoked by the inspect/pipe handlers for each request. Writes the JSONL line, then
    /// consults the controller. On satisfaction it latches outcome 0 and flags <see cref="StopRequested"/>;
    /// it does NOT call <c>StopApplication()</c> here, so the triggering response can flush first (F6).</summary>
    public void OnRecord(RequestRecord record)
    {
        if (_jsonSink is not null)
        {
            string line = RequestRecord.ToJsonl(record);
            // Kestrel handles requests concurrently; serialise writes so JSONL lines never interleave.
            lock (_sinkLock)
            {
                _jsonSink.WriteLine(line);
                _jsonSink.Flush();
            }
        }

        if (_controller.OnRequest(record))
        {
            // F9: stop-satisfied wins the latch only if nothing (e.g. the timeout) has decided yet.
            Interlocked.CompareExchange(ref _outcome, OutcomeSatisfied, OutcomeUndecided);
            // F6: flag only — the post-handler middleware calls StopApplication after the response flushes.
            Volatile.Write(ref _stopRequested, 1);
        }
    }

    /// <summary>Starts the <c>--timeout</c> timer if a timeout is configured. When it fires it latches outcome 1
    /// (only if the stop condition has not already won) and invokes <paramref name="onTimeout"/> to trigger the
    /// graceful shutdown. No-op when <paramref name="timeout"/> is null.</summary>
    /// <param name="timeout">The configured timeout, or null for "run until Ctrl+C / stop condition".</param>
    /// <param name="onTimeout">Called once when the timer fires (e.g. <c>StopApplication()</c>).</param>
    public void StartTimeout(TimeSpan? timeout, Action onTimeout)
    {
        if (timeout is not TimeSpan span)
        {
            return;
        }

        _timer = new Timer(
            _ =>
            {
                // F9: only act if WE win the latch — a request may already have satisfied the stop.
                if (Interlocked.CompareExchange(ref _outcome, OutcomeTimedOut, OutcomeUndecided) == OutcomeUndecided)
                {
                    onTimeout();
                }
            },
            state: null,
            dueTime: span,
            period: Timeout.InfiniteTimeSpan);
    }

    /// <summary>Maps the latched outcome to the run's exit code. Returns 0 when the stop condition was satisfied
    /// (or no CI condition was configured and the run ended cleanly), and 1 when a timeout elapsed without the
    /// stop condition being met.</summary>
    public int ExitCode()
    {
        int outcome = Volatile.Read(ref _outcome);
        // Undecided means a clean Ctrl+C / no-CI shutdown — treat as success (0).
        return outcome == OutcomeTimedOut ? 1 : 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer?.Dispose();
    }
}
