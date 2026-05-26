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

    [SkippableFact]
    public void Run_PermissionDenied_ThrowsCommandNotExecutableException()
    {
        // Create a file with no execute permission. On Windows the EACCES error code
        // is harder to trigger reliably without ACL manipulation, so skip on Windows.
        Skip.If(OperatingSystem.IsWindows(), "Unix-only — EACCES on chmod-cleared file is hard to trigger reliably on Windows without ACL plumbing.");
        if (OperatingSystem.IsWindows())
        {
            return; // redundant, satisfies CA1416 analyzer
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

    // Round-2 review TA-I6 — gated to Windows because Linux kernel behaviour for an
    // executable text file is non-deterministic: some distros' glibc posix_spawn falls
    // back to /bin/sh execution (no exception, child runs as a script), others return
    // ENOEXEC. The contract "bad EXE format throws InvalidOperationException" is reliable
    // only on Windows where ERROR_BAD_EXE_FORMAT is produced deterministically by
    // CreateProcess. macOS behaviour is similarly variable (depends on signing posture
    // and kernel version). Per CLAUDE.md the test must use SkippableFact + Skip.IfNot
    // rather than early-return — the early-return form silently CI-passes on Linux/macOS.
    [SkippableFact]
    public void Run_BadExecutableFormat_ThrowsInvalidOperationException()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only — Linux/macOS kernel behaviour for +x text files is non-deterministic.");
        if (!OperatingSystem.IsWindows()) return; // satisfies CA1416 alongside Skip.IfNot

        // Create a temp file with invalid EXE content — triggers ERROR_BAD_EXE_FORMAT
        // on Windows. CreateProcess fails with this error code, which CommandRunner.Run
        // surfaces as InvalidOperationException (not CommandNotFound, because the file
        // does exist; not CommandNotExecutable, because permissions aren't the issue).
        string tempFile = Path.Combine(Path.GetTempPath(), $"timeit-test-{Guid.NewGuid()}.exe");
        try
        {
            File.WriteAllText(tempFile, "this is not an executable");

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
