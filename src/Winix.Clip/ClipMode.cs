namespace Winix.Clip;

/// <summary>
/// The action <c>clip</c> will perform this invocation.
/// </summary>
public enum ClipMode
{
    /// <summary>Read stdin and write it to the clipboard.</summary>
    Copy,

    /// <summary>Read the clipboard and write it to stdout.</summary>
    Paste,

    /// <summary>Empty the clipboard.</summary>
    Clear,
}
