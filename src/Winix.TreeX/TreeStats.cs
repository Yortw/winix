namespace Winix.TreeX;

/// <summary>Summary statistics from a tree rendering pass.</summary>
/// <param name="DirectoryCount">Number of directories rendered (excluding root).</param>
/// <param name="FileCount">Number of files rendered.</param>
/// <param name="TotalSizeBytes">Total size of all files. -1 if sizes were not computed.</param>
public sealed record TreeStats(int DirectoryCount, int FileCount, long TotalSizeBytes);
