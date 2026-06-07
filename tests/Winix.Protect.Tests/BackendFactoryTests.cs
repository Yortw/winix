#nullable enable
using System;
using System.Runtime.InteropServices;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

// In the SharedKeystore collection so it serialises against the CliErrorHandlingTests that
// temporarily set the static BackendFactory.CreateOverride — a parallel collection calling
// Create() during that window would receive the throwing override (round-3 review finding).
[Collection(SharedKeystoreCollection.Name)]
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

    [SkippableFact]
    public void Create_MachineScope_Linux_Throws()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux-only");
        if (!OperatingSystem.IsLinux()) return; // CA1416 analyzer requires this; deliberate redundancy
        PlatformNotSupportedException ex = Assert.Throws<PlatformNotSupportedException>(
            () => BackendFactory.Create(Scope.Machine));
        Assert.Contains("Linux", ex.Message);
    }

    [SkippableFact]
    public void CreateForMarker_WrongPlatform_Throws()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only");
        if (!OperatingSystem.IsWindows()) return; // CA1416 analyzer requires this; deliberate redundancy
        PlatformNotSupportedException ex = Assert.Throws<PlatformNotSupportedException>(
            () => BackendFactory.CreateForMarker(PlatformMarker.MacKeychainUser));
        Assert.Contains("macOS", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
