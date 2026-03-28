using Xunit;
using Winix.TimeIt;

namespace Winix.TimeIt.Tests;

public class CommandRunnerTests
{
    [Fact]
    public void Run_SuccessfulCommand_ReturnsZeroExitCode()
    {
        var result = CommandRunner.Run("dotnet", new[] { "--version" });

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.WallTime > TimeSpan.Zero);
    }

    [Fact]
    public void Run_SuccessfulCommand_ReturnsCpuMetrics()
    {
        var result = CommandRunner.Run("dotnet", new[] { "--version" });

        // Both user and system CPU should be non-null (native APIs are reliable)
        Assert.NotNull(result.UserCpuTime);
        Assert.NotNull(result.SystemCpuTime);
        Assert.True(result.UserCpuTime!.Value >= TimeSpan.Zero);
        Assert.True(result.SystemCpuTime!.Value >= TimeSpan.Zero);

        // At least one should be non-zero for a real command
        Assert.True(
            result.UserCpuTime.Value > TimeSpan.Zero || result.SystemCpuTime.Value > TimeSpan.Zero,
            "Expected at least one of user/system CPU to be non-zero");
    }

    [Fact]
    public void Run_SuccessfulCommand_ReturnsPeakMemory()
    {
        var result = CommandRunner.Run("dotnet", new[] { "--version" });

        // Native APIs should reliably provide peak memory (no more race condition)
        Assert.NotNull(result.PeakMemoryBytes);
        Assert.True(result.PeakMemoryBytes!.Value > 0, "Expected non-zero peak memory for a real process");
    }

    [Fact]
    public void Run_SuccessfulCommand_ReturnsTotalCpuTime()
    {
        var result = CommandRunner.Run("dotnet", new[] { "--version" });

        Assert.NotNull(result.TotalCpuTime);
        Assert.Equal(result.UserCpuTime!.Value + result.SystemCpuTime!.Value, result.TotalCpuTime!.Value);
    }

    [Fact]
    public void Run_FailingCommand_ReturnsNonZeroExitCode()
    {
        var result = CommandRunner.Run("dotnet", new[] { "nonexistent-command-that-does-not-exist" });

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void Run_CommandNotFound_ThrowsCommandNotFoundException()
    {
        Assert.Throws<CommandNotFoundException>(
            () => CommandRunner.Run("this-command-surely-does-not-exist-abcxyz", Array.Empty<string>()));
    }

    [Fact]
    public void Run_CommandWithArguments_PassesArgsCorrectly()
    {
        var result = CommandRunner.Run("dotnet", new[] { "--list-sdks" });

        Assert.Equal(0, result.ExitCode);
    }
}
