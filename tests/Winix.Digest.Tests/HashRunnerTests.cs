#nullable enable
using System.IO;
using System.Text;
using Xunit;
using Winix.Codec;
using Winix.Digest;
using Winix.Digest.Tests.Fakes;

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
            stdin: new FakeTextReader(""),
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
            stdin: new FakeTextReader("abc"),
            out string? error);
        Assert.Null(error);
        Assert.Single(results);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            Hex.Encode(results[0].Hash));
        Assert.Null(results[0].Path);
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
                stdin: new FakeTextReader(""),
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
            stdin: new FakeTextReader(""),
            out string? error);
        Assert.NotNull(error);
        Assert.Contains("not found", error);
        Assert.Empty(results);
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
                stdin: new FakeTextReader(""),
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
                stdin: new FakeTextReader(""),
                out string? error);
            Assert.NotNull(error);
            Assert.Contains("not found", error);
            Assert.Empty(results);
        }
        finally { File.Delete(p1); }
    }
}
