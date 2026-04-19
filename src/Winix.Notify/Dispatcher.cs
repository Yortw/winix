#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify;

/// <summary>
/// Runs a list of backends in parallel and aggregates results in input order.
/// Each backend is responsible for converting its own exceptions into <see cref="BackendResult"/>;
/// the dispatcher does not catch — a backend that throws will fault the returned task.
/// </summary>
public static class Dispatcher
{
    /// <summary>Send the message to every backend in parallel; return results in the same order as input.</summary>
    public static async Task<IReadOnlyList<BackendResult>> SendAsync(
        IReadOnlyList<IBackend> backends,
        NotifyMessage message,
        CancellationToken ct)
    {
        if (backends.Count == 0)
        {
            return System.Array.Empty<BackendResult>();
        }

        var tasks = backends.Select(b => b.SendAsync(message, ct)).ToArray();
        BackendResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }
}
