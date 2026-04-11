#nullable enable

namespace Winix.Man;

/// <summary>Configuration options for the terminal renderer.</summary>
public sealed class RendererOptions
{
    /// <summary>Override the rendering width. If null, uses terminal width capped at 80.</summary>
    public int? WidthOverride { get; init; }

    /// <summary>Whether to emit ANSI colour escape sequences.</summary>
    public bool Color { get; init; }

    /// <summary>Whether to emit OSC 8 hyperlinks for cross-references and URLs.</summary>
    public bool Hyperlinks { get; init; }
}
