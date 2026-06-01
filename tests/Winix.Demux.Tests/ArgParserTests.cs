#nullable enable
using System.IO;
using Winix.Demux;
using Xunit;

namespace Winix.Demux.Tests;

public class ArgParserTests
{
    private static (DemuxOptions? opts, string err, int code) Parse(params string[] args)
    {
        var stderr = new StringWriter();
        var parser = ArgParser.BuildParser("0.0.0");
        int r = ArgParser.TryParse(args, parser, stderr, out DemuxOptions? opts, out _);
        return (opts, stderr.ToString(), r);
    }

    [Fact]
    public void TwoRoutes_FileAndExec_Parsed()
    {
        var (opts, _, code) = Parse("--to", "ERROR", "err.log", "--exec", "WARN", "logger");
        Assert.Equal(0, code);
        Assert.NotNull(opts);
        Assert.Equal(2, opts!.Routes.Count);
        Assert.Equal(TargetKind.File, opts.Routes[0].Kind);
        Assert.Equal("err.log", opts.Routes[0].Target);
        Assert.Equal(TargetKind.Exec, opts.Routes[1].Kind);
    }

    [Fact]
    public void Default_Parsed()
    {
        var (opts, _, code) = Parse("--to", "E", "e.log", "--default-exec", "gzip > r.gz");
        Assert.Equal(0, code);
        Assert.NotNull(opts!.DefaultRoute);
        Assert.Equal(TargetKind.Exec, opts.DefaultRoute!.Kind);
    }

    [Fact]
    public void NoRoutes_IsUsageError()
    {
        var (opts, err, code) = Parse("--all");
        Assert.Equal(125, code);
        Assert.Null(opts);
        Assert.Contains("no routes", err, System.StringComparison.Ordinal);
    }

    [Fact]
    public void RouteFlagMissingOperand_IsUsageError()
    {
        var (_, err, code) = Parse("--to", "ERROR");
        Assert.Equal(125, code);
        Assert.Contains("operand", err, System.StringComparison.Ordinal);
    }

    [Fact]
    public void TwoDefaults_IsUsageError()
    {
        var (_, _, code) = Parse("--to", "E", "e.log", "--default-to", "a", "--default-exec", "b");
        Assert.Equal(125, code);
    }

    [Fact]
    public void BadRegex_IsUsageError()
    {
        var (_, _, code) = Parse("--to", "(unclosed", "e.log");
        Assert.Equal(125, code);
    }

    [Fact]
    public void FieldZero_IsUsageError()
    {
        var (_, _, code) = Parse("--to", "E", "e.log", "--field", "0");
        Assert.Equal(125, code);
    }

    [Fact]
    public void Field_And_Flags_ReadFromResidual()
    {
        var (opts, _, code) = Parse("--to", "E", "e.log", "--field", "3", "--all", "--append");
        Assert.Equal(0, code);
        Assert.Equal(3, opts!.Field);
        Assert.True(opts.All);
        Assert.True(opts.Append);
    }

    // T8-a (adversarial F10): explicit empty --delimiter collides with the ""=whitespace sentinel — reject.
    [Fact]
    public void EmptyDelimiter_IsUsageError()
    {
        var (_, _, code) = Parse("--to", "E", "e.log", "--delimiter", "");
        Assert.Equal(125, code);
    }

    // Lock the IntOption contract: non-numeric --field surfaces via HasErrors → exit 125, not FormatException.
    [Fact]
    public void FieldNonNumeric_IsUsageError()
    {
        var (_, _, code) = Parse("--to", "E", "e.log", "--field", "abc");
        Assert.Equal(125, code);
    }
}
