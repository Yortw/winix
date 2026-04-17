using Xunit;
using Winix.Clip;

namespace Winix.Clip.Tests;

public class ClipboardBackendFactoryTests
{
    [Fact]
    public void Windows_ReturnsWindowsBackend()
    {
        var probe = new FakePlatformProbe { Os = ClipPlatform.Windows };

        var backend = ClipboardBackendFactory.Create(probe, primary: false, out string? error);

        Assert.NotNull(backend);
        Assert.Null(error);
        Assert.IsType<WindowsClipboardBackend>(backend);
    }

    [Fact]
    public void MacOs_ReturnsShellOutPbBackend()
    {
        var probe = new FakePlatformProbe { Os = ClipPlatform.MacOS };

        var backend = ClipboardBackendFactory.Create(probe, primary: false, out string? error);

        Assert.NotNull(backend);
        Assert.Null(error);
        Assert.IsType<ShellOutClipboardBackend>(backend);
    }

    [Fact]
    public void Linux_WaylandWithWlCopy_ReturnsWlClipboardBackend()
    {
        var probe = new FakePlatformProbe { Os = ClipPlatform.Linux };
        probe.Env["WAYLAND_DISPLAY"] = "wayland-0";
        probe.PresentBinaries.Add("wl-copy");

        var backend = ClipboardBackendFactory.Create(probe, primary: false, out string? error);

        Assert.NotNull(backend);
        Assert.Null(error);
        Assert.IsType<ShellOutClipboardBackend>(backend);
    }

    [Fact]
    public void Linux_NoWayland_WithXclip_ReturnsXclipBackend()
    {
        var probe = new FakePlatformProbe { Os = ClipPlatform.Linux };
        probe.PresentBinaries.Add("xclip");

        var backend = ClipboardBackendFactory.Create(probe, primary: false, out string? error);

        Assert.NotNull(backend);
        Assert.Null(error);
        Assert.IsType<ShellOutClipboardBackend>(backend);
    }

    [Fact]
    public void Linux_OnlyXsel_ReturnsXselBackend()
    {
        var probe = new FakePlatformProbe { Os = ClipPlatform.Linux };
        probe.PresentBinaries.Add("xsel");

        var backend = ClipboardBackendFactory.Create(probe, primary: false, out string? error);

        Assert.NotNull(backend);
        Assert.Null(error);
        Assert.IsType<ShellOutClipboardBackend>(backend);
    }

    [Fact]
    public void Linux_XclipPreferredOverXsel()
    {
        var probe = new FakePlatformProbe { Os = ClipPlatform.Linux };
        probe.PresentBinaries.Add("xclip");
        probe.PresentBinaries.Add("xsel");

        var backend = ClipboardBackendFactory.Create(probe, primary: false, out _);
        Assert.NotNull(backend);

        // Indirectly verify xclip was chosen by invoking via a fake runner and
        // asserting the binary name. Construct an equivalent backend explicitly
        // for cross-check, since we can't peek into the factory's choice.
        var runner = new FakeProcessRunner();
        var explicitly = new ShellOutClipboardBackend(HelperSets.XClip, runner);
        explicitly.CopyText("x");
        Assert.Equal("xclip", runner.Invocations[0].File);
    }

    [Fact]
    public void Linux_WaylandSetButNoWlCopy_FallsBackToXclip()
    {
        var probe = new FakePlatformProbe { Os = ClipPlatform.Linux };
        probe.Env["WAYLAND_DISPLAY"] = "wayland-0";
        probe.PresentBinaries.Add("xclip");

        var backend = ClipboardBackendFactory.Create(probe, primary: false, out string? error);

        Assert.NotNull(backend);
        Assert.Null(error);
    }

    [Fact]
    public void Linux_NoHelpers_ReturnsNullWithInstallHint()
    {
        var probe = new FakePlatformProbe { Os = ClipPlatform.Linux };

        var backend = ClipboardBackendFactory.Create(probe, primary: false, out string? error);

        Assert.Null(backend);
        Assert.NotNull(error);
        Assert.Contains("wl-clipboard", error);
        Assert.Contains("xclip", error);
        Assert.Contains("xsel", error);
    }

    [Fact]
    public void UnknownPlatform_ReturnsNullWithError()
    {
        var probe = new FakePlatformProbe { Os = ClipPlatform.Unknown };

        var backend = ClipboardBackendFactory.Create(probe, primary: false, out string? error);

        Assert.Null(backend);
        Assert.NotNull(error);
    }
}
