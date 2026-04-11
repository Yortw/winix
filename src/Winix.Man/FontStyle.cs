namespace Winix.Man;

/// <summary>
/// Font style flags for styled text spans in the intermediate representation.
/// </summary>
[Flags]
public enum FontStyle
{
    /// <summary>Normal (roman) text — no styling.</summary>
    Roman = 0,

    /// <summary>Bold text.</summary>
    Bold = 1,

    /// <summary>Italic text (typically rendered as underline in terminals).</summary>
    Italic = 2
}
