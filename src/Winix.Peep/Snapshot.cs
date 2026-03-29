namespace Winix.Peep;

/// <summary>
/// Immutable snapshot of a single command execution, stored in <see cref="SnapshotHistory"/>
/// for time machine browsing.
/// </summary>
/// <param name="Result">The command execution result.</param>
/// <param name="Timestamp">Wall clock time when the run started.</param>
/// <param name="RunNumber">1-based sequential run number within the session.</param>
/// <param name="LinesAdded">Lines added compared to the previous snapshot (0 for the first snapshot's removals).</param>
/// <param name="LinesRemoved">Lines removed compared to the previous snapshot (0 for the first snapshot).</param>
public sealed record Snapshot(
    PeepResult Result,
    DateTime Timestamp,
    int RunNumber,
    int LinesAdded,
    int LinesRemoved
);
