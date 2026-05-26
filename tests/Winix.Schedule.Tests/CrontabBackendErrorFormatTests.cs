#nullable enable

using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R4 contract pins for the CrontabBackend error-message formatters extracted as part of
/// the I7 seam-extraction pass. Pre-fix, these messages were inline interpolations inside
/// Process-spawning methods (Run, WriteCrontab) — a regression that shape-shifted them
/// (dropped task name, lost the trailing colon fix, etc.) would have shipped without any
/// test seeing it. Each helper is now a pure function over its inputs.
/// </summary>
public sealed class CrontabBackendErrorFormatTests
{
    [Fact]
    public void FormatRunFailureNullProcess_IncludesTaskName()
    {
        Assert.Equal(
            "Failed to run task 'myjob': Process.Start returned null for /bin/sh.",
            CrontabBackend.FormatRunFailureNullProcess("myjob"));
    }

    [Fact]
    public void FormatRunFailureShExit_IncludesNameAndExitCode()
    {
        Assert.Equal(
            "Failed to run task 'myjob': /bin/sh exited with code 127.",
            CrontabBackend.FormatRunFailureShExit("myjob", 127));
    }

    [Fact]
    public void FormatRunFailureShUnavailable_IncludesNameAndUnderlyingMessage()
    {
        Assert.Equal(
            "Failed to run task 'myjob': /bin/sh not available (No such file or directory).",
            CrontabBackend.FormatRunFailureShUnavailable("myjob", "No such file or directory"));
    }

    [Fact]
    public void FormatRunFailureGeneric_IncludesNameAndReason()
    {
        Assert.Equal(
            "Failed to run task 'myjob': Process disposed during start.",
            CrontabBackend.FormatRunFailureGeneric("myjob", "Process disposed during start."));
    }

    [Fact]
    public void FormatWriteTimeout_RendersInSeconds()
    {
        Assert.Equal(
            "crontab did not respond within 30s.",
            CrontabBackend.FormatWriteTimeout(30_000));
    }

    [Fact]
    public void FormatWriteFailure_NonEmptyStderr_TrimmedAndIncluded()
    {
        // Trailing newline / whitespace must not leak into the user-visible message.
        Assert.Equal(
            "crontab failed (exit 1): permission denied",
            CrontabBackend.FormatWriteFailure(1, "  permission denied  \n"));
    }

    [Fact]
    public void FormatWriteFailure_EmptyStderr_SubstitutesPlaceholder()
    {
        // The R3 fix that replaced an empty trailing-colon message with explicit "no
        // stderr output" — a regression that flattens this back would re-introduce the
        // confusing user-visible output.
        Assert.Equal(
            "crontab failed (exit 1): no stderr output",
            CrontabBackend.FormatWriteFailure(1, ""));
    }

    [Fact]
    public void FormatWriteFailure_NullStderr_SubstitutesPlaceholder()
    {
        Assert.Equal(
            "crontab failed (exit 5): no stderr output",
            CrontabBackend.FormatWriteFailure(5, null));
    }

    [Fact]
    public void FormatWriteFailure_WhitespaceOnlyStderr_SubstitutesPlaceholder()
    {
        Assert.Equal(
            "crontab failed (exit 1): no stderr output",
            CrontabBackend.FormatWriteFailure(1, "   \n  \t  "));
    }

    [Theory]
    [InlineData("a&b")]
    [InlineData("name with 'quote'")]
    [InlineData("name\\with\\backslash")]
    public void FormatRunFailures_EmbedNameVerbatim_NoExtraQuoting(string name)
    {
        // The name passes through unchanged — no double-escape, no truncation. Pre-R4
        // any name containing ' or \ could have been mangled by a future regression in
        // the message template; this pins the shape.
        string result = CrontabBackend.FormatRunFailureNullProcess(name);
        Assert.Contains($"'{name}'", result);
    }
}
