using System.Runtime.InteropServices;
using Winix.Wargs;
using Xunit;

namespace Winix.Wargs.Tests;

public class JobRunnerTests
{
    // Helper: cross-platform echo command
    private static CommandInvocation MakeEchoInvocation(string text, int jobIndex)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new CommandInvocation(
                "cmd", new[] { "/c", "echo", text },
                $"cmd /c echo {text}", new[] { text });
        }
        return new CommandInvocation(
            "echo", new[] { text },
            $"echo {text}", new[] { text });
    }

    // Helper: cross-platform command that exits with a specific code
    private static CommandInvocation MakeExitInvocation(int code, string item)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new CommandInvocation(
                "cmd", new[] { "/c", $"exit /b {code}" },
                $"cmd /c exit /b {code}", new[] { item });
        }
        return new CommandInvocation(
            "sh", new[] { "-c", $"exit {code}" },
            $"sh -c 'exit {code}'", new[] { item });
    }

    [Fact]
    public async Task RunAsync_Sequential_ExecutesAllJobs()
    {
        var options = new JobRunnerOptions();
        var runner = new JobRunner(options);
        var invocations = new[]
        {
            MakeEchoInvocation("hello", 1),
            MakeEchoInvocation("world", 2),
        };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(2, result.TotalJobs);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public async Task RunAsync_Sequential_CapturesOutput()
    {
        var options = new JobRunnerOptions();
        var runner = new JobRunner(options);
        var invocations = new[] { MakeEchoInvocation("hello", 1) };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Single(result.Jobs);
        Assert.NotNull(result.Jobs[0].Output);
        Assert.Contains("hello", result.Jobs[0].Output!);
    }

    [Fact]
    public async Task RunAsync_ChildFailure_RecordsExitCode()
    {
        var options = new JobRunnerOptions();
        var runner = new JobRunner(options);
        var invocations = new[] { MakeExitInvocation(42, "bad") };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, result.Failed);
        Assert.Equal(42, result.Jobs[0].ChildExitCode);
    }

    [Fact]
    public async Task RunAsync_CommandNotFound_RecordsFailure()
    {
        var options = new JobRunnerOptions();
        var runner = new JobRunner(options);
        var invocations = new[]
        {
            new CommandInvocation(
                "nonexistent_command_xyzzy_12345", Array.Empty<string>(),
                "nonexistent_command_xyzzy_12345", new[] { "item" })
        };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, result.Failed);
        Assert.True(result.Jobs[0].ChildExitCode != 0);
    }

    [Fact]
    public async Task RunAsync_DryRun_DoesNotExecute()
    {
        var stdout = new StringWriter();
        var options = new JobRunnerOptions(DryRun: true);
        var runner = new JobRunner(options);
        var invocations = new[] { MakeEchoInvocation("hello", 1) };

        var result = await runner.RunAsync(invocations, stdout, TextWriter.Null);

        Assert.Equal(0, result.TotalJobs);
        Assert.Contains("echo", stdout.ToString());
    }

    [Fact]
    public async Task RunAsync_Verbose_PrintsCommandToStderr()
    {
        var stderr = new StringWriter();
        var options = new JobRunnerOptions(Verbose: true);
        var runner = new JobRunner(options);
        var invocations = new[] { MakeEchoInvocation("hello", 1) };

        var result = await runner.RunAsync(invocations, TextWriter.Null, stderr);

        Assert.Contains("echo", stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_JobIndex_IsOneBased()
    {
        var options = new JobRunnerOptions();
        var runner = new JobRunner(options);
        var invocations = new[]
        {
            MakeEchoInvocation("a", 1),
            MakeEchoInvocation("b", 2),
        };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, result.Jobs[0].JobIndex);
        Assert.Equal(2, result.Jobs[1].JobIndex);
    }

    [Fact]
    public async Task RunAsync_Parallel_ExecutesAllJobs()
    {
        var options = new JobRunnerOptions(Parallelism: 4);
        var runner = new JobRunner(options);
        var invocations = Enumerable.Range(1, 8)
            .Select(i => MakeEchoInvocation($"item{i}", i))
            .ToArray();

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(8, result.TotalJobs);
        Assert.Equal(8, result.Succeeded);
    }

    [Fact]
    public async Task RunAsync_FailFast_StopsSpawningNewJobs()
    {
        var options = new JobRunnerOptions(FailFast: true);
        var runner = new JobRunner(options);
        var invocations = new[]
        {
            MakeExitInvocation(1, "fail"),
            MakeEchoInvocation("should-not-run", 2),
            MakeEchoInvocation("should-not-run", 3),
        };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, result.Failed);
        Assert.Equal(2, result.Skipped);
    }

    [Fact]
    public async Task RunAsync_Confirm_SkipsDeclinedJobs()
    {
        int callCount = 0;
        var options = new JobRunnerOptions(
            Confirm: true,
            ConfirmPrompt: _ =>
            {
                callCount++;
                return callCount != 2; // decline the second job
            });
        var runner = new JobRunner(options);
        var invocations = new[]
        {
            MakeEchoInvocation("a", 1),
            MakeEchoInvocation("b", 2),
            MakeEchoInvocation("c", 3),
        };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(1, result.Skipped);
    }
}
