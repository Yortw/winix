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

    // --- Round-1 review additions: specific colour per outcome + LaunchFailed + Reset-emitted ---

    [Fact]
    public void FormatAttempt_Failed_UsesRedColour()
    {
        // Round-1 I6: a regression that swapped red↔green (e.g. by re-ordering the AnsiColor
        // variable assignments) would ship green-coloured failures. Pin the specific SGR.
        var info = new AttemptInfo(1, 4, exitCode: 1,
            nextDelay: TimeSpan.FromSeconds(2), willRetry: true, stopReason: null);

        string line = Formatting.FormatAttempt(info, useColor: true);

        // ANSI SGR 31 = red. Must appear around the "failed" word.
        Assert.Contains("\x1b[31m", line);
        Assert.Contains("failed", line);
    }

    [Fact]
    public void FormatAttempt_Succeeded_UsesGreenColour()
    {
        var info = new AttemptInfo(3, 4, exitCode: 0,
            nextDelay: null, willRetry: false, stopReason: RetryOutcome.Succeeded);

        string line = Formatting.FormatAttempt(info, useColor: true);

        // ANSI SGR 32 = green.
        Assert.Contains("\x1b[32m", line);
        Assert.Contains("succeeded", line);
    }

    [Fact]
    public void FormatAttempt_WithColor_ClosesAllSequencesWithReset()
    {
        // Novel class N5-style check: a dropped Reset() would leak colour onto the shell prompt
        // after retry exits. Every opened SGR must have a corresponding close.
        var info = new AttemptInfo(1, 4, exitCode: 1,
            nextDelay: TimeSpan.FromSeconds(2), willRetry: true, stopReason: null);

        string line = Formatting.FormatAttempt(info, useColor: true);

        Assert.Contains("\x1b[0m", line);  // the reset sequence
        // Rough count check: there are 3 colours in the retry line (dim, red, yellow) plus the
        // attempt prefix's dim. Every opening SGR except the default closes with \x1b[0m, so we
        // expect multiple resets. Fewer than 3 is suspicious; require at least 2.
        int resetCount = CountOccurrences(line, "\x1b[0m");
        Assert.True(resetCount >= 2, $"expected at least 2 Reset sequences; got {resetCount} in: {line.Replace("\x1b", "ESC")}");
    }

    [Fact]
    public void FormatAttempt_LaunchFailed_ShowsLaunchFailedText()
    {
        // Round-1 C2: LaunchFailed is a new outcome that needs its own progress-line frame.
        var info = new AttemptInfo(2, 4, exitCode: 127,
            nextDelay: null, willRetry: false, stopReason: RetryOutcome.LaunchFailed);

        string line = Formatting.FormatAttempt(info, useColor: false);

        Assert.Contains("attempt 2/4", line);
        Assert.Contains("launch failed", line);
        Assert.Contains("stopping", line);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
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

    // --- Round-1 review additions: structural JSON parsing + LaunchFailed + OutcomeToReason ---

    [Fact]
    public void FormatJson_Succeeded_HasValidStructuralShape()
    {
        // Round-1 I4: previous tests used `Contains` which would pass on malformed JSON (missing
        // closing brace, wrong field types). Parse structurally and verify every field's kind.
        var result = new RetryResult(
            attempts: 3, maxAttempts: 4, childExitCode: 0,
            outcome: RetryOutcome.Succeeded,
            totalTime: TimeSpan.FromSeconds(6.5),
            delays: new List<TimeSpan> { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2) });

        string json = Formatting.FormatJson(result, "retry", "0.3.0");

        System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
        System.Text.Json.JsonElement root = doc.RootElement;
        Assert.Equal(System.Text.Json.JsonValueKind.Object, root.ValueKind);
        Assert.Equal("retry", root.GetProperty("tool").GetString());
        Assert.Equal("0.3.0", root.GetProperty("version").GetString());
        Assert.Equal(0, root.GetProperty("exit_code").GetInt32());
        Assert.Equal("succeeded", root.GetProperty("exit_reason").GetString());
        Assert.Equal(0, root.GetProperty("child_exit_code").GetInt32());
        Assert.Equal(3, root.GetProperty("attempts").GetInt32());
        Assert.Equal(4, root.GetProperty("max_attempts").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Number, root.GetProperty("total_seconds").ValueKind);
        System.Text.Json.JsonElement delays = root.GetProperty("delays_seconds");
        Assert.Equal(System.Text.Json.JsonValueKind.Array, delays.ValueKind);
        Assert.Equal(2, delays.GetArrayLength());
        foreach (var el in delays.EnumerateArray())
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Number, el.ValueKind);
        }
    }

    [Fact]
    public void FormatJson_LaunchFailed_EmitsNullChildExitCode()
    {
        // Round-1 C2: on LaunchFailed the child never ran, so child_exit_code must be null in
        // the JSON envelope (not the classification code 127 — that lives in exit_code). Prior
        // to the fix FormatJson emitted child_exit_code equal to the classification, which
        // misled consumers into thinking the child exited with 127.
        var ex = new Yort.ShellKit.CommandNotFoundException("no-such-binary");
        var result = new RetryResult(
            attempts: 2, maxAttempts: 4, childExitCode: 127,
            outcome: RetryOutcome.LaunchFailed,
            totalTime: TimeSpan.FromSeconds(1),
            delays: new List<TimeSpan> { TimeSpan.FromSeconds(1) },
            launchError: ex);

        string json = Formatting.FormatJson(result, "retry", "0.3.0");

        System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
        System.Text.Json.JsonElement root = doc.RootElement;
        Assert.Equal(127, root.GetProperty("exit_code").GetInt32());
        Assert.Equal("launch_failed", root.GetProperty("exit_reason").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty("child_exit_code").ValueKind);
        Assert.Equal(2, root.GetProperty("attempts").GetInt32());  // partial history preserved
    }

    [Fact]
    public void FormatJson_UnknownOutcome_ThrowsNotSilentlyEmitsUnknown()
    {
        // Round-1 I4: OutcomeToReason previously returned "unknown" for unrecognised enum
        // values. Silent emission of "unknown" is indistinguishable from "retry has a bug" vs
        // "retry has been updated and this is a new outcome to handle". Now throws.
        var result = new RetryResult(
            attempts: 1, maxAttempts: 1, childExitCode: 0,
            outcome: (RetryOutcome)999,   // forced invalid value
            totalTime: TimeSpan.Zero,
            delays: Array.Empty<TimeSpan>());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Formatting.FormatJson(result, "retry", "0.3.0"));
    }
}
