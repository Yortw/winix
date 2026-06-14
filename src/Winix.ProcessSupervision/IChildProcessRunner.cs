using System.Collections.Generic;
using System.Threading;
using Yort.ShellKit;

namespace Winix.ProcessSupervision;

/// <summary>
/// The process-supervision family's injectable child-runner seam. Tools depend on this interface
/// so tests can substitute a fake that models child lifecycle timing (exit-after-delay, killed-mid-run)
/// rather than driving a real process.
/// </summary>
public interface IChildProcessRunner
{
    /// <summary>
    /// Spawns <paramref name="command"/> with <paramref name="arguments"/>, letting the child inherit
    /// the parent's stdin/stdout/stderr, waits for it to exit, and returns its exit code.
    /// </summary>
    /// <param name="command">The executable to run (resolved against PATH by the OS).</param>
    /// <param name="arguments">Arguments passed verbatim via <c>ProcessStartInfo.ArgumentList</c>.</param>
    /// <param name="cancellationToken">
    /// When signalled before the child exits, the child's entire process tree is killed and the
    /// (killed) child's exit code is returned. The caller decides how to map that — e.g. <c>runfor</c>
    /// returns 124 when it cancelled for its own deadline.
    /// </param>
    /// <returns>
    /// The child process exit code. NOTE: the return value does NOT encode whether the run was
    /// cancelled — a tree-killed child's code is platform-specific and (on Windows) not reliably
    /// distinguishable from a legitimate non-zero exit. A caller that needs to apply a policy on
    /// cancellation (e.g. map to 124/130) MUST observe its own <paramref name="cancellationToken"/>
    /// to decide; it cannot infer cancellation from this return value.
    /// </returns>
    /// <exception cref="CommandNotFoundException">The command was not found on PATH.</exception>
    /// <exception cref="CommandNotExecutableException">The command exists but could not be executed.</exception>
    int Run(string command, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
