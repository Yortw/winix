namespace Winix.Peep;

/// <summary>
/// Identifies what caused a command execution.
/// </summary>
public enum TriggerSource
{
    /// <summary>The interval timer fired.</summary>
    Interval,

    /// <summary>A watched file changed.</summary>
    FileChange,

    /// <summary>The user pressed r/Enter to force a re-run.</summary>
    Manual,

    /// <summary>The initial run on startup.</summary>
    Initial
}
