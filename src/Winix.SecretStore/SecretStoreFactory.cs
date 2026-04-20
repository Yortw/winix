#nullable enable
using System;
using System.Runtime.InteropServices;

namespace Winix.SecretStore;

/// <summary>Selects an <see cref="ISecretStore"/> implementation appropriate to the current OS.</summary>
public static class SecretStoreFactory
{
    /// <summary>Create a user-scope store for the current OS.</summary>
    public static ISecretStore CreateUserStore()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsCredentialManagerStore();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsKeychainStore(useSystemKeychain: false);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxLibsecretStore();
        }
        throw new PlatformNotSupportedException("Unsupported OS.");
    }

    /// <summary>Create a machine-scope store. Throws on Linux (no native primitive in v1).</summary>
    public static ISecretStore CreateMachineStore()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsCredentialManagerStore();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsKeychainStore(useSystemKeychain: true);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException(
                "Machine scope is not supported on Linux. Use user scope, or install systemd-creds (Linux machine scope is a v2 feature).");
        }
        throw new PlatformNotSupportedException("Unsupported OS.");
    }
}
