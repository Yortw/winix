namespace Yort.ShellKit;

/// <summary>
/// Standard POSIX-convention exit codes for Winix CLI tools.
/// Tools with domain-specific codes (e.g. squeeze uses 1 for compression error)
/// can define their own constants alongside these.
/// </summary>
public static class ExitCode
{
    /// <summary>Successful execution.</summary>
    public const int Success = 0;

    /// <summary>Usage error: bad arguments, missing required input.</summary>
    public const int UsageError = 125;

    /// <summary>Command not executable (permission denied).</summary>
    public const int NotExecutable = 126;

    /// <summary>Command not found on PATH.</summary>
    public const int NotFound = 127;
}
