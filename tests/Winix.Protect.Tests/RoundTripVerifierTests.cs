#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;
using Winix.Protect;
using Winix.SecretStore;

namespace Winix.Protect.Tests;

public class RoundTripVerifierTests
{
    private sealed class TestAeadBackend : AeadBackend
    {
        public TestAeadBackend(ISecretStore store) : base(store, PlatformMarker.MacKeychainUser, "test-ns", "test-key") { }
    }

    [Fact]
    public void Verify_MatchingRoundTrip_Passes()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("hello world");
        NullSecretStore store = new();
        TestAeadBackend backend = new(store);
        byte[] header = Header.SerializeForAad(PlatformMarker.MacKeychainUser, new byte[16]);

        byte[] sourceHash;
        using (IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            hasher.AppendData(input);
            sourceHash = hasher.GetCurrentHash();
        }

        using MemoryStream encrypted = new();
        ChunkWriter.Write(new MemoryStream(input), encrypted, backend, header);
        encrypted.Position = 0;

        RoundTripVerifier.Verify(encrypted, backend, sourceHash);
    }

    [Fact]
    public void Verify_MismatchedHash_Throws()
    {
        byte[] input = new byte[] { 1, 2, 3 };
        NullSecretStore store = new();
        TestAeadBackend backend = new(store);
        byte[] header = Header.SerializeForAad(PlatformMarker.MacKeychainUser, new byte[16]);

        using MemoryStream encrypted = new();
        ChunkWriter.Write(new MemoryStream(input), encrypted, backend, header);
        encrypted.Position = 0;

        byte[] wrongHash = new byte[32];
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => RoundTripVerifier.Verify(encrypted, backend, wrongHash));
        Assert.Contains("integrity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
