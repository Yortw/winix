using System.Text.Json;
using Xunit;
using Yort.ShellKit;

namespace Yort.ShellKit.Tests;

public class DescribeTests
{
    [Fact]
    public void GenerateDescribe_ContainsToolAndVersion()
    {
        var parser = new CommandLineParser("mytool", "1.2.3")
            .Description("A test tool")
            .StandardFlags();

        string json = parser.GenerateDescribe();

        Assert.Contains("\"tool\":\"mytool\"", json);
        Assert.Contains("\"version\":\"1.2.3\"", json);
        Assert.Contains("\"description\":\"A test tool\"", json);
    }

    [Fact]
    public void GenerateDescribe_IncludesOptions()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .Flag("--verbose", "-v", "Verbose output")
            .Option("--output", "-o", "FILE", "Output file");

        string json = parser.GenerateDescribe();

        Assert.Contains("\"long\":\"--verbose\"", json);
        Assert.Contains("\"short\":\"-v\"", json);
        Assert.Contains("\"type\":\"flag\"", json);
        Assert.Contains("\"long\":\"--output\"", json);
        Assert.Contains("\"short\":\"-o\"", json);
        Assert.Contains("\"placeholder\":\"FILE\"", json);
        Assert.Contains("\"type\":\"string\"", json);
    }

    [Fact]
    public void GenerateDescribe_IncludesExamples()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .Example("mytool --verbose file.txt", "Verbose processing");

        string json = parser.GenerateDescribe();

        Assert.Contains("\"examples\":", json);
        Assert.Contains("\"command\":\"mytool --verbose file.txt\"", json);
        Assert.Contains("\"description\":\"Verbose processing\"", json);
    }

    [Fact]
    public void GenerateDescribe_IncludesPlatform()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .Platform("cross-platform", new[] { "time", "Measure-Command" }, "Replaces Measure-Command", "Replaces time");

        string json = parser.GenerateDescribe();

        Assert.Contains("\"platform\":", json);
        Assert.Contains("\"scope\":\"cross-platform\"", json);
        Assert.Contains("\"replaces\":[\"time\",\"Measure-Command\"]", json);
        Assert.Contains("\"value_on_windows\":\"Replaces Measure-Command\"", json);
        Assert.Contains("\"value_on_unix\":\"Replaces time\"", json);
    }

    [Fact]
    public void GenerateDescribe_IncludesIoDescriptions()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .StdinDescription("File list, one per line")
            .StdoutDescription("Compressed output")
            .StderrDescription("Progress and summary");

        string json = parser.GenerateDescribe();

        Assert.Contains("\"io\":", json);
        Assert.Contains("\"stdin\":\"File list, one per line\"", json);
        Assert.Contains("\"stdout\":\"Compressed output\"", json);
        Assert.Contains("\"stderr\":\"Progress and summary\"", json);
    }

    [Fact]
    public void GenerateDescribe_IncludesComposesWith()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .ComposesWith("jq", "mytool --json | jq .field", "Extract a field");

        string json = parser.GenerateDescribe();

        Assert.Contains("\"composes_with\":", json);
        Assert.Contains("\"tool\":\"jq\"", json);
        Assert.Contains("\"pattern\":\"mytool --json | jq .field\"", json);
        Assert.Contains("\"description\":\"Extract a field\"", json);
    }

    [Fact]
    public void GenerateDescribe_IncludesJsonOutputFields()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .JsonField("elapsed_ms", "number", "Elapsed time in milliseconds");

        string json = parser.GenerateDescribe();

        Assert.Contains("\"json_output_fields\":", json);
        Assert.Contains("\"name\":\"elapsed_ms\"", json);
        Assert.Contains("\"type\":\"number\"", json);
        Assert.Contains("\"description\":\"Elapsed time in milliseconds\"", json);
    }

    [Fact]
    public void GenerateDescribe_IncludesExitCodes()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .ExitCodes(
                (0, "Success"),
                (125, "Usage error"));

        string json = parser.GenerateDescribe();

        Assert.Contains("\"exit_codes\":", json);
        Assert.Contains("\"code\":0", json);
        Assert.Contains("\"description\":\"Success\"", json);
        Assert.Contains("\"code\":125", json);
        Assert.Contains("\"description\":\"Usage error\"", json);
    }

    [Fact]
    public void GenerateDescribe_OmitsEmptySections()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags();

        string json = parser.GenerateDescribe();

        // No examples, composability, json fields, io, exit codes, or platform registered
        Assert.DoesNotContain("\"examples\":", json);
        Assert.DoesNotContain("\"composes_with\":", json);
        Assert.DoesNotContain("\"json_output_fields\":", json);
        Assert.DoesNotContain("\"io\":", json);
        Assert.DoesNotContain("\"exit_codes\":", json);
        Assert.DoesNotContain("\"platform\":", json);
    }

    [Fact]
    public void GenerateDescribe_OutputIsValidJson()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .Description("A test tool")
            .StandardFlags()
            .Flag("--verbose", "-v", "Verbose output")
            .Option("--output", "-o", "FILE", "Output file")
            .IntOption("--count", "-c", "N", "Count")
            .DoubleOption("--interval", "-n", "SECS", "Interval")
            .ListOption("--watch", "-w", "GLOB", "Watch pattern")
            .StdinDescription("Input data")
            .StdoutDescription("Output data")
            .StderrDescription("Errors")
            .Example("mytool file.txt", "Process a file")
            .ComposesWith("jq", "mytool --json | jq .", "Parse JSON output")
            .JsonField("elapsed_ms", "number", "Time in ms")
            .Platform("cross-platform", new[] { "cat" }, "Works on Windows", "Works on Unix")
            .ExitCodes((0, "Success"), (1, "Failure"));

        string json = parser.GenerateDescribe();

        // Should not throw — proves the output is valid JSON
        JsonDocument doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
    }

    [Fact]
    public void GenerateDescribe_ListOptionIsRepeatable()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .ListOption("--watch", "-w", "GLOB", "Watch pattern");

        string json = parser.GenerateDescribe();

        // Find the --watch option entry and verify it has repeatable:true
        Assert.Contains("\"long\":\"--watch\"", json);
        Assert.Contains("\"repeatable\":true", json);
    }

    [Fact]
    public void GenerateDescribe_IncludesUsageLine()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .CommandMode();

        string json = parser.GenerateDescribe();

        Assert.Contains("\"usage\":\"mytool [options] [--] <command> [args...]\"", json);
    }

    [Fact]
    public void GenerateDescribe_EscapesSpecialCharacters()
    {
        var parser = new CommandLineParser("mytool", "1.0.0")
            .StandardFlags()
            .Description("A tool with \"quotes\" and \\backslashes");

        string json = parser.GenerateDescribe();

        // Should be valid JSON despite special characters
        JsonDocument doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
        Assert.Contains("\\\"quotes\\\"", json);
        Assert.Contains("\\\\backslashes", json);
    }
}

[Collection("ConsoleOutput")]
public class DescribeParseTests
{
    [Fact]
    public void Parse_DescribeFlag_SetsIsHandled()
    {
        var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var parser = new CommandLineParser("mytool", "1.0.0")
                .Description("Test tool")
                .StandardFlags();

            ParseResult result = parser.Parse(new[] { "--describe" });

            Assert.True(result.IsHandled);
            Assert.Equal(0, result.ExitCode);

            string output = sw.ToString();
            Assert.Contains("\"tool\":\"mytool\"", output);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }
}
