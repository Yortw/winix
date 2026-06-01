using System.IO;

namespace Winix.Demux;

/// <summary>
/// Passthrough sink for unmatched records — wraps demux's own stdout writer. A broken pipe here
/// (downstream consumer closed) marks the sink dead and counts undelivered, like any other sink.
/// </summary>
public sealed class StdoutSink : ISink
{
    private readonly TextWriter _writer;
    private bool _dead;
    private long _delivered;
    private long _undelivered;

    /// <summary>Initialises the sink wrapping the given writer.</summary>
    /// <param name="writer">The underlying writer to forward lines to.</param>
    /// <param name="label">Human-readable label used in summary output.</param>
    public StdoutSink(TextWriter writer, string label = "stdout")
    {
        _writer = writer;
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
        try
        {
            // Write '\n' explicitly (not WriteLine) so we don't rewrite LF input to CRLF on Windows
            // or append a terminator the original line didn't have. A router preserves line bytes.
            _writer.Write(line);
            _writer.Write('\n');
            _delivered++;
        }
        catch (IOException)
        {
            _dead = true;
            _undelivered++;
        }
    }

    /// <inheritdoc/>
    public void Close()
    {
        try { _writer.Flush(); } catch (IOException) { /* downstream gone; nothing to do */ }
    }
}
