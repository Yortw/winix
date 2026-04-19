#nullable enable
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
    public List<NotifyMessage> Received { get; } = new();

    public FakeBackend(string name) { Name = name; }

    public async Task<BackendResult> SendAsync(NotifyMessage message, CancellationToken ct)
    {
        if (DelayMs > 0)
        {
            await Task.Delay(DelayMs, ct);
        }
        Received.Add(message);
        return ShouldSucceed
            ? new BackendResult(Name, true, null, null)
            : new BackendResult(Name, false, FailureMessage, null);
    }
}
