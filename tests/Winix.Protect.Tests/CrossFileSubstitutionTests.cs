#nullable enable
using System;
using System.IO;
using Xunit;
using Winix.Protect;
using Winix.SecretStore;

namespace Winix.Protect.Tests;

/// <summary>
/// Pin the contract that a chunk lifted from one .prot file cannot be substituted into another
/// .prot file produced by the same backend with the same key. Per-file FileId binding (introduced
/// in the v0.4.0 format-hardening pass) is what makes this hold; without it, the AAD would be
/// identical across same-marker files and the GCM tag would verify cleanly against a foreign chunk.
/// </summary>
public class CrossFileSubstitutionTests
{
    private sealed class TestAeadBackend : AeadBackend
    {
        public TestAeadBackend(ISecretStore store) : base(store, PlatformMarker.MacKeychainUser, "test-ns", "test-key") { }
    }

    [Fact]
    public void Aead_ChunkSplicedFromAnotherFile_FailsTagVerification()
    {
        NullSecretStore shared = new();
        TestAeadBackend backendA = new(shared);
        TestAeadBackend backendB = new(shared);

        // Identical-length payloads so the encrypted chunks have identical wire-byte length;
        // a real substitution attack assumes the attacker can match offset + length.
        byte[] inputA = new byte[256];
        byte[] inputB = new byte[256];
        Random.Shared.NextBytes(inputA);
        Random.Shared.NextBytes(inputB);

        byte[] hdrA = Header.SerializeForAad(backendA.Marker, Header.NewFileId());
        byte[] hdrB = Header.SerializeForAad(backendB.Marker, Header.NewFileId());

        using MemoryStream cipherA = new();
        using MemoryStream cipherB = new();
        ChunkWriter.Write(new MemoryStream(inputA), cipherA, backendA, hdrA);
        ChunkWriter.Write(new MemoryStream(inputB), cipherB, backendB, hdrB);

        byte[] cA = cipherA.ToArray();
        byte[] cB = cipherB.ToArray();

        // Splice B's encrypted chunk over A's at the same offset (after the 22-byte header).
        byte[] spliced = new byte[cA.Length];
        Array.Copy(cA, spliced, cA.Length);
        Array.Copy(cB, Header.Length, spliced, Header.Length, cB.Length - Header.Length);

        using MemoryStream readStream = new(spliced, Header.Length, spliced.Length - Header.Length);
        using MemoryStream sink = new();
        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
            () => ChunkReader.Read(readStream, sink, backendA, hdrA));
    }
}
