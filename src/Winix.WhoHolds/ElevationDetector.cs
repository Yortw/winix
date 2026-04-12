#nullable enable

using System;

namespace Winix.WhoHolds;

/// <summary>
/// Detects whether the current process is running with elevated privileges.
/// Elevated means Administrator on Windows, root (UID 0) on Unix.
/// </summary>
public static class ElevationDetector
{
    /// <summary>
    /// Returns <c>true</c> if the process is running with elevated/admin/root privileges.
    /// Uses <see cref="Environment.IsPrivilegedProcess"/> (.NET 8+, AOT-safe).
    /// </summary>
    public static bool IsElevated()
    {
        return Environment.IsPrivilegedProcess;
    }
}
