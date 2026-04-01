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
