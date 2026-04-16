using Winix.Retry;
using Xunit;

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
    public void Run_Cancellation_BreaksLoop()
    {
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
    }
}
