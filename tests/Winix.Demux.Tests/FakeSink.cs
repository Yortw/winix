using System.Collections.Generic;
using Winix.Demux;

namespace Winix.Demux.Tests;

/// <summary>In-memory sink for Router tests. Optionally simulates death after N writes.</summary>
internal sealed class FakeSink : ISink
{
    private readonly int _dieAfter; // -1 = never die

    /// <summary>Initialises the fake sink.</summary>
    /// <param name="label">Human-readable label for summary assertions.</param>
    /// <param name="dieAfter">Number of writes before the sink simulates a broken-pipe death; -1 means never die.</param>
    public FakeSink(string label, int dieAfter = -1) { Label = label; _dieAfter = dieAfter; }

    /// <summary>Lines accepted by this sink, in write order.</summary>
    public List<string> Lines { get; } = new();

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public long DeliveredCount { get; private set; }

    /// <inheritdoc/>
    public long UndeliveredCount { get; private set; }

    /// <inheritdoc/>
    public bool IsDead { get; private set; }

    /// <inheritdoc/>
    public int? ChildExitCode { get; set; }

    /// <summary>True after <see cref="Close"/> has been called.</summary>
    public bool Closed { get; private set; }

    /// <inheritdoc/>
    public void Write(string line)
    {
        if (IsDead) { UndeliveredCount++; return; }
        if (_dieAfter >= 0 && Lines.Count >= _dieAfter) { IsDead = true; UndeliveredCount++; return; }
        Lines.Add(line);
        DeliveredCount++;
    }

    /// <inheritdoc/>
    public void Close() => Closed = true;
}
