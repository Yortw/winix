namespace Winix.Wargs;

/// <summary>
/// How job output is buffered and printed.
/// </summary>
public enum BufferStrategy
{
    /// <summary>Capture per-job output. Print atomically on job completion. Completion order.</summary>
    JobBuffered,

    /// <summary>Children inherit stdio. Output interleaves naturally.</summary>
    LineBuffered,

    /// <summary>Capture per-job output. Print in input order, holding back completed jobs until their turn.</summary>
    KeepOrder
}
