#nullable enable
using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Winix.Protect;

/// <summary>
/// Windows DPAPI-backed per-chunk encryption. Each chunk is framed with
/// <c>[is_final | FileId(16) | chunkIndex(8) | plaintext]</c> before being passed through
/// <see cref="ProtectedData"/>. DPAPI authenticates the framed plaintext as a single blob,
/// so binding the FileId and chunkIndex *inside* the envelope is what makes cross-file chunk
/// substitution and intra-file reorder fail-closed on Windows. (The AEAD path achieves the
/// same with GCM AAD; DPAPI has no AAD slot, hence the in-band binding here.)
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiBackend : IProtectBackend
{
    private const int EnvelopePrefixLength = 1 + Header.FileIdLength + 8;

    private readonly DataProtectionScope _scope;

    /// <summary>Create a DPAPI backend bound to the given <paramref name="scope"/>.</summary>
    public DpapiBackend(Scope scope)
    {
        _scope = scope == Scope.Machine
            ? DataProtectionScope.LocalMachine
            : DataProtectionScope.CurrentUser;
        Marker = scope == Scope.Machine
            ? PlatformMarker.WindowsDpapiMachine
            : PlatformMarker.WindowsDpapiUser;
    }

    /// <inheritdoc />
    public PlatformMarker Marker { get; }

    /// <inheritdoc />
    public byte[] EncryptChunk(byte[] plaintext, AadContext aad, bool isFinal)
    {
        if (aad.HeaderBytes is null || aad.HeaderBytes.Length != Header.Length)
        {
            throw new ArgumentException($"AadContext.HeaderBytes must be {Header.Length} bytes.", nameof(aad));
        }

        // Envelope: [isFinal(1) | FileId(16) | chunkIndex_be(8) | plaintext]
        byte[] framed = new byte[EnvelopePrefixLength + plaintext.Length];
        framed[0] = isFinal ? (byte)1 : (byte)0;
        Array.Copy(aad.HeaderBytes, Header.FileIdOffset, framed, 1, Header.FileIdLength);

        long idx = aad.ChunkIndex;
        int idxOff = 1 + Header.FileIdLength;
        framed[idxOff + 0] = (byte)(idx >> 56);
        framed[idxOff + 1] = (byte)(idx >> 48);
        framed[idxOff + 2] = (byte)(idx >> 40);
        framed[idxOff + 3] = (byte)(idx >> 32);
        framed[idxOff + 4] = (byte)(idx >> 24);
        framed[idxOff + 5] = (byte)(idx >> 16);
        framed[idxOff + 6] = (byte)(idx >> 8);
        framed[idxOff + 7] = (byte)idx;

        Array.Copy(plaintext, 0, framed, EnvelopePrefixLength, plaintext.Length);
        return ProtectedData.Protect(framed, optionalEntropy: null, _scope);
    }

    /// <inheritdoc />
    public (byte[] plaintext, bool isFinal) DecryptChunk(byte[] chunkPayload, AadContext aad)
    {
        if (aad.HeaderBytes is null || aad.HeaderBytes.Length != Header.Length)
        {
            throw new ArgumentException($"AadContext.HeaderBytes must be {Header.Length} bytes.", nameof(aad));
        }

        byte[] framed = ProtectedData.Unprotect(chunkPayload, optionalEntropy: null, _scope);
        if (framed.Length < EnvelopePrefixLength)
        {
            throw new CryptographicException("DPAPI payload too short (envelope header missing).");
        }

        bool isFinal = framed[0] == 1;

        // Verify FileId binding — rejects chunk substitution between files.
        for (int i = 0; i < Header.FileIdLength; i++)
        {
            if (framed[1 + i] != aad.HeaderBytes[Header.FileIdOffset + i])
            {
                throw new CryptographicException(
                    "DPAPI chunk does not belong to this file (FileId mismatch — chunk substitution attempted).");
            }
        }

        // Verify chunkIndex binding — rejects intra-file chunk reorder.
        int idxOff = 1 + Header.FileIdLength;
        long idx = ((long)framed[idxOff + 0] << 56)
                 | ((long)framed[idxOff + 1] << 48)
                 | ((long)framed[idxOff + 2] << 40)
                 | ((long)framed[idxOff + 3] << 32)
                 | ((long)framed[idxOff + 4] << 24)
                 | ((long)framed[idxOff + 5] << 16)
                 | ((long)framed[idxOff + 6] << 8)
                 |  (long)framed[idxOff + 7];
        if (idx != aad.ChunkIndex)
        {
            throw new CryptographicException(
                $"DPAPI chunk position mismatch (expected index {aad.ChunkIndex}, got {idx} — chunk reorder attempted).");
        }

        byte[] plaintext = new byte[framed.Length - EnvelopePrefixLength];
        Array.Copy(framed, EnvelopePrefixLength, plaintext, 0, plaintext.Length);
        return (plaintext, isFinal);
    }

    /// <summary>No-op. DPAPI is keyless from our perspective; the <see cref="DataProtectionScope"/> field is a value type.</summary>
    public void Dispose()
    {
        // Intentionally empty — present only to satisfy IProtectBackend : IDisposable.
    }
}
