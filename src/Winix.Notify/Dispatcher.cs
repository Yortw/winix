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

        // Round-1 review SFH-I3 + Round-2 R2-I1 — wrap each backend call in defense-in-depth
        // catch so a contract-violating backend (one that throws instead of returning a
        // BackendResult) can't corrupt the whole batch via Task.WhenAll faulting and
        // discarding peer successes. Round-2 update: OCE is also converted to a per-backend
        // failure rather than propagated. The original "cooperative cancellation propagates"
        // shape (round 1) was speculative — Cli.Run's only cancellation source today is its
        // own internal 15s timeout, with no external ct from the caller. Letting OCE
        // propagate meant a 15s timeout that fired while one backend was in flight would
        // discard the OTHER backend's already-completed success, leaving the user with a
        // generic "TaskCanceledException" stderr line and exit 125 — even though their
        // desktop notification fired. Converting to per-backend "cancelled" preserves
        // sibling results and produces a meaningful per-backend error message.
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
            // Round-2 R2-I1 — preserve sibling backends. The cancellation reason is the
            // dispatcher's own timeout (or, in future, an external ct) — either way the
            // user is better served by a per-backend "cancelled" result + sibling success
            // than a top-level OCE that wipes the whole result vector.
            return new BackendResult(
                backend.Name,
                false,
                $"{backend.Name}: cancelled before completion (timeout or caller cancellation)",
                null);
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
