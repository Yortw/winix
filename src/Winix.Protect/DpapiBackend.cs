#nullable enable
using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Winix.Protect;

/// <summary>
/// Windows DPAPI-backed per-chunk encryption. Each chunk is framed with a single leading
/// <c>is_final</c> byte before being passed through <see cref="ProtectedData"/>. DPAPI already
/// provides integrity and tamper-detection, so no additional AEAD plumbing is required.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiBackend : IProtectBackend
{
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
        byte[] framed = new byte[plaintext.Length + 1];
        framed[0] = isFinal ? (byte)1 : (byte)0;
        Array.Copy(plaintext, 0, framed, 1, plaintext.Length);
        return ProtectedData.Protect(framed, optionalEntropy: null, _scope);
    }

    /// <inheritdoc />
    public (byte[] plaintext, bool isFinal) DecryptChunk(byte[] chunkPayload, AadContext aad)
    {
        byte[] framed = ProtectedData.Unprotect(chunkPayload, optionalEntropy: null, _scope);
        if (framed.Length < 1)
        {
            throw new CryptographicException("DPAPI payload too short (missing is_final byte).");
        }
        bool isFinal = framed[0] == 1;
        byte[] plaintext = new byte[framed.Length - 1];
        Array.Copy(framed, 1, plaintext, 0, plaintext.Length);
        return (plaintext, isFinal);
    }
}
