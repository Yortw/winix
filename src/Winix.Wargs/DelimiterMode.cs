namespace Winix.Wargs;

/// <summary>
/// How the input stream is split into items.
/// </summary>
public enum DelimiterMode
{
    /// <summary>Split on newlines. Empty and whitespace-only lines are skipped.</summary>
    Line,

    /// <summary>Split on null characters (\0). For use with find -print0.</summary>
    Null,

    /// <summary>Split on a user-specified single character.</summary>
    Custom,

    /// <summary>Split on whitespace runs with POSIX quote handling. Enabled by --compat.</summary>
    Whitespace
}
