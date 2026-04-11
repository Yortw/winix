#nullable enable

using System.Collections.Generic;

namespace Winix.Winix;

/// <summary>
/// Identifies the operating system family the tool is running on.
/// </summary>
public enum PlatformId
{
    /// <summary>Microsoft Windows.</summary>
    Windows,

    /// <summary>Apple macOS.</summary>
    MacOS,

    /// <summary>Linux (any distribution).</summary>
    Linux,
}

/// <summary>
/// Detects the current operating system and resolves which
/// <see cref="IPackageManagerAdapter"/> to use based on the platform default
/// chain or a caller-specified override.
/// </summary>
public static class PlatformDetector
{
    // Default preference chains per platform. Each entry is the Name of a
    // registered IPackageManagerAdapter. Resolution walks left-to-right and
    // stops at the first adapter that reports IsAvailable() == true.
    private static readonly string[] WindowsChain = { "winget", "scoop", "dotnet" };
    private static readonly string[] MacOSChain   = { "brew", "dotnet" };
    private static readonly string[] LinuxChain   = { "dotnet" };

    /// <summary>
    /// Detects and returns the current operating system as a <see cref="PlatformId"/>.
    /// Falls back to <see cref="PlatformId.Linux"/> for any non-Windows, non-macOS OS.
    /// </summary>
    public static PlatformId GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return PlatformId.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return PlatformId.MacOS;
        }

        return PlatformId.Linux;
    }

    /// <summary>
    /// Returns the ordered list of package manager names to try on
    /// <paramref name="platform"/>, from most- to least-preferred.
    /// </summary>
    /// <param name="platform">The target platform.</param>
    /// <returns>
    /// A non-empty array of adapter names. The caller should walk the array
    /// and use the first entry for which <see cref="IPackageManagerAdapter.IsAvailable"/>
    /// returns <see langword="true"/>.
    /// </returns>
    public static string[] GetDefaultChain(PlatformId platform)
    {
        return platform switch
        {
            PlatformId.Windows => WindowsChain,
            PlatformId.MacOS   => MacOSChain,
            _                  => LinuxChain,
        };
    }

    /// <summary>
    /// Resolves the <see cref="IPackageManagerAdapter"/> to use, honouring an
    /// optional explicit override before falling back to the platform default chain.
    /// </summary>
    /// <param name="viaOverride">
    /// When non-<see langword="null"/> and non-empty, the adapter with this name
    /// is returned if it exists in <paramref name="adapters"/> and
    /// <see cref="IPackageManagerAdapter.IsAvailable"/> returns <see langword="true"/>;
    /// otherwise <see langword="null"/> is returned immediately (no fallback).
    /// </param>
    /// <param name="adapters">
    /// All registered adapters, keyed by <see cref="IPackageManagerAdapter.Name"/>.
    /// </param>
    /// <param name="platform">
    /// The platform whose default chain is used when <paramref name="viaOverride"/>
    /// is not set.
    /// </param>
    /// <returns>
    /// The first available adapter, or <see langword="null"/> when none is available.
    /// </returns>
    public static IPackageManagerAdapter? ResolveAdapter(
        string? viaOverride,
        IDictionary<string, IPackageManagerAdapter> adapters,
        PlatformId platform)
    {
        if (!string.IsNullOrEmpty(viaOverride))
        {
            // Explicit override: only use the requested adapter, and only if available.
            if (adapters.TryGetValue(viaOverride, out var overrideAdapter) && overrideAdapter.IsAvailable())
            {
                return overrideAdapter;
            }

            return null;
        }

        // Walk the platform chain and return the first available adapter.
        foreach (var name in GetDefaultChain(platform))
        {
            if (adapters.TryGetValue(name, out var adapter) && adapter.IsAvailable())
            {
                return adapter;
            }
        }

        return null;
    }
}
