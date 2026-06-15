namespace Winix.ProcessSupervision;

/// <summary>
/// The result of a deadline-driven termination attempt (<see cref="ProcessTreeTerminator.TerminateAtDeadline"/>).
/// Distinguishes "the child is gone" from "we tried to kill it and couldn't" from "we only sent a
/// signal with no kill guarantee" — the last is the coreutils-faithful default and is NOT an error.
/// </summary>
public enum TerminationOutcome
{
    /// <summary>The child (and its tree, where a kill was issued) is confirmed exited.</summary>
    ConfirmedDead,

    /// <summary>A kill was attempted but the process may still be alive (e.g. EPERM — a child owned
    /// by another user). The caller should warn the user the child may still be running.</summary>
    KillFailed,

    /// <summary>Coreutils-default signal-only mode: the signal was sent to the direct child with NO
    /// SIGKILL backstop. The child may legitimately still be running (it ignored the signal) — this
    /// is <c>timeout</c> semantics, not a failure. The caller does NOT warn.</summary>
    SignalSentNoGuarantee,
}
