#nullable enable

using System.ComponentModel;
using System.Diagnostics;

namespace Winix.TimeIt;

/// <summary>
/// Thrown when the specified command cannot be found.
/// </summary>
public sealed class CommandNotFoundException : Exception
{
    /// <inheritdoc />
    public CommandNotFoundException(string command)
        : base($"command not found: {command}")
    {
        Command = command;
    }

    /// <summary>
    /// The command name that could not be found.
    /// </summary>
    public string Command { get; }
}

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

        var stopwatch = Stopwatch.StartNew();
        Process process;

        try
        {
            process = Process.Start(startInfo)
                ?? throw new CommandNotFoundException(command);
        }
        catch (Win32Exception)
        {
            // Win32Exception is thrown on all .NET platforms (not just Windows) when the
            // executable cannot be found — .NET maps POSIX ENOENT to Win32Exception on Linux/macOS too.
            throw new CommandNotFoundException(command);
        }

        process.WaitForExit();
        stopwatch.Stop();

        TimeSpan cpuTime;
        long peakMemory;

        try
        {
            cpuTime = process.TotalProcessorTime;
        }
        catch (InvalidOperationException)
        {
            // Process exited and metrics were cleared before we could read them.
            cpuTime = TimeSpan.Zero;
        }

        try
        {
            peakMemory = process.PeakWorkingSet64;
        }
        catch (InvalidOperationException)
        {
            // Same race: process gone before peak memory was readable.
            peakMemory = 0;
        }

        return new TimeItResult(
            WallTime: stopwatch.Elapsed,
            CpuTime: cpuTime,
            PeakMemoryBytes: peakMemory,
            ExitCode: process.ExitCode
        );
    }
}
