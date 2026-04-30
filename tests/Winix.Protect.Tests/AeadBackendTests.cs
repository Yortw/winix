#nullable enable
using System;
using Xunit;
using Winix.Protect;
using Winix.SecretStore;

namespace Winix.Protect.Tests;

public class AeadBackendTests
{
    private sealed class TestAeadBackend : AeadBackend
    {
        public TestAeadBackend(ISecretStore store) : base(store, PlatformMarker.MacKeychainUser, "test-namespace", "test-key") { }
    }

    [Fact]
    public void EncryptDecrypt_RoundTrips()
    {
        TestAeadBackend backend = new(new NullSecretStore());
        byte[] plaintext = System.Text.Encoding.UTF8.GetBytes("hello");
        AadContext aad = new(Header.SerializeForAad(PlatformMarker.MacKeychainUser, new byte[16]), 0, true);
        byte[] chunk = backend.EncryptChunk(plaintext, aad, isFinal: true);
        (byte[] decrypted, bool isFinal) = backend.DecryptChunk(chunk, aad);
        Assert.Equal(plaintext, decrypted);
        Assert.True(isFinal);
    }

    [Fact]
    public void EncryptChunk_LayoutIsFinalFlagIvLengthCiphertextTag()
    {
        TestAeadBackend backend = new(new NullSecretStore());
        byte[] plaintext = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        AadContext aad = new(Header.SerializeForAad(PlatformMarker.MacKeychainUser, new byte[16]), 0, true);
        byte[] chunk = backend.EncryptChunk(plaintext, aad, isFinal: true);

        Assert.Equal(1, chunk[0]);
        int length = (chunk[13] << 24) | (chunk[14] << 16) | (chunk[15] << 8) | chunk[16];
        Assert.Equal(4, length);
        Assert.Equal(1 + 12 + 4 + 4 + 16, chunk.Length);
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        TestAeadBackend backend = new(new NullSecretStore());
        AadContext aad = new(Header.SerializeForAad(PlatformMarker.MacKeychainUser, new byte[16]), 0, true);
        byte[] chunk = backend.EncryptChunk([1, 2, 3], aad, isFinal: true);
        chunk[17] ^= 0x01;
        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
            () => backend.DecryptChunk(chunk, aad));
    }

    [Fact]
    public void Decrypt_WrongAad_Throws()
    {
        TestAeadBackend backend = new(new NullSecretStore());
        byte[] hdr = Header.SerializeForAad(PlatformMarker.MacKeychainUser, new byte[16]);
        AadContext encryptAad = new(hdr, 0, true);
        AadContext decryptAad = new(hdr, 1, true);
        byte[] chunk = backend.EncryptChunk([1, 2, 3], encryptAad, isFinal: true);
        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
            () => backend.DecryptChunk(chunk, decryptAad));
    }

    [Fact]
    public void Decrypt_ChunkFromDifferentFileId_Throws()
    {
        TestAeadBackend backend = new(new NullSecretStore());

        byte[] fileIdA = Header.NewFileId();
        byte[] fileIdB = Header.NewFileId();

        AadContext aadA = new(Header.SerializeForAad(PlatformMarker.MacKeychainUser, fileIdA), 0, true);
        AadContext aadB = new(Header.SerializeForAad(PlatformMarker.MacKeychainUser, fileIdB), 0, true);

        byte[] chunkFromB = backend.EncryptChunk([1, 2, 3], aadB, isFinal: true);

        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
            () => backend.DecryptChunk(chunkFromB, aadA));
    }

    [Fact]
    public void Key_IsGeneratedOnFirstUseAndReusedAfter()
    {
        NullSecretStore shared = new();
        TestAeadBackend one = new(shared);
        TestAeadBackend two = new(shared);
        byte[] plaintext = [1, 2, 3, 4];
        AadContext aad = new(Header.SerializeForAad(PlatformMarker.MacKeychainUser, new byte[16]), 0, true);
        byte[] chunk = one.EncryptChunk(plaintext, aad, isFinal: true);
        (byte[] decrypted, _) = two.DecryptChunk(chunk, aad);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Key_WrongSizeExisting_ThrowsInsteadOfOverwriting()
    {
        NullSecretStore store = new();
        // Plant a wrong-size value at the slot the backend will look at.
        store.Set("test-namespace", "test-key", new byte[16]);

        TestAeadBackend backend = new(store);

        AadContext aad = new(Header.SerializeForAad(PlatformMarker.MacKeychainUser, new byte[16]), 0, true);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => backend.EncryptChunk([1, 2, 3], aad, isFinal: true));
        Assert.Contains("test-namespace", ex.Message);
        Assert.Contains("test-key", ex.Message);

        // Ensure the planted key was NOT overwritten.
        byte[]? existing = store.Get("test-namespace", "test-key");
        Assert.NotNull(existing);
        Assert.Equal(16, existing!.Length);
    }

    [Fact]
    public void Key_GenerationRefusesToSilentlyOverwriteWhenStoreReportsTryAddFalse()
    {
        // Pin the load-bearing safety property: if the secret store reports Get == null
        // but TryAdd refuses (entry already exists from the store's perspective), the
        // AEAD backend must NOT generate a fresh master key and overwrite — that would
        // permanently destroy decryptability of every previously-protected file.
        // Instead it must throw with a clear "consistency error" message, leaving the
        // existing entry untouched so the user can recover. Mirrors the macOS Keychain
        // search-list-vs-default-keychain inconsistency uncovered in CI.
        InconsistentReadStore store = new();
        TestAeadBackend backend = new(store);

        AadContext aad = new(Header.SerializeForAad(PlatformMarker.MacKeychainUser, new byte[16]), 0, true);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => backend.EncryptChunk([1, 2, 3], aad, isFinal: true));
        Assert.Contains("consistency error", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test fake that emulates the macOS Keychain search-list-vs-default-keychain split:
    /// <see cref="Get"/> always returns null (search list misses the entry), while
    /// <see cref="TryAdd"/> always returns false (default keychain rejects "already exists").
    /// Without these dual semantics, <see cref="AeadBackend.GetOrCreateKey"/> would have
    /// no way to detect the inconsistency before silently overwriting.
    /// </summary>
    private sealed class InconsistentReadStore : ISecretStore
    {
        public void Set(string namespace_, string key, byte[] value) { /* no-op */ }
        public bool TryAdd(string namespace_, string key, byte[] value) => false;
        public byte[]? Get(string namespace_, string key) => null;
        public bool Delete(string namespace_, string key) => false;
        public System.Collections.Generic.IReadOnlyList<string> ListKeys(string namespace_) => System.Array.Empty<string>();
        public System.Collections.Generic.IReadOnlyList<string> ListNamespaces(string toolPrefix) => System.Array.Empty<string>();
    }

    [Fact]
    public void Dispose_ZeroesCachedKey()
    {
        NullSecretStore store = new();
        TestAeadBackend backend = new(store);

        // Force key materialisation by encrypting one chunk.
        AadContext aad = new(Header.SerializeForAad(PlatformMarker.MacKeychainUser, new byte[16]), 0, true);
        backend.EncryptChunk([1, 2, 3], aad, isFinal: true);

        byte[]? before = backend.PeekCachedKeyForTests();
        Assert.NotNull(before);
        Assert.Equal(32, before!.Length);
        bool allZeroBefore = true;
        foreach (byte b in before) { if (b != 0) { allZeroBefore = false; break; } }
        Assert.False(allZeroBefore, "key should be random pre-Dispose");

        // Capture the buffer reference so we can inspect it after Dispose nulls _cachedKey.
        byte[] capturedRef = before!;
        backend.Dispose();

        foreach (byte b in capturedRef)
        {
            Assert.Equal(0, b);
        }
    }
}
