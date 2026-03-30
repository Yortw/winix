using Xunit;
using Yort.ShellKit;

namespace Yort.ShellKit.Tests;

public class FlagParsingTests
{
    [Fact]
    public void Flag_Present_HasReturnsTrue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose output");

        var result = parser.Parse(new[] { "--verbose" });

        Assert.True(result.Has("--verbose"));
    }

    [Fact]
    public void Flag_Absent_HasReturnsFalse()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose output");

        var result = parser.Parse(Array.Empty<string>());

        Assert.False(result.Has("--verbose"));
    }

    [Fact]
    public void Flag_ShortForm_HasReturnsTrue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", "-v", "Verbose output");

        var result = parser.Parse(new[] { "-v" });

        Assert.True(result.Has("--verbose"));
    }

    [Fact]
    public void Flag_MultipleFlags_AllRecognised()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", "-v", "Verbose output")
            .Flag("--force", "-f", "Force");

        var result = parser.Parse(new[] { "-v", "-f" });

        Assert.True(result.Has("--verbose"));
        Assert.True(result.Has("--force"));
    }

    [Fact]
    public void Flag_UnknownFlag_HasErrors()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose output");

        var result = parser.Parse(new[] { "--unknown" });

        Assert.True(result.HasErrors);
        Assert.Contains("unknown option: --unknown", result.Errors[0]);
    }

    [Fact]
    public void Parse_NoArgs_NoErrors()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose output");

        var result = parser.Parse(Array.Empty<string>());

        Assert.False(result.HasErrors);
    }
}
