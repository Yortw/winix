#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Winix.Less;

/// <summary>
/// Provides the content lines to the pager, loaded from a file, stdin, or an in-memory string.
/// Supports polling for new content appended to a file, enabling follow-mode (tail -f style).
/// </summary>
public sealed class InputSource
{
    private readonly string? _filePath;
    private readonly List<string> _lines;
    private long _lastFileLength;

    /// <summary>
    /// The display name for this source — the file path for file sources, or
    /// a caller-supplied label for stdin and string sources.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The lines of content currently loaded from this source.
    /// For file sources, this list grows as <see cref="PollForNewContent"/> detects appended data.
    /// </summary>
    public IReadOnlyList<string> Lines => _lines;

    private InputSource(string name, List<string> lines, string? filePath, long lastFileLength)
    {
        Name = name;
        _lines = lines;
        _filePath = filePath;
        _lastFileLength = lastFileLength;
    }

    /// <summary>
    /// Loads all content from the specified file.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the file to read.</param>
    /// <returns>An <see cref="InputSource"/> whose <see cref="Lines"/> reflect the file contents.</returns>
    /// <exception cref="FileNotFoundException">Thrown when <paramref name="filePath"/> does not exist.</exception>
    public static InputSource FromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        // Read with ReadWrite share so we can re-open the same file during polling
        // without conflicting with other processes writing to it.
        string content;
        long fileLength;
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            fileLength = stream.Length;
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                content = reader.ReadToEnd();
            }
        }

        var lines = SplitLines(content);
        return new InputSource(filePath, lines, filePath, fileLength);
    }

    /// <summary>
    /// Reads all content from standard input and returns it as an <see cref="InputSource"/>.
    /// Polling is not supported for stdin sources — <see cref="PollForNewContent"/> always returns false.
    /// </summary>
    /// <returns>An <see cref="InputSource"/> whose <see cref="Lines"/> hold the stdin content.</returns>
    public static InputSource FromStdin()
    {
        string content = Console.In.ReadToEnd();
        var lines = SplitLines(content);
        return new InputSource("(stdin)", lines, null, 0);
    }

    /// <summary>
    /// Creates an <see cref="InputSource"/> from an in-memory string. Intended primarily for testing.
    /// Polling is not supported — <see cref="PollForNewContent"/> always returns false.
    /// </summary>
    /// <param name="content">The full text content to split into lines.</param>
    /// <param name="name">A display name to identify this source in the pager status bar.</param>
    /// <returns>An <see cref="InputSource"/> backed by the supplied string.</returns>
    public static InputSource FromString(string content, string name)
    {
        var lines = SplitLines(content);
        return new InputSource(name, lines, null, 0);
    }

    /// <summary>
    /// Checks whether the backing file has grown since the last read, and if so appends the new lines.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if new content was found and appended to <see cref="Lines"/>;
    /// <see langword="false"/> if the file is unchanged or this source is not backed by a file.
    /// </returns>
    /// <remarks>
    /// Reads only the bytes beyond <c>_lastFileLength</c>, so no re-reading of existing content.
    /// If the previous read left a trailing empty line (from a final newline), that placeholder is
    /// replaced by the first token of the new data rather than producing a spurious blank line.
    /// </remarks>
    public bool PollForNewContent()
    {
        if (_filePath == null)
        {
            return false;
        }

        string newChunk;
        long newLength;

        using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            newLength = stream.Length;
            if (newLength <= _lastFileLength)
            {
                return false;
            }

            // Seek past the already-read bytes and read only the new tail.
            stream.Seek(_lastFileLength, SeekOrigin.Begin);
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                newChunk = reader.ReadToEnd();
            }
        }

        _lastFileLength = newLength;

        var newTokens = newChunk.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        // If the last line in the existing buffer is empty (i.e. the previous read ended with a
        // newline), merge the first new token into it rather than leaving a spurious blank line.
        if (_lines.Count > 0 && _lines[_lines.Count - 1] == "")
        {
            _lines[_lines.Count - 1] = newTokens[0];
        }
        else
        {
            _lines.Add(newTokens[0]);
        }

        for (int i = 1; i < newTokens.Length - 1; i++)
        {
            _lines.Add(newTokens[i]);
        }

        // The final split token is either an empty string (trailing newline) or a partial line.
        // Keep it only if the new chunk ends with a newline — same trailing-empty-line convention
        // used by SplitLines — so follow-mode stays consistent.
        if (newTokens.Length > 1)
        {
            _lines.Add(newTokens[newTokens.Length - 1]);
        }

        // Remove a trailing empty string produced by a final newline, unless the list would become empty.
        if (_lines.Count > 1 && _lines[_lines.Count - 1] == "")
        {
            _lines.RemoveAt(_lines.Count - 1);
        }

        return true;
    }

    /// <summary>
    /// Splits <paramref name="content"/> on both LF and CRLF line endings.
    /// A trailing newline produces a trailing empty token that is removed, keeping
    /// the list length equal to the number of logical lines rather than the number of newlines.
    /// An empty string yields a single empty line so the pager always has at least one line to display.
    /// </summary>
    private static List<string> SplitLines(string content)
    {
        var tokens = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var result = new List<string>(tokens);

        // Remove the trailing empty string created by a final newline, but only when
        // the file has actual content — an empty file must keep its single empty line.
        if (result.Count > 1 && result[result.Count - 1] == "")
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }
}
