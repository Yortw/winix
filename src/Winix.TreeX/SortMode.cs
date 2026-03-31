#nullable enable

namespace Winix.TreeX;

/// <summary>Sort order for tree entries.</summary>
public enum SortMode
{
    /// <summary>Alphabetical, directories first (default).</summary>
    Name,

    /// <summary>Largest first, directories first.</summary>
    Size,

    /// <summary>Newest first, directories first.</summary>
    Modified
}
