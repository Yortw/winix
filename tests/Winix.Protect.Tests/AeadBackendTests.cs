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
}
