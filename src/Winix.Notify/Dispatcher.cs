#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify;

/// <summary>
/// Runs a list of backends in parallel and aggregates results in input order.
/// Each backend is responsible for converting its own exceptions into <see cref="BackendResult"/>;
/// the dispatcher also wraps each call defensively so that a contract-violating backend
/// can't fault the entire batch and discard peer successes.
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
            return Array.Empty<BackendResult>();
        }

        // Round-1 review SFH-I3 — wrap each backend call in defense-in-depth catch so a
        // contract-violating backend (one that throws instead of returning a BackendResult)
        // can't corrupt the whole batch via Task.WhenAll faulting and discarding peer
        // successes. The IBackend contract still says "never throw"; this is belt-and-braces
        // so a future backend bug can't silently mask a successful desktop notification with
        // a process-crash error. OperationCanceledException is allowed to propagate because
        // cooperative cancellation is part of the dispatcher's normal shape.
        var tasks = new Task<BackendResult>[backends.Count];
        for (int i = 0; i < backends.Count; i++)
        {
            tasks[i] = SafeSend(backends[i], message, ct);
        }
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task<BackendResult> SafeSend(IBackend backend, NotifyMessage message, CancellationToken ct)
    {
        try
        {
            return await backend.SendAsync(message, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cooperative cancellation — propagate so Task.WhenAll faults with cancellation
            // rather than swallowing it as a per-backend "failure".
            throw;
        }
        catch (Exception ex)
        {
            return new BackendResult(
                backend.Name,
                false,
                $"{backend.Name}: {ex.GetType().Name} (backend violated never-throw contract): {ex.Message}",
                null);
        }
    }
}
