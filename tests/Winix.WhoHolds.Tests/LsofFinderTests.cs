#nullable enable

using System;
using Winix.WhoHolds;
using Xunit;

namespace Winix.WhoHolds.Tests;

/// <summary>
/// Tests for <see cref="LsofFinder"/>'s failure-routing layer using the internal
/// <see cref="LsofFinder.ProcessRunner"/> seam. Round-1 fresh-eyes 2026-05-08
/// test-analyzer C1-C3: pre-fix, the timeout / start-failure / stderr-on-nonzero
/// / stream-read-exception branches of <see cref="LsofFinder.InterpretLsofRun"/>
/// were all zero-coverage on Windows hosts because they required a real lsof
/// binary. Substituting <see cref="LsofFinder.ProcessRunner"/> with a fake makes
/// each branch deterministically reachable.
/// </summary>
/// <remarks>
/// <c>[Collection("LsofFinder.ProcessRunner")]</c> serialises this class with any
/// future test class that touches <see cref="LsofFinder.FindFile"/>,
/// <see cref="LsofFinder.FindPort"/>, or <see cref="LsofFinder.IsAvailable"/>. xUnit
/// runs different test classes in parallel by default; without the collection lock,
/// the process-wide static <see cref="LsofFinder.ProcessRunner"/> seam would race
/// across classes. Round-2 fresh-eyes 2026-05-08 — three reviewers (SFH R2-2, CR W4,
/// TA I2) converged on this risk.
/// </remarks>
[Collection("LsofFinder.ProcessRunner")]
public sealed class LsofFinderTests
{
    /// <summary>
    /// Saves and restores <see cref="LsofFinder.ProcessRunner"/> around the action so a
    /// failing test cannot leak its fake into subsequent tests in the same xUnit
    /// collection. Tests must not run in parallel against this seam (xUnit defaults to
    /// per-class collections, so this helper plus the implicit no-parallel-within-class
    /// rule is sufficient).
    /// </summary>
    private static void WithFakeRunner(Func<string, string[], LsofFinder.LsofRun> fake, Action body)
    {
        var saved = LsofFinder.ProcessRunner;
        LsofFinder.ProcessRunner = fake;
        try
        {
            body();
        }
        finally
        {
            LsofFinder.ProcessRunner = saved;
        }
    }

    [Fact]
    public void FindFile_LsofTimesOut_ReturnsFailedWithTimeoutReason()
    {
        WithFakeRunner(
            (_, _) => new LsofFinder.LsofRun(-1, string.Empty, string.Empty, TimedOut: true, StartError: null, ReadError: null),
            () =>
            {
                FindResult result = LsofFinder.FindFile("/tmp/whatever");

                Assert.True(result.QueryFailed);
                Assert.NotNull(result.Reason);
                Assert.Contains("timed out", result.Reason!, StringComparison.Ordinal);
                Assert.Contains("2000ms", result.Reason!, StringComparison.Ordinal);
                Assert.Empty(result.Results);
            });
    }

    [Fact]
    public void FindPort_LsofTimesOut_ReturnsFailedWithTimeoutReason()
    {
        WithFakeRunner(
            (_, _) => new LsofFinder.LsofRun(-1, string.Empty, string.Empty, TimedOut: true, StartError: null, ReadError: null),
            () =>
            {
                FindResult result = LsofFinder.FindPort(8080);

                Assert.True(result.QueryFailed);
                Assert.Contains("timed out", result.Reason!, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void IsAvailable_LsofTimesOut_ReturnsFalse()
    {
        WithFakeRunner(
            (_, _) => new LsofFinder.LsofRun(-1, string.Empty, string.Empty, TimedOut: true, StartError: null, ReadError: null),
            () =>
            {
                Assert.False(LsofFinder.IsAvailable());
            });
    }

    [Fact]
    public void FindFile_LsofFailsToStart_ReturnsFailedWithStartError()
    {
        WithFakeRunner(
            (_, _) => new LsofFinder.LsofRun(-1, string.Empty, string.Empty, TimedOut: false, StartError: "executable not found", ReadError: null),
            () =>
            {
                FindResult result = LsofFinder.FindFile("/tmp/whatever");

                Assert.True(result.QueryFailed);
                Assert.Contains("failed to start", result.Reason!, StringComparison.Ordinal);
                Assert.Contains("executable not found", result.Reason!, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void IsAvailable_LsofFailsToStart_ReturnsFalse()
    {
        WithFakeRunner(
            (_, _) => new LsofFinder.LsofRun(-1, string.Empty, string.Empty, TimedOut: false, StartError: "lsof: command not found", ReadError: null),
            () =>
            {
                Assert.False(LsofFinder.IsAvailable());
            });
    }

    [Fact]
    public void IsAvailable_LsofRunsAndReturnsZero_ReturnsTrue()
    {
        // Production lsof -v exits 0; any successful run with no StartError + not timed
        // out should be reported as available.
        WithFakeRunner(
            (_, _) => new LsofFinder.LsofRun(0, "lsof version 4.91", string.Empty, TimedOut: false, StartError: null, ReadError: null),
            () =>
            {
                Assert.True(LsofFinder.IsAvailable());
            });
    }

    [Fact]
    public void FindFile_LsofExitsNonZeroWithStderr_ReturnsFailed()
    {
        // The central "lsof exit 1 + stderr non-empty = real failure" rule. Pre-fix this
        // class was untested; the rule could regress to "exit 1 = success-empty" without
        // any test catching it.
        WithFakeRunner(
            (_, _) => new LsofFinder.LsofRun(1, string.Empty, "lsof: bad option -- z", TimedOut: false, StartError: null, ReadError: null),
            () =>
            {
                FindResult result = LsofFinder.FindFile("/tmp/whatever");

                Assert.True(result.QueryFailed);
                Assert.Contains("lsof:", result.Reason!, StringComparison.Ordinal);
                Assert.Contains("bad option", result.Reason!, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void FindFile_LsofExitsOneWithEmptyStderr_ReturnsSuccessEmpty()
    {
        // The sibling rule: lsof exits 1 with no output AND no stderr = "no matches
        // found" (the documented and very common case). Treat as success-empty. This
        // pins that the SFH F3 fix's stricter stream-read-error handling didn't
        // accidentally narrow the empty-success path.
        WithFakeRunner(
            (_, _) => new LsofFinder.LsofRun(1, string.Empty, string.Empty, TimedOut: false, StartError: null, ReadError: null),
            () =>
            {
                FindResult result = LsofFinder.FindFile("/tmp/whatever");

                Assert.False(result.QueryFailed);
                Assert.Empty(result.Results);
            });
    }

    [Fact]
    public void FindFile_LsofExitsZeroWithOutput_ReturnsSuccessParsedHolders()
    {
        // The happy-path: lsof exit 0 with parseable output produces a populated
        // Results list. End-to-end glue test for the FindFile → InterpretLsofRun →
        // ParseLsofOutput pipeline.
        const string lsofOutput =
            "COMMAND PID USER FD TYPE DEVICE SIZE/OFF NODE NAME\n" +
            "vim 4242 troy 4u REG 8,1 1024 12345 /tmp/whatever";

        WithFakeRunner(
            (_, _) => new LsofFinder.LsofRun(0, lsofOutput, string.Empty, TimedOut: false, StartError: null, ReadError: null),
            () =>
            {
                FindResult result = LsofFinder.FindFile("/tmp/whatever");

                Assert.False(result.QueryFailed);
                Assert.Single(result.Results);
                Assert.Equal(4242, result.Results[0].ProcessId);
                Assert.Equal("vim", result.Results[0].ProcessName);
                Assert.Equal("/tmp/whatever", result.Results[0].Resource);
            });
    }

    [Fact]
    public void FindFile_StreamReadException_ReturnsFailedNotSuccessEmpty()
    {
        // SFH F3 fresh-eyes: pre-fix, an exception during stdout/stderr capture was
        // silently swallowed and the empty captures fell through to the
        // "empty stdout = no matches" branch — re-introducing the SFH defect class
        // the FindResult work was meant to eliminate. With the ReadError field, the
        // exception is captured and InterpretLsofRun routes it to Failed.
        WithFakeRunner(
            (_, _) => new LsofFinder.LsofRun(0, string.Empty, string.Empty, TimedOut: false, StartError: null, ReadError: "ObjectDisposedException: Cannot access a closed stream."),
            () =>
            {
                FindResult result = LsofFinder.FindFile("/tmp/whatever");

                Assert.True(result.QueryFailed);
                Assert.Contains("output capture failed", result.Reason!, StringComparison.Ordinal);
                Assert.Contains("ObjectDisposedException", result.Reason!, StringComparison.Ordinal);
            });
    }
}
