namespace Winix.Clip;

/// <summary>
/// Parsed command-line options for <c>clip</c>. Invalid combinations are rejected
/// at construction time.
/// </summary>
public sealed class ClipOptions
{
    /// <summary>
    /// Constructs a new <see cref="ClipOptions"/>. Throws <see cref="ArgumentException"/>
    /// on conflicting flag combinations.
    /// </summary>
    public ClipOptions(
        bool forceCopy = false,
        bool forcePaste = false,
        bool clear = false,
        bool raw = false,
        bool primary = false)
    {
        if (forceCopy && forcePaste)
        {
            throw new ArgumentException("--copy and --paste cannot be combined.");
        }

        if (clear && forceCopy)
        {
            throw new ArgumentException("--clear and --copy cannot be combined.");
        }

        if (clear && forcePaste)
        {
            throw new ArgumentException("--clear and --paste cannot be combined.");
        }

        if (clear && raw)
        {
            throw new ArgumentException("--raw only applies when pasting; --clear does not paste.");
        }

        ForceCopy = forceCopy;
        ForcePaste = forcePaste;
        Clear = clear;
        Raw = raw;
        Primary = primary;
    }

    /// <summary>True if the user passed <c>-c</c> / <c>--copy</c>.</summary>
    public bool ForceCopy { get; }

    /// <summary>True if the user passed <c>-p</c> / <c>--paste</c>.</summary>
    public bool ForcePaste { get; }

    /// <summary>True if the user passed <c>--clear</c>.</summary>
    public bool Clear { get; }

    /// <summary>True if the user passed <c>-r</c> / <c>--raw</c> (preserve trailing newline on paste).</summary>
    public bool Raw { get; }

    /// <summary>True if the user passed <c>--primary</c> (Linux primary selection; ignored on other OSes).</summary>
    public bool Primary { get; }
}
