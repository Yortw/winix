#nullable enable
using System;
using System.IO;
using Winix.TimeIt;
using Xunit;
using Yort.ShellKit;

namespace Winix.TimeIt.Tests;

// Round-1 review CR-I1 / TA-C1 — Program.cs's per-error-type exit-code matrix and
// the "errors always go to stderr even when --stdout redirects summary" invariant
// previously had zero coverage. Cli.Run now lives in the library so every dispatch
// path can be locked here.
//
// Round-1 review TA-C2 — exit-code passthrough is pinned for representative codes
// (0, 1, 42, 125, 126, 127) so a regression that clamps non-zero codes to 1, or
// that drops 125/126/127 collisions with timeit's own codes, would fail.
public class CliTests
{
    private static (int exit, string stdout, string stderr) RunCli(params string[] args)
    {
        var stdoutWriter = new StringWriter();
        var stderrWriter = new StringWriter();
        int exit = Cli.Run(args, stdoutWriter, stderrWriter);
        return (exit, stdoutWriter.ToString(), stderrWriter.ToString());
    }

    // ── Exit-code passthrough ──

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(125)]
    [InlineData(126)]
    [InlineData(127)]
    [InlineData(255)]
    public void Run_ChildExitCode_ForwardedUnchanged(int childCode)
    {
        // Use `cmd /c exit <N>` on Windows, `sh -c "exit <N>"` on POSIX.
        // The contract: timeit.ExitCode == child exit code, NOT 0/1/etc.
        string[] args = OperatingSystem.IsWindows()
            ? new[] { "cmd", "/c", $"exit {childCode}" }
            : new[] { "sh", "-c", $"exit {childCode}" };
        var r = RunCli(args);
        Assert.Equal(childCode, r.exit);
    }

    // ── Per-error-type exit codes (the bit Program.cs orchestration was unguarded for) ──

    [Fact]
    public void Run_NoCommand_ReturnsUsageError()
    {
        var r = RunCli();
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("no command specified", r.stderr, StringComparison.Ordinal);
    }

    // -- Round-2 review SFH-R2-I1 — the empty-command path used to route through `writer`
    //    (= stdout when --stdout is set) instead of stderr. That violated the
    //    "errors always go to stderr" invariant the catch arms enforce. A script
    //    doing `timeit --json --stdout | jq` with a missing command argument would
    //    pipe the JSON usage error to jq's stdin and silently misclassify the
    //    bad-invocation case. Now stderr is hardcoded for this path too. --
    [Fact]
    public void Run_NoCommand_WithStdout_ErrorMustGoToStderr()
    {
        var r = RunCli("--stdout");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Empty(r.stdout);
        Assert.Contains("no command specified", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_NoCommand_JsonStdout_ErrorMustGoToStderr()
    {
        var r = RunCli("--json", "--stdout");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Empty(r.stdout);
        Assert.Contains("no command specified", r.stderr, StringComparison.Ordinal);
    }

    // -- Round-2 review DOCS-IMP1 — pin that the JSON usage_error exit_reason value
    //    actually appears in stderr when --json is set. Without this, a regression
    //    that emitted a different reason string for usage errors would silently break
    //    the documented contract that scripts use to disambiguate timeit-side failures. --
    [Fact]
    public void Run_NoCommand_Json_EmitsUsageErrorReason()
    {
        var r = RunCli("--json");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("\"exit_reason\":\"usage_error\"", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_CommandNotFound_ReturnsNotFound()
    {
        var r = RunCli("this-command-surely-does-not-exist-abcxyz-9999");
        Assert.Equal(ExitCode.NotFound, r.exit);
        Assert.Contains("timeit:", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_CommandNotFound_JsonError_GoesToStderrEvenWithStdout()
    {
        // Round-1 review TA-C3 — pin the "errors always go to stderr even with --stdout"
        // invariant. Without this, a regression sending errors to the writer would
        // silently corrupt --stdout consumers' parse of the error case.
        var r = RunCli("--json", "--stdout", "this-command-surely-does-not-exist-abcxyz-9999");
        Assert.Equal(ExitCode.NotFound, r.exit);
        Assert.Empty(r.stdout); // No JSON to stdout — the error doesn't go there
        Assert.Contains("command_not_found", r.stderr, StringComparison.Ordinal);
        Assert.Contains("\"exit_reason\"", r.stderr, StringComparison.Ordinal);
    }

    // -- Round-2 review TA-I5 — IOE (bad EXE format / start error) at the Cli seam was
    //    untested. The catch arm at Cli.cs converts IOE → exit 126 + JSON exit_reason
    //    "start_error". A regression mapping IOE to 125/UsageError or to plain
    //    "command_not_executable" JSON would ship green without this pin.
    //    Gated to Windows for the same reason as CommandRunnerTests.Run_BadExecutableFormat:
    //    Linux/macOS kernel behaviour for +x text files is non-deterministic. --
    [SkippableFact]
    public void Run_BadExecutableFormat_PlainOutput_ExitsNotExecutable()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only — kernel ENOEXEC behaviour varies on Linux/macOS.");
        if (!OperatingSystem.IsWindows()) return;

        string tempFile = Path.Combine(Path.GetTempPath(), $"timeit-cli-test-{Guid.NewGuid()}.exe");
        try
        {
            File.WriteAllText(tempFile, "this is not an executable");
            var r = RunCli(tempFile);
            Assert.Equal(ExitCode.NotExecutable, r.exit); // 126, NOT 125
            Assert.Contains("timeit:", r.stderr, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [SkippableFact]
    public void Run_BadExecutableFormat_JsonOutput_EmitsStartErrorReason()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only — kernel ENOEXEC behaviour varies on Linux/macOS.");
        if (!OperatingSystem.IsWindows()) return;

        string tempFile = Path.Combine(Path.GetTempPath(), $"timeit-cli-test-{Guid.NewGuid()}.exe");
        try
        {
            File.WriteAllText(tempFile, "this is not an executable");
            var r = RunCli("--json", tempFile);
            Assert.Equal(ExitCode.NotExecutable, r.exit);
            Assert.Empty(r.stdout); // JSON-error invariant: stderr only
            Assert.Contains("\"exit_reason\":\"start_error\"", r.stderr, StringComparison.Ordinal);
            Assert.Contains("\"exit_code\":126", r.stderr, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── --stdout writer routing for SUCCESS path ──

    [Fact]
    public void Run_SuccessSummary_GoesToStderrByDefault()
    {
        // Default: summary on stderr, child output passes through inherited stdout.
        // The test harness captures inherited stdout via the parent process's stdout
        // (NOT our StringWriter), so the StringWriter for stdout should be empty.
        string[] args = OperatingSystem.IsWindows()
            ? new[] { "cmd", "/c", "exit 0" }
            : new[] { "sh", "-c", "exit 0" };
        var r = RunCli(args);
        Assert.Equal(0, r.exit);
        // Summary goes to stderr writer; stdout writer is unused in default mode.
        Assert.Empty(r.stdout);
        Assert.Contains("real", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_SuccessSummary_WithStdoutFlag_GoesToStdout()
    {
        // --stdout flips the summary destination. The "errors always go to stderr"
        // invariant doesn't apply here because this is a SUCCESS path.
        string[] args = OperatingSystem.IsWindows()
            ? new[] { "--stdout", "cmd", "/c", "exit 0" }
            : new[] { "--stdout", "sh", "-c", "exit 0" };
        var r = RunCli(args);
        Assert.Equal(0, r.exit);
        Assert.Contains("real", r.stdout, StringComparison.Ordinal);
        // Stderr should be empty for a successful run with --stdout.
        Assert.Empty(r.stderr);
    }

    [Fact]
    public void Run_JsonSuccess_DefaultGoesToStderr()
    {
        string[] args = OperatingSystem.IsWindows()
            ? new[] { "--json", "cmd", "/c", "exit 0" }
            : new[] { "--json", "sh", "-c", "exit 0" };
        var r = RunCli(args);
        Assert.Equal(0, r.exit);
        Assert.Empty(r.stdout);
        Assert.Contains("\"tool\":\"timeit\"", r.stderr, StringComparison.Ordinal);
        Assert.Contains("\"exit_reason\":\"success\"", r.stderr, StringComparison.Ordinal);
        // child_exit_code is the actual child code; exit_code is timeit's (0).
        Assert.Contains("\"child_exit_code\":0", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_JsonSuccess_StdoutFlag_GoesToStdout()
    {
        string[] args = OperatingSystem.IsWindows()
            ? new[] { "--json", "--stdout", "cmd", "/c", "exit 0" }
            : new[] { "--json", "--stdout", "sh", "-c", "exit 0" };
        var r = RunCli(args);
        Assert.Equal(0, r.exit);
        Assert.Contains("\"tool\":\"timeit\"", r.stdout, StringComparison.Ordinal);
        // Success path with --stdout: stderr is clean.
        Assert.Empty(r.stderr);
    }

    // ── JSON exit_code vs child_exit_code distinction (DOCS-C1 fix verification) ──

    [Fact]
    public void Run_JsonNonZeroChild_ToolExitCodeIsZero_ChildExitCodeIsActual()
    {
        // Pins the contract that DOCS-C1 documented: top-level exit_code reports
        // whether timeit itself succeeded (so 0 here even though child failed),
        // and child_exit_code is the child's actual code (42).
        string[] args = OperatingSystem.IsWindows()
            ? new[] { "--json", "cmd", "/c", "exit 42" }
            : new[] { "--json", "sh", "-c", "exit 42" };
        var r = RunCli(args);
        Assert.Equal(42, r.exit); // process exit code is the CHILD's
        Assert.Contains("\"exit_code\":0", r.stderr, StringComparison.Ordinal);          // timeit's
        Assert.Contains("\"exit_reason\":\"success\"", r.stderr, StringComparison.Ordinal); // timeit ran fine
        Assert.Contains("\"child_exit_code\":42", r.stderr, StringComparison.Ordinal);    // child's
    }

    // ── ShellKit-handled flags (--help/--version/--describe) ──
    // These return parse.IsHandled and ShellKit writes directly to Console.Out (not
    // the injected writer). We pin the exit-code contract; the output destination
    // is ShellKit's responsibility.

    [Fact]
    public void Run_Help_ExitZero()
    {
        var r = RunCli("--help");
        Assert.Equal(0, r.exit);
    }

    [Fact]
    public void Run_Version_ExitZero()
    {
        var r = RunCli("--version");
        Assert.Equal(0, r.exit);
    }

    [Fact]
    public void Run_Describe_ExitZero()
    {
        var r = RunCli("--describe");
        Assert.Equal(0, r.exit);
    }
}
