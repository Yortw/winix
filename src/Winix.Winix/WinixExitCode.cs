#nullable enable

namespace Winix.Winix;

/// <summary>
/// Exit codes for the winix suite installer. Aligns with the codes documented in
/// <c>src/winix/README.md</c> and emitted via <c>--describe</c>. The internal-error and
/// no-package-manager codes follow the same POSIX-ish convention as the rest of the suite
/// (125+ for tool-internal errors), while the operational codes (0/1) match the
/// "did all tools succeed" contract used by package managers like apt and brew.
/// </summary>
public static class WinixExitCode
{
    /// <summary>All requested operations succeeded.</summary>
    public const int Success = 0;

    /// <summary>One or more per-tool operations failed (e.g. an underlying PM returned non-zero).</summary>
    public const int ToolFailure = 1;

    /// <summary>Usage error — bad command, unknown flag, or invalid argument value.</summary>
    public const int UsageError = 125;

    /// <summary>No supported package manager could be found on this machine, or the
    /// package manager requested via <c>--via</c> is not available.</summary>
    public const int NoPackageManager = 126;

    /// <summary>An internal error prevented winix from running (manifest parse failure,
    /// network error fetching the manifest, unexpected exception during orchestration).</summary>
    public const int InternalError = 127;
}
