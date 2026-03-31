#nullable enable

namespace Winix.TreeX;

/// <summary>Controls how the tree is rendered to text output.</summary>
/// <param name="UseColor">When true, apply ANSI colour codes to output (respects NO_COLOR).</param>
/// <param name="UseLinks">When true, emit OSC 8 hyperlinks for each entry path.</param>
/// <param name="ShowSize">When true, display file sizes alongside each entry.</param>
/// <param name="ShowDate">When true, display the last-modified timestamp alongside each entry.</param>
/// <param name="DirsOnly">When true, render only directory nodes and omit files.</param>
public sealed record TreeRenderOptions(
    bool UseColor,
    bool UseLinks,
    bool ShowSize,
    bool ShowDate,
    bool DirsOnly);
