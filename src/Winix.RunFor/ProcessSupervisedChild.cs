using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Winix.ProcessSupervision;

namespace Winix.RunFor;

/// <summary>Real <see cref="ISupervisedChild"/> over a BCL <see cref="Process"/>.</summary>
internal sealed class ProcessSupervisedChild : ISupervisedChild
{
    private readonly Process _process;

    /// <summary>Wraps <paramref name="process"/> as a supervised child.</summary>
    public ProcessSupervisedChild(Process process) => _process = process;

    /// <inheritdoc />
    public int ExitCode => _process.ExitCode;

    /// <inheritdoc />
    public bool WaitForExit(TimeSpan timeout, CancellationToken cancellationToken)
    {
        int ms = timeout <= TimeSpan.Zero ? 0 : (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);
        try
        {
            // WaitForExitAsync completes when the child exits; the token aborts the wait on Ctrl+C.
            // .Wait(ms) bounds it by the deadline (returns false on timeout). Blocking the calling
            // thread is fine — runfor is a single-purpose console tool driven from Program.Main, not
            // a thread-pool work item (so the pool-starvation class in the memory notes does not apply).
            return _process.WaitForExitAsync(cancellationToken).Wait(ms);
        }
        catch (OperationCanceledException)
        {
            // Defensive belt: Task.Wait(int) surfaces cancellation as AggregateException (handled below),
            // not OperationCanceledException directly — so this arm is unreachable today. Kept so a future
            // refactor to `await ...WaitForExitAsync(ct)` (which DOES throw OCE) stays correct.
            return false; // Ctrl+C — caller checks the token to distinguish from a deadline timeout.
        }
        catch (AggregateException)
        {
            // .Wait wraps a faulted/cancelled task; treat as "did not exit cleanly" — the caller's
            // token check decides interrupt vs timeout.
            return false;
        }
    }

    /// <inheritdoc />
    public TerminationOutcome Terminate(int signal, TimeSpan? killAfter)
        => ProcessTreeTerminator.TerminateAtDeadline(_process, signal, killAfter);

    /// <inheritdoc />
    public void Dispose() => _process.Dispose();
}
