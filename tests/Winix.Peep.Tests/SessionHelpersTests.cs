using System.Text.RegularExpressions;
using Winix.Peep;
using Xunit;

namespace Winix.Peep.Tests;

/// <summary>
/// Tests for the pure decision helpers extracted in round 4 (TA I3/I4/I6/I7). Each
/// helper was previously an inline lambda or branch-arm body inside InteractiveSession
/// or Program — extraction makes round-1 / round-2 / round-3 contract fixes
/// regression-pinnable without driving a full event loop.
/// </summary>
public class WarnOnceForRegexTimeoutTests
{
    [Fact]
    public void FirstCall_WritesOnceForPattern()
    {
        var warned = new HashSet<Regex>();
        using var writer = new StringWriter();
        var regex = new Regex("foo");

        SessionHelpers.WarnOnceForRegexTimeout(regex, warned, writer);

        Assert.Contains("--exit-on-match pattern 'foo' timed out", writer.ToString());
        Assert.Single(warned);
    }

    [Fact]
    public void SecondCallWithSamePattern_IsSilent()
    {
        var warned = new HashSet<Regex>();
        using var writer = new StringWriter();
        var regex = new Regex("foo");

        SessionHelpers.WarnOnceForRegexTimeout(regex, warned, writer);
        long firstLength = writer.GetStringBuilder().Length;
        SessionHelpers.WarnOnceForRegexTimeout(regex, warned, writer);

        // No additional bytes written on the second invocation.
        Assert.Equal(firstLength, writer.GetStringBuilder().Length);
    }

    [Fact]
    public void DistinctPatterns_EachWarnOnce()
    {
        var warned = new HashSet<Regex>();
        using var writer = new StringWriter();
        var a = new Regex("foo");
        var b = new Regex("bar");

        SessionHelpers.WarnOnceForRegexTimeout(a, warned, writer);
        SessionHelpers.WarnOnceForRegexTimeout(b, warned, writer);
        SessionHelpers.WarnOnceForRegexTimeout(a, warned, writer);  // dup — silent
        SessionHelpers.WarnOnceForRegexTimeout(b, warned, writer);  // dup — silent

        string output = writer.ToString();
        // Two distinct patterns, two warnings, no duplicates.
        Assert.Equal(2, Regex.Matches(output, "timed out").Count);
        Assert.Equal(2, warned.Count);
    }

    [Fact]
    public void WriterThrows_DoesNotPropagate()
    {
        // Diagnostic must be strictly weaker than production: a failing writer must
        // not crash the watch loop. Verifies the inner try/catch in the helper.
        var warned = new HashSet<Regex>();
        var regex = new Regex("foo");
        var throwingWriter = new ThrowingTextWriter();

        // No exception expected; helper swallows.
        SessionHelpers.WarnOnceForRegexTimeout(regex, warned, throwingWriter);

        // Despite the writer throw, the pattern WAS marked as warned (Add returned true
        // before the write). A regression that flipped the order to "write first, mark
        // after" could double-warn under writer flakiness.
        Assert.Single(warned);
    }

    private sealed class ThrowingTextWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(string? value) => throw new IOException("simulated failure");
        public override void WriteLine(string? value) => throw new IOException("simulated failure");
    }
}

public class TryGetAutoExitTests
{
    private static SessionConfig Cfg(
        bool exitOnSuccess = false, bool exitOnError = false,
        bool exitOnChange = false, Regex[]? matches = null) =>
        new SessionConfig(
            Command: "cmd", CommandArgs: Array.Empty<string>(), CommandDisplay: "cmd",
            IntervalSeconds: 2, UseInterval: true, WatchPatterns: Array.Empty<string>(),
            DebounceMs: 300, HistoryCapacity: 1000, NoGitIgnore: false,
            ExitOnChange: exitOnChange, ExitOnSuccess: exitOnSuccess, ExitOnError: exitOnError,
            ExitOnMatchRegexes: matches ?? Array.Empty<Regex>(),
            DiffEnabled: false, NoHeader: false, JsonOutput: false,
            JsonOutputIncludeOutput: false, UseColor: false, Version: "0.1.0");

    private static PeepResult Result(int exitCode, string output) =>
        new PeepResult(output, exitCode, TimeSpan.FromSeconds(0.1), TriggerSource.Initial);

    [Fact]
    public void NullLastResult_ReturnsFalseManual()
    {
        bool exit = SessionHelpers.TryGetAutoExit(
            Cfg(exitOnSuccess: true), null, null,
            new HashSet<Regex>(), TextWriter.Null, out string reason);

        Assert.False(exit);
        Assert.Equal("manual", reason);
    }

    [Fact]
    public void ExitOnSuccess_ChildExit0_FiresWithReason()
    {
        bool exit = SessionHelpers.TryGetAutoExit(
            Cfg(exitOnSuccess: true), Result(0, "ok"), null,
            new HashSet<Regex>(), TextWriter.Null, out string reason);

        Assert.True(exit);
        Assert.Equal("exit_on_success", reason);
    }

    [Fact]
    public void ExitOnSuccess_ChildExitNonZero_DoesNotFire()
    {
        bool exit = SessionHelpers.TryGetAutoExit(
            Cfg(exitOnSuccess: true), Result(1, "fail"), null,
            new HashSet<Regex>(), TextWriter.Null, out _);

        Assert.False(exit);
    }

    [Fact]
    public void ExitOnError_ChildExitNonZero_FiresWithReason()
    {
        bool exit = SessionHelpers.TryGetAutoExit(
            Cfg(exitOnError: true), Result(1, "fail"), null,
            new HashSet<Regex>(), TextWriter.Null, out string reason);

        Assert.True(exit);
        Assert.Equal("exit_on_error", reason);
    }

    [Fact]
    public void ExitOnChange_DifferentOutput_Fires()
    {
        bool exit = SessionHelpers.TryGetAutoExit(
            Cfg(exitOnChange: true), Result(0, "new output"), prevOutput: "old output",
            new HashSet<Regex>(), TextWriter.Null, out string reason);

        Assert.True(exit);
        Assert.Equal("exit_on_change", reason);
    }

    [Fact]
    public void ExitOnChange_NullPrevOutput_DoesNotFire()
    {
        // The initial run (prevOutput=null) must not fire exit_on_change — there's
        // nothing to compare against. A regression that promoted null-vs-output to
        // "different" would silently exit on round 1.
        bool exit = SessionHelpers.TryGetAutoExit(
            Cfg(exitOnChange: true), Result(0, "anything"), prevOutput: null,
            new HashSet<Regex>(), TextWriter.Null, out _);

        Assert.False(exit);
    }

    [Fact]
    public void ExitOnChange_IdenticalOutput_DoesNotFire()
    {
        bool exit = SessionHelpers.TryGetAutoExit(
            Cfg(exitOnChange: true), Result(0, "same"), prevOutput: "same",
            new HashSet<Regex>(), TextWriter.Null, out _);

        Assert.False(exit);
    }

    [Fact]
    public void ExitOnMatch_PatternMatchesStrippedOutput_Fires()
    {
        // R3 CR I3 contract: --exit-on-match runs against StripAnsi'd output. Pin the
        // round-trip — an OSC-prefixed line still matches "READY" because OSC is stripped.
        var matches = new[] { new Regex("READY") };
        // OSC window-title escape followed by the matchable content.
        string output = "\x1b]0;build\x1b\\READY";

        bool exit = SessionHelpers.TryGetAutoExit(
            Cfg(matches: matches), Result(0, output), null,
            new HashSet<Regex>(), TextWriter.Null, out string reason);

        Assert.True(exit);
        Assert.Equal("exit_on_match", reason);
    }

    [Fact]
    public void ExitOnMatch_NoMatch_DoesNotFire()
    {
        var matches = new[] { new Regex("NEVER") };

        bool exit = SessionHelpers.TryGetAutoExit(
            Cfg(matches: matches), Result(0, "nothing here"), null,
            new HashSet<Regex>(), TextWriter.Null, out _);

        Assert.False(exit);
    }

    [Fact]
    public void ExitOnMatch_RegexTimeout_TreatsAsNonMatchAndWarnsOnce()
    {
        // Construct a regex with an aggressively short timeout against catastrophic-
        // backtracking input — guaranteed to throw RegexMatchTimeoutException. The
        // helper must swallow, treat as non-match, and emit a one-shot warning.
        var slowRegex = new Regex("(a+)+b", RegexOptions.None, TimeSpan.FromMilliseconds(1));
        string pathological = new string('a', 30);  // no 'b' — catastrophic backtracking
        var warned = new HashSet<Regex>();
        using var writer = new StringWriter();

        bool exit = SessionHelpers.TryGetAutoExit(
            Cfg(matches: new[] { slowRegex }), Result(0, pathological), null,
            warned, writer, out string reason);

        Assert.False(exit);
        Assert.Equal("manual", reason);
        Assert.Contains("timed out", writer.ToString());

        // Second invocation against the same pattern: no additional warning.
        long firstLen = writer.GetStringBuilder().Length;
        SessionHelpers.TryGetAutoExit(
            Cfg(matches: new[] { slowRegex }), Result(0, pathological), null,
            warned, writer, out _);
        Assert.Equal(firstLen, writer.GetStringBuilder().Length);
    }
}

public class ResolveExitCodeTests
{
    // R2 CR I2 regression pin: each of the three reasons in the success-class must
    // override the child exit code with 0. A future refactor that drops any of them
    // (e.g. accidentally listing only exit_on_change and exit_on_success) would
    // silently regress the "0 = Auto-exit condition met" README contract.

    [Theory]
    [InlineData("exit_on_change")]
    [InlineData("exit_on_success")]
    [InlineData("exit_on_match")]
    public void SuccessClassReason_OverridesChildExitTo0(string reason)
    {
        int exit = SessionHelpers.ResolveExitCode(reason, lastChildExit: 1, failedFallback: 0);
        Assert.Equal(0, exit);
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("exit_on_error")]
    [InlineData("interrupted")]
    public void NonSuccessReason_PassesThroughChildExit(string reason)
    {
        int exit = SessionHelpers.ResolveExitCode(reason, lastChildExit: 7, failedFallback: 0);
        Assert.Equal(7, exit);
    }

    [Fact]
    public void NullChildExit_UsesFallback()
    {
        // Initial run failed to start (command_not_found / not_executable) — there is
        // no child exit code, so Program supplies failedFallback (e.g. 127).
        int exit = SessionHelpers.ResolveExitCode("manual", lastChildExit: null, failedFallback: 127);
        Assert.Equal(127, exit);
    }

    [Fact]
    public void NullChildExit_SuccessClassReason_StillReturns0()
    {
        // Edge case: --exit-on-success fires with a missing child exit (impossible in
        // practice but pin the contract). Override applies regardless of child state.
        int exit = SessionHelpers.ResolveExitCode("exit_on_success", lastChildExit: null, failedFallback: 127);
        Assert.Equal(0, exit);
    }
}

public class RequestCancellationSilentlyTests
{
    [Fact]
    public void LiveCts_IsCancelled()
    {
        var cts = new CancellationTokenSource();

        SessionHelpers.RequestCancellationSilently(cts);

        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void DisposedCts_DoesNotThrow()
    {
        // R3 CR I6 regression pin: in-flight Ctrl+C handler racing with finally-block
        // shutdown can call Cancel() on a disposed CTS. Helper must swallow.
        var cts = new CancellationTokenSource();
        cts.Dispose();

        // No exception expected.
        SessionHelpers.RequestCancellationSilently(cts);
    }

    [Fact]
    public void DoubleInvocation_OnSameLiveCts_IsIdempotent()
    {
        var cts = new CancellationTokenSource();
        SessionHelpers.RequestCancellationSilently(cts);
        SessionHelpers.RequestCancellationSilently(cts);  // already cancelled — no-op
        Assert.True(cts.IsCancellationRequested);
    }
}

public class ShouldDispatchTests
{
    // R2 CR R2-C1 regression pin: when a child run is in flight (running=true) the
    // file-change flag must NOT be consumed by the dispatch predicate — otherwise a
    // long-running child silently swallows file-change triggers because the next
    // idle iteration sees flag=0.

    [Fact]
    public void Running_DoesNotConsumeFileChangeFlag()
    {
        int flag = 1;
        bool dispatch = SessionHelpers.ShouldDispatch(
            running: true, useInterval: true, ref flag,
            now: DateTime.UtcNow, nextRunTime: DateTime.UtcNow.AddSeconds(-1),
            out _);

        Assert.False(dispatch);
        Assert.Equal(1, flag);  // CRITICAL: flag preserved for next idle iteration
    }

    [Fact]
    public void NotRunning_FileChangeFlagSet_ConsumesAndDispatchesFileChange()
    {
        int flag = 1;
        bool dispatch = SessionHelpers.ShouldDispatch(
            running: false, useInterval: true, ref flag,
            now: DateTime.UtcNow, nextRunTime: DateTime.UtcNow.AddSeconds(60),
            out TriggerSource trigger);

        Assert.True(dispatch);
        Assert.Equal(TriggerSource.FileChange, trigger);
        Assert.Equal(0, flag);  // flag consumed
    }

    [Fact]
    public void NotRunning_NoFileChange_IntervalDue_DispatchesInterval()
    {
        int flag = 0;
        bool dispatch = SessionHelpers.ShouldDispatch(
            running: false, useInterval: true, ref flag,
            now: DateTime.UtcNow, nextRunTime: DateTime.UtcNow.AddSeconds(-1),
            out TriggerSource trigger);

        Assert.True(dispatch);
        Assert.Equal(TriggerSource.Interval, trigger);
    }

    [Fact]
    public void NotRunning_NoFileChange_IntervalNotDue_DoesNotDispatch()
    {
        int flag = 0;
        bool dispatch = SessionHelpers.ShouldDispatch(
            running: false, useInterval: true, ref flag,
            now: DateTime.UtcNow, nextRunTime: DateTime.UtcNow.AddSeconds(10),
            out _);

        Assert.False(dispatch);
    }

    [Fact]
    public void NotRunning_NoFileChange_IntervalDisabled_DoesNotDispatch()
    {
        int flag = 0;
        bool dispatch = SessionHelpers.ShouldDispatch(
            running: false, useInterval: false, ref flag,
            now: DateTime.UtcNow, nextRunTime: DateTime.UtcNow.AddSeconds(-1),
            out _);

        Assert.False(dispatch);
    }

    [Fact]
    public void FileChangePreemptsInterval_WhenBothEligible()
    {
        // Both flag=1 AND interval-due. File-change wins (consumed); interval is
        // implicitly delayed to the next iteration. This matches the watch-mode UX
        // expectation: a file save should fire immediately rather than waiting for
        // the next interval boundary.
        int flag = 1;
        bool dispatch = SessionHelpers.ShouldDispatch(
            running: false, useInterval: true, ref flag,
            now: DateTime.UtcNow, nextRunTime: DateTime.UtcNow.AddSeconds(-1),
            out TriggerSource trigger);

        Assert.True(dispatch);
        Assert.Equal(TriggerSource.FileChange, trigger);
        Assert.Equal(0, flag);
    }
}
