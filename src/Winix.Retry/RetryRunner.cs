using System.Diagnostics;
using Yort.ShellKit;

namespace Winix.Retry;

/// <summary>
/// Executes a command with automatic retries according to <see cref="RetryOptions"/>.
/// </summary>
public sealed class RetryRunner
{
    /// <summary>
    /// Signature of the process-execution delegate. Receives the command, arguments, and a
    /// cancellation token scoped to the current attempt. Production implementations SHOULD kill
    /// the child process when the token is signalled (e.g. Ctrl+C during a long-running child).
    /// Returns the child's exit code. May throw <see cref="CommandNotFoundException"/>,
    /// <see cref="CommandNotExecutableException"/>, or other exceptions on launch failure; the
    /// runner catches these, preserves partial history, and returns
    /// <see cref="RetryOutcome.LaunchFailed"/>.
    /// </summary>
    public delegate int RunProcessDelegate(string command, string[] arguments, CancellationToken cancellationToken);

    private readonly RunProcessDelegate _runProcess;

    /// <summary>
    /// Creates a runner with a cancellation-aware process delegate. Use this when the caller has a
    /// kill-on-cancel implementation (see <c>src/retry/Program.cs</c> for the reference spawner).
    /// </summary>
    public RetryRunner(RunProcessDelegate runProcess)
    {
        _runProcess = runProcess;
    }

    /// <summary>
    /// Compatibility constructor for callers that only need the cancellation-between-attempts
    /// behaviour (tests, simple fakes). Wraps the legacy <c>Func&lt;string, string[], int&gt;</c>
    /// shape — the cancellation token is ignored by the wrapper, so the in-flight attempt is not
    /// killable. Production code should use the <see cref="RunProcessDelegate"/> overload.
    /// </summary>
    public RetryRunner(Func<string, string[], int> runProcess)
        : this((cmd, args, _) => runProcess(cmd, args))
    {
    }

    /// <summary>
    /// Runs the command with retries according to <paramref name="options"/>.
    /// </summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="arguments">Arguments for the command.</param>
    /// <param name="options">Retry configuration.</param>
    /// <param name="onAttempt">Optional callback invoked after each attempt with progress info.</param>
    /// <param name="delayAction">
    /// Optional delay delegate (for testing). When null, uses a cancellation-aware wait
    /// via <see cref="CancellationToken.WaitHandle"/> so that Ctrl+C interrupts delays immediately.
    /// </param>
    /// <param name="cancellationToken">Token to abort the retry loop between attempts.</param>
    /// <returns>The final result describing how the loop terminated.</returns>
    public RetryResult Run(
        string command,
        string[] arguments,
        RetryOptions options,
        Action<AttemptInfo>? onAttempt = null,
        Action<TimeSpan>? delayAction = null,
        CancellationToken cancellationToken = default)
    {
        // Default delay is cancellation-aware: WaitHandle.WaitOne returns immediately
        // when the token is signalled (e.g. Ctrl+C), unlike Thread.Sleep which blocks
        // for the full duration regardless.
        delayAction ??= (duration) => cancellationToken.WaitHandle.WaitOne(duration);
        Random? random = options.Jitter ? new Random() : null;

        int maxAttempts = options.MaxRetries + 1;
        var delays = new List<TimeSpan>();
        var stopwatch = Stopwatch.StartNew();
        int lastExitCode = 0;
        int attemptNumber = 0;

        for (int i = 0; i < maxAttempts; i++)
        {
            // Check cancellation before every attempt after the first — we always run at
            // least once, but respect cancellation between retries.
            if (i > 0 && cancellationToken.IsCancellationRequested)
            {
                break;
            }

            attemptNumber = i + 1;
            try
            {
                lastExitCode = _runProcess(command, arguments, cancellationToken);
            }
            catch (Exception ex) when (IsLaunchFailure(ex))
            {
                // Launch failed mid-loop (e.g. binary deleted between attempts, permissions
                // revoked, PATH race). Without this catch, the exception escapes RetryRunner
                // with ALL partial history lost — the final JSON envelope reports attempts=0
                // even when N-1 attempts completed successfully. Caller sees "command not
                // found" with no indication that earlier attempts actually ran.
                //
                // Preserve the partial history by recording this failed attempt and returning
                // a LaunchFailed result. Program.cs reads result.LaunchError to route the
                // correct exit code (127/126/etc.) and reports result.Attempts including the
                // prior successful-ran-but-failed-exit-code attempts.
                stopwatch.Stop();
                int classifiedCode = ClassifyLaunchException(ex);

                // Intentionally do NOT fire the progress callback for the failed attempt. The
                // callback's documented contract is "progress for completed attempts" — a
                // LaunchFailed frame is conceptually an ERROR, and in Program.cs the callback
                // writes to the summary stream (which honours --stdout). Firing here would
                // route the launch-failure progress line to stdout under --stdout, while the
                // actual error message goes to stderr — mixing streams. Callers that need to
                // know about the launch failure read result.Outcome / result.LaunchError.
                return new RetryResult(
                    attemptNumber, maxAttempts, classifiedCode,
                    RetryOutcome.LaunchFailed, stopwatch.Elapsed, delays, launchError: ex);
            }

            bool shouldRetry = options.ShouldRetry(lastExitCode);
            bool hasRetriesLeft = attemptNumber < maxAttempts;
            bool willRetry = shouldRetry && hasRetriesLeft && !cancellationToken.IsCancellationRequested;

            TimeSpan? nextDelay = null;
            RetryOutcome? stopReason = null;

            if (willRetry)
            {
                // attempt here is the number of the attempt just completed (1-based).
                // BackoffCalculator uses this to scale: attempt 1 → first retry delay.
                nextDelay = BackoffCalculator.Calculate(
                    options.Delay, attemptNumber, options.Backoff, options.Jitter, random);
            }
            else if (!shouldRetry)
            {
                // Determine the specific stop reason from why ShouldRetry returned false.
                if (options.RetryOnCodes != null && lastExitCode != 0)
                {
                    // --on whitelist mode: non-zero code not in the whitelist — not retryable.
                    stopReason = RetryOutcome.NotRetryable;
                }
                else
                {
                    // Default or --until mode: reached a target code (success).
                    stopReason = RetryOutcome.Succeeded;
                }
            }
            else if (!hasRetriesLeft)
            {
                stopReason = RetryOutcome.RetriesExhausted;
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                // Cancellation during an attempt: shouldRetry and hasRetriesLeft are both true,
                // but willRetry was flipped to false by the cancel-token check on the willRetry
                // line. Without this arm, stopReason would stay null, FormatAttempt would fall
                // through to "no retries remaining" — actively misleading given the user pressed
                // Ctrl+C with retries still available. Round-4 I1 fix.
                stopReason = RetryOutcome.Cancelled;
            }

            onAttempt?.Invoke(new AttemptInfo(
                attemptNumber, maxAttempts, lastExitCode,
                nextDelay, willRetry, stopReason));

            if (!willRetry)
            {
                break;
            }

            // Record ACTUAL wall-clock delay, not nominal. If cancellation fires mid-delay the
            // delayAction returns early; recording the nominal value produces impossible data
            // (total_seconds < sum(delays_seconds)) for analytics consumers. Measure against the
            // outer stopwatch so the two fields stay coherent.
            //
            // The `finally` ensures partial history is preserved even if delayAction throws (e.g.
            // a caller that uses `Task.Delay(d, token).Wait()` raises AggregateException on
            // cancel). The delay entry lands in `delays` before the exception propagates to the
            // outer `IsLaunchFailure` catch — same partial-history discipline as the launch path.
            TimeSpan beforeDelay = stopwatch.Elapsed;
            try
            {
                delayAction(nextDelay!.Value);
            }
            finally
            {
                delays.Add(stopwatch.Elapsed - beforeDelay);
            }
        }

        stopwatch.Stop();

        // Derive the final outcome. Cancellation must be distinguished from exhaustion — a
        // user-initiated Ctrl+C reports the same "retries_exhausted" code as a genuinely-failing
        // command if we conflate the two, making CI dashboards and triage scripts indistinguishable
        // between "user gave up" and "command truly broken".
        RetryOutcome outcome;
        if (cancellationToken.IsCancellationRequested)
        {
            outcome = RetryOutcome.Cancelled;
        }
        else if (!options.ShouldRetry(lastExitCode))
        {
            if (options.RetryOnCodes != null && lastExitCode != 0)
            {
                outcome = RetryOutcome.NotRetryable;
            }
            else
            {
                outcome = RetryOutcome.Succeeded;
            }
        }
        else
        {
            // Wanted to retry but ran out of attempts.
            outcome = RetryOutcome.RetriesExhausted;
        }

        return new RetryResult(
            attemptNumber, maxAttempts, lastExitCode,
            outcome, stopwatch.Elapsed, delays);
    }

    /// <summary>
    /// Identifies exceptions that represent a launch failure the runner should convert into a
    /// <see cref="RetryOutcome.LaunchFailed"/> result (preserving partial history), as opposed to
    /// bugs that should propagate. Only the two typed launch-failure exceptions pass — anything
    /// else (Win32Exception from WaitForExit/ExitCode, OOM, library bugs) still escapes and
    /// crashes loudly, which is the correct behaviour for unexpected errors. Narrower than
    /// round-1's filter which caught all Win32Exception + string-matched InvalidOperationException.
    /// </summary>
    private static bool IsLaunchFailure(Exception ex)
        => ex is CommandNotFoundException
        || ex is CommandNotExecutableException;

    /// <summary>
    /// Maps a launch-failure exception to a POSIX-shaped exit code.
    /// </summary>
    private static int ClassifyLaunchException(Exception ex) => ex switch
    {
        CommandNotFoundException => ExitCode.NotFound,
        CommandNotExecutableException => ExitCode.NotExecutable,
        _ => throw new ArgumentException($"not a launch-failure exception: {ex.GetType().Name}", nameof(ex))
    };
}
