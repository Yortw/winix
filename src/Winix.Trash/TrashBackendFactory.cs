#nullable enable
using System;
using System.Runtime.InteropServices;

namespace Winix.Trash;

/// <summary>Creates the platform-appropriate <see cref="ITrashBackend"/> at runtime.
/// <see cref="Cli"/> unit tests inject a fake backend, so this is exercised only by the real
/// console app and the platform-gated integration tests.</summary>
public static class TrashBackendFactory
{
    /// <summary>Returns the OS-native trash backend.</summary>
    /// <exception cref="PlatformNotSupportedException">The current OS has no trash backend.</exception>
    public static ITrashBackend Create()
    {
        // OS guards double as CA1416 annotations for the platform-attributed backends.
        if (OperatingSystem.IsWindows())
        {
            return new WindowsRecycleBinBackend();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxFreeDesktopBackend();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOsTrashBackend();
        }

        throw new PlatformNotSupportedException(
            $"trash has no backend for this operating system ({RuntimeInformation.OSDescription}).");
    }
}
