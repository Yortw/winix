#nullable enable
namespace Winix.Qr;

/// <summary>Output rendering format for the generated QR code.</summary>
public enum OutputFormat
{
    /// <summary>Resolved at output time: unicode on TTY, SVG when stdout is redirected.</summary>
    Auto,
    /// <summary>Terminal unicode half-block art.</summary>
    Unicode,
    /// <summary>Two-char-wide ASCII full-block (<c>##</c>/spaces).</summary>
    Ascii,
    /// <summary>SVG text.</summary>
    Svg,
    /// <summary>PNG bytes.</summary>
    Png,
}
