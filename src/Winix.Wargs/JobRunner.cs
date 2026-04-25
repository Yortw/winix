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
            try
            {
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
            }
            catch (OperationCanceledException)
            {
                // External cancel — propagate so the caller can finalise. Other jobs added
                // to the result list so far survive.
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // Unexpected fault inside a single job — preserve partial history rather
                // than crashing the whole sequential run. Capture the exception on
                // FaultMessage so the user can diagnose.
                result = new JobResult(
                    JobIndex: jobIndex,
                    ChildExitCode: -1,
                    Output: null,
                    Duration: TimeSpan.Zero,
                    SourceItems: invocation.SourceItems,
                    Skipped: false,
                    FaultMessage: $"{ex.GetType().Name}: {ex.Message}");
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

            // Don't pass cancellationToken to Task.Run — if the token is signalled between
            // semaphore acquisition and Task.Run scheduling the body, the task transitions to
            // Canceled and the finally block (semaphore.Release) never runs, leaking slots and
            // potentially deadlocking subsequent jobs. Letting the body always run means the
            // try/finally always executes; the body itself checks the token and returns Skipped.
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    // Race check: abort or external cancel may have fired while we waited
                    // for the semaphore (or while Task.Run scheduled this body).
                    if (Volatile.Read(ref aborted) || cancellationToken.IsCancellationRequested)
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
                        try { failFastCts.Cancel(); }
                        catch (ObjectDisposedException) { /* race with end-of-run dispose */ }
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
                catch (OperationCanceledException)
                {
                    // External cancel — preserve as skipped so the result row exists.
                    return new JobResult(
                        JobIndex: capturedIndex,
                        ChildExitCode: -1,
                        Output: null,
                        Duration: TimeSpan.Zero,
                        SourceItems: capturedInvocation.SourceItems,
                        Skipped: true);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    // Unexpected fault — capture the exception type/message so the user can
                    // diagnose, rather than seeing a bare child_failed with no clue. Without
                    // this, the catch at WhenAll below would swallow silently (silent-failure
                    // class B). Surfacing FaultMessage means the JSON envelope and human
                    // summary can include it later.
                    return new JobResult(
                        JobIndex: capturedIndex,
                        ChildExitCode: -1,
                        Output: null,
                        Duration: TimeSpan.Zero,
                        SourceItems: capturedInvocation.SourceItems,
                        Skipped: false,
                        FaultMessage: $"{ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            });
        }

        // Some tasks may have been cancelled by fail-fast — task bodies handled the OCE
        // internally and returned Skipped, so WhenAll should complete without throwing.
        // The try/catch below is defence-in-depth: a future change that lets an exception
        // escape the body shouldn't take down the run.
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
        catch (Exception ex) when (IsSpawnFailure(ex))
        {
            // Process.Start can throw more than Win32Exception: empty FileName produces
            // InvalidOperationException, AOT trim edges produce PlatformNotSupportedException,
            // and FileNotFoundException is plausible on stripped runtimes. Catching only
            // Win32Exception leaves the others to escape into the parallel-loop catch with
            // no observable evidence (silent-failure class A). Capture the original message
            // on FaultMessage so the user can diagnose the failure rather than seeing a bare
            // child_failed exit.
            string directFault = FormatSpawnFault(ex, invocation);
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
                catch (Exception ex2) when (IsSpawnFailure(ex2))
                {
                    stopwatch.Stop();
                    return new JobResult(
                        JobIndex: jobIndex,
                        ChildExitCode: -1,
                        Output: null,
                        Duration: stopwatch.Elapsed,
                        SourceItems: invocation.SourceItems,
                        Skipped: false,
                        FaultMessage: $"{directFault}; shell fallback: {FormatSpawnFault(ex2, invocation)}");
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
                    Skipped: false,
                    FaultMessage: directFault);
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
                catch (Exception ex) when (
                    ex is InvalidOperationException
                    || ex is Win32Exception
                    || ex is NotSupportedException
                    || ex is AggregateException)
                {
                    // Process already exited, kill failed (access denied / zombie), platform
                    // can't kill the process tree, or the kill produced an aggregate of child
                    // failures. A CancellationToken callback that throws causes Cancel() at
                    // the call site to throw an aggregate, which would then escape the parallel
                    // task body's broad catch — silently broken. Swallow here so the kill is
                    // strictly best-effort.
                }
            });

            // Read stdout and stderr concurrently to avoid deadlock when the child
            // writes enough to fill one pipe's buffer while we're only reading the other.
            var output = new StringBuilder();
            var outputLock = new object();

            Task stdoutTask = ReadStreamAsync(process.StandardOutput, output, outputLock);
            Task stderrTask = ReadStreamAsync(process.StandardError, output, outputLock);

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

            // Use CancellationToken.None so WaitForExitAsync waits for the kill
            // (triggered by the Register callback) to fully complete, giving us a
            // valid ExitCode. Passing cancellationToken here could throw OCE before
            // the process has fully exited, leaving a zombie on Linux.
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

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
            // Dispose-in-finally: must not mask the original exception. Process.Dispose can
            // throw on Windows when the underlying handle is in an unusual state during
            // process-tree teardown after Kill; if that happened, swallowing here keeps the
            // real exception (cancellation, WaitForExit failure) propagating.
            try { process.Dispose(); }
            catch (Exception) { /* dispose-in-finally — original exception must propagate */ }
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
        catch (Exception ex) when (IsSpawnFailure(ex))
        {
            // See ExecuteJobAsync for rationale on the broader catch set.
            string directFault = FormatSpawnFault(ex, invocation);
            if (_options.ShellFallback)
            {
                startInfo = BuildShellFallbackStartInfo(invocation, redirectIo: false);
                try
                {
                    process = Process.Start(startInfo)
                        ?? throw new Win32Exception("Process.Start returned null");
                }
                catch (Exception ex2) when (IsSpawnFailure(ex2))
                {
                    stopwatch.Stop();
                    return new JobResult(
                        JobIndex: jobIndex,
                        ChildExitCode: -1,
                        Output: null,
                        Duration: stopwatch.Elapsed,
                        SourceItems: invocation.SourceItems,
                        Skipped: false,
                        FaultMessage: $"{directFault}; shell fallback: {FormatSpawnFault(ex2, invocation)}");
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
                    Skipped: false,
                    FaultMessage: directFault);
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
                catch (Exception ex) when (
                    ex is InvalidOperationException
                    || ex is Win32Exception
                    || ex is NotSupportedException
                    || ex is AggregateException)
                {
                    // Process already exited, kill failed (access denied / zombie), platform
                    // can't kill the process tree, or the kill produced an aggregate of child
                    // failures. A CancellationToken callback that throws causes Cancel() at
                    // the call site to throw an aggregate, which would then escape the parallel
                    // task body's broad catch — silently broken. Swallow here so the kill is
                    // strictly best-effort.
                }
            });

            // Use CancellationToken.None — see comment in ExecuteJobAsync above.
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

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
            // Dispose-in-finally: must not mask the original exception. Process.Dispose can
            // throw on Windows when the underlying handle is in an unusual state during
            // process-tree teardown after Kill; if that happened, swallowing here keeps the
            // real exception (cancellation, WaitForExit failure) propagating.
            try { process.Dispose(); }
            catch (Exception) { /* dispose-in-finally — original exception must propagate */ }
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
        catch (Exception ex) when (
            ex is IOException
            || ex is UnauthorizedAccessException
            || ex is PlatformNotSupportedException
            || ex is ArgumentException)
        {
            // No terminal available (CI, daemon, sandbox) — surface a diagnostic so the user
            // doesn't get a silent "all jobs skipped, exit 0". --confirm was deliberately
            // requested; silently no-op'ing it is a footgun.
            try
            {
                Console.Error.WriteLine("wargs: --confirm requested but no terminal available; declining.");
            }
            catch (Exception) { /* stderr also unavailable — best-effort */ }
            return false;
        }
    }

    /// <summary>
    /// Identifies <see cref="Process.Start"/> exceptions that should be classified as a spawn
    /// failure (and converted to a JobResult with FaultMessage) rather than escaping into the
    /// parallel-loop broad catch. Catching only <see cref="Win32Exception"/> leaves
    /// <see cref="InvalidOperationException"/> (empty FileName), <see cref="FileNotFoundException"/>,
    /// and <see cref="PlatformNotSupportedException"/> escaping with no observable evidence.
    /// </summary>
    private static bool IsSpawnFailure(Exception ex)
        => ex is Win32Exception
        || ex is InvalidOperationException
        || ex is FileNotFoundException
        || ex is PlatformNotSupportedException
        || ex is ObjectDisposedException;

    /// <summary>
    /// Renders a spawn-failure exception as a one-line diagnostic for <see cref="JobResult.FaultMessage"/>.
    /// </summary>
    private static string FormatSpawnFault(Exception ex, CommandInvocation invocation)
        => $"failed to spawn '{invocation.Command}': {ex.GetType().Name}: {ex.Message}";
}
