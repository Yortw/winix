#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Winix.Man;

/// <summary>
/// Converts a list of <see cref="DocumentBlock"/> instances to an ANSI-formatted
/// string suitable for display in a terminal. Handles word wrapping, bold/italic
/// spans, colour headings, and preformatted blocks.
/// </summary>
public sealed class TerminalRenderer
{
    // Default indentation for body text (matches traditional man page style).
    internal const int DefaultIndent = 7;

    // Column at which tagged-paragraph bodies begin.
    internal const int TaggedBodyIndent = 15;

    // Hard upper bound on rendering width.
    internal const int MaxWidth = 80;

    // ANSI escape codes.
    private const string AnsiReset = "\x1b[0m";
    private const string AnsiBold = "\x1b[1m";
    private const string AnsiItalic = "\x1b[4m";   // underline — standard terminal substitute for italic
    private const string AnsiCyan = "\x1b[36m";

    private readonly RendererOptions _options;
    private readonly int _width;

    /// <summary>
    /// Initialises a new <see cref="TerminalRenderer"/> with the supplied options.
    /// </summary>
    /// <param name="options">Rendering configuration; must not be null.</param>
    public TerminalRenderer(RendererOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _width = options.WidthOverride ?? Math.Min(Console.WindowWidth > 0 ? Console.WindowWidth : MaxWidth, MaxWidth);
    }

    /// <summary>
    /// Renders the supplied document blocks to an ANSI-formatted string.
    /// Returns an empty string when <paramref name="blocks"/> is empty.
    /// </summary>
    /// <param name="blocks">The blocks to render; must not be null.</param>
    /// <returns>The rendered text, with trailing newline stripped.</returns>
    public string Render(IReadOnlyList<DocumentBlock> blocks)
    {
        if (blocks == null) throw new ArgumentNullException(nameof(blocks));
        if (blocks.Count == 0) { return ""; }

        var sb = new StringBuilder();

        foreach (var block in blocks)
        {
            switch (block)
            {
                case TitleBlock title:
                    RenderTitleBlock(sb, title);
                    break;

                case SectionHeading heading:
                    RenderSectionHeading(sb, heading);
                    break;

                case SubsectionHeading sub:
                    RenderSubsectionHeading(sb, sub);
                    break;

                case Paragraph para:
                    RenderParagraph(sb, para.Content, DefaultIndent);
                    break;

                case TaggedParagraph tagged:
                    RenderTaggedParagraph(sb, tagged);
                    break;

                case IndentedParagraph indented:
                    RenderParagraph(sb, indented.Content, DefaultIndent + indented.Indent);
                    break;

                case PreformattedBlock pre:
                    RenderPreformattedBlock(sb, pre);
                    break;

                case VerticalSpace space:
                    RenderVerticalSpace(sb, space);
                    break;

                default:
                    // Unknown block type — ignore rather than throw, for forward compatibility.
                    break;
            }
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Block renderers
    // -------------------------------------------------------------------------

    private void RenderTitleBlock(StringBuilder sb, TitleBlock title)
    {
        var nameSection = $"{title.Name}({title.Section})";
        var header = BuildTitleLine(nameSection, title.Manual, _width);
        sb.Append(ApplyBold(header));
        sb.AppendLine();
    }

    private void RenderSectionHeading(StringBuilder sb, SectionHeading heading)
    {
        // Blank line before each section heading for visual separation.
        sb.AppendLine();
        string text = _options.Color
            ? $"{AnsiBold}{AnsiCyan}{heading.Text}{AnsiReset}"
            : $"{AnsiBold}{heading.Text}{AnsiReset}";
        sb.AppendLine(text);
    }

    private void RenderSubsectionHeading(StringBuilder sb, SubsectionHeading sub)
    {
        var indent = new string(' ', 3);
        sb.AppendLine($"{indent}{AnsiBold}{sub.Text}{AnsiReset}");
    }

    private void RenderParagraph(StringBuilder sb, IReadOnlyList<StyledSpan> content, int indent)
    {
        var indentStr = new string(' ', indent);
        var availableWidth = _width - indent;

        // Get plain text for wrapping calculations.
        var plainText = SpansToPlainText(content);
        var styledText = RenderSpansToAnsi(content);

        var lines = WordWrap(plainText, styledText, availableWidth);
        foreach (var line in lines)
        {
            sb.Append(indentStr);
            sb.AppendLine(line);
        }
    }

    private void RenderTaggedParagraph(StringBuilder sb, TaggedParagraph tagged)
    {
        var tagIndentStr = new string(' ', DefaultIndent);
        var bodyIndentStr = new string(' ', TaggedBodyIndent);

        var plainTag = SpansToPlainText(tagged.Tag);
        var styledTag = RenderSpansToAnsi(tagged.Tag);

        var bodyAvailableWidth = _width - TaggedBodyIndent;
        var plainBody = SpansToPlainText(tagged.Body);
        var styledBody = RenderSpansToAnsi(tagged.Body);
        var bodyLines = WordWrap(plainBody, styledBody, bodyAvailableWidth);

        if (plainTag.Length < TaggedBodyIndent - DefaultIndent)
        {
            // Tag fits on the same line as the first body line.
            var firstBodyLine = bodyLines.Count > 0 ? bodyLines[0] : "";
            var padding = new string(' ', TaggedBodyIndent - DefaultIndent - plainTag.Length);
            sb.Append(tagIndentStr);
            sb.Append(styledTag);
            sb.Append(padding);
            sb.AppendLine(firstBodyLine);

            for (var i = 1; i < bodyLines.Count; i++)
            {
                sb.Append(bodyIndentStr);
                sb.AppendLine(bodyLines[i]);
            }
        }
        else
        {
            // Tag is too long — put it on its own line, body on next line.
            sb.Append(tagIndentStr);
            sb.AppendLine(styledTag);
            foreach (var line in bodyLines)
            {
                sb.Append(bodyIndentStr);
                sb.AppendLine(line);
            }
        }
    }

    private void RenderPreformattedBlock(StringBuilder sb, PreformattedBlock pre)
    {
        var indentStr = new string(' ', DefaultIndent);
        // Split on newlines; each line is indented but never wrapped.
        var lines = pre.Text.Split('\n');
        foreach (var line in lines)
        {
            sb.Append(indentStr);
            sb.AppendLine(line.TrimEnd('\r'));
        }
    }

    private static void RenderVerticalSpace(StringBuilder sb, VerticalSpace space)
    {
        for (var i = 0; i < space.Lines; i++)
        {
            sb.AppendLine();
        }
    }

    // -------------------------------------------------------------------------
    // ANSI helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts a list of styled spans to an ANSI-escaped string. Roman spans
    /// emit no codes; Bold emits bold; Italic emits underline (terminal convention).
    /// </summary>
    internal string RenderSpansToAnsi(IReadOnlyList<StyledSpan> spans)
    {
        var sb = new StringBuilder();
        foreach (var span in spans)
        {
            if (span.Style == FontStyle.Bold)
            {
                sb.Append(AnsiBold);
                sb.Append(span.Text);
                sb.Append(AnsiReset);
            }
            else if (span.Style == FontStyle.Italic)
            {
                sb.Append(AnsiItalic);
                sb.Append(span.Text);
                sb.Append(AnsiReset);
            }
            else
            {
                sb.Append(span.Text);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Concatenates the plain text of all spans without any ANSI codes.
    /// Used for width calculations during word wrapping.
    /// </summary>
    internal static string SpansToPlainText(IReadOnlyList<StyledSpan> spans)
    {
        var sb = new StringBuilder();
        foreach (var span in spans)
        {
            sb.Append(span.Text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Wraps <paramref name="plainText"/> at <paramref name="availableWidth"/> columns,
    /// then maps the wrapped plain lines back to the styled version for output.
    /// Returns one entry per output line; entries may contain ANSI codes.
    /// </summary>
    /// <remarks>
    /// Because inline spans can split mid-word, this uses a simplified strategy:
    /// word boundaries are determined from the plain text, then each plain-text
    /// line is re-rendered by consuming characters from the styled text.
    /// </remarks>
    internal static List<string> WordWrap(string plainText, string styledText, int availableWidth)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(plainText)) { return result; }

        // Split plainText into words on whitespace boundaries.
        var words = plainText.Split(' ');
        var currentLine = new StringBuilder();
        var plainLines = new List<string>();

        foreach (var word in words)
        {
            if (word.Length == 0) { continue; }

            if (currentLine.Length == 0)
            {
                currentLine.Append(word);
            }
            else if (currentLine.Length + 1 + word.Length <= availableWidth)
            {
                currentLine.Append(' ');
                currentLine.Append(word);
            }
            else
            {
                plainLines.Add(currentLine.ToString());
                currentLine.Clear();
                currentLine.Append(word);
            }
        }

        if (currentLine.Length > 0)
        {
            plainLines.Add(currentLine.ToString());
        }

        // Map each plain line back to the styled equivalent.
        // We consume characters from styledText, skipping over ANSI escape sequences.
        var styledPos = 0;
        foreach (var plainLine in plainLines)
        {
            var lineBuilder = new StringBuilder();
            var charsNeeded = plainLine.Length;
            var charsConsumed = 0;

            while (charsConsumed < charsNeeded && styledPos < styledText.Length)
            {
                // Copy any ANSI escape sequences verbatim (they have zero visible width).
                while (styledPos < styledText.Length && styledText[styledPos] == '\x1b')
                {
                    var escEnd = styledPos + 1;
                    // Consume: ESC [ ... m
                    if (escEnd < styledText.Length && styledText[escEnd] == '[')
                    {
                        escEnd++;
                        while (escEnd < styledText.Length && styledText[escEnd] != 'm')
                        {
                            escEnd++;
                        }
                        if (escEnd < styledText.Length) { escEnd++; } // include 'm'
                    }
                    lineBuilder.Append(styledText, styledPos, escEnd - styledPos);
                    styledPos = escEnd;
                }

                if (styledPos < styledText.Length)
                {
                    lineBuilder.Append(styledText[styledPos]);
                    styledPos++;
                    charsConsumed++;
                }
            }

            // Skip the space separator between lines in the plain text mapping.
            if (styledPos < styledText.Length && styledText[styledPos] == ' ')
            {
                styledPos++;
            }
            // Also skip any trailing ANSI codes before the next word (e.g. reset codes).
            while (styledPos < styledText.Length && styledText[styledPos] == '\x1b')
            {
                var escEnd = styledPos + 1;
                if (escEnd < styledText.Length && styledText[escEnd] == '[')
                {
                    escEnd++;
                    while (escEnd < styledText.Length && styledText[escEnd] != 'm')
                    {
                        escEnd++;
                    }
                    if (escEnd < styledText.Length) { escEnd++; }
                }
                lineBuilder.Append(styledText, styledPos, escEnd - styledPos);
                styledPos = escEnd;
            }

            result.Add(lineBuilder.ToString());
        }

        return result;
    }

    /// <summary>
    /// Removes ANSI escape sequences from <paramref name="text"/> for width
    /// measurement purposes.
    /// </summary>
    internal static string StripAnsi(string text)
    {
        // Matches ESC [ ... m sequences.
        return Regex.Replace(text, @"\x1b\[[^m]*m", "");
    }

    // -------------------------------------------------------------------------
    // Formatting helpers
    // -------------------------------------------------------------------------

    private string ApplyBold(string text)
    {
        return $"{AnsiBold}{text}{AnsiReset}";
    }

    /// <summary>
    /// Builds the title header line in the traditional man page format:
    /// "NAME(N)   Manual   NAME(N)", padded to <paramref name="width"/>.
    /// </summary>
    internal static string BuildTitleLine(string nameSection, string manual, int width)
    {
        // Traditional layout: left = nameSection, centre = manual, right = nameSection.
        var centre = manual;
        var side = nameSection;

        // Compute spacing so the three parts are distributed across the width.
        var totalPadding = width - side.Length - centre.Length - side.Length;
        if (totalPadding < 2) { totalPadding = 2; }

        // Split padding roughly in half either side of the centre.
        var leftPad = totalPadding / 2;
        var rightPad = totalPadding - leftPad;

        return side + new string(' ', leftPad) + centre + new string(' ', rightPad) + side;
    }
}
