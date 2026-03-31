namespace Winix.Wargs;

/// <summary>
/// Configuration for <see cref="JobRunner"/>.
/// </summary>
/// <param name="Parallelism">Max concurrent jobs. 1 = sequential. 0 = unlimited.</param>
/// <param name="Strategy">How job output is buffered and printed.</param>
/// <param name="FailFast">Stop spawning after first child failure.</param>
/// <param name="DryRun">Print commands without executing.</param>
/// <param name="Verbose">Print each command to stderr before running.</param>
/// <param name="Confirm">Prompt before each job.</param>
/// <param name="ConfirmPrompt">
/// Delegate that displays a command and returns true to proceed, false to skip.
/// Null uses the default console prompt (reads from /dev/tty or CON).
/// Injected for testability.
/// </param>
public sealed record JobRunnerOptions(
    int Parallelism = 1,
    BufferStrategy Strategy = BufferStrategy.JobBuffered,
    bool FailFast = false,
    bool DryRun = false,
    bool Verbose = false,
    bool Confirm = false,
    Func<string, bool>? ConfirmPrompt = null
);
