namespace Winix.Squeeze;

/// <summary>
/// Minimal ANSI colour helpers. Returns escape sequences when colour is enabled, empty strings otherwise.
/// </summary>
internal static class AnsiColor
{
    internal static string Dim(bool enabled) => enabled ? "\x1b[2m" : "";
    internal static string Green(bool enabled) => enabled ? "\x1b[32m" : "";
    internal static string Red(bool enabled) => enabled ? "\x1b[31m" : "";
    internal static string Reset(bool enabled) => enabled ? "\x1b[0m" : "";
}
