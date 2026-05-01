#nullable enable
using System;

namespace Winix.Protect;

/// <summary>
/// Per-chunk encrypt/decrypt contract. Called by <see cref="ChunkWriter"/> / <see cref="ChunkReader"/>.
/// Implementations are Windows DPAPI (keyless) or AES-GCM-with-SecretStore-key (Mac/Linux).
/// </summary>
/// <remarks>
/// Extends <see cref="IDisposable"/> so backends holding sensitive in-memory key material
/// (e.g. AES-256 cache in <see cref="AeadBackend"/>) can zero it deterministically rather
/// than waiting for GC. Callers that construct a backend own its lifetime.
/// </remarks>
public interface IProtectBackend : IDisposable
{
    /// <summary>The platform marker for files produced by this backend.</summary>
    PlatformMarker Marker { get; }

    /// <summary>Encrypt a single chunk of plaintext. <paramref name="isFinal"/> must be folded into the ciphertext integrity.</summary>
    byte[] EncryptChunk(byte[] plaintext, AadContext aad, bool isFinal);

    /// <summary>Decrypt a single chunk. Returns (plaintext, isFinal). Must throw on any tamper / integrity failure.</summary>
    (byte[] plaintext, bool isFinal) DecryptChunk(byte[] chunkPayload, AadContext aad);
}
