#nullable enable

namespace Winix.FileWalk;

/// <summary>The type of a file system entry.</summary>
public enum FileEntryType
{
    /// <summary>A regular file.</summary>
    File,

    /// <summary>A directory.</summary>
    Directory,

    /// <summary>A symbolic link.</summary>
    Symlink
}
