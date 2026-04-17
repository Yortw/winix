namespace Yort.ShellKit;

/// <summary>
/// Controls whether ANSI colour output is enabled.
/// </summary>
public enum ColorMode
{
    /// <summary>Enable colour output if stdout/stderr is a terminal and NO_COLOR is not set.</summary>
    Auto,

    /// <summary>Always emit ANSI colour escapes regardless of terminal detection or NO_COLOR.</summary>
    Always,

    /// <summary>Never emit ANSI colour escapes.</summary>
    Never,
}
