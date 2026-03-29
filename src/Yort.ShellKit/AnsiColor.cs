namespace Yort.ShellKit;

/// <summary>
/// Minimal ANSI colour helpers. Returns escape sequences when colour is enabled, empty strings otherwise.
/// </summary>
public static class AnsiColor
{
    /// <summary>Dim/faint text (ANSI SGR 2).</summary>
    public static string Dim(bool enabled) => enabled ? "\x1b[2m" : "";

    /// <summary>Green text (ANSI SGR 32).</summary>
    public static string Green(bool enabled) => enabled ? "\x1b[32m" : "";

    /// <summary>Red text (ANSI SGR 31).</summary>
    public static string Red(bool enabled) => enabled ? "\x1b[31m" : "";

    /// <summary>Reset all attributes (ANSI SGR 0).</summary>
    public static string Reset(bool enabled) => enabled ? "\x1b[0m" : "";
}
