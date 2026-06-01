using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class OptionalValueColorTests
{
    private static CommandLineParser NewParser() =>
        new CommandLineParser("t", "1.0").StandardFlags();

    [Fact]
    public void Bare_Color_ResolvesToDefaultAlways()
    {
        var r = NewParser().Parse(new[] { "--color" });
        Assert.False(r.HasErrors);
        Assert.True(r.Has("--color"));
        Assert.Equal("always", r.GetString("--color"));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("always")]
    [InlineData("never")]
    public void Color_EqualsValue_Parses(string when)
    {
        var r = NewParser().Parse(new[] { $"--color={when}" });
        Assert.False(r.HasErrors);
        Assert.Equal(when, r.GetString("--color"));
    }

    [Fact]
    public void Color_BadValue_IsError()
    {
        var r = NewParser().Parse(new[] { "--color=purple" });
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("--color") && e.Contains("auto, always, never"));
    }

    [Fact]
    public void Color_EmptyValue_IsError()
    {
        var r = NewParser().Parse(new[] { "--color=" });
        Assert.True(r.HasErrors);
    }

    [Fact]
    public void Color_Absent_IsNotPresent()
    {
        var r = NewParser().Parse(System.Array.Empty<string>());
        Assert.False(r.Has("--color"));
    }

    [Fact]
    public void NoColor_StillABooleanFlag()
    {
        var r = NewParser().Parse(new[] { "--no-color" });
        Assert.False(r.HasErrors);
        Assert.True(r.Has("--no-color"));
    }

    [Fact]
    public void Color_DuplicateValues_LastWins()  // F3
    {
        var r = NewParser().Parse(new[] { "--color=always", "--color=never" });
        Assert.False(r.HasErrors);
        Assert.Equal("never", r.GetString("--color"));
    }

    [Fact]
    public void Color_MixedCaseValue_IsError()  // F4: values are case-sensitive
    {
        var r = NewParser().Parse(new[] { "--color=Always" });
        Assert.True(r.HasErrors);
    }

    [Fact]
    public void Color_FlagShapedValue_IsError()  // F6
    {
        var r = NewParser().Parse(new[] { "--color=--always" });
        Assert.True(r.HasErrors);
    }

    [Fact]
    public void Color_BadValue_ErrorMessageIsExact()  // F7: pin the docs-referenced contract
    {
        var r = NewParser().Parse(new[] { "--color=purple" });
        Assert.Contains(r.Errors, e => e.Contains("--color: 'purple' is not one of: auto, always, never"));
    }

    [Fact]
    public void Parse_CalledTwice_OptionalValueStillResolves()  // F8: locks the BuildLookups guard
    {
        var p = NewParser();
        var r1 = p.Parse(new[] { "--color=never" });
        Assert.Equal("never", r1.GetString("--color"));
        var r2 = p.Parse(new[] { "--color=always" });
        Assert.Equal("always", r2.GetString("--color"));
    }

    [Fact]
    public void Help_WithAttachedValue_IsError()  // F12
    {
        var r = NewParser().Parse(new[] { "--help=x" });
        Assert.True(r.HasErrors);
        Assert.False(r.IsHandled); // malformed --help=x does not trigger help; it errors
    }

    [Fact]
    public void Help_RendersColorWithAllowedValues()
    {
        string help = new CommandLineParser("t", "1.0").StandardFlags().GenerateHelp();
        Assert.Contains("--color[=auto|always|never]", help);
    }

    [Fact]
    public void Describe_EmitsOptionalValueTypeAndAllowedValues()
    {
        string json = new CommandLineParser("t", "1.0").StandardFlags().GenerateDescribe();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var options = doc.RootElement.GetProperty("options");
        System.Text.Json.JsonElement color = default;
        foreach (var o in options.EnumerateArray())
        {
            if (o.GetProperty("long").GetString() == "--color") { color = o; break; }
        }
        Assert.Equal("optional-value", color.GetProperty("type").GetString());
        Assert.Equal("always", color.GetProperty("default_when_bare").GetString());
        var allowed = color.GetProperty("allowed_values");
        Assert.Equal(3, allowed.GetArrayLength());
        Assert.Equal("auto", allowed[0].GetString());
    }
}
