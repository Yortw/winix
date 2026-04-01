namespace Winix.FileWalk;

/// <summary>
/// Immutable configuration for <see cref="FileWalker"/>. All filtering predicates and
/// behaviour flags are set here before walking begins.
/// </summary>
/// <param name="GlobPatterns">Glob patterns to filter files by name (OR logic). Empty = no glob filter.</param>
/// <param name="RegexPatterns">Regex patterns to filter files by name (OR logic). Empty = no regex filter.</param>
/// <param name="TypeFilter">Restrict results to a specific entry type, or null for all types.</param>
/// <param name="TextOnly">True = only text files, false = only binary files, null = no content filter.</param>
/// <param name="MinSize">Minimum file size in bytes (inclusive), or null for no minimum.</param>
/// <param name="MaxSize">Maximum file size in bytes (inclusive), or null for no maximum.</param>
/// <param name="NewerThan">Only include files modified after this time, or null for no minimum age.</param>
/// <param name="OlderThan">Only include files modified before this time, or null for no maximum age.</param>
/// <param name="MaxDepth">Maximum directory recursion depth, or null for unlimited.</param>
/// <param name="IncludeHidden">Whether to include hidden files and directories.</param>
/// <param name="FollowSymlinks">Whether to follow symbolic links into target directories.</param>
/// <param name="UseGitIgnore">Whether gitignore filtering is enabled (caller provides the predicate).</param>
/// <param name="AbsolutePaths">Whether to output absolute paths instead of relative.</param>
/// <param name="CaseInsensitive">Whether glob and regex matching is case-insensitive.</param>
public sealed record FileWalkerOptions(
    IReadOnlyList<string> GlobPatterns,
    IReadOnlyList<string> RegexPatterns,
    FileEntryType? TypeFilter,
    bool? TextOnly,
    long? MinSize,
    long? MaxSize,
    DateTimeOffset? NewerThan,
    DateTimeOffset? OlderThan,
    int? MaxDepth,
    bool IncludeHidden,
    bool FollowSymlinks,
    bool UseGitIgnore,
    bool AbsolutePaths,
    bool CaseInsensitive);
