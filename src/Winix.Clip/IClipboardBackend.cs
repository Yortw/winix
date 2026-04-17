namespace Winix.Clip;

/// <summary>
/// Platform-neutral clipboard abstraction. Implementations are constructed by
/// <see cref="ClipboardBackendFactory"/> based on the current OS and available
/// helper binaries.
/// </summary>
public interface IClipboardBackend
{
    /// <summary>
    /// Writes <paramref name="text"/> to the clipboard.
    /// Throws <see cref="ClipboardException"/> on backend failure.
    /// </summary>
    void CopyText(string text);

    /// <summary>
    /// Returns the clipboard contents as text. Returns an empty string if the
    /// clipboard is empty or contains no text representation.
    /// Throws <see cref="ClipboardException"/> on backend failure.
    /// </summary>
    string PasteText();

    /// <summary>
    /// Empties the clipboard. Throws <see cref="ClipboardException"/> on backend failure.
    /// </summary>
    void Clear();
}

/// <summary>
/// Backend-layer failure (helper non-zero exit, Win32 API failure, etc.).
/// Console app maps to an appropriate exit code.
/// </summary>
public sealed class ClipboardException : Exception
{
    /// <summary>Constructs a new <see cref="ClipboardException"/>.</summary>
    public ClipboardException(string message) : base(message) { }

    /// <summary>Constructs a new <see cref="ClipboardException"/> with an inner cause.</summary>
    public ClipboardException(string message, Exception inner) : base(message, inner) { }
}
