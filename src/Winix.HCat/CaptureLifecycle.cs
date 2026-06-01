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
    private readonly TextWriter? _humanSink;
    private readonly bool _useColor;
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
    /// <param name="humanSink">When non-null AND <paramref name="jsonSink"/> is null, a terse per-request line is
    /// written here (the human "watch requests" log; stderr in production). Ignored when JSONL is active.</param>
    /// <param name="useColor">When true, emit ANSI colour in the human log lines (method dim, status by class).
    /// JSONL output is never coloured regardless of this flag. Pass <c>options.UseColor</c> at the production
    /// call site; default false preserves existing callers that omit the parameter.</param>
    public CaptureLifecycle(CaptureController controller, TextWriter? jsonSink, TextWriter? humanSink = null,
                            bool useColor = false)
    {
        _controller = controller;
        _jsonSink = jsonSink;
        _humanSink = humanSink;
        // A6: _useColor is immutable after construction so ColorStatus is thread-safe without locking.
        _useColor = useColor;
    }

    /// <summary>Record sink invoked by the inspect/pipe handlers for each request (before the response status is
    /// known — pipe calls this prior to running the child). Emits the per-request line, then consults the
    /// controller. It does NOT call <c>StopApplication()</c> here, so the triggering response can flush first
    /// (F6). JSONL is the full request record; the human line is method (dim when colour on) + path.</summary>
    public void OnRecord(RequestRecord record)
    {
        if (_jsonSink is not null)
        {
            // JSONL output is never coloured — byte-identical to the no-colour path.
            WriteLineLocked(_jsonSink, RequestRecord.ToJsonl(record));
        }
        else if (_humanSink is not null)
        {
            // A6: build the coloured line as a local before entering the lock so no work is done inside it.
            string dim   = Yort.ShellKit.AnsiColor.Dim(_useColor);
            string reset = Yort.ShellKit.AnsiColor.Reset(_useColor);
            string line  = $"{dim}{record.Method}{reset} {record.Path}";
            WriteLineLocked(_humanSink, line);
        }

        ConsultController(record);
    }

    /// <summary>Per-request sink for SERVE mode, called AFTER the file server produced the response so the final
    /// <paramref name="status"/> is known. Emits the access-log line — JSONL <c>{method,path,status}</c> under
    /// <c>--json</c>, else a human <c>METHOD /path STATUS</c> line (method dim, status coloured by class when
    /// colour is on) — then consults the controller. Like <see cref="OnRecord"/>, it only flags the stop (F6).</summary>
    public void OnServeAccess(RequestRecord record, int status)
    {
        if (_jsonSink is not null)
        {
            // JSONL output is never coloured — byte-identical to the no-colour path.
            WriteLineLocked(_jsonSink, AccessLogRecord.ToJsonl(new AccessLogRecord(record.Method, record.Path, status)));
        }
        else if (_humanSink is not null)
        {
            // A6: build the coloured line as a local before entering the lock so no work is done inside it.
            string dim   = Yort.ShellKit.AnsiColor.Dim(_useColor);
            string reset = Yort.ShellKit.AnsiColor.Reset(_useColor);
            string line  = $"{dim}{record.Method}{reset} {record.Path} {ColorStatus(status)}";
            WriteLineLocked(_humanSink, line);
        }

        ConsultController(record);
    }

    /// <summary>Returns the status code as a string, wrapped in the appropriate ANSI colour when colour is
    /// enabled: 2xx green, 3xx cyan, 4xx yellow, 5xx red. Returns a plain decimal string when colour is off.
    /// <c>_useColor</c> is immutable (ctor-set), so this method is thread-safe without locking.</summary>
    private string ColorStatus(int status)
    {
        if (!_useColor)
        {
            return status.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        string code =
            status >= 500 ? Yort.ShellKit.AnsiColor.Red(true) :
            status >= 400 ? Yort.ShellKit.AnsiColor.Yellow(true) :
            status >= 300 ? Yort.ShellKit.AnsiColor.Cyan(true) :
                            Yort.ShellKit.AnsiColor.Green(true);
        return $"{code}{status.ToString(System.Globalization.CultureInfo.InvariantCulture)}{Yort.ShellKit.AnsiColor.Reset(true)}";
    }

    /// <summary>Feeds the request to the controller and, on stop-condition satisfaction, latches outcome 0 and
    /// flags <see cref="StopRequested"/> (the post-handler middleware calls <c>StopApplication</c> after the
    /// response flushes — F6/F9). Shared by <see cref="OnRecord"/> and <see cref="OnServeAccess"/>.</summary>
    private void ConsultController(RequestRecord record)
    {
        if (_controller.OnRequest(record))
        {
            Interlocked.CompareExchange(ref _outcome, OutcomeSatisfied, OutcomeUndecided);
            Volatile.Write(ref _stopRequested, 1);
        }
    }

    /// <summary>Writes one line + flush under the sink lock. Kestrel handles requests concurrently, so the lock
    /// keeps lines from interleaving across requests.</summary>
    private void WriteLineLocked(TextWriter sink, string line)
    {
        lock (_sinkLock)
        {
            sink.WriteLine(line);
            sink.Flush();
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
