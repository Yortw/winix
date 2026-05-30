#nullable enable
using System;

namespace Winix.Trash;

/// <summary>Pure resolution logic for the FreeDesktop Trash spec's per-volume trash location.
/// Device lookup and mount-point detection are injected as delegates so the policy is fully
/// unit-testable with no real syscalls; the real syscall-backed delegates live in
/// <see cref="LinuxFreeDesktopBackend"/>.</summary>
internal static class MountResolver
{
    /// <summary>Returns the home-volume trash directory: <c>$XDG_DATA_HOME/Trash</c> when
    /// <c>XDG_DATA_HOME</c> is set and non-empty, otherwise <c>~/.local/share/Trash</c>.</summary>
    public static string HomeTrashDir()
    {
        // These are Linux filesystem paths and must always use '/' — Path.Combine would emit '\'
        // when this code is exercised on a Windows test host, so we join explicitly.
        string? xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdg))
        {
            return Join(xdg, "Trash");
        }

        // $HOME first (matches the spec's UserProfile on *nix); GetFolderPath as a fallback.
        string home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Join(home, ".local/share/Trash");
    }

    /// <summary>Resolves which trash directory a file should be sent to under the FreeDesktop spec.</summary>
    /// <param name="filePath">Absolute path of the file being trashed.</param>
    /// <param name="deviceIdOf">Returns the (packed) device id for a path. Injected for testability;
    /// the real implementation uses <c>statx</c>. Ids are only ever compared for equality.</param>
    /// <param name="homeTrashDir">The home-volume trash dir (see <see cref="HomeTrashDir"/>).</param>
    /// <param name="homeDeviceId">Device id of the home trash dir's volume.</param>
    /// <param name="uid">The current user's uid, used in the top-dir <c>.Trash-$uid</c> name.</param>
    /// <param name="mountPointOf">Returns the mount-point top-dir for a path, or null if undetermined.</param>
    /// <returns>The home trash dir for same-device files, else <c>&lt;topdir&gt;/.Trash-&lt;uid&gt;</c>.</returns>
    public static string ResolveTrashDir(
        string filePath,
        Func<string, ulong> deviceIdOf,
        string homeTrashDir,
        ulong homeDeviceId,
        int uid,
        Func<string, string?> mountPointOf)
    {
        if (deviceIdOf(filePath) == homeDeviceId)
        {
            return homeTrashDir;
        }

        // Cross-volume: the file's own volume must hold the trash so the move stays a rename.
        // We use the v1-recommended per-user top-dir form `$topdir/.Trash-$uid` rather than the
        // admin `$topdir/.Trash/$uid` sticky-bit form — it needs no pre-existing admin dir and is
        // the simplest/safest to create on demand.
        string topDir = mountPointOf(filePath) ?? "/";
        return Join(topDir, $".Trash-{uid}");
    }

    /// <summary>Joins two Linux path segments with a single '/' separator, regardless of host OS.</summary>
    private static string Join(string left, string right)
    {
        if (left.EndsWith('/'))
        {
            return left + right;
        }

        return left + "/" + right;
    }
}
