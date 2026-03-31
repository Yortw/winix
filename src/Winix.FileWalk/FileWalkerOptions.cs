#nullable enable

namespace Winix.FileWalk;

/// <summary>
/// Immutable configuration for <see cref="FileWalker"/>. All filtering predicates and
/// behaviour flags are set here before walking begins.
/// </summary>
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
