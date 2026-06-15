using System;
using Winix.ProcessSupervision;

namespace Winix.RunFor;

/// <summary>
/// A started child process under <c>runfor</c>'s deadline supervision. The seam through which tests
/// model child lifecycle TIMING (exits-in-time vs runs-past-deadline) without a real process — the
/// CLIo BUG-010 lesson: the fake must reproduce timing, not just a final state.
/// </summary>
public interface ISupervisedChild : IDisposable
{
    /// <summary>
    /// Blocks until the child exits, the <paramref name="timeout"/> elapses, or
    /// <paramref name="cancellationToken"/> is signalled.
    /// </summary>
    /// <returns><c>true</c> iff the child exited within the timeout and was not cancelled;
    /// <c>false</c> on timeout OR cancellation (the caller inspects the token to tell them apart).</returns>
    bool WaitForExit(TimeSpan timeout, System.Threading.CancellationToken cancellationToken);

    /// <summary>The child's exit code. Only valid after <see cref="WaitForExit"/> returned <c>true</c>.</summary>
    int ExitCode { get; }

    /// <summary>
    /// Terminates the child at the deadline/interrupt per the platform × mode matrix
    /// (<see cref="ProcessTreeTerminator.TerminateAtDeadline"/>).
    /// </summary>
    /// <param name="signal">The Unix signal number to send. Ignored on Windows (no signal model —
    /// the tree is killed immediately); see <see cref="ProcessTreeTerminator.TerminateAtDeadline"/>.</param>
    /// <param name="killAfter">Grace before the SIGKILL backstop; <c>null</c> ⇒ signal-only default.</param>
    TerminationOutcome Terminate(int signal, TimeSpan? killAfter);
}
