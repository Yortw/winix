#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Winix.Digest;

/// <summary>
/// Unix-only helper that returns a warning message when an HMAC key file is
/// readable by group or other (modes 0x40 = group read, 0x04 = other read).
/// No-op on Windows — ACLs are harder to check succinctly and DPAPI is the
/// better long-term answer there (see future <c>protect</c>/<c>unprotect</c> tool).
/// </summary>
public static class KeyFilePermissionsCheck
{
    /// <summary>Returns a warning message if the file is group/other readable; otherwise null.</summary>
    public static string? GetWarningOrNull(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        try
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0)
            {
                return null;
            }

            int bits = (int)mode & 0x1FF;
            string octal = Convert.ToString(bits, 8).PadLeft(3, '0');

            return $"digest: warning: {path} has mode 0{octal} and is readable by group/other.{Environment.NewLine}" +
                   $"        Consider 'chmod 0600 {path}'.";
        }
        catch
        {
            // If we can't read permissions, don't block the operation — just stay silent.
            return null;
        }
    }
}
