namespace Winix.Wargs;

/// <summary>
/// A concrete command ready to execute — the command name, its arguments, a display
/// string for --verbose/--dry-run, and the input items that produced this invocation.
/// </summary>
/// <param name="Command">The executable name or path.</param>
/// <param name="Arguments">Arguments to pass to the process.</param>
/// <param name="DisplayString">Human-readable, shell-quoted form for display.</param>
/// <param name="SourceItems">The input items that were used to build this invocation.</param>
public sealed record CommandInvocation(
    string Command,
    string[] Arguments,
    string DisplayString,
    string[] SourceItems
);
