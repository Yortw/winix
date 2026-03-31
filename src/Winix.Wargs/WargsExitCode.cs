namespace Winix.Wargs;

/// <summary>
/// Exit codes specific to wargs, beyond the standard POSIX codes in <see cref="Yort.ShellKit.ExitCode"/>.
/// </summary>
public static class WargsExitCode
{
    /// <summary>One or more child processes exited non-zero (GNU xargs convention).</summary>
    public const int ChildFailed = 123;

    /// <summary>Execution aborted early due to --fail-fast.</summary>
    public const int FailFastAbort = 124;
}
