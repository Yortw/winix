using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Winix.ProcessSupervision;

/// <summary>
/// Real <see cref="IChildProcessRunner"/>: spawns the child via <see cref="ChildProcessLauncher"/>
/// (which uses <c>ProcessStartInfo.ArgumentList</c>, never string concatenation — suite rule),
/// inherits the parent's console handles so the child is invisible in the pipeline, and kills the
/// child's process tree if the supervising token is cancelled.
/// </summary>
public sealed class ChildProcessRunner : IChildProcessRunner
{
    /// <inheritdoc />
    public int Run(string command, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Process process = ChildProcessLauncher.Launch(command, arguments);

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
            using CancellationTokenRegistration killReg =
                cancellationToken.Register(() => ProcessTreeTerminator.KillTree(process));

            process.WaitForExit();
            return process.ExitCode;
        }
        finally
        {
            process.Dispose();
        }
    }
}
