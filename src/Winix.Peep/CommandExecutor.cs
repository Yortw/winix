using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Yort.ShellKit;

namespace Winix.Peep;

/// <summary>
/// Spawns a child process, captures merged stdout+stderr output, and returns a <see cref="PeepResult"/>.
/// ANSI escape sequences in the child's output are preserved.
/// </summary>
public static class CommandExecutor
{
    /// <summary>
    /// Runs the specified command with arguments, capturing all output.
    /// </summary>
    /// <param name="command">The executable name or full path to run.</param>
    /// <param name="arguments">Arguments to pass to the process.</param>
    /// <param name="trigger">What triggered this execution (for inclusion in the result).</param>
    /// <param name="cancellationToken">Cancellation token to abort the run. When cancelled, the child process is killed.</param>
    /// <returns>A <see cref="PeepResult"/> with the merged output, exit code, duration, and trigger source.</returns>
    /// <exception cref="CommandNotFoundException">The command was not found on PATH.</exception>
    /// <exception cref="CommandNotExecutableException">The command exists but cannot be executed.</exception>
    public static async Task<PeepResult> RunAsync(
        string command,
        string[] arguments,
        TriggerSource trigger,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };

        foreach (string arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var stopwatch = Stopwatch.StartNew();
        Process process;

        try
        {
            process = Process.Start(startInfo)
                ?? throw new CommandNotFoundException(command);
        }
        catch (Win32Exception ex)
        {
            // Win32Exception is thrown on all .NET platforms (not just Windows).
            // .NET maps POSIX errors to Win32 error codes on Linux/macOS.
            // ERROR_ACCESS_DENIED (5) on Windows, EACCES (13) on Linux/macOS.
            // ERROR_FILE_NOT_FOUND (2), ERROR_PATH_NOT_FOUND (3), ENOENT (2).
            if (ex.NativeErrorCode == 5 || ex.NativeErrorCode == 13)
            {
                throw new CommandNotExecutableException(command);
            }

            throw new CommandNotFoundException(command);
        }

        // Close stdin immediately -- peep commands don't read interactive input
        process.StandardInput.Close();

        try
        {
            // Read stdout and stderr concurrently to avoid deadlock when the child
            // writes enough to fill one pipe's buffer while we're only reading the other.
            var output = new StringBuilder();
            var outputLock = new object();

            Task stdoutTask = ReadStreamAsync(process.StandardOutput, output, outputLock);
            Task stderrTask = ReadStreamAsync(process.StandardError, output, outputLock);

            // Wait for both stream reads to complete and for the process to exit.
            // If cancelled, kill the child process so we don't leak it.
            using var reg = cancellationToken.Register(() =>
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
                    // Process already exited between the check and the kill -- safe to ignore.
                }
            });

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            // If the process was killed by our cancellation callback, WaitForExitAsync
            // may return without throwing because the process already exited. Honour the
            // caller's cancellation request explicitly.
            cancellationToken.ThrowIfCancellationRequested();

            string captured;
            lock (outputLock)
            {
                captured = output.ToString();
            }

            return new PeepResult(captured, process.ExitCode, stopwatch.Elapsed, trigger);
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>
    /// Reads a redirected stream in chunks and appends to the shared StringBuilder.
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
}
