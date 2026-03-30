using System.Text.RegularExpressions;

namespace Winix.Peep;

/// <summary>
/// Immutable configuration for an interactive peep session. Constructed from parsed
/// command-line arguments by the entry point, consumed by <see cref="InteractiveSession"/>.
/// </summary>
public sealed record SessionConfig(
    string Command,
    string[] CommandArgs,
    string CommandDisplay,
    double IntervalSeconds,
    bool UseInterval,
    string[] WatchPatterns,
    int DebounceMs,
    int HistoryCapacity,
    bool NoGitIgnore,
    bool ExitOnChange,
    bool ExitOnSuccess,
    bool ExitOnError,
    Regex[] ExitOnMatchRegexes,
    bool DiffEnabled,
    bool NoHeader,
    bool JsonOutput,
    bool JsonOutputIncludeOutput,
    bool UseColor,
    string Version);
