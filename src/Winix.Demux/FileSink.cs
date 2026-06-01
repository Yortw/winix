using System.IO;

namespace Winix.Demux;

/// <summary>
/// File target. Opens once at construction (truncate or append); an unopenable path throws
/// at construction so the caller can map it to a setup-failure exit (126).
/// </summary>
public sealed class FileSink : ISink
{
    private readonly StreamWriter _writer;
    private bool _dead;
    private long _delivered;
    private long _undelivered;

    /// <summary>
    /// Opens <paramref name="path"/> for writing. Throws <see cref="IOException"/>,
    /// <see cref="UnauthorizedAccessException"/>, or <see cref="DirectoryNotFoundException"/>
    /// immediately if the path cannot be opened — callers should treat these as exit 126.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="label">Human-readable label used in summary output.</param>
    /// <param name="append">
    /// <see langword="true"/> to append to an existing file; <see langword="false"/> to truncate.
    /// </param>
    public FileSink(string path, string label, bool append)
    {
        // Throws (IOException / UnauthorizedAccess / DirectoryNotFound) if the path can't be opened —
        // caller maps to exit 126.
        _writer = new StreamWriter(path, append) { AutoFlush = false };
        Label = label;
    }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public long DeliveredCount => _delivered;

    /// <inheritdoc/>
    public long UndeliveredCount => _undelivered;

    /// <inheritdoc/>
    public bool IsDead => _dead;

    /// <inheritdoc/>
    public int? ChildExitCode => null;

    /// <inheritdoc/>
    public void Write(string line)
    {
        if (_dead) { _undelivered++; return; }
        // Write '\n' explicitly (not WriteLine) to preserve line bytes — no LF→CRLF rewrite on Windows.
        try
        {
            _writer.Write(line);
            _writer.Write('\n');
            _delivered++;
        }
        catch (IOException) { _dead = true; _undelivered++; }
    }

    /// <inheritdoc/>
    public void Close()
    {
        try { _writer.Flush(); } catch (IOException) { /* disk gone; counts already reflect it */ }
        _writer.Dispose();
    }
}
