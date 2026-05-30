#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Winix.Trash;

// Real device-id / mount-point lookup for the FreeDesktop backend. This interop is NOT unit-tested
// (it needs real mounts); the pure policy lives in MountResolver and is unit-tested, and this code
// is exercised by the Task 11 WSL integration tests.
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxFreeDesktopBackend
{
    // statx(2) constants. We follow symlinks (flags 0) so the device id matches the file's real
    // residence, matching the behaviour of the trash move (which moves the link target's data only
    // when the link is the thing being trashed — but here we only need the volume identity).
    private const int AT_FDCWD = unchecked((int)-100);
    private const uint STATX_BASIC_STATS = 0x000007ffU;

    // We deliberately declare the FULL fixed statx layout up to the dev fields. struct statx has a
    // kernel-defined, architecture-INDEPENDENT layout (unlike struct stat, whose glibc layout
    // differs across x86_64/aarch64), which is exactly why we use statx here. Field offsets below
    // are pinned by the kernel uapi (linux/stat.h).
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Statx
    {
        [FieldOffset(0)] public uint stx_mask;
        // stx_dev_major / stx_dev_minor are the device the file RESIDES on (not stx_rdev).
        [FieldOffset(128)] public uint stx_dev_major;
        [FieldOffset(132)] public uint stx_dev_minor;
    }

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int StatxNative(int dirfd, string pathname, int flags, uint mask, out Statx buf);

    [LibraryImport("libc", EntryPoint = "getuid")]
    private static partial uint GetUidNative();

    /// <summary>Returns the current process's real user id, used in the top-dir <c>.Trash-$uid</c> name.</summary>
    private static int CurrentUid()
    {
        return (int)GetUidNative();
    }

    /// <summary>Returns a packed device id for the volume a path resides on. The packing
    /// (<c>(major &lt;&lt; 32) | minor</c>) is lossless and only ever compared for equality — we never
    /// decode it back into major/minor — so an order-preserving pack is all that's required.</summary>
    /// <exception cref="IOException">statx failed (e.g. path vanished).</exception>
    private static ulong DeviceIdOf(string path)
    {
        // flags 0 → follow symlinks; STATX_BASIC_STATS asks for the standard fields incl. dev.
        if (StatxNative(AT_FDCWD, path, 0, STATX_BASIC_STATS, out Statx buf) != 0)
        {
            int err = Marshal.GetLastPInvokeError();
            // No framework ex.Message (would leak SR keys under InvariantGlobalization); errno only.
            throw new IOException($"statx failed for path (errno {err}).");
        }

        return ((ulong)buf.stx_dev_major << 32) | buf.stx_dev_minor;
    }

    /// <summary>Walks up from a file to the directory at which the device id changes — that boundary
    /// is the mount point top-dir. Returns "/" if the walk reaches the root.</summary>
    private static string? MountPointOf(string path)
    {
        try
        {
            string current = Path.GetFullPath(path);
            ulong dev = DeviceIdOf(current);

            while (true)
            {
                string? parent = Path.GetDirectoryName(current.TrimEnd('/'));
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
                {
                    return "/";
                }

                ulong parentDev = DeviceIdOf(parent);
                if (parentDev != dev)
                {
                    // The device changed crossing into `parent`, so `current` is the mount top-dir.
                    return current;
                }

                current = parent;
            }
        }
        catch (IOException)
        {
            return null;
        }
    }
}
