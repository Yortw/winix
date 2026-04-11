namespace Winix.Man;

/// <summary>
/// A span of text with a font style. The building block for inline content
/// within paragraphs, headings, and tagged items.
/// </summary>
/// <param name="Text">The text content.</param>
/// <param name="Style">The font style to apply.</param>
public sealed record StyledSpan(string Text, FontStyle Style);
