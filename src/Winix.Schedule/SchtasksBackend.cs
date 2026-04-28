#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace Winix.Schedule;

/// <summary>
/// Windows scheduler backend that delegates to <c>schtasks.exe</c> for task management.
/// Guaranteed AOT-compatible (pure process spawning, no COM interop).
/// </summary>
public sealed class SchtasksBackend : ISchedulerBackend
{
    /// <inheritdoc />
    public ScheduleResult Add(string name, CronExpression cron, string command, string[] arguments, string folder)
    {
        string taskPath = BuildTaskPath(folder, name);

        // Map cron to schtasks schedule parameters before any I/O — if the expression has
        // no clean mapping, fail fast with a clear diagnostic rather than registering a
        // task that runs every minute, 24/7. The original fallback silently mis-scheduled.
        SchtasksSchedule schedule = CronToSchtasksMapper.Map(cron);
        if (schedule.Degraded)
        {
            return ScheduleResult.Fail(schedule.DegradedReason ?? "cron expression cannot be mapped to schtasks.");
        }

        // Build the command string for schtasks /TR.
        // schtasks /TR takes a single string. We must quote the command and arguments.
        string taskRun = BuildTaskRunString(command, arguments);

        var args = new List<string>
        {
            "/Create",
            "/TN", taskPath,
            "/TR", taskRun,
            "/SC", schedule.ScheduleType,
            "/F", // Force overwrite if exists.
        };

        if (schedule.Modifier != null)
        {
            args.Add("/MO");
            args.Add(schedule.Modifier);
        }

        if (schedule.StartTime != null)
        {
            args.Add("/ST");
            args.Add(schedule.StartTime);
        }

        if (schedule.Days != null)
        {
            args.Add("/D");
            args.Add(schedule.Days);
        }

        if (schedule.DayOfMonth != null)
        {
            args.Add("/D");
            args.Add(schedule.DayOfMonth);
        }

        // Run the task with limited privileges by default.
        args.Add("/RL");
        args.Add("LIMITED");

        var result = RunSchtasks(args.ToArray());

        if (result.ExitCode != 0)
        {
            return ScheduleResult.Fail($"schtasks failed: {result.Stderr}");
        }

        // schtasks /Create does not have a /Comment flag. Ideally we'd store the cron
        // expression by using /XML to embed it in the task description, but for v1 the
        // `list` command will display the cron from the schedule mapping.
        // TODO: Use schtasks /Create /XML to embed the cron expression in the task description.

        return ScheduleResult.OkWithWarning($"Created task '{name}'.", result.Stderr);
    }

    /// <inheritdoc />
    public ScheduleListResult List(string? folder, bool all)
    {
        string queryFolder = NormaliseFolderForQuery(folder);

        var args = all
            ? new[] { "/Query", "/FO", "CSV", "/V", "/NH" }
            : new[] { "/Query", "/TN", queryFolder, "/FO", "CSV", "/V", "/NH" };

        var result = RunSchtasks(args);

        if (result.ExitCode != 0)
        {
            // The "folder doesn't exist / no matching tasks" case is a normal empty: schtasks
            // returns a non-zero exit with stderr matching "cannot find the file specified" or
            // "no tasks". Anything else (service stopped, RPC unavailable, access denied,
            // corrupted task store) MUST surface — pre-R4 every non-zero exit collapsed to an
            // empty list and the user thought they had no tasks while the service was wedged.
            if (IsBenignSchtasksEmpty(result.Stderr))
            {
                return ScheduleListResult.Ok(Array.Empty<ScheduledTask>());
            }

            string detail = string.IsNullOrWhiteSpace(result.Stderr)
                ? $"schtasks query failed with exit code {result.ExitCode}."
                : $"schtasks query failed (exit {result.ExitCode}): {result.Stderr.Trim()}";
            return ScheduleListResult.Unavailable(detail);
        }

        var tasks = SchtasksCsvParser.Parse(result.Stdout, queryFolder);
        return ScheduleListResult.OkWithWarning(tasks, result.Stderr);
    }

    /// <summary>
    /// Normalises a folder argument for use with <c>schtasks /Query /TN</c>. Empty/null
    /// becomes the default <c>\Winix</c>. Trailing backslashes are stripped because schtasks
    /// distinguishes them: <c>\Winix</c> fails with "cannot find the file specified" (matched
    /// by <see cref="IsBenignSchtasksEmpty"/> as a clean empty), while <c>\Winix\</c> fails
    /// with "filename, directory name, or volume label syntax is incorrect" (NOT matched —
    /// would surface as an unhelpful Unavailable error after the R4 I3+I4 widening).
    /// Symmetrical with <see cref="BuildTaskPath"/>'s <c>TrimEnd('\\')</c>.
    /// </summary>
    internal static string NormaliseFolderForQuery(string? folder)
    {
        return (folder ?? @"\Winix").TrimEnd('\\');
    }

    /// <summary>
    /// Recognises the "no tasks / folder doesn't exist" stderr signatures that schtasks
    /// emits with a non-zero exit, distinguishing them from real backend failures (service
    /// stopped, RPC unavailable, access denied). Internal so tests can pin the pattern set.
    /// </summary>
    internal static bool IsBenignSchtasksEmpty(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            // Pre-R4 behaviour treated empty stderr + non-zero exit as benign empty. We
            // keep that for backwards compat with environments where schtasks emits no
            // stderr for "folder not found" (some Server SKUs), but only when stderr is
            // genuinely silent — any present text is treated as a real failure signal.
            return true;
        }

        return stderr.Contains("cannot find the file specified", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("no tasks", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("no scheduled tasks", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public ScheduleResult Remove(string name, string folder)
    {
        string taskPath = BuildTaskPath(folder, name);

        var result = RunSchtasks(new[] { "/Delete", "/TN", taskPath, "/F" });

        if (result.ExitCode != 0)
        {
            return ScheduleResult.Fail($"Failed to remove task '{name}': {result.Stderr}");
        }

        return ScheduleResult.OkWithWarning($"Removed task '{name}'.", result.Stderr);
    }

    /// <inheritdoc />
    public ScheduleResult Enable(string name, string folder)
    {
        string taskPath = BuildTaskPath(folder, name);

        var result = RunSchtasks(new[] { "/Change", "/TN", taskPath, "/ENABLE" });

        if (result.ExitCode != 0)
        {
            return ScheduleResult.Fail($"Failed to enable task '{name}': {result.Stderr}");
        }

        return ScheduleResult.OkWithWarning($"Enabled task '{name}'.", result.Stderr);
    }

    /// <inheritdoc />
    public ScheduleResult Disable(string name, string folder)
    {
        string taskPath = BuildTaskPath(folder, name);

        var result = RunSchtasks(new[] { "/Change", "/TN", taskPath, "/DISABLE" });

        if (result.ExitCode != 0)
        {
            return ScheduleResult.Fail($"Failed to disable task '{name}': {result.Stderr}");
        }

        return ScheduleResult.OkWithWarning($"Disabled task '{name}'.", result.Stderr);
    }

    /// <inheritdoc />
    public ScheduleResult Run(string name, string folder)
    {
        string taskPath = BuildTaskPath(folder, name);

        var result = RunSchtasks(new[] { "/Run", "/TN", taskPath });

        if (result.ExitCode != 0)
        {
            return ScheduleResult.Fail($"Failed to run task '{name}': {result.Stderr}");
        }

        return ScheduleResult.OkWithWarning($"Triggered task '{name}'.", result.Stderr);
    }

    /// <inheritdoc />
    public IReadOnlyList<TaskRunRecord> GetHistory(string name, string folder)
    {
        // schtasks.exe does not have a direct "history" query. Task Scheduler history
        // is stored in the Windows Event Log (Microsoft-Windows-TaskScheduler/Operational).
        // Querying it requires wevtutil or COM -- both are complex for v1.
        // Return empty; the console app will display a note about this limitation.
        return Array.Empty<TaskRunRecord>();
    }

    /// <summary>Builds the full task path from folder and name.</summary>
    private static string BuildTaskPath(string folder, string name)
    {
        string cleanFolder = folder.TrimEnd('\\');
        return cleanFolder + "\\" + name;
    }

    /// <summary>
    /// Builds the /TR string for schtasks.exe. Internal only so tests can pin the
    /// Windows argument-escape contract without spawning schtasks itself.
    /// </summary>
    /// <remarks>
    /// schtasks /TR is one of the few places in the suite where a single command-line
    /// string is genuinely required by the OS API — Task Scheduler stores the trigger
    /// command as one string, not an argument vector. CLAUDE.md mandates ArgumentList
    /// for child processes, but here the child IS schtasks.exe (which uses ArgumentList);
    /// the /TR value is a downstream argument that schtasks itself passes through to
    /// Task Scheduler verbatim. The actual command process is launched later by Task
    /// Scheduler via CreateProcess, so each argument must be escaped per the
    /// Windows CRT command-line tokenisation rules
    /// (https://learn.microsoft.com/en-us/cpp/c-language/parsing-c-command-line-arguments)
    /// — naive backslash-quote escaping breaks on trailing backslashes
    /// (e.g. "C:\Program Files\\" before a closing quote), which is the canonical Windows
    /// quoting foot-gun.
    /// </remarks>
    internal static string BuildTaskRunString(string command, string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return EscapeWindowsArg(command);
        }

        var sb = new StringBuilder();
        sb.Append(EscapeWindowsArg(command));
        foreach (string arg in arguments)
        {
            sb.Append(' ');
            sb.Append(EscapeWindowsArg(arg));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Escapes a single argument per Microsoft CRT command-line tokenisation rules so it
    /// round-trips through CommandLineToArgvW.
    /// </summary>
    /// <remarks>
    /// Rules:
    /// <list type="bullet">
    /// <item>Backslashes are literal unless they immediately precede a double-quote.</item>
    /// <item>2N backslashes followed by a double-quote → N backslashes + the quote terminates the quoted region.</item>
    /// <item>2N+1 backslashes followed by a double-quote → N backslashes + a literal double-quote.</item>
    /// <item>Trailing backslashes before a closing quote must be doubled to remain literal.</item>
    /// </list>
    /// Arguments without whitespace, double-quote, or tab characters are returned unchanged
    /// since they don't need quoting.
    /// </remarks>
    internal static string EscapeWindowsArg(string arg)
    {
        if (arg.Length == 0)
        {
            return "\"\"";
        }

        // No quoting needed if the value has no characters that would alter tokenisation.
        if (arg.IndexOfAny(WindowsArgSpecial) < 0)
        {
            return arg;
        }

        var sb = new StringBuilder(arg.Length + 8);
        sb.Append('"');

        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                // Escape every preceding backslash AND the quote itself: 2N+1 escapes.
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                backslashes = 0;
                continue;
            }

            // Ordinary character: emit accumulated backslashes literally.
            sb.Append('\\', backslashes);
            sb.Append(c);
            backslashes = 0;
        }

        // Trailing backslashes precede the closing quote — double them to keep them literal.
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }

    private static readonly char[] WindowsArgSpecial = { ' ', '\t', '"' };

    /// <summary>
    /// Maximum time to wait for schtasks.exe to exit. A corrupted task store or hung
    /// RPC service can wedge schtasks indefinitely; without this bound the tool would
    /// appear frozen with no diagnostic.
    /// </summary>
    private const int SchtasksTimeoutMs = 30_000;

    /// <summary>
    /// Runs schtasks.exe with the given arguments and returns captured output.
    /// Uses <see cref="ProcessStartInfo.ArgumentList"/> for safe argument passing.
    /// stderr is drained on a separate task to prevent buffer-fill deadlock when
    /// schtasks /Query /V emits hundreds of KB to stdout alongside stderr warnings.
    /// </summary>
    private static ProcessRunResult RunSchtasks(string[] arguments)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null for schtasks.exe.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            // ERROR_FILE_NOT_FOUND / ERROR_PATH_NOT_FOUND.
            return new ProcessRunResult(-1, "", "schtasks.exe not found");
        }
        catch (Win32Exception ex)
        {
            return new ProcessRunResult(-1, "", FormatLaunchFailure(ex.NativeErrorCode, ex.Message));
        }

        using (process)
        {
            process.StandardInput.Close();

            // Concurrent drain: read stderr on a worker task while the main thread reads stdout.
            // Sequential ReadToEnd(stdout) -> ReadToEnd(stderr) deadlocks if either pipe buffer
            // fills while the parent is blocked on the other stream.
            Task<string> stderrTask = Task.Run(() =>
            {
                try { return process.StandardError.ReadToEnd(); }
                catch (System.IO.IOException) { return ""; }
            });

            string stdout;
            try { stdout = process.StandardOutput.ReadToEnd(); }
            catch (System.IO.IOException) { stdout = ""; }

            if (!process.WaitForExit(SchtasksTimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* already exited */ }
                catch (Win32Exception) { /* kill races OS cleanup */ }

                return new ProcessRunResult(-1, "", FormatTimeoutFailure(SchtasksTimeoutMs));
            }

            string stderr;
            try { stderr = stderrTask.Wait(5_000) ? stderrTask.Result : ""; }
            catch (AggregateException) { stderr = ""; }

            return new ProcessRunResult(process.ExitCode, stdout.Trim(), stderr.Trim());
        }
    }

    /// <summary>
    /// Formats the diagnostic for a Win32Exception thrown by <c>Process.Start</c>. Native
    /// error codes 740 (ERROR_ELEVATION_REQUIRED) and 5 (ERROR_ACCESS_DENIED) get an
    /// actionable hint; everything else just surfaces the code + message. Internal so
    /// tests can pin every branch — pre-R4 these messages had zero coverage and a
    /// regression that dropped the elevation hint would have shipped.
    /// </summary>
    internal static string FormatLaunchFailure(int nativeErrorCode, string exceptionMessage)
    {
        string hint = nativeErrorCode switch
        {
            740 => " (try running from an elevated command prompt)",
            5   => " (access denied — try running from an elevated command prompt)",
            _   => "",
        };
        return $"could not launch schtasks.exe (Win32 error {nativeErrorCode.ToString(CultureInfo.InvariantCulture)}): {exceptionMessage}{hint}";
    }

    /// <summary>"schtasks.exe did not respond within Ns" — timeout converted to seconds
    /// for user-readable output. Internal so tests can pin the wording.</summary>
    internal static string FormatTimeoutFailure(int timeoutMs) =>
        $"schtasks.exe did not respond within {timeoutMs / 1000}s";
}

/// <summary>Captured output from a child process.</summary>
internal sealed class ProcessRunResult
{
    /// <summary>The process exit code.</summary>
    public int ExitCode { get; }

    /// <summary>Captured standard output text (trimmed).</summary>
    public string Stdout { get; }

    /// <summary>Captured standard error text (trimmed).</summary>
    public string Stderr { get; }

    /// <summary>Creates a new <see cref="ProcessRunResult"/>.</summary>
    public ProcessRunResult(int exitCode, string stdout, string stderr)
    {
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
    }
}
