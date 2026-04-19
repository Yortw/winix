#nullable enable
using System;
using System.IO;
using System.Text;
using Winix.Codec;
using Winix.Digest;
using Xunit;

namespace Winix.Digest.Tests;

public class HashFactoryTests
{
    // SHA-256 — NIST FIPS 180-4 test vectors.
    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("The quick brown fox jumps over the lazy dog",
                "d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592")]
    public void Sha256_KnownVectors(string input, string expectedHex)
    {
        var hasher = HashFactory.Create(HashAlgorithm.Sha256);
        byte[] hash = hasher.Hash(Encoding.UTF8.GetBytes(input));
        Assert.Equal(expectedHex, Hex.Encode(hash));
    }

    [Theory]
    [InlineData("", "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e")]
    [InlineData("abc", "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f")]
    public void Sha512_KnownVectors(string input, string expectedHex)
    {
        var hasher = HashFactory.Create(HashAlgorithm.Sha512);
        byte[] hash = hasher.Hash(Encoding.UTF8.GetBytes(input));
        Assert.Equal(expectedHex, Hex.Encode(hash));
    }

    [Fact]
    public void Sha1_KnownVector()
    {
        var hasher = HashFactory.Create(HashAlgorithm.Sha1);
        byte[] hash = hasher.Hash(Encoding.UTF8.GetBytes("abc"));
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", Hex.Encode(hash));
    }

    [Fact]
    public void Md5_KnownVector()
    {
        var hasher = HashFactory.Create(HashAlgorithm.Md5);
        byte[] hash = hasher.Hash(Encoding.UTF8.GetBytes("abc"));
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", Hex.Encode(hash));
    }

    [Fact]
    public void Sha3_256_KnownVector()
    {
        if (!System.Security.Cryptography.SHA3_256.IsSupported)
        {
            return; // Skip on platforms without SHA-3 support.
        }
        var hasher = HashFactory.Create(HashAlgorithm.Sha3_256);
        byte[] hash = hasher.Hash(Encoding.UTF8.GetBytes("abc"));
        Assert.Equal("3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532", Hex.Encode(hash));
    }

    [Fact]
    public void Blake2b_KnownVector()
    {
        // RFC 7693 test vector for BLAKE2b of "abc" (512-bit / 64-byte output).
        var hasher = HashFactory.Create(HashAlgorithm.Blake2b);
        byte[] hash = hasher.Hash(Encoding.UTF8.GetBytes("abc"));
        Assert.Equal(
            "ba80a53f981c4d0d6a2797b69f12f6e94c212f14685ac4b74b12bb6fdbffa2d17d87c5392aab792dc252d5de4533cc9518d38aa8dbf1925ab92386edd4009923",
            Hex.Encode(hash));
    }

    [Fact]
    public void Hash_StreamMatches_Hash_Bytes()
    {
        byte[] input = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");
        var hasher = HashFactory.Create(HashAlgorithm.Sha256);

        byte[] bytesHash = hasher.Hash(input);
        byte[] streamHash;
        using (var stream = new MemoryStream(input))
        {
            streamHash = hasher.Hash(stream);
        }

        Assert.Equal(bytesHash, streamHash);
    }

    [Fact]
    public void Blake2b_StreamMatches_Bytes()
    {
        byte[] input = new byte[16384]; // 16 KB — forces multiple buffer fills in stream path
        new Random(42).NextBytes(input);
        var hasher = HashFactory.Create(HashAlgorithm.Blake2b);

        byte[] bytesHash = hasher.Hash(input);
        byte[] streamHash;
        using (var stream = new MemoryStream(input))
        {
            streamHash = hasher.Hash(stream);
        }

        Assert.Equal(bytesHash, streamHash);
    }
}
