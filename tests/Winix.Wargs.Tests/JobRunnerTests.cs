using System.Runtime.InteropServices;
using System.Text;
using Winix.Wargs;
using Xunit;

namespace Winix.Wargs.Tests;

public class JobRunnerTests
{
    /// <summary>
    /// TextWriter that throws <see cref="IOException"/> on Write/WriteAsync to simulate a
    /// downstream pipe being closed (e.g. <c>wargs ... | head -1</c>). Used to pin that
    /// JobRunner's flush paths swallow the IOException and abort gracefully rather than
    /// crashing the whole run.
    /// </summary>
    private sealed class BrokenPipeWriter : TextWriter
    {
        public int WriteAttempts { get; private set; }
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(string? value)
        {
            WriteAttempts++;
            throw new IOException("simulated broken pipe");
        }
        public override void Write(char value)
        {
            WriteAttempts++;
            throw new IOException("simulated broken pipe");
        }
        public override Task WriteAsync(string? value)
        {
            WriteAttempts++;
            return Task.FromException(new IOException("simulated broken pipe"));
        }
    }

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
        // Round-3 Q2: at least one of the cancelled-in-flight jobs must classify as
        // Skipped (the fail-fast OCE arm at JobRunner.cs:359-368 returns Skipped). If a
        // future change misroutes that arm to Failed, total non-succeeded count would
        // still satisfy the previous assertion but the specific contract would silently
        // regress. Pinning Skipped >= 1 disambiguates the fail-fast arm from a generic
        // failure path.
        Assert.True(result.Skipped >= 1,
            $"Fail-fast must produce at least one Skipped job (got {result.Skipped} skipped, {result.Failed} failed).");
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

    // -- Round-1 review: pinning tests for spawn-fault classification, cancellation, and
    //    empty-input handling. --

    [Fact]
    public async Task RunAsync_NoInvocations_ReturnsZeroJobsWithoutThrowing()
    {
        // Library-level pin for the "no_input" path in Program.cs. The library itself must
        // accept an empty list and return a zero-counts result so the caller can decide what
        // to do (the console app turns this into the no_input exit reason).
        var runner = new JobRunner(new JobRunnerOptions());
        var result = await runner.RunAsync(
            Array.Empty<CommandInvocation>(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, result.TotalJobs);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Skipped);
        Assert.Empty(result.Jobs);
    }

    [Fact]
    public async Task RunAsync_EmptyFileName_ClassifiesAsSpawnFailureWithFaultMessage()
    {
        // Process.Start with empty FileName throws InvalidOperationException, NOT
        // Win32Exception. Before the round-1 fix this escaped into the parallel-loop broad
        // catch with no diagnostic. The fix broadened the spawn-failure classification and
        // surfaces the original exception text on JobResult.FaultMessage so the user can
        // see what went wrong rather than a bare child_failed.
        var invocation = new CommandInvocation(
            Command: "",
            Arguments: Array.Empty<string>(),
            DisplayString: "(empty)",
            SourceItems: new[] { "x" });

        var runner = new JobRunner(new JobRunnerOptions(ShellFallback: false));
        var result = await runner.RunAsync(
            new[] { invocation }, TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, result.Failed);
        JobResult job = Assert.Single(result.Jobs);
        Assert.Equal(-1, job.ChildExitCode);
        Assert.False(job.Skipped);
        Assert.NotNull(job.FaultMessage);
        Assert.Contains("InvalidOperationException", job.FaultMessage);
    }

    // Note: this test deliberately tolerates BOTH OCE-throw and clean-result-with-skipped
    // because its purpose is to pin the no-hang invariant (semaphore slots aren't leaked
    // on pre-cancel). The tighter "external cancel must propagate OCE" contract is pinned
    // separately by RunAsync_Parallel_ExternalCancelMidRun_PropagatesOperationCanceledException
    // (round 3). Don't tighten this test's tolerance — it would regress the no-hang signal.
    [Fact]
    public async Task RunAsync_PreCancelledToken_ParallelDoesNotHangAndReleasesSemaphoreSlots()
    {
        // Round-1 silent-failure-hunter Critical: passing the outer token to Task.Run meant
        // a cancel between semaphore acquisition and Task.Run scheduling caused the task to
        // transition to Canceled WITHOUT running its body — the finally that releases the
        // semaphore never fired, leaking slots. With many invocations this could hang
        // subsequent runs. The fix removed the token argument from Task.Run; the body now
        // checks the token explicitly. This test pins that pre-cancellation produces a
        // bounded result (no hang, no orphaned slots) and that no job reports as a
        // hard failure with no diagnostic.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var invocations = Enumerable.Range(0, 20)
            .Select(i => MakeEchoInvocation($"item{i}", i + 1))
            .ToArray();

        var runner = new JobRunner(new JobRunnerOptions(Parallelism: 4));
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // RunAsync is allowed to throw OCE OR to return a result with all jobs Skipped — both
        // are acceptable outcomes for a pre-cancelled run. What is NOT acceptable is hanging
        // or leaking semaphore slots (the test would time out).
        try
        {
            var result = await runner.RunAsync(
                invocations, TextWriter.Null, TextWriter.Null, cts.Token);
            sw.Stop();
            Assert.Equal(20, result.TotalJobs);
            // Either skipped (preferred) or zero successes — never a half-completed dangling state.
            Assert.True(result.Succeeded + result.Skipped + result.Failed == 20);
            // Round-4 refinement: extend the SkippedJobs_NeverCarryFaultMessage invariant
            // to the pre-cancel skip path. Skipped jobs from cancellation must never
            // carry FaultMessage — formatters rely on the (Skipped=true ⇔ FaultMessage=null)
            // invariant for output-mode consistency.
            Assert.All(result.Jobs.Where(j => j.Skipped), j => Assert.Null(j.FaultMessage));
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
        }

        Assert.True(sw.Elapsed.TotalSeconds < 10,
            $"Pre-cancelled parallel run should complete near-instantly; took {sw.Elapsed.TotalSeconds:F1}s — possible semaphore leak or deadlock.");
    }

    [Fact]
    public async Task RunAsync_EmptyFileName_FaultedTaskInKeepOrderProducesFailedJob()
    {
        // Pin the KeepOrder + faulted-task interaction. A spawn failure must not break the
        // KeepOrder flush loop; the failed job slot is reported with FaultMessage and other
        // jobs still flush their captured output in order.
        var bad = new CommandInvocation("", Array.Empty<string>(), "(bad)", new[] { "bad" });
        var ok = MakeEchoInvocation("ok", 2);

        var stdout = new StringWriter();
        var runner = new JobRunner(new JobRunnerOptions(
            Parallelism: 2,
            Strategy: BufferStrategy.KeepOrder,
            ShellFallback: false));

        var result = await runner.RunAsync(
            new[] { bad, ok }, stdout, TextWriter.Null);

        Assert.Equal(2, result.TotalJobs);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Succeeded);
        // The OK job's output reaches stdout in input order even though the previous job
        // faulted with no output of its own.
        Assert.Contains("ok", stdout.ToString());
        // The failed job carries a FaultMessage explaining why.
        JobResult failedJob = result.Jobs.First(j => j.ChildExitCode != 0);
        Assert.NotNull(failedJob.FaultMessage);
    }

    // -- Round-2 review: broken-pipe handling, many-job fail-fast race, real KeepOrder
    //    re-ordering, and Unix shell-injection canary. --

    [Fact]
    public async Task RunAsync_Sequential_BrokenStdoutPipe_DoesNotCrashRun()
    {
        // `wargs ... | head -1` is a normal usage pattern: the downstream consumer closes
        // the pipe after one read, the next stdout flush raises IOException. Round 1 left
        // sequential flushes unwrapped — the IOException escaped to Main as "unexpected
        // error" with exit 126. Round 2 wraps via SafeWriteAsync; this test pins that
        // behaviour: the run completes, abort flag stops further flushing, no exception
        // escapes RunAsync.
        var invocations = new[]
        {
            MakeEchoInvocation("first", 1),
            MakeEchoInvocation("second", 2),
            MakeEchoInvocation("third", 3),
        };

        var brokenStdout = new BrokenPipeWriter();
        var runner = new JobRunner(new JobRunnerOptions());

        // Should not throw — the IOException must be swallowed.
        var result = await runner.RunAsync(invocations, brokenStdout, TextWriter.Null);

        Assert.Equal(3, result.TotalJobs);
        // Round-4 refinement: the first job's flush failure sets `abort = true`; subsequent
        // jobs short-circuit at the loop top with Skipped before reaching their flush, so
        // exactly one Write attempt should have happened. The earlier `>= 1` tolerance was
        // weak; tightening to `== 1` catches a regression where abort isn't set on broken
        // pipe and every subsequent job re-discovers the failure.
        Assert.Equal(1, brokenStdout.WriteAttempts);
    }

    [Fact]
    public async Task RunAsync_ParallelJobBuffered_BrokenStdoutPipe_DoesNotCrashRun()
    {
        var invocations = Enumerable.Range(0, 8)
            .Select(i => MakeEchoInvocation($"item{i}", i + 1))
            .ToArray();

        var brokenStdout = new BrokenPipeWriter();
        var runner = new JobRunner(new JobRunnerOptions(
            Parallelism: 4,
            Strategy: BufferStrategy.JobBuffered));

        // Should not throw — every flush IOException must be swallowed and the abort
        // flag must stop further dispatch.
        var result = await runner.RunAsync(invocations, brokenStdout, TextWriter.Null);

        Assert.Equal(8, result.TotalJobs);
    }

    [Fact]
    public async Task RunAsync_DryRun_BrokenStdoutPipe_DoesNotCrashRun()
    {
        // Round-8 SFH C1 / TA C1: RunDryRun was the lone unwrapped stdout path. Without
        // protection, `wargs --dry-run --json | head -1` raised IOException on the second
        // WriteLine, escaped JobRunner.RunAsync, and surfaced as unexpected_error/exit 126
        // instead of dry_run/exit 0. Round-8 wraps each WriteLine in try/catch IOException +
        // ObjectDisposedException, breaking the loop on first failure so Main can still
        // emit the dry_run envelope on stderr.
        var brokenStdout = new BrokenPipeWriter();
        var runner = new JobRunner(new JobRunnerOptions(DryRun: true));
        var invocations = new[]
        {
            MakeEchoInvocation("a", 1),
            MakeEchoInvocation("b", 2),
            MakeEchoInvocation("c", 3),
        };

        // Must not throw — the IOException must be swallowed so Main can emit the envelope.
        var result = await runner.RunAsync(invocations, brokenStdout, TextWriter.Null);

        Assert.Equal(0, result.TotalJobs); // RunDryRun returns zero by contract
        // First WriteLine attempted; loop should break on first IOException so subsequent
        // jobs don't re-discover the failure.
        Assert.Equal(1, brokenStdout.WriteAttempts);
    }

    [Fact]
    public async Task RunAsync_KeepOrder_BrokenStdoutPipe_DoesNotCrashRun()
    {
        var invocations = Enumerable.Range(0, 4)
            .Select(i => MakeEchoInvocation($"item{i}", i + 1))
            .ToArray();

        var brokenStdout = new BrokenPipeWriter();
        var runner = new JobRunner(new JobRunnerOptions(
            Parallelism: 2,
            Strategy: BufferStrategy.KeepOrder));

        var result = await runner.RunAsync(invocations, brokenStdout, TextWriter.Null);

        Assert.Equal(4, result.TotalJobs);
        // KeepOrder breaks the flush loop on first IOException — subsequent attempts
        // skipped. WriteAttempts should be 1 (the first-job flush that triggered the break).
        Assert.Equal(1, brokenStdout.WriteAttempts);
    }

    [SkippableFact]
    public async Task RunAsync_KeepOrder_PreservesOrder_WhenJobsCompleteOutOfOrder()
    {
        // Round-1 Q2: the existing KeepOrder test used fast echo; even if KeepOrder were a
        // no-op, fast jobs would naturally complete in input order. This test deliberately
        // forces out-of-order completion: jobs 1-4 sleep for decreasing durations, so
        // job 4 finishes first, job 1 last. With KeepOrder the output must STILL be in
        // input order.
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Unix-only — Windows ping-based sleep is too coarse for the descending-duration timing this test relies on.");

        // Linux/macOS: use sh -c with sleep + echo, durations descending so completion order
        // is reverse of input order.
        CommandInvocation MakeSleepEcho(double seconds, string token, int idx)
            => new CommandInvocation(
                "sh",
                new[] { "-c", $"sleep {seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}; echo {token}" },
                $"sh -c 'sleep {seconds}; echo {token}'",
                new[] { token });

        var invocations = new[]
        {
            MakeSleepEcho(0.6, "alpha", 1),
            MakeSleepEcho(0.45, "bravo", 2),
            MakeSleepEcho(0.30, "charlie", 3),
            MakeSleepEcho(0.15, "delta", 4),
        };

        var stdout = new StringWriter();
        var runner = new JobRunner(new JobRunnerOptions(
            Parallelism: 4,
            Strategy: BufferStrategy.KeepOrder));

        var result = await runner.RunAsync(invocations, stdout, TextWriter.Null);

        Assert.Equal(4, result.Succeeded);
        string output = stdout.ToString();
        int posAlpha = output.IndexOf("alpha");
        int posBravo = output.IndexOf("bravo");
        int posCharlie = output.IndexOf("charlie");
        int posDelta = output.IndexOf("delta");
        Assert.True(posAlpha >= 0 && posBravo >= 0 && posCharlie >= 0 && posDelta >= 0);
        Assert.True(posAlpha < posBravo, "alpha before bravo (despite finishing last)");
        Assert.True(posBravo < posCharlie, "bravo before charlie");
        Assert.True(posCharlie < posDelta, "charlie before delta (despite finishing first)");
    }

    [Fact]
    public async Task RunAsync_ParallelFailFast_ManyJobs_BulkSkipsRemaining()
    {
        // With many invocations (50) and Parallelism=4 + FailFast, the early-failing job
        // must trigger the skip path at the dispatch loop's `Volatile.Read(ref aborted)`
        // check (line 242) — not just the in-flight cancellation path. Pre-round-1 this was
        // covered only with 4 jobs + Parallelism=4, where the dispatch-loop short-circuit
        // never had a chance to fire. This test pins the bulk-skip behaviour.
        var invocations = new List<CommandInvocation>();
        // First two jobs: success
        invocations.Add(MakeEchoInvocation("ok-1", 1));
        invocations.Add(MakeEchoInvocation("ok-2", 2));
        // Third job: failure (triggers fail-fast)
        invocations.Add(MakeExitInvocation(7, "fail-3"));
        // Remaining 47 jobs: should be skipped, NOT executed
        for (int i = 4; i <= 50; i++)
        {
            invocations.Add(MakeEchoInvocation($"would-skip-{i}", i));
        }

        var runner = new JobRunner(new JobRunnerOptions(
            Parallelism: 4,
            FailFast: true));

        var result = await runner.RunAsync(invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(50, result.TotalJobs);
        Assert.True(result.Failed >= 1, $"Expected at least 1 failure, got {result.Failed}");
        // The bulk-skip should leave a high skipped count — at least half of the remaining
        // jobs must be skipped before they can dispatch. Allow scheduling slack but require
        // observably more than just the "in-flight at moment of failure" handful.
        Assert.True(result.Skipped >= 25,
            $"Expected bulk skip (>=25), got {result.Skipped} skipped, {result.Succeeded} succeeded — fail-fast may not be hitting the dispatch-loop short-circuit.");
    }

    // -- Round-3 review pinning tests. --

    [Fact]
    public async Task RunAsync_Sequential_ExternalCancelMidRun_PropagatesOperationCanceledException()
    {
        // Sequential mode has always rethrown OCE on external cancel (line 151). Pin that
        // contract so a future change can't silently regress to the parallel-mode bug
        // round-3 fixed.
        using var cts = new CancellationTokenSource();
        var invocations = new[]
        {
            MakeSleepInvocation(10, "slow1"),
            MakeSleepInvocation(10, "slow2"),
        };

        var runner = new JobRunner(new JobRunnerOptions(Parallelism: 1));
        Task<WargsResult> runTask = runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null, cts.Token);

        await Task.Delay(300);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runTask);
    }

    [Fact]
    public async Task RunAsync_Parallel_ExternalCancelMidRun_PropagatesOperationCanceledException()
    {
        // Round-3 Critical: each task body's external-cancel arm correctly catches OCE and
        // returns Skipped, so Task.WhenAll completes normally — without an explicit
        // ThrowIfCancellationRequested at the join point, RunParallelAsync would return a
        // "successful" all-skipped WargsResult and Program.Main would emit exit 0 with
        // exit_reason=success. This test pins the contract: external cancel propagates OCE
        // out of RunAsync so Main's catch arm produces exit 130.
        using var cts = new CancellationTokenSource();
        var invocations = Enumerable.Range(0, 8)
            .Select(_ => MakeSleepInvocation(10, "slow"))
            .ToArray();

        var runner = new JobRunner(new JobRunnerOptions(Parallelism: 4));
        Task<WargsResult> runTask = runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null, cts.Token);

        await Task.Delay(300);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runTask);
    }

    [Fact]
    public async Task RunAsync_Parallel_ExternalCancel_JobResultsCarrySkipReasonExternalCancel()
    {
        // Round-17 TA I1: SkipReason.ExternalCancel is set in three production sites
        // (JobRunner.cs:354 race-check arm, :435 external-cancel arm, :567 IsCanceled
        // defence-in-depth) but no test pinned the actual SkipReason value on a JobResult.
        // The round-12 SFH+TA I4 fix introduced SkipReason specifically so Program.cs's
        // actualFailFastTriggered classifier could distinguish external-cancel skips from
        // fail-fast skips. A regression silently flipping ExternalCancel to FailFastAbort
        // would mis-promote exit_reason on Ctrl+C+child_failed runs from child_failed to
        // fail_fast_abort — and every other test would still pass.
        //
        // The OnJobCompleted callback fires from each of the three production sites BEFORE
        // RunAsync's join-point throw, so the callback captures results that the WargsResult
        // never returns (the throw at JobRunner.cs:490 prevents that).
        using var cts = new CancellationTokenSource();
        var capturedResults = new System.Collections.Concurrent.ConcurrentBag<JobResult>();
        var invocations = Enumerable.Range(0, 8)
            .Select(_ => MakeSleepInvocation(10, "slow"))
            .ToArray();

        var runner = new JobRunner(new JobRunnerOptions(
            Parallelism: 4,
            OnJobCompleted: capturedResults.Add));
        Task<WargsResult> runTask = runner.RunAsync(
            invocations, TextWriter.Null, TextWriter.Null, cts.Token);

        await Task.Delay(300);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runTask);

        // At least one captured result must carry SkipReason.ExternalCancel — the contract
        // is that external-cancel skips are classified as such, distinct from fail-fast or
        // confirm-declined.
        Assert.Contains(capturedResults, r => r.Skipped && r.SkipReason == SkipReason.ExternalCancel);
        // None should be misclassified as FailFastAbort (no fail-fast was requested) or
        // ConfirmDeclined (no --confirm was used).
        Assert.DoesNotContain(capturedResults, r => r.SkipReason == SkipReason.FailFastAbort);
        Assert.DoesNotContain(capturedResults, r => r.SkipReason == SkipReason.ConfirmDeclined);
    }

    [Fact]
    public async Task RunAsync_Sequential_Verbose_BrokenStderrPipe_DoesNotMisattributeFault()
    {
        // Round-3: verbose stderr writes now use SafeWriteAsync. Pre-fix, a broken stderr
        // pipe in verbose mode raised IOException that escaped to the body's catch and
        // misattributed the pipe-closed condition as a FaultMessage on an otherwise
        // successful job. Now the IOException is swallowed and the job reports clean.
        var brokenStderr = new BrokenPipeWriter();
        var runner = new JobRunner(new JobRunnerOptions(Verbose: true));
        var invocations = new[] { MakeEchoInvocation("hello", 1) };

        var result = await runner.RunAsync(invocations, TextWriter.Null, brokenStderr);

        Assert.Equal(1, result.Succeeded);
        Assert.Null(result.Jobs[0].FaultMessage);
    }

    [Fact]
    public async Task RunAsync_Parallel_Verbose_BrokenStderrPipe_DoesNotMisattributeFault()
    {
        var brokenStderr = new BrokenPipeWriter();
        var runner = new JobRunner(new JobRunnerOptions(Parallelism: 4, Verbose: true));
        var invocations = Enumerable.Range(0, 4)
            .Select(i => MakeEchoInvocation($"x{i}", i + 1))
            .ToArray();

        var result = await runner.RunAsync(invocations, TextWriter.Null, brokenStderr);

        Assert.Equal(4, result.Succeeded);
        // Every job must report clean — pre-fix, every parallel task hit the same broken
        // stderr concurrently and produced N misattributed FaultMessages.
        Assert.All(result.Jobs, j => Assert.Null(j.FaultMessage));
    }

    [Fact]
    public async Task RunAsync_FaultedJob_AlwaysHasChildExitCodeNegativeOne()
    {
        // Pin the FaultMessage <-> ChildExitCode invariant. Every code path that sets
        // FaultMessage also sets ChildExitCode = -1. Formatters and the human summary rely
        // on this; an accidental "ChildExitCode: 42, FaultMessage: ..." combination would
        // surface inconsistently across output modes (Failed count vs human stderr line).
        var bad = new CommandInvocation("", Array.Empty<string>(), "(bad)", new[] { "x" });
        var runner = new JobRunner(new JobRunnerOptions(ShellFallback: false));
        var result = await runner.RunAsync(new[] { bad }, TextWriter.Null, TextWriter.Null);

        JobResult job = Assert.Single(result.Jobs);
        Assert.NotNull(job.FaultMessage);
        Assert.Equal(-1, job.ChildExitCode);
    }

    [Fact]
    public async Task RunAsync_Parallel_EmptyFileName_FaultMessageHasSpawnPrefix()
    {
        // Pin that spawn-failure classification fires inside ExecuteJobAsync (where the
        // "failed to spawn 'X': ..." prefix is added by FormatSpawnFault) rather than
        // escaping to the task body's outer catch (which produces the bare
        // "ExceptionType: message" form). A future narrowing of IsSpawnFailure would
        // silently re-classify spawn faults as task-body faults — observable contract
        // drift this test catches.
        var bad = new CommandInvocation("", Array.Empty<string>(), "(bad)", new[] { "x" });
        var runner = new JobRunner(new JobRunnerOptions(Parallelism: 2, ShellFallback: false));

        var result = await runner.RunAsync(new[] { bad }, TextWriter.Null, TextWriter.Null);

        JobResult job = Assert.Single(result.Jobs);
        Assert.NotNull(job.FaultMessage);
        Assert.StartsWith("failed to spawn", job.FaultMessage);
    }

    [Fact]
    public async Task RunAsync_SkippedJobs_NeverCarryFaultMessage()
    {
        // Pin the unwritten invariant that no JobResult has both Skipped=true AND
        // FaultMessage set. Formatters rely on this: NDJSON omits skipped jobs entirely
        // (so a fault on a skip would be invisible), while the JSON faults array and
        // human-stderr fault loop don't filter on Skipped — so a skip-with-fault row
        // would surface inconsistently across output modes. This test runs the major
        // skip-producing paths (confirm decline, fail-fast skip, pre-cancel skip) and
        // asserts none of them attach a FaultMessage.

        // (a) Confirm prompt declined (sequential — confirm + parallel rejected at parser).
        var declinedRunner = new JobRunner(new JobRunnerOptions(
            Confirm: true, ConfirmPrompt: _ => false));
        var declinedResult = await declinedRunner.RunAsync(
            new[] { MakeEchoInvocation("x", 1) }, TextWriter.Null, TextWriter.Null);
        Assert.All(declinedResult.Jobs.Where(j => j.Skipped), j => Assert.Null(j.FaultMessage));

        // (b) Fail-fast triggered: first job exits non-zero, remaining jobs skipped.
        var ffRunner = new JobRunner(new JobRunnerOptions(FailFast: true));
        var ffResult = await ffRunner.RunAsync(
            new[] { MakeExitInvocation(1, "fail"), MakeEchoInvocation("skip", 2) },
            TextWriter.Null, TextWriter.Null);
        Assert.All(ffResult.Jobs.Where(j => j.Skipped), j => Assert.Null(j.FaultMessage));
    }

    // -- Round-12 review: SkipReason classification + NDJSON streaming via OnJobCompleted. --

    [Fact]
    public async Task RunAsync_FailFastSkipsCarryFailFastAbortReason()
    {
        // Round-12 SFH+TA I4: when --fail-fast triggers and skips remaining jobs, those
        // skipped jobs must carry SkipReason.FailFastAbort so Program.cs's classifier can
        // distinguish them from confirm-declined or external-cancel skips.
        var runner = new JobRunner(new JobRunnerOptions(FailFast: true));
        var invocations = new[]
        {
            MakeExitInvocation(1, "fail"),                // job 1: fails
            MakeEchoInvocation("would-skip-2", 2),        // job 2+: skipped due to fail-fast
            MakeEchoInvocation("would-skip-3", 3),
        };

        var result = await runner.RunAsync(invocations, TextWriter.Null, TextWriter.Null);

        Assert.True(result.Failed >= 1);
        Assert.True(result.Skipped >= 1);
        // Every skipped job from this run must be classified as FailFastAbort
        // (the only skip-producing path here besides the failure itself).
        Assert.All(result.Jobs.Where(j => j.Skipped),
            j => Assert.Equal(SkipReason.FailFastAbort, j.SkipReason));
    }

    [Fact]
    public async Task RunAsync_ConfirmDeclinedSkipsCarryConfirmDeclinedReason()
    {
        // Round-12 SFH+TA I4: confirm-declined skips must carry SkipReason.ConfirmDeclined,
        // NOT FailFastAbort. Without this distinction Program.cs would mis-classify a
        // confirm-decline + later child failure as fail_fast_abort.
        var runner = new JobRunner(new JobRunnerOptions(
            Confirm: true,
            ConfirmPrompt: _ => false));  // decline every prompt
        var invocations = new[]
        {
            MakeEchoInvocation("a", 1),
            MakeEchoInvocation("b", 2),
        };

        var result = await runner.RunAsync(invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(2, result.Skipped);
        Assert.All(result.Jobs.Where(j => j.Skipped),
            j => Assert.Equal(SkipReason.ConfirmDeclined, j.SkipReason));
    }

    [Fact]
    public async Task RunAsync_OnJobCompleted_FiresPerJobAsTheyComplete_NotBatched()
    {
        // Round-12 CR/SFH/TA C1: NDJSON was documented as "streaming per job" but the prior
        // implementation emitted lines after Task.WhenAll. The fix added an OnJobCompleted
        // callback invoked from inside the task body. This pin asserts the callback is
        // invoked at LEAST as many times as completed jobs (the structural shape — actual
        // timing is covered by an integration test that compares timestamps).
        var completedJobs = new List<int>();
        var lockObj = new object();
        var runner = new JobRunner(new JobRunnerOptions(
            OnJobCompleted: job =>
            {
                lock (lockObj) { completedJobs.Add(job.JobIndex); }
            }));
        var invocations = new[]
        {
            MakeEchoInvocation("a", 1),
            MakeEchoInvocation("b", 2),
            MakeEchoInvocation("c", 3),
        };

        var result = await runner.RunAsync(invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(3, result.Succeeded);
        // Three completed jobs → three callback invocations.
        Assert.Equal(3, completedJobs.Count);
        Assert.Contains(1, completedJobs);
        Assert.Contains(2, completedJobs);
        Assert.Contains(3, completedJobs);
    }

    [Fact]
    public async Task RunAsync_OnJobCompleted_FiresForEveryJobIncludingSkipped()
    {
        // Round-12.5: contract changed to "callback fires for EVERY job (including skipped)
        // so reorder-buffer subscribers can advance their next-expected pointer past
        // skipped slots." Subscribers that want to omit skipped from their own stream
        // (the default --ndjson contract: "one line per job actually run") filter on
        // JobResult.Skipped themselves. This test pins the new contract — total
        // invocation count equals total job count, regardless of skip state.
        int callbackCount = 0;
        int skippedCallbackCount = 0;
        var runner = new JobRunner(new JobRunnerOptions(
            FailFast: true,
            OnJobCompleted: job =>
            {
                Interlocked.Increment(ref callbackCount);
                if (job.Skipped) { Interlocked.Increment(ref skippedCallbackCount); }
            }));
        var invocations = new[]
        {
            MakeExitInvocation(1, "fail"),                // fires; failure
            MakeEchoInvocation("would-skip", 2),          // fires; Skipped=true
            MakeEchoInvocation("would-skip-too", 3),      // fires; Skipped=true
        };

        var result = await runner.RunAsync(invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, result.Failed);
        Assert.True(result.Skipped >= 1);
        // Callback must fire for all 3 jobs (1 failed + N skipped, total = invocations.Count).
        Assert.Equal(3, callbackCount);
        Assert.True(skippedCallbackCount >= 1,
            "At least one skipped job must have fired the callback so reorder-buffer subscribers can advance.");
    }

    [SkippableFact]
    public async Task RunAsync_OnJobCompleted_FiresInCompletionOrderUnderParallel()
    {
        // Round-12.5: pin that the callback fires in COMPLETION order under parallel
        // execution (not input order). This is the contract Program.cs's --keep-order
        // reorder buffer is designed against — without out-of-order callback delivery,
        // the buffer would be unnecessary. Cross-platform via descending sleeps.
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Unix-only — Windows ping-based sleep is too coarse for sub-second timing.");

        CommandInvocation MakeSleep(double seconds, int jobIndex)
            => new CommandInvocation(
                "sh",
                new[] { "-c", $"sleep {seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}" },
                $"sh -c 'sleep {seconds}'",
                new[] { jobIndex.ToString() });

        var completionOrder = new List<int>();
        var lockObj = new object();
        var runner = new JobRunner(new JobRunnerOptions(
            Parallelism: 4,
            OnJobCompleted: job =>
            {
                lock (lockObj) { completionOrder.Add(job.JobIndex); }
            }));
        // Job 1 sleeps 0.6s (slowest), 2 sleeps 0.4s, 3 sleeps 0.2s (fastest).
        var invocations = new[]
        {
            MakeSleep(0.6, 1),
            MakeSleep(0.4, 2),
            MakeSleep(0.2, 3),
        };

        await runner.RunAsync(invocations, TextWriter.Null, TextWriter.Null);

        // Completion order should be reverse of input order under parallel execution.
        Assert.Equal(new[] { 3, 2, 1 }, completionOrder.ToArray());
    }

    [Fact]
    public async Task RunAsync_CustomConfirmPromptThrows_DoesNotAbortRun_Sequential()
    {
        // Round-13 SFH I3 / TA I2: a custom ConfirmPrompt delegate (test-seam) that throws
        // would otherwise escape RunSequentialAsync entirely and land in Main's broad catch
        // as unexpected_error/exit 126 — taking down the entire run for one prompt fault.
        // Symmetric with OnJobCompleted's swallow rule (callback faults must not abort the
        // run). Round-13 wraps the prompt call in try/catch; on throw, treats as decline so
        // the run continues.
        var runner = new JobRunner(new JobRunnerOptions(
            Confirm: true,
            ConfirmPrompt: _ => throw new InvalidOperationException("simulated prompt bug")));
        var invocations = new[] { MakeEchoInvocation("a", 1), MakeEchoInvocation("b", 2) };

        // Should not throw — the prompt fault should classify each affected job as Skipped
        // and continue.
        var result = await runner.RunAsync(invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(2, result.TotalJobs);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(0, result.Failed);
        // Skipped jobs must NOT carry FaultMessage (preserves the
        // SkippedJobs_NeverCarryFaultMessage invariant pinned earlier).
        Assert.All(result.Jobs.Where(j => j.Skipped), j => Assert.Null(j.FaultMessage));
    }

    [Fact]
    public async Task RunAsync_Sequential_OnJobCompleted_FiresInDispatchOrder()
    {
        // Round-13 TA I4: explicitly pin that the OnJobCompleted callback fires for
        // sequential mode in dispatch (= input) order. Existing tests cover parallel-mode
        // completion-order delivery; sequential delivery order is implied but not separately
        // pinned. A future change that splits the sequential vs parallel callback contracts
        // (e.g. only fires from parallel) would silently break Program.cs's keep-order
        // reorder buffer for sequential runs.
        var completionOrder = new List<int>();
        var runner = new JobRunner(new JobRunnerOptions(
            Parallelism: 1,  // explicit sequential
            OnJobCompleted: job => completionOrder.Add(job.JobIndex)));
        var invocations = new[]
        {
            MakeEchoInvocation("a", 1),
            MakeEchoInvocation("b", 2),
            MakeEchoInvocation("c", 3),
        };

        await runner.RunAsync(invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(new[] { 1, 2, 3 }, completionOrder.ToArray());
    }

    [Fact]
    public async Task RunAsync_OnJobCompleted_CallbackFault_DoesNotAbortRun()
    {
        // The callback is best-effort; a buggy subscriber must not abort the run.
        // Pin via a callback that throws on every invocation.
        var runner = new JobRunner(new JobRunnerOptions(
            OnJobCompleted: _ => throw new InvalidOperationException("subscriber bug")));
        var invocations = new[]
        {
            MakeEchoInvocation("a", 1),
            MakeEchoInvocation("b", 2),
        };

        var result = await runner.RunAsync(invocations, TextWriter.Null, TextWriter.Null);

        // All jobs ran successfully despite the callback throwing on each.
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
    }

    [SkippableFact]
    public async Task RunAsync_Parallel_WallSecondsIsRunDurationNotSum()
    {
        // Round-12 CR/SFH/TA I2: wall_seconds is wall-clock duration of the run, NOT sum
        // of per-job durations. Under -P 4 with 4 jobs each sleeping ~500ms, the run takes
        // ~500ms not ~2000ms. The earlier "across all jobs" wording suggested summation;
        // the new wording explicitly disclaims it. Pin the actual semantics.
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Unix-only — Windows ping-based sleep is too coarse for sub-second timing pins.");

        CommandInvocation MakeSleep(double seconds, string token)
            => new CommandInvocation(
                "sh",
                new[] { "-c", $"sleep {seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}" },
                $"sh -c 'sleep {seconds}'",
                new[] { token });

        var invocations = Enumerable.Range(0, 4).Select(i => MakeSleep(0.5, $"j{i}")).ToArray();
        var runner = new JobRunner(new JobRunnerOptions(Parallelism: 4));

        var result = await runner.RunAsync(invocations, TextWriter.Null, TextWriter.Null);

        Assert.Equal(4, result.Succeeded);
        // Sum of per-job durations would be ~2.0s; wall-clock should be < 1.5s under -P 4.
        Assert.True(result.WallTime.TotalSeconds < 1.5,
            $"Expected wall_seconds < 1.5s under -P 4 (concurrent execution), got {result.WallTime.TotalSeconds:F2}s");
    }

    [SkippableFact]
    public async Task RunAsync_ShellFallback_ItemWithNewline_DoesNotInjectShellCommand()
    {
        // End-to-end safety pin for the round-1 Critical (shell-fallback newline injection).
        // The CommandBuilder ShellQuote tests pin DisplayString — but the actual safety
        // property is "an item containing a newline does not become a shell command
        // separator when fed to sh -c". This test triggers the shell-fallback path with a
        // canary file: if the injection succeeded, the canary would be removed.
        Skip.IfNot(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Unix-only — exercises the sh -c fallback path.");

        string canaryPath = Path.Combine(Path.GetTempPath(), $"wargs-injection-canary-{Guid.NewGuid():N}");
        File.WriteAllText(canaryPath, "canary present");
        try
        {
            // Item value: "safe\nrm -f <canaryPath>". If ShellQuote omitted \n from its
            // special-char set (the round-1 Critical), the formatted sh -c string would
            // contain a literal newline, splitting into two commands and removing the canary.
            string maliciousItem = "safe\nrm -f " + canaryPath;
            // Use a deliberately-non-existent direct command so we hit the shell fallback,
            // and a shell builtin that exists on every Unix `:` will be invoked via sh.
            var invocation = new CommandInvocation(
                Command: "wargs_test_definitely_not_a_command_xyz",
                Arguments: new[] { maliciousItem },
                DisplayString: "wargs_test_definitely_not_a_command_xyz '<malicious>'",
                SourceItems: new[] { maliciousItem });

            var runner = new JobRunner(new JobRunnerOptions(ShellFallback: true));
            await runner.RunAsync(new[] { invocation }, TextWriter.Null, TextWriter.Null);

            Assert.True(File.Exists(canaryPath),
                "Canary file was removed — shell-fallback newline injection succeeded. ShellQuote must single-quote items containing newlines.");
        }
        finally
        {
            try { File.Delete(canaryPath); } catch { /* test cleanup best-effort */ }
        }
    }
}
