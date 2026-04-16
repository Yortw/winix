using Xunit;
using Winix.Retry;

namespace Winix.Retry.Tests;

public class FormatAttemptTests
{
    [Fact]
    public void FormatAttempt_FailedWillRetry_ShowsRetryMessage()
    {
        var info = new AttemptInfo(1, 4, exitCode: 1,
            nextDelay: TimeSpan.FromSeconds(2), willRetry: true, stopReason: null);

        string line = Formatting.FormatAttempt(info, useColor: false);

        Assert.Contains("attempt 1/4", line);
        Assert.Contains("failed", line);
        Assert.Contains("exit 1", line);
        Assert.Contains("retrying in 2", line);
    }

    [Fact]
    public void FormatAttempt_Succeeded_ShowsSuccessMessage()
    {
        var info = new AttemptInfo(3, 4, exitCode: 0,
            nextDelay: null, willRetry: false, stopReason: RetryOutcome.Succeeded);

        string line = Formatting.FormatAttempt(info, useColor: false);

        Assert.Contains("attempt 3/4", line);
        Assert.Contains("succeeded", line);
        Assert.Contains("3 attempts", line);
    }

    [Fact]
    public void FormatAttempt_Exhausted_ShowsNoRetriesMessage()
    {
        var info = new AttemptInfo(4, 4, exitCode: 1,
            nextDelay: null, willRetry: false, stopReason: RetryOutcome.RetriesExhausted);

        string line = Formatting.FormatAttempt(info, useColor: false);

        Assert.Contains("attempt 4/4", line);
        Assert.Contains("failed", line);
        Assert.Contains("no retries remaining", line);
    }

    [Fact]
    public void FormatAttempt_NotRetryable_ShowsStoppingMessage()
    {
        var info = new AttemptInfo(1, 4, exitCode: 137,
            nextDelay: null, willRetry: false, stopReason: RetryOutcome.NotRetryable);

        string line = Formatting.FormatAttempt(info, useColor: false);

        Assert.Contains("attempt 1/4", line);
        Assert.Contains("exit 137", line);
        Assert.Contains("not retryable", line);
    }

    [Fact]
    public void FormatAttempt_UntilTargetHit_ShowsMatchedMessage()
    {
        var info = new AttemptInfo(2, 4, exitCode: 1,
            nextDelay: null, willRetry: false, stopReason: RetryOutcome.Succeeded);

        // Exit code 1 succeeded = --until target hit (non-zero success)
        string line = Formatting.FormatAttempt(info, useColor: false);

        Assert.Contains("attempt 2/4", line);
        Assert.Contains("matched target", line);
        Assert.Contains("exit 1", line);
    }

    [Fact]
    public void FormatAttempt_WithColor_ContainsAnsiSequences()
    {
        var info = new AttemptInfo(1, 4, exitCode: 1,
            nextDelay: TimeSpan.FromSeconds(2), willRetry: true, stopReason: null);

        string line = Formatting.FormatAttempt(info, useColor: true);

        Assert.Contains("\x1b[", line);
    }

    [Fact]
    public void FormatAttempt_SubSecondDelay_ShowsMilliseconds()
    {
        var info = new AttemptInfo(1, 4, exitCode: 1,
            nextDelay: TimeSpan.FromMilliseconds(500), willRetry: true, stopReason: null);

        string line = Formatting.FormatAttempt(info, useColor: false);

        Assert.Contains("500ms", line);
    }
}

public class FormatJsonTests
{
    [Fact]
    public void FormatJson_Succeeded_ContainsExpectedFields()
    {
        var result = new RetryResult(
            attempts: 3, maxAttempts: 4, childExitCode: 0,
            outcome: RetryOutcome.Succeeded,
            totalTime: TimeSpan.FromSeconds(6.5),
            delays: new List<TimeSpan> { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2) });

        string json = Formatting.FormatJson(result, "retry", "0.3.0");

        Assert.Contains("\"tool\":\"retry\"", json);
        Assert.Contains("\"version\":\"0.3.0\"", json);
        Assert.Contains("\"exit_code\":0", json);
        Assert.Contains("\"exit_reason\":\"succeeded\"", json);
        Assert.Contains("\"child_exit_code\":0", json);
        Assert.Contains("\"attempts\":3", json);
        Assert.Contains("\"max_attempts\":4", json);
        Assert.Contains("\"total_seconds\":", json);
        Assert.Contains("\"delays_seconds\":[", json);
    }

    [Fact]
    public void FormatJson_Exhausted_ShowsCorrectReason()
    {
        var result = new RetryResult(
            attempts: 4, maxAttempts: 4, childExitCode: 1,
            outcome: RetryOutcome.RetriesExhausted,
            totalTime: TimeSpan.FromSeconds(12),
            delays: new List<TimeSpan> { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2) });

        string json = Formatting.FormatJson(result, "retry", "0.3.0");

        Assert.Contains("\"exit_code\":1", json);
        Assert.Contains("\"exit_reason\":\"retries_exhausted\"", json);
    }

    [Fact]
    public void FormatJson_NotRetryable_ShowsCorrectReason()
    {
        var result = new RetryResult(
            attempts: 2, maxAttempts: 4, childExitCode: 137,
            outcome: RetryOutcome.NotRetryable,
            totalTime: TimeSpan.FromSeconds(3),
            delays: new List<TimeSpan> { TimeSpan.FromSeconds(1) });

        string json = Formatting.FormatJson(result, "retry", "0.3.0");

        Assert.Contains("\"exit_code\":137", json);
        Assert.Contains("\"exit_reason\":\"not_retryable\"", json);
    }

    [Fact]
    public void FormatJsonError_ContainsExpectedFields()
    {
        string json = Formatting.FormatJsonError(127, "command_not_found", "retry", "0.3.0");

        Assert.Contains("\"tool\":\"retry\"", json);
        Assert.Contains("\"exit_code\":127", json);
        Assert.Contains("\"exit_reason\":\"command_not_found\"", json);
        Assert.Contains("\"child_exit_code\":null", json);
        Assert.Contains("\"attempts\":0", json);
    }
}
