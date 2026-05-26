namespace Winix.Clip;

/// <summary>
/// Predefined <see cref="ClipboardHelperSet"/> instances for each supported helper.
/// </summary>
public static class HelperSets
{
    /// <summary>wl-copy / wl-paste (Wayland).</summary>
    public static ClipboardHelperSet WlClipboard { get; } = new(
        Name: "wl-clipboard",
        CopyBinary: "wl-copy",
        CopyArgs: Array.Empty<string>(),
        PasteBinary: "wl-paste",
        // --no-newline so our own NewlineStripping is the single source of truth.
        PasteArgs: new[] { "--no-newline" },
        ClearBinary: "wl-copy",
        ClearArgs: new[] { "--clear" },
        ClearUsesEmptyStdin: false);

    /// <summary>xclip targeting the CLIPBOARD selection.</summary>
    public static ClipboardHelperSet XClip { get; } = new(
        Name: "xclip",
        CopyBinary: "xclip",
        CopyArgs: new[] { "-selection", "clipboard", "-i" },
        PasteBinary: "xclip",
        PasteArgs: new[] { "-selection", "clipboard", "-o" },
        ClearBinary: "xclip",
        ClearArgs: new[] { "-selection", "clipboard", "-i" },
        ClearUsesEmptyStdin: true);

    /// <summary>xsel targeting the CLIPBOARD selection.</summary>
    public static ClipboardHelperSet XSel { get; } = new(
        Name: "xsel",
        CopyBinary: "xsel",
        CopyArgs: new[] { "--clipboard", "--input" },
        PasteBinary: "xsel",
        PasteArgs: new[] { "--clipboard", "--output" },
        ClearBinary: "xsel",
        ClearArgs: new[] { "--clipboard", "--clear" },
        ClearUsesEmptyStdin: false);

    /// <summary>pbcopy / pbpaste (macOS).</summary>
    public static ClipboardHelperSet Pb { get; } = new(
        Name: "pb",
        CopyBinary: "pbcopy",
        CopyArgs: Array.Empty<string>(),
        PasteBinary: "pbpaste",
        PasteArgs: Array.Empty<string>(),
        ClearBinary: "pbcopy",
        ClearArgs: Array.Empty<string>(),
        ClearUsesEmptyStdin: true);
}
