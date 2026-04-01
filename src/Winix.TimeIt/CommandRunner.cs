using System.ComponentModel;
using System.Diagnostics;
using Yort.ShellKit;

namespace Winix.TimeIt;

/// <summary>
/// Spawns a child process and collects timing/memory metrics.
/// </summary>
public static class CommandRunner
{
    /// <summary>
    /// Runs the specified command with arguments, collecting wall time, CPU time, peak memory, and exit code.
    /// Stdin, stdout, and stderr are inherited — the child process interacts with the terminal directly.
    /// </summary>
    /// <param name="command">The executable name or full path to run.</param>
    /// <param name="arguments">Arguments to pass to the process. Each element is quoted correctly per platform.</param>
    /// <returns>A <see cref="TimeItResult"/> with timing and resource metrics from the child process.</returns>
    /// <exception cref="CommandNotFoundException">The command was not found on PATH.</exception>
    /// <exception cref="InvalidOperationException">
    /// The process could not be started for reasons other than missing or non-executable file
    /// (e.g. bad executable format, insufficient memory).
    /// </exception>
    public static TimeItResult Run(string command, string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            // Inherit stdin/stdout/stderr — no redirection
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
        };

        foreach (string arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var baseline = NativeMetrics.CaptureBaseline();
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
            // ERROR_ACCESS_DENIED (5) on Windows, EACCES (13) on Linux/macOS → not executable.
            if (ex.NativeErrorCode == 5 || ex.NativeErrorCode == 13)
            {
                throw new CommandNotExecutableException(command);
            }

            // ERROR_FILE_NOT_FOUND (2), ERROR_PATH_NOT_FOUND (3), ENOENT (2) → not found.
            if (ex.NativeErrorCode == 2 || ex.NativeErrorCode == 3)
            {
                throw new CommandNotFoundException(command);
            }

            // Other errors (ERROR_BAD_EXE_FORMAT, ERROR_NOT_ENOUGH_MEMORY, etc.)
            // — surface the original message rather than misreporting as "not found".
            throw new InvalidOperationException($"failed to start '{command}': {ex.Message}", ex);
        }

        try
        {
            process.WaitForExit();
            stopwatch.Stop();

            var metrics = NativeMetrics.GetMetrics(process, baseline);

            return new TimeItResult(
                WallTime: stopwatch.Elapsed,
                UserCpuTime: metrics.UserCpuTime,
                SystemCpuTime: metrics.SystemCpuTime,
                PeakMemoryBytes: metrics.PeakMemoryBytes,
                ExitCode: process.ExitCode
            );
        }
        finally
        {
            process.Dispose();
        }
    }
}
