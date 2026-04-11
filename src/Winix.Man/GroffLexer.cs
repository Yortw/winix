#nullable enable

namespace Winix.Man;

/// <summary>
/// Stage 1 of the groff pipeline: tokenises raw groff/man source into a
/// sequence of <see cref="GroffToken"/> values, one per input line.
/// The lexer only classifies lines — it does not interpret macro semantics,
/// inline escape sequences, or argument quoting.  That is the macro
/// expander's responsibility (Task 4).
/// </summary>
public sealed class GroffLexer
{
    /// <summary>
    /// Tokenises the supplied groff source text, yielding one token per line.
    /// Lines are delimited by the platform-independent <see cref="StringReader"/>
    /// rules (<c>\n</c>, <c>\r\n</c>, or <c>\r</c>).
    /// </summary>
    /// <param name="source">The raw groff/man page text to tokenise.</param>
    /// <returns>
    /// A sequence of <see cref="GroffToken"/> values, one per line.
    /// The sequence is lazily evaluated via <c>yield return</c>.
    /// </returns>
    public IEnumerable<GroffToken> Tokenise(string source)
    {
        using (var reader = new StringReader(source))
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                yield return TokeniseLine(line);
            }
        }
    }

    /// <summary>
    /// Classifies a single line as a request, comment, or text token.
    /// </summary>
    private static GroffToken TokeniseLine(string line)
    {
        if (line.Length == 0)
        {
            return new TextLineToken("");
        }

        if (line[0] == '.' || line[0] == '\'')
        {
            return ParseRequest(line);
        }

        return new TextLineToken(line);
    }

    /// <summary>
    /// Parses a request line (one beginning with '.' or ''') into either a
    /// <see cref="RequestToken"/> or a <see cref="CommentToken"/>.
    /// The control character itself is consumed and not included in the result.
    /// </summary>
    private static GroffToken ParseRequest(string line)
    {
        // Comment lines begin with ." — the backslash-quote combination signals
        // a groff comment regardless of what follows.
        if (line.Length >= 3 && line[0] == '.' && line[1] == '\\' && line[2] == '"')
        {
            // Comment text starts after ." and an optional single space.
            string commentText = line.Length > 4 ? line.Substring(4) : "";
            return new CommentToken(commentText);
        }

        // Skip the control character ('.' or ''').
        int pos = 1;

        // Skip any whitespace between the control character and the macro name
        // (unusual but permitted by groff).
        while (pos < line.Length && line[pos] == ' ')
        {
            pos++;
        }

        int nameStart = pos;

        // The macro name ends at the first whitespace character.
        while (pos < line.Length && line[pos] != ' ' && line[pos] != '\t')
        {
            pos++;
        }

        // A line with only the control character and whitespace is treated as
        // a plain text line — it cannot be a valid request.
        if (pos == nameStart)
        {
            return new TextLineToken(line);
        }

        string macroName = line.Substring(nameStart, pos - nameStart);

        // Skip the whitespace separator between macro name and arguments.
        while (pos < line.Length && (line[pos] == ' ' || line[pos] == '\t'))
        {
            pos++;
        }

        string arguments = pos < line.Length ? line.Substring(pos) : "";

        return new RequestToken(macroName, arguments);
    }
}
