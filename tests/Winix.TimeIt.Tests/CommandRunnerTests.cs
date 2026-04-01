using Xunit;
using Winix.TimeIt;
using Yort.ShellKit;

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

        // On Windows, peak memory always comes from the process handle (reliable).
        // On Unix, ru_maxrss is a high-water mark across all waited children — the
        // delta can be zero if a previous test already waited for a child with higher
        // peak RSS, in which case null is returned (indeterminate). Either non-null
        // positive or null is acceptable; only 0 would be wrong.
        if (result.PeakMemoryBytes.HasValue)
        {
            Assert.True(result.PeakMemoryBytes.Value > 0, "Peak memory should be positive when available");
        }
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

    [Fact]
    public void Run_PermissionDenied_ThrowsCommandNotExecutableException()
    {
        // Create a file with no execute permission. On Windows the EACCES error code
        // is harder to trigger reliably without ACL manipulation, so skip on Windows.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string tempFile = Path.Combine(Path.GetTempPath(), $"timeit-test-noexec-{Guid.NewGuid()}");
        try
        {
            File.WriteAllText(tempFile, "#!/bin/sh\nexit 0\n");
            File.SetUnixFileMode(tempFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            Assert.Throws<CommandNotExecutableException>(
                () => CommandRunner.Run(tempFile, Array.Empty<string>()));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Run_BadExecutableFormat_ThrowsInvalidOperationException()
    {
        // Create a temp file with invalid EXE content — triggers ERROR_BAD_EXE_FORMAT
        // on Windows or ENOEXEC on Linux. Previously this was misreported as
        // CommandNotFoundException; now it surfaces as InvalidOperationException.
        string tempFile = Path.Combine(Path.GetTempPath(), $"timeit-test-{Guid.NewGuid()}.exe");
        try
        {
            File.WriteAllText(tempFile, "this is not an executable");
            if (!OperatingSystem.IsWindows())
            {
                // Make it executable on Unix so we get past the EACCES check
                File.SetUnixFileMode(tempFile,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var ex = Assert.Throws<InvalidOperationException>(
                () => CommandRunner.Run(tempFile, Array.Empty<string>()));

            Assert.Contains("failed to start", ex.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
