#nullable enable

namespace Winix.Man;

/// <summary>Exit codes for the man tool.</summary>
public static class ManExitCode
{
    /// <summary>Page found and displayed successfully.</summary>
    public const int Success = 0;

    /// <summary>Requested page was not found.</summary>
    public const int NotFound = 1;

    /// <summary>Usage error (bad arguments).</summary>
    public const int UsageError = 2;
}
