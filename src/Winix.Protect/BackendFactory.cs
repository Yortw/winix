#nullable enable
using System;
using System.Runtime.InteropServices;

namespace Winix.Protect;

public static class BackendFactory
{
    public static IProtectBackend Create(Scope scope)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#pragma warning disable CA1416
            return new DpapiBackend(scope);
#pragma warning restore CA1416
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
#pragma warning disable CA1416
            return new AeadKeychainBackend(scope);
#pragma warning restore CA1416
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (scope == Scope.Machine)
            {
                throw new PlatformNotSupportedException(
                    "Machine scope is not supported on Linux. Use user scope, or install systemd-creds (v2 feature).");
            }
#pragma warning disable CA1416
            return new AeadLibsecretBackend();
#pragma warning restore CA1416
        }
        throw new PlatformNotSupportedException("Unsupported OS.");
    }

    public static IProtectBackend CreateForMarker(PlatformMarker marker)
    {
#pragma warning disable CA1416
        return marker switch
        {
            PlatformMarker.WindowsDpapiUser when RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                => new DpapiBackend(Scope.User),
            PlatformMarker.WindowsDpapiMachine when RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                => new DpapiBackend(Scope.Machine),
            PlatformMarker.MacKeychainUser when RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                => new AeadKeychainBackend(Scope.User),
            PlatformMarker.MacKeychainMachine when RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                => new AeadKeychainBackend(Scope.Machine),
            PlatformMarker.LinuxLibsecretUser when RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                => new AeadLibsecretBackend(),
            _ => throw new PlatformNotSupportedException(
                $"This file was encrypted on {PlatformOfMarker(marker)} and cannot be decrypted on this machine."),
        };
#pragma warning restore CA1416
    }

    private static string PlatformOfMarker(PlatformMarker marker) => marker switch
    {
        PlatformMarker.WindowsDpapiUser or PlatformMarker.WindowsDpapiMachine => "Windows",
        PlatformMarker.MacKeychainUser or PlatformMarker.MacKeychainMachine => "macOS",
        PlatformMarker.LinuxLibsecretUser => "Linux",
        _ => "an unknown platform",
    };
}
