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
    /// Default cap on captured child output, in characters. A runaway child (e.g. a
    /// `find /` that should have been gitignored, or an infinite loop printing to
    /// stdout) would otherwise grow the StringBuilder without bound and OOM the peep
    /// process. 64 MB is generous enough that any realistic interactive output fits,
    /// while still bounded.
    /// </summary>
    public const int DefaultMaxOutputChars = 64 * 1024 * 1024;

    /// <summary>
    /// Runs the specified command with arguments, capturing all output.
    /// </summary>
    /// <param name="command">The executable name or full path to run.</param>
    /// <param name="arguments">Arguments to pass to the process.</param>
    /// <param name="trigger">What triggered this execution (for inclusion in the result).</param>
    /// <param name="cancellationToken">Cancellation token to abort the run. When cancelled, the child process is killed.</param>
    /// <param name="maxOutputChars">
    /// Cap on captured output in characters. Beyond this, output is truncated and a trailing
    /// marker is appended. Null (default) uses <see cref="DefaultMaxOutputChars"/>.
    /// Per-call parameter (rather than a static seam) so tests running in parallel cannot
    /// race on the cap value, AND so user-facing error text never carries a test-overridden
    /// number leaking from a concurrent test case.
    /// </param>
    /// <returns>A <see cref="PeepResult"/> with the merged output, exit code, duration, and trigger source.</returns>
    /// <exception cref="CommandNotFoundException">The command was not found on PATH.</exception>
    /// <exception cref="CommandNotExecutableException">The command exists but cannot be executed.</exception>
    /// <exception cref="CommandStreamException">Reading the child's stdout/stderr failed (e.g. the child closed a pipe abnormally). The child process is killed before this is thrown.</exception>
    public static async Task<PeepResult> RunAsync(
        string command,
        string[] arguments,
        TriggerSource trigger,
        CancellationToken cancellationToken = default,
        int? maxOutputChars = null)
    {
        int effectiveMaxOutputChars = maxOutputChars ?? DefaultMaxOutputChars;
        if (effectiveMaxOutputChars < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOutputChars),
                "maxOutputChars must be non-negative.");
        }
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
        catch (FileNotFoundException)
        {
            // .NET 5+ may surface a missing executable as FileNotFoundException directly
            // rather than wrapping it in Win32Exception. Map to the typed exit reason
            // (command_not_found / 127) so the user sees the right diagnostic instead of
            // hitting the watch-loop's last-resort "unexpected error" arm.
            throw new CommandNotFoundException(command);
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                    or PlatformNotSupportedException
                                    or ArgumentException)
        {
            // Process.Start can throw these for malformed StartInfo, an empty FileName,
            // or platform configurations that don't support process spawn. These are
            // structural problems with the request rather than runtime command failures
            // — surface as command_not_executable (126) so the user sees a typed exit
            // code instead of unexpected_error (the catch-all in InteractiveSession).
            throw new CommandNotExecutableException(command);
        }

        // Close stdin immediately -- peep commands don't read interactive input.
        // Wrap in try/catch because a fast-exiting child (echo-style commands or one
        // that died on its own start) can have its stdin pipe already gone by the time
        // Close() runs, raising IOException ("pipe has been ended") — closing a pipe
        // that's already gone is the success state we want, not an error to surface.
        // Without this, peep emits a flaky "unexpected error: IOException" envelope
        // that looks like a CI flake but is actually a race.
        try { process.StandardInput.Close(); }
        catch (IOException) { /* child already exited and pipe is gone — benign */ }
        catch (ObjectDisposedException) { /* same */ }

        try
        {
            // Read stdout and stderr concurrently to avoid deadlock when the child
            // writes enough to fill one pipe's buffer while we're only reading the other.
            var output = new StringBuilder();
            var outputLock = new object();
            var truncation = new TruncationFlag();

            Task stdoutTask = ReadStreamAsync(process.StandardOutput, output, outputLock, truncation, effectiveMaxOutputChars);
            Task stderrTask = ReadStreamAsync(process.StandardError, output, outputLock, truncation, effectiveMaxOutputChars);

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
                catch (Exception)
                {
                    // Diagnostic must be strictly weaker than production. A Kill failure here
                    // (already-exited race, Win32 access denied on protected process, Linux
                    // PID-reuse race) must not propagate out of the cancellation callback —
                    // doing so would re-throw at `using var reg`'s dispose point and corrupt
                    // the alternate-screen-buffer state during teardown.
                }
            });

            try
            {
                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            }
            catch (Exception streamEx) when (streamEx is IOException or ObjectDisposedException)
            {
                // A redirected pipe became invalid mid-read. Kill the child to avoid
                // a leaked process, then surface a typed exception so the watch loop
                // can produce a clean error envelope rather than crashing the session.
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception)
                {
                    // best effort — see Register-callback rationale above.
                }
                throw new CommandStreamException(
                    $"failed to read child output ({streamEx.GetType().Name}): {streamEx.Message}",
                    streamEx);
            }

            // Use CancellationToken.None so WaitForExitAsync waits for the kill
            // (triggered by the Register callback above) to fully complete, giving
            // us a valid ExitCode. Passing cancellationToken here could throw OCE
            // before the process has fully exited, making ExitCode unreliable.
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

            stopwatch.Stop();

            // Now that the process has fully exited, honour the caller's cancellation.
            cancellationToken.ThrowIfCancellationRequested();

            string captured;
            lock (outputLock)
            {
                if (truncation.Triggered)
                {
                    output.AppendLine();
                    output.Append($"[peep: output truncated at {effectiveMaxOutputChars:N0} characters; child likely runaway]");
                }
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
    /// The lock serialises interleaved stdout/stderr writes so chunks don't get torn,
    /// and enforces the <paramref name="maxOutputChars"/> cap by setting the shared
    /// <paramref name="truncation"/> flag and dropping further input once exceeded.
    /// </summary>
    private static async Task ReadStreamAsync(
        StreamReader reader, StringBuilder output, object outputLock, TruncationFlag truncation, int maxOutputChars)
    {
        char[] buffer = new char[4096];
        int charsRead;

        while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            lock (outputLock)
            {
                if (truncation.Triggered)
                {
                    // Already over cap — drain and discard the rest so the child can't
                    // wedge on a full pipe. Continue the loop rather than break: the
                    // child's writes still need to drain so it sees EPIPE / EOF cleanly.
                    continue;
                }

                int remaining = maxOutputChars - output.Length;
                if (remaining <= 0)
                {
                    truncation.Triggered = true;
                    continue;
                }

                int toAppend = Math.Min(charsRead, remaining);
                output.Append(buffer, 0, toAppend);
                if (toAppend < charsRead)
                {
                    truncation.Triggered = true;
                }
            }
        }
    }

    /// <summary>
    /// Shared mutable state between stdout and stderr readers indicating whether the
    /// output cap has been reached. Mutated under <c>outputLock</c>.
    /// </summary>
    private sealed class TruncationFlag
    {
        public bool Triggered;
    }
}
