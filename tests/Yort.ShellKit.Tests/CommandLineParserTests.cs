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

public class OptionParsingTests
{
    [Fact]
    public void StringOption_Present_ReturnsValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Option("--output", "-o", "FILE", "Output file");

        var result = parser.Parse(new[] { "--output", "file.txt" });

        Assert.Equal("file.txt", result.GetString("--output"));
    }

    [Fact]
    public void StringOption_ShortForm_ReturnsValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Option("--output", "-o", "FILE", "Output file");

        var result = parser.Parse(new[] { "-o", "file.txt" });

        Assert.Equal("file.txt", result.GetString("--output"));
    }

    [Fact]
    public void StringOption_Absent_ReturnsDefault()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Option("--output", null, "FILE", "Output file");

        var result = parser.Parse(Array.Empty<string>());

        Assert.Equal("default.txt", result.GetString("--output", defaultValue: "default.txt"));
    }

    [Fact]
    public void StringOption_AbsentNoDefault_Throws()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Option("--output", null, "FILE", "Output file");

        var result = parser.Parse(Array.Empty<string>());

        Assert.Throws<InvalidOperationException>(() => result.GetString("--output"));
    }

    [Fact]
    public void StringOption_MissingValue_HasErrors()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Option("--output", null, "FILE", "Output file");

        var result = parser.Parse(new[] { "--output" });

        Assert.True(result.HasErrors);
        Assert.Contains("requires a value", result.Errors[0]);
    }

    [Fact]
    public void IntOption_Present_ReturnsValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--count", "-c", "N", "Count");

        var result = parser.Parse(new[] { "--count", "42" });

        Assert.Equal(42, result.GetInt("--count"));
    }

    [Fact]
    public void IntOption_InvalidValue_HasErrors()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--count", null, "N", "Count");

        var result = parser.Parse(new[] { "--count", "abc" });

        Assert.True(result.HasErrors);
        Assert.Contains("not a valid integer", result.Errors[0]);
    }

    [Fact]
    public void IntOption_WithValidation_RejectsInvalidValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--level", null, "N", "Level",
                validate: v => v >= 1 && v <= 9 ? null : "must be 1-9");

        var result = parser.Parse(new[] { "--level", "0" });

        Assert.True(result.HasErrors);
        Assert.Contains("must be 1-9", result.Errors[0]);
    }

    [Fact]
    public void IntOption_WithValidation_AcceptsValidValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--level", null, "N", "Level",
                validate: v => v >= 1 && v <= 9 ? null : "must be 1-9");

        var result = parser.Parse(new[] { "--level", "5" });

        Assert.False(result.HasErrors);
        Assert.Equal(5, result.GetInt("--level"));
    }

    [Fact]
    public void IntOption_Absent_ReturnsDefault()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--count", null, "N", "Count");

        var result = parser.Parse(Array.Empty<string>());

        Assert.Equal(10, result.GetInt("--count", defaultValue: 10));
    }

    [Fact]
    public void DoubleOption_Present_ReturnsValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .DoubleOption("--interval", "-n", "N", "Interval");

        var result = parser.Parse(new[] { "--interval", "2.5" });

        Assert.Equal(2.5, result.GetDouble("--interval"));
    }

    [Fact]
    public void DoubleOption_InvalidValue_HasErrors()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .DoubleOption("--interval", null, "N", "Interval");

        var result = parser.Parse(new[] { "--interval", "xyz" });

        Assert.True(result.HasErrors);
        Assert.Contains("not a valid number", result.Errors[0]);
    }

    [Fact]
    public void DoubleOption_WithValidation_RejectsInvalidValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .DoubleOption("--interval", null, "N", "Interval",
                validate: v => v > 0 ? null : "must be positive");

        var result = parser.Parse(new[] { "--interval", "-1.0" });

        Assert.True(result.HasErrors);
        Assert.Contains("must be positive", result.Errors[0]);
    }
}
