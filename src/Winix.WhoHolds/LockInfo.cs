#nullable enable

namespace Winix.WhoHolds;

/// <summary>
/// A process holding a resource (file lock or port binding).
/// Returned by all finder implementations.
/// </summary>
public sealed class LockInfo
{
    /// <summary>Process ID.</summary>
    public int ProcessId { get; }

    /// <summary>Process name (e.g. "devenv.exe").</summary>
    public string ProcessName { get; }

    /// <summary>
    /// The resource being held. For files: the queried file path.
    /// For ports: "TCP :8080" or "UDP :53".
    /// </summary>
    public string Resource { get; }

    /// <summary>Creates a new LockInfo.</summary>
    public LockInfo(int processId, string processName, string resource)
    {
        ProcessId = processId;
        ProcessName = processName ?? "";
        Resource = resource ?? "";
    }
}
