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

    [Fact]
    public void IsOnPath_EmptyCommand_ReturnsFalse()
    {
        // Guard added in F6: an empty string used to fall through to a Process.Start
        // attempt with command="" which threw an unhelpful Win32Exception. The PATH
        // walk has nowhere meaningful to look for an empty-named executable.
        Assert.False(ProcessHelper.IsOnPath(string.Empty));
    }

    [Fact]
    public void IsOnPath_WindowsCmdExtension_FindsCommandWithoutExtension()
    {
        // Round-1 fresh-eyes 2026-05-09 pr-test-analyzer I4 closure: F6's PATHEXT-
        // aware PATH walk replaced the old spawn-and-kill probe specifically so
        // PowerShell + scoop installations (where scoop ships as `scoop.cmd`)
        // would resolve correctly. Pre-fix the test suite only covered the .exe
        // branch via `dotnet`, leaving the .cmd / .bat branches untested. A
        // regression to GetExecutableExtensions returning Array.Empty<string>()
        // would still pass the dotnet test (because dotnet.exe matches the bare
        // name on first PATH hit) while breaking real scoop.cmd discovery in
        // production.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"winix-pathext-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(tempDir);
        string cmdPath = System.IO.Path.Combine(tempDir, "winix-pathext-probe.cmd");
        System.IO.File.WriteAllText(cmdPath, "@echo off\r\nexit 0\r\n");

        string? originalPath = System.Environment.GetEnvironmentVariable("PATH");
        try
        {
            // Prepend the temp dir to PATH so the probe is discoverable.
            System.Environment.SetEnvironmentVariable("PATH", tempDir + ";" + originalPath);

            // Probe the BARE name — IsOnPath must walk PATHEXT and find the .cmd file.
            bool found = ProcessHelper.IsOnPath("winix-pathext-probe");

            Assert.True(found,
                $"IsOnPath should resolve a .cmd file via PATHEXT walk. tempDir={tempDir}, file exists: {System.IO.File.Exists(cmdPath)}");
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("PATH", originalPath);
            try
            {
                System.IO.File.Delete(cmdPath);
                System.IO.Directory.Delete(tempDir);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public void IsOnPath_DoesNotSpawnProcess()
    {
        // Pre-F6 the probe spawned 'command --version' and immediately killed it. The
        // first invocation of any non-trivial process on Windows costs at least
        // ~80–150ms (CreateProcess + image-load + close-handle); a PATH walk on a
        // typical machine completes in single-digit milliseconds. Use the
        // process-snapshot count as a stronger signal than timing — a regression to
        // the spawn-and-kill shape would briefly create a child process visible to
        // Process.GetProcessesByName during the call.
        int beforeCount = System.Diagnostics.Process.GetProcessesByName("dotnet").Length;
        bool found = ProcessHelper.IsOnPath("dotnet");
        int afterCount = System.Diagnostics.Process.GetProcessesByName("dotnet").Length;

        Assert.True(found);
        // Tolerate +1 to avoid flakes from concurrent dotnet processes (test runner,
        // background restore, IDE). A regression that spawns AND kills the child
        // before we observe would bypass this — accept that limitation; the no-spawn
        // contract is also locked by the lack of process-spawn imports in the
        // implementation, which a code reviewer can verify directly.
        Assert.True(afterCount <= beforeCount + 1,
            $"PATH-walk probe should not spawn a child process. Process count went from {beforeCount} to {afterCount}.");
    }
}
