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

    /// <summary>
    /// Full path to the process executable (e.g. "C:\Windows\System32\svchost.exe").
    /// Empty when unavailable (elevated access required, process exited, or platform limitation).
    /// </summary>
    public string ProcessPath { get; }

    /// <summary>
    /// TCP connection state (e.g. "LISTEN", "ESTABLISHED"). Empty for file locks and UDP entries,
    /// which have no connection state concept.
    /// </summary>
    public string State { get; }

    /// <summary>Creates a new LockInfo.</summary>
    /// <param name="processId">Process ID.</param>
    /// <param name="processName">Process name; null is treated as empty.</param>
    /// <param name="resource">Resource being held; null is treated as empty.</param>
    /// <param name="processPath">Full executable path; null or omitted defaults to empty.</param>
    /// <param name="state">TCP state string; null or omitted defaults to empty.</param>
    public LockInfo(int processId, string processName, string resource, string processPath = "", string state = "")
    {
        ProcessId = processId;
        ProcessName = processName ?? "";
        Resource = resource ?? "";
        ProcessPath = processPath ?? "";
        State = state ?? "";
    }
}
