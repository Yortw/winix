#nullable enable

using System;
using System.IO;
using Winix.WhoHolds;
using Xunit;

namespace Winix.WhoHolds.Tests;

/// <summary>
/// Tests for the library-seam <see cref="Cli.Run"/> entry point. These pin the
/// orchestration layer's exit-code routing, --json stream-routing, elevation-warning
/// emission, and target-not-found path without spawning a process. Round-1 fresh-eyes
/// 2026-05-08: addresses test-analyzer C1-C4 (zero coverage of QueryFailed → exit 1
/// routing) by injecting fake finders / elevation seams.
/// </summary>
public sealed class CliRunTests
{
    private static (StringWriter stdout, StringWriter stderr) Sinks()
    {
        return (new StringWriter(), new StringWriter());
    }

    // ── QueryFailed routing ────────────────────────────────────────────────────────

    [Fact]
    public void Run_FileFinderQueryFailed_ExitsOneWithStderrMessage()
    {
        var (stdout, stderr) = Sinks();
        string realFile = Path.GetTempFileName();
        try
        {
            int exit = Cli.Run(
                new[] { realFile },
                stdout,
                stderr,
                isStdoutRedirected: true,
                fileFinder: _ => FindResult.Failed("RmStartSession failed: hr=0x80004005"),
                portFinder: _ => FindResult.Empty,
                isElevated: () => true);

            Assert.Equal(1, exit);
            Assert.Contains("query failed for", stderr.ToString(), StringComparison.Ordinal);
            Assert.Contains("RmStartSession failed: hr=0x80004005", stderr.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, stdout.ToString());
        }
        finally
        {
            File.Delete(realFile);
        }
    }

    [Fact]
    public void Run_PortFinderQueryFailed_ExitsOneWithStderrMessage()
    {
        var (stdout, stderr) = Sinks();
        int exit = Cli.Run(
            new[] { ":8080" },
            stdout,
            stderr,
            isStdoutRedirected: true,
            fileFinder: _ => FindResult.Empty,
            portFinder: _ => FindResult.Failed("GetExtendedTcpTable failed: status=0x000000C2"),
            isElevated: () => true);

        Assert.Equal(1, exit);
        string err = stderr.ToString();
        Assert.Contains("query failed for ':8080'", err, StringComparison.Ordinal);
        Assert.Contains("GetExtendedTcpTable failed", err, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public void Run_QueryFailed_WithJson_RoutesEnvelopeToStdoutNotStderr()
    {
        var (stdout, stderr) = Sinks();
        string realFile = Path.GetTempFileName();
        try
        {
            int exit = Cli.Run(
                new[] { "--json", realFile },
                stdout,
                stderr,
                isStdoutRedirected: false,
                fileFinder: _ => FindResult.Failed("simulated backend failure"),
                portFinder: _ => FindResult.Empty,
                isElevated: () => true);

            Assert.Equal(1, exit);
            string outText = stdout.ToString();
            Assert.Contains("\"exit_code\":1", outText, StringComparison.Ordinal);
            Assert.Contains("\"exit_reason\":\"query_failed\"", outText, StringComparison.Ordinal);
            Assert.Contains("\"error\":\"simulated backend failure\"", outText, StringComparison.Ordinal);
            // The plain-text "whoholds: query failed for ..." must NOT appear on stderr
            // when --json is requested — JSON consumers get the structured envelope only.
            Assert.DoesNotContain("query failed for", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(realFile);
        }
    }

    // ── --json output stream routing (suite convention) ────────────────────────────

    [Fact]
    public void Run_JsonSuccess_RoutesEnvelopeToStdoutNotStderr()
    {
        var (stdout, stderr) = Sinks();
        string realFile = Path.GetTempFileName();
        try
        {
            int exit = Cli.Run(
                new[] { "--json", realFile },
                stdout,
                stderr,
                isStdoutRedirected: false,
                fileFinder: _ => FindResult.Empty,
                portFinder: _ => FindResult.Empty,
                isElevated: () => true);

            Assert.Equal(0, exit);
            string outText = stdout.ToString();
            // JSON envelope is on stdout per suite convention (man-F12, winix-F3 precedent).
            Assert.Contains("\"tool\":\"whoholds\"", outText, StringComparison.Ordinal);
            Assert.Contains("\"processes\":[]", outText, StringComparison.Ordinal);
            // stderr may contain the elevation warning depending on isElevated; assert that
            // it does NOT contain the JSON envelope so we don't regress to the old behaviour.
            Assert.DoesNotContain("\"tool\":\"whoholds\"", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(realFile);
        }
    }

    [Fact]
    public void Run_JsonOutputDoesNotEmitNoResultsMessageToStderr()
    {
        // Pre-fix the no-results "No processes found holding ..." message went to stderr
        // alongside the JSON envelope. After the JSON-to-stdout fix, the no-results text
        // is NOT emitted in JSON mode (the JSON envelope itself encodes the empty list).
        var (stdout, stderr) = Sinks();
        string realFile = Path.GetTempFileName();
        try
        {
            Cli.Run(
                new[] { "--json", realFile },
                stdout,
                stderr,
                isStdoutRedirected: false,
                fileFinder: _ => FindResult.Empty,
                portFinder: _ => FindResult.Empty,
                isElevated: () => true);

            Assert.DoesNotContain("No processes found holding", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(realFile);
        }
    }

    // ── Elevation-warning routing ──────────────────────────────────────────────────

    [Fact]
    public void Run_NotElevated_EmitsElevationWarningToStderr()
    {
        var (stdout, stderr) = Sinks();
        string realFile = Path.GetTempFileName();
        try
        {
            Cli.Run(
                new[] { realFile },
                stdout,
                stderr,
                isStdoutRedirected: true,
                fileFinder: _ => FindResult.Empty,
                portFinder: _ => FindResult.Empty,
                isElevated: () => false);

            Assert.Contains("Not elevated", stderr.ToString(), StringComparison.Ordinal);
            Assert.Contains("only showing current user", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(realFile);
        }
    }

    [Fact]
    public void Run_Elevated_OmitsElevationWarning()
    {
        var (stdout, stderr) = Sinks();
        string realFile = Path.GetTempFileName();
        try
        {
            Cli.Run(
                new[] { realFile },
                stdout,
                stderr,
                isStdoutRedirected: true,
                fileFinder: _ => FindResult.Empty,
                portFinder: _ => FindResult.Empty,
                isElevated: () => true);

            Assert.DoesNotContain("Not elevated", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(realFile);
        }
    }

    // ── target-not-found exit-1 ────────────────────────────────────────────────────

    [Fact]
    public void Run_TargetNotFound_ExitsOneWithStderrMessage()
    {
        var (stdout, stderr) = Sinks();
        int exit = Cli.Run(
            new[] { "/does/not/exist/probably.bin" },
            stdout,
            stderr,
            isStdoutRedirected: true,
            fileFinder: _ => FindResult.Empty,
            portFinder: _ => FindResult.Empty,
            isElevated: () => true);

        Assert.Equal(1, exit);
        Assert.Contains("target not found", stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public void Run_TargetNotFound_WithJson_RoutesEnvelopeToStdout()
    {
        var (stdout, stderr) = Sinks();
        int exit = Cli.Run(
            new[] { "--json", "/does/not/exist/probably.bin" },
            stdout,
            stderr,
            isStdoutRedirected: true,
            fileFinder: _ => FindResult.Empty,
            portFinder: _ => FindResult.Empty,
            isElevated: () => true);

        Assert.Equal(1, exit);
        string outText = stdout.ToString();
        Assert.Contains("\"exit_reason\":\"target_not_found\"", outText, StringComparison.Ordinal);
        Assert.Contains("\"error\":", outText, StringComparison.Ordinal);
    }

    // ── Usage / parse errors ───────────────────────────────────────────────────────

    [Fact]
    public void Run_NoPositional_ExitsUsageError125()
    {
        var (stdout, stderr) = Sinks();
        int exit = Cli.Run(
            Array.Empty<string>(),
            stdout,
            stderr,
            isStdoutRedirected: true,
            fileFinder: _ => FindResult.Empty,
            portFinder: _ => FindResult.Empty,
            isElevated: () => true);

        Assert.Equal(125, exit);
        Assert.Contains("expected exactly one argument", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_InvalidPort_ExitsUsageError125()
    {
        var (stdout, stderr) = Sinks();
        int exit = Cli.Run(
            new[] { ":99999" },
            stdout,
            stderr,
            isStdoutRedirected: true,
            fileFinder: _ => FindResult.Empty,
            portFinder: _ => FindResult.Empty,
            isElevated: () => true);

        Assert.Equal(125, exit);
        Assert.Contains("Invalid port", stderr.ToString(), StringComparison.Ordinal);
    }

    // ── --pid-only auto-detection ──────────────────────────────────────────────────

    [Fact]
    public void Run_StdoutRedirected_AutoEnablesPidOnly()
    {
        // Inject a finder that returns one holder; with isStdoutRedirected=true the auto
        // --pid-only branch fires, producing just "1234\n" on stdout (no table headers).
        var (stdout, stderr) = Sinks();
        string realFile = Path.GetTempFileName();
        try
        {
            Cli.Run(
                new[] { realFile },
                stdout,
                stderr,
                isStdoutRedirected: true,
                fileFinder: _ => FindResult.Success(new[] { new LockInfo(1234, "fake.exe", realFile) }),
                portFinder: _ => FindResult.Empty,
                isElevated: () => true);

            string outText = stdout.ToString();
            Assert.Contains("1234", outText, StringComparison.Ordinal);
            Assert.DoesNotContain("PID", outText, StringComparison.Ordinal); // table header would include "PID"
        }
        finally
        {
            File.Delete(realFile);
        }
    }
}
