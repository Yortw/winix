using System;

namespace Winix.Codec;

/// <summary>
/// Source of cryptographically secure random bytes. The default implementation
/// delegates to <see cref="System.Security.Cryptography.RandomNumberGenerator"/>;
/// tests inject deterministic fakes.
/// </summary>
public interface ISecureRandom
{
    /// <summary>
    /// Fills the destination span with random bytes.
    /// </summary>
    void Fill(Span<byte> destination);
}
