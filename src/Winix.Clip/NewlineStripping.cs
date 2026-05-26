namespace Winix.Clip;

/// <summary>
/// Removes exactly one trailing newline from paste output by default.
/// Mirrors shell command-substitution semantics (<c>$(...)</c>) so piping clip's
/// output into another command does not introduce phantom blank lines.
/// </summary>
public static class NewlineStripping
{
    /// <summary>
    /// Returns <paramref name="value"/> with one trailing <c>\n</c> or <c>\r\n</c> removed,
    /// if present. Internal newlines are preserved. Passing <c>null</c> returns <c>null</c>.
    /// </summary>
    public static string? StripTrailingNewline(string? value)
    {
        if (value is null || value.Length == 0)
        {
            return value;
        }

        if (value.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return value.Substring(0, value.Length - 2);
        }

        if (value[value.Length - 1] == '\n')
        {
            return value.Substring(0, value.Length - 1);
        }

        return value;
    }
}
