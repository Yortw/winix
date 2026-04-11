#nullable enable

namespace Winix.Man;

/// <summary>
/// Base type for all tokens produced by the groff lexer.
/// Each token represents one line of input.
/// </summary>
public abstract record GroffToken;

/// <summary>
/// A macro invocation line — a line beginning with '.' or '''.
/// The comment marker <c>.\</c>" is handled separately as <see cref="CommentToken"/>.
/// </summary>
/// <param name="MacroName">The macro name, e.g. "SH", "PP", "TH".</param>
/// <param name="Arguments">
/// The remainder of the line after the macro name and any whitespace separator.
/// Empty string if the macro has no arguments.
/// Quoted strings within the arguments are preserved as-is — the lexer does
/// not split or unquote them.
/// </param>
public sealed record RequestToken(string MacroName, string Arguments) : GroffToken;

/// <summary>
/// A line of body text that may contain inline escape sequences.
/// Inline escapes (e.g. <c>\fB</c>, <c>\fI</c>) are not interpreted at this stage —
/// that is the macro expander's responsibility.
/// An empty line is represented as a <see cref="TextLineToken"/> with
/// <see cref="Text"/> set to the empty string.
/// </summary>
/// <param name="Text">The raw text of the line.</param>
public sealed record TextLineToken(string Text) : GroffToken;

/// <summary>
/// A groff comment line — a line whose first three characters are <c>.\</c>".
/// The comment content (after the opening marker and optional space) is captured
/// but not interpreted.
/// </summary>
/// <param name="Text">The comment text, not including the <c>.\</c>" prefix.</param>
public sealed record CommentToken(string Text) : GroffToken;
