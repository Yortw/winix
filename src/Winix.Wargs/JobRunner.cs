using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Winix.Wargs;

/// <summary>
/// Executes <see cref="CommandInvocation"/>s sequentially or in parallel,
/// capturing output per job and collecting results.
/// </summary>
public sealed class JobRunner
{
    private readonly JobRunnerOptions _options;

    /// <summary>
    /// Initialises a new <see cref="JobRunner"/> with the given options.
    /// </summary>
    /// <param name="options">Execution options (parallelism, buffering, fail-fast, etc.).</param>
    public JobRunner(JobRunnerOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Runs all invocations and returns aggregate results.
    /// </summary>
    /// <param name="invocations">The commands to execute.</param>
    /// <param name="stdout">Writer for job output (captured output is flushed here on completion).</param>
    /// <param name="stderr">Writer for diagnostic messages (verbose, confirm prompts, etc.).</param>
    /// <param name="cancellationToken">Token to cancel the run.</param>
    /// <returns>A <see cref="WargsResult"/> summarising all job outcomes.</returns>
    public async Task<WargsResult> RunAsync(
        IReadOnlyList<CommandInvocation> invocations,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken = default)
    {
        if (_options.DryRun)
        {
            return RunDryRun(invocations, stdout);
        }

        if (_options.Parallelism == 1)
        {
            return await RunSequentialAsync(invocations, stdout, stderr, cancellationToken)
                .ConfigureAwait(false);
        }

        return await RunParallelAsync(invocations, stdout, stderr, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Prints each command without executing. Returns a result with zero TotalJobs
    /// (no jobs were actually executed).
    /// </summary>
    private static WargsResult RunDryRun(IReadOnlyList<CommandInvocation> invocations, TextWriter stdout)
    {
        foreach (CommandInvocation invocation in invocations)
        {
            stdout.WriteLine(invocation.DisplayString);
        }

        return new WargsResult(
            TotalJobs: 0,
            Succeeded: 0,
            Failed: 0,
            Skipped: 0,
            WallTime: TimeSpan.Zero,
            Jobs: new List<JobResult>());
    }

    /// <summary>
    /// Runs invocations one at a time in input order.
    /// </summary>
    private async Task<WargsResult> RunSequentialAsync(
        IReadOnlyList<CommandInvocation> invocations,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        var wallStopwatch = Stopwatch.StartNew();
        var jobs = new List<JobResult>(invocations.Count);
        bool abort = false;
        int succeeded = 0;
        int failed = 0;
        int skipped = 0;

        for (int i = 0; i < invocations.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CommandInvocation invocation = invocations[i];
            int jobIndex = i + 1; // 1-based

            // Fail-fast: skip remaining jobs after a failure
            if (abort)
            {
                jobs.Add(new JobResult(
                    JobIndex: jobIndex,
                    ChildExitCode: -1,
                    Output: null,
                    Duration: TimeSpan.Zero,
                    SourceItems: invocation.SourceItems,
                    Skipped: true));
                skipped++;
                continue;
            }

            // Confirm prompt
            if (_options.Confirm)
            {
                Func<string, bool> prompt = _options.ConfirmPrompt ?? DefaultConfirmPrompt;
                if (!prompt(invocation.DisplayString))
                {
                    jobs.Add(new JobResult(
                        JobIndex: jobIndex,
                        ChildExitCode: -1,
                        Output: null,
                        Duration: TimeSpan.Zero,
                        SourceItems: invocation.SourceItems,
                        Skipped: true));
                    skipped++;
                    continue;
                }
            }

            // Verbose: print command before execution
            if (_options.Verbose)
            {
                await stderr.WriteLineAsync($"wargs: {invocation.DisplayString}")
                    .ConfigureAwait(false);
            }

            JobResult result;
            if (_options.Strategy == BufferStrategy.LineBuffered)
            {
                result = await ExecuteJobLineBufferedAsync(invocation, jobIndex, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                result = await ExecuteJobAsync(invocation, jobIndex, cancellationToken)
                    .ConfigureAwait(false);
            }

            jobs.Add(result);

            // Flush captured output to stdout
            if (result.Output != null)
            {
                await stdout.WriteAsync(result.Output).ConfigureAwait(false);
            }

            if (result.ChildExitCode == 0)
            {
                succeeded++;
            }
            else
            {
                failed++;

                if (_options.FailFast)
                {
                    abort = true;
                }
            }
        }

        wallStopwatch.Stop();

        return new WargsResult(
            TotalJobs: invocations.Count,
            Succeeded: succeeded,
            Failed: failed,
            Skipped: skipped,
            WallTime: wallStopwatch.Elapsed,
            Jobs: jobs);
    }

    /// <summary>
    /// Runs invocations concurrently, limited by <see cref="JobRunnerOptions.Parallelism"/>.
    /// Uses <see cref="SemaphoreSlim"/> to throttle concurrency and <see cref="Volatile"/>
    /// read/write on the abort flag for fail-fast across threads.
    /// </summary>
    private async Task<WargsResult> RunParallelAsync(
        IReadOnlyList<CommandInvocation> invocations,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        var wallStopwatch = Stopwatch.StartNew();
        int maxParallelism = _options.Parallelism == 0 ? int.MaxValue : _options.Parallelism;
        var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);
        var tasks = new Task<JobResult>[invocations.Count];
        // Volatile.Read/Write on this captured local is intentional: the compiler hoists
        // it to a closure class field (heap allocation shared across all task closures),
        // and Volatile provides the cross-thread visibility guarantee we need.
        bool aborted = false;
        // Lock protects stdout/stderr writes so job outputs don't interleave
        var outputLock = new object();

        // Linked CTS: cancels in-flight jobs when either the caller cancels OR
        // --fail-fast triggers. The kill-on-cancel registration in ExecuteJobAsync
        // ensures child processes are actually killed rather than just abandoned.
        using var failFastCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken jobToken = failFastCts.Token;

        for (int i = 0; i < invocations.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CommandInvocation invocation = invocations[i];
            int jobIndex = i + 1; // 1-based

            // Fail-fast: skip remaining jobs after a failure (checked before acquiring semaphore)
            if (Volatile.Read(ref aborted))
            {
                tasks[i] = Task.FromResult(new JobResult(
                    JobIndex: jobIndex,
                    ChildExitCode: -1,
                    Output: null,
                    Duration: TimeSpan.Zero,
                    SourceItems: invocation.SourceItems,
                    Skipped: true));
                continue;
            }

            // Confirm prompt — must run on the dispatching thread (sequential, before spawning)
            if (_options.Confirm)
            {
                Func<string, bool> prompt = _options.ConfirmPrompt ?? DefaultConfirmPrompt;
                if (!prompt(invocation.DisplayString))
                {
                    tasks[i] = Task.FromResult(new JobResult(
                        JobIndex: jobIndex,
                        ChildExitCode: -1,
                        Output: null,
                        Duration: TimeSpan.Zero,
                        SourceItems: invocation.SourceItems,
                        Skipped: true));
                    continue;
                }
            }

            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            // Capture loop variable for the closure
            int capturedIndex = jobIndex;
            CommandInvocation capturedInvocation = invocation;

            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    // Race check: abort may have been set while we waited for the semaphore
                    if (Volatile.Read(ref aborted))
                    {
                        return new JobResult(
                            JobIndex: capturedIndex,
                            ChildExitCode: -1,
                            Output: null,
                            Duration: TimeSpan.Zero,
                            SourceItems: capturedInvocation.SourceItems,
                            Skipped: true);
                    }


                    if (_options.Verbose)
                    {
                        lock (outputLock)
                        {
                            stderr.WriteLine($"wargs: {capturedInvocation.DisplayString}");
                        }
                    }

                    JobResult result;
                    if (_options.Strategy == BufferStrategy.LineBuffered)
                    {
                        result = await ExecuteJobLineBufferedAsync(capturedInvocation, capturedIndex, jobToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        result = await ExecuteJobAsync(capturedInvocation, capturedIndex, jobToken)
                            .ConfigureAwait(false);
                    }

                    // JobBuffered: flush captured output atomically as each job completes.
                    // KeepOrder: defer output until all jobs finish (written in input order below).
                    // LineBuffered: Output is null (children wrote directly to inherited stdio).
                    if (_options.Strategy == BufferStrategy.JobBuffered && result.Output != null)
                    {
                        lock (outputLock)
                        {
                            stdout.Write(result.Output);
                        }
                    }

                    // Check for fail-fast trigger — cancel the linked CTS so in-flight
                    // jobs are killed rather than running to completion.
                    if (result.ChildExitCode != 0 && _options.FailFast)
                    {
                        Volatile.Write(ref aborted, true);
                        failFastCts.Cancel();
                    }

                    return result;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Cancelled by fail-fast, not by the caller — treat as skipped
                    return new JobResult(
                        JobIndex: capturedIndex,
                        ChildExitCode: -1,
                        Output: null,
                        Duration: TimeSpan.Zero,
                        SourceItems: capturedInvocation.SourceItems,
                        Skipped: true);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken);
        }

        // Some tasks may have been cancelled by fail-fast or faulted unexpectedly.
        // Collect results without letting the aggregate exception propagate.
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Fail-fast cancellation — tasks already returned Skipped results
        }
        catch (Exception)
        {
            // Unexpected exception from a task body. Faulted tasks are handled below
            // by checking task status before accessing .Result.
        }

        // Keep-order: write all output in input order after all jobs complete.
        // Check task status before accessing .Result — a faulted task would re-throw.
        if (_options.Strategy == BufferStrategy.KeepOrder)
        {
            for (int i = 0; i < tasks.Length; i++)
            {
                if (tasks[i].IsCompletedSuccessfully)
                {
                    JobResult r = tasks[i].Result;
                    if (r.Output != null)
                    {
                        stdout.Write(r.Output);
                    }
                }
            }
        }

        wallStopwatch.Stop();

        // Collect results sorted by JobIndex (tasks array is already in order)
        var jobs = new List<JobResult>(invocations.Count);
        int succeeded = 0;
        int failed = 0;
        int skipped = 0;

        for (int i = 0; i < tasks.Length; i++)
        {
            JobResult result;
            if (tasks[i].IsCompletedSuccessfully)
            {
                result = tasks[i].Result;
            }
            else
            {
                // Task faulted with an unexpected exception — treat as failed
                result = new JobResult(
                    JobIndex: i + 1,
                    ChildExitCode: -1,
                    Output: null,
                    Duration: TimeSpan.Zero,
                    SourceItems: invocations[i].SourceItems,
                    Skipped: false);
            }

            jobs.Add(result);

            if (result.Skipped)
            {
                skipped++;
            }
            else if (result.ChildExitCode == 0)
            {
                succeeded++;
            }
            else
            {
                failed++;
            }
        }

        return new WargsResult(
            TotalJobs: invocations.Count,
            Succeeded: succeeded,
            Failed: failed,
            Skipped: skipped,
            WallTime: wallStopwatch.Elapsed,
            Jobs: jobs);
    }

    /// <summary>
    /// Spawns a single child process, captures merged stdout+stderr, and returns a <see cref="JobResult"/>.
    /// If the command is not found and shell fallback is enabled, retries via the platform shell.
    /// </summary>
    private async Task<JobResult> ExecuteJobAsync(
        CommandInvocation invocation,
        int jobIndex,
        CancellationToken cancellationToken)
    {
        var startInfo = BuildStartInfo(invocation, redirectIo: true);

        var stopwatch = Stopwatch.StartNew();
        Process process;

        try
        {
            process = Process.Start(startInfo)
                ?? throw new Win32Exception("Process.Start returned null");
        }
        catch (Win32Exception)
        {
            if (_options.ShellFallback)
            {
                // Command not found as standalone executable — retry via platform shell
                // so that shell builtins (echo, del, type, etc.) work transparently.
                startInfo = BuildShellFallbackStartInfo(invocation, redirectIo: true);
                try
                {
                    process = Process.Start(startInfo)
                        ?? throw new Win32Exception("Process.Start returned null");
                }
                catch (Win32Exception)
                {
                    stopwatch.Stop();
                    return new JobResult(
                        JobIndex: jobIndex,
                        ChildExitCode: -1,
                        Output: null,
                        Duration: stopwatch.Elapsed,
                        SourceItems: invocation.SourceItems,
                        Skipped: false);
                }
            }
            else
            {
                stopwatch.Stop();
                return new JobResult(
                    JobIndex: jobIndex,
                    ChildExitCode: -1,
                    Output: null,
                    Duration: stopwatch.Elapsed,
                    SourceItems: invocation.SourceItems,
                    Skipped: false);
            }
        }

        // Close stdin immediately — child commands don't read interactive input
        process.StandardInput.Close();

        try
        {
            // Kill the child process tree if cancellation is requested, so we don't
            // leak long-running children after wargs exits.
            using var killReg = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process already exited between the check and the kill — safe to ignore.
                }
                catch (Win32Exception)
                {
                    // Kill failed (e.g. access denied on elevated child, zombie process on Linux).
                    // Swallow to avoid tearing down the process from an unhandled callback exception.
                }
            });

            // Read stdout and stderr concurrently to avoid deadlock when the child
            // writes enough to fill one pipe's buffer while we're only reading the other.
            var output = new StringBuilder();
            var outputLock = new object();

            Task stdoutTask = ReadStreamAsync(process.StandardOutput, output, outputLock);
            Task stderrTask = ReadStreamAsync(process.StandardError, output, outputLock);

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            string captured;
            lock (outputLock)
            {
                captured = output.ToString();
            }

            return new JobResult(
                JobIndex: jobIndex,
                ChildExitCode: process.ExitCode,
                Output: captured,
                Duration: stopwatch.Elapsed,
                SourceItems: invocation.SourceItems,
                Skipped: false);
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>
    /// Reads a redirected stream in chunks and appends to the shared <see cref="StringBuilder"/>.
    /// The lock serialises interleaved stdout/stderr writes so chunks don't get torn.
    /// </summary>
    private static async Task ReadStreamAsync(StreamReader reader, StringBuilder output, object outputLock)
    {
        char[] buffer = new char[4096];
        int charsRead;

        while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            lock (outputLock)
            {
                output.Append(buffer, 0, charsRead);
            }
        }
    }

    /// <summary>
    /// Spawns a single child process without redirecting stdio, so output flows
    /// directly to the inherited console. The returned <see cref="JobResult.Output"/>
    /// is always null because nothing is captured.
    /// If the command is not found and shell fallback is enabled, retries via the platform shell.
    /// </summary>
    private async Task<JobResult> ExecuteJobLineBufferedAsync(
        CommandInvocation invocation,
        int jobIndex,
        CancellationToken cancellationToken)
    {
        var startInfo = BuildStartInfo(invocation, redirectIo: false);

        var stopwatch = Stopwatch.StartNew();
        Process process;

        try
        {
            process = Process.Start(startInfo)
                ?? throw new Win32Exception("Process.Start returned null");
        }
        catch (Win32Exception)
        {
            if (_options.ShellFallback)
            {
                startInfo = BuildShellFallbackStartInfo(invocation, redirectIo: false);
                try
                {
                    process = Process.Start(startInfo)
                        ?? throw new Win32Exception("Process.Start returned null");
                }
                catch (Win32Exception)
                {
                    stopwatch.Stop();
                    return new JobResult(
                        JobIndex: jobIndex,
                        ChildExitCode: -1,
                        Output: null,
                        Duration: stopwatch.Elapsed,
                        SourceItems: invocation.SourceItems,
                        Skipped: false);
                }
            }
            else
            {
                stopwatch.Stop();
                return new JobResult(
                    JobIndex: jobIndex,
                    ChildExitCode: -1,
                    Output: null,
                    Duration: stopwatch.Elapsed,
                    SourceItems: invocation.SourceItems,
                    Skipped: false);
            }
        }

        try
        {
            // Kill the child process tree if cancellation is requested, so we don't
            // leak long-running children after wargs exits.
            using var killReg = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process already exited between the check and the kill — safe to ignore.
                }
                catch (Win32Exception)
                {
                    // Kill failed (e.g. access denied on elevated child, zombie process on Linux).
                    // Swallow to avoid tearing down the process from an unhandled callback exception.
                }
            });

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return new JobResult(
                JobIndex: jobIndex,
                ChildExitCode: process.ExitCode,
                Output: null,
                Duration: stopwatch.Elapsed,
                SourceItems: invocation.SourceItems,
                Skipped: false);
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>
    /// Builds a <see cref="ProcessStartInfo"/> for direct execution of the invocation's command.
    /// </summary>
    private static ProcessStartInfo BuildStartInfo(CommandInvocation invocation, bool redirectIo)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.Command,
            UseShellExecute = false,
            RedirectStandardOutput = redirectIo,
            RedirectStandardError = redirectIo,
            RedirectStandardInput = redirectIo,
            CreateNoWindow = true,
        };

        foreach (string arg in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return startInfo;
    }

    /// <summary>
    /// Builds a <see cref="ProcessStartInfo"/> that wraps the invocation in the platform shell
    /// (<c>cmd /c</c> on Windows, <c>sh -c</c> on Unix). Used as a fallback when the command
    /// is not found as a standalone executable — this allows shell builtins like <c>echo</c>,
    /// <c>del</c>, <c>type</c> to work transparently.
    /// </summary>
    private static ProcessStartInfo BuildShellFallbackStartInfo(CommandInvocation invocation, bool redirectIo)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = redirectIo,
            RedirectStandardError = redirectIo,
            RedirectStandardInput = redirectIo,
            CreateNoWindow = true,
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo.FileName = "cmd";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(invocation.Command);
            foreach (string arg in invocation.Arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }
        }
        else
        {
            // sh -c takes a single string command, so join command + args with shell quoting.
            // Build the shell command string directly from Command/Arguments rather than
            // reusing DisplayString, so that future display formatting changes (colour,
            // truncation, etc.) don't silently break shell execution.
            startInfo.FileName = "sh";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(CommandBuilder.FormatDisplayString(invocation.Command, invocation.Arguments));
        }

        return startInfo;
    }

    /// <summary>
    /// Default confirm prompt that reads from /dev/tty (Unix) or CON (Windows)
    /// to avoid consuming stdin that may be piped input.
    /// </summary>
    private static bool DefaultConfirmPrompt(string displayString)
    {
        string ttyPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "CON" : "/dev/tty";

        try
        {
            using var tty = new StreamReader(ttyPath);
            Console.Error.Write($"wargs: run '{displayString}'? [y/N] ");
            Console.Error.Flush();
            string? response = tty.ReadLine();
            return string.Equals(response?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            // No terminal available — default to skip
            return false;
        }
    }
}
