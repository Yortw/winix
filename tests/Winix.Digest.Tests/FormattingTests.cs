#nullable enable
using System.Text.Json;
using Xunit;
using Winix.Digest;

namespace Winix.Digest.Tests;

public class FormattingTests
{
    // "abc" SHA-256 hash bytes — reused across tests.
    private static readonly byte[] AbcSha256 = Winix.Codec.Hex.Decode(
        "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");

    [Fact]
    public void Plain_Single_HexLowercase_Default()
    {
        string line = Formatting.PlainSingle(AbcSha256, OutputFormat.Hex, uppercase: false);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", line);
    }

    [Fact]
    public void Plain_Single_HexUppercase()
    {
        string line = Formatting.PlainSingle(AbcSha256, OutputFormat.Hex, uppercase: true);
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", line);
    }

    [Fact]
    public void Plain_Single_Base64()
    {
        string line = Formatting.PlainSingle(AbcSha256, OutputFormat.Base64, uppercase: false);
        Assert.Equal("ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=", line);
    }

    [Fact]
    public void Plain_MultiLine_UsesBinaryMarker()
    {
        // sha256sum -c understands "<hash> *<filename>" as binary mode. digest always reads
        // files as raw bytes, so the * is correct and keeps verification flows working.
        string line = Formatting.PlainMultiLine(AbcSha256, "/path/file.bin", OutputFormat.Hex, uppercase: false);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad */path/file.bin", line);
    }

    [Fact]
    public void Json_Single_HasExpectedShape()
    {
        var opts = DigestOptions.Defaults with { Algorithm = HashAlgorithm.Sha256, Source = new StringInput("abc") };
        string json = Formatting.JsonElement(AbcSha256, path: null, opts);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("sha256", doc.RootElement.GetProperty("algorithm").GetString());
        Assert.Equal("hex", doc.RootElement.GetProperty("format").GetString());
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            doc.RootElement.GetProperty("hash").GetString());
        Assert.Equal("string", doc.RootElement.GetProperty("source").GetString());
        Assert.False(doc.RootElement.TryGetProperty("path", out _));
    }

    [Fact]
    public void Json_MultiFile_IncludesPath()
    {
        var opts = DigestOptions.Defaults;
        string json = Formatting.JsonElement(AbcSha256, path: "/path/file.bin", opts);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("file", doc.RootElement.GetProperty("source").GetString());
        Assert.Equal("/path/file.bin", doc.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public void Json_Hmac_UsesHmacAlgorithmPrefix()
    {
        var opts = DigestOptions.Defaults with { Algorithm = HashAlgorithm.Sha256, IsHmac = true };
        string json = Formatting.JsonElement(AbcSha256, path: null, opts);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("hmac-sha256", doc.RootElement.GetProperty("algorithm").GetString());
    }

    [Fact]
    public void Json_Stdin_SourceIsStdin()
    {
        var opts = DigestOptions.Defaults with { Source = new StdinInput() };
        string json = Formatting.JsonElement(AbcSha256, path: null, opts);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("stdin", doc.RootElement.GetProperty("source").GetString());
    }
}
