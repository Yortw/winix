#nullable enable
using System;
using System.Runtime.InteropServices;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

public class BackendFactoryTests
{
    private static bool OnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static bool OnMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    private static bool OnLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    [Fact]
    public void Create_UserScope_ReturnsPlatformBackend()
    {
        IProtectBackend backend = BackendFactory.Create(Scope.User);
        if (OnWindows) Assert.Equal(PlatformMarker.WindowsDpapiUser, backend.Marker);
        else if (OnMac) Assert.Equal(PlatformMarker.MacKeychainUser, backend.Marker);
        else if (OnLinux) Assert.Equal(PlatformMarker.LinuxLibsecretUser, backend.Marker);
    }

    [Fact]
    public void Create_MachineScope_Linux_Throws()
    {
        if (!OnLinux) return;
        PlatformNotSupportedException ex = Assert.Throws<PlatformNotSupportedException>(
            () => BackendFactory.Create(Scope.Machine));
        Assert.Contains("Linux", ex.Message);
    }

    [Fact]
    public void CreateForMarker_WrongPlatform_Throws()
    {
        if (!OnWindows) return;
        PlatformNotSupportedException ex = Assert.Throws<PlatformNotSupportedException>(
            () => BackendFactory.CreateForMarker(PlatformMarker.MacKeychainUser));
        Assert.Contains("macOS", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
