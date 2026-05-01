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
        byte[] header = Header.SerializeForAad(PlatformMarker.MacKeychainUser, new byte[16]);

        using MemoryStream cipherStream = new();
        using MemoryStream sourceStream = new(plaintext);
        ChunkWriter.Write(sourceStream, cipherStream, backend, header, chunkSize);

        byte[] encrypted = cipherStream.ToArray();
        using MemoryStream readStream = new(encrypted, Header.Length, encrypted.Length - Header.Length);
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
        byte[] header = Header.SerializeForAad(PlatformMarker.MacKeychainUser, new byte[16]);

        byte[] input = new byte[100_000];
        Random.Shared.NextBytes(input);

        using MemoryStream cipherStream = new();
        using MemoryStream sourceStream = new(input);
        ChunkWriter.Write(sourceStream, cipherStream, backend, header, chunkSize: 50_000);

        byte[] encrypted = cipherStream.ToArray();
        byte[] truncated = new byte[encrypted.Length - 30_000];
        Array.Copy(encrypted, truncated, truncated.Length);

        using MemoryStream readStream = new(truncated, Header.Length, truncated.Length - Header.Length);
        using MemoryStream outStream = new();
        Assert.Throws<FormatException>(() => ChunkReader.Read(readStream, outStream, backend, header));
    }

    [SkippableFact]
    public void Dpapi_RoundTrip_SinglePayload_Works()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
        if (!OperatingSystem.IsWindows()) return; // CA1416 analyzer requires this; deliberate redundancy
#pragma warning disable CA1416
        DpapiBackend backend = new(Scope.User);
#pragma warning restore CA1416
        byte[] header = Header.SerializeForAad(PlatformMarker.WindowsDpapiUser, new byte[16]);

        byte[] input = System.Text.Encoding.UTF8.GetBytes("hello from dpapi chunk round-trip");

        using MemoryStream cipherStream = new();
        using MemoryStream sourceStream = new(input);
        ChunkWriter.Write(sourceStream, cipherStream, backend, header);

        byte[] encrypted = cipherStream.ToArray();
        using MemoryStream readStream = new(encrypted, Header.Length, encrypted.Length - Header.Length);
        using MemoryStream outStream = new();
        ChunkReader.Read(readStream, outStream, backend, header);

        Assert.Equal(input, outStream.ToArray());
    }

    [SkippableFact]
    public void Dpapi_RoundTrip_MultiChunkPayload_Works()
    {
        // Critical regression test: if ChunkWriter drops the 4-byte length prefix
        // for DPAPI chunks, this test fails with "Truncated DPAPI chunk blob" because
        // ChunkReader can't frame the next chunk without knowing how many bytes to consume.
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
        if (!OperatingSystem.IsWindows()) return; // CA1416 analyzer requires this; deliberate redundancy
#pragma warning disable CA1416
        DpapiBackend backend = new(Scope.User);
#pragma warning restore CA1416
        byte[] header = Header.SerializeForAad(PlatformMarker.WindowsDpapiUser, new byte[16]);

        byte[] input = new byte[200_000];
        Random.Shared.NextBytes(input);

        using MemoryStream cipherStream = new();
        using MemoryStream sourceStream = new(input);
        ChunkWriter.Write(sourceStream, cipherStream, backend, header, chunkSize: 64_000);

        byte[] encrypted = cipherStream.ToArray();
        using MemoryStream readStream = new(encrypted, Header.Length, encrypted.Length - Header.Length);
        using MemoryStream outStream = new();
        ChunkReader.Read(readStream, outStream, backend, header);

        Assert.Equal(input, outStream.ToArray());
    }

    [SkippableFact]
    public void Dpapi_RoundTrip_OneByte_Works()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
        if (!OperatingSystem.IsWindows()) return; // CA1416 analyzer requires this; deliberate redundancy
#pragma warning disable CA1416
        DpapiBackend backend = new(Scope.User);
#pragma warning restore CA1416
        byte[] header = Header.SerializeForAad(PlatformMarker.WindowsDpapiUser, Header.NewFileId());

        byte[] input = [0x42];

        using MemoryStream cipherStream = new();
        using MemoryStream sourceStream = new(input);
        ChunkWriter.Write(sourceStream, cipherStream, backend, header);

        byte[] encrypted = cipherStream.ToArray();
        using MemoryStream readStream = new(encrypted, Header.Length, encrypted.Length - Header.Length);
        using MemoryStream outStream = new();
        ChunkReader.Read(readStream, outStream, backend, header);

        Assert.Equal(input, outStream.ToArray());
    }

    [SkippableFact]
    public void Dpapi_RoundTrip_EmptyPayload_Works()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
        if (!OperatingSystem.IsWindows()) return; // CA1416 analyzer requires this; deliberate redundancy
#pragma warning disable CA1416
        DpapiBackend backend = new(Scope.User);
#pragma warning restore CA1416
        byte[] header = Header.SerializeForAad(PlatformMarker.WindowsDpapiUser, new byte[16]);

        using MemoryStream cipherStream = new();
        using MemoryStream sourceStream = new(Array.Empty<byte>());
        ChunkWriter.Write(sourceStream, cipherStream, backend, header);

        byte[] encrypted = cipherStream.ToArray();
        using MemoryStream readStream = new(encrypted, Header.Length, encrypted.Length - Header.Length);
        using MemoryStream outStream = new();
        ChunkReader.Read(readStream, outStream, backend, header);

        Assert.Empty(outStream.ToArray());
    }
}
