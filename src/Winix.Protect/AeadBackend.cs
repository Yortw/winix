#nullable enable
using System;
using System.Security.Cryptography;
using Winix.SecretStore;

namespace Winix.Protect;

/// <summary>
/// AES-256-GCM backend template. Subclasses inject a platform-specific <see cref="ISecretStore"/>
/// and the service/key pair to use for persistent AEAD-key storage. The key is generated
/// on first use and reused on subsequent encrypt/decrypt calls.
/// </summary>
public abstract class AeadBackend : IProtectBackend
{
    private const int IvSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly ISecretStore _store;
    private readonly string _namespace;
    private readonly string _keyName;
    private byte[]? _cachedKey;

    /// <summary>Construct an AEAD backend that stores its 256-bit key under <paramref name="namespace_"/>/<paramref name="keyName"/> in the given <paramref name="store"/>.</summary>
    protected AeadBackend(ISecretStore store, PlatformMarker marker, string namespace_, string keyName)
    {
        _store = store;
        Marker = marker;
        _namespace = namespace_;
        _keyName = keyName;
    }

    /// <inheritdoc />
    public PlatformMarker Marker { get; }

    /// <inheritdoc />
    public byte[] EncryptChunk(byte[] plaintext, AadContext aad, bool isFinal)
    {
        byte[] key = GetOrCreateKey();
        byte[] iv = new byte[IvSize];
        RandomNumberGenerator.Fill(iv);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];
        byte[] aadBytes = BuildAadBytes(aad, isFinal);

        using AesGcm gcm = new(key, TagSize);
        gcm.Encrypt(iv, plaintext, ciphertext, tag, aadBytes);

        byte[] chunk = new byte[1 + IvSize + 4 + ciphertext.Length + TagSize];
        chunk[0] = isFinal ? (byte)1 : (byte)0;
        Array.Copy(iv, 0, chunk, 1, IvSize);
        chunk[13] = (byte)(ciphertext.Length >> 24);
        chunk[14] = (byte)(ciphertext.Length >> 16);
        chunk[15] = (byte)(ciphertext.Length >> 8);
        chunk[16] = (byte)ciphertext.Length;
        Array.Copy(ciphertext, 0, chunk, 17, ciphertext.Length);
        Array.Copy(tag, 0, chunk, 17 + ciphertext.Length, TagSize);
        return chunk;
    }

    /// <inheritdoc />
    public (byte[] plaintext, bool isFinal) DecryptChunk(byte[] chunkPayload, AadContext aad)
    {
        if (chunkPayload.Length < 1 + IvSize + 4 + TagSize)
        {
            throw new FormatException("Chunk too short.");
        }

        bool isFinal = chunkPayload[0] == 1;
        byte[] iv = new byte[IvSize];
        Array.Copy(chunkPayload, 1, iv, 0, IvSize);
        int length = (chunkPayload[13] << 24) | (chunkPayload[14] << 16) | (chunkPayload[15] << 8) | chunkPayload[16];
        if (chunkPayload.Length != 1 + IvSize + 4 + length + TagSize)
        {
            throw new FormatException("Chunk length field does not match payload length.");
        }
        byte[] ciphertext = new byte[length];
        Array.Copy(chunkPayload, 17, ciphertext, 0, length);
        byte[] tag = new byte[TagSize];
        Array.Copy(chunkPayload, 17 + length, tag, 0, TagSize);

        byte[] key = GetOrCreateKey();
        byte[] plaintext = new byte[length];
        byte[] aadBytes = BuildAadBytes(aad, isFinal);
        using AesGcm gcm = new(key, TagSize);
        gcm.Decrypt(iv, ciphertext, tag, plaintext, aadBytes);
        return (plaintext, isFinal);
    }

    private byte[] GetOrCreateKey()
    {
        if (_cachedKey is not null) return _cachedKey;
        byte[]? existing = _store.Get(_namespace, _keyName);
        if (existing is not null)
        {
            if (existing.Length != KeySize)
            {
                throw new InvalidOperationException(
                    $"Existing key in '{_namespace}/{_keyName}' has wrong size ({existing.Length} bytes; expected {KeySize}). " +
                    $"Refusing to overwrite — encrypted files using this key would become permanently undecryptable. " +
                    $"Manually delete the keychain/libsecret entry to regenerate.");
            }
            _cachedKey = existing;
            return existing;
        }
        byte[] fresh = new byte[KeySize];
        RandomNumberGenerator.Fill(fresh);
        _store.Set(_namespace, _keyName, fresh);
        _cachedKey = fresh;
        return fresh;
    }

    private static byte[] BuildAadBytes(AadContext aad, bool isFinal)
    {
        byte[] buffer = new byte[aad.HeaderBytes.Length + 8 + 1];
        Array.Copy(aad.HeaderBytes, 0, buffer, 0, aad.HeaderBytes.Length);
        long idx = aad.ChunkIndex;
        int off = aad.HeaderBytes.Length;
        buffer[off + 0] = (byte)(idx >> 56);
        buffer[off + 1] = (byte)(idx >> 48);
        buffer[off + 2] = (byte)(idx >> 40);
        buffer[off + 3] = (byte)(idx >> 32);
        buffer[off + 4] = (byte)(idx >> 24);
        buffer[off + 5] = (byte)(idx >> 16);
        buffer[off + 6] = (byte)(idx >> 8);
        buffer[off + 7] = (byte)idx;
        buffer[off + 8] = isFinal ? (byte)1 : (byte)0;
        return buffer;
    }
}
