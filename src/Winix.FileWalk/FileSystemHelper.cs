using System.Runtime.InteropServices;

namespace Winix.FileWalk;

/// <summary>
/// Shared file system helpers used by <see cref="FileWalker"/> and
/// <c>Winix.TreeX.TreeBuilder</c>.
/// </summary>
public static class FileSystemHelper
{
    /// <summary>Returns a forward-slash relative path from <paramref name="root"/> to <paramref name="fullPath"/>.</summary>
    public static string GetRelativePath(string root, string fullPath)
    {
        return Path.GetRelativePath(root, fullPath).Replace('\\', '/');
    }

    /// <summary>
    /// Checks if a file or directory is hidden. A dot-prefix in the name is always hidden.
    /// On Windows, <see cref="FileAttributes.Hidden"/> is also checked using the supplied
    /// <paramref name="attrs"/> (to avoid a redundant <see cref="File.GetAttributes"/> call),
    /// falling back to reading attributes if not supplied.
    /// </summary>
    public static bool IsHidden(string fullPath, string name, FileAttributes? attrs = null)
    {
        if (name.Length > 0 && name[0] == '.')
        {
            return true;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                FileAttributes a = attrs ?? File.GetAttributes(fullPath);
                if ((a & FileAttributes.Hidden) != 0)
                {
                    return true;
                }
            }
            catch
            {
                // Can't read attributes -- treat as not hidden
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a short display string for a <see cref="FileEntryType"/>:
    /// <c>file</c>, <c>dir</c>, or <c>link</c>.
    /// </summary>
    public static string FormatTypeString(FileEntryType type)
    {
        return type switch
        {
            FileEntryType.File => "file",
            FileEntryType.Directory => "dir",
            FileEntryType.Symlink => "link",
            _ => "file"
        };
    }
}
