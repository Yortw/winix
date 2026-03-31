using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

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

        // Sequential path (P=1). Parallel dispatch will be added in a later task.
        return await RunSequentialAsync(invocations, stdout, stderr, cancellationToken)
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

            JobResult result = await ExecuteJobAsync(invocation, jobIndex, cancellationToken)
                .ConfigureAwait(false);
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
    /// Spawns a single child process, captures merged stdout+stderr, and returns a <see cref="JobResult"/>.
    /// </summary>
    private static async Task<JobResult> ExecuteJobAsync(
        CommandInvocation invocation,
        int jobIndex,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.Command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };

        foreach (string arg in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var stopwatch = Stopwatch.StartNew();
        Process process;

        try
        {
            process = Process.Start(startInfo)
                ?? throw new Win32Exception("Process.Start returned null");
        }
        catch (Win32Exception)
        {
            // Command not found or not executable — record as failed, don't throw.
            stopwatch.Stop();
            return new JobResult(
                JobIndex: jobIndex,
                ChildExitCode: -1,
                Output: null,
                Duration: stopwatch.Elapsed,
                SourceItems: invocation.SourceItems,
                Skipped: false);
        }

        // Close stdin immediately — child commands don't read interactive input
        process.StandardInput.Close();

        try
        {
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
