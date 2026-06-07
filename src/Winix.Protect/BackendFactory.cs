#nullable enable
using System;
using System.Runtime.InteropServices;

namespace Winix.Protect;

public static class BackendFactory
{
    /// <summary>
    /// Test seam: when set, <see cref="Create"/> returns this instead of a real platform backend.
    /// Exists so Cli.Run's SecretStoreException catch arm is deterministically testable — the real
    /// keychain backends only raise it on environmental failures (locked collection, missing
    /// secret-tool) that can't be triggered on demand. Mirrors trash's backendOverride precedent.
    /// Tests must reset to null in a finally block (static state).
    /// </summary>
    internal static Func<Scope, IProtectBackend>? CreateOverride;

    public static IProtectBackend Create(Scope scope)
    {
        if (CreateOverride is not null)
        {
            return CreateOverride(scope);
        }
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
