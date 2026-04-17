using System;
using System.Security.Cryptography;

namespace Winix.Codec;

/// <summary>
/// Default <see cref="ISecureRandom"/> implementation backed by the OS CSPRNG
/// via <see cref="RandomNumberGenerator.Fill(Span{byte})"/>. Thread-safe.
/// </summary>
public sealed class SecureRandom : ISecureRandom
{
    /// <summary>Singleton instance. Safe to share across threads and generators.</summary>
    public static readonly ISecureRandom Default = new SecureRandom();

    private SecureRandom() { }

    /// <inheritdoc />
    public void Fill(Span<byte> destination) => RandomNumberGenerator.Fill(destination);
}
