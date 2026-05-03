#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Winix.Notify;

namespace Winix.Notify.Tests.Fakes;

/// <summary>Test double for <see cref="IBackend"/>. Records every call; lets tests force success or failure.</summary>
public sealed class FakeBackend : IBackend
{
    public string Name { get; }
    public bool ShouldSucceed { get; set; } = true;
    public string FailureMessage { get; set; } = "fake failure";
    public int DelayMs { get; set; } = 0;
    /// <summary>When non-null, SendAsync throws this exception instead of returning a result. Used to test the dispatcher's never-throw defense-in-depth catch.</summary>
    public Exception? ShouldThrow { get; set; }
    public List<NotifyMessage> Received { get; } = new();

    /// <summary>UTC timestamp at which <see cref="SendAsync"/> entered. Null until the call happens.</summary>
    public DateTime? StartedAt { get; private set; }

    /// <summary>UTC timestamp at which <see cref="SendAsync"/> was about to return. Null until the call completes.</summary>
    public DateTime? EndedAt { get; private set; }

    public FakeBackend(string name) { Name = name; }

    public async Task<BackendResult> SendAsync(NotifyMessage message, CancellationToken ct)
    {
        StartedAt = DateTime.UtcNow;
        if (DelayMs > 0)
        {
            await Task.Delay(DelayMs, ct);
        }
        Received.Add(message);
        EndedAt = DateTime.UtcNow;
        if (ShouldThrow is not null)
        {
            throw ShouldThrow;
        }
        return ShouldSucceed
            ? new BackendResult(Name, true, null, null)
            : new BackendResult(Name, false, FailureMessage, null);
    }
}
