using System;
using System.Collections.Generic;
using Winix.Codec;

namespace Winix.Ids.Tests.Fakes;

/// <summary>
/// Deterministic byte source backed by an in-memory queue. Tests enqueue the byte
/// sequence they want emitted; <see cref="Fill"/> consumes it in order. Throws if
/// the queue runs dry, so a test that reads more bytes than provided fails loudly
/// rather than returning zeros.
/// </summary>
public sealed class FakeSecureRandom : ISecureRandom
{
    private readonly Queue<byte> _bytes = new();

    /// <summary>Initialises the fake with an initial sequence of bytes.</summary>
    public FakeSecureRandom(params byte[] bytes)
    {
        foreach (var b in bytes)
        {
            _bytes.Enqueue(b);
        }
    }

    /// <summary>Appends more bytes to the queue mid-test.</summary>
    public void Enqueue(params byte[] bytes)
    {
        foreach (var b in bytes)
        {
            _bytes.Enqueue(b);
        }
    }

    /// <inheritdoc />
    public void Fill(Span<byte> destination)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            if (_bytes.Count == 0)
            {
                throw new InvalidOperationException("FakeSecureRandom ran out of bytes.");
            }
            destination[i] = _bytes.Dequeue();
        }
    }
}
