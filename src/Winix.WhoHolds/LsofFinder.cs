#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

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
        var run = RunProcess("lsof", new[] { filePath });
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
        var run = RunProcess("lsof", new[] { "-i", portArg });
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
        var run = RunProcess("lsof", new[] { "-v" });
        // exitCode == -1 means the process could not start (lsof not on PATH).
        // TimedOut means the binary is wedged — treat as unavailable rather than hang.
        return run.ExitCode != -1 && !run.TimedOut;
    }

    /// <summary>
    /// Maps a <see cref="LsofRun"/> outcome to a <see cref="FindResult"/>. Centralises
    /// the "exit 1 with no output = no matches = success-empty" rule so file and port
    /// queries behave identically.
    /// </summary>
    private static FindResult InterpretLsofRun(LsofRun run, string resource)
    {
        if (run.TimedOut)
        {
            return FindResult.Failed(
                "lsof timed out after " + LsofTimeoutMs.ToString(CultureInfo.InvariantCulture) + "ms.");
        }
        if (run.ExitCode == -1)
        {
            // Process couldn't be started after IsAvailable() returned true — race or
            // PATH change mid-run. Surface as a real failure.
            string detail = string.IsNullOrWhiteSpace(run.StartError) ? "Process.Start failed" : run.StartError;
            return FindResult.Failed("lsof failed to start: " + detail + ".");
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
    /// </summary>
    /// <param name="output">Raw stdout from an lsof invocation.</param>
    /// <param name="resource">Resource label to attach to each result.</param>
    private static List<LockInfo> ParseLsofOutput(string output, string resource)
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

            if (!int.TryParse(cols[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid))
            {
                continue;
            }

            // Deduplicate: a process with multiple open file descriptors on the same
            // resource produces multiple lsof rows; we only want one entry per PID.
            if (!seenPids.Add(pid))
            {
                continue;
            }

            string command = cols[0];
            results.Add(new LockInfo(pid, command, resource));
        }

        return results;
    }

    /// <summary>
    /// Outcome of a single lsof invocation — captures both stdout and stderr so the
    /// caller can distinguish "no matches" from "lsof itself errored" without losing
    /// diagnostic detail.
    /// </summary>
    private readonly record struct LsofRun(int ExitCode, string Output, string Error, bool TimedOut, string? StartError);

    /// <summary>
    /// Runs an external process under a fixed timeout, capturing stdout and stderr.
    /// </summary>
    /// <param name="command">Executable name or path.</param>
    /// <param name="arguments">Arguments passed via <see cref="ProcessStartInfo.ArgumentList"/>.</param>
    /// <returns>
    /// An <see cref="LsofRun"/> with <c>ExitCode == -1</c> when the process could not be
    /// started (capturing the exception text into <c>StartError</c>), or
    /// <c>TimedOut == true</c> when the process did not exit within
    /// <see cref="LsofTimeoutMs"/>.
    /// </returns>
    private static LsofRun RunProcess(string command, string[] arguments)
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

        try
        {
            using var process = Process.Start(startInfo)!;

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
                catch
                {
                    // Best-effort kill — if the kill itself fails the process is wedged
                    // beyond our recovery; surfaced as TimedOut to the caller.
                }
                return new LsofRun(-1, string.Empty, string.Empty, TimedOut: true, StartError: null);
            }

            // Process has exited; the read tasks should complete promptly. Use a small
            // grace-period wait so we don't return partial captures if the runtime hasn't
            // yet pumped the final buffer.
            try
            {
                System.Threading.Tasks.Task.WaitAll(new[] { stdoutTask, stderrTask }, LsofTimeoutMs);
            }
            catch
            {
                // Stream read exceptions are unusual after WaitForExit; surface what we got.
            }

            string output = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
            string error  = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
            return new LsofRun(process.ExitCode, output, error, TimedOut: false, StartError: null);
        }
        catch (Exception ex)
        {
            return new LsofRun(-1, string.Empty, string.Empty, TimedOut: false, StartError: ex.Message);
        }
    }
}
