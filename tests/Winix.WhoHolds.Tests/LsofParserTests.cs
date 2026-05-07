#nullable enable

using Winix.WhoHolds;
using Xunit;

namespace Winix.WhoHolds.Tests;

/// <summary>
/// Fixture tests for <see cref="LsofFinder.ParseLsofOutput"/>. The Linux/macOS code path
/// was previously zero-coverage on Windows hosts because RunProcess required a real lsof
/// binary. Round-1 review 2026-05-08 test-analyzer I2 surfaced this gap; ParseLsofOutput
/// is now internal so it can be exercised without delegation.
/// </summary>
public sealed class LsofParserTests
{
    [Fact]
    public void ParseLsofOutput_SkipsHeaderLine()
    {
        // Real lsof output: first row is a column header. The parser must skip it,
        // otherwise "PID" would be passed to int.TryParse, which fails — but a
        // regression that disabled header skip would silently ship empty results.
        const string output =
            "COMMAND PID USER FD TYPE DEVICE SIZE/OFF NODE NAME\n" +
            "node 1234 troy 19u IPv4 0t0 TCP localhost:8080 (LISTEN)";

        var results = LsofFinder.ParseLsofOutput(output, "TCP :8080");

        Assert.Single(results);
        Assert.Equal(1234, results[0].ProcessId);
        Assert.Equal("node", results[0].ProcessName);
        Assert.Equal("TCP :8080", results[0].Resource);
    }

    [Fact]
    public void ParseLsofOutput_DeduplicatesByPid()
    {
        // A process holding multiple file descriptors on the same resource produces
        // multiple rows with the same PID. Pin that we collapse them, otherwise the
        // human-readable table and PID-only output would repeat the same row.
        const string output =
            "COMMAND PID USER FD TYPE DEVICE SIZE/OFF NODE NAME\n" +
            "vim 5678 troy 4r REG 8,1 1024 12345 /tmp/file.txt\n" +
            "vim 5678 troy 5w REG 8,1 1024 12345 /tmp/file.txt\n" +
            "vim 5678 troy 6u REG 8,1 1024 12345 /tmp/file.txt";

        var results = LsofFinder.ParseLsofOutput(output, "/tmp/file.txt");

        Assert.Single(results);
        Assert.Equal(5678, results[0].ProcessId);
    }

    [Fact]
    public void ParseLsofOutput_SkipsMalformedLines()
    {
        // Defensive: blank lines, lines with too few columns, and lines whose PID column
        // is non-numeric must be skipped without throwing. Pin three concrete malformed
        // forms because each goes through a different early-continue branch.
        const string output =
            "COMMAND PID USER FD TYPE DEVICE SIZE/OFF NODE NAME\n" +
            "\n" +                                  // blank line — skipped
            "onecol\n" +                            // <2 columns — skipped
            "noisy notanumber troy 4r REG 8,1 1024 12345 /tmp\n" +  // PID not numeric — skipped
            "good 9999 troy 7u REG 8,1 1024 12345 /tmp/file.txt";

        var results = LsofFinder.ParseLsofOutput(output, "/tmp/file.txt");

        Assert.Single(results);
        Assert.Equal(9999, results[0].ProcessId);
        Assert.Equal("good", results[0].ProcessName);
    }

    [Fact]
    public void ParseLsofOutput_BlankInput_ReturnsEmpty()
    {
        // The InterpretLsofRun caller short-circuits empty stdout BEFORE this method
        // runs, but pin the parser's own contract too — a future caller change must
        // not produce phantom rows from whitespace-only input.
        Assert.Empty(LsofFinder.ParseLsofOutput("", "anything"));
        Assert.Empty(LsofFinder.ParseLsofOutput("   \n  \n  ", "anything"));
        Assert.Empty(LsofFinder.ParseLsofOutput(
            "COMMAND PID USER FD TYPE DEVICE SIZE/OFF NODE NAME",
            "anything")); // header only, no data rows
    }
}
