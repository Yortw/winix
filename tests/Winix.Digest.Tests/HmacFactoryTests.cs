#nullable enable
using System;
using System.IO;
using System.Text;
using Xunit;
using Winix.Codec;
using Winix.Digest;

namespace Winix.Digest.Tests;

public class HmacFactoryTests
{
    // RFC 4231 test case 1: key = 0x0b × 20, data = "Hi There"
    [Fact]
    public void HmacSha256_Rfc4231_TestCase1()
    {
        byte[] key = new byte[20];
        Array.Fill(key, (byte)0x0b);
        byte[] data = Encoding.UTF8.GetBytes("Hi There");

        var hasher = HmacFactory.Create(HashAlgorithm.Sha256, key);
        byte[] hash = hasher.Hash(data);

        Assert.Equal(
            "b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7",
            Hex.Encode(hash));
    }

    [Fact]
    public void HmacSha512_Rfc4231_TestCase1()
    {
        byte[] key = new byte[20];
        Array.Fill(key, (byte)0x0b);
        byte[] data = Encoding.UTF8.GetBytes("Hi There");

        var hasher = HmacFactory.Create(HashAlgorithm.Sha512, key);
        byte[] hash = hasher.Hash(data);

        Assert.Equal(
            "87aa7cdea5ef619d4ff0b4241a1d6cb02379f4e2ce4ec2787ad0b30545e17cdedaa833b7d6b8a702038b274eaea3f4e4be9d914eeb61f1702e696c203a126854",
            Hex.Encode(hash));
    }

    // RFC 4231 test case 2: key = "Jefe", data = "what do ya want for nothing?"
    [Fact]
    public void HmacSha256_Rfc4231_TestCase2_ShortKey()
    {
        byte[] key = Encoding.UTF8.GetBytes("Jefe");
        byte[] data = Encoding.UTF8.GetBytes("what do ya want for nothing?");

        var hasher = HmacFactory.Create(HashAlgorithm.Sha256, key);
        byte[] hash = hasher.Hash(data);

        Assert.Equal(
            "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843",
            Hex.Encode(hash));
    }

    // RFC 2202 test case 1 for HMAC-SHA-1.
    [Fact]
    public void HmacSha1_Rfc2202_TestCase1()
    {
        byte[] key = new byte[20];
        Array.Fill(key, (byte)0x0b);
        byte[] data = Encoding.UTF8.GetBytes("Hi There");

        var hasher = HmacFactory.Create(HashAlgorithm.Sha1, key);
        byte[] hash = hasher.Hash(data);

        Assert.Equal("b617318655057264e28bc0b6fb378c8ef146be00", Hex.Encode(hash));
    }

    // RFC 2104 test case for HMAC-MD5.
    [Fact]
    public void HmacMd5_Rfc2104_TestCase()
    {
        byte[] key = Encoding.ASCII.GetBytes("Jefe");
        byte[] data = Encoding.ASCII.GetBytes("what do ya want for nothing?");

        var hasher = HmacFactory.Create(HashAlgorithm.Md5, key);
        byte[] hash = hasher.Hash(data);

        Assert.Equal("750c783e6ab0b503eaa86e310a5db738", Hex.Encode(hash));
    }

    [Fact]
    public void LongKey_HashedFirstPerSpec()
    {
        // RFC 4231 test case 4: key longer than block size (SHA-256 block = 64 bytes).
        byte[] key = new byte[131];
        Array.Fill(key, (byte)0xaa);
        byte[] data = Encoding.UTF8.GetBytes("Test Using Larger Than Block-Size Key - Hash Key First");

        var hasher = HmacFactory.Create(HashAlgorithm.Sha256, key);
        byte[] hash = hasher.Hash(data);

        Assert.Equal(
            "60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54",
            Hex.Encode(hash));
    }

    [Fact]
    public void HmacSha256_StreamMatches_Bytes()
    {
        byte[] key = Encoding.UTF8.GetBytes("my-secret-key");
        byte[] data = Encoding.UTF8.GetBytes("payload bytes here");

        var hasher = HmacFactory.Create(HashAlgorithm.Sha256, key);
        byte[] bytesHash = hasher.Hash(data);
        byte[] streamHash;
        using (var stream = new MemoryStream(data))
        {
            streamHash = hasher.Hash(stream);
        }

        Assert.Equal(bytesHash, streamHash);
    }

    [Fact]
    public void HmacBlake2b_StreamMatches_Bytes()
    {
        byte[] key = Encoding.UTF8.GetBytes("my-blake2b-key");
        byte[] data = new byte[16384]; // 16 KB — multi-buffer stream path
        new Random(42).NextBytes(data);

        var hasher = HmacFactory.Create(HashAlgorithm.Blake2b, key);
        byte[] bytesHash = hasher.Hash(data);
        byte[] streamHash;
        using (var stream = new MemoryStream(data))
        {
            streamHash = hasher.Hash(stream);
        }

        Assert.Equal(bytesHash, streamHash);
    }

    // SHA-3 platform guard — see HashFactoryTests.cs Sha3_256_KnownVector for rationale.
    [Fact]
    public void HmacSha3_256_Available()
    {
        if (!System.Security.Cryptography.HMACSHA3_256.IsSupported) return;

        byte[] key = Encoding.UTF8.GetBytes("key");
        byte[] data = Encoding.UTF8.GetBytes("data");
        var hasher = HmacFactory.Create(HashAlgorithm.Sha3_256, key);
        byte[] hash = hasher.Hash(data);
        // Just sanity-check it's 32 bytes and non-zero; the BCL's correctness is already vouched for.
        Assert.Equal(32, hash.Length);
        Assert.NotEqual(new byte[32], hash);
    }

    [Fact]
    public void HmacSha3_512_Available()
    {
        if (!System.Security.Cryptography.HMACSHA3_512.IsSupported) return;

        byte[] key = Encoding.UTF8.GetBytes("key");
        byte[] data = Encoding.UTF8.GetBytes("data");
        var hasher = HmacFactory.Create(HashAlgorithm.Sha3_512, key);
        byte[] hash = hasher.Hash(data);
        Assert.Equal(64, hash.Length);
        Assert.NotEqual(new byte[64], hash);
    }

    [Fact]
    public void Blake2b_KeyOver64Bytes_Throws()
    {
        byte[] key = new byte[65];
        Array.Fill(key, (byte)0xAB);
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => HmacFactory.Create(HashAlgorithm.Blake2b, key));
        Assert.Contains("64 bytes", ex.Message);
    }

    [Fact]
    public void Blake2b_Key64Bytes_IsAllowed()
    {
        // Exactly-at-boundary should succeed.
        byte[] key = new byte[64];
        Array.Fill(key, (byte)0xCD);
        byte[] data = Encoding.UTF8.GetBytes("payload");
        var hasher = HmacFactory.Create(HashAlgorithm.Blake2b, key);
        byte[] hash = hasher.Hash(data);
        Assert.Equal(64, hash.Length);
    }
}
