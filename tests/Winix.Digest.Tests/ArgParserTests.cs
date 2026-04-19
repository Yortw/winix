#nullable enable
using System;
using Xunit;
using Winix.Digest;

namespace Winix.Digest.Tests;

public class ArgParserTests
{
    [Fact]
    public void Parse_NoArgs_DefaultsToSha256StdinHex()
    {
        var r = ArgParser.Parse(Array.Empty<string>());
        Assert.True(r.Success);
        Assert.Equal(HashAlgorithm.Sha256, r.Options!.Algorithm);
        Assert.False(r.Options.IsHmac);
        Assert.Equal(OutputFormat.Hex, r.Options.Format);
        Assert.IsType<StdinInput>(r.Options.Source);
    }

    [Theory]
    [InlineData("--sha256", HashAlgorithm.Sha256)]
    [InlineData("--sha384", HashAlgorithm.Sha384)]
    [InlineData("--sha512", HashAlgorithm.Sha512)]
    [InlineData("--sha1", HashAlgorithm.Sha1)]
    [InlineData("--md5", HashAlgorithm.Md5)]
    [InlineData("--sha3-256", HashAlgorithm.Sha3_256)]
    [InlineData("--sha3-512", HashAlgorithm.Sha3_512)]
    [InlineData("--blake2b", HashAlgorithm.Blake2b)]
    public void Parse_IndividualAlgorithmFlags(string flag, HashAlgorithm expected)
    {
        var r = ArgParser.Parse(new[] { flag, "-s", "abc" });
        Assert.True(r.Success);
        Assert.Equal(expected, r.Options!.Algorithm);
    }

    [Theory]
    [InlineData("sha256", HashAlgorithm.Sha256)]
    [InlineData("sha3-256", HashAlgorithm.Sha3_256)]
    [InlineData("blake2b", HashAlgorithm.Blake2b)]
    public void Parse_AlgoFlag(string value, HashAlgorithm expected)
    {
        var r = ArgParser.Parse(new[] { "--algo", value, "-s", "abc" });
        Assert.True(r.Success);
        Assert.Equal(expected, r.Options!.Algorithm);
    }

    [Fact]
    public void Parse_MultipleAlgorithmFlags_Errors()
    {
        var r = ArgParser.Parse(new[] { "--sha256", "--sha512", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("multiple algorithms", r.Error);
    }

    [Fact]
    public void Parse_UnknownAlgo_Errors()
    {
        var r = ArgParser.Parse(new[] { "--algo", "weirdhash", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("unknown algorithm", r.Error);
    }

    [Fact]
    public void Parse_Hmac_RequiresKeySource()
    {
        var r = ArgParser.Parse(new[] { "--hmac", "sha256", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("--hmac requires", r.Error);
    }

    [Fact]
    public void Parse_Hmac_WithMultipleKeySources_Errors()
    {
        var r = ArgParser.Parse(new[] {
            "--hmac", "sha256",
            "--key-env", "MY_KEY",
            "--key-file", "/tmp/k",
            "-s", "abc"
        });
        Assert.False(r.Success);
        Assert.Contains("exactly one of", r.Error);
    }

    [Fact]
    public void Parse_Hmac_WithAlgorithmFlag_Errors()
    {
        var r = ArgParser.Parse(new[] { "--hmac", "sha256", "--sha512", "--key-env", "K", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("--hmac carries its own algorithm", r.Error);
    }

    [Fact]
    public void Parse_Hmac_WithKeyEnv_PopulatesKeySource()
    {
        var r = ArgParser.Parse(new[] { "--hmac", "sha256", "--key-env", "MY_KEY", "-s", "abc" });
        Assert.True(r.Success);
        Assert.True(r.Options!.IsHmac);
        Assert.NotNull(r.KeySourceForHmac);
        Assert.IsType<KeySource.EnvSource>(r.KeySourceForHmac);
    }

    [Fact]
    public void Parse_String_WithPositional_Errors()
    {
        var r = ArgParser.Parse(new[] { "-s", "hello", "file.txt" });
        Assert.False(r.Success);
        Assert.Contains("--string cannot be combined with file arguments", r.Error);
    }

    [Fact]
    public void Parse_MultipleStrings_Errors()
    {
        var r = ArgParser.Parse(new[] { "-s", "hello", "--string", "world" });
        Assert.False(r.Success);
        Assert.Contains("--string can only be specified once", r.Error);
    }

    [Fact]
    public void Parse_MultipleOutputFormats_Errors()
    {
        var r = ArgParser.Parse(new[] { "--hex", "--base64", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("multiple output formats", r.Error);
    }

    [Theory]
    [InlineData("--hex", OutputFormat.Hex)]
    [InlineData("--base64", OutputFormat.Base64)]
    [InlineData("--base64-url", OutputFormat.Base64Url)]
    [InlineData("--base32", OutputFormat.Base32)]
    public void Parse_OutputFormatFlags(string flag, OutputFormat expected)
    {
        var r = ArgParser.Parse(new[] { flag, "-s", "abc" });
        Assert.True(r.Success);
        Assert.Equal(expected, r.Options!.Format);
    }

    [Fact]
    public void Parse_StringFlag_ProducesStringInput()
    {
        var r = ArgParser.Parse(new[] { "-s", "hello world" });
        Assert.True(r.Success);
        Assert.IsType<StringInput>(r.Options!.Source);
        Assert.Equal("hello world", ((StringInput)r.Options.Source).Value);
    }

    [Fact]
    public void Parse_NoPositional_NoStringFlag_DefaultsToStdin()
    {
        var r = ArgParser.Parse(Array.Empty<string>());
        Assert.True(r.Success);
        Assert.IsType<StdinInput>(r.Options!.Source);
    }

    [Fact]
    public void Parse_SingleDash_DefaultsToStdin()
    {
        var r = ArgParser.Parse(new[] { "-" });
        Assert.True(r.Success);
        Assert.IsType<StdinInput>(r.Options!.Source);
    }

    [Fact]
    public void Parse_SingleFile_ProducesSingleFileInput()
    {
        // Use the test assembly itself — guaranteed to exist.
        string self = typeof(ArgParserTests).Assembly.Location;
        var r = ArgParser.Parse(new[] { self });
        Assert.True(r.Success);
        Assert.IsType<SingleFileInput>(r.Options!.Source);
        Assert.Equal(self, ((SingleFileInput)r.Options.Source).Path);
    }

    [Fact]
    public void Parse_MissingSingleFile_ErrorsWithStringHint()
    {
        var r = ArgParser.Parse(new[] { "/this/file/does/not/exist-xyz123" });
        Assert.False(r.Success);
        Assert.Contains("not found", r.Error);
        Assert.Contains("--string", r.Error);
    }

    [Fact]
    public void Parse_MultipleFiles_ProducesMultiFileInput()
    {
        string self = typeof(ArgParserTests).Assembly.Location;
        var r = ArgParser.Parse(new[] { self, self });
        Assert.True(r.Success);
        Assert.IsType<MultiFileInput>(r.Options!.Source);
    }

    [Fact]
    public void Parse_VerifyWithMultiFile_Errors()
    {
        string self = typeof(ArgParserTests).Assembly.Location;
        var r = ArgParser.Parse(new[] { "--verify", "abc", self, self });
        Assert.False(r.Success);
        Assert.Contains("--verify is not supported with multiple files", r.Error);
    }

    [Fact]
    public void Parse_KeyRaw_SetsStripKeyNewlineFalse()
    {
        var r = ArgParser.Parse(new[] { "--hmac", "sha256", "--key-env", "K", "--key-raw", "-s", "abc" });
        Assert.True(r.Success);
        Assert.False(r.StripKeyNewline);
    }

    [Fact]
    public void Parse_DefaultStripKeyNewlineTrue()
    {
        var r = ArgParser.Parse(new[] { "--hmac", "sha256", "--key-env", "K", "-s", "abc" });
        Assert.True(r.Success);
        Assert.True(r.StripKeyNewline);
    }
}
