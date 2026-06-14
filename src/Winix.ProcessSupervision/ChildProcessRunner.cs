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
            // ORDERING INVARIANT (load-bearing — do not move): killReg is a `using` declared INSIDE
            // this try, so on any exit path its Dispose runs as the try-scope unwinds — BEFORE the
            // finally's process.Dispose(). CancellationTokenRegistration.Dispose() blocks until any
            // in-flight callback completes, so the kill callback can never run against an
            // already-disposed Process. Disposing the process inside the try, or hoisting killReg
            // out, would reintroduce that race. Mirrors retry's disposal-order fix (Cli.cs).
            //
            // Register a kill-the-tree callback on cancel. The synchronous WaitForExit() below
            // returns once the kill terminates the child. The careful catch set mirrors retry/wargs:
            // a CancellationToken callback that throws makes the cancelling Cancel() call throw,
            // which would escape the supervising tool — so the kill is strictly best-effort.
            using CancellationTokenRegistration killReg = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                // ObjectDisposedException FIRST — it derives from InvalidOperationException.
                catch (ObjectDisposedException) { /* disposed before kill fired */ }
                catch (InvalidOperationException) { /* already exited — benign */ }
                catch (Win32Exception) { /* access denied / signal-delivery error — best-effort */ }
                catch (NotSupportedException) { /* platform cannot kill the tree — best-effort */ }
            });

            process.WaitForExit();
            return process.ExitCode;
        }
        finally
        {
            process.Dispose();
        }
    }
}
