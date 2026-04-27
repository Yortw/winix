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

        // Detach the spawned task's stdio from our terminal AND background it via shell:
        //  - </dev/null  detaches stdin so the child cannot consume keystrokes
        //  - >/dev/null 2>&1  prevents the task's output mixing into schedule's stderr
        //  - &  backgrounds the job so /bin/sh exits immediately, leaving the task running
        // Without redirection the child inherits stdio (UseShellExecute=false + no
        // RedirectStandard*) and any output the task produces appears interleaved with
        // schedule's own messages on the user's terminal.
        string detachedCommand = target.Command + " </dev/null >/dev/null 2>&1 &";
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
                throw new CrontabUnavailableException($"crontab did not respond within {CrontabTimeoutMs / 1000}s.");
            }

            // Drain the stderr worker so it doesn't outlive this method.
            try { stderrTask.Wait(5_000); }
            catch (AggregateException) { /* swallow read errors after exit */ }

            // Exit code 1 with "no crontab for ..." is normal on most Unix systems; treat
            // any non-zero exit as "no entries" rather than failing the operation. The
            // CrontabUnavailableException path above already handles the binary-missing case.
            return process.ExitCode == 0 ? output : "";
        }
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
                throw new CrontabUnavailableException($"crontab rejected input: {ex.Message}", ex);
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
                throw new CrontabUnavailableException($"crontab did not respond within {CrontabTimeoutMs / 1000}s.");
            }

            string stderr;
            try { stderr = stderrTask.Wait(5_000) ? (stderrTask.Result ?? "") : ""; }
            catch (AggregateException) { stderr = ""; }

            if (process.ExitCode != 0)
            {
                throw new CrontabUnavailableException(
                    $"crontab failed (exit {process.ExitCode}): {stderr.Trim()}");
            }

            return stderr.Trim();
        }
    }

    /// <summary>
    /// Signals that the user's crontab is unreachable — the binary is missing, the process
    /// failed to start, the process exited non-zero, or it timed out. Carried as a typed
    /// exception so callers can map it to a clean <see cref="ScheduleResult"/>.Fail rather
    /// than letting <see cref="Win32Exception"/> escape uncaught and produce a stack trace.
    /// </summary>
    private sealed class CrontabUnavailableException : InvalidOperationException
    {
        public CrontabUnavailableException(string message) : base(message) { }
        public CrontabUnavailableException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Builds a shell command string from a command and its arguments.
    /// Arguments containing spaces or shell-special characters are single-quote escaped.
    /// </summary>
    private static string BuildCommandString(string command, string[] arguments)
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
            // Single-quote escape any argument that contains characters the shell would interpret.
            if (arg.Contains(' ') || arg.Contains('\'') || arg.Contains('"') || arg.Contains('$') || arg.Contains('\\'))
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
}
