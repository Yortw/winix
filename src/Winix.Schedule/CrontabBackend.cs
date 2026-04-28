#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Winix.Schedule;

/// <summary>
/// Linux/macOS scheduler backend that manages the user's crontab.
/// Winix-managed entries are identified by <c># winix:&lt;name&gt;</c> comment tags
/// on the line preceding each cron entry.
/// </summary>
public sealed class CrontabBackend : ISchedulerBackend
{
    /// <summary>
    /// Maximum time to wait for a crontab subprocess to exit. crontab against an unreachable
    /// PAM/LDAP backend can hang; this ceiling keeps the tool responsive.
    /// </summary>
    private const int CrontabTimeoutMs = 30_000;

    /// <inheritdoc />
    public ScheduleResult Add(string name, CronExpression cron, string command, string[] arguments, string folder)
    {
        string fullCommand = BuildCommandString(command, arguments);
        try
        {
            string currentCrontab = ReadCrontab();
            string newCrontab = CrontabParser.AddEntry(currentCrontab, name, cron.Expression, fullCommand);
            string warnings = WriteCrontab(newCrontab);
            return ScheduleResult.OkWithWarning($"Created task '{name}'.", warnings);
        }
        catch (CrontabUnavailableException ex)
        {
            return ScheduleResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ScheduledTask> List(string? folder, bool all)
    {
        // List returns an empty collection on read failure; the interface offers no
        // way to signal "scheduler unavailable" to the caller. A future iteration may
        // widen ISchedulerBackend.List to return a result type.
        try
        {
            string crontab = ReadCrontab();
            return CrontabParser.ParseEntries(crontab, winixOnly: !all);
        }
        catch (CrontabUnavailableException)
        {
            return Array.Empty<ScheduledTask>();
        }
    }

    /// <inheritdoc />
    public ScheduleResult Remove(string name, string folder)
    {
        try
        {
            string crontab = ReadCrontab();
            string newCrontab = CrontabParser.RemoveEntry(crontab, name);

            if (crontab == newCrontab)
            {
                return ScheduleResult.Fail($"Task '{name}' not found.");
            }

            string warnings = WriteCrontab(newCrontab);
            return ScheduleResult.OkWithWarning($"Removed task '{name}'.", warnings);
        }
        catch (CrontabUnavailableException ex)
        {
            return ScheduleResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public ScheduleResult Enable(string name, string folder)
    {
        try
        {
            string crontab = ReadCrontab();
            string newCrontab = CrontabParser.EnableEntry(crontab, name);

            // Mirror Remove's symmetry: when nothing changed, the entry was either missing
            // or already in the desired state. Reporting "Enabled X" for a non-existent
            // task is silent success — the user acts on a typo with no warning.
            if (crontab == newCrontab)
            {
                return ScheduleResult.Fail($"Task '{name}' not found or already enabled.");
            }

            string warnings = WriteCrontab(newCrontab);
            return ScheduleResult.OkWithWarning($"Enabled task '{name}'.", warnings);
        }
        catch (CrontabUnavailableException ex)
        {
            return ScheduleResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public ScheduleResult Disable(string name, string folder)
    {
        try
        {
            string crontab = ReadCrontab();
            string newCrontab = CrontabParser.DisableEntry(crontab, name);

            if (crontab == newCrontab)
            {
                return ScheduleResult.Fail($"Task '{name}' not found or already disabled.");
            }

            string warnings = WriteCrontab(newCrontab);
            return ScheduleResult.OkWithWarning($"Disabled task '{name}'.", warnings);
        }
        catch (CrontabUnavailableException ex)
        {
            return ScheduleResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public ScheduleResult Run(string name, string folder)
    {
        ScheduledTask? target;
        try
        {
            string crontab = ReadCrontab();
            var tasks = CrontabParser.ParseEntries(crontab, winixOnly: true);

            target = null;
            foreach (var task in tasks)
            {
                if (string.Equals(task.Name, name, StringComparison.Ordinal))
                {
                    target = task;
                    break;
                }
            }
        }
        catch (CrontabUnavailableException ex)
        {
            return ScheduleResult.Fail(ex.Message);
        }

        if (target is null)
        {
            return ScheduleResult.Fail($"Task '{name}' not found.");
        }

        // Build the detached/backgrounded shell command. See BuildRunDetachedCommand for
        // the full reasoning on terminator handling.
        string detachedCommand = BuildRunDetachedCommand(target.Command);
        var psi = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(detachedCommand);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return ScheduleResult.Fail($"Failed to run task '{name}': Process.Start returned null for /bin/sh.");
            }

            // Brief wait. If /bin/sh fails to spawn the backgrounded job (syntax error,
            // missing executable in $PATH), it will exit non-zero within milliseconds.
            // The actual task continues in the background regardless.
            if (process.WaitForExit(500) && process.ExitCode != 0)
            {
                return ScheduleResult.Fail($"Failed to run task '{name}': /bin/sh exited with code {process.ExitCode}.");
            }
        }
        catch (Win32Exception ex)
        {
            return ScheduleResult.Fail($"Failed to run task '{name}': /bin/sh not available ({ex.Message}).");
        }
        catch (InvalidOperationException ex)
        {
            return ScheduleResult.Fail($"Failed to run task '{name}': {ex.Message}");
        }

        return ScheduleResult.Ok($"Triggered task '{name}'.");
    }

    /// <inheritdoc />
    public IReadOnlyList<TaskRunRecord> GetHistory(string name, string folder)
    {
        // Crontab has no built-in run history. The console app displays a note about this.
        return Array.Empty<TaskRunRecord>();
    }

    /// <summary>
    /// Reads the current user crontab via <c>crontab -l</c>.
    /// Returns an empty string when the user has no crontab (a normal state on most systems).
    /// </summary>
    /// <exception cref="CrontabUnavailableException">
    /// Thrown when the <c>crontab</c> binary is not on PATH. Distinguishing this from "no
    /// crontab" lets callers surface a clear "scheduler not installed" diagnostic instead of
    /// silently treating a missing binary as an empty crontab.
    /// </exception>
    private static string ReadCrontab()
    {
        var psi = new ProcessStartInfo("crontab")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-l");

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new CrontabUnavailableException("Failed to start crontab process.");
        }
        catch (Win32Exception ex)
        {
            throw new CrontabUnavailableException("crontab command not found on PATH.", ex);
        }

        using (process)
        {
            // Drain stderr concurrently so a verbose-warning crontab can't fill its pipe and
            // wedge the parent on stdout. crontab implementations on PAM-stacked systems
            // routinely write deprecation/auth warnings to stderr.
            Task<string> stderrTask = Task.Run(() =>
            {
                try { return process.StandardError.ReadToEnd(); }
                catch (IOException) { return ""; }
            });

            string output;
            try { output = process.StandardOutput.ReadToEnd(); }
            catch (IOException) { output = ""; }

            if (!process.WaitForExit(CrontabTimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* already exited */ }
                catch (Win32Exception) { /* race */ }
                DrainStderrTask(stderrTask);
                throw new CrontabUnavailableException($"crontab did not respond within {CrontabTimeoutMs / 1000}s.");
            }

            // Drain the stderr worker so it doesn't outlive this method, capturing its
            // text so we can distinguish "no crontab yet" from a real read failure.
            string stderr = DrainStderrTask(stderrTask);

            return InterpretReadResult(process.ExitCode, output, stderr);
        }
    }

    /// <summary>
    /// Interprets the captured result of <c>crontab -l</c>. Internal so tests can pin the
    /// branching contract without spawning a subprocess.
    /// </summary>
    /// <param name="exitCode">The exit code returned by the crontab process.</param>
    /// <param name="stdout">Captured standard output (the user's crontab content on success).</param>
    /// <param name="stderr">Captured standard error (used to disambiguate failure modes).</param>
    /// <returns>
    /// The crontab content on exit code 0, or an empty string when the user simply has no
    /// crontab installed yet (vixie/cronie/BSD all emit "no crontab for $USER" on stderr
    /// with a non-zero exit for this case).
    /// </returns>
    /// <exception cref="CrontabUnavailableException">
    /// Thrown when the exit code is non-zero AND the stderr does not match the legitimate
    /// "no crontab for ..." pattern. Pre-fix, every non-zero exit silently produced an
    /// empty baseline, and the next <see cref="WriteCrontab"/> call destroyed any existing
    /// crontab — silent data loss reported as a successful action. The exception message
    /// embeds the trimmed stderr (or the exit code, when stderr is empty) so the user has
    /// enough to diagnose cron.deny, PAM/LDAP, or spool-lock failure.
    /// </exception>
    internal static string InterpretReadResult(int exitCode, string stdout, string stderr)
    {
        if (exitCode == 0)
        {
            return stdout;
        }

        // "no crontab for $USER" is the canonical empty-state marker across vixie, cronie,
        // BSD, busybox. Case-insensitive substring keeps us forward-compatible with prefix
        // variations like "crontab: no crontab for ...".
        if (stderr.Contains("no crontab for", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        string trimmed = stderr.Trim();
        string detail = trimmed.Length == 0
            ? $"crontab -l exited with code {exitCode} and no diagnostic output."
            : $"crontab -l failed (exit {exitCode}): {trimmed}";
        throw new CrontabUnavailableException(detail);
    }

    /// <summary>
    /// Writes new content to the user crontab by piping to <c>crontab -</c>.
    /// stderr is drained concurrently to prevent buffer-fill deadlock if crontab emits
    /// PAM/locale warnings while we're still writing stdin.
    /// </summary>
    /// <returns>
    /// stderr content captured during a successful write, or an empty string. Real
    /// <c>crontab -</c> implementations frequently print non-fatal warnings to stderr
    /// while still returning a zero exit code (e.g. "Skipping line N: bad day-of-month",
    /// PAM/SELinux notices). Callers surface this back to the user via
    /// <see cref="ScheduleResult.OkWithWarning"/> so silent partial-success can't hide
    /// a dropped task line.
    /// </returns>
    /// <exception cref="CrontabUnavailableException">
    /// Thrown when the <c>crontab</c> binary is not on PATH, the process fails to start, or
    /// the write returns a non-zero exit code. The exception message includes any stderr
    /// captured from the failed crontab invocation so callers can surface a clear diagnostic.
    /// </exception>
    /// <remarks>
    /// This is a read-modify-write across two crontab invocations and is NOT atomic against
    /// concurrent edits or signal-driven termination. A user receiving SIGTERM mid-write may
    /// be left with a truncated crontab. Atomicity via a temp-file pattern is deferred.
    /// </remarks>
    private static string WriteCrontab(string content)
    {
        var psi = new ProcessStartInfo("crontab")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-");

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new CrontabUnavailableException("Failed to start crontab process.");
        }
        catch (Win32Exception ex)
        {
            throw new CrontabUnavailableException("crontab command not found on PATH.", ex);
        }

        using (process)
        {
            // Drain stderr on a worker BEFORE writing stdin — without this, a child that
            // fills its stderr pipe buffer (~4KB on Linux default) blocks on write while the
            // parent blocks on StandardInput.Write past the stdin buffer → deadlock.
            Task<string> stderrTask = Task.Run(() =>
            {
                try { return process.StandardError.ReadToEnd(); }
                catch (IOException) { return ""; }
            });

            try
            {
                process.StandardInput.Write(content);
            }
            catch (IOException ex)
            {
                // crontab rejected input early and closed stdin; the truncated content may have
                // been committed depending on the implementation. Still try to read stderr.
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* already exited */ }
                catch (Win32Exception) { /* race */ }
                DrainStderrTask(stderrTask);
                throw new CrontabUnavailableException(
                    $"crontab connection failed mid-write — your crontab may be partially modified; "
                  + $"run 'crontab -l' to verify. Underlying error: {ex.Message}", ex);
            }
            finally
            {
                try { process.StandardInput.Close(); } catch (IOException) { /* already closed */ }
            }

            if (!process.WaitForExit(CrontabTimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* already exited */ }
                catch (Win32Exception) { /* race */ }
                DrainStderrTask(stderrTask);
                throw new CrontabUnavailableException($"crontab did not respond within {CrontabTimeoutMs / 1000}s.");
            }

            string stderr;
            try { stderr = stderrTask.Wait(5_000) ? (stderrTask.Result ?? "") : ""; }
            catch (AggregateException) { stderr = ""; }

            if (process.ExitCode != 0)
            {
                // Some crontab implementations emit nothing on stderr even when failing
                // (e.g. permission-denied returns 1 with empty stderr on macOS). A bare
                // "crontab failed (exit 1):" message with a trailing colon is confusing —
                // surface "no stderr output" so the user knows there's no further detail
                // to dig into rather than wondering what got truncated.
                string detail = string.IsNullOrWhiteSpace(stderr) ? "no stderr output" : stderr.Trim();
                throw new CrontabUnavailableException(
                    $"crontab failed (exit {process.ExitCode}): {detail}");
            }

            return stderr.Trim();
        }
    }

    /// <summary>
    /// Wraps the user's stored command in a brace-group plus stdin/stdout/stderr redirection
    /// and trailing background-operator, so <c>/bin/sh -c</c> launches it detached from the
    /// parent terminal.
    /// </summary>
    /// <remarks>
    /// The brace-group binds the redirects and trailing <c>&amp;</c> to the whole compound.
    /// Without the braces:
    /// <list type="bullet">
    /// <item><c>cmd1 | cmd2</c> leaks the redirect to cmd2 only — cmd1's stdout still inherits.</item>
    /// <item><c>cmd1 &amp;&amp; cmd2</c> is mostly fine, but multi-statement compounds need explicit grouping for the redirect to apply uniformly.</item>
    /// </list>
    /// Inside the braces, POSIX requires a list-terminator (<c>;</c>, newline, or <c>&amp;</c>)
    /// before the closing brace. We append <c>;</c> by default. Two cases must skip our
    /// terminator to avoid producing an empty-statement parse error
    /// (<c>{ cmd &amp; ; }</c> is rejected by bash/dash):
    /// <list type="bullet">
    /// <item>The user's command already ends with a bare <c>&amp;</c> (backgrounded). Note this
    /// is distinct from <c>&amp;&amp;</c> which is a logical-AND operator and DOES need a terminator.</item>
    /// <item>The user's command already ends with <c>;</c>.</item>
    /// </list>
    /// Method is internal so tests can pin the wrap shape without spawning <c>/bin/sh</c>.
    /// </remarks>
    internal static string BuildRunDetachedCommand(string command)
    {
        string trimmed = command.TrimEnd();

        // 'cmd &' (backgrounded) — '&' is itself a list terminator. Distinguish from '&&'.
        bool endsBackgrounded = trimmed.EndsWith('&')
            && !trimmed.EndsWith("&&", StringComparison.Ordinal);
        bool endsTerminated = trimmed.EndsWith(';');

        string terminator = (endsBackgrounded || endsTerminated) ? "" : "; ";
        return "{ " + command + " " + terminator + "} </dev/null >/dev/null 2>&1 &";
    }

    /// <summary>
    /// Best-effort drain of the stderr-reader worker before exiting the
    /// <see cref="System.Diagnostics.Process"/>'s <c>using</c> block. Without this, throw
    /// paths in <see cref="ReadCrontab"/> and <see cref="WriteCrontab"/> would let the
    /// stderr Task outlive the Process — when Dispose closes the underlying StandardError
    /// stream, the in-flight ReadToEnd would race against handle disposal. The worker's
    /// own <see cref="IOException"/> catch makes that race benign in practice, but the
    /// success path explicitly waits 5s for the worker; the failure paths should observe
    /// the same invariant. Bounded at 1s so a wedged Task can't extend the failure latency.
    /// </summary>
    private static string DrainStderrTask(Task<string> stderrTask)
    {
        try
        {
            return stderrTask.Wait(1_000) ? (stderrTask.Result ?? "") : "";
        }
        catch (AggregateException)
        {
            // Swallow read errors — task was best-effort. Caller treats empty stderr
            // as "no diagnostic output," not as "everything was fine."
            return "";
        }
    }

    /// <summary>
    /// Signals that the user's crontab is unreachable — the binary is missing, the process
    /// failed to start, the process exited non-zero, or it timed out. Carried as a typed
    /// exception so callers can map it to a clean <see cref="ScheduleResult"/>.Fail rather
    /// than letting <see cref="Win32Exception"/> escape uncaught and produce a stack trace.
    /// </summary>
    /// <summary>
    /// Signals that the <c>crontab</c> binary is unreachable, denied, or returned a non-zero
    /// exit with output that does not match the legitimate "no crontab for $USER" pattern.
    /// Internal so tests can pin the read-failure contract.
    /// </summary>
    internal sealed class CrontabUnavailableException : InvalidOperationException
    {
        public CrontabUnavailableException(string message) : base(message) { }
        public CrontabUnavailableException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Builds a shell command string from a command and its arguments. Arguments containing
    /// any character the shell would tokenise specially are single-quote escaped using the
    /// canonical bash <c>'\''</c> apostrophe-escape pattern. Internal so tests can pin the
    /// quoting contract.
    /// </summary>
    /// <remarks>
    /// The character set <see cref="ShellSpecialChars"/> is conservative: any argument
    /// containing one of those characters is quoted. This is the Linux/macOS analogue of
    /// <c>SchtasksBackend.EscapeWindowsArg</c> — without it, an argument like
    /// <c>cmd1;rm</c> (no whitespace) would land in the crontab as a literal injection of
    /// a second command. The space, single-quote, double-quote, dollar, backslash set was
    /// the original R1 cover; <c>;</c>, <c>&amp;</c>, <c>|</c>, <c>&lt;</c>, <c>&gt;</c>,
    /// backtick, and parens were added in R3 because they're equally dangerous in cron's
    /// shell context but didn't trigger the original quote check.
    /// </remarks>
    internal static string BuildCommandString(string command, string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return command;
        }

        var sb = new StringBuilder();
        sb.Append(command);
        foreach (string arg in arguments)
        {
            sb.Append(' ');
            if (arg.IndexOfAny(ShellSpecialChars) >= 0)
            {
                sb.Append('\'');
                sb.Append(arg.Replace("'", "'\\''")); // Terminate quote, escaped apostrophe, re-open quote.
                sb.Append('\'');
            }
            else
            {
                sb.Append(arg);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Characters whose presence in an argument forces single-quote escaping. Intentionally
    /// conservative — any of these would change tokenisation, redirection, command
    /// chaining, command substitution, glob expansion, or comment behaviour if left
    /// unquoted in the resulting crontab line.
    /// </summary>
    private static readonly char[] ShellSpecialChars =
    {
        ' ', '\t',
        '\'', '"',
        '$', '\\', '`',
        ';', '&', '|',
        '<', '>',
        '(', ')',
        '*', '?', '[', ']',
        '#', '~', '!',
    };
}
