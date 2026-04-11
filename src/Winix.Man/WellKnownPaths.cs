#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace Winix.Man;

/// <summary>
/// Detects well-known man page search directories for the current platform.
/// </summary>
/// <remarks>
/// Covers standard system paths and common package-manager / toolchain locations.
/// Only directories that exist on disk at the time of the call are returned.
/// </remarks>
internal static class WellKnownPaths
{
    /// <summary>
    /// Returns all well-known man page directories that currently exist on the local machine.
    /// </summary>
    /// <returns>
    /// A list of existing directory paths, ordered from most-specific to least-specific.
    /// The list may be empty when running in a stripped-down environment.
    /// </returns>
    internal static IReadOnlyList<string> Detect()
    {
        var paths = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            AddWindowsPaths(paths);
        }
        else if (OperatingSystem.IsMacOS())
        {
            AddMacOsPaths(paths);
        }
        else
        {
            // Linux and other Unix-like systems share the FHS layout.
            AddLinuxPaths(paths);
        }

        return paths;
    }

    /// <summary>
    /// Adds an existing directory to the list if it exists on disk.
    /// </summary>
    private static void AddIfExists(List<string> paths, string path)
    {
        if (Directory.Exists(path))
        {
            paths.Add(path);
        }
    }

    private static void AddWindowsPaths(List<string> paths)
    {
        // Git for Windows ships man pages under usr/share/man relative to its install root.
        // Try to locate the Git install root via PATH before falling back to well-known locations.
        string? gitFromPath = FindGitInstallRootViaPath();
        if (gitFromPath is not null)
        {
            AddIfExists(paths, Path.Combine(gitFromPath, "usr", "share", "man"));
        }

        // Common Git for Windows install locations (32-bit and 64-bit Program Files).
        AddIfExists(paths, @"C:\Program Files\Git\usr\share\man");
        AddIfExists(paths, @"C:\Program Files (x86)\Git\usr\share\man");

        // MSYS2 — check the MSYS2_ROOT environment variable first, then the standard install path.
        string msys2Root = Environment.GetEnvironmentVariable("MSYS2_ROOT") ?? @"C:\msys64";
        AddIfExists(paths, Path.Combine(msys2Root, "usr", "share", "man"));
    }

    /// <summary>
    /// Walks PATH entries to find a git.exe and derives the Git install root from it.
    /// </summary>
    /// <returns>
    /// The Git install root (the directory two levels above git.exe when it lives at
    /// <c>&lt;root&gt;\bin\git.exe</c> or <c>&lt;root&gt;\cmd\git.exe</c>), or <see langword="null"/>
    /// if git.exe cannot be found on PATH.
    /// </returns>
    private static string? FindGitInstallRootViaPath()
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv is null)
        {
            return null;
        }

        foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir.Trim(), "git.exe");
            if (File.Exists(candidate))
            {
                // git.exe lives at <root>/bin/git.exe or <root>/cmd/git.exe;
                // parent of parent is the install root.
                string? binDir = Path.GetDirectoryName(candidate);
                if (binDir is not null)
                {
                    return Path.GetDirectoryName(binDir);
                }
            }
        }

        return null;
    }

    private static void AddMacOsPaths(List<string> paths)
    {
        // Standard macOS system man pages.
        AddIfExists(paths, "/usr/share/man");

        // Homebrew — respect HOMEBREW_PREFIX when set, otherwise check both ARM and Intel defaults.
        string? brewPrefix = Environment.GetEnvironmentVariable("HOMEBREW_PREFIX");
        if (brewPrefix is not null)
        {
            AddIfExists(paths, Path.Combine(brewPrefix, "share", "man"));
        }
        else
        {
            // Apple Silicon default (/opt/homebrew) and Intel default (/usr/local).
            AddIfExists(paths, "/opt/homebrew/share/man");
            AddIfExists(paths, "/usr/local/share/man");
        }

        // Xcode Command Line Tools.
        AddIfExists(paths, "/Library/Developer/CommandLineTools/usr/share/man");
    }

    private static void AddLinuxPaths(List<string> paths)
    {
        AddIfExists(paths, "/usr/share/man");
        AddIfExists(paths, "/usr/local/share/man");
    }
}
