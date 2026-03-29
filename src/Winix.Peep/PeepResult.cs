namespace Winix.Peep;

/// <summary>
/// Immutable result of a single command execution within a peep session.
/// </summary>
/// <param name="Output">Merged stdout+stderr text from the child process, with ANSI sequences preserved.</param>
/// <param name="ExitCode">Exit code of the child process.</param>
/// <param name="Duration">Wall-clock duration of the child process execution.</param>
/// <param name="Trigger">What triggered this execution.</param>
public sealed record PeepResult(
    string Output,
    int ExitCode,
    TimeSpan Duration,
    TriggerSource Trigger
);
