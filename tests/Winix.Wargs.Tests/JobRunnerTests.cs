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

    [Fact]
    public async Task RunAsync_KeepOrder_OutputsInInputOrder()
    {
        var stdout = new StringWriter();
        var options = new JobRunnerOptions(Parallelism: 4, Strategy: BufferStrategy.KeepOrder);
        var runner = new JobRunner(options);
        var invocations = Enumerable.Range(1, 4)
            .Select(i => MakeEchoInvocation($"item{i}", i))
            .ToArray();

        await runner.RunAsync(invocations, stdout, TextWriter.Null);

        string output = stdout.ToString();
        int pos1 = output.IndexOf("item1");
        int pos2 = output.IndexOf("item2");
        int pos3 = output.IndexOf("item3");
        int pos4 = output.IndexOf("item4");

        Assert.True(pos1 >= 0, "item1 should be in output");
        Assert.True(pos2 >= 0, "item2 should be in output");
        Assert.True(pos3 >= 0, "item3 should be in output");
        Assert.True(pos4 >= 0, "item4 should be in output");
        Assert.True(pos1 < pos2, "item1 should appear before item2");
        Assert.True(pos2 < pos3, "item2 should appear before item3");
        Assert.True(pos3 < pos4, "item3 should appear before item4");
    }

    [Fact]
    public async Task RunAsync_LineBuffered_DoesNotCaptureOutput()
    {
        var options = new JobRunnerOptions(Strategy: BufferStrategy.LineBuffered);
        var runner = new JobRunner(options);
        var invocations = new[] { MakeEchoInvocation("hello", 1) };

        var result = await runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null);

        Assert.Single(result.Jobs);
        Assert.Null(result.Jobs[0].Output);
    }

    [Fact]
    public async Task RunAsync_ShellFallback_RunsBuiltinViaShell()
    {
        // Use a command name that doesn't exist as a standalone executable but
        // is a shell builtin. "echo" is a builtin on Windows cmd.exe. On Unix
        // /usr/bin/echo exists so direct exec succeeds — use "type" which is
        // a builtin in both cmd.exe and bash (type is also a standalone on some
        // systems, but this test verifies the fallback path doesn't break anything).
        var options = new JobRunnerOptions(ShellFallback: true);
        var runner = new JobRunner(options);

        // Use a guaranteed non-existent command that will trigger the fallback,
        // then verify the fallback itself works by checking cmd /c echo on Windows
        // or sh -c echo on Unix.
        string command;
        string[] arguments;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // "echo" without cmd /c won't be found as an exe on a stock Windows install
            // unless Git Bash's /usr/bin is on PATH. Use ver instead — it's cmd-only.
            command = "ver";
            arguments = Array.Empty<string>();
        }
        else
        {
            // On Unix, test with "echo" which exists as /usr/bin/echo — direct exec works,
            // so instead use ":" (the shell no-op) which is a builtin with no standalone binary.
            command = ":";
            arguments = Array.Empty<string>();
        }

        var invocations = new[]
        {
            new CommandInvocation(command, arguments, command, new[] { "test" })
        };

        var result = await runner.RunAsync(invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, result.TotalJobs);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Jobs[0].ChildExitCode);
    }

    [Fact]
    public async Task RunAsync_ShellFallbackDisabled_FailsForBuiltin()
    {
        var options = new JobRunnerOptions(ShellFallback: false);
        var runner = new JobRunner(options);
        var invocations = new[]
        {
            new CommandInvocation(
                "nonexistent_command_xyzzy_99999", Array.Empty<string>(),
                "nonexistent_command_xyzzy_99999", new[] { "item" })
        };

        var result = await runner.RunAsync(invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, result.Failed);
        Assert.Equal(-1, result.Jobs[0].ChildExitCode);
    }

    [Fact]
    public async Task RunAsync_ParallelFailFast_CancelsInFlightJobs()
    {
        // Job 1 fails immediately; jobs 2-4 are long-running sleeps.
        // With --fail-fast + --parallel, the sleep jobs should be cancelled
        // and the total wall time should be much less than 30 seconds.
        var options = new JobRunnerOptions(Parallelism: 4, FailFast: true);
        var runner = new JobRunner(options);

        CommandInvocation sleepInvocation = MakeSleepInvocation(30, "slow");

        var invocations = new[]
        {
            MakeExitInvocation(1, "fail"),
            sleepInvocation,
            sleepInvocation,
            sleepInvocation,
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await runner.RunAsync(invocations, TextWriter.Null, TextWriter.Null);
        sw.Stop();

        Assert.True(result.Failed >= 1, "At least one job should have failed");
        // The key assertion: if fail-fast cancellation works, we finish in well under
        // 30 seconds. The sleep jobs may show as Skipped (cancelled before start) or
        // Failed (killed mid-flight), depending on timing — either is correct.
        Assert.True(sw.Elapsed.TotalSeconds < 15, $"Expected fast completion but took {sw.Elapsed.TotalSeconds:F1}s");
        int nonSucceeded = result.Failed + result.Skipped;
        Assert.True(nonSucceeded >= 2, $"Expected at least 2 non-succeeded jobs but got {nonSucceeded}");
    }

    /// <summary>
    /// Helper: cross-platform command that sleeps for the given number of seconds.
    /// </summary>
    private static CommandInvocation MakeSleepInvocation(int seconds, string item)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // ping -n N localhost has ~1 second per ping — crude but available everywhere
            return new CommandInvocation(
                "ping", new[] { "-n", (seconds + 1).ToString(), "127.0.0.1" },
                $"ping -n {seconds + 1} 127.0.0.1", new[] { item });
        }
        return new CommandInvocation(
            "sleep", new[] { seconds.ToString() },
            $"sleep {seconds}", new[] { item });
    }
}
