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
    public void IsOnPath_DoesNotExecuteTheTarget()
    {
        // The PATH-walk contract: IsOnPath must LOCATE an executable by file existence,
        // never RUN it. The pre-F6 implementation spawned `command --version` and killed
        // the child — a regression to that shape would re-execute the probed target.
        // Prove non-execution DETERMINISTICALLY: drop a script on PATH that writes a
        // sentinel file *if it runs*, then assert IsOnPath found it (true) yet the
        // sentinel was never created. Replaces a global process-count proxy that flaked
        // on CI boxes running parallel dotnet processes (a 13->15 jump tripped its +1
        // tolerance) and which — by the prior author's own note — a spawn-and-kill
        // regression could slip past anyway.
        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"winix-noexec-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(tempDir);

        string sentinel = System.IO.Path.Combine(tempDir, "executed.sentinel");
        const string probeName = "winix-noexec-probe";

        if (OperatingSystem.IsWindows())
        {
            // PATHEXT walk resolves the .cmd from the bare name. If executed, the
            // redirect-first form writes "ran" into the sentinel.
            string cmdPath = System.IO.Path.Combine(tempDir, probeName + ".cmd");
            System.IO.File.WriteAllText(cmdPath, $"@echo off\r\n> \"{sentinel}\" echo ran\r\n");
        }
        else
        {
            // Bare-name File.Exists resolves it; the exec bit gives the guard teeth so a
            // spawn regression would actually run it and create the sentinel.
            string scriptPath = System.IO.Path.Combine(tempDir, probeName);
            System.IO.File.WriteAllText(scriptPath, $"#!/bin/sh\necho ran > \"{sentinel}\"\n");
            System.IO.File.SetUnixFileMode(
                scriptPath,
                System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute);
        }

        string? originalPath = System.Environment.GetEnvironmentVariable("PATH");
        try
        {
            System.Environment.SetEnvironmentVariable("PATH", tempDir + System.IO.Path.PathSeparator + originalPath);

            bool found = ProcessHelper.IsOnPath(probeName);

            Assert.True(found, "IsOnPath should locate the probe via the PATH walk.");
            Assert.False(
                System.IO.File.Exists(sentinel),
                "IsOnPath must not execute the probed target — the sentinel file proves it was run.");
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("PATH", originalPath);
            try
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
