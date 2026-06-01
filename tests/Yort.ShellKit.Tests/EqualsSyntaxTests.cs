using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class EqualsSyntaxTests
{
    private static CommandLineParser NewParser() =>
        new CommandLineParser("t", "1.0")
            .Option("--output", "-o", "FILE", "output file")
            .IntOption("--level", null, "N", "level", validate: v => v < 0 ? "must be >= 0" : null)
            .DoubleOption("--ratio", null, "R", "ratio")
            .ListOption("--ext", null, "EXT", "extensions")
            .Flag("--verbose", "-v", "verbose");

    [Fact]
    public void StringOption_EqualsForm_ParsesValue()
    {
        var r = NewParser().Parse(new[] { "--output=out.txt" });
        Assert.False(r.HasErrors);
        Assert.Equal("out.txt", r.GetString("--output"));
    }

    [Fact]
    public void StringOption_SpaceForm_StillWorks()
    {
        var r = NewParser().Parse(new[] { "--output", "out.txt" });
        Assert.False(r.HasErrors);
        Assert.Equal("out.txt", r.GetString("--output"));
    }

    [Fact]
    public void IntOption_EqualsForm_ParsesAndValidates()
    {
        var ok = NewParser().Parse(new[] { "--level=3" });
        Assert.False(ok.HasErrors);
        Assert.Equal(3, ok.GetInt("--level"));

        var bad = NewParser().Parse(new[] { "--level=abc" });
        Assert.True(bad.HasErrors);

        var invalid = NewParser().Parse(new[] { "--level=-1" });
        Assert.True(invalid.HasErrors); // validator: must be >= 0
    }

    [Fact]
    public void ListOption_EqualsForm_CollectsAndMixesWithSpaceForm()
    {
        var r = NewParser().Parse(new[] { "--ext=.cs", "--ext", ".txt" });
        Assert.False(r.HasErrors);
        Assert.Equal(new[] { ".cs", ".txt" }, r.GetList("--ext"));
    }

    [Fact]
    public void ValueContainingEquals_SplitsOnFirstOnly()
    {
        var r = NewParser().Parse(new[] { "--output=a=b.txt" });
        Assert.False(r.HasErrors);
        Assert.Equal("a=b.txt", r.GetString("--output"));
    }

    [Fact]
    public void EmptyAttachedValue_OnStringOption_IsAllowed()
    {
        var r = NewParser().Parse(new[] { "--output=" });
        Assert.False(r.HasErrors);
        Assert.Equal("", r.GetString("--output"));
    }

    [Fact]
    public void BooleanFlag_WithAttachedValue_IsError()
    {
        var r = NewParser().Parse(new[] { "--verbose=true" });
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("takes no value"));
    }

    [Fact]
    public void UnknownEqualsToken_ReportsKeyNotWholeToken()
    {
        var r = NewParser().Parse(new[] { "--nope=x" });
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("--nope") && !e.Contains("--nope=x"));
    }

    [Fact]
    public void ShortFlag_IsNotEqualsSplit()  // F11: -o=x is a single unknown token (long-only =-split)
    {
        var r = NewParser().Parse(new[] { "-o=x" });
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("unknown option: -o=x"));
    }

    [Fact]
    public void SpaceSeparatedValueContainingEquals_Unaffected()
    {
        var r = NewParser().Parse(new[] { "--output", "k=v" });
        Assert.False(r.HasErrors);
        Assert.Equal("k=v", r.GetString("--output"));
    }

    [Fact]
    public void CommandMode_ArgsAfterSeparator_NotEqualsSplit()  // F1
    {
        var p = new CommandLineParser("t", "1.0").CommandMode().Flag("--verbose", "-v", "v");
        var r = p.Parse(new[] { "--", "child", "--opt=v" });
        Assert.False(r.HasErrors);
        Assert.Equal(new[] { "child", "--opt=v" }, r.Command);
    }

    [Fact]
    public void CommandMode_FirstNonFlagStops_LaterEqualsTokenNotSplit()  // F1
    {
        var p = new CommandLineParser("t", "1.0").CommandMode().Flag("--verbose", "-v", "v");
        var r = p.Parse(new[] { "child", "--opt=v" });
        Assert.False(r.HasErrors);
        Assert.Equal(new[] { "child", "--opt=v" }, r.Command);
    }

    [Fact]
    public void Positional_TokenWithEquals_NotSplit()  // F1
    {
        var p = new CommandLineParser("t", "1.0").Positional("files").Flag("--verbose", "-v", "v");
        var r = p.Parse(new[] { "foo=bar" });
        Assert.False(r.HasErrors);
        Assert.Equal(new[] { "foo=bar" }, r.Positionals);
    }

    [Fact]
    public void EmptyKey_DashDashEquals_IsUnknownOption()  // F2
    {
        var r = NewParser().Parse(new[] { "--=x" });
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("unknown option: --"));
    }

    [Fact]
    public void LeadingEqualsInValue_PassesThroughToStringOption()  // F2
    {
        var r = NewParser().Parse(new[] { "--output==v" });
        Assert.False(r.HasErrors);
        Assert.Equal("=v", r.GetString("--output"));
    }

    [Fact]
    public void DuplicateOption_LastWins()  // F3
    {
        var r = NewParser().Parse(new[] { "--output=a", "--output=b" });
        Assert.False(r.HasErrors);
        Assert.Equal("b", r.GetString("--output"));
    }

    [Fact]
    public void StringOption_EqualsForm_ValueLooksLikeFlag()  // F5
    {
        var r = NewParser().Parse(new[] { "--output=--foo" });
        Assert.False(r.HasErrors);
        Assert.Equal("--foo", r.GetString("--output"));
    }

    [Fact]
    public void DoubleOption_EqualsForm_BadValue_IsError()  // P2-F1: int/double symmetry
    {
        var r = NewParser().Parse(new[] { "--ratio=abc" });
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("--ratio") && e.Contains("not a valid number"));
    }
}
