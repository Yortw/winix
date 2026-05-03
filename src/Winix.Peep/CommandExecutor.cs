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

            // Normalise Windows CRLF line endings to LF at the API boundary. Windows
            // children (notably `dotnet`, `cmd`, and any .NET console app) write \r\n;
            // passing CRLF through to peep's downstream consumers causes:
            //   (a) Display rendering bugs in --once mode — Console.Write on Windows
            //       (with Console.OutputEncoding=UTF8 set by ShellKit) routes through
            //       a different code path than direct console output; ConPTY + mintty
            //       mangles CRLF near terminal-width wrap boundaries. Verified
            //       empirically: same bytes render correctly via `cat` from a file
            //       but break via .NET's Console.Write on the same terminal.
            //   (b) Cross-platform JSON envelope inconsistency — last_output for
            //       `dotnet --version` should be the same shape on Windows and Linux.
            //   (c) Downstream pipe consumers (jq, etc.) expect LF, not CRLF.
            // SnapshotHistory.SplitLines already normalises for diff/render; this is
            // the single API-boundary normalisation that covers Console.Write, JSON
            // envelope last_output, --exit-on-match regex input, and any future
            // consumer of PeepResult.Output.
            captured = captured.Replace("\r\n", "\n");

            return new PeepResult(captured, process.ExitCode, stopwatch.Elapsed, trigger);
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>
    /// Reads a redirected stream in chunks and emits one line at a time into the shared
    /// <paramref name="output"/> StringBuilder, holding the lock only across each line
    /// flush. This prevents cross-stream chunk interleaving from landing mid-line —
    /// which corrupts output when a child writes to BOTH stdout and stderr (notably
    /// `dotnet`, which writes the first line of a not-found-subcommand error to stderr
    /// and the rest to stdout: pre-fix, peep's reader could land a stdout chunk inside
    /// a stderr line, producing output like
    /// <c>"this naCould not execute...me could not be found"</c>).
    /// <para/>
    /// Lines from stdout and stderr can still interleave at line boundaries (true
    /// chronological merge across two pipes is impossible without OS-level support),
    /// but the granularity is line-atomic instead of chunk-atomic. The output cap is
    /// enforced per line; a pathological single line larger than the cap is truncated
    /// at the flush boundary.
    /// </summary>
    // Internal (not private) so deterministic chunk-pattern tests can drive this directly
    // via TextReader subclasses without spawning a real child process. Parameter is the
    // TextReader base type (StreamReader inherits from it) so a custom test reader can
    // return controlled char-buffer chunks on each ReadAsync call. Production call sites
    // pass `process.StandardOutput` / `process.StandardError` (StreamReader) — unchanged.
    internal static async Task ReadStreamAsync(
        TextReader reader, StringBuilder output, object outputLock, TruncationFlag truncation, int maxOutputChars)
    {
        char[] buffer = new char[4096];
        var lineBuffer = new StringBuilder();
        int charsRead;

        // Per-stream lineBuffer cap. If a child writes a stream WITHOUT '\n' (e.g. a binary
        // dump, `cat largebinary`, `dd if=/dev/urandom`, `curl https://huge.bin`), the
        // line-atomic merge would otherwise grow lineBuffer unbounded — the per-line
        // FlushLine at '\n' is the only place the per-call cap is applied. Round-19
        // verification (2026-05-03) caught this regression: today's line-atomic merge fix
        // bypassed the R1 C2 OOM defence (the 64MB DefaultMaxOutputChars cap) for
        // newline-less input. Cap lineBuffer at maxOutputChars so the worst case is one
        // mid-line forced flush per stream — same OOM bound as the chunked-merge era,
        // with only the cosmetic cost of splitting an arbitrarily-long single line at the
        // cap boundary across one extra Append.
        int lineBufferCap = maxOutputChars;

        while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            for (int i = 0; i < charsRead; i++)
            {
                char c = buffer[i];
                lineBuffer.Append(c);
                if (c == '\n')
                {
                    FlushLine(lineBuffer, output, outputLock, truncation, maxOutputChars);
                    lineBuffer.Clear();
                }
                else if (lineBuffer.Length >= lineBufferCap)
                {
                    // OOM safety: forced mid-line flush. FlushLine respects the per-call
                    // cap and trips truncation if appending the partial would exceed it,
                    // so the user-visible "[peep: output truncated ...]" marker still
                    // fires via the post-merge handler. After flush, continue reading
                    // (don't break the read loop) so the child's pipe still drains and
                    // we don't wedge a runaway producer.
                    FlushLine(lineBuffer, output, outputLock, truncation, maxOutputChars);
                    lineBuffer.Clear();
                }
            }
        }

        // Final partial line at EOF (no trailing newline) — flush so it isn't lost.
        if (lineBuffer.Length > 0)
        {
            FlushLine(lineBuffer, output, outputLock, truncation, maxOutputChars);
        }
    }

    /// <summary>
    /// Atomically appends one line (already accumulated in <paramref name="line"/>) to
    /// the shared output buffer under the lock, applying the per-call truncation cap.
    /// </summary>
    private static void FlushLine(
        StringBuilder line, StringBuilder output, object outputLock,
        TruncationFlag truncation, int maxOutputChars)
    {
        lock (outputLock)
        {
            if (truncation.Triggered)
            {
                // Already capped — drop the line so the child still drains its pipe
                // (no wedge), but no further bytes are appended.
                return;
            }

            int remaining = maxOutputChars - output.Length;
            if (remaining <= 0)
            {
                truncation.Triggered = true;
                return;
            }

            int toAppend = Math.Min(line.Length, remaining);
            output.Append(line.ToString(0, toAppend));
            if (toAppend < line.Length)
            {
                truncation.Triggered = true;
            }
        }
    }

    /// <summary>
    /// Shared mutable state between stdout and stderr readers indicating whether the
    /// output cap has been reached. Mutated under <c>outputLock</c>.
    /// </summary>
    // Internal so deterministic ReadStreamAsync tests can construct one. Production
    // call site (RunAsync) is the only producer; consumers are the two ReadStreamAsync
    // tasks plus the post-merge truncation marker emit at line ~195.
    internal sealed class TruncationFlag
    {
        public bool Triggered;
    }
}
