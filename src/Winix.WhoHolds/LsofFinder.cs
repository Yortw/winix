#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Winix.WhoHolds;

/// <summary>
/// Finds processes holding a file lock or port binding by delegating to the system
/// <c>lsof</c> utility. Intended for Linux and macOS only; returns empty results
/// (or <c>false</c> from <see cref="IsAvailable"/>) when <c>lsof</c> is not on PATH.
/// </summary>
public static class LsofFinder
{
    /// <summary>
    /// Returns processes holding an open handle on <paramref name="filePath"/>.
    /// Uses <c>lsof &lt;filePath&gt;</c>.
    /// Returns an empty list if <c>lsof</c> is unavailable, the file is not locked,
    /// or any error occurs.
    /// </summary>
    /// <param name="filePath">Absolute path to the file to query.</param>
    public static List<LockInfo> FindFile(string filePath)
    {
        var (exitCode, output) = RunProcess("lsof", new[] { filePath });
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            return new List<LockInfo>();
        }

        return ParseLsofOutput(output, filePath);
    }

    /// <summary>
    /// Returns processes bound to <paramref name="port"/>.
    /// Uses <c>lsof -i :&lt;port&gt;</c>.
    /// Returns an empty list if <c>lsof</c> is unavailable or any error occurs.
    /// </summary>
    /// <param name="port">TCP/UDP port number to query.</param>
    public static List<LockInfo> FindPort(int port)
    {
        string portArg = $":{port}";
        var (exitCode, output) = RunProcess("lsof", new[] { "-i", portArg });

        // lsof exits 1 when no matches — treat the same as empty output.
        if (string.IsNullOrWhiteSpace(output))
        {
            return new List<LockInfo>();
        }

        return ParseLsofOutput(output, $"TCP :{port}");
    }

    /// <summary>
    /// Returns <c>true</c> if <c>lsof</c> is available on the current PATH.
    /// </summary>
    public static bool IsAvailable()
    {
        var (exitCode, _) = RunProcess("lsof", new[] { "-v" });
        // lsof -v exits 0 on most platforms; exitCode == -1 means the process could not start.
        return exitCode != -1;
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

            if (!int.TryParse(cols[1], out int pid))
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
    /// Runs an external process and returns its exit code and captured stdout.
    /// Returns <c>(-1, "")</c> if the process cannot be started.
    /// </summary>
    /// <param name="command">Executable name or path.</param>
    /// <param name="arguments">Arguments passed via <see cref="ProcessStartInfo.ArgumentList"/>.</param>
    private static (int ExitCode, string Output) RunProcess(string command, string[] arguments)
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
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
        catch
        {
            return (-1, "");
        }
    }
}
