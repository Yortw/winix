namespace Winix.Wargs;

/// <summary>
/// Reads items from a text stream, splitting by the configured delimiter.
/// Streaming — reads one item at a time without buffering the entire input.
/// </summary>
public sealed class InputReader
{
    private readonly TextReader _source;
    private readonly DelimiterMode _mode;
    private readonly char _customDelimiter;

    /// <summary>
    /// Creates a new input reader.
    /// </summary>
    /// <param name="source">The text stream to read from (typically stdin).</param>
    /// <param name="mode">How to split the stream into items.</param>
    /// <param name="customDelimiter">
    /// The delimiter character when <paramref name="mode"/> is <see cref="DelimiterMode.Custom"/>.
    /// Ignored for other modes.
    /// </param>
    public InputReader(TextReader source, DelimiterMode mode, char customDelimiter = '\0')
    {
        _source = source;
        _mode = mode;
        _customDelimiter = customDelimiter;
    }

    /// <summary>
    /// Yields items from the input stream one at a time. The optional
    /// <paramref name="cancellationToken"/> is observed between reads — when signalled,
    /// the enumerator throws <see cref="OperationCanceledException"/> so a Ctrl+C during a
    /// slow stdin read is reported as cancellation rather than misclassified as
    /// empty-input or input-read-failed by the caller. Without this observation, a
    /// blocked TextReader.Read() that returns null/EOF after Console.In is closed by a
    /// cancellation callback would silently land in the empty-input branch.
    /// </summary>
    public IEnumerable<string> ReadItems(CancellationToken cancellationToken = default)
    {
        return _mode switch
        {
            DelimiterMode.Line => ReadLineDelimited(cancellationToken),
            DelimiterMode.Null => ReadCharDelimited('\0', cancellationToken),
            DelimiterMode.Custom => ReadCharDelimited(_customDelimiter, cancellationToken),
            DelimiterMode.Whitespace => ReadWhitespaceDelimited(cancellationToken),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private IEnumerable<string> ReadLineDelimited(CancellationToken ct)
    {
        string? line;
        while ((line = _source.ReadLine()) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line;
            }
        }
        ct.ThrowIfCancellationRequested();
    }

    private IEnumerable<string> ReadCharDelimited(char delimiter, CancellationToken ct)
    {
        var buffer = new System.Text.StringBuilder();
        int ch;
        while ((ch = _source.Read()) != -1)
        {
            ct.ThrowIfCancellationRequested();
            if ((char)ch == delimiter)
            {
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString();
                    buffer.Clear();
                }
            }
            else
            {
                buffer.Append((char)ch);
            }
        }

        ct.ThrowIfCancellationRequested();

        // Emit trailing item if no final delimiter
        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }

    private IEnumerable<string> ReadWhitespaceDelimited(CancellationToken ct)
    {
        var buffer = new System.Text.StringBuilder();
        int ch;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool escaped = false;

        while ((ch = _source.Read()) != -1)
        {
            ct.ThrowIfCancellationRequested();
            char c = (char)ch;

            if (escaped)
            {
                buffer.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\' && !inSingleQuote)
            {
                escaped = true;
                continue;
            }

            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inSingleQuote && !inDoubleQuote)
            {
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString();
                    buffer.Clear();
                }
                continue;
            }

            buffer.Append(c);
        }

        ct.ThrowIfCancellationRequested();

        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }
}
