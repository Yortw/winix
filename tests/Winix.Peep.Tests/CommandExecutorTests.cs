using Xunit;
using Winix.Peep;
using Yort.ShellKit;

namespace Winix.Peep.Tests;

public class CommandExecutorTests
{
    [Fact]
    public async Task RunAsync_SuccessfulCommand_ReturnsZeroExitCode()
    {
        // Use "dotnet --list-runtimes" which reliably returns 0 on all CI platforms
        PeepResult result = await CommandExecutor.RunAsync(
            "dotnet", new[] { "--list-runtimes" }, TriggerSource.Initial);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Duration > TimeSpan.Zero);
        Assert.Equal(TriggerSource.Initial, result.Trigger);
    }

    [Fact]
    public async Task RunAsync_SuccessfulCommand_CapturesOutput()
    {
        PeepResult result = await CommandExecutor.RunAsync(
            "dotnet", new[] { "--version" }, TriggerSource.Initial);

        Assert.False(string.IsNullOrWhiteSpace(result.Output));
        // dotnet --version outputs a version string like "10.0.100"
        Assert.Contains(".", result.Output);
    }

    [Fact]
    public async Task RunAsync_FailingCommand_ReturnsNonZeroExitCode()
    {
        PeepResult result = await CommandExecutor.RunAsync(
            "dotnet", new[] { "nonexistent-command-that-does-not-exist" }, TriggerSource.Interval);

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_FailingCommand_CapturesErrorOutput()
    {
        PeepResult result = await CommandExecutor.RunAsync(
            "dotnet", new[] { "nonexistent-command-that-does-not-exist" }, TriggerSource.Interval);

        // dotnet should produce some error message on stderr about unknown command
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
    }

    [Fact]
    public async Task RunAsync_CommandNotFound_ThrowsCommandNotFoundException()
    {
        await Assert.ThrowsAsync<CommandNotFoundException>(
            () => CommandExecutor.RunAsync(
                "this-command-surely-does-not-exist-abcxyz",
                Array.Empty<string>(),
                TriggerSource.Initial));
    }

    [Fact]
    public async Task RunAsync_EmptyCommandName_ThrowsCommandNotExecutableException()
    {
        // R3 CR I2: Process.Start throws InvalidOperationException("No file name was
        // specified") when ProcessStartInfo.FileName is empty. Pre-fix this propagated
        // through the watch loop's last-resort catch as "unexpected error" with exit
        // code 126 from the catch-all path. Post-fix the InvalidOperationException is
        // mapped to CommandNotExecutableException, so the user sees the typed
        // command_not_executable diagnostic that --describe advertises.
        await Assert.ThrowsAsync<CommandNotExecutableException>(
            () => CommandExecutor.RunAsync(
                "", Array.Empty<string>(), TriggerSource.Initial));
    }

    [SkippableFact]
    public async Task RunAsync_ChildWritesToBothStdoutAndStderr_LinesAreNotCorrupted()
    {
        // R6 smoke-test bug: pre-fix, ReadStreamAsync read in 4096-char chunks and
        // appended each chunk under a lock. Two concurrent readers (stdout + stderr)
        // could land a chunk from one stream MID-LINE of the other, corrupting output
        // like "this naCould not execute...me could not be found".
        //
        // Earlier versions of this test used `dotnet some-missing-subcommand` because
        // dotnet's "command not found" diagnostic happens to write the first line to
        // stderr and the rest to stdout. That was a happy accident and not a contract:
        // the macos-latest dotnet SDK now emits a different shape (System.IO.
        // FileNotFoundException stack on stderr only) which broke the assertions
        // without telling us anything about peep. Replaced with a deterministic shell
        // reproducer that writes known lines to both streams in a known order — that
        // is what the contract actually requires.
        //
        // Skip on Windows because /bin/sh is not available; the contract being
        // tested (line-atomic merge in ReadStreamAsync) is platform-independent code,
        // so Unix coverage exercises it. A separate Windows-targeted dual-stream
        // reproducer would need cmd.exe or PowerShell scaffolding and isn't worth
        // the duplication for a contract already covered.
        Skip.IfNot(!OperatingSystem.IsWindows(), "Unix-only — uses /bin/sh + printf");
        if (OperatingSystem.IsWindows()) return; // CA1416 satisfaction; redundant after Skip.IfNot

        // Force interleaving: alternate small writes to stdout and stderr several
        // times. Each write is fully terminated by \n so a correct line-atomic merge
        // produces every line intact regardless of the order the OS schedules pipe
        // reads. Pre-fix (chunked merge) would land cross-stream chunks mid-line and
        // break at least one of the substring asserts below.
        const string script =
            "for i in 1 2 3 4 5; do " +
            "  printf 'stdout line %s\\n' \"$i\"; " +
            "  printf 'stderr line %s\\n' \"$i\" 1>&2; " +
            "done";
        PeepResult result = await CommandExecutor.RunAsync(
            "/bin/sh", new[] { "-c", script }, TriggerSource.Initial);

        Assert.Equal(0, result.ExitCode);
        for (int i = 1; i <= 5; i++)
        {
            Assert.Contains($"stdout line {i}", result.Output);
            Assert.Contains($"stderr line {i}", result.Output);
        }
    }

    [SkippableFact]
    public async Task RunAsync_CapturedOutput_HasLfLineEndings()
    {
        // R6 smoke-test fix: CRLF → LF normalisation at the API boundary. Windows
        // children write \r\n; peep's downstream consumers (Console.Write to alt-
        // buffer, JSON envelope last_output, --exit-on-match regex) expect LF.
        // Pin: captured Output must not contain CR characters when running a
        // Windows child that writes CRLF (cmd.exe echo always writes \r\n).
        Skip.IfNot(OperatingSystem.IsWindows(), "CRLF is only emitted by Windows children");
        if (!OperatingSystem.IsWindows()) return; // redundant, satisfies CA1416 analyzer

        PeepResult result = await CommandExecutor.RunAsync(
            "cmd.exe", new[] { "/c", "echo line1 & echo line2" }, TriggerSource.Initial);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("line1", result.Output);
        Assert.Contains("line2", result.Output);
        // Must be LF-only — no carriage returns survive the API boundary.
        Assert.DoesNotContain("\r", result.Output);
    }

    [Fact]
    public async Task RunAsync_FastExitingChild_DoesNotLeakIOException()
    {
        // R3 SFH I4 regression-style smoke: process.StandardInput.Close() races with
        // a child that exits before peep gets a chance to close its stdin pipe.
        // Pre-fix, the IOException ("pipe has been ended") escaped to the watch-loop's
        // last-resort catch and looked like a CI flake. Post-fix, Close() is wrapped
        // in try/catch (IOException, ObjectDisposedException). Run dotnet --version
        // (a fast-exiter) repeatedly to flush the race. A regression that re-removes
        // the wrap would surface as one of these iterations throwing IOException
        // / unexpected_error rather than completing cleanly.
        //
        // The contract under test is "RunAsync returns a PeepResult cleanly without
        // leaking an IOException" — what the child's exit code is is ancillary. On
        // contended CI runners (notably the Ubuntu libsecret-service-wrapped step
        // where many parallel test workers compete for resources) `dotnet --version`
        // itself can occasionally exit non-zero for environmental reasons unrelated
        // to peep. Asserting exit==0 on every iteration was too strict — it caught
        // an environmental flake rather than the regression class we want to pin.
        // We assert that we got A RESULT (i.e. RunAsync returned, didn't throw).
        for (int i = 0; i < 10; i++)
        {
            PeepResult result = await CommandExecutor.RunAsync(
                "dotnet", new[] { "--version" }, TriggerSource.Initial);
            Assert.NotNull(result);
        }
    }

    [Fact]
    public async Task RunAsync_Cancellation_KillsProcess()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // ping with a long timeout -- will be cancelled
        // On Windows: ping -n 30 127.0.0.1
        // On Linux: ping -c 30 127.0.0.1
        string flag = OperatingSystem.IsWindows() ? "-n" : "-c";

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CommandExecutor.RunAsync(
                "ping", new[] { flag, "30", "127.0.0.1" },
                TriggerSource.Manual, cts.Token));
    }
}
