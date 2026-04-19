#nullable enable
using System.IO;
using System.Text;

namespace Winix.Digest.Tests.Fakes;

/// <summary>
/// A <see cref="TextReader"/> backed by in-memory bytes, for injecting
/// test stdin content without touching real stdin.
/// </summary>
public sealed class FakeTextReader : StringReader
{
    /// <inheritdoc cref="StringReader(string)"/>
    public FakeTextReader(string content) : base(content) { }

    /// <summary>Convenience constructor for raw bytes interpreted as UTF-8.</summary>
    public static FakeTextReader FromBytes(byte[] bytes) =>
        new(Encoding.UTF8.GetString(bytes));
}
