#nullable enable
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using Winix.Notify;
using Xunit;
using Yort.ShellKit;

namespace Winix.Notify.Tests;

// Round-1 review TA-C1/C2 — Program.cs's ResolveExitCode and BuildBackends previously had
// zero coverage. Both contracts are user-visible: ResolveExitCode is what every CI/cron
// consumer parses; BuildBackends is what determines whether a Linux user gets notify-send
// vs (other-Unix) gets ntfy-only. Pin all four exit-code cells and each platform branch.
public class CliTests
{
    private static NotifyOptions OptsAllOn() => new(
        Title: "t", Body: "b", Urgency: Urgency.Normal, IconPath: null,
        DesktopEnabled: true, NtfyEnabled: true,
        NtfyServer: "https://ntfy.sh", NtfyTopic: "alerts", NtfyToken: null,
        Json: false, Strict: false);

    // ── ResolveExitCode — 4 cells ──

    [Fact]
    public void ResolveExitCode_AllOk_ReturnsSuccess()
    {
        var results = new List<BackendResult>
        {
            new("desktop", true, null, null),
            new("ntfy", true, null, null),
        };
        Assert.Equal(ExitCode.Success, Cli.ResolveExitCode(strict: false, results));
        Assert.Equal(ExitCode.Success, Cli.ResolveExitCode(strict: true, results));
    }

    [Fact]
    public void ResolveExitCode_AllFail_ReturnsNotExecutable_BothModes()
    {
        var results = new List<BackendResult>
        {
            new("desktop", false, "no display", null),
            new("ntfy", false, "401", null),
        };
        Assert.Equal(ExitCode.NotExecutable, Cli.ResolveExitCode(strict: false, results));
        Assert.Equal(1, Cli.ResolveExitCode(strict: true, results));
    }

    [Fact]
    public void ResolveExitCode_Mixed_NonStrict_ReturnsSuccess()
    {
        // Best-effort default: at least one delivered → exit 0.
        var results = new List<BackendResult>
        {
            new("desktop", true, null, null),
            new("ntfy", false, "topic disabled", null),
        };
        Assert.Equal(ExitCode.Success, Cli.ResolveExitCode(strict: false, results));
    }

    [Fact]
    public void ResolveExitCode_Mixed_Strict_ReturnsOne()
    {
        // Strict: any failure → exit 1.
        var results = new List<BackendResult>
        {
            new("desktop", true, null, null),
            new("ntfy", false, "topic disabled", null),
        };
        Assert.Equal(1, Cli.ResolveExitCode(strict: true, results));
    }

    [Fact]
    public void ResolveExitCode_EmptyResults_ReturnsNotExecutable()
    {
        // Defensive — Cli.Run guards against this upstream, but if a future refactor passes
        // an empty list through, the contract should be NotExecutable, not Success.
        var results = new List<BackendResult>();
        Assert.Equal(ExitCode.NotExecutable, Cli.ResolveExitCode(strict: false, results));
        Assert.Equal(ExitCode.NotExecutable, Cli.ResolveExitCode(strict: true, results));
    }

    // ── BuildBackends — per-platform selection ──

    [Fact]
    public void BuildBackends_Linux_DesktopOnly_ProducesNotifySend()
    {
        var opts = OptsAllOn() with { NtfyEnabled = false, NtfyTopic = null };
        var http = new HttpClient();
        var backends = Cli.BuildBackends(opts, OSPlatform.Linux, http);
        Assert.Single(backends);
        Assert.IsType<LinuxNotifySendBackend>(backends[0]);
    }

    [Fact]
    public void BuildBackends_OSX_DesktopOnly_ProducesAppleScript()
    {
        var opts = OptsAllOn() with { NtfyEnabled = false, NtfyTopic = null };
        var http = new HttpClient();
        var backends = Cli.BuildBackends(opts, OSPlatform.OSX, http);
        Assert.Single(backends);
        Assert.IsType<MacOsAppleScriptBackend>(backends[0]);
    }

    [Fact]
    public void BuildBackends_Windows_DesktopOnly_ProducesToast()
    {
        var opts = OptsAllOn() with { NtfyEnabled = false, NtfyTopic = null };
        var http = new HttpClient();
        var backends = Cli.BuildBackends(opts, OSPlatform.Windows, http);
        Assert.Single(backends);
        // Type check via name to avoid CA1416 — WindowsToastBackend is [SupportedOSPlatform("windows")]
        // so the analyzer flags a direct typeof() on non-Windows; the runtime check works on every OS.
        Assert.Equal("WindowsToastBackend", backends[0].GetType().Name);
    }

    [Fact]
    public void BuildBackends_OtherUnix_DesktopOnly_ProducesEmpty()
    {
        // FreeBSD / Solaris / others — no desktop backend supported. With ntfy disabled,
        // the list is empty; Cli.Run then returns UsageError. This is the silent-fall-through
        // branch the previous Program.cs comment ("Other Unixes — no desktop backend") created
        // and this test pins.
        var opts = OptsAllOn() with { NtfyEnabled = false, NtfyTopic = null };
        var http = new HttpClient();
        var backends = Cli.BuildBackends(opts, OSPlatform.Create("FREEBSD"), http);
        Assert.Empty(backends);
    }

    [Fact]
    public void BuildBackends_OtherUnix_NtfyOnly_ProducesNtfy()
    {
        var opts = OptsAllOn() with { DesktopEnabled = false };
        var http = new HttpClient();
        var backends = Cli.BuildBackends(opts, OSPlatform.Create("FREEBSD"), http);
        Assert.Single(backends);
        Assert.IsType<NtfyBackend>(backends[0]);
    }

    [Fact]
    public void BuildBackends_Linux_DesktopAndNtfy_ProducesBoth_InOrder()
    {
        // Order matters for the dispatcher (it preserves order in results) and JSON output.
        var http = new HttpClient();
        var backends = Cli.BuildBackends(OptsAllOn(), OSPlatform.Linux, http);
        Assert.Equal(2, backends.Count);
        Assert.IsType<LinuxNotifySendBackend>(backends[0]);
        Assert.IsType<NtfyBackend>(backends[1]);
    }

    [Fact]
    public void BuildBackends_DesktopDisabled_NtfyDisabled_ProducesEmpty()
    {
        var opts = OptsAllOn() with { DesktopEnabled = false, NtfyEnabled = false, NtfyTopic = null };
        var http = new HttpClient();
        var backends = Cli.BuildBackends(opts, OSPlatform.Linux, http);
        Assert.Empty(backends);
    }
}
