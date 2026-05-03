#nullable enable
using System;
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

    // -- Round-1 review TA-I3 — was previously a [Fact] with conditional assertions
    //    ("if (!result.Ok) ..."), which let the test pass vacuously on Linux when
    //    notify-send was installed (no negative assertion ran), and rely on the wrong
    //    platform's binary-not-found failure on Windows/macOS. Split into two
    //    SkippableFacts so each platform deterministically exercises one branch. --

    [SkippableFact]
    public async System.Threading.Tasks.Task Send_OnLinux_ProducesResultWithCorrectName()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux-only — exercises the notify-send process invocation.");
        if (!OperatingSystem.IsLinux()) return; // satisfies CA1416 alongside Skip.IfNot

        var b = new LinuxNotifySendBackend();
        var result = await b.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);
        Assert.Equal("linux-notify-send", result.BackendName);
        // Don't assert Ok — CI may not have a display server even on Linux. We only assert
        // the contract that the backend produced a typed result and didn't throw.
    }

    [SkippableFact]
    public async System.Threading.Tasks.Task Send_OnNonLinux_FailsWithNotifySendNotFound()
    {
        Skip.If(OperatingSystem.IsLinux(), "Non-Linux platforms — pins the binary-not-found failure-path message.");

        var b = new LinuxNotifySendBackend();
        var result = await b.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);
        Assert.Equal("linux-notify-send", result.BackendName);
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Contains("notify-send", result.Error, StringComparison.Ordinal);
    }
}
