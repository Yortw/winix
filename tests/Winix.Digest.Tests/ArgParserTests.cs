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
        Assert.Contains("multiple algorithms", r.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UnknownAlgo_Errors()
    {
        var r = ArgParser.Parse(new[] { "--algo", "weirdhash", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("unknown algorithm", r.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Hmac_RequiresKeySource()
    {
        var r = ArgParser.Parse(new[] { "--hmac", "sha256", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("--hmac requires", r.Error, StringComparison.Ordinal);
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
        Assert.Contains("exactly one of", r.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Hmac_WithAlgorithmFlag_Errors()
    {
        var r = ArgParser.Parse(new[] { "--hmac", "sha256", "--sha512", "--key-env", "K", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("--hmac carries its own algorithm", r.Error, StringComparison.Ordinal);
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
        Assert.Contains("--string cannot be combined with file arguments", r.Error, StringComparison.Ordinal);
    }

    // -- Round-1 review I8 — `digest -s hello -` should fail with a stdin-specific
    //    message, not "file arguments". Verifies the I8 message split. --
    [Fact]
    public void Parse_String_WithStdinDash_ErrorsWithStdinMessage()
    {
        var r = ArgParser.Parse(new[] { "-s", "hello", "-" });
        Assert.False(r.Success);
        Assert.Contains("stdin", r.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("file arguments", r.Error, StringComparison.Ordinal);
    }

    // -- Round-1 review I-SFH-18 — --describe must reflect actual SHA-3 availability,
    //    not advertise it unconditionally. The test asserts the binding both ways: when
    //    SHA-3 is supported the description names it; when not, the description says so. --
    [Fact]
    public void IsSha3Available_MatchesBclProbe()
    {
        bool expected = System.Security.Cryptography.SHA3_256.IsSupported &&
                        System.Security.Cryptography.SHA3_512.IsSupported;
        Assert.Equal(expected, ArgParser.IsSha3Available());
    }

    // -- Tier-2 re-verification 2026-05-06 finding F1: the binary's --describe and
    //    --help exit-code descriptions used to be narrower than README/man — they only
    //    said "Usage error: bad flags, unknown value, or flag conflict" for code 125,
    //    even though the binary returns 125 for missing files (ArgParser.cs Fail at
    //    line 215) and empty HMAC keys (KeyResolver). README.md and man1/digest.1
    //    correctly enumerated those cases. AI agents reading --describe couldn't
    //    discover the 125 vs 126 boundary for the common "file not found" case.
    [Fact]
    public void Describe_ExitCode125_EnumeratesMissingFileAndEmptyHmacKey()
    {
        using var sw = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            var r = ArgParser.Parse(new[] { "--describe" });
            Assert.True(r.IsHandled);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        string output = sw.ToString();
        Assert.Contains("missing file", output, StringComparison.Ordinal);
        Assert.Contains("empty HMAC key", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_AdvertisesSha3OnlyWhenAvailable()
    {
        // Capture stdout while ShellKit emits the --describe payload.
        using var sw = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            var r = ArgParser.Parse(new[] { "--describe" });
            Assert.True(r.IsHandled);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        string output = sw.ToString();
        // The bare "SHA-3 unavailable on this platform" substring matches BOTH the
        // tool-description block (only when SHA-3 isn't available) AND the exit-code
        // description (always, after tier-2 re-verification finding F1 expanded the
        // 126-description to enumerate "SHA-3 unavailable on this platform, file read
        // failure, ..."). Use the unique description-only fragment that includes the
        // surrounding hash-list to disambiguate.
        const string descriptionOnlyFragment = "SHA-2/BLAKE2b (SHA-3 unavailable on this platform)";
        if (ArgParser.IsSha3Available())
        {
            Assert.Contains("SHA-3", output, StringComparison.Ordinal);
            Assert.DoesNotContain(descriptionOnlyFragment, output, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(descriptionOnlyFragment, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Parse_MultipleStrings_Errors()
    {
        var r = ArgParser.Parse(new[] { "-s", "hello", "--string", "world" });
        Assert.False(r.Success);
        Assert.Contains("--string can only be specified once", r.Error, StringComparison.Ordinal);
    }

    // -- Round-1 review I7 — `digest -s -s` is one --string flag-use with the literal
    //    string value "-s" (a hyphen-s). The previous textual scan counted it as 2
    //    occurrences and rejected it. The fixed walker steps over option-values, so the
    //    second `-s` is not mistaken for a flag-use. --
    [Fact]
    public void Parse_StringValueIsLiteralHyphenS_Succeeds()
    {
        var r = ArgParser.Parse(new[] { "-s", "-s" });
        Assert.True(r.Success);
        Assert.IsType<StringInput>(r.Options!.Source);
        Assert.Equal("-s", ((StringInput)r.Options.Source).Value);
    }

    [Fact]
    public void Parse_StringValueIsLiteralLongFlag_Succeeds()
    {
        // The same case for the long-form: `digest --string --string` is one --string
        // flag-use with the literal value "--string".
        var r = ArgParser.Parse(new[] { "--string", "--string" });
        Assert.True(r.Success);
        Assert.IsType<StringInput>(r.Options!.Source);
        Assert.Equal("--string", ((StringInput)r.Options.Source).Value);
    }

    [Fact]
    public void Parse_OtherOptionValueIsHyphenS_StringNotCounted()
    {
        // `--algo` takes a value; if the user (oddly) wrote `--algo -s`, our walker must
        // skip past `-s` as the value of --algo, not count it as a --string flag-use.
        // This will fail with "unknown algorithm '-s'" but must NOT fail with a string
        // count error.
        var r = ArgParser.Parse(new[] { "--algo", "-s", "-s", "actual-value" });
        // First --algo consumes "-s" as its (invalid) value; then the real -s consumes "actual-value".
        // Outcome: --algo error fires before --string count check, but the count itself
        // should be 1, not 2.
        Assert.False(r.Success);
        Assert.Contains("unknown algorithm", r.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_MultipleOutputFormats_Errors()
    {
        var r = ArgParser.Parse(new[] { "--hex", "--base64", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("multiple output formats", r.Error, StringComparison.Ordinal);
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
        Assert.Contains("not found", r.Error, StringComparison.Ordinal);
        Assert.Contains("--string", r.Error, StringComparison.Ordinal);
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
        Assert.Contains("--verify is not supported with multiple files", r.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_VerifyWithJson_Errors()
    {
        // --verify and --json have no useful combined behaviour: verify's output is an
        // exit code + terse stderr message, not a structured record. Reject the pair.
        var r = ArgParser.Parse(new[] { "--verify", "abc", "--json", "-s", "hello" });
        Assert.False(r.Success);
        Assert.Contains("--verify is not compatible with --json", r.Error, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("--key-env", "K")]
    [InlineData("--key-file", "/tmp/k")]
    [InlineData("--key", "secret")]
    public void Parse_KeyOption_WithoutHmac_Errors(string flag, string value)
    {
        var r = ArgParser.Parse(new[] { flag, value, "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("--key* options require --hmac", r.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_KeyStdin_WithoutHmac_Errors()
    {
        var r = ArgParser.Parse(new[] { "--key-stdin", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("--key* options require --hmac", r.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_KeyRaw_WithoutHmac_Errors()
    {
        var r = ArgParser.Parse(new[] { "--key-raw", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("--key* options require --hmac", r.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--base64")]
    [InlineData("--base64-url")]
    [InlineData("--base32")]
    public void Parse_Uppercase_WithNonHexFormat_Errors(string formatFlag)
    {
        var r = ArgParser.Parse(new[] { formatFlag, "--uppercase", "-s", "abc" });
        Assert.False(r.Success);
        Assert.Contains("--uppercase only applies to hex", r.Error, StringComparison.Ordinal);
    }
}
