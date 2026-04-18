#nullable enable
using System;
using Winix.Ids;
using Xunit;

namespace Winix.Ids.Tests;

public class ArgParserTests
{
    // --- positive parses ---

    [Fact]
    public void Parse_NoArgs_ReturnsDefaults()
    {
        var r = ArgParser.Parse(Array.Empty<string>());
        Assert.True(r.Success);
        Assert.Equal(IdType.Uuid7, r.Options!.Type);
        Assert.Equal(1, r.Options.Count);
    }

    [Theory]
    [InlineData("uuid4",  IdType.Uuid4)]
    [InlineData("uuid7",  IdType.Uuid7)]
    [InlineData("ulid",   IdType.Ulid)]
    [InlineData("nanoid", IdType.Nanoid)]
    public void Parse_Type_ParsesEachKnownValue(string input, IdType expected)
    {
        var r = ArgParser.Parse(new[] { "--type", input });
        Assert.True(r.Success);
        Assert.Equal(expected, r.Options!.Type);
    }

    [Fact]
    public void Parse_CountShortFlag_Works()
    {
        var r = ArgParser.Parse(new[] { "-n", "5" });
        Assert.True(r.Success);
        Assert.Equal(5, r.Options!.Count);
    }

    [Fact]
    public void Parse_NanoidWithLengthAndAlphabet_Works()
    {
        var r = ArgParser.Parse(new[] {
            "--type", "nanoid", "--length", "12", "--alphabet", "hex",
        });
        Assert.True(r.Success);
        Assert.Equal(12, r.Options!.Length);
        Assert.Equal(NanoidAlphabet.Hex, r.Options.Alphabet);
    }

    [Fact]
    public void Parse_UuidFormatBraces_Works()
    {
        var r = ArgParser.Parse(new[] {
            "--type", "uuid7", "--format", "braces", "--uppercase",
        });
        Assert.True(r.Success);
        Assert.Equal(UuidFormat.Braces, r.Options!.Format);
        Assert.True(r.Options.Uppercase);
    }

    // --- Q5 compatibility matrix (all errors exit 125) ---

    [Theory]
    [InlineData("uuid4")]
    [InlineData("uuid7")]
    [InlineData("ulid")]
    public void Parse_LengthWithNonNanoid_Errors(string type)
    {
        var r = ArgParser.Parse(new[] { "--type", type, "--length", "12" });
        Assert.False(r.Success);
        Assert.Contains("--length only applies to --type nanoid", r.Error);
    }

    [Theory]
    [InlineData("uuid4")]
    [InlineData("uuid7")]
    [InlineData("ulid")]
    public void Parse_AlphabetWithNonNanoid_Errors(string type)
    {
        var r = ArgParser.Parse(new[] { "--type", type, "--alphabet", "hex" });
        Assert.False(r.Success);
        Assert.Contains("--alphabet only applies to --type nanoid", r.Error);
    }

    [Theory]
    [InlineData("ulid")]
    [InlineData("nanoid")]
    public void Parse_FormatWithNonUuid_Errors(string type)
    {
        var r = ArgParser.Parse(new[] { "--type", type, "--format", "hex" });
        Assert.False(r.Success);
        Assert.Contains("--format only applies to --type uuid4 or uuid7", r.Error);
    }

    [Fact]
    public void Parse_UppercaseWithUlid_Errors()
    {
        var r = ArgParser.Parse(new[] { "--type", "ulid", "--uppercase" });
        Assert.False(r.Success);
        Assert.Contains("ULID output is already uppercase", r.Error);
    }

    [Fact]
    public void Parse_UppercaseWithNanoid_Errors()
    {
        var r = ArgParser.Parse(new[] { "--type", "nanoid", "--uppercase" });
        Assert.False(r.Success);
        Assert.Contains("use --alphabet upper", r.Error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Parse_CountNonPositive_Errors(string value)
    {
        var r = ArgParser.Parse(new[] { "--count", value });
        Assert.False(r.Success);
        Assert.Contains("--count must be ≥ 1", r.Error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void Parse_LengthNonPositive_Errors(string value)
    {
        var r = ArgParser.Parse(new[] { "--type", "nanoid", "--length", value });
        Assert.False(r.Success);
        Assert.Contains("--length must be ≥ 1", r.Error);
    }

    [Fact]
    public void Parse_UnknownType_Errors()
    {
        var r = ArgParser.Parse(new[] { "--type", "uuidX" });
        Assert.False(r.Success);
        Assert.Contains("unknown --type 'uuidX'", r.Error);
    }

    [Fact]
    public void Parse_UnknownAlphabet_Errors()
    {
        var r = ArgParser.Parse(new[] { "--type", "nanoid", "--alphabet", "morse" });
        Assert.False(r.Success);
        Assert.Contains("unknown --alphabet 'morse'", r.Error);
    }

    [Fact]
    public void Parse_UnknownFormat_Errors()
    {
        var r = ArgParser.Parse(new[] { "--format", "weird" });
        Assert.False(r.Success);
        Assert.Contains("unknown --format 'weird'", r.Error);
    }
}
