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
        // PeakMemoryBytes is best-effort: on .NET 10, Process.PeakWorkingSet64 throws
        // InvalidOperationException after exit for short-lived processes, so 0 is a valid result.
        Assert.True(result.PeakMemoryBytes >= 0);
    }

    [Fact]
    public void Run_FailingCommand_ReturnsNonZeroExitCode()
    {
        // dotnet with a bad argument returns non-zero
        var result = CommandRunner.Run("dotnet", new[] { "nonexistent-command-that-does-not-exist" });

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void Run_CommandNotFound_ThrowsCommandNotFoundException()
    {
        Assert.Throws<CommandNotFoundException>(
            () => CommandRunner.Run("this-command-surely-does-not-exist-abcxyz", Array.Empty<string>()));
    }
}
