namespace Winix.Demux;

/// <summary>
/// A delivery target for routed lines. Implementations must be broken-pipe-safe: a write that
/// fails (e.g. a child closed its stdin) marks the sink dead and counts the record as undelivered
/// rather than throwing — one failing sink must never starve its siblings or crash the router.
/// </summary>
public interface ISink
{
    /// <summary>Human label for the summary (e.g. the pattern or "(default)"/"stdout").</summary>
    string Label { get; }

    /// <summary>Writes one line (a trailing newline is added). Never throws on broken pipe.</summary>
    void Write(string line);

    /// <summary>Flushes and closes; for command sinks, closes stdin, waits, captures the exit code.</summary>
    void Close();

    /// <summary>Lines successfully written.</summary>
    long DeliveredCount { get; }

    /// <summary>Lines that could not be delivered because the sink died mid-run.</summary>
    long UndeliveredCount { get; }

    /// <summary>True once a write failed and the sink stopped accepting records.</summary>
    bool IsDead { get; }

    /// <summary>The child's exit code (Exec sinks only, after Close); null otherwise.</summary>
    int? ChildExitCode { get; }
}
