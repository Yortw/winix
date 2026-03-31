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
    /// Yields items from the input stream one at a time.
    /// </summary>
    public IEnumerable<string> ReadItems()
    {
        return _mode switch
        {
            DelimiterMode.Line => ReadLineDelimited(),
            DelimiterMode.Null => ReadCharDelimited('\0'),
            DelimiterMode.Custom => ReadCharDelimited(_customDelimiter),
            DelimiterMode.Whitespace => ReadWhitespaceDelimited(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private IEnumerable<string> ReadLineDelimited()
    {
        string? line;
        while ((line = _source.ReadLine()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line;
            }
        }
    }

    private IEnumerable<string> ReadCharDelimited(char delimiter)
    {
        var buffer = new System.Text.StringBuilder();
        int ch;
        while ((ch = _source.Read()) != -1)
        {
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

        // Emit trailing item if no final delimiter
        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }

    private IEnumerable<string> ReadWhitespaceDelimited()
    {
        // Placeholder — implemented in Task 3
        yield break;
    }
}
