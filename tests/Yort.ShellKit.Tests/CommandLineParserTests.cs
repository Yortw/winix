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

public class ListOptionTests
{
    [Fact]
    public void ListOption_SingleValue_ReturnsSingleElementArray()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .ListOption("--watch", "-w", "GLOB", "Watch pattern");

        var result = parser.Parse(new[] { "--watch", "*.cs" });

        Assert.Equal(new[] { "*.cs" }, result.GetList("--watch"));
    }

    [Fact]
    public void ListOption_MultipleValues_ReturnsAll()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .ListOption("--watch", "-w", "GLOB", "Watch pattern");

        var result = parser.Parse(new[] { "-w", "*.cs", "-w", "*.fs" });

        Assert.Equal(new[] { "*.cs", "*.fs" }, result.GetList("--watch"));
    }

    [Fact]
    public void ListOption_Absent_ReturnsEmptyArray()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .ListOption("--watch", null, "GLOB", "Watch pattern");

        var result = parser.Parse(Array.Empty<string>());

        Assert.Empty(result.GetList("--watch"));
    }

    [Fact]
    public void ListOption_MissingValue_HasErrors()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .ListOption("--watch", null, "GLOB", "Watch pattern");

        var result = parser.Parse(new[] { "--watch" });

        Assert.True(result.HasErrors);
        Assert.Contains("requires a value", result.Errors[0]);
    }
}

public class FlagAliasTests
{
    [Fact]
    public void FlagAlias_ExpandsToOptionValue()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--level", null, "N", "Level")
            .FlagAlias("-9", "--level", "9");

        var result = parser.Parse(new[] { "-9" });

        Assert.Equal(9, result.GetInt("--level"));
    }

    [Fact]
    public void FlagAlias_MultipleAliases_LastWins()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--level", null, "N", "Level")
            .FlagAlias("-1", "--level", "1")
            .FlagAlias("-9", "--level", "9");

        var result = parser.Parse(new[] { "-1", "-9" });

        Assert.Equal(9, result.GetInt("--level"));
    }

    [Fact]
    public void FlagAlias_NotShownAsUnknown()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .IntOption("--level", null, "N", "Level")
            .FlagAlias("-5", "--level", "5");

        var result = parser.Parse(new[] { "-5" });

        Assert.False(result.HasErrors);
    }
}

public class CommandModeTests
{
    [Fact]
    public void CommandMode_FirstNonFlag_StartsCommand()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose")
            .CommandMode();

        var result = parser.Parse(new[] { "--verbose", "ls", "-la" });

        Assert.True(result.Has("--verbose"));
        Assert.Equal(new[] { "ls", "-la" }, result.Command);
    }

    [Fact]
    public void CommandMode_DoubleDash_StartsCommand()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose")
            .CommandMode();

        var result = parser.Parse(new[] { "--verbose", "--", "git", "status" });

        Assert.True(result.Has("--verbose"));
        Assert.Equal(new[] { "git", "status" }, result.Command);
    }

    [Fact]
    public void CommandMode_NoCommand_EmptyArray()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .CommandMode();

        var result = parser.Parse(Array.Empty<string>());

        Assert.Empty(result.Command);
    }

    [Fact]
    public void CommandMode_DoubleDashThenFlags_FlagsAreCommand()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose")
            .CommandMode();

        var result = parser.Parse(new[] { "--", "--verbose", "arg" });

        Assert.False(result.Has("--verbose"));
        Assert.Equal(new[] { "--verbose", "arg" }, result.Command);
    }
}

public class PositionalTests
{
    [Fact]
    public void Positional_NonFlags_CollectedInOrder()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose");

        var result = parser.Parse(new[] { "file1.txt", "--verbose", "file2.txt" });

        Assert.Equal(new[] { "file1.txt", "file2.txt" }, result.Positionals);
        Assert.True(result.Has("--verbose"));
    }

    [Fact]
    public void Positional_AfterDoubleDash_AllPositional()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .Flag("--verbose", null, "Verbose");

        var result = parser.Parse(new[] { "--", "--verbose", "file.txt" });

        Assert.Equal(new[] { "--verbose", "file.txt" }, result.Positionals);
        Assert.False(result.Has("--verbose"));
    }

    [Fact]
    public void Positional_NoArgs_EmptyArray()
    {
        var parser = new CommandLineParser("test", "1.0.0");

        var result = parser.Parse(Array.Empty<string>());

        Assert.Empty(result.Positionals);
    }
}

[Collection("ConsoleOutput")]
public class StandardFlagTests
{
    [Fact]
    public void StandardFlags_RegistersHelpVersionColorJson()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .StandardFlags();

        var result = parser.Parse(new[] { "--json", "--no-color" });

        Assert.True(result.Has("--json"));
        Assert.True(result.Has("--no-color"));
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void StandardFlags_Help_IsHandled()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("test", "1.0.0")
                .StandardFlags();

            var result = parser.Parse(new[] { "--help" });

            Assert.True(result.IsHandled);
            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void StandardFlags_Version_IsHandled()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("test", "1.0.0")
                .StandardFlags();

            var result = parser.Parse(new[] { "--version" });

            Assert.True(result.IsHandled);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("test 1.0.0", writer.ToString());
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void StandardFlags_ShortHelp_IsHandled()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("test", "1.0.0")
                .StandardFlags();

            var result = parser.Parse(new[] { "-h" });

            Assert.True(result.IsHandled);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void StandardFlags_NormalArgs_NotHandled()
    {
        var parser = new CommandLineParser("test", "1.0.0")
            .StandardFlags()
            .Flag("--verbose", null, "Verbose");

        var result = parser.Parse(new[] { "--verbose" });

        Assert.False(result.IsHandled);
    }
}

public class WriteErrorsTests
{
    [Fact]
    public void WriteErrors_PlainText_WritesToolPrefixedErrors()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .Flag("--verbose", null, "Verbose");

        var result = parser.Parse(new[] { "--unknown" });

        var writer = new StringWriter();
        int exitCode = result.WriteErrors(writer);

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("mytool: unknown option: --unknown", writer.ToString());
    }

    [Fact]
    public void WriteErrors_JsonMode_WritesJsonError()
    {
        var parser = new CommandLineParser("mytool", "2.0.0")
            .StandardFlags()
            .Flag("--verbose", null, "Verbose");

        var result = parser.Parse(new[] { "--json", "--unknown" });

        var writer = new StringWriter();
        int exitCode = result.WriteErrors(writer);

        Assert.Equal(ExitCode.UsageError, exitCode);
        string output = writer.ToString();
        Assert.Contains("\"tool\":\"mytool\"", output);
        Assert.Contains("\"exit_code\":125", output);
        Assert.Contains("\"exit_reason\":\"usage_error\"", output);
    }

    [Fact]
    public void WriteErrors_CustomUsageErrorCode_ReturnsCustomCode()
    {
        var parser = new CommandLineParser("squeeze", "1.0.0")
            .Flag("--verbose", null, "Verbose")
            .UsageErrorCode(2);

        var result = parser.Parse(new[] { "--unknown" });

        var writer = new StringWriter();
        int exitCode = result.WriteErrors(writer);

        Assert.Equal(2, exitCode);
    }
}

[Collection("ConsoleOutput")]
public class HelpGenerationTests
{
    [Fact]
    public void GenerateHelp_IncludesUsageLine()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("mytool", "1.0.0")
                .Description("A test tool")
                .StandardFlags();

            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("Usage: mytool", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_CommandMode_ShowsCommandInUsage()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("mytool", "1.0.0")
                .StandardFlags()
                .CommandMode();

            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("<command>", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_Positional_ShowsLabelInUsage()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("mytool", "1.0.0")
                .StandardFlags()
                .Positional("files...");

            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("[files...]", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_IncludesDescription()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("mytool", "1.0.0")
                .Description("A useful test tool")
                .StandardFlags();

            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("A useful test tool", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_IncludesRegisteredFlags()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("mytool", "1.0.0")
                .StandardFlags()
                .Flag("--verbose", "-v", "Enable verbose output");

            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("-v, --verbose", help);
            Assert.Contains("Enable verbose output", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_IncludesOptionWithPlaceholder()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("mytool", "1.0.0")
                .StandardFlags()
                .IntOption("--count", "-c", "N", "Number of items");

            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("-c, --count N", help);
            Assert.Contains("Number of items", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_ListOption_ShowsRepeatable()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("mytool", "1.0.0")
                .StandardFlags()
                .ListOption("--watch", "-w", "GLOB", "Watch pattern");

            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("(repeatable)", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_IncludesCustomSections()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("mytool", "1.0.0")
                .StandardFlags()
                .Section("Compatibility", "These flags match gzip");

            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("Compatibility:", help);
            Assert.Contains("These flags match gzip", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_IncludesExitCodes()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("mytool", "1.0.0")
                .StandardFlags()
                .ExitCodes(
                    (0, "Success"),
                    (125, "Usage error"));

            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            Assert.Contains("Exit Codes:", help);
            Assert.Contains("0", help);
            Assert.Contains("Success", help);
            Assert.Contains("125", help);
            Assert.Contains("Usage error", help);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void GenerateHelp_StandardFlagsAppearLast()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var parser = new CommandLineParser("mytool", "1.0.0")
                .StandardFlags()
                .Flag("--verbose", "-v", "Verbose output");

            parser.Parse(new[] { "--help" });
            string help = writer.ToString();

            int verbosePos = help.IndexOf("--verbose");
            int helpPos = help.IndexOf("--help");
            Assert.True(verbosePos < helpPos, "--verbose should appear before --help in options list");
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }
}

public class WriteErrorTests
{
    [Fact]
    public void WriteError_PlainText_WritesToolPrefixedMessage()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .Flag("--verbose", null, "Verbose");

        var result = parser.Parse(Array.Empty<string>());

        var writer = new StringWriter();
        int exitCode = result.WriteError("no command specified", writer);

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("mytool: no command specified", writer.ToString());
    }

    [Fact]
    public void WriteError_JsonMode_WritesJsonError()
    {
        var parser = new CommandLineParser("mytool", "2.0.0")
            .StandardFlags();

        var result = parser.Parse(new[] { "--json" });

        var writer = new StringWriter();
        int exitCode = result.WriteError("no command specified", writer);

        Assert.Equal(ExitCode.UsageError, exitCode);
        string output = writer.ToString();
        Assert.Contains("\"tool\":\"mytool\"", output);
        Assert.Contains("\"exit_code\":125", output);
        Assert.Contains("\"exit_reason\":\"usage_error\"", output);
    }

    [Fact]
    public void WriteError_CustomUsageErrorCode_ReturnsCustomCode()
    {
        var parser = new CommandLineParser("squeeze", "1.0.0")
            .UsageErrorCode(2);

        var result = parser.Parse(Array.Empty<string>());

        var writer = new StringWriter();
        int exitCode = result.WriteError("bad args", writer);

        Assert.Equal(2, exitCode);
    }
}
