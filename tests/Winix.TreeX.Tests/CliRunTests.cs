#nullable enable

using System;
using System.IO;
using Winix.TreeX;
using Xunit;

namespace Winix.TreeX.Tests;

/// <summary>
/// Tests for the library-seam <see cref="Cli.Run"/> entry point. Pin orchestration-layer
/// exit-code routing, --json envelope routing, error messages, and the CR I1 totalSize
/// bug fix without spawning a process. Round-1 fresh-eyes 2026-05-09: addresses
/// test-analyzer C2-C3 (zero coverage of dispatch layer).
/// </summary>
public sealed class CliRunTests : IDisposable
{
    private readonly string _tempDir;

    public CliRunTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "treex-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; tmpdir may be locked under Defender scan etc.
        }
    }

    private static (StringWriter stdout, StringWriter stderr) Sinks()
    {
        return (new StringWriter(), new StringWriter());
    }

    // ── Path-not-found / not-a-directory exit-1 routing ───────────────────────────

    [Fact]
    public void Run_PathNotFound_Returns1AndStderrMessage()
    {
        var (stdout, stderr) = Sinks();
        string missingPath = Path.Combine(_tempDir, "does-not-exist");

        int exit = Cli.Run(new[] { missingPath }, stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(1, exit);
        Assert.Contains("path not found", stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public void Run_PathIsFile_Returns1AndStderrNotADirectory()
    {
        var (stdout, stderr) = Sinks();
        string filePath = Path.Combine(_tempDir, "regular-file.txt");
        File.WriteAllText(filePath, "content");

        int exit = Cli.Run(new[] { filePath }, stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(1, exit);
        Assert.Contains("not a directory", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("path not found", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_PathNotFound_WithJson_RoutesEnvelopeToStdout()
    {
        var (stdout, stderr) = Sinks();
        string missingPath = Path.Combine(_tempDir, "does-not-exist");

        int exit = Cli.Run(new[] { "--json", missingPath }, stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(1, exit);
        string outText = stdout.ToString();
        Assert.Contains("\"exit_code\":1", outText, StringComparison.Ordinal);
        Assert.Contains("\"exit_reason\":\"path_not_found\"", outText, StringComparison.Ordinal);
        // Round-2 fresh-eyes 2026-05-09 code-reviewer I1: the human-readable error
        // detail must travel via the documented `error` field on the envelope. Without
        // this field, JSON consumers see only the machine code with no detail — defeats
        // the purpose of having both.
        Assert.Contains("\"error\":\"treex: path not found:", outText, StringComparison.Ordinal);
        // Plain-text "treex: path not found" must NOT appear on stderr when --json is requested.
        Assert.DoesNotContain("treex: path not found", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_PathIsFile_WithJson_RoutesNotADirectoryEnvelopeToStdout()
    {
        var (stdout, stderr) = Sinks();
        string filePath = Path.Combine(_tempDir, "regular-file.txt");
        File.WriteAllText(filePath, "content");

        int exit = Cli.Run(new[] { "--json", filePath }, stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(1, exit);
        string outText = stdout.ToString();
        Assert.Contains("\"exit_reason\":\"not_a_directory\"", outText, StringComparison.Ordinal);
        // Round-2 fresh-eyes 2026-05-09 code-reviewer I1: error field present on this
        // envelope too, with the canonical "treex: not a directory:" prefix.
        Assert.Contains("\"error\":\"treex: not a directory:", outText, StringComparison.Ordinal);
    }

    // ── --json success envelope routing (suite convention) ────────────────────────

    [Fact]
    public void Run_JsonSuccess_RoutesEnvelopeToStdoutNotStderr()
    {
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(new[] { "--json", _tempDir }, stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(0, exit);
        string outText = stdout.ToString();
        // JSON envelope is on stdout per suite convention (man-F12, winix-F3, whoholds).
        Assert.Contains("\"tool\":\"treex\"", outText, StringComparison.Ordinal);
        Assert.Contains("\"exit_code\":0", outText, StringComparison.Ordinal);
        Assert.Contains("\"directories\":0", outText, StringComparison.Ordinal);
        Assert.Contains("\"files\":0", outText, StringComparison.Ordinal);
        // Stderr may contain the human summary; assert it does NOT contain the JSON envelope.
        Assert.DoesNotContain("\"tool\":\"treex\"", stderr.ToString(), StringComparison.Ordinal);
    }

    // ── CR I1: --size --ndjson totalSize bug ──────────────────────────────────────

    [Fact]
    public void Run_SizeNdjson_AccumulatesTotalSizeBytesInSummary()
    {
        // Pre-fix: --size --ndjson reported total_size_bytes:0 because the NDJSON branch
        // never accumulated sizes. With three files totaling 600 bytes, the JSON summary
        // must report total_size_bytes:600.
        var (stdout, stderr) = Sinks();
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), new string('a', 100));
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), new string('b', 200));
        File.WriteAllText(Path.Combine(_tempDir, "c.txt"), new string('c', 300));

        int exit = Cli.Run(new[] { "--size", "--ndjson", "--json", _tempDir }, stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(0, exit);
        string outText = stdout.ToString();
        // The summary envelope follows the NDJSON records on stdout.
        Assert.Contains("\"total_size_bytes\":600", outText, StringComparison.Ordinal);
        // The human stderr summary line should also reflect a non-zero total.
        Assert.DoesNotContain("(0)", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_SizeNonNdjson_AlsoAccumulatesTotalSize()
    {
        // Regression guard: ensure the existing render-branch totalSize accumulation
        // still works post-refactor (the renderer path was previously the only one
        // that summed correctly).
        var (stdout, stderr) = Sinks();
        File.WriteAllText(Path.Combine(_tempDir, "x.txt"), new string('x', 500));

        int exit = Cli.Run(new[] { "--size", "--json", _tempDir }, stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(0, exit);
        Assert.Contains("\"total_size_bytes\":500", stdout.ToString(), StringComparison.Ordinal);
    }

    // ── Mutex / parser-error routing ──────────────────────────────────────────────

    [Fact]
    public void Run_BothCaseFlags_ReturnsUsageError()
    {
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "--ignore-case", "--case-sensitive", _tempDir },
            stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(ExitCodeUsageError, exit);
        Assert.Contains("mutually exclusive", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_InvalidSort_ReturnsUsageError()
    {
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "--sort", "bogus", _tempDir },
            stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(ExitCodeUsageError, exit);
        Assert.Contains("invalid --sort value", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_InvalidType_ReturnsUsageError()
    {
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "--type", "bogus", _tempDir },
            stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(ExitCodeUsageError, exit);
        Assert.Contains("invalid --type value", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_InvalidMinSize_ReturnsUsageError()
    {
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "--min-size", "bogus", _tempDir },
            stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(ExitCodeUsageError, exit);
        Assert.Contains("invalid --min-size", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_InvalidNewer_ReturnsUsageError()
    {
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "--newer", "bogus", _tempDir },
            stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(ExitCodeUsageError, exit);
        Assert.Contains("invalid --newer", stderr.ToString(), StringComparison.Ordinal);
    }

    // ── --ext leading-dot warning ─────────────────────────────────────────────────

    [Fact]
    public void Run_ExtWithLeadingDot_StripsAndWarnsOnStderr()
    {
        var (stdout, stderr) = Sinks();
        File.WriteAllText(Path.Combine(_tempDir, "code.cs"), "x");

        int exit = Cli.Run(
            new[] { "--ext", ".cs", _tempDir },
            stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(0, exit);
        Assert.Contains("stripping leading dot", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("'.cs'", stderr.ToString(), StringComparison.Ordinal);
    }

    // ── Multi-root rendering ──────────────────────────────────────────────────────

    [Fact]
    public void Run_MultipleRoots_BlankLineBetween()
    {
        var (stdout, stderr) = Sinks();
        string root1 = Path.Combine(_tempDir, "r1");
        string root2 = Path.Combine(_tempDir, "r2");
        Directory.CreateDirectory(root1);
        Directory.CreateDirectory(root2);

        Cli.Run(new[] { root1, root2 }, stdout, stderr, isStdoutRedirected: true);

        string outText = stdout.ToString();
        // Two roots separated by a blank line — assert there are at least two newline
        // tokens with nothing between them somewhere in the output.
        Assert.Contains("\n\n", outText.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    // Mirror Yort.ShellKit's ExitCode constant rather than depending on it directly.
    private const int ExitCodeUsageError = 125;
}
