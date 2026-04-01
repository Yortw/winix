using System.Text.RegularExpressions;

namespace Yort.ShellKit;

/// <summary>
/// Creates <see cref="Regex"/> instances that are safe against catastrophic backtracking (ReDoS).
/// Attempts <see cref="RegexOptions.NonBacktracking"/> first (linear-time guarantee);
/// falls back to the standard engine with a match timeout when the pattern uses features
/// incompatible with the non-backtracking engine (backreferences, lookahead/lookbehind, atomic groups).
/// </summary>
public static class SafeRegex
{
    private static readonly TimeSpan FallbackTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Creates a regex with ReDoS protection. Tries <see cref="RegexOptions.NonBacktracking"/>
    /// first; falls back to a standard regex with a 2-second match timeout if the pattern
    /// uses features that require backtracking.
    /// </summary>
    /// <param name="pattern">The regular expression pattern.</param>
    /// <param name="options">
    /// Regex options to apply. <see cref="RegexOptions.NonBacktracking"/> is added automatically
    /// and should not be included by the caller.
    /// </param>
    /// <returns>A compiled <see cref="Regex"/> instance.</returns>
    /// <exception cref="ArgumentException">The pattern is not a valid regular expression.</exception>
    public static Regex Create(string pattern, RegexOptions options)
    {
        try
        {
            return new Regex(pattern, options | RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            // Pattern uses features incompatible with NonBacktracking (backreferences,
            // lookahead/lookbehind, atomic groups). Fall back to standard engine with
            // a timeout to prevent catastrophic backtracking.
            return new Regex(pattern, options, FallbackTimeout);
        }
    }
}
