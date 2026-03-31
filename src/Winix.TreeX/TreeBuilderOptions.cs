using Winix.FileWalk;

namespace Winix.TreeX;

/// <summary>
/// Immutable configuration for <see cref="TreeBuilder"/>. Controls filtering, sorting,
/// and size computation during tree construction.
/// </summary>
/// <param name="GlobPatterns">Glob patterns to include. Empty means include all.</param>
/// <param name="RegexPatterns">Regex patterns to include. Empty means include all.</param>
/// <param name="TypeFilter">Restrict results to a specific entry type, or null for all types.</param>
/// <param name="MinSize">Minimum file size in bytes to include, or null for no lower bound.</param>
/// <param name="MaxSize">Maximum file size in bytes to include, or null for no upper bound.</param>
/// <param name="NewerThan">Include only entries modified after this timestamp, or null for no lower bound.</param>
/// <param name="OlderThan">Include only entries modified before this timestamp, or null for no upper bound.</param>
/// <param name="MaxDepth">Maximum directory depth to descend, or null for unlimited.</param>
/// <param name="IncludeHidden">When true, include hidden files and directories (dot-prefixed on Unix, Hidden attribute on Windows).</param>
/// <param name="UseGitIgnore">When true, honour .gitignore rules during traversal.</param>
/// <param name="CaseInsensitive">When true, apply glob and regex patterns case-insensitively.</param>
/// <param name="ComputeSizes">When true, roll up file sizes to parent directories after traversal.</param>
/// <param name="Sort">Sort order applied to each level of the tree.</param>
public sealed record TreeBuilderOptions(
    IReadOnlyList<string> GlobPatterns,
    IReadOnlyList<string> RegexPatterns,
    FileEntryType? TypeFilter,
    long? MinSize,
    long? MaxSize,
    DateTimeOffset? NewerThan,
    DateTimeOffset? OlderThan,
    int? MaxDepth,
    bool IncludeHidden,
    bool UseGitIgnore,
    bool CaseInsensitive,
    bool ComputeSizes,
    SortMode Sort);
