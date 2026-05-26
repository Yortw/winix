#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify;

/// <summary>One notification destination. Implementations are stateless; the dispatcher selects which backends to invoke per call.</summary>
public interface IBackend
{
    /// <summary>Stable backend identifier ("windows-toast", "ntfy", etc.). Used in JSON output and stderr warnings.</summary>
    string Name { get; }

    /// <summary>Sends the message. Implementations should not throw — convert exceptions into <see cref="BackendResult"/> with <c>Ok=false</c>.</summary>
    Task<BackendResult> SendAsync(NotifyMessage message, CancellationToken ct);
}
