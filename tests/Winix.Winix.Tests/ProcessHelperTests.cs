#nullable enable

using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class ProcessHelperTests
{
    [Fact]
    public async Task RunAsync_CapturesStdoutAndExitCode()
    {
        // "dotnet --version" is always available in CI and dev machines
        ProcessResult result = await ProcessHelper.RunAsync("dotnet", new[] { "--version" });

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Stdout));
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task RunAsync_NonZeroExitCode_CapturesStderr()
    {
        // "dotnet --invalid-flag" writes an error to stderr and returns non-zero
        ProcessResult result = await ProcessHelper.RunAsync("dotnet", new[] { "--invalid-flag" });

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_CommandNotFound_ReturnsNotFoundResult()
    {
        ProcessResult result = await ProcessHelper.RunAsync(
            "winix-definitely-not-a-real-command-9999", Array.Empty<string>());

        Assert.True(result.IsNotFound);
    }

    [Fact]
    public void IsOnPath_Dotnet_ReturnsTrue()
    {
        bool found = ProcessHelper.IsOnPath("dotnet");

        Assert.True(found);
    }

    [Fact]
    public void IsOnPath_FakeCommand_ReturnsFalse()
    {
        bool found = ProcessHelper.IsOnPath("winix-definitely-not-a-real-command-9999");

        Assert.False(found);
    }
}
