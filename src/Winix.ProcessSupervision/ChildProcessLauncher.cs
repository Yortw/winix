using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace Winix.ProcessSupervision;

/// <summary>
/// Spawns a child process the way the whole supervision family requires: arguments via
/// <c>ProcessStartInfo.ArgumentList</c> (never string concatenation — suite rule), inheriting the
/// parent's console handles so the child is invisible in the pipeline, and classifying launch
/// failures into the suite's typed exceptions. Shared by <see cref="ChildProcessRunner"/> (immediate
/// kill-on-cancel consumers: lock/soak/attempt) and runfor's deadline orchestration.
/// </summary>
public static class ChildProcessLauncher
{
    /// <summary>
    /// Starts <paramref name="command"/> with <paramref name="arguments"/> and returns the running
    /// <see cref="Process"/>. The caller owns disposal and lifecycle (wait/kill).
    /// </summary>
    /// <param name="command">The executable name or full path to launch.</param>
    /// <param name="arguments">Arguments to pass via <c>ProcessStartInfo.ArgumentList</c>.</param>
    /// <returns>The started <see cref="Process"/>; the caller is responsible for waiting and disposing.</returns>
    /// <exception cref="CommandNotFoundException">The command was not found on PATH (errno 2/3).</exception>
    /// <exception cref="CommandNotExecutableException">The command exists but could not be executed
    /// (errno 5/13 or any other launch error such as ERROR_BAD_EXE_FORMAT 193).</exception>
    public static Process Launch(string command, IReadOnlyList<string> arguments)
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

        try
        {
            // Process.Start returns null only when an existing process is reused — effectively
            // unreachable with UseShellExecute=false. Surface a neutral error rather than
            // mislabelling it "command not found".
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Process.Start returned no process for '{command}'.");
        }
        catch (Win32Exception ex)
        {
            throw ChildProcessLaunch.ClassifyWin32(ex, command);
        }
    }
}
