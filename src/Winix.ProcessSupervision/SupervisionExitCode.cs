namespace Winix.ProcessSupervision;

/// <summary>
/// Exit-code constants specific to the process-supervision tool family. The usage/not-executable/
/// not-found codes (125/126/127) live in <see cref="Yort.ShellKit.ExitCode"/> and are reused
/// directly; this class adds only the two codes the family introduces.
/// </summary>
public static class SupervisionExitCode
{
    /// <summary>
    /// Deadline exceeded — <c>runfor</c> killed the child because its time budget ran out.
    /// Matches coreutils <c>timeout</c> (124) so ported scripts behave identically.
    /// </summary>
    public const int Timeout = 124;

    /// <summary>
    /// Interrupted by SIGINT (Ctrl+C), forwarded to the child tree. 128 + signal number 2,
    /// the conventional shell exit code for a process killed by SIGINT.
    /// </summary>
    public const int Interrupted = 130;
}
