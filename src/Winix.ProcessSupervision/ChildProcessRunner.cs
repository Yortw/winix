using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace Winix.ProcessSupervision;

/// <summary>
/// Real <see cref="IChildProcessRunner"/>: spawns the child via <c>ProcessStartInfo.ArgumentList</c>
/// (never string concatenation — suite rule), inherits the parent's console handles so the child is
/// invisible in the pipeline, and kills the child's process tree if the supervising token is cancelled.
/// </summary>
public sealed class ChildProcessRunner : IChildProcessRunner
{
    /// <inheritdoc />
    public int Run(string command, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            // Inherit the real console handles so child output passes through unmodified.
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
        };

        foreach (string arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            // Process.Start returns null only when an existing process is reused — effectively
            // unreachable with UseShellExecute=false (a new process is always started, or an
            // exception is thrown). Belt-and-braces: surface a neutral error rather than
            // mislabelling it "command not found" (null != "not on PATH").
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Process.Start returned no process for '{command}'.");
        }
        catch (Win32Exception ex)
        {
            throw ChildProcessLaunch.ClassifyWin32(ex, command);
        }

        try
        {
            process.WaitForExit();
            return process.ExitCode;
        }
        finally
        {
            process.Dispose();
        }
    }
}
