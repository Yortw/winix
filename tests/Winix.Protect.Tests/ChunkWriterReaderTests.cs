#nullable enable
using System;
using System.IO;
using Xunit;
using Winix.Protect;
using Winix.SecretStore;

namespace Winix.Protect.Tests;

public class ChunkWriterReaderTests
{
    private sealed class TestAeadBackend : AeadBackend
    {
        public TestAeadBackend(ISecretStore store) : base(store, PlatformMarker.MacKeychainUser, "test-ns", "test-key") { }
    }

    private static (byte[] ciphertext, byte[] plaintext) EncodeThenRead(byte[] plaintext, int chunkSize = 64 * 1024)
    {
        NullSecretStore store = new();
        TestAeadBackend backend = new(store);
        byte[] header = [(byte)'W', (byte)'P', (byte)'R', (byte)'T', 0x01, (byte)PlatformMarker.MacKeychainUser];

        using MemoryStream cipherStream = new();
        using MemoryStream sourceStream = new(plaintext);
        ChunkWriter.Write(sourceStream, cipherStream, backend, header, chunkSize);

        byte[] encrypted = cipherStream.ToArray();
        using MemoryStream readStream = new(encrypted, 6, encrypted.Length - 6);
        using MemoryStream outStream = new();
        ChunkReader.Read(readStream, outStream, backend, header);
        return (encrypted, outStream.ToArray());
    }

    [Fact]
    public void RoundTrip_EmptyPayload_Works()
    {
        (byte[] _, byte[] decrypted) = EncodeThenRead(Array.Empty<byte>());
        Assert.Empty(decrypted);
    }

    [Fact]
    public void RoundTrip_SingleByte_Works()
    {
        byte[] input = [0x42];
        (byte[] _, byte[] decrypted) = EncodeThenRead(input);
        Assert.Equal(input, decrypted);
    }

    [Fact]
    public void RoundTrip_SmallPayload_OneChunk()
    {
        byte[] input = new byte[1024];
        Random.Shared.NextBytes(input);
        (byte[] _, byte[] decrypted) = EncodeThenRead(input);
        Assert.Equal(input, decrypted);
    }

    [Fact]
    public void RoundTrip_MultiChunkPayload_ViaSmallChunkSize()
    {
        byte[] input = new byte[200_000];
        Random.Shared.NextBytes(input);
        (byte[] _, byte[] decrypted) = EncodeThenRead(input, chunkSize: 64_000);
        Assert.Equal(input, decrypted);
    }

    [Fact]
    public void Truncation_FinalChunkDropped_Throws()
    {
        NullSecretStore store = new();
        TestAeadBackend backend = new(store);
        byte[] header = [(byte)'W', (byte)'P', (byte)'R', (byte)'T', 0x01, (byte)PlatformMarker.MacKeychainUser];

        byte[] input = new byte[100_000];
        Random.Shared.NextBytes(input);

        using MemoryStream cipherStream = new();
        using MemoryStream sourceStream = new(input);
        ChunkWriter.Write(sourceStream, cipherStream, backend, header, chunkSize: 50_000);

        byte[] encrypted = cipherStream.ToArray();
        byte[] truncated = new byte[encrypted.Length - 30_000];
        Array.Copy(encrypted, truncated, truncated.Length);

        using MemoryStream readStream = new(truncated, 6, truncated.Length - 6);
        using MemoryStream outStream = new();
        Assert.Throws<FormatException>(() => ChunkReader.Read(readStream, outStream, backend, header));
    }
}
