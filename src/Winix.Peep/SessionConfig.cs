using System.Text.RegularExpressions;

namespace Winix.Peep;

/// <summary>
/// Immutable configuration for an interactive peep session. Constructed from parsed
/// command-line arguments by the entry point, consumed by <see cref="InteractiveSession"/>.
/// </summary>
/// <param name="Command">Executable name or path to run.</param>
/// <param name="CommandArgs">Arguments to pass to the command.</param>
/// <param name="CommandDisplay">Human-readable display string for the command + args.</param>
/// <param name="IntervalSeconds">Seconds between automatic re-runs (when <paramref name="UseInterval"/> is true).</param>
/// <param name="UseInterval">Whether to re-run the command on a timed interval.</param>
/// <param name="WatchPatterns">Glob patterns for file-change watching. Empty = no file watching.</param>
/// <param name="DebounceMs">Milliseconds to debounce file-change events before triggering a re-run.</param>
/// <param name="HistoryCapacity">Maximum number of run snapshots to keep in the history ring buffer.</param>
/// <param name="NoGitIgnore">When true, disables gitignore filtering for file-change events.</param>
/// <param name="ExitOnChange">Exit automatically when command output changes from the previous run.</param>
/// <param name="ExitOnSuccess">Exit automatically when the command exits with code 0.</param>
/// <param name="ExitOnError">Exit automatically when the command exits with a non-zero code.</param>
/// <param name="ExitOnMatchRegexes">Exit when command output matches any of these regexes.</param>
/// <param name="DiffEnabled">Whether to show a diff between consecutive runs.</param>
/// <param name="NoHeader">Suppress the header/status bar in the display.</param>
/// <param name="JsonOutput">Emit JSON output on exit instead of human-readable text.</param>
/// <param name="JsonOutputIncludeOutput">Include the command's stdout in the JSON exit output.</param>
/// <param name="UseColor">Whether to use ANSI colour codes in output.</param>
/// <param name="Version">Tool version string for JSON output.</param>
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
