namespace Winix.Man;

/// <summary>
/// Base type for all blocks in the intermediate representation produced
/// by macro expansion and consumed by the renderer.
/// </summary>
public abstract record DocumentBlock;

/// <summary>
/// Title block from .TH macro. Rendered as the header/footer line.
/// </summary>
public sealed record TitleBlock(
    string Name, string Section, string Date, string Source, string Manual) : DocumentBlock;

/// <summary>
/// Section heading from .SH macro (e.g. NAME, SYNOPSIS, DESCRIPTION).
/// </summary>
public sealed record SectionHeading(string Text) : DocumentBlock;

/// <summary>
/// Subsection heading from .SS macro.
/// </summary>
public sealed record SubsectionHeading(string Text) : DocumentBlock;

/// <summary>
/// A paragraph of styled text from .PP/.P/.LP or plain text lines.
/// </summary>
public sealed record Paragraph(IReadOnlyList<StyledSpan> Content) : DocumentBlock;

/// <summary>
/// Tagged paragraph from .TP macro — a tag (typically a flag like -v)
/// followed by an indented body description.
/// </summary>
public sealed record TaggedParagraph(
    IReadOnlyList<StyledSpan> Tag, IReadOnlyList<StyledSpan> Body) : DocumentBlock;

/// <summary>
/// Indented paragraph from .IP macro, indented at the current level.
/// </summary>
public sealed record IndentedParagraph(
    IReadOnlyList<StyledSpan> Content, int Indent) : DocumentBlock;

/// <summary>
/// Preformatted (no-fill) block from .nf/.fi. Rendered without wrapping.
/// </summary>
public sealed record PreformattedBlock(string Text) : DocumentBlock;

/// <summary>
/// Vertical space from .sp macro.
/// </summary>
public sealed record VerticalSpace(int Lines) : DocumentBlock;
