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
