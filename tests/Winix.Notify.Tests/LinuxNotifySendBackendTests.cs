#nullable enable
using System.Threading;
using Xunit;
using Winix.Notify;

namespace Winix.Notify.Tests;

public class LinuxNotifySendBackendTests
{
    [Fact]
    public void Backend_Name_IsLinuxNotifySend()
    {
        var b = new LinuxNotifySendBackend();
        Assert.Equal("linux-notify-send", b.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task Send_ProducesResult_WithCorrectName()
    {
        // We can't reliably exercise the success path in CI (no display), but we can verify
        // the backend never throws and surfaces a result with the expected name. On Windows
        // dev machines this falls into the "notify-send not found" failure path.
        var b = new LinuxNotifySendBackend();
        var result = await b.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);

        Assert.Equal("linux-notify-send", result.BackendName);
        if (!result.Ok)
        {
            // The failure message should mention notify-send so the user knows what's missing.
            Assert.Contains("notify-send", result.Error);
        }
    }
}
