#nullable enable
using System;
using System.IO;
using System.Text;
using Xunit;
using Winix.Codec;
using Winix.Digest;

namespace Winix.Digest.Tests;

public class HashRunnerTests
{
    [Fact]
    public void RunString_ProducesExpectedHash()
    {
        var hasher = HashFactory.Create(HashAlgorithm.Sha256);
        var results = HashRunner.Run(
            source: new StringInput("abc"),
            hasher: hasher,
            stdinPayload: Stream.Null,
            out string? error);
        Assert.Null(error);
        Assert.Single(results);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            Hex.Encode(results[0].Hash));
        Assert.Null(results[0].Path);
    }

    [Fact]
    public void RunStdin_ProducesExpectedHash()
    {
        var hasher = HashFactory.Create(HashAlgorithm.Sha256);
        var results = HashRunner.Run(
            source: new StdinInput(),
            hasher: hasher,
            stdinPayload: new MemoryStream(Encoding.UTF8.GetBytes("abc")),
            out string? error);
        Assert.Null(error);
        Assert.Single(results);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            Hex.Encode(results[0].Hash));
        Assert.Null(results[0].Path);
    }

    // -- Round-2 review CR-I3 — stdin payload must be hashed as raw bytes, not
    //    text+UTF-8-roundtripped. This test pins the byte-precision contract by
    //    feeding a non-UTF-8 byte sequence (0xFF 0xFE — invalid UTF-8) and asserting
    //    the hash matches a direct in-memory hash of the same bytes. Pre-fix this
    //    test fails because the TextReader path replaced 0xFF/0xFE with U+FFFD before
    //    re-encoding, producing a different hash than `sha256sum binary.bin`. --
    [Fact]
    public void RunStdin_BinaryBytes_NotMangledByTextRoundtrip()
    {
        byte[] binary = new byte[] { 0xFF, 0xFE, 0x80, 0x81, 0x00, 0xC0, 0xC1 };
        var hasher = HashFactory.Create(HashAlgorithm.Sha256);

        byte[] expected = hasher.Hash(binary);

        var results = HashRunner.Run(
            source: new StdinInput(),
            hasher: hasher,
            stdinPayload: new MemoryStream(binary),
            out string? error);

        Assert.Null(error);
        Assert.Single(results);
        Assert.Equal(expected, results[0].Hash);
    }

    [Fact]
    public void RunSingleFile_ProducesExpectedHash()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("abc"));
            var hasher = HashFactory.Create(HashAlgorithm.Sha256);
            var results = HashRunner.Run(
                source: new SingleFileInput(path),
                hasher: hasher,
                stdinPayload: Stream.Null,
                out string? error);
            Assert.Null(error);
            Assert.Single(results);
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                Hex.Encode(results[0].Hash));
            Assert.Equal(path, results[0].Path);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RunSingleFile_MissingFile_Errors()
    {
        var hasher = HashFactory.Create(HashAlgorithm.Sha256);
        var results = HashRunner.Run(
            source: new SingleFileInput("/this/file/does/not/exist-12345"),
            hasher: hasher,
            stdinPayload: Stream.Null,
            out string? error);
        Assert.NotNull(error);
        Assert.Contains("not found", error, StringComparison.Ordinal);
        Assert.Empty(results);
    }

    // -- Round-2 review I4 test gap — a file that exists at File.Exists() time but
    //    fails at File.OpenRead time (TOCTOU race, perm change, exclusive-lock contention)
    //    must produce a typed `error` rather than escaping to the caller's catch. The
    //    round-1 I4 fix wired scoped catches around File.OpenRead but had no test
    //    pinning the behaviour. Reproduce by holding the file with FileShare.None. --
    [Fact]
    public void RunSingleFile_LockedFile_TypedError()
    {
        string path = Path.GetTempFileName();
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("abc"));
        try
        {
            using var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            var hasher = HashFactory.Create(HashAlgorithm.Sha256);
            var results = HashRunner.Run(
                source: new SingleFileInput(path),
                hasher: hasher,
                stdinPayload: Stream.Null,
                out string? error);
            // Lenient on POSIX where FileShare.None doesn't block; assert on either path.
            if (error is not null)
            {
                Assert.Contains("failed to read", error, StringComparison.Ordinal);
                Assert.Contains(path, error, StringComparison.Ordinal);
                Assert.Empty(results);
            }
            else
            {
                Assert.Single(results);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RunMultiFile_OneLockedFile_TypedError_NoPartialOutput()
    {
        // Pin the all-or-nothing rule under TOCTOU: validation passes (both files exist),
        // but one OpenRead fails. No results must be returned (the all-or-nothing contract).
        string p1 = Path.GetTempFileName();
        string p2 = Path.GetTempFileName();
        File.WriteAllBytes(p1, Encoding.UTF8.GetBytes("abc"));
        File.WriteAllBytes(p2, Encoding.UTF8.GetBytes("xyz"));
        try
        {
            using var holder = new FileStream(p2, FileMode.Open, FileAccess.Read, FileShare.None);
            var hasher = HashFactory.Create(HashAlgorithm.Sha256);
            var results = HashRunner.Run(
                source: new MultiFileInput(new[] { p1, p2 }),
                hasher: hasher,
                stdinPayload: Stream.Null,
                out string? error);
            if (error is not null)
            {
                Assert.Contains("failed to read", error, StringComparison.Ordinal);
                Assert.Contains(p2, error, StringComparison.Ordinal);
                Assert.Empty(results); // all-or-nothing: no partial output
            }
            else
            {
                // POSIX may not honour FileShare.None — accept the success path too.
                Assert.Equal(2, results.Count);
            }
        }
        finally { File.Delete(p1); File.Delete(p2); }
    }

    [Fact]
    public void RunMultiFile_ProducesOneResultPerFile_InOrder()
    {
        string p1 = Path.GetTempFileName();
        string p2 = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(p1, Encoding.UTF8.GetBytes("abc"));
            File.WriteAllBytes(p2, Encoding.UTF8.GetBytes("xyz"));
            var hasher = HashFactory.Create(HashAlgorithm.Sha256);
            var results = HashRunner.Run(
                source: new MultiFileInput(new[] { p1, p2 }),
                hasher: hasher,
                stdinPayload: Stream.Null,
                out string? error);
            Assert.Null(error);
            Assert.Equal(2, results.Count);
            Assert.Equal(p1, results[0].Path);
            Assert.Equal(p2, results[1].Path);
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                Hex.Encode(results[0].Hash));
            // "xyz" SHA-256:
            Assert.Equal("3608bca1e44ea6c4d268eb6db02260269892c0b42b86bbf1e77a6fa16c3c9282",
                Hex.Encode(results[1].Hash));
        }
        finally { File.Delete(p1); File.Delete(p2); }
    }

    [Fact]
    public void RunMultiFile_MissingFile_ErrorsBeforeAnyOutput()
    {
        // The "all-or-nothing" rule: if any file is missing, no results are returned,
        // so we don't print hashes for files before the bad one (sha256sum compatibility).
        string p1 = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(p1, Encoding.UTF8.GetBytes("abc"));
            string pMissing = "/this/file/definitely/does/not/exist-12345";
            var hasher = HashFactory.Create(HashAlgorithm.Sha256);
            var results = HashRunner.Run(
                source: new MultiFileInput(new[] { p1, pMissing }),
                hasher: hasher,
                stdinPayload: Stream.Null,
                out string? error);
            Assert.NotNull(error);
            Assert.Contains("not found", error, StringComparison.Ordinal);
            Assert.Empty(results);
        }
        finally { File.Delete(p1); }
    }

    // Empty-file stream path: specifically targets the Blake2b incremental hasher's
    // "Update never called, Finish on empty state" path, which can silently misbehave
    // if a future incremental-hasher swap forgets to handle zero-length input.
    // Parametrised across platform-available algos to also lock in SHA-family behaviour.
    [Theory]
    [InlineData(HashAlgorithm.Sha256)]
    [InlineData(HashAlgorithm.Sha384)]
    [InlineData(HashAlgorithm.Sha512)]
    [InlineData(HashAlgorithm.Sha1)]
    [InlineData(HashAlgorithm.Md5)]
    [InlineData(HashAlgorithm.Blake2b)]
    public void RunSingleFile_EmptyFile_MatchesBytesPath(HashAlgorithm algorithm)
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, System.Array.Empty<byte>());
            var hasher = HashFactory.Create(algorithm);

            byte[] bytesHash = hasher.Hash(System.ReadOnlySpan<byte>.Empty);

            var results = HashRunner.Run(
                source: new SingleFileInput(path),
                hasher: hasher,
                stdinPayload: Stream.Null,
                out string? error);

            Assert.Null(error);
            Assert.Single(results);
            Assert.Equal(bytesHash, results[0].Hash);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RunSingleFile_EmptyFile_Hmac_StreamMatchesBytes()
    {
        // Same invariant for the HMAC (keyed) path — HmacBlake2bKeyedHasher uses a
        // different incremental API than the plain hasher, so the empty-input path
        // could in principle diverge.
        byte[] key = Encoding.UTF8.GetBytes("my-key");
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, System.Array.Empty<byte>());
            var hasher = HmacFactory.Create(HashAlgorithm.Blake2b, key);

            byte[] bytesHash = hasher.Hash(System.ReadOnlySpan<byte>.Empty);

            var results = HashRunner.Run(
                source: new SingleFileInput(path),
                hasher: hasher,
                stdinPayload: Stream.Null,
                out string? error);

            Assert.Null(error);
            Assert.Equal(bytesHash, results[0].Hash);
        }
        finally { File.Delete(path); }
    }
}
