using Winix.FileWalk;

namespace Winix.TreeX;

/// <summary>
/// A node in the in-memory directory tree. Built by <see cref="TreeBuilder"/>,
/// consumed by <see cref="TreeRenderer"/>.
/// </summary>
public sealed class TreeNode
{
    /// <summary>Entry name (filename or directory name).</summary>
    public required string Name { get; init; }

    /// <summary>Full absolute path to this entry.</summary>
    public required string FullPath { get; init; }

    /// <summary>File, directory, or symlink.</summary>
    public required FileEntryType Type { get; init; }

    /// <summary>
    /// File size in bytes. For directories, -1 initially; set to sum of descendants
    /// during size rollup. For files, the actual file size.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>Last modified timestamp.</summary>
    public DateTimeOffset Modified { get; init; }

    /// <summary>True if the file is executable (Unix permission or Windows extension).</summary>
    public bool IsExecutable { get; init; }

    /// <summary>
    /// True if this entry directly matches the active filters. False for ancestor
    /// directories kept only to show the path to matching descendants.
    /// When no filters are active, all entries have IsMatch = true.
    /// </summary>
    public bool IsMatch { get; set; } = true;

    /// <summary>Child nodes. Empty for files. Sorted by TreeBuilder.</summary>
    public List<TreeNode> Children { get; } = new();

    /// <summary>
    /// True when this directory could not be enumerated (permission denied, vanished,
    /// I/O error). Used by the renderer to print <c>[error opening dir]</c> per
    /// <c>tree(1)</c> precedent. Round-1 fresh-eyes 2026-05-09 silent-failure-hunter C1
    /// fix: pre-fix the silent-return on enumeration failure produced an apparently-empty
    /// directory, indistinguishable from a legitimately-empty one.
    /// </summary>
    public bool IsUnreadable { get; set; }
}
