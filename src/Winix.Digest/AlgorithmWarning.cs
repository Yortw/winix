#nullable enable
using System.IO;

namespace Winix.Digest;

/// <summary>
/// Returns the user-facing legacy-algorithm warning text for MD5 and SHA-1, or null
/// for non-legacy algorithms. Extracted from Program.cs so the warning strings can
/// be pinned in tests rather than living as a free-floating literal in Main.
/// </summary>
public static class AlgorithmWarning
{
    /// <summary>Returns the warning string for legacy algorithms, or null when nothing should be emitted.</summary>
    public static string? GetWarningOrNull(HashAlgorithm algorithm)
    {
        return algorithm switch
        {
            HashAlgorithm.Md5  => "digest: warning: MD5 is cryptographically broken; do not use for security-sensitive purposes.",
            HashAlgorithm.Sha1 => "digest: warning: SHA-1 is broken for collision resistance; HMAC-SHA-1 is still acceptable for signing but prefer HMAC-SHA-256 for new systems.",
            _ => null,
        };
    }

    /// <summary>Writes the legacy-algorithm warning (if any) to <paramref name="stderr"/>.</summary>
    public static void EmitIfLegacy(HashAlgorithm algorithm, TextWriter stderr)
    {
        string? warning = GetWarningOrNull(algorithm);
        if (warning is not null) stderr.WriteLine(warning);
    }
}
