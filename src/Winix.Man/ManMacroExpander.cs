#nullable enable

using System.Text;

namespace Winix.Man;

/// <summary>
/// Stage 2 of the groff pipeline: expands a stream of <see cref="GroffToken"/>
/// values into a sequence of <see cref="DocumentBlock"/> values that form the
/// intermediate representation consumed by the renderer.
/// <para>
/// Handles man-page macros (.TH, .SH, .SS, .PP, .TP, .IP, .B, .I, .BR, etc.),
/// no-fill mode (.nf/.fi), indent tracking (.RS/.RE), and inline escape
/// sequences (\fB, \fI, \fR, \-, \(xx, etc.).
/// </para>
/// </summary>
public sealed class ManMacroExpander
{
    /// <summary>
    /// Expands the token stream into document blocks.
    /// </summary>
    /// <param name="tokens">Tokens produced by <see cref="GroffLexer.Tokenise"/>.</param>
    /// <returns>An ordered list of document blocks representing the man page structure.</returns>
    public IReadOnlyList<DocumentBlock> Expand(IEnumerable<GroffToken> tokens)
    {
        var blocks = new List<DocumentBlock>();
        var currentSpans = new List<StyledSpan>();
        int indentLevel = 0;
        bool pendingIndent = false;
        int pendingIndentValue = 0;
        List<StyledSpan>? pendingIndentMarker = null;
        bool inNoFill = false;
        var noFillLines = new List<string>();
        bool expectingTag = false;
        List<StyledSpan>? tagSpans = null;
        bool collectingTagBody = false;
        var tagBodySpans = new List<StyledSpan>();

        foreach (var token in tokens)
        {
            if (token is CommentToken)
            {
                continue;
            }

            if (inNoFill)
            {
                if (token is RequestToken req && req.MacroName == "fi")
                {
                    inNoFill = false;
                    blocks.Add(new PreformattedBlock(string.Join("\n", noFillLines)));
                    noFillLines.Clear();
                }
                else
                {
                    string line = token switch
                    {
                        TextLineToken t => t.Text,
                        RequestToken r => "." + r.MacroName + (r.Arguments.Length > 0 ? " " + r.Arguments : ""),
                        _ => ""
                    };
                    noFillLines.Add(line);
                }
                continue;
            }

            if (token is TextLineToken textLine)
            {
                if (expectingTag)
                {
                    // This text line is the tag for a .TP
                    tagSpans = ParseInlineContent(textLine.Text);
                    expectingTag = false;
                    collectingTagBody = true;
                    tagBodySpans.Clear();
                    continue;
                }

                if (collectingTagBody)
                {
                    if (textLine.Text.Length == 0)
                    {
                        // Empty line ends body collection — but still add it
                        continue;
                    }
                    var bodyLine = ParseInlineContent(textLine.Text);
                    if (tagBodySpans.Count > 0)
                    {
                        tagBodySpans.Add(new StyledSpan(" ", FontStyle.Roman));
                    }
                    tagBodySpans.AddRange(bodyLine);
                    continue;
                }

                if (textLine.Text.Length == 0)
                {
                    // Empty line acts like .PP
                    FlushParagraph(blocks, currentSpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);
                    currentSpans.Clear();
                    pendingIndent = false;
                    pendingIndentMarker = null;
                    continue;
                }

                var spans = ParseInlineContent(textLine.Text);
                if (currentSpans.Count > 0)
                {
                    currentSpans.Add(new StyledSpan(" ", FontStyle.Roman));
                }
                currentSpans.AddRange(spans);
                continue;
            }

            if (token is RequestToken request)
            {
                switch (request.MacroName)
                {
                    case "TH":
                    {
                        FlushAll(blocks, currentSpans, ref collectingTagBody, tagSpans, tagBodySpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);
                        pendingIndent = false;
                        pendingIndentMarker = null;
                        var args = SplitArguments(request.Arguments);
                        blocks.Add(new TitleBlock(
                            args.Count > 0 ? Unquote(args[0]) : "",
                            args.Count > 1 ? Unquote(args[1]) : "",
                            args.Count > 2 ? Unquote(args[2]) : "",
                            args.Count > 3 ? Unquote(args[3]) : "",
                            args.Count > 4 ? Unquote(args[4]) : ""));
                        break;
                    }

                    case "SH":
                    {
                        FlushAll(blocks, currentSpans, ref collectingTagBody, tagSpans, tagBodySpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);
                        pendingIndent = false;
                        pendingIndentMarker = null;
                        blocks.Add(new SectionHeading(Unquote(request.Arguments)));
                        break;
                    }

                    case "SS":
                    {
                        FlushAll(blocks, currentSpans, ref collectingTagBody, tagSpans, tagBodySpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);
                        pendingIndent = false;
                        pendingIndentMarker = null;
                        blocks.Add(new SubsectionHeading(Unquote(request.Arguments)));
                        break;
                    }

                    case "PP":
                    case "P":
                    case "LP":
                    {
                        FlushAll(blocks, currentSpans, ref collectingTagBody, tagSpans, tagBodySpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);
                        pendingIndent = false;
                        pendingIndentMarker = null;
                        break;
                    }

                    case "TP":
                    {
                        FlushAll(blocks, currentSpans, ref collectingTagBody, tagSpans, tagBodySpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);
                        pendingIndent = false;
                        pendingIndentMarker = null;
                        expectingTag = true;
                        tagSpans = null;
                        tagBodySpans.Clear();
                        collectingTagBody = false;
                        break;
                    }

                    case "IP":
                    {
                        FlushAll(blocks, currentSpans, ref collectingTagBody, tagSpans, tagBodySpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);
                        pendingIndent = true;
                        pendingIndentMarker = null;
                        var ipArgs = SplitArguments(request.Arguments);
                        if (ipArgs.Count >= 2)
                        {
                            int.TryParse(Unquote(ipArgs[ipArgs.Count - 1]), out pendingIndentValue);
                        }
                        if (ipArgs.Count >= 1)
                        {
                            string marker = Unquote(ipArgs[0]);
                            // Resolve escape sequences in the marker
                            var markerSpans = ParseInlineContent(marker);
                            if (markerSpans.Count > 0)
                            {
                                pendingIndentMarker = new List<StyledSpan>(markerSpans);
                            }
                        }
                        break;
                    }

                    case "RS":
                    {
                        FlushAll(blocks, currentSpans, ref collectingTagBody, tagSpans, tagBodySpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);
                        pendingIndent = false;
                        pendingIndentMarker = null;
                        indentLevel++;
                        break;
                    }

                    case "RE":
                    {
                        FlushAll(blocks, currentSpans, ref collectingTagBody, tagSpans, tagBodySpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);
                        pendingIndent = false;
                        pendingIndentMarker = null;
                        if (indentLevel > 0)
                        {
                            indentLevel--;
                        }
                        break;
                    }

                    case "B":
                    {
                        FlushAll(blocks, currentSpans, ref collectingTagBody, tagSpans, tagBodySpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);
                        pendingIndent = false;
                        pendingIndentMarker = null;
                        if (request.Arguments.Length > 0)
                        {
                            blocks.Add(new Paragraph(new List<StyledSpan>
                            {
                                new StyledSpan(Unquote(request.Arguments), FontStyle.Bold)
                            }));
                        }
                        break;
                    }

                    case "I":
                    {
                        FlushAll(blocks, currentSpans, ref collectingTagBody, tagSpans, tagBodySpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);
                        pendingIndent = false;
                        pendingIndentMarker = null;
                        if (request.Arguments.Length > 0)
                        {
                            blocks.Add(new Paragraph(new List<StyledSpan>
                            {
                                new StyledSpan(Unquote(request.Arguments), FontStyle.Italic)
                            }));
                        }
                        break;
                    }

                    case "BR":
                    case "BI":
                    case "IB":
                    case "IR":
                    case "RB":
                    case "RI":
                    {
                        FlushAll(blocks, currentSpans, ref collectingTagBody, tagSpans, tagBodySpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);
                        pendingIndent = false;
                        pendingIndentMarker = null;
                        var altSpans = ParseAlternatingFonts(request.MacroName, request.Arguments);
                        if (altSpans.Count > 0)
                        {
                            blocks.Add(new Paragraph(altSpans));
                        }
                        break;
                    }

                    case "nf":
                    {
                        FlushAll(blocks, currentSpans, ref collectingTagBody, tagSpans, tagBodySpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);
                        pendingIndent = false;
                        pendingIndentMarker = null;
                        inNoFill = true;
                        noFillLines.Clear();
                        break;
                    }

                    case "fi":
                    {
                        // .fi without matching .nf — ignore
                        break;
                    }

                    case "sp":
                    {
                        FlushAll(blocks, currentSpans, ref collectingTagBody, tagSpans, tagBodySpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);
                        pendingIndent = false;
                        pendingIndentMarker = null;
                        blocks.Add(new VerticalSpace(1));
                        break;
                    }

                    default:
                    {
                        // Unknown macro — treat arguments as text
                        if (request.Arguments.Length > 0)
                        {
                            var spans = ParseInlineContent(request.Arguments);
                            if (currentSpans.Count > 0)
                            {
                                currentSpans.Add(new StyledSpan(" ", FontStyle.Roman));
                            }
                            currentSpans.AddRange(spans);
                        }
                        break;
                    }
                }
            }
        }

        // Flush any remaining state
        FlushAll(blocks, currentSpans, ref collectingTagBody, tagSpans, tagBodySpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);

        // Flush remaining no-fill content
        if (inNoFill && noFillLines.Count > 0)
        {
            blocks.Add(new PreformattedBlock(string.Join("\n", noFillLines)));
        }

        return blocks;
    }

    /// <summary>
    /// Flushes all pending state (current paragraph, tagged paragraph) into the block list.
    /// </summary>
    private static void FlushAll(
        List<DocumentBlock> blocks,
        List<StyledSpan> currentSpans,
        ref bool collectingTagBody,
        List<StyledSpan>? tagSpans,
        List<StyledSpan> tagBodySpans,
        bool pendingIndent,
        int pendingIndentValue,
        List<StyledSpan>? pendingIndentMarker,
        int indentLevel)
    {
        if (collectingTagBody && tagSpans != null)
        {
            blocks.Add(new TaggedParagraph(
                new List<StyledSpan>(tagSpans),
                new List<StyledSpan>(tagBodySpans)));
            tagSpans.Clear();
            tagBodySpans.Clear();
            collectingTagBody = false;
        }

        if (currentSpans.Count > 0)
        {
            FlushParagraph(blocks, currentSpans, pendingIndent, pendingIndentValue, pendingIndentMarker, indentLevel);
            currentSpans.Clear();
        }
    }

    /// <summary>
    /// Flushes the current spans into a paragraph or indented paragraph block.
    /// </summary>
    private static void FlushParagraph(
        List<DocumentBlock> blocks,
        List<StyledSpan> currentSpans,
        bool pendingIndent,
        int pendingIndentValue,
        List<StyledSpan>? pendingIndentMarker,
        int indentLevel)
    {
        if (currentSpans.Count == 0)
        {
            return;
        }

        if (pendingIndent || indentLevel > 0)
        {
            var content = new List<StyledSpan>();
            if (pendingIndentMarker != null)
            {
                content.AddRange(pendingIndentMarker);
                content.Add(new StyledSpan(" ", FontStyle.Roman));
            }
            content.AddRange(currentSpans);
            int indent = pendingIndent ? Math.Max(pendingIndentValue, 1) : indentLevel;
            if (indentLevel > 0 && pendingIndent)
            {
                indent = indentLevel + pendingIndentValue;
            }
            blocks.Add(new IndentedParagraph(content, indent));
        }
        else
        {
            blocks.Add(new Paragraph(new List<StyledSpan>(currentSpans)));
        }
    }

    /// <summary>
    /// Parses inline escape sequences in a text fragment, producing a list of
    /// styled spans. Handles \fB (bold), \fI (italic), \fR (roman), \fP (previous),
    /// \- (hyphen-minus), \\ and \e (backslash), \(xx (two-char specials),
    /// \&amp; (zero-width), and \~ (non-breaking space).
    /// </summary>
    /// <param name="text">Raw text that may contain groff inline escapes.</param>
    /// <returns>A list of styled spans with escapes resolved.</returns>
    internal static List<StyledSpan> ParseInlineContent(string text)
    {
        var spans = new List<StyledSpan>();
        var currentText = new StringBuilder();
        var currentStyle = FontStyle.Roman;
        var previousStyle = FontStyle.Roman;
        int i = 0;

        while (i < text.Length)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                char next = text[i + 1];

                if (next == 'f' && i + 2 < text.Length)
                {
                    // Font change — handle both \fB (single-char) and \f[B] (bracket) forms
                    if (currentText.Length > 0)
                    {
                        spans.Add(new StyledSpan(currentText.ToString(), currentStyle));
                        currentText.Clear();
                    }

                    FontStyle newStyle;
                    int advance;

                    if (text[i + 2] == '[')
                    {
                        // Bracket form: \f[B], \f[I], \f[R], \f[BI], etc.
                        int close = text.IndexOf(']', i + 3);
                        if (close > i + 3)
                        {
                            string fontName = text.Substring(i + 3, close - (i + 3));
                            newStyle = fontName switch
                            {
                                "B" => FontStyle.Bold,
                                "I" => FontStyle.Italic,
                                "R" => FontStyle.Roman,
                                "P" => previousStyle,
                                "BI" => FontStyle.Bold | FontStyle.Italic,
                                "IB" => FontStyle.Bold | FontStyle.Italic,
                                _ => currentStyle
                            };
                            advance = close - i + 1;
                        }
                        else
                        {
                            // Malformed — skip \f[
                            newStyle = currentStyle;
                            advance = 3;
                        }
                    }
                    else
                    {
                        // Single-char form: \fB, \fI, \fR, \fP
                        char fontChar = text[i + 2];
                        newStyle = fontChar switch
                        {
                            'B' => FontStyle.Bold,
                            'I' => FontStyle.Italic,
                            'R' => FontStyle.Roman,
                            'P' => previousStyle,
                            _ => currentStyle
                        };
                        advance = 3;
                    }

                    previousStyle = currentStyle;
                    currentStyle = newStyle;
                    i += advance;
                    continue;
                }

                if (next == '-')
                {
                    // Hyphen-minus
                    currentText.Append('-');
                    i += 2;
                    continue;
                }

                if (next == '\\')
                {
                    // Literal backslash
                    currentText.Append('\\');
                    i += 2;
                    continue;
                }

                if (next == 'e')
                {
                    // Literal backslash
                    currentText.Append('\\');
                    i += 2;
                    continue;
                }

                if (next == '(' && i + 3 < text.Length)
                {
                    // Two-char special
                    string special = text.Substring(i + 2, 2);
                    string replacement = special switch
                    {
                        "em" => "\u2014",   // em dash
                        "en" => "--",       // en dash — render as -- on terminal (matches traditional man)
                        "bu" => "\u2022",   // bullet
                        "lq" => "\u201C",   // left double quote
                        "rq" => "\u201D",   // right double quote
                        "aq" => "'",        // apostrophe quote
                        "cq" => "\u2019",   // right single quote (pandoc uses for curly apostrophe)
                        "dq" => "\"",       // double quote
                        "oq" => "\u2018",   // left single quote
                        _ => special        // pass through unknown specials as-is
                    };
                    currentText.Append(replacement);
                    i += 4;
                    continue;
                }

                if (next == '&')
                {
                    // Zero-width space — skip
                    i += 2;
                    continue;
                }

                if (next == '~')
                {
                    // Non-breaking space
                    currentText.Append('\u00A0');
                    i += 2;
                    continue;
                }

                // Unknown escape — pass through the character after backslash
                currentText.Append(next);
                i += 2;
                continue;
            }

            currentText.Append(text[i]);
            i++;
        }

        if (currentText.Length > 0)
        {
            spans.Add(new StyledSpan(currentText.ToString(), currentStyle));
        }

        return spans;
    }

    /// <summary>
    /// Splits a groff argument string on whitespace, respecting double-quoted strings.
    /// Quoted strings are returned with their surrounding quotes intact.
    /// </summary>
    /// <param name="arguments">The raw argument string from a request token.</param>
    /// <returns>A list of individual arguments.</returns>
    internal static List<string> SplitArguments(string arguments)
    {
        var args = new List<string>();
        int i = 0;

        while (i < arguments.Length)
        {
            // Skip whitespace
            while (i < arguments.Length && (arguments[i] == ' ' || arguments[i] == '\t'))
            {
                i++;
            }

            if (i >= arguments.Length)
            {
                break;
            }

            if (arguments[i] == '"')
            {
                // Quoted argument — find the closing quote
                int start = i;
                i++; // skip opening quote
                while (i < arguments.Length && arguments[i] != '"')
                {
                    i++;
                }
                if (i < arguments.Length)
                {
                    i++; // skip closing quote
                }
                args.Add(arguments.Substring(start, i - start));
            }
            else
            {
                // Unquoted argument — find the next whitespace
                int start = i;
                while (i < arguments.Length && arguments[i] != ' ' && arguments[i] != '\t')
                {
                    i++;
                }
                args.Add(arguments.Substring(start, i - start));
            }
        }

        return args;
    }

    /// <summary>
    /// Strips surrounding double quotes from a string, if present.
    /// </summary>
    /// <param name="text">The text to unquote.</param>
    /// <returns>The text without surrounding quotes, or the original text if not quoted.</returns>
    internal static string Unquote(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
        {
            return text.Substring(1, text.Length - 2);
        }
        return text;
    }

    /// <summary>
    /// Parses alternating-font macro arguments (e.g. .BR, .BI, .IR).
    /// The first character of the macro name determines the font for odd-positioned
    /// arguments (1st, 3rd, ...) and the second character determines the font for
    /// even-positioned arguments (2nd, 4th, ...).
    /// </summary>
    /// <param name="macroName">The two-character macro name (e.g. "BR", "BI").</param>
    /// <param name="arguments">The raw argument string.</param>
    /// <returns>A list of styled spans with alternating fonts.</returns>
    internal static List<StyledSpan> ParseAlternatingFonts(string macroName, string arguments)
    {
        var spans = new List<StyledSpan>();
        var args = SplitArguments(arguments);

        FontStyle font1 = CharToFontStyle(macroName[0]);
        FontStyle font2 = macroName.Length > 1 ? CharToFontStyle(macroName[1]) : FontStyle.Roman;

        for (int i = 0; i < args.Count; i++)
        {
            FontStyle style = (i % 2 == 0) ? font1 : font2;
            spans.Add(new StyledSpan(Unquote(args[i]), style));
        }

        return spans;
    }

    /// <summary>
    /// Converts a single font character (B, I, R) to a <see cref="FontStyle"/> value.
    /// </summary>
    private static FontStyle CharToFontStyle(char c)
    {
        return c switch
        {
            'B' => FontStyle.Bold,
            'I' => FontStyle.Italic,
            _ => FontStyle.Roman
        };
    }
}
