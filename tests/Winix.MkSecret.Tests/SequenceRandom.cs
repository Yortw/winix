using System;
using Winix.Codec;

namespace Winix.MkSecret.Tests;

/// <summary>ISecureRandom that yields a fixed, scripted byte sequence so generators produce
/// deterministic output. Throws if the script is exhausted (tests must supply enough bytes).</summary>
public sealed class SequenceRandom : ISecureRandom
{
    private readonly byte[] _bytes;
    private int _pos;

    public SequenceRandom(params byte[] bytes) => _bytes = bytes;

    public void Fill(Span<byte> destination)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            if (_pos >= _bytes.Length)
            {
                throw new InvalidOperationException("SequenceRandom exhausted: supply more scripted bytes.");
            }
            destination[i] = _bytes[_pos++];
        }
    }
}

/// <summary>ISecureRandom whose Fill always throws — exercises Cli.Run's catch-all error path.</summary>
public sealed class ThrowingRandom : ISecureRandom
{
    public void Fill(Span<byte> destination)
        => throw new InvalidOperationException("simulated CSPRNG failure");
}
