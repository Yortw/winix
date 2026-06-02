#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Yort.ShellKit;

namespace Winix.WhoHolds;

/// <summary>
/// Finds processes holding a file lock or port binding by delegating to the system
/// <c>lsof</c> utility. Intended for Linux and macOS only; returns empty success
/// (or <c>false</c> from <see cref="IsAvailable"/>) when <c>lsof</c> is not on PATH.
/// </summary>
public static class LsofFinder
{
    // Hard upper bound on how long we wait for lsof to exit. Lsof usually returns within
    // a few hundred milliseconds; a 2-second budget tolerates loaded systems and slow
    // disks. SFH I4 (round-1 review 2026-05-08): a misbehaving lsof binary on PATH would
    // otherwise hang the whoholds CLI forever via the RunProcess WaitForExit() call.
    private const int LsofTimeoutMs = 2000;

    /// <summary>
    /// Outcome of a single lsof invocation — captures stdout, stderr, and any failure
    /// signals so <see cref="InterpretLsofRun"/> can map distinct failure modes
    /// (start failure, timeout, mid-read exception) to <see cref="FindResult.Failed"/>
    /// without losing diagnostic detail.
    /// </summary>
    /// <param name="ExitCode">
    /// Process exit code, or <c>-1</c> sentinel when the process did not run to
    /// completion (start failure or timeout). Use <see cref="TimedOut"/> /
    /// <see cref="StartError"/> to disambiguate.
    /// </param>
    /// <param name="Output">Captured stdout. Empty when <see cref="TimedOut"/> or process never started.</param>
    /// <param name="Error">Captured stderr. Empty when timed out or process never started.</param>
    /// <param name="TimedOut"><see langword="true"/> when WaitForExit hit the configured timeout and the process was force-killed.</param>
    /// <param name="StartError">Non-null when <see cref="Process.Start(ProcessStartInfo)"/> threw or returned <see langword="null"/>.</param>
    /// <param name="ReadError">
    /// Non-null when stdout or stderr capture threw an unexpected exception
    /// (e.g. <see cref="ObjectDisposedException"/> if the streams were torn down racily).
    /// SFH F3 fresh-eyes finding 2026-05-08: pre-fix this was an empty catch and silently
    /// produced an empty-stdout success-empty result, re-introducing the SFH defect class.
    /// </param>
    internal readonly record struct LsofRun(
        int ExitCode,
        string Output,
        string Error,
        bool TimedOut,
        string? StartError,
        string? ReadError);

    /// <summary>
    /// Test seam for the underlying process runner. Production code uses
    /// <see cref="DefaultRunProcess"/>; tests substitute a fake to exercise timeout,
    /// start-failure, stderr-on-nonzero, and stream-read-exception paths without spawning
    /// an actual lsof binary. Round-1 fresh-eyes 2026-05-08 test-analyzer C1-C3.
    /// </summary>
    /// <remarks>
    /// <strong>Process-wide static.</strong> xUnit runs different test classes in parallel
    /// by default; any test class that touches <see cref="FindFile"/>, <see cref="FindPort"/>,
    /// or <see cref="IsAvailable"/> MUST join the <c>"LsofFinder.ProcessRunner"</c> xUnit
    /// collection (e.g. <c>[Collection("LsofFinder.ProcessRunner")]</c>) so the seam
    /// substitution is serialised. Round-2 fresh-eyes 2026-05-08 — three reviewers
    /// (silent-failure-hunter R2-2, code-reviewer W4, test-analyzer I2) converged on this
    /// risk; <c>LsofFinderTests</c> is the only current consumer and joins the collection.
    /// </remarks>
    internal static Func<string, string[], LsofRun> ProcessRunner { get; set; } = DefaultRunProcess;

    /// <summary>
    /// Returns processes holding an open handle on <paramref name="filePath"/>.
    /// Uses <c>lsof &lt;filePath&gt;</c>.
    /// Returns a successful empty result when lsof reports no holders, and
    /// <see cref="FindResult.Failed"/> when the lsof process could not be started, timed
    /// out, or exited with a non-zero status accompanied by stderr output indicating an
    /// error (lsof exit 1 with no output is "no matches" — treated as success-empty).
    /// </summary>
    /// <param name="filePath">Absolute path to the file to query.</param>
    public static FindResult FindFile(string filePath)
    {
        var run = ProcessRunner("lsof", new[] { filePath });
        return InterpretLsofRun(run, filePath);
    }

    /// <summary>
    /// Returns processes bound to <paramref name="port"/>.
    /// Uses <c>lsof -i :&lt;port&gt;</c>.
    /// Returns a successful empty result when lsof reports no holders, and
    /// <see cref="FindResult.Failed"/> when the lsof process could not be started, timed
    /// out, or exited with a non-zero status accompanied by stderr output.
    /// </summary>
    /// <param name="port">TCP/UDP port number to query.</param>
    public static FindResult FindPort(int port)
    {
        string portArg = ":" + port.ToString(CultureInfo.InvariantCulture);
        var run = ProcessRunner("lsof", new[] { "-i", portArg });
        return InterpretLsofRun(run, "TCP :" + port.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Returns <c>true</c> if <c>lsof</c> is available on the current PATH and responds to
    /// <c>-v</c> within the internal timeout. Wraps the probe in the same timeout as
    /// <see cref="FindFile"/> and <see cref="FindPort"/> — a misbehaving lsof binary on
    /// PATH would otherwise hang here indefinitely (SFH I4 round-1 2026-05-08).
    /// </summary>
    public static bool IsAvailable()
    {
        var run = ProcessRunner("lsof", new[] { "-v" });
        // exitCode == -1 means the process could not start (lsof not on PATH).
        // TimedOut means the binary is wedged — treat as unavailable rather than hang.
        return run.StartError is null && !run.TimedOut;
    }

    /// <summary>
    /// Maps a <see cref="LsofRun"/> outcome to a <see cref="FindResult"/>. Centralises
    /// the "exit 1 with no output = no matches = success-empty" rule so file and port
    /// queries behave identically.
    /// </summary>
    internal static FindResult InterpretLsofRun(LsofRun run, string resource)
    {
        if (run.TimedOut)
        {
            return FindResult.Failed(
                "lsof timed out after " + LsofTimeoutMs.ToString(CultureInfo.InvariantCulture) + "ms.");
        }
        if (run.StartError is not null)
        {
            // Process couldn't be started after IsAvailable() returned true — race or
            // PATH change mid-run. Surface as a real failure.
            return FindResult.Failed("lsof failed to start: " + run.StartError + ".");
        }
        if (run.ReadError is not null)
        {
            // Stream capture threw mid-read (e.g. ObjectDisposedException on a racing
            // teardown). SFH F3 fresh-eyes: pre-fix, the bare catch silently produced
            // an empty-stdout success-empty here. Surface explicitly so the failure
            // doesn't masquerade as "no holders found".
            return FindResult.Failed("lsof output capture failed: " + run.ReadError + ".");
        }

        if (string.IsNullOrWhiteSpace(run.Output))
        {
            // lsof exits 1 when it finds nothing; treat empty stdout as success-empty even
            // on non-zero exit. Real errors (bad arg, permission denied) usually print to
            // stderr; surface those as a failure rather than reporting "no holders".
            if (run.ExitCode != 0 && !string.IsNullOrWhiteSpace(run.Error))
            {
                return FindResult.Failed("lsof: " + run.Error.Trim());
            }
            return FindResult.Empty;
        }

        return FindResult.Success(ParseLsofOutput(run.Output, resource));
    }

    /// <summary>
    /// Parses raw lsof output into a deduplicated list of <see cref="LockInfo"/> records.
    /// Internal so the parser can be fixture-tested on Windows without spawning a real
    /// lsof binary — the actual lsof invocation lives in <see cref="DefaultRunProcess"/>,
    /// which is gated by platform availability and not directly testable cross-platform.
    /// </summary>
    /// <param name="output">Raw stdout from an lsof invocation.</param>
    /// <param name="resource">Resource label to attach to each result.</param>
    internal static List<LockInfo> ParseLsofOutput(string output, string resource)
    {
        var results = new List<LockInfo>();
        var seenPids = new HashSet<int>();

        string[] lines = output.Split('\n');
        bool isFirstLine = true;

        foreach (string line in lines)
        {
            // Skip the header line produced by lsof.
            if (isFirstLine)
            {
                isFirstLine = false;
                continue;
            }

            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            // lsof columns: COMMAND PID USER FD TYPE DEVICE SIZE/OFF NODE NAME
            string[] cols = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length < 2)
            {
                continue;
            }

            // Round-2 fresh-eyes 2026-05-08 test-analyzer I1: anchor on the first numeric
            // column rather than assuming cols[1] is the PID. macOS lsof can emit
            // multi-word command names (e.g. "Google Chrome 1234 troy 19u IPv4 ..."),
            // which would push the PID into cols[2] and previously cause int.TryParse to
            // fail on cols[1] = "Chrome", silently dropping the entire row. The fix
            // tolerates any number of leading command-name tokens up to the first
            // numeric column.
            int pid = 0;
            int pidColIndex = -1;
            for (int i = 1; i < cols.Length; i++)
            {
                if (int.TryParse(cols[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out pid))
                {
                    pidColIndex = i;
                    break;
                }
            }

            if (pidColIndex < 0)
            {
                continue;
            }

            // Deduplicate: a process with multiple open file descriptors on the same
            // resource produces multiple lsof rows; we only want one entry per PID.
            if (!seenPids.Add(pid))
            {
                continue;
            }

            string command = pidColIndex == 1
                ? cols[0]
                : string.Join(' ', cols, 0, pidColIndex);
            results.Add(new LockInfo(pid, command, resource));
        }

        return results;
    }

    /// <summary>
    /// Production process runner. Spawns <paramref name="command"/> under the configured
    /// timeout, captures both stdout and stderr asynchronously, and reports timeouts /
    /// start failures / stream-read exceptions via the structured <see cref="LsofRun"/>
    /// fields rather than empty catches.
    /// </summary>
    private static LsofRun DefaultRunProcess(string command, string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (string arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception ex)
        {
            return new LsofRun(-1, string.Empty, string.Empty, TimedOut: false, StartError: ex.Message, ReadError: null);
        }
        catch (System.IO.FileNotFoundException ex)
        {
            return new LsofRun(-1, string.Empty, string.Empty, TimedOut: false, StartError: SafeError.Describe(ex), ReadError: null);
        }
        catch (InvalidOperationException ex)
        {
            return new LsofRun(-1, string.Empty, string.Empty, TimedOut: false, StartError: SafeError.Describe(ex), ReadError: null);
        }

        if (process is null)
        {
            // Process.Start(ProcessStartInfo) is documented as nullable when no new process
            // was started because an existing one was reused — surface that as a start
            // failure rather than NRE under the `!` suppression.
            return new LsofRun(-1, string.Empty, string.Empty, TimedOut: false, StartError: "Process.Start returned null.", ReadError: null);
        }

        using (process)
        {
            // Begin async reads BEFORE WaitForExit to avoid a deadlock if either pipe
            // fills its buffer (~64 KB on Windows). lsof's normal output is well under
            // that, but a wedged or chatty binary could still block.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(LsofTimeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process exited between WaitForExit-returns-false and Kill — race.
                    // No remediation needed; we still report the timeout.
                }
                catch (Win32Exception)
                {
                    // OS refused the kill (rare; e.g. permission denied on a kernel-managed
                    // process). Best-effort kill; surface as TimedOut so the caller routes
                    // to FindResult.Failed.
                }

                // CR W3 fresh-eyes: give the read tasks a brief window to observe the
                // kill before we dispose the streams. Without this, ObjectDisposedException
                // is raised on disposal and we lose any partial captures.
                try
                {
                    System.Threading.Tasks.Task.WaitAll(new[] { stdoutTask, stderrTask }, 100);
                }
                catch (AggregateException)
                {
                    // Stream-read exceptions on the timeout path are expected; the killed
                    // process tears down the streams. No structured capture needed — the
                    // user-visible result is "timed out", which is what we report.
                }

                return new LsofRun(-1, string.Empty, string.Empty, TimedOut: true, StartError: null, ReadError: null);
            }

            // Process has exited; the read tasks should complete promptly. Wait with a
            // bounded budget so a stuck stream cannot extend our timeout indefinitely.
            // Capture exceptions explicitly per SFH F3: if a read fails we surface that
            // via ReadError instead of silently substituting empty strings.
            string output = string.Empty;
            string error = string.Empty;
            string? readError = null;
            bool allCompleted;
            try
            {
                allCompleted = System.Threading.Tasks.Task.WaitAll(new[] { stdoutTask, stderrTask }, LsofTimeoutMs);
            }
            catch (AggregateException ex)
            {
                allCompleted = stdoutTask.IsCompleted && stderrTask.IsCompleted;
                readError = ex.GetType().Name + ": " + SafeError.Describe(ex.InnerException);
            }

            // Round-2 fresh-eyes 2026-05-08 silent-failure-hunter R2-1: defensive guard for
            // the edge case where the lsof child forks a subprocess inheriting its stdout/
            // stderr file descriptors. WaitForExit returns when the lsof main exits, but the
            // ReadToEndAsync tasks remain running because the inherited descriptors keep the
            // pipes open. WaitAll then returns false (no exception), tasks stay in
            // WaitingForActivation, and the previous code would substitute empty captures —
            // re-introducing the SFH defect class. Surface as ReadError so InterpretLsofRun
            // routes to FindResult.Failed instead of producing a silent success-empty.
            if (!allCompleted && readError is null)
            {
                readError = "lsof read did not complete within "
                    + LsofTimeoutMs.ToString(CultureInfo.InvariantCulture)
                    + "ms (stream may be held open by a forked subprocess).";
            }

            if (stdoutTask.IsCompletedSuccessfully)
            {
                output = stdoutTask.Result;
            }
            else if (readError is null && stdoutTask.IsFaulted)
            {
                readError = "stdout read faulted: " + SafeError.Describe(stdoutTask.Exception?.GetBaseException());
            }

            if (stderrTask.IsCompletedSuccessfully)
            {
                error = stderrTask.Result;
            }
            else if (readError is null && stderrTask.IsFaulted)
            {
                readError = "stderr read faulted: " + SafeError.Describe(stderrTask.Exception?.GetBaseException());
            }

            return new LsofRun(process.ExitCode, output, error, TimedOut: false, StartError: null, ReadError: readError);
        }
    }
}
