using System.Diagnostics;

namespace Winix.Retry;

/// <summary>
/// Executes a command with automatic retries according to <see cref="RetryOptions"/>.
/// </summary>
public sealed class RetryRunner
{
    private readonly Func<string, string[], int> _runProcess;

    /// <summary>
    /// Creates a runner that uses the given delegate to execute the command.
    /// The delegate receives (command, arguments) and returns the exit code.
    /// </summary>
    /// <param name="runProcess">
    /// Process execution delegate. For production use, pass a delegate that spawns
    /// a real child process. For testing, pass a fake that returns scripted exit codes.
    /// </param>
    public RetryRunner(Func<string, string[], int> runProcess)
    {
        _runProcess = runProcess;
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
            lastExitCode = _runProcess(command, arguments);

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
            // If cancelled mid-loop, stopReason stays null — the outer outcome derivation handles it.

            onAttempt?.Invoke(new AttemptInfo(
                attemptNumber, maxAttempts, lastExitCode,
                nextDelay, willRetry, stopReason));

            if (!willRetry)
            {
                break;
            }

            delays.Add(nextDelay!.Value);
            delayAction(nextDelay!.Value);
        }

        stopwatch.Stop();

        // Derive the final outcome from the last exit code using the same ShouldRetry logic.
        RetryOutcome outcome;
        if (!options.ShouldRetry(lastExitCode))
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
            // Still wanted to retry (either exhausted or cancelled).
            outcome = RetryOutcome.RetriesExhausted;
        }

        return new RetryResult(
            attemptNumber, maxAttempts, lastExitCode,
            outcome, stopwatch.Elapsed, delays);
    }
}
