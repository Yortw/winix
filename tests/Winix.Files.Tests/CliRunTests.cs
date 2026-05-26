#nullable enable

using System;
using System.IO;
using Winix.Files;
using Xunit;

namespace Winix.Files.Tests;

/// <summary>
/// Tests for the library-seam <see cref="Cli.Run"/> entry point. Pin orchestration-layer
/// exit-code routing, --json envelope routing, mutex validation, error messages, and
/// the dual-formatter shape parity without spawning a process. Round-1 fresh-eyes
/// 2026-05-09 closes test-analyzer C1-C4 (zero coverage of dispatch layer) and the
/// suite-convention --json-to-stdout drift.
/// </summary>
public sealed class CliRunTests : IDisposable
{
    private const int ExitCodeUsageError = 125;

    private readonly string _tempDir;

    public CliRunTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "files-cli-" + Guid.NewGuid().ToString("N"));
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
            // Best-effort cleanup.
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

    // ── --json envelope routing (suite convention: stdout) ────────────────────────

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
        // Round-1 fresh-eyes 2026-05-09 code-reviewer I3 + dual-envelope-formatter
        // shape parity rule (`feedback_dual_envelope_formatters_drift.md`): the
        // pre-walk error envelope must carry the same array fields as the success
        // envelope so consumers can use a single shape.
        Assert.Contains("\"error\":\"files: path not found:", outText, StringComparison.Ordinal);
        Assert.Contains("\"searched_roots\":[]", outText, StringComparison.Ordinal);
        Assert.Contains("\"walk_errors\":[]", outText, StringComparison.Ordinal);
        Assert.DoesNotContain("files: path not found", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_PathIsFile_WithJson_EmitsNotADirectoryReason()
    {
        var (stdout, stderr) = Sinks();
        string filePath = Path.Combine(_tempDir, "regular-file.txt");
        File.WriteAllText(filePath, "content");

        int exit = Cli.Run(new[] { "--json", filePath }, stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(1, exit);
        string outText = stdout.ToString();
        Assert.Contains("\"exit_reason\":\"not_a_directory\"", outText, StringComparison.Ordinal);
        Assert.Contains("\"error\":\"files: not a directory:", outText, StringComparison.Ordinal);
        Assert.Contains("\"searched_roots\":[]", outText, StringComparison.Ordinal);
        Assert.Contains("\"walk_errors\":[]", outText, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_JsonSuccess_RoutesEnvelopeToStdoutNotStderr()
    {
        var (stdout, stderr) = Sinks();
        File.WriteAllText(Path.Combine(_tempDir, "x.txt"), "x");

        int exit = Cli.Run(new[] { "--json", _tempDir }, stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(0, exit);
        string outText = stdout.ToString();
        // Round-1 fresh-eyes 2026-05-09 (CR C1, SFH M1, TA C2, Docs I1): JSON envelope
        // goes to stdout per suite convention (man-F12, winix-F3, whoholds, treex).
        Assert.Contains("\"tool\":\"files\"", outText, StringComparison.Ordinal);
        Assert.Contains("\"exit_code\":0", outText, StringComparison.Ordinal);
        Assert.Contains("\"walk_errors\":[]", outText, StringComparison.Ordinal);
        // Stderr must NOT contain the JSON envelope.
        Assert.DoesNotContain("\"tool\":\"files\"", stderr.ToString(), StringComparison.Ordinal);
    }

    // ── Mutex validation ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(new[] { "--text", "--binary" }, "mutually exclusive")]
    [InlineData(new[] { "--ignore-case", "--case-sensitive" }, "mutually exclusive")]
    [InlineData(new[] { "--text", "--type", "d" }, "cannot be combined with --type d")]
    [InlineData(new[] { "--binary", "--type", "d" }, "cannot be combined with --type d")]
    [InlineData(new[] { "--long", "--print0" }, "mutually exclusive")]
    [InlineData(new[] { "--long", "--ndjson" }, "mutually exclusive")]
    [InlineData(new[] { "--print0", "--ndjson" }, "mutually exclusive")]
    public void Run_MutuallyExclusiveFlags_ReturnsUsageError(string[] flags, string expectedMessageSubstring)
    {
        var (stdout, stderr) = Sinks();
        var args = new string[flags.Length + 1];
        Array.Copy(flags, args, flags.Length);
        args[flags.Length] = _tempDir;

        int exit = Cli.Run(args, stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(ExitCodeUsageError, exit);
        Assert.Contains(expectedMessageSubstring, stderr.ToString(), StringComparison.Ordinal);
    }

    // ── Parser-error routing ──────────────────────────────────────────────────────

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

    [Fact]
    public void Run_InvalidRegex_ReturnsUsageErrorAndDoesNotLeakSrKey()
    {
        // Round-1 fresh-eyes 2026-05-09 test-analyzer C3: regex-parse exception → 125
        // mapping was unpinned. A future refactor that broadens the catch could
        // silently regress. Plus per `feedback_invariant_globalization_resource_keys.md`,
        // ensure the message doesn't leak SR keys (e.g. "Argument_") under
        // InvariantGlobalization=true.
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "--regex", "([", _tempDir },  // unbalanced bracket
            stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(ExitCodeUsageError, exit);
        Assert.Contains("invalid regex", stderr.ToString(), StringComparison.Ordinal);
        // Should not leak SR resource keys — RegexParseException's English message
        // should always include some recognisable English content.
        Assert.DoesNotContain("Arg_", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("IO_", stderr.ToString(), StringComparison.Ordinal);
    }

    // ── --ext leading-dot warning ─────────────────────────────────────────────────

    [Fact]
    public void Run_ExtWithLeadingDot_StripsAndWarnsToStderr()
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

    [Fact]
    public void Run_ExtWithoutDot_DoesNotWarn()
    {
        var (stdout, stderr) = Sinks();
        File.WriteAllText(Path.Combine(_tempDir, "code.cs"), "x");

        int exit = Cli.Run(
            new[] { "--ext", "cs", _tempDir },
            stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("stripping leading dot", stderr.ToString(), StringComparison.Ordinal);
    }

    // ── --gitignore-not-repo warning ──────────────────────────────────────────────

    [Fact]
    public void Run_GitignoreOutsideRepo_WarnsButContinues()
    {
        var (stdout, stderr) = Sinks();
        File.WriteAllText(Path.Combine(_tempDir, "regular.txt"), "x");

        int exit = Cli.Run(
            new[] { "--gitignore", _tempDir },
            stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(0, exit);
        Assert.Contains(
            "--gitignore specified but git not found on PATH or no roots are inside a git repository",
            stderr.ToString(),
            StringComparison.Ordinal);
    }
}
