namespace Winix.RunFor;

/// <summary>How a <c>runfor</c> invocation ended.</summary>
public enum RunForOutcome
{
    /// <summary>The child exited on its own before the deadline; its code is forwarded.</summary>
    Completed,

    /// <summary>The deadline fired; the child was terminated and runfor returns 124.</summary>
    TimedOut,

    /// <summary>Ctrl+C: the child tree was terminated and runfor returns 130.</summary>
    Interrupted,

    /// <summary>The child never started (not executable / not found); runfor returns 126/127.</summary>
    LaunchFailed,
}
