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
        // Defensive: blank lines, lines with too few columns, and lines with NO numeric
        // column at all must be skipped without throwing. Pin three concrete malformed
        // forms because each goes through a different early-continue branch.
        //
        // Round-2 contract update 2026-05-08: post-multi-word-command fix (TA I1) the
        // parser scans for the first numeric column rather than anchoring on cols[1].
        // The original test row "noisy notanumber troy 4r REG 8,1 1024 12345 /tmp"
        // contained "1024" and "12345" as legitimate numeric columns, so the new
        // parser would extract PID=1024 from it. The replacement row below contains no
        // numeric tokens anywhere, exercising the "no PID column found" skip branch.
        const string output =
            "COMMAND PID USER FD TYPE DEVICE SIZE/OFF NODE NAME\n" +
            "\n" +                                                  // blank line — skipped
            "onecol\n" +                                            // <2 columns — skipped
            "alpha beta gamma delta epsilon zeta eta theta\n" +     // no numeric column — skipped
            "good 9999 troy 7u REG x,y abcd ef /tmp/file.txt";

        var results = LsofFinder.ParseLsofOutput(output, "/tmp/file.txt");

        Assert.Single(results);
        Assert.Equal(9999, results[0].ProcessId);
        Assert.Equal("good", results[0].ProcessName);
    }

    [Fact]
    public void ParseLsofOutput_MultiWordCommandName_PreservesFullName()
    {
        // Round-2 fresh-eyes 2026-05-08 test-analyzer I1: macOS lsof can emit multi-word
        // command names like "Google Chrome" — they push the PID column index past 1.
        // Pre-fix the parser anchored on cols[1] for the PID, so int.TryParse failed on
        // "Chrome" and the row was silently dropped — producing an empty results list
        // for any browser/IDE/launchd-managed process holding a file. The fix anchors
        // on the first numeric column and joins everything before it as the command.
        const string output =
            "COMMAND   PID USER FD TYPE DEVICE SIZE/OFF NODE NAME\n" +
            "Google Chrome 4242 troy 19u IPv4 0t0 TCP localhost:8080 (LISTEN)";

        var results = LsofFinder.ParseLsofOutput(output, "TCP :8080");

        Assert.Single(results);
        Assert.Equal(4242, results[0].ProcessId);
        Assert.Equal("Google Chrome", results[0].ProcessName);
    }

    [Fact]
    public void ParseLsofOutput_ThreeWordCommandName_PreservesFullName()
    {
        // Defensive: arbitrary leading-token count. Pin an extreme case so a future
        // regression to cols[2] anchoring would also fail this test.
        const string output =
            "COMMAND PID USER FD TYPE DEVICE SIZE/OFF NODE NAME\n" +
            "Microsoft SQL Server 7777 sql 4u REG 8,1 1024 12345 /tmp/db.lock";

        var results = LsofFinder.ParseLsofOutput(output, "/tmp/db.lock");

        Assert.Single(results);
        Assert.Equal(7777, results[0].ProcessId);
        Assert.Equal("Microsoft SQL Server", results[0].ProcessName);
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
