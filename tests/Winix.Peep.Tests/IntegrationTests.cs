using System.Globalization;
using Xunit;
using Winix.Peep;

namespace Winix.Peep.Tests;

/// <summary>
/// End-to-end integration tests that exercise multiple library components together.
/// These tests use real processes and the filesystem, so they are slower than unit tests
/// but catch wiring issues that unit tests miss.
/// </summary>
[Collection("FileWatcherIntegration")]
public class IntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public IntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"peep-integ-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        TryDeleteDirectory(_tempDir);
    }

    [Fact]
    public async Task CommandExecutor_CapturesRealCommandOutput()
    {
        PeepResult result = await CommandExecutor.RunAsync(
            "dotnet", new[] { "--version" }, TriggerSource.Initial);

        Assert.Equal(0, result.ExitCode);
        // dotnet --version outputs something like "10.0.100"
        Assert.Contains(".", result.Output);
    }

    [Fact]
    public async Task CommandExecutor_OnceMode_Simulation()
    {
        // Simulate --once: run command once, capture output, verify exit code, done.
        PeepResult result = await CommandExecutor.RunAsync(
            "dotnet", new[] { "--version" }, TriggerSource.Initial);

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
        Assert.True(result.Duration > TimeSpan.Zero);
        Assert.Equal(TriggerSource.Initial, result.Trigger);
    }

    [Fact]
    public async Task FileWatcher_DetectsFileCreation()
    {
        string subDir = Path.Combine(_tempDir, "watched");
        Directory.CreateDirectory(subDir);

        string originalDir = Directory.GetCurrentDirectory();
        FileWatcher? watcher = null;
        try
        {
            Directory.SetCurrentDirectory(_tempDir);

            watcher = new FileWatcher(new[] { "watched/*.txt" }, debounceMs: 50);
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.FileChanged += () => tcs.TrySetResult();
            watcher.Start();

            // Give the watcher a moment to register with the OS
            await Task.Delay(100);

            // Create a file that matches the glob
            await File.WriteAllTextAsync(Path.Combine(subDir, "test.txt"), "integration test");

            Task completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            Assert.Same(tcs.Task, completed);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
            watcher?.Dispose();
        }
    }

    [Fact]
    public async Task AutoExit_ExitOnSuccess_DetectsSuccess()
    {
        // Run a command that succeeds, then check if the exit-on-success condition is met.
        PeepResult result = await CommandExecutor.RunAsync(
            "dotnet", new[] { "--version" }, TriggerSource.Initial);

        // exit-on-success triggers when exit code becomes 0
        bool shouldExit = result.ExitCode == 0;
        Assert.True(shouldExit);
    }

    [Fact]
    public async Task AutoExit_ExitOnSuccess_DoesNotTriggerOnFailure()
    {
        PeepResult result = await CommandExecutor.RunAsync(
            "dotnet", new[] { "nonexistent-command-that-does-not-exist" }, TriggerSource.Initial);

        bool shouldExit = result.ExitCode == 0;
        Assert.False(shouldExit);
    }

    [Fact]
    public async Task AutoExit_ExitOnChange_DetectsOutputChange()
    {
        // Run the same command twice -- for stable output (dotnet --version) they should match.
        PeepResult result1 = await CommandExecutor.RunAsync(
            "dotnet", new[] { "--version" }, TriggerSource.Initial);
        PeepResult result2 = await CommandExecutor.RunAsync(
            "dotnet", new[] { "--version" }, TriggerSource.Interval);

        Assert.Equal(result1.Output, result2.Output);

        // exit-on-change would NOT trigger here (outputs are the same)
        bool outputChanged = result1.Output != result2.Output;
        Assert.False(outputChanged);
    }

    [Fact]
    public void ScreenRenderer_FullRender_ProducesOutput()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet --version",
            timestamp: new DateTime(2026, 3, 29, 12, 0, 0),
            exitCode: 0,
            runCount: 1,
            isPaused: false,
            useColor: false);

        string? watchLine = ScreenRenderer.FormatWatchLine(
            new[] { "src/**/*.cs" }, useColor: false);

        using var writer = new StringWriter();
        ScreenRenderer.Render(
            writer,
            header,
            watchLine,
            output: "10.0.100\n",
            terminalHeight: 24,
            scrollOffset: 0,
            showHeader: true);

        string rendered = writer.ToString();
        Assert.Contains("Every 2.0s", rendered);
        Assert.Contains("dotnet --version", rendered);
        Assert.Contains("10.0.100", rendered);
        Assert.Contains("Watching:", rendered);
        Assert.Contains("src/**/*.cs", rendered);
    }

    [Fact]
    public void ScreenRenderer_FullRender_WithoutHeader_OmitsHeaderAndWatchLine()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "echo hi",
            timestamp: DateTime.Now,
            exitCode: 0,
            runCount: 1,
            isPaused: false,
            useColor: false);

        using var writer = new StringWriter();
        ScreenRenderer.Render(
            writer,
            header,
            watchLine: null,
            output: "hi\n",
            terminalHeight: 24,
            scrollOffset: 0,
            showHeader: false);

        string rendered = writer.ToString();
        Assert.DoesNotContain("Every", rendered);
        Assert.Contains("hi", rendered);
    }

    [Fact]
    public void Formatting_FullSessionJson_IsValid()
    {
        string json = Formatting.FormatJson(
            exitCode: 0,
            exitReason: "manual",
            runs: 5,
            lastChildExitCode: 0,
            durationSeconds: 30.0,
            command: "git status",
            lastOutput: null,
            toolName: "peep",
            version: "0.1.0");

        Assert.Contains("\"tool\":\"peep\"", json);
        Assert.Contains("\"version\":\"0.1.0\"", json);
        Assert.Contains("\"exit_code\":0", json);
        Assert.Contains("\"exit_reason\":\"manual\"", json);
        Assert.Contains("\"runs\":5", json);
        Assert.Contains("\"last_child_exit_code\":0", json);
        Assert.Contains("\"duration_seconds\":30.000", json);
        Assert.Contains("\"command\":\"git status\"", json);
    }

    [Fact]
    public void Formatting_FullSessionJson_WithOutput_IncludesStrippedOutput()
    {
        // Output with ANSI escape sequences that should be stripped
        string outputWithAnsi = "\x1b[32mOK\x1b[0m all tests passed";

        string json = Formatting.FormatJson(
            exitCode: 0,
            exitReason: "exit_on_success",
            runs: 3,
            lastChildExitCode: 0,
            durationSeconds: 12.5,
            command: "dotnet test",
            lastOutput: outputWithAnsi,
            toolName: "peep",
            version: "0.1.0");

        Assert.Contains("\"last_output\":\"OK all tests passed\"", json);
        // Verify the ANSI escape sequence was stripped -- the original had \x1b[32m and \x1b[0m
        Assert.DoesNotContain("[32m", json);
        Assert.DoesNotContain("[0m", json);
    }

    [Fact]
    public async Task FullWorkflow_RunCommand_FormatHeader_RenderScreen()
    {
        // End-to-end: execute a real command, format the result, render a screen.
        PeepResult result = await CommandExecutor.RunAsync(
            "dotnet", new[] { "--version" }, TriggerSource.Initial);

        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet --version",
            timestamp: DateTime.Now,
            exitCode: result.ExitCode,
            runCount: 1,
            isPaused: false,
            useColor: false);

        using var writer = new StringWriter();
        ScreenRenderer.Render(
            writer,
            header,
            watchLine: null,
            output: result.Output,
            terminalHeight: 24,
            scrollOffset: 0,
            showHeader: true);

        string rendered = writer.ToString();

        // Header should contain the interval and command
        Assert.Contains("Every 2.0s", rendered);
        Assert.Contains("dotnet --version", rendered);
        // Output should contain the version number
        Assert.Contains(".", rendered);
        // Should show exit code and run count
        Assert.Contains("[exit 0]", rendered);
        Assert.Contains("[run #1]", rendered);
    }

    [Fact]
    public async Task FullWorkflow_RunCommand_FormatJson_ProducesValidSummary()
    {
        // End-to-end: execute a real command, produce JSON summary.
        PeepResult result = await CommandExecutor.RunAsync(
            "dotnet", new[] { "--version" }, TriggerSource.Initial);

        string json = Formatting.FormatJson(
            exitCode: 0,
            exitReason: "manual",
            runs: 1,
            lastChildExitCode: result.ExitCode,
            durationSeconds: result.Duration.TotalSeconds,
            command: "dotnet --version",
            lastOutput: result.Output,
            toolName: "peep",
            version: "0.1.0");

        Assert.Contains("\"tool\":\"peep\"", json);
        Assert.Contains("\"runs\":1", json);
        Assert.Contains("\"last_child_exit_code\":0", json);
        Assert.Contains("\"command\":\"dotnet --version\"", json);
        Assert.Contains("\"last_output\":", json);
    }

    /// <summary>
    /// Best-effort temp directory cleanup with retry for OS handle release delays.
    /// </summary>
    private static void TryDeleteDirectory(string path)
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                return;
            }
            catch (IOException) when (i < 9)
            {
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException) when (i < 9)
            {
                Thread.Sleep(200);
            }
        }
    }
}
