using System.ComponentModel;
using Winix.Retry;
using Xunit;
using Yort.ShellKit;

namespace Winix.Retry.Tests;

public class RetryRunnerTests
{
    /// <summary>
    /// Helper: creates a process runner that returns exit codes from the given sequence,
    /// repeating the last code if the sequence is overrun.
    /// </summary>
    private static Func<string, string[], int> ExitCodeSequence(params int[] codes)
    {
        int index = 0;
        return (cmd, args) =>
        {
            if (index >= codes.Length)
            {
                return codes[codes.Length - 1];
            }
            return codes[index++];
        };
    }

    [Fact]
    public void Run_SucceedsOnFirstAttempt_ReturnsSucceeded()
    {
        var runner = new RetryRunner(ExitCodeSequence(0));
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, result.Attempts);
        Assert.Equal(0, result.ChildExitCode);
        Assert.Empty(result.Delays);
    }

    [Fact]
    public void Run_FailsThenSucceeds_ReturnsSucceeded()
    {
        var runner = new RetryRunner(ExitCodeSequence(1, 1, 0));
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.Succeeded, result.Outcome);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(0, result.ChildExitCode);
        Assert.Equal(2, result.Delays.Count);
    }

    [Fact]
    public void Run_AllAttemptsFail_ReturnsExhausted()
    {
        var runner = new RetryRunner(ExitCodeSequence(1, 1, 1, 1));
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.RetriesExhausted, result.Outcome);
        Assert.Equal(4, result.Attempts);
        Assert.Equal(4, result.MaxAttempts);
        Assert.Equal(1, result.ChildExitCode);
        Assert.Equal(3, result.Delays.Count);
    }

    [Fact]
    public void Run_ZeroRetries_RunsOnceOnly()
    {
        var runner = new RetryRunner(ExitCodeSequence(1));
        var options = new RetryOptions(maxRetries: 0, delay: TimeSpan.Zero);

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.RetriesExhausted, result.Outcome);
        Assert.Equal(1, result.Attempts);
        Assert.Empty(result.Delays);
    }

    [Fact]
    public void Run_WithOnCodes_StopsOnNonRetryableCode()
    {
        var runner = new RetryRunner(ExitCodeSequence(1, 137));
        var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.Zero,
            retryOnCodes: new HashSet<int> { 1 });

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.NotRetryable, result.Outcome);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(137, result.ChildExitCode);
    }

    [Fact]
    public void Run_WithOnCodes_SucceedsOnFirstAttempt()
    {
        var runner = new RetryRunner(ExitCodeSequence(0));
        var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.Zero,
            retryOnCodes: new HashSet<int> { 1 });

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, result.Attempts);
        Assert.Equal(0, result.ChildExitCode);
    }

    [Fact]
    public void Run_WithUntilCodes_StopsOnTargetCode()
    {
        var runner = new RetryRunner(ExitCodeSequence(0, 0, 1));
        var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.Zero,
            stopOnCodes: new HashSet<int> { 1 });

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.Succeeded, result.Outcome);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(1, result.ChildExitCode);
    }

    [Fact]
    public void Run_WithUntilWithoutZero_RetriesOnZero()
    {
        var runner = new RetryRunner(ExitCodeSequence(0, 0, 0, 1));
        var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.Zero,
            stopOnCodes: new HashSet<int> { 1 });

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(4, result.Attempts);
        Assert.Equal(1, result.ChildExitCode);
        Assert.Equal(RetryOutcome.Succeeded, result.Outcome);
    }

    [Fact]
    public void Run_InvokesCallback_ForEachAttempt()
    {
        var runner = new RetryRunner(ExitCodeSequence(1, 0));
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);
        var callbacks = new List<AttemptInfo>();

        var result = runner.Run("cmd", Array.Empty<string>(), options,
            onAttempt: info => callbacks.Add(info));

        Assert.Equal(2, callbacks.Count);

        // First attempt: failed, will retry
        Assert.Equal(1, callbacks[0].Attempt);
        Assert.Equal(1, callbacks[0].ExitCode);
        Assert.True(callbacks[0].WillRetry);
        Assert.NotNull(callbacks[0].NextDelay);

        // Second attempt: succeeded, no retry
        Assert.Equal(2, callbacks[1].Attempt);
        Assert.Equal(0, callbacks[1].ExitCode);
        Assert.False(callbacks[1].WillRetry);
        Assert.Equal(RetryOutcome.Succeeded, callbacks[1].StopReason);
    }

    [Fact]
    public void Run_Cancellation_BreaksLoopWithCancelledOutcome()
    {
        // Round-2 C1: previously the cancellation path fell through to RetriesExhausted,
        // indistinguishable from genuine failure in JSON output. Now reports Cancelled so
        // dashboards and CI scripts can tell user-initiated cancellation apart from "retry
        // genuinely gave up".
        int callCount = 0;
        var cts = new CancellationTokenSource();

        var runner = new RetryRunner((cmd, args) =>
        {
            callCount++;
            if (callCount == 2) { cts.Cancel(); }
            return 1;
        });

        var options = new RetryOptions(maxRetries: 10, delay: TimeSpan.Zero);

        var result = runner.Run("cmd", Array.Empty<string>(), options,
            cancellationToken: cts.Token);

        // Should have stopped after cancellation, not run all 11 attempts
        Assert.True(result.Attempts <= 3);
        Assert.Equal(1, result.ChildExitCode);
        // The outcome must be Cancelled — NOT RetriesExhausted — so JSON `exit_reason` is
        // distinguishable from a genuinely-exhausted retry loop.
        Assert.Equal(RetryOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public void Run_CancellationDuringDelay_RecordsActualDelayNotNominal()
    {
        // Round-2 I2: delays_seconds previously recorded the nominal delay, not the actual
        // wait. If cancellation fires mid-delay, recording 30.0 when the actual wait was 0.3s
        // produces impossible data (total_seconds < sum(delays_seconds)). Now measured against
        // the outer stopwatch so the two fields stay coherent.
        var cts = new CancellationTokenSource();
        bool delayCalled = false;

        var runner = new RetryRunner((cmd, args) => 1);
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(30));

        // Fake delayAction that simulates early-return on cancel (10 ms instead of 30 s).
        Action<TimeSpan> fakeDelay = (duration) =>
        {
            delayCalled = true;
            cts.Cancel();    // triggers outer loop to break at next iteration
            // Do NOT sleep the full duration — simulates the cancellation-aware
            // WaitHandle.WaitOne returning early. Real delay: near-zero.
        };

        var result = runner.Run("cmd", Array.Empty<string>(), options,
            delayAction: fakeDelay, cancellationToken: cts.Token);

        Assert.True(delayCalled);
        Assert.Equal(RetryOutcome.Cancelled, result.Outcome);
        // The recorded delay must be the actual near-zero elapsed, not the nominal 30s.
        Assert.Single(result.Delays);
        Assert.True(result.Delays[0] < TimeSpan.FromSeconds(1),
            $"expected near-zero actual delay; recorded {result.Delays[0]}");
    }

    // --- Round-1 review additions: LaunchFailed outcome + partial-history preservation ---

    [Fact]
    public void Run_DelegateThrowsCommandNotFound_ReturnsLaunchFailedWith127()
    {
        // C2 regression: prior to round 1, any exception from the delegate escaped RetryRunner
        // and all partial history (prior successful attempts, delays, timer) was lost. Now the
        // runner catches the known launch-failure shapes, preserves partial history, and
        // surfaces LaunchFailed outcome with the classified POSIX exit code.
        var runner = new RetryRunner((cmd, args) => throw new CommandNotFoundException(cmd));
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.LaunchFailed, result.Outcome);
        Assert.Equal(ExitCode.NotFound, result.ChildExitCode);
        Assert.IsType<CommandNotFoundException>(result.LaunchError);
        Assert.Equal(1, result.Attempts);
        Assert.Empty(result.Delays);
    }

    [Fact]
    public void Run_DelegateThrowsCommandNotExecutable_ReturnsLaunchFailedWith126()
    {
        var runner = new RetryRunner((cmd, args) => throw new CommandNotExecutableException(cmd));
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.LaunchFailed, result.Outcome);
        Assert.Equal(ExitCode.NotExecutable, result.ChildExitCode);
        Assert.IsType<CommandNotExecutableException>(result.LaunchError);
    }

    [Fact]
    public void Run_DelegateThrowsRawWin32Exception_Propagates()
    {
        // Round-2 narrowing: the library's IsLaunchFailure catch only recognises the TWO typed
        // launch-failure exceptions (CommandNotFoundException, CommandNotExecutableException).
        // A raw Win32Exception — previously caught as LaunchFailed — now escapes because mid-run
        // Win32Exceptions (from WaitForExit, ExitCode) must not be silently misclassified as
        // launch failures. Spawners are responsible for translating launch-specific Win32
        // errors into the typed wrappers at Process.Start time; see retry's Program.cs spawner
        // which now wraps unknown Win32 codes in CommandNotExecutableException.
        var runner = new RetryRunner((cmd, args) =>
            throw new Win32Exception(193, "%1 is not a valid Win32 application."));
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);

        Assert.Throws<Win32Exception>(() =>
            runner.Run("cmd", Array.Empty<string>(), options));
    }

    [Fact]
    public void Run_DelegateThrowsCommandNotExecutableForBadExeFormat_ReturnsLaunchFailedWith126()
    {
        // The supported path: spawner translates ERROR_BAD_EXE_FORMAT into a typed
        // CommandNotExecutableException. Library catches it and classifies as NotExecutable (126).
        var runner = new RetryRunner((cmd, args) =>
            throw new CommandNotExecutableException($"{cmd}: %1 is not a valid Win32 application."));
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.LaunchFailed, result.Outcome);
        Assert.Equal(ExitCode.NotExecutable, result.ChildExitCode);
        Assert.IsType<CommandNotExecutableException>(result.LaunchError);
    }

    [Fact]
    public void Run_DelegateThrowsInvalidOperation_Propagates_NotMisclassified()
    {
        // Round-2: prior IsLaunchFailure included `InvalidOperationException && Message.Contains("failed to start")`
        // — a locale-dependent string-sniff that could misclassify genuine library bugs as
        // LaunchFailed. Removed. Library bugs that throw InvalidOperationException must now
        // escape loudly so they surface in testing rather than hide in the JSON envelope.
        var runner = new RetryRunner((cmd, args) =>
            throw new InvalidOperationException("failed to start something unrelated"));
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);

        Assert.Throws<InvalidOperationException>(() =>
            runner.Run("cmd", Array.Empty<string>(), options));
    }

    [Fact]
    public void Run_DelegateThrowsMidLoop_PartialHistoryPreserved()
    {
        // C2 core scenario: two successful attempts (exit 1), then the third throws. Previously
        // the user saw "command not found" with attempts=0 — actively misleading. Now the
        // result reports 3 attempts (two that ran + the one that failed to launch), the two
        // actual delays, and LaunchFailed outcome.
        int callCount = 0;
        var runner = new RetryRunner((cmd, args) =>
        {
            callCount++;
            if (callCount >= 3) { throw new CommandNotFoundException(cmd); }
            return 1;  // trigger retries via default `retry on non-zero` behaviour
        });
        var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.Zero);

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.LaunchFailed, result.Outcome);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(2, result.Delays.Count);  // two inter-attempt delays preceded the failing launch
        Assert.Equal(ExitCode.NotFound, result.ChildExitCode);
        Assert.IsType<CommandNotFoundException>(result.LaunchError);
    }

    [Fact]
    public void Run_DelegateThrowsMidLoop_ProgressCallbackOnlyFiresForCompletedAttempts()
    {
        // The callback's documented contract is "progress for completed attempts" — a launch
        // failure is an error, not progress. Firing the callback for the launch-failed attempt
        // would route the progress line to stdout under --stdout while the actual error goes
        // to stderr (Program.cs), mixing output streams. Callers read result.Outcome /
        // result.LaunchError instead.
        int callCount = 0;
        var runner = new RetryRunner((cmd, args) =>
        {
            callCount++;
            if (callCount == 2) { throw new CommandNotFoundException(cmd); }
            return 1;
        });
        var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.Zero);
        var callbacks = new List<AttemptInfo>();

        var result = runner.Run("cmd", Array.Empty<string>(), options,
            onAttempt: info => callbacks.Add(info));

        // Exactly one callback: the first attempt that actually ran (exit 1, will retry).
        // The second attempt's launch failed and did not produce a progress callback.
        Assert.Single(callbacks);
        Assert.Equal(1, callbacks[0].Attempt);
        Assert.True(callbacks[0].WillRetry);

        // Partial history still preserved in the result — caller learns about the failure here.
        Assert.Equal(2, result.Attempts);
        Assert.Equal(RetryOutcome.LaunchFailed, result.Outcome);
    }

    [Fact]
    public void Run_DelegateThrowsUnexpectedException_NotCaught()
    {
        // The catch filter is intentionally narrow — only launch-shaped exceptions produce
        // LaunchFailed. A genuine bug (OutOfMemoryException, InvalidCastException, NRE) must
        // escape so it crashes loudly in testing rather than being masked as "launch failed".
        var runner = new RetryRunner((cmd, args) => throw new InvalidCastException("definitely-a-bug"));
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.Zero);

        var ex = Assert.Throws<InvalidCastException>(() =>
            runner.Run("cmd", Array.Empty<string>(), options));
        Assert.Equal("definitely-a-bug", ex.Message);
    }

    [Fact]
    public void Run_RunProcessDelegateOverload_PropagatesCancellationToken()
    {
        // The new RunProcessDelegate signature includes a CancellationToken so the spawner can
        // kill the in-flight child on Ctrl+C. Verify the token the runner passes to the
        // delegate is the same one the caller supplied.
        CancellationToken? seen = null;
        var cts = new CancellationTokenSource();
        RetryRunner.RunProcessDelegate del = (cmd, args, token) =>
        {
            seen = token;
            return 0;
        };
        var runner = new RetryRunner(del);
        var options = new RetryOptions(maxRetries: 0, delay: TimeSpan.Zero);

        runner.Run("cmd", Array.Empty<string>(), options, cancellationToken: cts.Token);

        Assert.True(seen.HasValue);
        Assert.Equal(cts.Token, seen!.Value);
    }

    [Fact]
    public void Run_OnCodesWithZero_ShouldRetryStillStopsOnZero()
    {
        // Edge case: `--on 0,1` — user asks to retry on 0 or 1. But ShouldRetry's exit-code
        // guard (`exitCode != 0`) short-circuits retrying on 0. Pin the behaviour so a refactor
        // that removes the guard doesn't accidentally make retry loop forever on success.
        var runner = new RetryRunner(ExitCodeSequence(0));
        var options = new RetryOptions(maxRetries: 10, delay: TimeSpan.Zero,
            retryOnCodes: new HashSet<int> { 0, 1 });

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(1, result.Attempts);
        Assert.Equal(RetryOutcome.Succeeded, result.Outcome);
    }

    [Fact]
    public void Run_UntilCodes_MatchOnFirstAttempt_ReturnsOneAttemptNoDelays()
    {
        // Asymmetric-path gap from the review: first-attempt --until match wasn't pinned. If
        // the user's poll-condition fires immediately (e.g. `retry --until 42` and the command
        // already returns 42), we must return 1 attempt, 0 delays, Succeeded outcome.
        var runner = new RetryRunner(ExitCodeSequence(42));
        var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.Zero,
            stopOnCodes: new HashSet<int> { 42 });

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, result.Attempts);
        Assert.Empty(result.Delays);
        Assert.Equal(42, result.ChildExitCode);
    }

    [Fact]
    public void Run_CancellationDuringAttempt_CallbackStopReasonIsCancelled()
    {
        // Round-4 I1 regression pin: on cancellation mid-attempt the progress-callback's
        // StopReason must be Cancelled, not null. Previously stayed null → FormatAttempt fell
        // through to "no retries remaining". The fix adds the Cancelled arm in the derivation;
        // this test pins it so a future refactor of the if/else chain doesn't regress.
        var cts = new CancellationTokenSource();
        int callCount = 0;
        var runner = new RetryRunner((cmd, args) =>
        {
            callCount++;
            if (callCount == 1)
            {
                cts.Cancel();
                // Return a kill-signal-like code to mimic a child killed by the cancel.
                return 137;
            }
            return 0;
        });
        var options = new RetryOptions(maxRetries: 10, delay: TimeSpan.Zero);
        var callbacks = new List<AttemptInfo>();

        var result = runner.Run("cmd", Array.Empty<string>(), options,
            onAttempt: info => callbacks.Add(info),
            cancellationToken: cts.Token);

        Assert.Single(callbacks);
        Assert.False(callbacks[0].WillRetry);
        // The key assertion: StopReason is Cancelled, NOT null (which would cause FormatAttempt
        // to render "no retries remaining" after a cancel).
        Assert.Equal(RetryOutcome.Cancelled, callbacks[0].StopReason);
        Assert.Equal(RetryOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public void Run_CancellationWithOnCodes_OutcomeIsCancelledNotNotRetryable()
    {
        // Round-4 gap from pr-test-analyzer: Cancelled must take precedence over the --on
        // filter's NotRetryable classification. The outcome-derivation ordering in
        // RetryRunner checks IsCancellationRequested BEFORE !ShouldRetry — but a refactor
        // that reordered them would silently demote user cancellations to not_retryable.
        var cts = new CancellationTokenSource();
        var runner = new RetryRunner((cmd, args) =>
        {
            cts.Cancel();
            return 1;   // would have been retryable under --on 1
        });
        // --on 1 means "retry only on exit 1". Default ShouldRetry says retry; cancel-first wins.
        var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.Zero,
            retryOnCodes: new HashSet<int> { 1 });

        var result = runner.Run("cmd", Array.Empty<string>(), options,
            cancellationToken: cts.Token);

        Assert.Equal(RetryOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public void Run_CancellationWithUntilCodes_OutcomeIsCancelledNotSucceeded()
    {
        // Mirror of the --on test for --until. Cancel must take precedence over the Succeeded
        // classification that --until would otherwise produce.
        var cts = new CancellationTokenSource();
        var runner = new RetryRunner((cmd, args) =>
        {
            cts.Cancel();
            return 42;   // would match --until 42 and report Succeeded
        });
        var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.Zero,
            stopOnCodes: new HashSet<int> { 42 });

        var result = runner.Run("cmd", Array.Empty<string>(), options,
            cancellationToken: cts.Token);

        Assert.Equal(RetryOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public void Run_MultipleAttempts_TotalSecondsIsAtLeastSumOfDelays()
    {
        // Round-4 gap (NH-3): the actual-delay recording fix at R2 must preserve the coherence
        // invariant `total_seconds >= sum(delays_seconds)`. Any Stopwatch misuse or rounding
        // regression that broke the invariant would produce impossible analytics data. Pin it.
        var delayRecord = new List<TimeSpan>();
        var runner = new RetryRunner((cmd, args) => 1);   // always fails → retries exhausted
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10));

        // Use a fake delayAction that waits the full duration (Thread.Sleep is cheap at 10ms).
        var result = runner.Run("cmd", Array.Empty<string>(), options,
            delayAction: (d) => { delayRecord.Add(d); Thread.Sleep(d); });

        Assert.Equal(3, result.Delays.Count);
        double sumDelays = result.Delays.Sum(d => d.TotalSeconds);
        double total = result.TotalTime.TotalSeconds;
        Assert.True(total >= sumDelays,
            $"total_seconds ({total:F4}) must be >= sum(delays_seconds) ({sumDelays:F4})");
    }

    [Fact]
    public void Run_DelayActionThrows_ExceptionPropagatesNotSwallowed()
    {
        // Round-4 Minor: delayAction throwing previously lost the partial delay entry because
        // delays.Add fired AFTER the call. Now wrapped in try/finally so the partial history
        // lands in `delays` before the exception propagates — the finally runs on the way out.
        //
        // The partial-delay-was-recorded invariant is a white-box property: the runner's
        // internal `delays` list is never surfaced to the caller when an exception escapes
        // (the RetryResult is only returned on normal exit). What this test genuinely pins
        // is the public contract: a delayAction that throws an unexpected exception type
        // (not in IsLaunchFailure's allowlist) escapes the runner — it is NOT swallowed as
        // a LaunchFailed outcome. The try/finally internal shape is a code-comment assertion;
        // this test is the behavioural regression-pin that would fail if the try/finally
        // accidentally became try/catch-and-swallow.
        var runner = new RetryRunner((cmd, args) => 1);
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10));

        Assert.Throws<InvalidOperationException>(() =>
            runner.Run("cmd", Array.Empty<string>(), options,
                delayAction: (_) => throw new InvalidOperationException("simulated delay failure")));
    }

    [Fact]
    public void Run_MaxRetriesOne_FirstFailsSecondSucceeds_Returns2AttemptsAnd1Delay()
    {
        // The --times 1 boundary: exactly one retry after the initial attempt. Off-by-one
        // regressions (treating --times as inclusive/exclusive of the initial run) are the
        // most common class of retry bug. Pin the boundary.
        var runner = new RetryRunner(ExitCodeSequence(1, 0));
        var options = new RetryOptions(maxRetries: 1, delay: TimeSpan.Zero);

        var result = runner.Run("cmd", Array.Empty<string>(), options);

        Assert.Equal(RetryOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, result.Attempts);
        Assert.Single(result.Delays);
        Assert.Equal(0, result.ChildExitCode);
    }
}
