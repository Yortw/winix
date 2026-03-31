#nullable enable

namespace Winix.FileWalk;

/// <summary>
/// Represents a file system entry found during directory walking.
/// Immutable — produced by <see cref="FileWalker"/> and consumed by formatters.
/// </summary>
/// <param name="Path">Relative or absolute path (determined by walker options).</param>
/// <param name="Name">Filename only (no directory component).</param>
/// <param name="Type">File, directory, or symlink.</param>
/// <param name="SizeBytes">File size in bytes. -1 for directories.</param>
/// <param name="Modified">Last modified timestamp.</param>
/// <param name="Depth">Depth relative to the search root (root entries are depth 0).</param>
/// <param name="IsText">True if text, false if binary. Null unless --text/--binary detection was requested.</param>
public sealed record FileEntry(
    string Path,
    string Name,
    FileEntryType Type,
    long SizeBytes,
    DateTimeOffset Modified,
    int Depth,
    bool? IsText);
