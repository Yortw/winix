#nullable enable
using Xunit;
using Winix.Url;

namespace Winix.Url.Tests;

public class ArgParserTests
{
    [Fact]
    public void Parse_NoArgs_Errors()
    {
        var r = ArgParser.Parse(System.Array.Empty<string>());
        Assert.False(r.Success);
        Assert.Contains("missing subcommand", r.Error);
    }

    [Fact]
    public void Parse_UnknownSubcommand_Errors()
    {
        var r = ArgParser.Parse(new[] { "bogus" });
        Assert.False(r.Success);
        Assert.Contains("unknown subcommand", r.Error);
    }

    // encode
    [Fact]
    public void Parse_Encode_InputRequired()
    {
        var r = ArgParser.Parse(new[] { "encode" });
        Assert.False(r.Success);
        Assert.Contains("encode requires an input", r.Error);
    }

    [Fact]
    public void Parse_Encode_WithInput()
    {
        var r = ArgParser.Parse(new[] { "encode", "hello world" });
        Assert.True(r.Success);
        Assert.Equal(SubCommand.Encode, r.Options!.SubCommand);
        Assert.Equal("hello world", r.Options.PrimaryInput);
    }

    [Theory]
    [InlineData("component", EncodeMode.Component)]
    [InlineData("path", EncodeMode.Path)]
    [InlineData("query", EncodeMode.Query)]
    [InlineData("form", EncodeMode.Form)]
    public void Parse_Encode_ModeFlag(string value, EncodeMode expected)
    {
        var r = ArgParser.Parse(new[] { "encode", "--mode", value, "hello" });
        Assert.True(r.Success);
        Assert.Equal(expected, r.Options!.Mode);
    }

    [Fact]
    public void Parse_Encode_FormFlagSetsForm()
    {
        var r = ArgParser.Parse(new[] { "encode", "--form", "hello" });
        Assert.True(r.Success);
        Assert.True(r.Options!.Form);
    }

    [Fact]
    public void Parse_Encode_UnknownMode_Errors()
    {
        var r = ArgParser.Parse(new[] { "encode", "--mode", "bogus", "hello" });
        Assert.False(r.Success);
        Assert.Contains("unknown --mode", r.Error);
    }

    // decode
    [Fact]
    public void Parse_Decode_InputRequired()
    {
        var r = ArgParser.Parse(new[] { "decode" });
        Assert.False(r.Success);
    }

    [Fact]
    public void Parse_Decode_WithInput()
    {
        var r = ArgParser.Parse(new[] { "decode", "hello%20world" });
        Assert.True(r.Success);
        Assert.Equal(SubCommand.Decode, r.Options!.SubCommand);
    }

    // parse
    [Fact]
    public void Parse_Parse_InputRequired()
    {
        var r = ArgParser.Parse(new[] { "parse" });
        Assert.False(r.Success);
    }

    [Fact]
    public void Parse_Parse_Field()
    {
        var r = ArgParser.Parse(new[] { "parse", "https://x.io/", "--field", "host" });
        Assert.True(r.Success);
        Assert.Equal(SubCommand.Parse, r.Options!.SubCommand);
        Assert.Equal("host", r.Options.Field);
    }

    [Fact]
    public void Parse_Parse_JsonAndField_Errors()
    {
        var r = ArgParser.Parse(new[] { "parse", "https://x.io/", "--field", "host", "--json" });
        Assert.False(r.Success);
        Assert.Contains("--field is not compatible with --json", r.Error);
    }

    [Fact]
    public void Parse_FieldOnNonParseSubcommand_Errors()
    {
        var r = ArgParser.Parse(new[] { "encode", "--field", "host", "hello" });
        Assert.False(r.Success);
        Assert.Contains("--field only applies to parse", r.Error);
    }

    // build
    [Fact]
    public void Parse_Build_HostRequired()
    {
        var r = ArgParser.Parse(new[] { "build" });
        Assert.False(r.Success);
        Assert.Contains("--host is required", r.Error);
    }

    [Fact]
    public void Parse_Build_AllFields()
    {
        var r = ArgParser.Parse(new[] {
            "build",
            "--scheme", "https",
            "--host", "api.example.com",
            "--port", "8443",
            "--path", "/v1",
            "--query", "q=hello world",
            "--query", "limit=10",
            "--fragment", "top",
        });
        Assert.True(r.Success);
        var o = r.Options!;
        Assert.Equal(SubCommand.Build, o.SubCommand);
        Assert.Equal("https", o.BuildScheme);
        Assert.Equal("api.example.com", o.BuildHost);
        Assert.Equal(8443, o.BuildPort);
        Assert.Equal("/v1", o.BuildPath);
        Assert.Equal(2, o.BuildQuery.Count);
        Assert.Equal(("q", "hello world"), o.BuildQuery[0]);
        Assert.Equal(("limit", "10"), o.BuildQuery[1]);
        Assert.Equal("top", o.BuildFragment);
    }

    [Fact]
    public void Parse_Build_QueryWithoutEquals_Errors()
    {
        var r = ArgParser.Parse(new[] { "build", "--host", "x.io", "--query", "badvalue" });
        Assert.False(r.Success);
        Assert.Contains("--query must be K=V", r.Error);
    }

    [Fact]
    public void Parse_Build_InvalidPort_Errors()
    {
        var r = ArgParser.Parse(new[] { "build", "--host", "x.io", "--port", "notanumber" });
        Assert.False(r.Success);
        Assert.Contains("--port", r.Error);
    }

    // join
    [Fact]
    public void Parse_Join_RequiresTwoPositionals()
    {
        var r = ArgParser.Parse(new[] { "join", "https://example.com/" });
        Assert.False(r.Success);
        Assert.Contains("join requires BASE and RELATIVE", r.Error);
    }

    [Fact]
    public void Parse_Join_WithBothPositionals()
    {
        var r = ArgParser.Parse(new[] { "join", "https://example.com/a/", "./b" });
        Assert.True(r.Success);
        Assert.Equal(SubCommand.Join, r.Options!.SubCommand);
        Assert.Equal("https://example.com/a/", r.Options.PrimaryInput);
        Assert.Equal("./b", r.Options.JoinRelative);
    }

    // query get/set/delete
    [Fact]
    public void Parse_QueryGet_RequiresUrlAndKey()
    {
        var r = ArgParser.Parse(new[] { "query", "get", "https://x.io/" });
        Assert.False(r.Success);
        Assert.Contains("key is required", r.Error);
    }

    [Fact]
    public void Parse_QueryGet_Success()
    {
        var r = ArgParser.Parse(new[] { "query", "get", "https://x.io/?a=1", "a" });
        Assert.True(r.Success);
        Assert.Equal(SubCommand.QueryGet, r.Options!.SubCommand);
        Assert.Equal("https://x.io/?a=1", r.Options.PrimaryInput);
        Assert.Equal("a", r.Options.QueryKey);
    }

    [Fact]
    public void Parse_QuerySet_RequiresUrlKeyValue()
    {
        var r = ArgParser.Parse(new[] { "query", "set", "https://x.io/", "a" });
        Assert.False(r.Success);
        Assert.Contains("value is required", r.Error);
    }

    [Fact]
    public void Parse_QuerySet_Success()
    {
        var r = ArgParser.Parse(new[] { "query", "set", "https://x.io/?a=1", "a", "2" });
        Assert.True(r.Success);
        Assert.Equal(SubCommand.QuerySet, r.Options!.SubCommand);
        Assert.Equal("a", r.Options.QueryKey);
        Assert.Equal("2", r.Options.QueryValue);
    }

    [Fact]
    public void Parse_QueryDelete_Success()
    {
        var r = ArgParser.Parse(new[] { "query", "delete", "https://x.io/?a=1", "a" });
        Assert.True(r.Success);
        Assert.Equal(SubCommand.QueryDelete, r.Options!.SubCommand);
        Assert.Equal("a", r.Options.QueryKey);
    }

    [Fact]
    public void Parse_QueryUnknownOp_Errors()
    {
        var r = ArgParser.Parse(new[] { "query", "bogus", "https://x.io/", "a" });
        Assert.False(r.Success);
        Assert.Contains("unknown query op", r.Error);
    }

    // Global flags
    [Fact]
    public void Parse_JsonFlag_Propagates()
    {
        var r = ArgParser.Parse(new[] { "parse", "https://x.io/", "--json" });
        Assert.True(r.Options!.Json);
    }

    [Fact]
    public void Parse_RawFlag_Propagates()
    {
        var r = ArgParser.Parse(new[] { "build", "--host", "x.io", "--raw" });
        Assert.True(r.Options!.Raw);
    }
}
