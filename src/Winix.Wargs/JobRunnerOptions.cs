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
/// <param name="ShellFallback">
/// When true (default), if a command is not found as a standalone executable, retry via
/// the platform shell (<c>cmd /c</c> on Windows, <c>sh -c</c> on Unix). This allows
/// shell builtins like <c>echo</c>, <c>del</c>, <c>type</c> to work transparently.
/// Disable with <c>--no-shell-fallback</c> for strict direct-exec-only behaviour.
/// </param>
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
    bool ShellFallback = true,
    Func<string, bool>? ConfirmPrompt = null
);
