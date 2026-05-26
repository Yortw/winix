#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Winix.Man;
using Xunit;

namespace Winix.Man.Tests;

/// <summary>
/// Orchestration-layer tests for <see cref="Cli.Run"/>. Pin contracts that previously
/// lived only in <c>man/Program.cs</c> with no test coverage:
/// F12 (--json to stdout), F2 (corrupt-gzip catch path),
/// and exit-code routing for --manpath / --path / --where / not-found / usage-error.
/// Round-1 fresh-eyes 2026-05-09 closure for pr-test-analyzer I1, I2, I6.
/// </summary>
public sealed class CliRunTests : IDisposable
{
    private readonly string _tempDir;

    public CliRunTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mancli-" + Guid.NewGuid().ToString("N"));
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
            // Best-effort.
        }
    }

    private static (StringWriter stdout, StringWriter stderr) Sinks()
    {
        return (new StringWriter(), new StringWriter());
    }

    private void CreateValidManPage(string name, int section, string nameSection = "name - description")
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, $"man{section}"));
        string path = Path.Combine(_tempDir, $"man{section}", $"{name}.{section}");
        File.WriteAllText(path,
            $".TH {name.ToUpperInvariant()} {section}\n" +
            ".SH NAME\n" +
            $"{nameSection}\n");
    }

    // ── I1: F12 --json to stdout, nothing-but-JSON to stderr ───────────────────────

    [Fact]
    public void Run_JsonFlag_WritesJsonToStdoutAndNothingToStderr()
    {
        // F12 contract (was stderr pre-fix, now stdout per suite convention). A regression
        // that flipped Console.Out.WriteLine to Console.Error.WriteLine would hide JSON
        // from `man --json X | jq` consumers; this pins the routing.
        CreateValidManPage("probe", 1, "probe - test page");
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "--json", "--no-pager", "probe" },
            stdout, stderr,
            isTerminal: false,
            terminalWidth: 80,
            exeDirectory: _tempDir,
            manpathEnv: _tempDir);

        Assert.Equal(ManExitCode.Success, exit);
        Assert.Contains("\"tool\":\"man\"", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"name\":\"probe\"", stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal("", stderr.ToString());
    }

    [Fact]
    public void Run_JsonFlag_OutputContainsExpectedFields()
    {
        // Pre-fix the agent guide claimed exit_code/exit_reason/title fields existed; they
        // don't. Pin the actual emitted shape (tool, version, name, section, path, description)
        // so a future drift between code and docs is caught as a test failure.
        CreateValidManPage("probe", 1, "probe - test page");
        var (stdout, stderr) = Sinks();

        Cli.Run(
            new[] { "--json", "--no-pager", "probe" },
            stdout, stderr,
            isTerminal: false,
            terminalWidth: 80,
            exeDirectory: _tempDir,
            manpathEnv: _tempDir);

        string json = stdout.ToString();
        Assert.Contains("\"tool\":", json, StringComparison.Ordinal);
        Assert.Contains("\"version\":", json, StringComparison.Ordinal);
        Assert.Contains("\"name\":", json, StringComparison.Ordinal);
        Assert.Contains("\"section\":", json, StringComparison.Ordinal);
        Assert.Contains("\"path\":", json, StringComparison.Ordinal);
        Assert.Contains("\"description\":", json, StringComparison.Ordinal);

        // Fields that the agent guide lied about pre-fix should NOT be emitted.
        Assert.DoesNotContain("\"exit_code\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"exit_reason\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"title\"", json, StringComparison.Ordinal);
    }

    // ── I2: F2 corrupt-gzip catch path ─────────────────────────────────────────────

    [Fact]
    public void Run_CorruptGzipPage_ReturnsInternalErrorWithEnglishMessage()
    {
        // F2 contract: corrupt gzip → exit 125 + 'man: failed to decompress' on stderr.
        // The framework SR-key message under InvariantGlobalization=true is suppressed
        // (CR I3-class concern). Pre-fix the catch existed but no test pinned it; a
        // regression that removed the catch or stopped suppressing the SR key would
        // ship silently.
        Directory.CreateDirectory(Path.Combine(_tempDir, "man1"));
        // 4 bytes of non-gzip garbage with the .gz extension. The plain-extension probe
        // (no .gz) hits a different path; this one specifically exercises the
        // GZipStream-throws-InvalidDataException branch.
        File.WriteAllBytes(Path.Combine(_tempDir, "man1", "garbage.1.gz"), new byte[] { 0xFF, 0xFE, 0xFD, 0xFC });
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "--no-pager", "garbage" },
            stdout, stderr,
            isTerminal: false,
            terminalWidth: 80,
            exeDirectory: _tempDir,
            manpathEnv: _tempDir);

        Assert.Equal(ManExitCode.InternalError, exit);
        string err = stderr.ToString();
        Assert.Contains("failed to decompress", err, StringComparison.Ordinal);
        Assert.Contains("garbage", err, StringComparison.Ordinal);
        // SR-key safety: the framework's English message for InvalidDataException is
        // suppressed; only our own English string appears. Sample SR-key tokens that
        // would indicate leakage:
        Assert.DoesNotContain("Arg_", err, StringComparison.Ordinal);
        Assert.DoesNotContain("IO_PathNotFound", err, StringComparison.Ordinal);
    }

    // ── I6: exit-code routing for --manpath / --path / --where / not-found / usage ─

    [Fact]
    public void Run_Manpath_WritesPathsToStdoutAndExitsZero()
    {
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "--manpath" },
            stdout, stderr,
            isTerminal: false,
            terminalWidth: 80,
            exeDirectory: _tempDir,
            manpathEnv: _tempDir);

        Assert.Equal(ManExitCode.Success, exit);
        Assert.Contains(_tempDir, stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal("", stderr.ToString());
    }

    [Fact]
    public void Run_PathFlag_PrintsFilePathAndExitsZero()
    {
        // F1 share/man-path contract is verified in PageDiscoveryTests; here we verify the
        // orchestration layer routes the discovered path to stdout (not stderr) and returns
        // Success.
        CreateValidManPage("probe", 1);
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "--path", "probe" },
            stdout, stderr,
            isTerminal: false,
            terminalWidth: 80,
            exeDirectory: _tempDir,
            manpathEnv: _tempDir);

        Assert.Equal(ManExitCode.Success, exit);
        Assert.Contains("probe.1", stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal("", stderr.ToString());
    }

    [Fact]
    public void Run_WhereFlag_AliasForPathFlag()
    {
        // GNU compatibility: --where is identical to --path. Pre-fix this was only
        // documented in Program.cs:22 with no test pinning the alias behaviour.
        CreateValidManPage("probe", 1);
        var (stdoutPath, stderrPath) = Sinks();
        var (stdoutWhere, stderrWhere) = Sinks();

        int exitPath = Cli.Run(
            new[] { "--path", "probe" },
            stdoutPath, stderrPath,
            isTerminal: false, terminalWidth: 80,
            exeDirectory: _tempDir, manpathEnv: _tempDir);

        int exitWhere = Cli.Run(
            new[] { "--where", "probe" },
            stdoutWhere, stderrWhere,
            isTerminal: false, terminalWidth: 80,
            exeDirectory: _tempDir, manpathEnv: _tempDir);

        Assert.Equal(exitPath, exitWhere);
        Assert.Equal(stdoutPath.ToString(), stdoutWhere.ToString());
    }

    [Fact]
    public void Run_NoArguments_ReturnsUsageErrorOnStderr()
    {
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            Array.Empty<string>(),
            stdout, stderr,
            isTerminal: false,
            terminalWidth: 80,
            exeDirectory: _tempDir,
            manpathEnv: _tempDir);

        Assert.Equal(ManExitCode.UsageError, exit);
        Assert.Contains("what manual page", stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal("", stdout.ToString());
    }

    [Fact]
    public void Run_PageNotFound_ReturnsNotFoundExitCodeAndStderrMessage()
    {
        // Empty MANPATH (just _tempDir with no man1 subdirectory at all) — page lookup
        // returns null, NotFound exit, error to stderr.
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "--no-pager", "this_page_does_not_exist_xyz" },
            stdout, stderr,
            isTerminal: false,
            terminalWidth: 80,
            exeDirectory: _tempDir,
            manpathEnv: _tempDir);

        Assert.Equal(ManExitCode.NotFound, exit);
        Assert.Contains("no manual entry", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("this_page_does_not_exist_xyz", stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal("", stdout.ToString());
    }

    [Fact]
    public void Run_PageNotFound_WithSection_IncludesSectionInError()
    {
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "--no-pager", "3", "missing_xyz" },
            stdout, stderr,
            isTerminal: false,
            terminalWidth: 80,
            exeDirectory: _tempDir,
            manpathEnv: _tempDir);

        Assert.Equal(ManExitCode.NotFound, exit);
        Assert.Contains("missing_xyz(3)", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_BogusFlag_ReturnsUsageError2NotShellKit125()
    {
        // F3 contract: ShellKit's WriteErrors returns 125 (suite-wide), but man documents
        // POSIX-traditional 2 for usage errors. Cli.Run overrides the return code while
        // still emitting ShellKit's error messages. Same shape as less round-1 F3 test.
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "--bogus-flag-xyz" },
            stdout, stderr,
            isTerminal: false,
            terminalWidth: 80,
            exeDirectory: _tempDir,
            manpathEnv: _tempDir);

        Assert.Equal(ManExitCode.UsageError, exit);
        Assert.NotEqual(125, exit);  // explicit: NOT ShellKit's default
    }

    // ── F2 corrupt-gzip + --json: catch fires before JSON formatting ───────────────

    [Fact]
    public void Run_JsonFlagOnCorruptGzip_HitsF2CatchBeforeJsonFormatter()
    {
        // The F2 catch is in the read pipeline; it should fire BEFORE --json reaches the
        // formatter. Pre-fix this path wasn't tested at all. Pin: corrupt-gzip + --json
        // returns InternalError (not Success with empty JSON).
        Directory.CreateDirectory(Path.Combine(_tempDir, "man1"));
        File.WriteAllBytes(Path.Combine(_tempDir, "man1", "broken.1.gz"), new byte[] { 0xFF, 0xFE, 0xFD, 0xFC });
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "--json", "--no-pager", "broken" },
            stdout, stderr,
            isTerminal: false,
            terminalWidth: 80,
            exeDirectory: _tempDir,
            manpathEnv: _tempDir);

        Assert.Equal(ManExitCode.InternalError, exit);
        Assert.Contains("failed to decompress", stderr.ToString(), StringComparison.Ordinal);
        // No JSON should have been emitted to stdout (the catch returns before formatting).
        Assert.DoesNotContain("\"tool\":", stdout.ToString(), StringComparison.Ordinal);
    }
}
