using Xunit;
using Winix.Peep;

namespace Winix.Peep.Tests;

public class FormatJsonTests
{
    [Fact]
    public void FormatJson_IncludesStandardFields()
    {
        string json = Formatting.FormatJson(
            exitCode: 0,
            exitReason: "exit_on_success",
            runs: 12,
            lastChildExitCode: 0,
            durationSeconds: 24.5,
            command: "dotnet build",
            lastOutput: null,
            toolName: "peep",
            version: "0.1.0");

        Assert.Contains("\"tool\":\"peep\"", json);
        Assert.Contains("\"version\":\"0.1.0\"", json);
        Assert.Contains("\"exit_code\":0", json);
        Assert.Contains("\"exit_reason\":\"exit_on_success\"", json);
    }

    [Fact]
    public void FormatJson_IncludesPeepSpecificFields()
    {
        string json = Formatting.FormatJson(
            exitCode: 0,
            exitReason: "manual",
            runs: 5,
            lastChildExitCode: 1,
            durationSeconds: 10.123,
            command: "dotnet test",
            lastOutput: null,
            toolName: "peep",
            version: "0.1.0");

        Assert.Contains("\"runs\":5", json);
        Assert.Contains("\"last_child_exit_code\":1", json);
        Assert.Contains("\"duration_seconds\":10.123", json);
        Assert.Contains("\"command\":\"dotnet test\"", json);
    }

    [Fact]
    public void FormatJson_NullLastChildExitCode_OutputsNull()
    {
        string json = Formatting.FormatJson(
            exitCode: 125,
            exitReason: "usage_error",
            runs: 0,
            lastChildExitCode: null,
            durationSeconds: 0.0,
            command: "dotnet build",
            lastOutput: null,
            toolName: "peep",
            version: "0.1.0");

        Assert.Contains("\"last_child_exit_code\":null", json);
    }

    [Fact]
    public void FormatJson_WithLastOutput_IncludesStrippedText()
    {
        string json = Formatting.FormatJson(
            exitCode: 0,
            exitReason: "exit_on_success",
            runs: 1,
            lastChildExitCode: 0,
            durationSeconds: 1.0,
            command: "echo hello",
            lastOutput: "\x1b[32mBuild succeeded.\x1b[0m\n",
            toolName: "peep",
            version: "0.1.0");

        Assert.Contains("\"last_output\":\"Build succeeded.\\n\"", json);
        // ANSI sequences should be stripped
        Assert.DoesNotContain("\x1b[32m", json);
        Assert.DoesNotContain("\x1b[0m", json);
    }

    [Fact]
    public void FormatJson_WithoutLastOutput_OmitsField()
    {
        string json = Formatting.FormatJson(
            exitCode: 0,
            exitReason: "manual",
            runs: 1,
            lastChildExitCode: 0,
            durationSeconds: 1.0,
            command: "echo hello",
            lastOutput: null,
            toolName: "peep",
            version: "0.1.0");

        Assert.DoesNotContain("last_output", json);
    }

    [Fact]
    public void FormatJson_CommandWithSpecialChars_Escaped()
    {
        string json = Formatting.FormatJson(
            exitCode: 0,
            exitReason: "manual",
            runs: 1,
            lastChildExitCode: 0,
            durationSeconds: 1.0,
            command: "echo \"hello world\"",
            lastOutput: null,
            toolName: "peep",
            version: "0.1.0");

        // Quotes should be escaped
        Assert.Contains("\\\"hello world\\\"", json);
    }

    [Fact]
    public void FormatJson_WithHistoryRetained_IncludesField()
    {
        string json = Formatting.FormatJson(
            exitCode: 0,
            exitReason: "manual",
            runs: 50,
            lastChildExitCode: 0,
            durationSeconds: 100.0,
            command: "dotnet test",
            lastOutput: null,
            toolName: "peep",
            version: "0.1.0",
            historyRetained: 50);

        Assert.Contains("\"history_retained\":50", json);
    }

    [Fact]
    public void FormatJson_NullHistoryRetained_OmitsField()
    {
        string json = Formatting.FormatJson(
            exitCode: 0,
            exitReason: "manual",
            runs: 5,
            lastChildExitCode: 0,
            durationSeconds: 10.0,
            command: "dotnet test",
            lastOutput: null,
            toolName: "peep",
            version: "0.1.0");

        Assert.DoesNotContain("history_retained", json);
    }
}

public class FormatJsonErrorTests
{
    [Fact]
    public void FormatJsonError_IncludesStandardFields()
    {
        string json = Formatting.FormatJsonError(125, "usage_error", "peep", "0.1.0");

        Assert.Contains("\"tool\":\"peep\"", json);
        Assert.Contains("\"version\":\"0.1.0\"", json);
        Assert.Contains("\"exit_code\":125", json);
        Assert.Contains("\"exit_reason\":\"usage_error\"", json);
    }

    [Fact]
    public void FormatJsonError_CommandNotFound()
    {
        string json = Formatting.FormatJsonError(127, "command_not_found", "peep", "0.1.0");

        Assert.Contains("\"exit_code\":127", json);
        Assert.Contains("\"exit_reason\":\"command_not_found\"", json);
    }

    [Fact]
    public void FormatJsonError_CommandNotExecutable()
    {
        string json = Formatting.FormatJsonError(126, "command_not_executable", "peep", "0.1.0");

        Assert.Contains("\"exit_code\":126", json);
        Assert.Contains("\"exit_reason\":\"command_not_executable\"", json);
    }
}

public class StripAnsiTests
{
    [Fact]
    public void StripAnsi_RemovesColourCodes()
    {
        string input = "\x1b[32mgreen\x1b[0m normal \x1b[31mred\x1b[0m";

        string result = Formatting.StripAnsi(input);

        Assert.Equal("green normal red", result);
    }

    [Fact]
    public void StripAnsi_RemovesDimAndBold()
    {
        string input = "\x1b[2mdim\x1b[0m \x1b[1mbold\x1b[0m";

        string result = Formatting.StripAnsi(input);

        Assert.Equal("dim bold", result);
    }

    [Fact]
    public void StripAnsi_PreservesPlainText()
    {
        string input = "no ANSI here, just plain text";

        string result = Formatting.StripAnsi(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void StripAnsi_EmptyString_ReturnsEmpty()
    {
        string result = Formatting.StripAnsi("");

        Assert.Equal("", result);
    }

    [Fact]
    public void StripAnsi_NullString_ReturnsNull()
    {
        string result = Formatting.StripAnsi(null!);

        Assert.Null(result);
    }

    [Fact]
    public void StripAnsi_ComplexSgrSequences()
    {
        // Multi-parameter SGR: bold + red (1;31)
        string input = "\x1b[1;31mERROR\x1b[0m: something went wrong";

        string result = Formatting.StripAnsi(input);

        Assert.Equal("ERROR: something went wrong", result);
    }
}
