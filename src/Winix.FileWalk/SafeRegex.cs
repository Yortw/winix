using System.Text.RegularExpressions;

namespace Winix.FileWalk;

/// <summary>
/// Convenience re-export of <see cref="Yort.ShellKit.SafeRegex"/> for use within the
/// FileWalk namespace. Delegates to the shared ShellKit implementation.
/// </summary>
public static class SafeRegex
{
    /// <inheritdoc cref="Yort.ShellKit.SafeRegex.Create"/>
    public static Regex Create(string pattern, RegexOptions options)
    {
        return Yort.ShellKit.SafeRegex.Create(pattern, options);
    }
}
