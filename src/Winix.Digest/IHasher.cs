#nullable enable
using System;
using System.IO;

namespace Winix.Digest;

/// <summary>
/// Computes a hash (or HMAC) over bytes or a stream. Implementations wrap
/// .NET BCL primitives (SHA-2, SHA-3, SHA-1, MD5, HMAC*) or third-party
/// implementations (BLAKE2b via SauceControl.Blake2Fast).
/// </summary>
public interface IHasher
{
    /// <summary>Hashes the given byte span in one shot.</summary>
    byte[] Hash(ReadOnlySpan<byte> input);

    /// <summary>Hashes the given stream incrementally (no full buffering).</summary>
    byte[] Hash(Stream input);
}
