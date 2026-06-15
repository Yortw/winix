using System;
using System.Collections.Generic;
using System.Threading;
using Winix.ProcessSupervision;
using Yort.ShellKit;

namespace Winix.RunFor.Tests;

/// <summary>
/// Lifecycle-timing fake. <see cref="ExitsWithinDeadline"/> drives whether <see cref="WaitForExit"/>
/// reports the child finishing in time; the test controls the CancellationToken externally to model
/// Ctrl+C. Records the termination call so the decision tree can be asserted.
/// </summary>
internal sealed class FakeChild : ISupervisedChild
{
    /// <summary>When true, <see cref="WaitForExit"/> reports the child finished in time.</summary>
    public bool ExitsWithinDeadline { get; init; }

    /// <summary>The exit code returned by <see cref="ExitCode"/>.</summary>
    public int FakeExitCode { get; init; }

    /// <summary>The value <see cref="Terminate"/> returns.</summary>
    public TerminationOutcome TerminateResult { get; init; } = TerminationOutcome.ConfirmedDead;

    /// <summary>Number of times <see cref="Terminate"/> was called.</summary>
    public int TerminateCallCount { get; private set; }

    /// <summary>The signal passed to the most recent <see cref="Terminate"/> call; null if never called.</summary>
    public int? LastSignal { get; private set; }

    /// <summary>The killAfter value passed to the most recent <see cref="Terminate"/> call; null if never called.</summary>
    public TimeSpan? LastKillAfter { get; private set; }

    /// <summary>True after <see cref="Dispose"/> has been called.</summary>
    public bool Disposed { get; private set; }

    /// <inheritdoc/>
    public bool WaitForExit(TimeSpan timeout, CancellationToken cancellationToken)
        // Deterministic: no real waiting, and the token is deliberately NOT inspected here. Tests model
        // Ctrl+C by PRE-cancelling the token before Execute (so WaitForExit returns ExitsWithinDeadline
        // and the orchestrator's own token-check decides interrupt-vs-deadline). The mid-wait
        // "token cancels a blocking wait" path belongs to the real ProcessSupervisedChild and is
        // integration-pinned (Task 11), not modelled here.
        => ExitsWithinDeadline;

    /// <inheritdoc/>
    public int ExitCode => FakeExitCode;

    /// <inheritdoc/>
    public TerminationOutcome Terminate(int signal, TimeSpan? killAfter)
    {
        TerminateCallCount++;
        LastSignal = signal;
        LastKillAfter = killAfter;
        return TerminateResult;
    }

    /// <inheritdoc/>
    public void Dispose() => Disposed = true;
}

/// <summary>Returns a fixed <see cref="ISupervisedChild"/> (or one produced by a factory) on each <see cref="Start"/> call.</summary>
internal sealed class FakeChildStarter : IChildStarter
{
    private readonly Func<ISupervisedChild> _factory;

    /// <summary>Initialises with a specific child instance.</summary>
    public FakeChildStarter(ISupervisedChild child) => _factory = () => child;

    /// <summary>Initialises with a factory invoked on each <see cref="Start"/> call.</summary>
    public FakeChildStarter(Func<ISupervisedChild> factory) => _factory = factory;

    /// <inheritdoc/>
    public ISupervisedChild Start(string command, IReadOnlyList<string> arguments) => _factory();
}

/// <summary>Always throws the configured exception from <see cref="Start"/>, modelling a launch failure.</summary>
internal sealed class ThrowingChildStarter : IChildStarter
{
    private readonly Exception _toThrow;

    /// <summary>Initialises with the exception to throw on <see cref="Start"/>.</summary>
    public ThrowingChildStarter(Exception toThrow) => _toThrow = toThrow;

    /// <inheritdoc/>
    public ISupervisedChild Start(string command, IReadOnlyList<string> arguments) => throw _toThrow;
}
