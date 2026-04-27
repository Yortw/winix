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
        for (int i = 0; i < 10; i++)
        {
            PeepResult result = await CommandExecutor.RunAsync(
                "dotnet", new[] { "--version" }, TriggerSource.Initial);
            Assert.Equal(0, result.ExitCode);
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
